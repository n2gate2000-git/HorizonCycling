using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HorizonCyclingBridge.Controller;
using HorizonCyclingBridge.Telemetry;
using HorizonCyclingBridge.Trainer;

namespace HorizonCyclingBridge.Core
{
    public class BridgeStateSnapshot
    {
        // システム状態
        public string ModeName { get; set; } = "SIMULATION MODE";
        public int Mode { get; set; } = 2; // 1: Arcade, 2: Simulation
        public string BleStatus { get; set; } = "Disconnected";
        public string ConnectedDeviceName { get; set; } = "None";
        public bool IsBleConnected { get; set; } = false;
        public bool IsVJoyActive { get; set; } = false;
        public bool IsTelemetryActive { get; set; } = false;
        public bool PedalBrakeEnabled { get; set; } = true;
        public double Difficulty { get; set; } = 0.5;
        public double Ftp { get; set; } = 200.0;

        // リアルタイムメトリクス
        public double Power { get; set; } = 0.0;
        public double Cadence { get; set; } = 0.0;
        public double HeartRate { get; set; } = 0.0;
        public double TargetSpeedKmh { get; set; } = 0.0;
        public double CarSpeedKmh { get; set; } = 0.0;
        public double RawGrade { get; set; } = 0.0;
        public double SentGrade { get; set; } = 0.0;
        public double Throttle { get; set; } = 0.0;
        public double Brake { get; set; } = 0.0;
        public byte Gear { get; set; } = 1;

        // 座標・標高・セッション
        public float PositionX { get; set; } = 0.0f;
        public float PositionY { get; set; } = 0.0f; // 標高
        public float PositionZ { get; set; } = 0.0f;
        public float Yaw { get; set; } = 0.0f;
        public double MapPixelX { get; set; } = 4096.0;
        public double MapPixelY { get; set; } = 4096.0;
        public double Latitude { get; set; } = 35.3606;
        public double Longitude { get; set; } = 138.7274;

        public double ElevationMeters { get; set; } = 0.0;
        public double ElevationGainMeters { get; set; } = 0.0;
        public double TotalDistanceKm { get; set; } = 0.0;

        public bool IsPositionValid { get; set; } = true;
        public bool IsTeleport { get; set; } = false;
        public string SessionState { get; set; } = "Stopped";
        public double ElapsedSeconds { get; set; } = 0.0;
    }

    public class BleDeviceInfo
    {
        public ulong Address { get; set; }
        public string MacAddressHex => Address.ToString("X12");
        public string FormattedMac => string.Join(":", Enumerable.Range(0, 6).Select(i => (Address >> ((5 - i) * 8) & 0xFF).ToString("X2")));
        public string Name { get; set; } = "Unknown";
        public string TypeName { get; set; } = "Ftms"; // Ftms or CyclingPower
        public int SensorTypeInt { get; set; } = 1; // 1: Ftms, 2: CyclingPower
        public bool IsPaired { get; set; } = false;
    }

    /// <summary>
    /// HorizonCycling の中核となるバックグラウンドブリッジサービス
    /// </summary>
    public class BridgeService : IDisposable
    {
        private readonly AppConfig _config;
        private readonly VJoyVehicleController _vjoy;
        private readonly ForzaUdpReceiver _udpReceiver;
        private readonly CoordinateEngine _coordEngine;
        private readonly ElevationTracker _elevationTracker;
        private readonly SessionManager _sessionManager;

        private FtmsClient? _ftmsClient;
        private CyclingPowerClient? _powerClient;
        private IPowerMappingStrategy _strategy;

        private double _currentPower = 0.0;
        private double _currentCadence = 0.0;
        private double _currentHeartRate = 0.0;
        private double _filteredGrade = 0.0;
        private double _trainerDifficulty = 0.5;
        private double _trainerSpeedKmh = 0.0;
        private double _lastSentGrade = 999.0;
        private uint _lastSentTimeMS = 0;
        private DateTime _lastSentTime = DateTime.MinValue;
        private const double EMA_ALPHA = 0.03;

        private DateTime _lastPacketTime = DateTime.MinValue;
        private ForzaDataPacket? _lastPacket = null;
        private bool _isDisposed = false;
        private bool _isBleConnected = false;
        private string _bleStatus = "Disconnected";
        private DateTime _lastLoopTime = DateTime.UtcNow;

