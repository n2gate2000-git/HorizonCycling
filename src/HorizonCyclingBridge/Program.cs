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
                // PIDゲイン調整値: Kp=0.6, Ki=0.15, Kd=0.05 (ゲームと自転車の追従遅延のバランス)
                strategy = new SimulationMappingStrategy(kp: 0.6f, ki: 0.15f, kd: 0.05f);
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

            // スマートローラーへの接続（タイムアウト20秒）
            bool isTrainerConnected = await trainerClient.ScanAndConnectAsync(timeoutMs: 20000);
            if (!isTrainerConnected)
            {
                Console.WriteLine("[WARNING] Could not connect to smart trainer via BLE.");
                Console.WriteLine("          Pedal power input will fall back to 0W. (Use Keyboard or Keyboard-emulated power for tests)");
            }

            // C. Forza UDP テレメトリ受信サーバーの初期化 (ポート 5000)
            int port = 5000;
            var receiver = new ForzaUdpReceiver(port);

            // 3. テレメトリパケット受信時の連動ロジック (双方向ループ)
            receiver.OnPacketReceived += (packet) =>
            {
                // A. ペダリングパワー(W)を、選択されたStrategyでアクセル/ブレーキ値(0.0〜1.0)に変換
                ControlOutput control = strategy.CalculateOutput(_currentPower, packet);

                // B. 仮想コントローラー(vJoy)へ入力を送信
                if (isVJoyReady)
                {
                    vJoyController.SendInputs(control.Throttle, control.Brake);
                }

                // ★1秒に1回、Forzaの生テレメトリデータを調査用デバッグログとして改行出力
                uint currentTimeMS = packet.TimestampMS;
                if (currentTimeMS - _lastDebugTimeMS >= 1000 || _lastDebugTimeMS == 0)
                {
                    Console.WriteLine($"\n[DEBUG-TELEMETRY] Time: {currentTimeMS} | RawPitch: {packet.Pitch:F4} rad | Accel: X:{packet.AccelerationX:F2}, Y:{packet.AccelerationY:F2}, Z:{packet.AccelerationZ:F2} | Speed: {packet.SpeedKmh:F1} km/h");
                    _lastDebugTimeMS = currentTimeMS;
                }

                // C. ゲーム内のPitch(ラジアン)から斜度%を求め、スマートローラーへ負荷指示をフィードバック
                // ★極めて重要な座標系修正：Forzaのピッチ角は「車首上げ（上り）の時にマイナス（負）」を指す右手系仕様になっています。
                // そのため、ゲームのPitchの符号を反転（マイナスを掛ける）して、正しく「上りがプラス％、下りがマイナス％」になるようにします。
                double rawGradePercent = -Math.Tan(packet.Pitch) * 100.0;
                
                // ★サスペンション姿勢変化の相殺補正（平地負荷高の解消）
                // 1. 車が走っている時 (Speed > 3.0 km/h) は、駆動トルクや空気抵抗による車首の浮き上がり分（約 -0.9%）を定常引き算して補正します。
                // 2. 加減速による車体の前後の沈み込み (AccelerationZ) を打ち消すため、加速度に比例した値 (AccelerationZ * 0.12) を引き算して相殺します。
                // これにより「ノイズや姿勢変化を取り除いた、純粋な道路勾配（trueRoadGrade）」を求めます。
                double trueRoadGrade = rawGradePercent - (packet.AccelerationZ * 0.12) - 0.9;
                
                // ★負荷再現割合（Trainer Difficulty）の適用
                // 真の道路勾配に対して難易度割合を掛けます。これにより平地（trueRoadGrade ≒ 0%）の時は難易度によらず常に0%負荷になります。
                double difficultyGrade = trueRoadGrade * _trainerDifficulty;

                double correctedGrade = difficultyGrade;
                if (packet.SpeedKmh <= 3.0f)
                {
                    // 停車中（または極低速）は強制的に0%近辺にします
                    correctedGrade = 0.0;
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

                        _lastSentGrade = targetIncline;
                        _lastSentTimeMS = currentTimeMS;

                        // ★スマートローラーへの実際の送信コマンド値と、その時の生データを改行出力
                        Console.WriteLine($"\n[DEBUG-BLE-SEND] Time: {currentTimeMS} | Filtered: {_filteredGrade:F2}% | SentToTrainer: {targetIncline:F1}% (Trigger: {(isZeroReset ? "ZeroReset" : "Normal")})");

                        // 非同期でスマートローラーへ負荷指示を送信
                        _ = trainerClient.SetTargetInclinationAsync(targetIncline);
                    }
                }

                // D. 連動状況のリアルタイムコンソールデバッグ画面表示
                double currentSpeedKmh = packet.SpeedKmh;
                double targetSpeedKmh = (strategy is SimulationMappingStrategy sim) ? sim.TargetSpeedKmh : 0.0;

                string speedComparison = (strategy is SimulationMappingStrategy)
                    ? $"Target: {targetSpeedKmh:F1} km/h | Car: {currentSpeedKmh:F1} km/h"
                    : $"Direct Accel: {(control.Throttle * 100.0):F0}%";

                Console.Write($"\r[ACTIVE] {modeName} | Pedal: {_currentPower:F0} W | {speedComparison} | Grade: {_filteredGrade:F1}% (Diff: {(_trainerDifficulty * 100.0):F0}%) | Out -> Thr: {control.Throttle:F2}, Brk: {control.Brake:F2}        ");
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
                Console.WriteLine("  Forza や x360ce への入力アサイン補助:");
                Console.WriteLine("  - [T] キーを押す : アクセル（Throttle 100%）を 3秒間 送信します");
                Console.WriteLine("  - [B] キーを押す : ブレーキ（Brake 100%）を 3秒間 送信します");
                Console.WriteLine("  終了:");
                Console.WriteLine("  - [Q] または [Esc] キーを押す: アプリケーションを安全に終了します");
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
                        }
                        else if (keyChar == '+' || keyChar == '=')
                        {
                            _trainerDifficulty = Math.Clamp(_trainerDifficulty + 0.1, 0.0, 1.0);
                            Console.WriteLine($"\n[DIFFICULTY] Trainer Difficulty increased to: {(_trainerDifficulty * 100.0):F0}%");
                        }
                        else if (key == ConsoleKey.T)
                        {
                            Console.WriteLine("\n[TEST] Sending THROTTLE 100% for 3 seconds...");
                            if (isVJoyReady) vJoyController.SendInputs(1.0f, 0.0f);
                            await Task.Delay(3000);
                            if (isVJoyReady) vJoyController.SendInputs(0.0f, 0.0f);
                            Console.WriteLine("[TEST] Throttle output stopped. Restored to telemetry control.");
                        }
                        else if (key == ConsoleKey.B)
                        {
                            Console.WriteLine("\n[TEST] Sending BRAKE 100% for 3 seconds...");
                            if (isVJoyReady) vJoyController.SendInputs(0.0f, 1.0f);
                            await Task.Delay(3000);
                            if (isVJoyReady) vJoyController.SendInputs(0.0f, 0.0f);
                            Console.WriteLine("[TEST] Brake output stopped. Restored to telemetry control.");
                        }
                        else if (key == ConsoleKey.Q || key == ConsoleKey.Escape)
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
