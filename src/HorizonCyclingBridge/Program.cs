using System;
using System.Threading.Tasks;
using HorizonCyclingBridge.Core;
using HorizonCyclingBridge.Telemetry;
using HorizonCyclingBridge.Controller;
using HorizonCyclingBridge.Trainer;

namespace HorizonCyclingBridge
{
    class Program
    {
        private static double _currentPower = 0.0;
        private static double _filteredGrade = 0.0;
        private static double _trainerDifficulty = 0.5; // スマートローラー負荷再現割合 (0.0〜1.0)
        private static double _trainerSpeedKmh = 0.0;   // スマートローラーから送られる現在の物理速度 (km/h)
        private static bool _isTestingThrottle = false; // ★アクセル動作テスト中フラグ
        
        // スマートローラーへの送信データ履歴（間引き用）
        private static double _lastSentGrade = 999.0;
        private static uint _lastSentTimeMS = 0;
        
        // 調査用デバッグログの間隔制御用
        private static uint _lastDebugTimeMS = 0;
        
        // 移動平均（EMA）フィルタの平滑化係数
        // 0.15 から 0.03 に引き下げて、加減速によるノイズ（リアの沈み込み等）を強力にカットします
        private const double EMA_ALPHA = 0.03;

        static async Task Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("======================================================================");
            Console.WriteLine("        HorizonCyclingBridge: Smart Trainer & Forza 6 Dual-Bridge     ");
            Console.WriteLine("======================================================================");

            // 1. 動作モードの選択
            Console.WriteLine("\n[MODE SELECTION]");
            Console.WriteLine(" 1. Arcade Mode (Pedal Power -> Direct Throttle Mapping)");
            Console.WriteLine(" 2. Simulation Mode (Pedal Power + Pitch -> Speed Tracking via PID)");
            Console.Write(" Select mode (1 or 2, default is 2): ");
            string input = Console.ReadLine() ?? "2";
            
            IPowerMappingStrategy strategy;
            string modeName;

            if (input.Trim() == "1")
            {
                strategy = new ArcadeMappingStrategy(ftp: 200.0); // 基準FTP: 200W
                modeName = "ARCADE MODE";
            }
            else
            {
                // PIDゲイン調整値: Kp=1.0, Ki=0.2, Kd=0.05 (Vivio RX-Rなどの軽自動車向け、安定した追従と加速のバランス設定)
                strategy = new SimulationMappingStrategy(kp: 1.0f, ki: 0.2f, kd: 0.05f);
                modeName = "SIMULATION MODE";
            }

            Console.WriteLine($"\n[INFO] Selected Mode: {modeName}");

            // 1.5. 負荷再現割合 (Trainer Difficulty) の選択
            Console.WriteLine("\n[TRAINER DIFFICULTY SELECTION]");
            Console.Write(" Enter Trainer Difficulty (0% to 100%, default is 50%): ");
            string diffInput = Console.ReadLine() ?? "";
            if (string.IsNullOrWhiteSpace(diffInput))
            {
                _trainerDifficulty = 0.5;
            }
            else if (double.TryParse(diffInput, out double parsedDiff))
            {
                _trainerDifficulty = Math.Clamp(parsedDiff / 100.0, 0.0, 1.0);
            }
            else
            {
                _trainerDifficulty = 0.5;
            }
            Console.WriteLine($"[INFO] Trainer Difficulty set to: {(_trainerDifficulty * 100.0):F0}%");

            // 2. 各連携モジュールの初期化
            Console.WriteLine("\n[INITIALIZING CORE MODULES]");
            
            // A. vJoy 仮想コントローラーの初期化
            using var vJoyController = new VJoyVehicleController(1);
            bool isVJoyReady = vJoyController.Initialize();
            if (!isVJoyReady)
            {
                Console.WriteLine("[WARNING] vJoy failed to initialize. Controller output emulation is DISABLED.");
                Console.WriteLine("          (The bridge will run in dry-run mode without driving the in-game car)");
            }


            // B. BLEスマートローラートレーナーの接続と初期化
            using var trainerClient = new FtmsClient();
            