        // メニュー画面・0,0,0ワープ対策用キャッシュ
        private bool _hasValidPosition = false;
        private float _lastValidPositionX = 0f;
        private float _lastValidPositionY = 0f;
        private float _lastValidPositionZ = 0f;
        private float _lastValidYaw = 0f;
        private double _lastValidPixelX = 4096.0;
        private double _lastValidPixelY = 4096.0;
        private double _lastValidLat = 35.3606;
        private double _lastValidLon = 138.7274;

        public event Action<BridgeStateSnapshot>? OnStateUpdated;
        public event Action<string>? OnLogMessage;
        public event Action<SessionSummary>? OnSessionSaved;

        public CoordinateEngine CoordinateEngine => _coordEngine;
        public ElevationTracker ElevationTracker => _elevationTracker;
        public SessionManager SessionManager => _sessionManager;
        public AppConfig Config => _config;

        public BridgeService(AppConfig? config = null)
        {
            _config = config ?? ConfigManager.Load();
            _vjoy = new VJoyVehicleController(1);
            _vjoy.OnStatusMessage += msg => Log(msg);

            int port = 5000;
            _udpReceiver = new ForzaUdpReceiver(port);
            _udpReceiver.OnStatusMessage += msg => Log(msg);

            _coordEngine = new CoordinateEngine
            {
                WorldOriginX = _config.MapOriginX,
                WorldOriginZ = _config.MapOriginZ,
                WorldScale = _config.MapScale > 0 ? _config.MapScale : 0.263,
                InvertZ = _config.InvertZ
            };
            _elevationTracker = new ElevationTracker();
            _sessionManager = new SessionManager();

            _trainerDifficulty = Math.Clamp(_config.TrainerDifficulty, 0.0, 1.0);
            
            int mode = (_config.DefaultMode == 1 || _config.DefaultMode == 2) ? _config.DefaultMode : 2;
            if (mode == 1)
            {
                _strategy = new ArcadeMappingStrategy(_config.Ftp);
            }
            else
            {
                _strategy = new SimulationMappingStrategy();
            }
            _strategy.PedalBrakeEnabled = _config.PedalBrakeEnabled;

            // マップピン情報のロード試行
            string[] possiblePinPaths = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "map", "fh6_japan_pins.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "map", "fh6_japan_pins.json"),
                @"d:\develop\HorizonCycling\map\fh6_japan_pins.json"
            };
            foreach (var p in possiblePinPaths)
            {
                if (File.Exists(p))
                {
                    _coordEngine.LoadPins(p);
                    break;
                }
            }
        }

        /// <summary>
        /// マップ上の指定ピクセル位置に現在の自車位置を吸着（キャリブレーション）
        /// </summary>
        public void CalibrateMap(double targetPixelX, double targetPixelY)
        {
            if (_lastPacket == null)
            {
                Log("[MAP-WARN] テレメトリを受信していないため、キャリブレーションできません。ゲームを起動して車を走らせてください。");
                return;
            }

            _coordEngine.Calibrate(targetPixelX, targetPixelY, _lastPacket.PositionX, _lastPacket.PositionZ);
            _config.MapOriginX = _coordEngine.WorldOriginX;
            _config.MapOriginZ = _coordEngine.WorldOriginZ;
            ConfigManager.Save(_config);
            Log($"[MAP] キャリブレーション完了: 原点(X={_coordEngine.WorldOriginX:F1}, Z={_coordEngine.WorldOriginZ:F1}) -> 地図位置({targetPixelX:F0}, {targetPixelY:F0})");
        }

        /// <summary>
        /// マップの縮尺・反転設定の更新
        /// </summary>
        public void UpdateMapConfig(double scale, bool invertZ, double? originX = null, double? originZ = null)
        {
            if (scale > 0) _coordEngine.WorldScale = scale;
            _coordEngine.InvertZ = invertZ;
            if (originX.HasValue) _coordEngine.WorldOriginX = originX.Value;
            if (originZ.HasValue) _coordEngine.WorldOriginZ = originZ.Value;

            _config.MapScale = _coordEngine.WorldScale;
            _config.InvertZ = _coordEngine.InvertZ;
            _config.MapOriginX = _coordEngine.WorldOriginX;
            _config.MapOriginZ = _coordEngine.WorldOriginZ;
            ConfigManager.Save(_config);
            Log($"[MAP] マップ設定更新: 縮尺={_coordEngine.WorldScale:F2}, Z反転={invertZ}, 原点=({_coordEngine.WorldOriginX:F0}, {_coordEngine.WorldOriginZ:F0})");
        }

