using System;
using System.Collections.Generic;
using System.Linq;
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

            // 0. 引数解析とコンフィグのロード
            bool setupMode = args.Contains("--setup-sensors");
            AppConfig? config = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--power" && i + 1 < args.Length)
                {
                    string macStr = args[i + 1].Replace(":", "");
                    if (ulong.TryParse(macStr, System.Globalization.NumberStyles.HexNumber, null, out ulong mac))
                    {
                        config = new AppConfig { PowerSourceType = SensorType.CyclingPower, PowerSourceMacAddress = mac };
                        Console.WriteLine($"[INFO] CLI override: Power Meter MAC {mac:X}");
                    }
                }
            }

            if (config == null && !setupMode)
            {
                config = ConfigManager.Load();
                if (config.PowerSourceType == SensorType.None)
                {
                    setupMode = true;
                }
                else
                {
                    Console.WriteLine($"[INFO] Loaded config: {config.PowerSourceType} ({config.PowerSourceMacAddress:X})");
                }
            }

            if (setupMode)
            {
                config = await RunSetupSensorsAsync();
            }

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
                // PIDゲイン調整値: Kp=1.0, Ki=0.2, Kd=0.05
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
            }

            // B. BLE デバイスの接続
            FtmsClient? ftmsClient = null;
            CyclingPowerClient? cpClient = null;
            bool isBleConnected = false;

            if (config != null && config.PowerSourceType == SensorType.Ftms)
            {
                ftmsClient = new FtmsClient();
                ftmsClient.OnStatusMessage += msg => Console.WriteLine(msg);
                ftmsClient.OnPowerReceived += power => _currentPower = power;
                ftmsClient.OnSpeedReceived += speed => _trainerSpeedKmh = speed;

                isBleConnected = await ftmsClient.ScanAndConnectAsync(20000, config.PowerSourceMacAddress);
                if (isBleConnected)
                {
                    await ftmsClient.SetTargetResistanceLevelAsync(0);
                    _lastSentGrade = 0.0;
                    Console.WriteLine("[BLE] Smart trainer resistance initialized to FREE (Level 0).");
                }
            }
            else if (config != null && config.PowerSourceType == SensorType.CyclingPower)
            {
                cpClient = new CyclingPowerClient();
                cpClient.OnStatusMessage += msg => Console.WriteLine(msg);
                cpClient.OnPowerReceived += power => _currentPower = power;

                isBleConnected = await cpClient.ScanAndConnectAsync(20000, config.PowerSourceMacAddress);
            }

            if (!isBleConnected)
            {
                Console.WriteLine("[WARNING] Could not connect to BLE device.");
                Console.WriteLine("          Pedal power input will fall back to 0W.");
            }

            // C. Forza UDP テレメトリ受信サーバーの初期化
            int port = 5000;
            var receiver = new ForzaUdpReceiver(port);

            // 3. テレメトリパケット受信時の連動ロジック
            receiver.OnPacketReceived += (packet) =>
            {
                double rawGradePercent = -Math.Tan(packet.Pitch) * 100.0;
                
                double trueRoadGrade = rawGradePercent;
                if (packet.SpeedKmh > 3.0f)
                {
                    trueRoadGrade = rawGradePercent - (packet.AccelerationZ * 0.12) - 0.9;
                }
                else
                {
                    trueRoadGrade = 0.0;
                }

                double difficultyGrade = trueRoadGrade * _trainerDifficulty;
                if (trueRoadGrade < 0.0)
                {
                    difficultyGrade = trueRoadGrade * (_trainerDifficulty * 0.5);
                }

                double correctedGrade = difficultyGrade;
                if (packet.SpeedKmh <= 3.0f)
                {
                    correctedGrade = 0.0;
                }

                if (strategy is SimulationMappingStrategy simStrategy)
                {
                    simStrategy.TrainerSpeedKmh = _trainerSpeedKmh;
                    simStrategy.RoadGradePercent = difficultyGrade; 
                    simStrategy.TrueRoadGradePercent = trueRoadGrade; 
                }

                ControlOutput control = strategy.CalculateOutput(_currentPower, packet);

                if (isVJoyReady && !_isTestingThrottle)
                {
                    vJoyController.SendInputs(control.Throttle);
                }

                uint currentTimeMS = packet.TimestampMS;
                if (currentTimeMS - _lastDebugTimeMS >= 1000 || _lastDebugTimeMS == 0)
                {
                    Console.WriteLine($"\n[DEBUG-TELEMETRY] Time: {currentTimeMS} | RawPitch: {packet.Pitch:F4} rad | Accel: X:{packet.AccelerationX:F2}, Y:{packet.AccelerationY:F2}, Z:{packet.AccelerationZ:F2} | Speed: {packet.SpeedKmh:F1} km/h");
                    
                    double currentSpeedKmh = packet.SpeedKmh;
                    double targetSpeedKmh = (strategy is SimulationMappingStrategy sim) ? sim.TargetSpeedKmh : 0.0;

                    string speedComparison = (strategy is SimulationMappingStrategy)
                        ? $"Target: {targetSpeedKmh:F1} km/h | Car: {currentSpeedKmh:F1} km/h"
                        : $"Direct Accel: {(control.Throttle * 100.0):F0}%";

                    Console.Write($"\r[ACTIVE] {modeName} | Pedal: {_currentPower:F0} W | {speedComparison} | Grade: {_filteredGrade:F1}% (Diff: {(_trainerDifficulty * 100.0):F0}%) | Out -> Thr: {control.Throttle:F2}, Brk: {control.Brake:F2}        ");

                    _lastDebugTimeMS = currentTimeMS;
                }
                
                _filteredGrade = (_filteredGrade * (1.0 - EMA_ALPHA)) + (correctedGrade * EMA_ALPHA);

                // スマートローラーが接続されている場合のみ、物理抵抗をフィードバックする
                if (isBleConnected && packet.IsRaceOn && ftmsClient != null && ftmsClient.IsConnected)
                {
                    long timeDiff = (long)currentTimeMS - (long)_lastSentTimeMS;
                    double gradeDiff = Math.Abs(_filteredGrade - _lastSentGrade);

                    bool isZeroReset = Math.Abs(_filteredGrade) < 0.3 && _lastSentGrade != 0.0 && _lastSentGrade != 999.0;
                    bool isSignificantChange = timeDiff >= 1500 && gradeDiff >= 0.8;

                    if (_lastSentGrade == 999.0 || isSignificantChange || isZeroReset)
                    {
                        double targetIncline = _filteredGrade;

                        if (isZeroReset)
                        {
                            targetIncline = 0.0;
                        }
                        else
                        {
                            if (_lastSentGrade != 999.0)
                            {
                                double maxStep = 2.0;
                                double step = Math.Clamp(_filteredGrade - _lastSentGrade, -maxStep, maxStep);
                                targetIncline = _lastSentGrade + step;
                            }
                        }

                        targetIncline = Math.Round(targetIncline, 1);

                        if (strategy is SimulationMappingStrategy simStrat)
                        {
                            double targetSpd = simStrat.TargetSpeedKmh;
                            double carSpd = packet.SpeedKmh;
                            
                            if (targetSpd > 10.0 && carSpd < targetSpd * 0.95 && targetIncline > 0.0)
                            {
                                double deficit = 1.0 - (carSpd / targetSpd);
                                double gearMultiplier = Math.Max(0.0, 1.0 - (deficit * 4.0)); 
                                targetIncline = targetIncline * gearMultiplier;
                            }
                            
                            double maxIncline = 15.0 * _trainerDifficulty;
                            if (targetIncline > maxIncline)
                            {
                                targetIncline = maxIncline;
                            }
                        }

                        _lastSentGrade = targetIncline;
                        _lastSentTimeMS = currentTimeMS;

                        if (targetIncline <= 0.0)
                        {
                            Console.WriteLine($"\n[DEBUG-BLE-SEND] Time: {currentTimeMS} | Filtered: {_filteredGrade:F2}% | SentToTrainer: FREE (OpCode 0x04, Level 0) [Descent/Flat]");
                            _ = ftmsClient.SetTargetResistanceLevelAsync(0);
                        }
                        else
                        {
                            Console.WriteLine($"\n[DEBUG-BLE-SEND] Time: {currentTimeMS} | Filtered: {_filteredGrade:F2}% | SentToTrainer: {targetIncline:F1}% (Trigger: {(isZeroReset ? "ZeroReset" : "Normal")})");
                            _ = ftmsClient.SetIndoorBikeSimulationParametersAsync(targetIncline);
                        }
                    }
                }

            };

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
                            _lastSentGrade = 999.0; 
                            
                            if (_trainerDifficulty <= 0.001 && ftmsClient != null && ftmsClient.IsConnected)
                            {
                                _ = ftmsClient.SetTargetResistanceLevelAsync(0);
                                _lastSentGrade = 0.0;
                            }
                        }
                        else if (keyChar == '+' || keyChar == '=')
                        {
                            _trainerDifficulty = Math.Clamp(_trainerDifficulty + 0.1, 0.0, 1.0);
                            Console.WriteLine($"\n[DIFFICULTY] Trainer Difficulty increased to: {(_trainerDifficulty * 100.0):F0}%");
                            _lastSentGrade = 999.0; 
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
                            _lastSentGrade = 999.0; 
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
                ftmsClient?.Disconnect();
                cpClient?.Disconnect();
                Console.WriteLine("\n[BRIDGE] Sessions ended. Bluetooth and UDP connections successfully released.");
                Console.WriteLine("======================================================================");
            }
        }

        private static async Task<AppConfig> RunSetupSensorsAsync()
        {
            Console.WriteLine("\n[SETUP] Scanning for BLE devices (FTMS and Cycling Power)... (10 seconds)");
            var foundDevices = new List<(ulong Address, string Name, SensorType Type)>();
            
            // 1. Windowsにペアリング済み（またはシステムが記憶している）デバイスから検索
            try
            {
                var ftmsSelector = Windows.Devices.Bluetooth.GenericAttributeProfile.GattDeviceService.GetDeviceSelectorFromUuid(Guid.Parse("00001826-0000-1000-8000-00805f9b34fb"));
                var ftmsDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(ftmsSelector);
                foreach (var d in ftmsDevices)
                {
                    var bleDevice = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(d.Id);
                    if (bleDevice != null && !foundDevices.Any(x => x.Address == bleDevice.BluetoothAddress))
                    {
                        foundDevices.Add((bleDevice.BluetoothAddress, bleDevice.Name, SensorType.Ftms));
                        Console.WriteLine($"  [{foundDevices.Count}] {SensorType.Ftms}: {bleDevice.Name} ({bleDevice.BluetoothAddress:X}) [Paired]");
                    }
                }

                var powerSelector = Windows.Devices.Bluetooth.GenericAttributeProfile.GattDeviceService.GetDeviceSelectorFromUuid(Guid.Parse("00001818-0000-1000-8000-00805f9b34fb"));
                var powerDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(powerSelector);
                foreach (var d in powerDevices)
                {
                    var bleDevice = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(d.Id);
                    if (bleDevice != null && !foundDevices.Any(x => x.Address == bleDevice.BluetoothAddress))
                    {
                        foundDevices.Add((bleDevice.BluetoothAddress, bleDevice.Name, SensorType.CyclingPower));
                        Console.WriteLine($"  [{foundDevices.Count}] {SensorType.CyclingPower}: {bleDevice.Name} ({bleDevice.BluetoothAddress:X}) [Paired]");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SETUP] Paired device query failed: {ex.Message}");
            }

            // 2. ペアリングされていない新しいデバイスをアドバタイズメントから検索
            var watcher = new Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher();
            // FTMS
            watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(Guid.Parse("00001826-0000-1000-8000-00805f9b34fb"));
            // Cycling Power
            watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(Guid.Parse("00001818-0000-1000-8000-00805f9b34fb"));

            watcher.Received += (s, e) =>
            {
                lock (foundDevices)
                {
                    if (!foundDevices.Any(d => d.Address == e.BluetoothAddress))
                    {
                        string name = string.IsNullOrEmpty(e.Advertisement.LocalName) ? "Unknown" : e.Advertisement.LocalName;
                        SensorType type = e.Advertisement.ServiceUuids.Contains(Guid.Parse("00001826-0000-1000-8000-00805f9b34fb")) 
                                          ? SensorType.Ftms : SensorType.CyclingPower;
                        foundDevices.Add((e.BluetoothAddress, name, type));
                        Console.WriteLine($"  [{foundDevices.Count}] {type}: {name} ({e.BluetoothAddress:X}) [Advertising]");
                    }
                }
            };

            watcher.Start();
            await Task.Delay(10000);
            watcher.Stop();

            Console.WriteLine("\n[SETUP] Scan complete.");
            if (foundDevices.Count == 0)
            {
                Console.WriteLine("No devices found. Falling back to default (FTMS any).");
                return new AppConfig();
            }

            Console.Write("Select device for POWER (Enter number, or 0 to skip): ");
            string input = Console.ReadLine() ?? "0";
            if (int.TryParse(input, out int idx) && idx > 0 && idx <= foundDevices.Count)
            {
                var selected = foundDevices[idx - 1];
                var config = new AppConfig
                {
                    PowerSourceType = selected.Type,
                    PowerSourceMacAddress = selected.Address,
                    PowerSourceName = selected.Name
                };
                ConfigManager.Save(config);
                Console.WriteLine($"[SETUP] Configuration saved to config.json. Selected: {selected.Name} ({selected.Address:X})");
                return config;
            }

            Console.WriteLine("[SETUP] Setup skipped or invalid selection. Falling back to default.");
            return new AppConfig();
        }
    }
}