            // BLEログのコンソール出力アタッチ
            trainerClient.OnStatusMessage += (msg) => Console.WriteLine(msg);
            
            // パワー受信時のイベントハンドラ
            trainerClient.OnPowerReceived += (power) =>
            {
                _currentPower = power;
            };

            // 速度受信時のイベントハンドラ
            trainerClient.OnSpeedReceived += (speed) =>
            {
                _trainerSpeedKmh = speed;
            };

            // スマートローラーへの接続（タイムアウト20秒）
            bool isTrainerConnected = await trainerClient.ScanAndConnectAsync(timeoutMs: 20000);
            if (!isTrainerConnected)
            {
                Console.WriteLine("[WARNING] Could not connect to smart trainer via BLE.");
                Console.WriteLine("          Pedal power input will fall back to 0W. (Use Keyboard or Keyboard-emulated power for tests)");
            }
            else
            {
                // 接続成功直後に、スマートローラーの物理抵抗を「0 (完全スピンフリー)」に初期リセットします
                // これにより、斜度0%シミュレーション時の空気抵抗発生を防ぎ、完全に軽い状態でスタートできます
                await trainerClient.SetTargetResistanceLevelAsync(0);
                _lastSentGrade = 0.0;
                Console.WriteLine("[BLE] Smart trainer resistance initialized to FREE (Level 0).");
            }

            // C. Forza UDP テレメトリ受信サーバーの初期化 (ポート 5000)
            int port = 5000;
            var receiver = new ForzaUdpReceiver(port);