        public void SetMode(int mode)
        {
            if (mode == 1)
            {
                _strategy = new ArcadeMappingStrategy(_config.Ftp);
                Log("[MODE] Switched to ARCADE MODE.");
            }
            else
            {
                _strategy = new SimulationMappingStrategy();
                Log("[MODE] Switched to SIMULATION MODE.");
            }
            _strategy.PedalBrakeEnabled = _config.PedalBrakeEnabled;

            _config.DefaultMode = mode;
            ConfigManager.Save(_config);
        }

        public void SetDifficulty(double difficulty)
        {
            _trainerDifficulty = Math.Clamp(difficulty, 0.0, 1.0);
            _config.TrainerDifficulty = _trainerDifficulty;
            ConfigManager.Save(_config);
            Log($"[CONFIG] Trainer Difficulty set to {_trainerDifficulty * 100:F0}%.");
        }

        public void SetPedalBrake(bool enabled)
        {
            _config.PedalBrakeEnabled = enabled;
            _strategy.PedalBrakeEnabled = enabled;
            ConfigManager.Save(_config);
            Log($"[CONFIG] Pedal Brake {(enabled ? "Enabled" : "Disabled")}.");
        }

        public void SetFtp(double ftp)
        {
            if (ftp <= 0) return;
            _config.Ftp = ftp;
            ConfigManager.Save(_config);

            if (_strategy is ArcadeMappingStrategy arcade)
            {
                arcade.Ftp = ftp;
            }
            Log($"[CONFIG] Rider FTP set to {_config.Ftp:F0}W.");
        }

        public void StartSession()
        {
            _elevationTracker.Reset();
            _sessionManager.Start();
            Log("[SESSION] Started recording activity.");
        }

        public void PauseSession()
        {
            _sessionManager.Pause();
            Log("[SESSION] Paused.");
        }

        public void ResumeSession()
        {
            _sessionManager.Resume();
            Log("[SESSION] Resumed.");
        }

