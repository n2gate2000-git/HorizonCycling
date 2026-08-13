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
        private static bool _isTestingBrake = false;    // ★ブレーキ動作テスト中フラグ
        
        // スマートローラーへの送信データ履歴（間引き用）
        private static double _lastSentGrade = 999.0;
        private static uint _lastSentTimeMS = 0;
        
        // 移動平均（EMA）フィルタの平滑化係数
        private const double EMA_ALPHA = 0.03;

        // 固定画面表示ダッシュボード管理インスタンス
        private static readonly ConsoleDashboard _dashboard = new ConsoleDashboard();

        static async Task Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("======================================================================");
            Console.WriteLine("     HorizonCyclingBridge v0.3: Smart Trainer & Forza 6 Dual-Bridge   ");
            Console.WriteLine("======================================================================");

            // 0. 引数解析とコンフィグのロード
            bool setupMode = args.Contains("--setup-sensors");
            AppConfig config = ConfigManager.Load();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--power" && i + 1 < args.Length)
                {
                    string macStr = args[i + 1].Replace(":", "");
                    if (ulong.TryParse(macStr, System.Globalization.NumberStyles.HexNumber, null, out ulong mac))
                    {
                        config.PowerSourceType = SensorType.CyclingPower;
                        config.PowerSourceMacAddress = mac;
                        Console.WriteLine($"[INFO] CLI override: Power Meter MAC {mac:X}");
                    }
                }
            }

            if (config.PowerSourceType == SensorType.None)
            {
                setupMode = true;
            }
            else if (!setupMode)
            {
                Console.WriteLine($"[INFO] Loaded config: {config.PowerSourceType} ({config.PowerSourceMacAddress:X})");
            }

            if (setupMode)
            {
                config = await RunSetupSensorsAsync(config);
            }

            // 1. 動作モードの選択
            int defaultMode = (config.DefaultMode == 1 || config.DefaultMode == 2) ? config.DefaultMode : 2;
            Console.WriteLine("\n[MODE SELECTION]");
            Console.WriteLine(" 1. Arcade Mode (Pedal Power -> Direct Throttle Mapping)");
            Console.WriteLine(" 2. Simulation Mode (Pedal Power + Pitch -> Speed Tracking via PID)");
            Console.Write($" Select mode (1 or 2, default is {defaultMode}): ");
            string input = Console.ReadLine() ?? "";
            
            IPowerMappingStrategy strategy;
            string modeName;
            int selectedMode;

            if (input.Trim() == "1")
            {
                selectedMode = 1;
            }
            else if (input.Trim() == "2")
            {
                selectedMode = 2;
            }
            else
            {
                selectedMode = defaultMode;
            }

            if (selectedMode == 1)
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

            if (config.DefaultMode != selectedMode)
            {
                config.DefaultMode = selectedMode;
                ConfigManager.Save(config);
            }

            // 1.5. 負荷再現割合 (Trainer Difficulty) の選択
            double defaultDiff = config.TrainerDifficulty;
            if (defaultDiff < 0.0 || defaultDiff > 1.0) defaultDiff = 0.5;
            int defaultDiffPercent = (int)Math.Round(defaultDiff * 100.0);

            Console.WriteLine("\n[TRAINER DIFFICULTY SELECTION]");
            Console.Write($" Enter Trainer Difficulty (0% to 100%, default is {defaultDiffPercent}%): ");
            string diffInput = Console.ReadLine() ?? "";
            if (string.IsNullOrWhiteSpace(diffInput))
            {
                _trainerDifficulty = defaultDiff;
            }
            else if (double.TryParse(diffInput, out double parsedDiff))
            {
                _trainerDifficulty = Math.Clamp(parsedDiff / 100.0, 0.0, 1.0);
            }
            else
            {
                _trainerDifficulty = defaultDiff;
            }

            if (Math.Abs(config.TrainerDifficulty - _trainerDifficulty) > 0.0001)
            {
                config.TrainerDifficulty = _trainerDifficulty;
                ConfigManager.Save(config);
            }

            // ダッシュボード初期化
            strategy.OnDebugLog = msg => _dashboard.AddLog(msg);
            _dashboard.ModeName = modeName;
            _dashboard.IsArcadeMode = (strategy is ArcadeMappingStrategy);
            _dashboard.InitializeLayout();
            _dashboard.AddLog($"Selected Mode: {modeName}");
            _dashboard.AddLog($"Trainer Difficulty set to: {(_trainerDifficulty * 100.0):F0}%");

            // 2. 各連携モジュールの初期化
            // A. vJoy 仮想コントローラーの初期化
            using var vJoyController = new VJoyVehicleController(1);
            vJoyController.OnStatusMessage += msg => _dashboard.AddLog(msg);
            bool isVJoyReady = vJoyController.Initialize();
            _dashboard.IsVJoyActive = isVJoyReady;

            // B. BLE デバイスの接続
            FtmsClient? ftmsClient = null;
            CyclingPowerClient? cpClient = null;
            bool isBleConnected = false;

            if (config != null && config.PowerSourceType == SensorType.Ftms)
            {
                ftmsClient = new FtmsClient();
                ftmsClient.OnStatusMessage += msg => _dashboard.AddLog($"[BLE] {msg}");
                ftmsClient.OnPowerReceived += power => _currentPower = power;
                ftmsClient.OnSpeedReceived += speed => _trainerSpeedKmh = speed;

                _dashboard.BleStatus = $"Connecting (FTMS MAC: {config.PowerSourceMacAddress:X})...";
                isBleConnected = await ftmsClient.ScanAndConnectAsync(20000, config.PowerSourceMacAddress);
                if (isBleConnected)
                {
                    await ftmsClient.SetTargetResistanceLevelAsync(0);
                    _lastSentGrade = 0.0;
                    _dashboard.BleStatus = $"Connected (FTMS: {config.PowerSourceMacAddress:X})";
                    _dashboard.AddLog("[BLE] Smart trainer resistance set to FREE (Level 0).");
                }
                else
                {
                    _dashboard.BleStatus = "Connection Failed (FTMS)";
                }
            }
            else if (config != null && config.PowerSourceType == SensorType.CyclingPower)
            {
                cpClient = new CyclingPowerClient();
                cpClient.OnStatusMessage += msg => _dashboard.AddLog($"[BLE] {msg}");
                cpClient.OnPowerReceived += power => _currentPower = power;

                _dashboard.BleStatus = $"Connecting (PowerMeter MAC: {config.PowerSourceMacAddress:X})...";
                isBleConnected = await cpClient.ScanAndConnectAsync(20000, config.PowerSourceMacAddress);
                if (isBleConnected)
                {
                    _dashboard.BleStatus = $"Connected (PowerMeter: {config.PowerSourceMacAddress:X})";
                    _dashboard.AddLog("[BLE] Cycling Power Meter connected.");
                }
                else
                {
                    _dashboard.BleStatus = "Connection Failed (PowerMeter)";
                }
            }
            else
            {
                _dashboard.BleStatus = "Not Connected (Fallback 0W)";
                _dashboard.AddLog("[WARNING] Could not connect to BLE device. Fallback 0W.");
            }

            // C. Forza UDP テレメトリ受信サーバーの初期化
            int port = 5000;
            var receiver = new ForzaUdpReceiver(port);
            receiver.OnStatusMessage += msg => _dashboard.AddLog(msg);

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

                if (isVJoyReady && !_isTestingThrottle && !_isTestingBrake)
                {
                    vJoyController.SendInputs(control.Throttle, control.Brake);
                }

                uint currentTimeMS = packet.TimestampMS;
                double currentSpeedKmh = packet.SpeedKmh;
                double targetSpeedKmh = (strategy is SimulationMappingStrategy sim) ? sim.TargetSpeedKmh : 0.0;

                // ダッシュボード更新
                _dashboard.UpdateMetrics(
                    power: _currentPower,
                    targetSpeed: targetSpeedKmh,
                    carSpeed: currentSpeedKmh,
                    rawGrade: _filteredGrade,
                    sentGrade: _lastSentGrade == 999.0 ? 0.0 : _lastSentGrade,
                    difficulty: _trainerDifficulty,
                    throttle: control.Throttle,
                    brake: control.Brake
                );

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
                            _dashboard.AddLog("[BLE-SEND] Sent FREE (Level 0)");
                            _ = ftmsClient.SetTargetResistanceLevelAsync(0);
                        }
                        else
                        {
                            _dashboard.AddLog($"[BLE-SEND] Sent Grade {targetIncline:F1}%");
                            _ = ftmsClient.SetIndoorBikeSimulationParametersAsync(targetIncline);
                        }
                    }
                }

            };

            receiver.OnError += (ex) =>
            {
                _dashboard.AddLog($"[ERROR] Telemetry error: {ex.Message}");
            };

            // 4. システムの実行稼働
            try
            {
                receiver.Start();
                _dashboard.IsTelemetryActive = true;
                _dashboard.AddLog("[BRIDGE] Middle-ware bridge is now ACTIVE.");
                
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
                            _dashboard.AddLog($"Difficulty decreased to: {(_trainerDifficulty * 100.0):F0}%");
                            _lastSentGrade = 999.0; 
                            
                            config!.TrainerDifficulty = _trainerDifficulty;
                            ConfigManager.Save(config!);

                            if (_trainerDifficulty <= 0.001 && ftmsClient != null && ftmsClient.IsConnected)
                            {
                                _ = ftmsClient.SetTargetResistanceLevelAsync(0);
                                _lastSentGrade = 0.0;
                            }
                        }
                        else if (keyChar == '+' || keyChar == '=')
                        {
                            _trainerDifficulty = Math.Clamp(_trainerDifficulty + 0.1, 0.0, 1.0);
                            _dashboard.AddLog($"Difficulty increased to: {(_trainerDifficulty * 100.0):F0}%");
                            _lastSentGrade = 999.0; 
                            
                            config!.TrainerDifficulty = _trainerDifficulty;
                            ConfigManager.Save(config!);
                        }
                        else if (key == ConsoleKey.T)
                        {
                            _dashboard.AddLog("[TEST] Sending THROTTLE 100% (3 seconds)...");
                            _isTestingThrottle = true;
                            if (isVJoyReady) vJoyController.SendInputs(1.0f, 0.0f);
                            await Task.Delay(3000);
                            if (isVJoyReady) vJoyController.SendInputs(0.0f, 0.0f);
                            _isTestingThrottle = false;
                            _dashboard.AddLog("[TEST] Throttle output stopped.");
                        }
                        else if (key == ConsoleKey.B || key == ConsoleKey.Spacebar)
                        {
                            _dashboard.AddLog("[TEST] Sending BRAKE 100% Emergency (3 seconds)...");
                            _isTestingBrake = true;
                            if (isVJoyReady) vJoyController.SendInputs(0.0f, 1.0f);
                            await Task.Delay(3000);
                            if (isVJoyReady) vJoyController.SendInputs(0.0f, 0.0f);
                            _isTestingBrake = false;
                            _dashboard.AddLog("[TEST] Emergency Brake output released.");
                        }
                        else if (key == ConsoleKey.M)
                        {
                            int newMode;
                            if (strategy is SimulationMappingStrategy)
                            {
                                strategy = new ArcadeMappingStrategy(ftp: 200.0);
                                modeName = "ARCADE MODE";
                                newMode = 1;
                            }
                            else
                            {
                                strategy = new SimulationMappingStrategy(kp: 1.0f, ki: 0.2f, kd: 0.05f);
                                modeName = "SIMULATION MODE";
                                newMode = 2;
                            }
                            strategy.OnDebugLog = msg => _dashboard.AddLog(msg);
                            _dashboard.ModeName = modeName;
                            _dashboard.IsArcadeMode = (strategy is ArcadeMappingStrategy);
                            _dashboard.AddLog($"Switched mode to: {modeName}");
                            _lastSentGrade = 999.0; 

                            config!.DefaultMode = newMode;
                            ConfigManager.Save(config!);
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
                _dashboard.AddLog($"[FATAL] Application crash: {ex.Message}");
            }
            finally
            {
                receiver.Stop();
                ftmsClient?.Disconnect();
                cpClient?.Disconnect();
                _dashboard.Cleanup();
            }
        }

        private static async Task<AppConfig> RunSetupSensorsAsync(AppConfig existingConfig)
        {
            var config = existingConfig;
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
                return config;
            }

            Console.Write("Select device for POWER (Enter number, or 0 to skip): ");
            string input = Console.ReadLine() ?? "0";
            if (int.TryParse(input, out int idx) && idx > 0 && idx <= foundDevices.Count)
            {
                var selected = foundDevices[idx - 1];
                config.PowerSourceType = selected.Type;
                config.PowerSourceMacAddress = selected.Address;
                config.PowerSourceName = selected.Name;

                ConfigManager.Save(config);
                Console.WriteLine($"[SETUP] Configuration saved to config.json. Selected: {selected.Name} ({selected.Address:X})");
                return config;
            }

            Console.WriteLine("[SETUP] Setup skipped or invalid selection. Falling back to default.");
            return config;
        }
    }
}