            // 3. テレメトリパケット受信時の連動ロジック (双方向ループ)
            receiver.OnPacketReceived += (packet) =>
            {
                // C. ゲーム内のPitch(ラジアン)から斜度%を求め、スマートローラーへ負荷指示をフィードバック
                // ★極めて重要な座標系修正：Forzaのピッチ角は「車首上げ（上り）の時にマイナス（負）」を指す右手系仕様になっています。
                // そのため、ゲームのPitchの符号を反転（マイナスを掛ける）して、正しく「上りがプラス％、下りがマイナス％」になるようにします。
                double rawGradePercent = -Math.Tan(packet.Pitch) * 100.0;
                
                // ★サスペンション姿勢変化の相殺補正（平地負荷高の解消）
                // 1. 車が走っている時 (Speed > 3.0 km/h) は、駆動トルクや空気抵抗による車首の浮き上がり分（約 -0.9%）を定常引き算して補正します。
                // 2. 加減速による車体の前後の沈み込み (AccelerationZ) を打ち消すため、加速度に比例した値 (AccelerationZ * 0.12) を引き算して相殺します。
                // これにより「ノイズや姿勢変化を取り除いた、純粋な道路勾配（trueRoadGrade）」を求めます。
                double trueRoadGrade = rawGradePercent;
                if (packet.SpeedKmh > 3.0f)
                {
                    trueRoadGrade = rawGradePercent - (packet.AccelerationZ * 0.12) - 0.9;
                }
                else
                {
                    trueRoadGrade = 0.0;
                }

                // ★負荷再現割合（Trainer Difficulty）の適用
                // 真の道路勾配に対して難易度割合を掛けます。これにより平地（trueRoadGrade ≒ 0%）の時は難易度によらず常に0%負荷になります。
                double difficultyGrade = trueRoadGrade * _trainerDifficulty;
                if (trueRoadGrade < 0.0)
                {
                    // 下り坂は負荷の抜けすぎを防ぐためさらに半分（50%減少）にマイルド化して過度な軟化を抑制します
                    difficultyGrade = trueRoadGrade * (_trainerDifficulty * 0.5);
                }

                double correctedGrade = difficultyGrade;
                if (packet.SpeedKmh <= 3.0f)
                {
                    // 停車中（または極低速）は強制的に0%近辺にします
                    correctedGrade = 0.0;
                }

                // A. ペダリングパワー(W)を、選択されたStrategyでアクセル/ブレーキ値(0.0〜1.0)に変換
                if (strategy is SimulationMappingStrategy simStrategy)
                {
                    simStrategy.TrainerSpeedKmh = _trainerSpeedKmh;
                    simStrategy.RoadGradePercent = correctedGrade; // ★難易度が適用された勾配を物理モデル（ペダルの重さ）へインプット
                    simStrategy.TrueRoadGradePercent = trueRoadGrade; // ★難易度無視の真の勾配（下り坂の重力加速計算用）
                }

                ControlOutput control = strategy.CalculateOutput(_currentPower, packet);

                // B. 仮想コントローラー(vJoy)へ入力を送信
                // テスト送信中 (_isTestingThrottle) は、テレメトリ受信による上書きを防止します
                if (isVJoyReady && !_isTestingThrottle)
                {
                    vJoyController.SendInputs(control.Throttle);
                }

                // ★1秒に1回、Forzaの生テレメトリデータを調査用デバッグログとして改行出力
                uint currentTimeMS = packet.TimestampMS;
                if (currentTimeMS - _lastDebugTimeMS >= 1000 || _lastDebugTimeMS == 0)
                {
                    Console.WriteLine($"\n[DEBUG-TELEMETRY] Time: {currentTimeMS} | RawPitch: {packet.Pitch:F4} rad | Accel: X:{packet.AccelerationX:F2}, Y:{packet.AccelerationY:F2}, Z:{packet.AccelerationZ:F2} | Speed: {packet.SpeedKmh:F1} km/h");
                    
                    // D. 連動状況のリアルタイムコンソールデバッグ画面表示
                    double currentSpeedKmh = packet.SpeedKmh;
                    double targetSpeedKmh = (strategy is SimulationMappingStrategy sim) ? sim.TargetSpeedKmh : 0.0;

                    string speedComparison = (strategy is SimulationMappingStrategy)
                        ? $"Target: {targetSpeedKmh:F1} km/h | Car: {currentSpeedKmh:F1} km/h"
                        : $"Direct Accel: {(control.Throttle * 100.0):F0}%";

                    Console.Write($"\r[ACTIVE] {modeName} | Pedal: {_currentPower:F0} W | {speedComparison} | Grade: {_filteredGrade:F1}% (Diff: {(_trainerDifficulty * 100.0):F0}%) | Out -> Thr: {control.Throttle:F2}, Brk: {control.Brake:F2}        ");

                    _lastDebugTimeMS = currentTimeMS;
                }
                
                // EMAフィルタを適用し、急激な段差やジャンプによる負荷変動をまろやかにする
                _filteredGrade = (_filteredGrade * (1.0 - EMA_ALPHA)) + (correctedGrade * EMA_ALPHA);

                if (isTrainerConnected && packet.IsRaceOn)
                {
                    // ★BLE送信の「不感帯（デッドバンド）＆ 長時間デバウンス ＆ ゼロ自動リセット」
                    long timeDiff = (long)currentTimeMS - (long)_lastSentTimeMS;
                    double gradeDiff = Math.Abs(_filteredGrade - _lastSentGrade);

                    // 1. 前回の送信から 1500ms (1.5秒) 以上経過し、かつ 0.8% 以上の明確な斜度変化があること (不感帯の適用)
                    // 2. または、平地に完全に戻りかけた時 (Math.Abs(FilteredGrade) < 0.3% かつ 前回の送信値が 0 でない) の強制ゼロリセット
                    // 3. または、初回送信であること
                    // (★バグ修正：_filteredGradeがマイナス[下り坂]の時に常にゼロリセットが暴発するのを防ぐため、絶対値 Math.Abs を取ります)
                    bool isZeroReset = Math.Abs(_filteredGrade) < 0.3 && _lastSentGrade != 0.0 && _lastSentGrade != 999.0;
                    bool isSignificantChange = timeDiff >= 1500 && gradeDiff >= 0.8;

                    if (_lastSentGrade == 999.0 || isSignificantChange || isZeroReset)
                    {
                        double targetIncline = _filteredGrade;

                        if (isZeroReset)
                        {
                            // 平地に戻った場合は完全に0%でリセット
                            targetIncline = 0.0;
                        }
                        else
                        {
                            // 1回あたりの最大変化率（スルーレート）を ±2.0% に制限し、自然な斜度変化にします
                            if (_lastSentGrade != 999.0)
                            {
                                double maxStep = 2.0;
                                double step = Math.Clamp(_filteredGrade - _lastSentGrade, -maxStep, maxStep);
                                targetIncline = _lastSentGrade + step;
                            }
                        }

                        // スマートローラーが過敏に反応するのを防ぐため、値を小数点第1位に丸めます (例: 1.13% -> 1.1%)
                        targetIncline = Math.Round(targetIncline, 1);

                        // ★仮想ギアダウン（車の限界スピードによる負荷軽減アシスト）
                        // 目標速度(Target)に対して実際の車速(Car)が追いつかずアクセル全開になっている場合、
                        // Peel P50のような非力な車が性能限界に達しているため、送信斜度を下げてペダルを自動で軽く（ローギア化）します。
                        // これにより「必死に漕いでも車が進まず重いだけ」というフラストレーションを解消し、車速に見合った適正な軽さを提供します。
                        if (strategy is SimulationMappingStrategy simStrat)
                        {
                            double targetSpd = simStrat.TargetSpeedKmh;
                            double carSpd = packet.SpeedKmh;
                            
                            // 1. 仮想ギアダウン（車の限界スピードによるローギア化アシスト）
                            if (targetSpd > 10.0 && carSpd < targetSpd * 0.95 && targetIncline > 0.0)
                            {
                                // 目標速度に追いついていない度合い（不足率）
                                double deficit = 1.0 - (carSpd / targetSpd);
                                // 少しの不足でもペダルを一気に軽く（斜度をゼロに近く）する強力なローギア化
                                double gearMultiplier = Math.Max(0.0, 1.0 - (deficit * 4.0)); 
                                targetIncline = targetIncline * gearMultiplier;
                            }
                            
                            // 2. スマートローラー自体の「ベースの重さ」対策としての絶対上限リミッター
                            // 難易度10%などの場合、元の坂がどれだけ激坂であっても、送信斜度の上限を低く抑える
                            // 例: 難易度10%なら最大1.5%までに制限
                            double maxIncline = 15.0 * _trainerDifficulty;
                            if (targetIncline > maxIncline)
                            {
                                targetIncline = maxIncline;
                            }
                        }

                        _lastSentGrade = targetIncline;
                        _lastSentTimeMS = currentTimeMS;

                        // ★スマートローラーへの実際の送信コマンド
                        if (targetIncline <= 0.0)
                        {
                            // 平地、および下り坂（targetIncline <= 0.0%）の場合は、
                            // 斜度0%シミュレーション（速度依存の空気抵抗が高速回転時に自動発生してしまう）を完全にシャットダウンし、
                            // 物理抵抗レベル自体を強制的に「0 (完全スピンフリー)」にして負荷を完全に解放します。
                            // これにより、下り坂で高速回転した際に発生する激重な空気抵抗を完璧に防ぎ、スカスカで軽い滑走感を提供します。
                            Console.WriteLine($"\n[DEBUG-BLE-SEND] Time: {currentTimeMS} | Filtered: {_filteredGrade:F2}% | SentToTrainer: FREE (OpCode 0x04, Level 0) [Descent/Flat]");
                            _ = trainerClient.SetTargetResistanceLevelAsync(0);
                        }
                        else
                        {
                            // シミュレーションモードかつ上り坂（targetIncline > 0.0%）の場合は、斜度シミュレーションを実行します
                            Console.WriteLine($"\n[DEBUG-BLE-SEND] Time: {currentTimeMS} | Filtered: {_filteredGrade:F2}% | SentToTrainer: {targetIncline:F1}% (Trigger: {(isZeroReset ? "ZeroReset" : "Normal")})");
                            _ = trainerClient.SetIndoorBikeSimulationParametersAsync(targetIncline);
                        }
                    }
                }

            };