        public void StopSession()
        {
            string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "activities");
            var summary = _sessionManager.StopAndExport(outDir, _elevationTracker.TotalDistanceMeters / 1000.0, _elevationTracker.ElevationGain);
            Log($"[SESSION] Finished. Saved to: {summary.SavedFilePath}");
            OnSessionSaved?.Invoke(summary);
        }

        public void ClearTrack()
        {
            _elevationTracker.Reset();
            Log("[TRACK] Cleared track and reset elevation gain.");
        }

        public async Task StartAsync()
        {
            // vJoy 初期化
            _vjoy.Initialize();

            // UDP受信パケットハンドラ登録
            _udpReceiver.OnPacketReceived += HandlePacketReceived;
            _udpReceiver.Start();
            Log("[SYSTEM] HorizonCycling core service started.");

            // BLE接続（バックグラウンド非同期）
            _ = Task.Run(async () =>
            {
                await ConnectConfiguredSensorAsync();
            });
        }

        public async Task<List<BleDeviceInfo>> ScanBleDevicesAsync(int timeoutSeconds = 6)
        {
            var foundDevices = new List<BleDeviceInfo>();
            Log($"[SETUP] Scanning for BLE devices ({timeoutSeconds}s)...");

            // 1. ペアリング済みデバイス検索
            try
            {
                var ftmsSelector = Windows.Devices.Bluetooth.GenericAttributeProfile.GattDeviceService.GetDeviceSelectorFromUuid(Guid.Parse("00001826-0000-1000-8000-00805f9b34fb"));
                var ftmsDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(ftmsSelector);
                foreach (var d in ftmsDevices)
                {
                    var bleDevice = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(d.Id);
                    if (bleDevice != null && !foundDevices.Any(x => x.Address == bleDevice.BluetoothAddress))
                    {
                        foundDevices.Add(new BleDeviceInfo
                        {
                            Address = bleDevice.BluetoothAddress,
                            Name = string.IsNullOrEmpty(bleDevice.Name) ? "FTMS Trainer" : bleDevice.Name,
                            TypeName = "FTMS Smart Trainer",
                            SensorTypeInt = 1,
                            IsPaired = true
                        });
                    }
                }

                var powerSelector = Windows.Devices.Bluetooth.GenericAttributeProfile.GattDeviceService.GetDeviceSelectorFromUuid(Guid.Parse("00001818-0000-1000-8000-00805f9b34fb"));
                var powerDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(powerSelector);
                foreach (var d in powerDevices)
                {
                    var bleDevice = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(d.Id);
                    if (bleDevice != null && !foundDevices.Any(x => x.Address == bleDevice.BluetoothAddress))
                    {
                        foundDevices.Add(new BleDeviceInfo
                        {
                            Address = bleDevice.BluetoothAddress,
                            Name = string.IsNullOrEmpty(bleDevice.Name) ? "Power Meter" : bleDevice.Name,
                            TypeName = "Cycling Power Meter",
                            SensorTypeInt = 2,
                            IsPaired = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[SETUP] Paired query warning: {ex.Message}");
            }

            // 2. Advertisement Watcher
            try
            {
                var watcher = new Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher();
                watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(Guid.Parse("00001826-0000-1000-8000-00805f9b34fb"));
                watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(Guid.Parse("00001818-0000-1000-8000-00805f9b34fb"));

                watcher.Received += (s, e) =>
                {
                    lock (foundDevices)
                    {
                        if (!foundDevices.Any(d => d.Address == e.BluetoothAddress))
                        {
                            string name = string.IsNullOrEmpty(e.Advertisement.LocalName) ? "Unknown Device" : e.Advertisement.LocalName;
                            bool isFtms = e.Advertisement.ServiceUuids.Contains(Guid.Parse("00001826-0000-1000-8000-00805f9b34fb"));
                            foundDevices.Add(new BleDeviceInfo
                            {
                                Address = e.BluetoothAddress,
                                Name = name,
                                TypeName = isFtms ? "FTMS Smart Trainer" : "Cycling Power Meter",
                                SensorTypeInt = isFtms ? 1 : 2,
                                IsPaired = false
                            });
                        }
                    }
                };

                watcher.Start();
                await Task.Delay(timeoutSeconds * 1000);
                watcher.Stop();
            }
            catch (Exception ex)
            {
                Log($"[SETUP] Advertisement scan warning: {ex.Message}");
            }

            Log($"[SETUP] Scan finished. Found {foundDevices.Count} devices.");
            return foundDevices;
        }

        public async Task<bool> ConnectSensorAsync(SensorType type, ulong macAddress, string name)
        {
            _ftmsClient?.Disconnect();
            _ftmsClient?.Dispose();
            _ftmsClient = null;

            _powerClient?.Disconnect();
            _powerClient?.Dispose();
            _powerClient = null;

            _isBleConnected = false;
            _config.PowerSourceType = type;
            _config.PowerSourceMacAddress = macAddress;
            _config.PowerSourceName = name;
            ConfigManager.Save(_config);

            Log($"[BLE] Target device changed to {name} ({macAddress:X}). Connecting...");
            return await ConnectConfiguredSensorAsync();
        }

        public async Task<bool> ConnectConfiguredSensorAsync()
        {
            try
            {
                if (_config.PowerSourceType == SensorType.Ftms)
                {
                    _ftmsClient = new FtmsClient();
                    _ftmsClient.OnStatusMessage += msg => Log($"[BLE] {msg}");
                    _ftmsClient.OnPowerReceived += power => _currentPower = power;
                    _ftmsClient.OnSpeedReceived += speed => _trainerSpeedKmh = speed;

                    string displayName = string.IsNullOrEmpty(_config.PowerSourceName) ? $"{_config.PowerSourceMacAddress:X}" : _config.PowerSourceName;
                    _bleStatus = $"Connecting ({displayName})...";
                    _isBleConnected = await _ftmsClient.ScanAndConnectAsync(20000, _config.PowerSourceMacAddress);
                    if (_isBleConnected)
                    {
                        await _ftmsClient.SetTargetResistanceLevelAsync(0);
                        _lastSentGrade = 0.0;
                        _bleStatus = $"Connected ({displayName})";
                        Log($"[BLE] Smart trainer connected: {displayName} (Resistance: Level 0).");
                        return true;
                    }
                    else
                    {
                        _bleStatus = "Connection Failed (FTMS)";
                        return false;
                    }
                }
                else if (_config.PowerSourceType == SensorType.CyclingPower)
                {
                    _powerClient = new CyclingPowerClient();
                    _powerClient.OnStatusMessage += msg => Log($"[BLE] {msg}");
                    _powerClient.OnPowerReceived += power => _currentPower = power;

                    string displayName = string.IsNullOrEmpty(_config.PowerSourceName) ? $"{_config.PowerSourceMacAddress:X}" : _config.PowerSourceName;
                    _bleStatus = $"Connecting ({displayName})...";
                    _isBleConnected = await _powerClient.ScanAndConnectAsync(20000, _config.PowerSourceMacAddress);
                    if (_isBleConnected)
                    {
                        _bleStatus = $"Connected ({displayName})";
                        Log($"[BLE] Cycling Power Meter connected: {displayName}.");
                        return true;
                    }
                    else
                    {
                        _bleStatus = "Connection Failed (PowerMeter)";
                        return false;
                    }
                }
                else
                {
                    _bleStatus = "Not Configured (Fallback 0W)";
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"[BLE-ERR] Connection error: {ex.Message}");
                _bleStatus = "Error";
                return false;
            }
        }

        internal void HandlePacketReceived(ForzaDataPacket packet)
        {
            DateTime now = DateTime.UtcNow;
            double deltaSec = (now - _lastLoopTime).TotalSeconds;
            _lastLoopTime = now;
            if (deltaSec <= 0 || deltaSec > 0.5) deltaSec = 0.016;

            _lastPacketTime = now;
            _lastPacket = packet;

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

            if (_strategy is SimulationMappingStrategy simStrategy)
            {
                simStrategy.TrainerSpeedKmh = _trainerSpeedKmh;
                simStrategy.RoadGradePercent = difficultyGrade;
                simStrategy.TrueRoadGradePercent = trueRoadGrade;
            }

            ControlOutput control = _strategy.CalculateOutput(_currentPower, packet);
            if (_vjoy.IsAcquired)
            {
                _vjoy.SendInputs(control.Throttle, control.Brake);
            }

            // 勾配フィルタ更新
            _filteredGrade = (_filteredGrade * (1.0 - EMA_ALPHA)) + (correctedGrade * EMA_ALPHA);

            // ゼロ座標およびメニュー画面判定
            // Forza はメニューやポーズ、マップ画面を開くと PositionX/Y/Z がすべて 0 に飛ぶ
            bool isZeroPosition = Math.Abs(packet.PositionX) < 0.0001f && 
                                  Math.Abs(packet.PositionY) < 0.0001f && 
                                  Math.Abs(packet.PositionZ) < 0.0001f;
            bool isPositionValid = packet.IsRaceOn && !isZeroPosition;

            double pixelX;
            double pixelY;
            double lat;
            double lon;
            float outPosX;
            float outPosY;
            float outPosZ;
            float outYaw;
            bool isFastTravel = false;

            if (isPositionValid)
            {
                // ファストトラベル（テレポート・リセット）判定
                if (_hasValidPosition)
                {
                    float dx = packet.PositionX - _lastValidPositionX;
                    float dy = packet.PositionY - _lastValidPositionY;
                    float dz = packet.PositionZ - _lastValidPositionZ;
                    double horizontalDist = Math.Sqrt(dx * dx + dz * dz);
                    double verticalDist = Math.Abs(dy);

                    // 最高速でも到達不能な距離（時速400km/h = 111m/s相当）
                    double maxAllowedHorizontal = Math.Max(35.0, (packet.SpeedKmh / 3.6) * deltaSec * 3.0 + 15.0);
                    double maxAllowedVertical = Math.Max(4.0, (packet.SpeedKmh / 3.6) * deltaSec * 1.5 + 2.0);

                    if (horizontalDist > maxAllowedHorizontal || (verticalDist > maxAllowedVertical && deltaSec < 0.5))
                    {
                        isFastTravel = true;
                        Log($"[TELEMETRY] Fast travel detected (Jumped {horizontalDist:F0}m, Ele: {dy:+0.0;-0.0}m). Elevation gain skipped.");
                    }
                }

                // 有効な走行座標
                (pixelX, pixelY) = _coordEngine.WorldToPixel(packet.PositionX, packet.PositionZ);
                (lat, lon) = _coordEngine.WorldToGps(packet.PositionX, packet.PositionZ);

                _lastValidPositionX = packet.PositionX;
                _lastValidPositionY = packet.PositionY;
                _lastValidPositionZ = packet.PositionZ;
                _lastValidYaw = packet.Yaw;
                _lastValidPixelX = pixelX;
                _lastValidPixelY = pixelY;
                _lastValidLat = lat;
                _lastValidLon = lon;
                _hasValidPosition = true;

                outPosX = packet.PositionX;
                outPosY = packet.PositionY;
                outPosZ = packet.PositionZ;
                outYaw = packet.Yaw;

                // 標高トラッカー更新 (ファストトラベル時は獲得標高加算をスキップ)
                _elevationTracker.Update(packet.PositionY, packet.SpeedKmh, _filteredGrade, deltaSec, isFastTravel);

                // セッショントラックポイント記録 (走行中かつ有効座標のみ)
                _sessionManager.AddTrackPoint(lat, lon, _elevationTracker.CurrentElevation, packet.SpeedKmh, _currentPower, _currentCadence, _currentHeartRate);
            }
            else
            {
                // メニュー中や0,0,0受信時は直前の有効座標を維持して原点ジャンプを防ぐ
                if (_hasValidPosition)
                {
                    outPosX = _lastValidPositionX;
                    outPosY = _lastValidPositionY;
                    outPosZ = _lastValidPositionZ;
                    outYaw = _lastValidYaw;
                    pixelX = _lastValidPixelX;
                    pixelY = _lastValidPixelY;
                    lat = _lastValidLat;
                    lon = _lastValidLon;

                    // 標高が0mに急落して累積標高が崩れないよう、直前標高を維持
                    _elevationTracker.Update(_lastValidPositionY, 0.0f, 0.0, deltaSec);
                }
                else
                {
                    // まだ有効座標を受信していない初期状態
                    (pixelX, pixelY) = _coordEngine.WorldToPixel(packet.PositionX, packet.PositionZ);
                    (lat, lon) = _coordEngine.WorldToGps(packet.PositionX, packet.PositionZ);
                    outPosX = packet.PositionX;
                    outPosY = packet.PositionY;
                    outPosZ = packet.PositionZ;
                    outYaw = packet.Yaw;

                    _elevationTracker.Update(packet.PositionY, 0.0f, 0.0, deltaSec);
                }
                // ※_sessionManager.AddTrackPoint はスキップ（GPXやログの汚染を防止）
            }

            // スマートローラー負荷フィードバック
            if (_isBleConnected && _ftmsClient != null && _ftmsClient.IsConnected)
            {
                // 車両停止中（< 1.0 km/h）またはレース非アクティブ時は負荷をフリー(0.0%)に解放
                bool shouldReleaseLoad = !packet.IsRaceOn || packet.SpeedKmh < 1.0f;
                double effectiveGrade = shouldReleaseLoad ? 0.0 : _filteredGrade;

                double timeDiffMs = (now - _lastSentTime).TotalMilliseconds;
                double gradeDiff = Math.Abs(effectiveGrade - _lastSentGrade);

                // 判定条件:
                // 1. 初回送信 (_lastSentGrade == 999.0)
                // 2. ゼロ復帰 (勾配がほぼ平坦に戻った時、または停止・非レース時)
                // 3. 有意な勾配変化 (800ms以上経過 かつ 0.3%以上の変化)
                // 4. キープアライブ (坂道を走行中、パケット欠落やトレーナータイムアウトを防ぐため3秒ごとに再送)
                bool isZeroReset = (Math.Abs(effectiveGrade) < 0.2 || shouldReleaseLoad) && _lastSentGrade != 0.0 && _lastSentGrade != 999.0;
                bool isSignificantChange = timeDiffMs >= 800 && gradeDiff >= 0.3;
                bool isKeepAlive = timeDiffMs >= 3000 && _lastSentGrade != 999.0;

                if (_lastSentGrade == 999.0 || isSignificantChange || isZeroReset || isKeepAlive)
                {
                    double targetIncline = isZeroReset ? 0.0 : effectiveGrade;

                    // 急激な負荷変化の緩和（最大ステップ ±2.0%/回）※キープアライブやゼロリセット時はステップ制限なし
                    if (!isZeroReset && !isKeepAlive && _lastSentGrade != 999.0)
                    {
                        double maxStep = 2.0;
                        double step = Math.Clamp(effectiveGrade - _lastSentGrade, -maxStep, maxStep);
                        targetIncline = _lastSentGrade + step;
                    }

                    targetIncline = Math.Round(targetIncline, 1);

                    // 仮想ギア比（Simulation モード時: 車速がターゲット速度を下回った際の負荷抜け防止）
                    if (_strategy is SimulationMappingStrategy simStrat)
                    {
                        double targetSpd = simStrat.TargetSpeedKmh;
                        double carSpd = packet.SpeedKmh;
                        if (targetSpd > 10.0 && carSpd < targetSpd * 0.95 && targetIncline > 0.0)
                        {
                            double deficit = 1.0 - (carSpd / targetSpd);
                            double gearMultiplier = Math.Max(0.0, 1.0 - (deficit * 4.0));
                            targetIncline = targetIncline * gearMultiplier;
                        }
                    }

                    // 安全クランプ（難易度を考慮した最大傾斜制限）
                    double maxIncline = 20.0 * _trainerDifficulty;
                    double minIncline = -10.0 * _trainerDifficulty;
                    targetIncline = Math.Clamp(targetIncline, minIncline, maxIncline);
                    targetIncline = Math.Round(targetIncline, 1);

                    // CUI版で実証済みの方式（登坂: シミュレーションパラメータOpCode 0x11、平地・下り: フリーレベル0）で送信
                    _ = _ftmsClient.SendGradeSimulationAsync(targetIncline);

                    // ログ出力（キープアライブ再送時はログ抑制し、勾配変化時またはゼロリセット時のみ出力）
                    if (_lastSentGrade != targetIncline || _lastSentGrade == 999.0)
                    {
                        if (targetIncline <= 0.0)
                        {
                            Log($"[TRAINER] Incline set to FREE (0.0%, Road: {trueRoadGrade:F1}%)");
                        }
                        else
                        {
                            Log($"[TRAINER] Incline sent: {targetIncline:F1}% (Road: {trueRoadGrade:F1}%, Diff: {_trainerDifficulty * 100:F0}%)");
                        }
                    }

                    _lastSentGrade = targetIncline;
                    _lastSentTime = now;
                    _lastSentTimeMS = packet.TimestampMS;
                }
            }

            // スナップショット通知
            double targetSpeedKmh = (_strategy is SimulationMappingStrategy sim) ? sim.TargetSpeedKmh : 0.0;
            var snapshot = new BridgeStateSnapshot
            {
                ModeName = _strategy is ArcadeMappingStrategy ? "ARCADE MODE" : "SIMULATION MODE",
                Mode = _strategy is ArcadeMappingStrategy ? 1 : 2,
                BleStatus = _bleStatus,
                ConnectedDeviceName = string.IsNullOrEmpty(_config.PowerSourceName) ? "None" : _config.PowerSourceName,
                IsBleConnected = _isBleConnected,
                IsVJoyActive = _vjoy.IsAcquired,
                IsTelemetryActive = (DateTime.UtcNow - _lastPacketTime).TotalMilliseconds < 1000,
                PedalBrakeEnabled = _strategy.PedalBrakeEnabled,
                Difficulty = _trainerDifficulty,
                Ftp = _config.Ftp,

                Power = _currentPower,
                Cadence = _currentCadence,
                HeartRate = _currentHeartRate,
                TargetSpeedKmh = targetSpeedKmh,
                CarSpeedKmh = packet.SpeedKmh,
                RawGrade = _filteredGrade,
                SentGrade = _lastSentGrade == 999.0 ? 0.0 : _lastSentGrade,
                Throttle = control.Throttle,
                Brake = control.Brake,
                Gear = packet.Gear,

                PositionX = outPosX,
                PositionY = outPosY,
                PositionZ = outPosZ,
                Yaw = outYaw,
                MapPixelX = pixelX,
                MapPixelY = pixelY,
                Latitude = lat,
                Longitude = lon,
                IsPositionValid = isPositionValid,
                IsTeleport = isFastTravel,

                ElevationMeters = _elevationTracker.CurrentElevation,
                ElevationGainMeters = _elevationTracker.ElevationGain,
                TotalDistanceKm = _elevationTracker.TotalDistanceMeters / 1000.0,

                SessionState = _sessionManager.State.ToString(),
                ElapsedSeconds = _sessionManager.ElapsedTime.TotalSeconds
            };

            OnStateUpdated?.Invoke(snapshot);
        }

        private void Log(string message)
        {
            OnLogMessage?.Invoke(message);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _udpReceiver.Stop();
            _vjoy.Dispose();
            _ftmsClient?.Dispose();
            _powerClient?.Dispose();
        }
    }
}