            // 受信エラー時のログ出力
            receiver.OnError += (ex) =>
            {
                Console.WriteLine($"\n[ERROR] Telemetry receiver encountered error: {ex.Message}");
            };

            // 4. システムの実行稼働
            try
            {
                receiver.Start();
                Console.WriteLine("\n[BRIDGE] Middle-ware bridge is now fully ACTIVE. Have a nice virtual ride!");
                Console.WriteLine("======================================================================");
                Console.WriteLine("[CONTROLLER & KEYBOARD INSTRUCTIONS]");
                Console.WriteLine("  - [-] キーを押す : スマートローラーの負荷再現割合を 10% 下げます");
                Console.WriteLine("  - [+] キーを押す : スマートローラーの負荷再現割合を 10% 上げます");
                Console.WriteLine("  - [M] キーを押す : シミュレーションとアーケードの動作モードを切り替えます");
                Console.WriteLine("  Forza や x360ce への入力アサイン補助:");
                Console.WriteLine("  - [T] キーを押す : アクセル（Throttle 100%）を 3秒間 送信します");
                Console.WriteLine("  終了:");
                Console.WriteLine("  - [Q] キーを押す: アプリケーションを安全に終了します");
                Console.WriteLine("======================================================================");
                
                bool running = true;
                while (running)
                {
                    if (Console.KeyAvailable)
                    {
                        var keyInfo = Console.ReadKey(intercept: true);
                        var key = keyInfo.Key;
                        char keyChar = keyInfo.KeyChar;

                        if (keyChar == '-' || keyChar == '_')
                        {
                            _trainerDifficulty = Math.Clamp(_trainerDifficulty - 0.1, 0.0, 1.0);
                            Console.WriteLine($"\n[DIFFICULTY] Trainer Difficulty decreased to: {(_trainerDifficulty * 100.0):F0}%");
                            _lastSentGrade = 999.0; // 送信履歴をリセットして即座の再計算・送信を強制
                            
                            // もし難易度が0%に達した場合は、テレメトリ受信を待つことなく即座にスマートローラーの抵抗を完全解放（フリー）にします
                            if (_trainerDifficulty <= 0.001 && isTrainerConnected)
                            {
                                _ = trainerClient.SetTargetResistanceLevelAsync(0);
                                _lastSentGrade = 0.0;
                            }
                        }
                        else if (keyChar == '+' || keyChar == '=')
                        {
                            _trainerDifficulty = Math.Clamp(_trainerDifficulty + 0.1, 0.0, 1.0);
                            Console.WriteLine($"\n[DIFFICULTY] Trainer Difficulty increased to: {(_trainerDifficulty * 100.0):F0}%");
                            _lastSentGrade = 999.0; // 送信履歴をリセットして即座の再計算・送信を強制
                        }
                        else if (key == ConsoleKey.T)
                        {
                            Console.WriteLine("\n[TEST] Sending THROTTLE 100% for 3 seconds...");
                            _isTestingThrottle = true;
                            if (isVJoyReady) vJoyController.SendInputs(1.0f);
                            await Task.Delay(3000);
                            if (isVJoyReady) vJoyController.SendInputs(0.0f);
                            _isTestingThrottle = false;
                            Console.WriteLine("[TEST] Throttle output stopped. Restored to telemetry control.");
                        }
                        else if (key == ConsoleKey.M)
                        {
                            if (strategy is SimulationMappingStrategy)
                            {
                                strategy = new ArcadeMappingStrategy(ftp: 200.0);
                                modeName = "ARCADE MODE";
                                Console.WriteLine($"\n[MODE] Switched to: {modeName}");
                            }
                            else
                            {
                                strategy = new SimulationMappingStrategy(kp: 1.0f, ki: 0.2f, kd: 0.05f);
                                modeName = "SIMULATION MODE";
                                Console.WriteLine($"\n[MODE] Switched to: {modeName}");
                            }
                            _lastSentGrade = 999.0; // 斜度送信履歴をリセットして即座の再計算・送信を強制
                        }
                        else if (key == ConsoleKey.Q)
                        {
                            running = false;
                        }
                    }
                    await Task.Delay(100);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FATAL] Application crash: {ex.Message}");
            }
            finally
            {
                receiver.Stop();
                trainerClient.Disconnect();
                Console.WriteLine("\n[BRIDGE] Sessions ended. Bluetooth and UDP connections successfully released.");
                Console.WriteLine("======================================================================");
            }
        }
    }
}
