using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HorizonCyclingBridge.Controller;
using HorizonCyclingBridge.Telemetry;
using HorizonCyclingBridge.Trainer;
using HorizonCyclingBridge.Workout;

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

        // 心拍計ステータス
        public bool IsHrConnected { get; set; } = false;
        public string HrStatus { get; set; } = "Disconnected";
        public string ConnectedHrDeviceName { get; set; } = "None";

        // セカンダリ ケイデンス / パワーメーター (Magene PES-P505 等) ステータス
        public bool IsCadenceConnected { get; set; } = false;
        public string CadenceStatus { get; set; } = "Disconnected";
        public string ConnectedCadenceDeviceName { get; set; } = "None";

        public bool IsVJoyActive { get; set; } = false;
        public bool IsTelemetryActive { get; set; } = false;
        public bool PedalBrakeEnabled { get; set; } = true;
        public double Difficulty { get; set; } = 0.5;
        public double Ftp { get; set; } = 200.0;
        public double RiderWeightKg { get; set; } = 70.0;

        // リアルタイムメトリクス
        public double Power { get; set; } = 0.0;
        public double Wkg { get; set; } = 0.0;
        public double CaloriesKcal { get; set; } = 0.0;
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

        // ワークアウト状態
        public bool IsWorkoutActive { get; set; } = false;
        public WorkoutStateSnapshot? Workout { get; set; }
    }

    public class BleDeviceInfo
    {
        public ulong Address { get; set; }
        public string MacAddressHex => Address.ToString("X12");
        public string FormattedMac => string.Join(":", Enumerable.Range(0, 6).Select(i => (Address >> ((5 - i) * 8) & 0xFF).ToString("X2")));
        public string Name { get; set; } = "Unknown";
        public string TypeName { get; set; } = "Ftms"; // Ftms, CyclingPower, or Heart Rate Monitor
        public int SensorTypeInt { get; set; } = 1; // 1: Ftms, 2: CyclingPower, 3: HeartRate
        public string Category { get; set; } = "trainer"; // "trainer" or "heartrate"
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
        private readonly WorkoutLibraryManager _workoutLibrary;
        private readonly WorkoutEngine _workoutEngine;

        private FtmsClient? _ftmsClient;
        private CyclingPowerClient? _powerClient;
        private HeartRateClient? _heartRateClient;
        private CyclingPowerClient? _cadenceClient; // セカンダリ ケイデンス / パワーメーター (Magene PES-P505 等)
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

        private double _lastThrottle = 0.0;
        private double _lastBrake = 0.0;
        private System.Threading.Timer? _idleTimer;

        private DateTime _lastPacketTime = DateTime.MinValue;
        private ForzaDataPacket? _lastPacket = null;
        private bool _isDisposed = false;
        private bool _isBleConnected = false;
        private string _bleStatus = "Disconnected";

        private bool _isHrConnected = false;
        private string _hrStatus = "Disconnected";

        private bool _isCadenceConnected = false;
        private string _cadenceStatus = "Disconnected";
        private double _cadencePower = 0.0;
        private double _ftmsPower = 0.0;
        private double _cadenceSensorCadence = 0.0;
        private double _ftmsCadence = 0.0;
        private DateTime _lastLoopTime = DateTime.UtcNow;

        // メニュー画面・0,0,0ワープ対策用キャッシュ
        private bool _hasValidPosition = false;
        private bool _isLastPositionValid = false;
        private bool _lastIsTeleport = false;
        private float _lastValidPositionX = 0f;
        private float _lastValidPositionY = 0f;
        private float _lastValidPositionZ = 0f;
        private float _lastValidYaw = 0f;
        private double _lastValidPixelX = 4096.0;
        private double _lastValidPixelY = 4096.0;
        private double _lastValidLat = 35.3606;
        private double _lastValidLon = 138.7274;
        private bool _isNextPointTeleport = false;

        public event Action<BridgeStateSnapshot>? OnStateUpdated;
        public event Action<string>? OnLogMessage;
        public event Action<SessionSummary>? OnSessionSaved;
        public event Action<WorkoutStateSnapshot>? OnWorkoutStateUpdated;
        public event Action<string>? OnWorkoutMessage;
        public event Action<RampTestResult>? OnRampTestResult;

        public CoordinateEngine CoordinateEngine => _coordEngine;
        public ElevationTracker ElevationTracker => _elevationTracker;
        public SessionManager SessionManager => _sessionManager;
        public WorkoutLibraryManager WorkoutLibrary => _workoutLibrary;
        public WorkoutEngine WorkoutEngine => _workoutEngine;
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

            _workoutLibrary = new WorkoutLibraryManager(ftp: _config.Ftp);
            _workoutEngine = new WorkoutEngine();

            _workoutEngine.OnTargetPowerChanged += async (watts) =>
            {
                if (_isBleConnected && _ftmsClient != null && _ftmsClient.IsConnected)
                {
                    await _ftmsClient.SetTargetPowerAsync(watts);
                }
            };
            _workoutEngine.OnMessageAlert += (msg) =>
            {
                Log($"[WORKOUT] 💬 {msg}");
                OnWorkoutMessage?.Invoke(msg);
            };
            _workoutEngine.OnWorkoutCompleted += () =>
            {
                Log("[WORKOUT] 🎉 ワークアウト完了！お疲れ様でした！");
            };
            _workoutEngine.OnRampTestCompleted += (res) =>
            {
                Log($"[RAMP-TEST] 🎉 ランプテスト完了! 新測定FTP: {res.NewFtp}W (旧: {res.OldFtp}W, {res.FtpDelta:+#;-#;0}W), 1分最大平均: {res.Best1MinPower:F1}W");
                OnRampTestResult?.Invoke(res);
            };
            _workoutEngine.OnStateUpdated += (snap) =>
            {
                OnWorkoutStateUpdated?.Invoke(snap);
            };

            _trainerDifficulty = Math.Clamp(_config.TrainerDifficulty, 0.0, 1.0);
            
            int mode = (_config.DefaultMode == 1 || _config.DefaultMode == 2) ? _config.DefaultMode : 2;
            if (mode == 1)
            {
                _strategy = new ArcadeMappingStrategy(_config.Ftp);
            }
            else
            {
                _strategy = new SimulationMappingStrategy { RiderWeightKg = _config.RiderWeightKg };
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
                _strategy = new SimulationMappingStrategy { RiderWeightKg = _config.RiderWeightKg };
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

            _workoutLibrary.SetFtp(ftp);

            if (_strategy is ArcadeMappingStrategy arcade)
            {
                arcade.Ftp = ftp;
            }
            Log($"[CONFIG] Rider FTP set to {_config.Ftp:F0}W.");
        }

        public void SetRiderWeight(double weight)
        {
            if (weight < 30.0 || weight > 250.0) return;
            _config.RiderWeightKg = Math.Round(weight, 1);
            ConfigManager.Save(_config);
            if (_strategy is SimulationMappingStrategy sim)
            {
                sim.RiderWeightKg = _config.RiderWeightKg;
            }
            Log($"[CONFIG] Rider Weight set to {_config.RiderWeightKg:F1}kg (Power-to-weight and physics updated).");
            PushCurrentSnapshot();
        }

        public void SetMaxHeartRate(int maxHr)
        {
            if (maxHr < 60 || maxHr > 250) return;
            _config.MaxHeartRate = maxHr;
            ConfigManager.Save(_config);
            Log($"[CONFIG] Rider Max HR set to {_config.MaxHeartRate} bpm.");
        }

        public void SetRestingHeartRate(int restingHr)
        {
            if (restingHr < 30 || restingHr > 120) return;
            _config.RestingHeartRate = restingHr;
            ConfigManager.Save(_config);
            Log($"[CONFIG] Rider Resting HR set to {_config.RestingHeartRate} bpm.");
        }

        public void SetProfileRecentDistance(int distanceM)
        {
            int clamped = Math.Clamp(distanceM, 100, 1000);
            clamped = (int)Math.Round(clamped / 100.0) * 100;
            _config.ProfileRecentDistanceM = clamped;
            ConfigManager.Save(_config);
            Log($"[CONFIG] Profile recent distance set to {clamped}m.");
        }

        public int ProfileRecentDistanceM => _config.ProfileRecentDistanceM;

        private string? _activitiesDirectory;
        public string ActivitiesDirectory
        {
            get
            {
                if (_activitiesDirectory != null) return _activitiesDirectory;

                // exeを起動したフォルダ（BaseDirectory）の下に logs/activities を作成
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dir = Path.Combine(baseDir, "logs", "activities");
                try
                {
                    Directory.CreateDirectory(dir);
                    _activitiesDirectory = dir;

                    // 旧固定フォルダ (d:\develop\HorizonCycling\logs\activities) からの既存ログ移行
                    string legacyDir = @"d:\develop\HorizonCycling\logs\activities";
                    if (Directory.Exists(legacyDir) && !string.Equals(Path.GetFullPath(dir), Path.GetFullPath(legacyDir), StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var file in Directory.GetFiles(legacyDir, "HorizonCycling_*.*"))
                        {
                            string dest = Path.Combine(dir, Path.GetFileName(file));
                            if (!File.Exists(dest))
                            {
                                try { File.Copy(file, dest, false); } catch { }
                            }
                        }
                    }

                    return dir;
                }
                catch
                {
                    string fallback = Path.Combine(Directory.GetCurrentDirectory(), "logs", "activities");
                    Directory.CreateDirectory(fallback);
                    _activitiesDirectory = fallback;
                    return fallback;
                }
            }
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
            // トラックポイントが0件でも有効座標がある場合は現在位置を追加して確実に保存可能にする
            if (_sessionManager.PointCount == 0 && _hasValidPosition)
            {
                (double pixelX, double pixelY) = _coordEngine.WorldToPixel(_lastValidPositionX, _lastValidPositionZ);
                (double lat, double lon) = _coordEngine.WorldToGps(_lastValidPositionX, _lastValidPositionZ);
                _sessionManager.AddTrackPoint(lat, lon, pixelX, pixelY, _elevationTracker.CurrentElevation, 0.0, 0.0, _currentPower, _currentCadence, _currentHeartRate, _filteredGrade, _elevationTracker.TotalDistanceMeters / 1000.0);
            }

            var summary = _sessionManager.StopAndExport(ActivitiesDirectory, _elevationTracker.TotalDistanceMeters / 1000.0, _elevationTracker.ElevationGain, _config.RiderWeightKg);
            if (!string.IsNullOrEmpty(summary.SavedFilePath))
            {
                Log($"[SESSION] Finished. Saved to: {summary.SavedFilePath}");
                OnSessionSaved?.Invoke(summary);
            }
            else
            {
                Log("[SESSION] Finished. (No movement detected, session export skipped)");
                OnSessionSaved?.Invoke(summary);
            }

            // 保存完了後、トラッカーを初期化して次のセッションの自動記録を即時開始
            _elevationTracker.Reset();
            _sessionManager.Start();
            Log("[SESSION] Auto-recording restarted for the next activity.");
        }

        /// <summary>
        /// 軌跡をクリアし、直前のセッションを安全に保存した上で新しい記録を即座に開始します
        /// </summary>
        public SessionSummary? ClearTrackAndRestart()
        {
            SessionSummary? summary = null;
            if (_sessionManager.PointCount > 0 || _elevationTracker.TotalDistanceMeters > 0)
            {
                summary = _sessionManager.StopAndExport(ActivitiesDirectory, _elevationTracker.TotalDistanceMeters / 1000.0, _elevationTracker.ElevationGain, _config.RiderWeightKg);
                if (!string.IsNullOrEmpty(summary.SavedFilePath))
                {
                    Log($"[SESSION] Saved previous session before clearing: {summary.SavedFilePath}");
                    OnSessionSaved?.Invoke(summary);
                }
                else
                {
                    Log("[SESSION] Cleared track. (Distance was 0 km, session export skipped)");
                }
            }

            _elevationTracker.Reset();
            _sessionManager.Start();
            Log("[SESSION] Cleared track and automatically started a new session.");
            return summary;
        }

        public void ClearTrack()
        {
            ClearTrackAndRestart();
        }

        public List<SessionHistoryItem> GetHistoryList()
        {
            return SessionHistoryManager.GetHistoryList(ActivitiesDirectory);
        }

        public string? GetHistoryDetailJson(string fileName)
        {
            return SessionHistoryManager.GetHistoryDetailJson(ActivitiesDirectory, fileName);
        }

        public bool DeleteHistory(string fileName)
        {
            return SessionHistoryManager.DeleteHistory(ActivitiesDirectory, fileName);
        }

        #region ワークアウト管理・制御API

        public bool IsWorkoutActive => _workoutEngine.IsActive;

        public List<WorkoutSummaryItem> GetWorkoutLibrary()
        {
            return _workoutLibrary.GetLibrary();
        }

        public WorkoutDefinition? LoadWorkoutDetail(string filePath)
        {
            return _workoutLibrary.LoadWorkout(filePath);
        }

        public List<string> ImportWorkoutFiles(IEnumerable<string> filePaths)
        {
            var res = _workoutLibrary.ImportFiles(filePaths);
            Log($"[WORKOUT] Imported {res.Count} workout file(s).");
            return res;
        }

        public bool StartWorkout(string filePath)
        {
            var def = _workoutLibrary.LoadWorkout(filePath);
            if (def == null || def.Segments.Count == 0)
            {
                Log($"[WORKOUT-ERR] Failed to load workout: {filePath}");
                return false;
            }

            _workoutEngine.Start(def, _config.Ftp);
            Log($"[WORKOUT] Started: \"{def.Title}\" ({def.TotalDurationSec / 60}m {def.TotalDurationSec % 60}s, {def.Segments.Count} blocks). ERG mode enabled.");
            PushCurrentSnapshot();
            return true;
        }

        public void PauseWorkout()
        {
            _workoutEngine.Pause();
            Log("[WORKOUT] Workout paused.");
            PushCurrentSnapshot();
        }

        public void ResumeWorkout()
        {
            _workoutEngine.Resume();
            Log("[WORKOUT] Workout resumed.");
            PushCurrentSnapshot();
        }

        public void StopWorkout()
        {
            if (_workoutEngine.IsActive)
            {
                _workoutEngine.Stop();
                // 勾配モードへ復帰するため抵抗リセット
                if (_isBleConnected && _ftmsClient != null)
                {
                    _ = _ftmsClient.SetTargetResistanceLevelAsync(0);
                    _lastSentGrade = 999.0;
                }
                Log("[WORKOUT] Stopped workout. Restored normal SIM mode.");
                PushCurrentSnapshot();
            }
        }

        public void SkipWorkoutStep()
        {
            _workoutEngine.SkipStep();
            Log("[WORKOUT] Skipped to next step.");
            PushCurrentSnapshot();
        }

        public void PreviousWorkoutStep()
        {
            _workoutEngine.PreviousStep();
            Log("[WORKOUT] Returned to previous step.");
            PushCurrentSnapshot();
        }

        public void SetWorkoutIntensityBias(double bias)
        {
            _workoutEngine.SetIntensityBias(bias);
            Log($"[WORKOUT] Intensity bias set to {bias * 100:F0}%.");
            PushCurrentSnapshot();
        }

        public WorkoutStateSnapshot GetWorkoutSnapshot()
        {
            return _workoutEngine.CreateSnapshot();
        }

        /// <summary>
        /// ランプテスト（FTP自動計測モード）を開始
        /// </summary>
        public bool StartRampTest()
        {
            // ライブラリから Ramp Test を検索
            var library = _workoutLibrary.GetLibrary();
            var rampItem = library.FirstOrDefault(w => w.Title.Contains("Ramp Test", StringComparison.OrdinalIgnoreCase) || (w.Tags != null && w.Tags.Contains("RampTest")));

            WorkoutDefinition? def = null;
            if (rampItem != null && File.Exists(rampItem.FilePath))
            {
                def = _workoutLibrary.LoadWorkout(rampItem.FilePath);
            }

            if (def == null)
            {
                // ライブラリに見つからない場合は標準ランプテスト（100W開始、毎分+20W）を動的生成
                def = WorkoutLibraryManager.CreateStandardRampTest(_config.Ftp);
            }

            def.IsRampTest = true;
            _workoutEngine.Start(def, _config.Ftp);
            Log($"[RAMP-TEST] ⚡ ランプテスト開始: \"{def.Title}\" (ウォームアップ後 毎分+20W自動上昇、ケイデンス50rpm以下で自動終了)");
            PushCurrentSnapshot();
            return true;
        }

        /// <summary>
        /// ランプテストで測定された新FTPを設定ファイルに保存・適用
        /// </summary>
        public void ApplyRampTestFtp(double newFtp)
        {
            if (newFtp < 50 || newFtp > 600) return;
            SetFtp(newFtp);
            Log($"[RAMP-TEST] 💾 新FTP {newFtp:F0}W を設定(config.json)に保存・適用しました。");
            PushCurrentSnapshot();
        }

        #endregion

        public async Task StartAsync()
        {
            // vJoy 初期化
            _vjoy.Initialize();

            // UDP受信パケットハンドラ登録
            _udpReceiver.OnPacketReceived += HandlePacketReceived;
            _udpReceiver.Start();
            Log("[SYSTEM] HorizonCycling core service started.");

            // アプリ起動時の自動記録開始
            _sessionManager.Start();
            Log("[SESSION] Auto-recording initialized. Ready to ride.");

            // アイドル監視タイマー（Forza未起動時でもステータスや心拍を1秒ごとに配信）
            _idleTimer = new System.Threading.Timer(OnIdleTimerTick, null, 1000, 1000);

            // 起動直後の初期スナップショット配信
            PushCurrentSnapshot();

            // BLE接続（バックグラウンド非同期）
            _ = Task.Run(async () =>
            {
                await ConnectConfiguredSensorAsync();
            });
            _ = Task.Run(async () =>
            {
                await ConnectConfiguredHeartRateAsync();
            });
            _ = Task.Run(async () =>
            {
                await ConnectConfiguredCadenceAsync();
            });
        }

        private void OnIdleTimerTick(object? state)
        {
            if (_isDisposed) return;

            // 直近1秒以上テレメトリを受信していない待機時に定期スナップショット配信
            if ((DateTime.UtcNow - _lastPacketTime).TotalMilliseconds >= 1000)
            {
                if (_workoutEngine.IsActive)
                {
                    _workoutEngine.Update(1.0, _config.Ftp, _currentPower, _currentCadence);
                }

                // Forza未起動時でもローラーを漕いでいる場合は移動時間とカロリーを積算
                if (_currentPower > 0)
                {
                    _sessionManager.UpdateMovingState(true, 1.0, _currentPower, 0.0, _config.RiderWeightKg);
                }

                PushCurrentSnapshot();
            }
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
                            Category = "cadence",
                            IsPaired = true
                        });
                    }
                }

                var cscSelector = Windows.Devices.Bluetooth.GenericAttributeProfile.GattDeviceService.GetDeviceSelectorFromUuid(Guid.Parse("00001816-0000-1000-8000-00805f9b34fb"));
                var cscDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(cscSelector);
                foreach (var d in cscDevices)
                {
                    var bleDevice = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(d.Id);
                    if (bleDevice != null && !foundDevices.Any(x => x.Address == bleDevice.BluetoothAddress))
                    {
                        foundDevices.Add(new BleDeviceInfo
                        {
                            Address = bleDevice.BluetoothAddress,
                            Name = string.IsNullOrEmpty(bleDevice.Name) ? "Cadence Sensor" : bleDevice.Name,
                            TypeName = "Cadence Sensor",
                            SensorTypeInt = 2,
                            Category = "cadence",
                            IsPaired = true
                        });
                    }
                }

                var hrSelector = Windows.Devices.Bluetooth.GenericAttributeProfile.GattDeviceService.GetDeviceSelectorFromUuid(Guid.Parse("0000180d-0000-1000-8000-00805f9b34fb"));
                var hrDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(hrSelector);
                foreach (var d in hrDevices)
                {
                    var bleDevice = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(d.Id);
                    if (bleDevice != null && !foundDevices.Any(x => x.Address == bleDevice.BluetoothAddress))
                    {
                        foundDevices.Add(new BleDeviceInfo
                        {
                            Address = bleDevice.BluetoothAddress,
                            Name = string.IsNullOrEmpty(bleDevice.Name) ? "Heart Rate Monitor" : bleDevice.Name,
                            TypeName = "Heart Rate Monitor",
                            SensorTypeInt = 3,
                            Category = "heartrate",
                            IsPaired = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[SETUP] Paired query warning: {ex.Message}");
            }

            // 2. Advertisement Watcher (Activeスキャン & フィルタなしで確実に検出)
            try
            {
                var watcher = new Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher
                {
                    ScanningMode = Windows.Devices.Bluetooth.Advertisement.BluetoothLEScanningMode.Active
                };

                watcher.Received += (s, e) =>
                {
                    lock (foundDevices)
                    {
                        string localName = e.Advertisement.LocalName ?? string.Empty;
                        bool hasHrUuid = e.Advertisement.ServiceUuids.Any(u => u == Guid.Parse("0000180d-0000-1000-8000-00805f9b34fb") || u.ToString().StartsWith("0000180d", StringComparison.OrdinalIgnoreCase));
                        bool hasFtmsUuid = e.Advertisement.ServiceUuids.Any(u => u == Guid.Parse("00001826-0000-1000-8000-00805f9b34fb") || u.ToString().StartsWith("00001826", StringComparison.OrdinalIgnoreCase));
                        bool hasPowerUuid = e.Advertisement.ServiceUuids.Any(u => u == Guid.Parse("00001818-0000-1000-8000-00805f9b34fb") || u.ToString().StartsWith("00001818", StringComparison.OrdinalIgnoreCase));
                        bool hasCscUuid = e.Advertisement.ServiceUuids.Any(u => u == Guid.Parse("00001816-0000-1000-8000-00805f9b34fb") || u.ToString().StartsWith("00001816", StringComparison.OrdinalIgnoreCase));

                        string upperName = localName.ToUpperInvariant();

                        bool isPowerOrCadenceByName = upperName.Contains("PES") ||
                                                      upperName.Contains("P505") ||
                                                      upperName.Contains("P325") ||
                                                      upperName.Contains("P500") ||
                                                      upperName.Contains("P300") ||
                                                      upperName.Contains("PWR") ||
                                                      upperName.Contains("POWER") ||
                                                      upperName.Contains("CADENCE") ||
                                                      upperName.Contains("RPM") ||
                                                      upperName.Contains("BK467") ||
                                                      upperName.Contains("S3+") ||
                                                      upperName.Contains("SPD/CAD") ||
                                                      upperName.Contains("ASSIOMA") ||
                                                      upperName.Contains("STAGES") ||
                                                      upperName.Contains("4IIII") ||
                                                      upperName.Contains("ROTOR") ||
                                                      upperName.Contains("FAVERO");

                        bool isMageneDevice = upperName.Contains("MAGENE");

                        bool isHrByName = upperName.Contains("HR") ||
                                          upperName.Contains("HEART") ||
                                          upperName.Contains("PULSE") ||
                                          upperName.Contains("POLAR") ||
                                          upperName.Contains("TICKR") ||
                                          upperName.Contains("COOSPO") ||
                                          upperName.Contains("GARMIN") ||
                                          upperName.Contains("H64") ||
                                          upperName.Contains("H303") ||
                                          upperName.Contains("H808") ||
                                          upperName.Contains("H603") ||
                                          upperName.Contains("H10") ||
                                          upperName.Contains("H9") ||
                                          upperName.Contains("H7") ||
                                          upperName.Contains("OH1") ||
                                          upperName.Contains("VERITY") ||
                                          upperName.Contains("STRAP") ||
                                          upperName.Contains("BAND") ||
                                          upperName.Contains("CHEST") ||
                                          upperName.Contains("COROS") ||
                                          upperName.Contains("DUAL");

                        bool isTrainerByName = upperName.Contains("TACX") ||
                                               upperName.Contains("KICKR") ||
                                               upperName.Contains("WAHOO") ||
                                               upperName.Contains("FLUX") ||
                                               upperName.Contains("NEO") ||
                                               upperName.Contains("SARIS") ||
                                               upperName.Contains("DIRETO") ||
                                               upperName.Contains("SUITO") ||
                                               upperName.Contains("THINKRIDER") ||
                                               upperName.Contains("T2") ||
                                               upperName.Contains("CYCLOPS") ||
                                               upperName.Contains("TRAINER");

                        // カテゴリ決定（UUIDを最優先、次に明確なキーワードで判定）
                        string category;
                        int typeInt;
                        string typeName;

                        if (hasPowerUuid || hasCscUuid || isPowerOrCadenceByName || (isMageneDevice && !isHrByName))
                        {
                            category = "cadence";
                            typeInt = 2;
                            typeName = (upperName.Contains("PES") || upperName.Contains("P505") || upperName.Contains("POWER") || upperName.Contains("PWR") || hasPowerUuid)
                                ? "Cycling Power Meter (P505等)"
                                : "Cadence Sensor";
                        }
                        else if (hasFtmsUuid || isTrainerByName)
                        {
                            category = "trainer";
                            typeInt = 1;
                            typeName = "FTMS Smart Trainer";
                        }
                        else if (hasHrUuid || isHrByName)
                        {
                            category = "heartrate";
                            typeInt = 3;
                            typeName = "Heart Rate Monitor";
                        }
                        else if (!string.IsNullOrEmpty(localName))
                        {
                            category = "other";
                            typeInt = 0;
                            typeName = "Bluetooth Device";
                        }
                        else
                        {
                            // 名前も該当UUIDもないパケットはスキップ
                            return;
                        }

                        string finalName = !string.IsNullOrEmpty(localName) ? localName : typeName;

                        // 既存デバイスの更新 (Scan Responseで名前やUUIDが届いた場合など)
                        var existing = foundDevices.FirstOrDefault(d => d.Address == e.BluetoothAddress);
                        if (existing != null)
                        {
                            if (!string.IsNullOrEmpty(localName) && (existing.Name == "Unknown Device" || existing.Name == "Heart Rate Monitor" || existing.Name == "FTMS Trainer" || existing.Name == "Power Meter" || existing.Name == "Cadence Sensor"))
                            {
                                existing.Name = localName;
                            }
                            existing.Category = category;
                            existing.SensorTypeInt = typeInt;
                            existing.TypeName = typeName;
                            return;
                        }

                        foundDevices.Add(new BleDeviceInfo
                        {
                            Address = e.BluetoothAddress,
                            Name = finalName,
                            TypeName = typeName,
                            SensorTypeInt = typeInt,
                            Category = category,
                            IsPaired = false
                        });
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

            Log($"[BLE] Target trainer/power device changed to {name} ({macAddress:X}). Connecting...");
            bool res = await ConnectConfiguredSensorAsync();
            PushCurrentSnapshot();
            return res;
        }

        public async Task<bool> ConnectConfiguredSensorAsync()
        {
            try
            {
                if (_config.PowerSourceType == SensorType.Ftms)
                {
                    _ftmsClient = new FtmsClient();
                    _ftmsClient.OnStatusMessage += msg => Log($"[BLE] {msg}");
                    _ftmsClient.OnPowerReceived += power =>
                    {
                        _ftmsPower = power;
                        // ケイデンス/パワーメーター（P505）が接続されており、PreferPowerMeterWattsがtrueならパワーメーターの数値を優先
                        if (_cadenceClient == null || !_isCadenceConnected || !_config.PreferPowerMeterWatts)
                        {
                            _currentPower = power;
                        }
                        if ((DateTime.UtcNow - _lastPacketTime).TotalMilliseconds >= 500)
                        {
                            PushCurrentSnapshot();
                        }
                    };
                    _ftmsClient.OnCadenceReceived += cadence =>
                    {
                        _ftmsCadence = cadence;
                        // ケイデンス/パワーメーター（P505）が接続されていない場合のみ、トレーナーのケイデンスを採用
                        if (_cadenceClient == null || !_isCadenceConnected)
                        {
                            _currentCadence = cadence;
                        }
                    };
                    _ftmsClient.OnSpeedReceived += speed => _trainerSpeedKmh = speed;

                    string displayName = string.IsNullOrEmpty(_config.PowerSourceName) ? $"{_config.PowerSourceMacAddress:X}" : _config.PowerSourceName;
                    _bleStatus = $"Connecting ({displayName})...";
                    PushCurrentSnapshot();

                    _isBleConnected = await _ftmsClient.ScanAndConnectAsync(20000, _config.PowerSourceMacAddress);
                    if (_isBleConnected)
                    {
                        await _ftmsClient.SetTargetResistanceLevelAsync(0);
                        _lastSentGrade = 0.0;
                        _bleStatus = $"Connected ({displayName})";
                        Log($"[BLE] Smart trainer connected: {displayName} (Resistance: Level 0).");
                        PushCurrentSnapshot();
                        return true;
                    }
                    else
                    {
                        _bleStatus = "Connection Failed (FTMS)";
                        PushCurrentSnapshot();
                        return false;
                    }
                }
                else if (_config.PowerSourceType == SensorType.CyclingPower)
                {
                    _powerClient = new CyclingPowerClient();
                    _powerClient.OnStatusMessage += msg => Log($"[BLE] {msg}");
                    _powerClient.OnPowerReceived += power =>
                    {
                        _currentPower = power;
                        if ((DateTime.UtcNow - _lastPacketTime).TotalMilliseconds >= 500)
                        {
                            PushCurrentSnapshot();
                        }
                    };
                    _powerClient.OnCadenceReceived += cadence =>
                    {
                        _currentCadence = cadence;
                    };

                    string displayName = string.IsNullOrEmpty(_config.PowerSourceName) ? $"{_config.PowerSourceMacAddress:X}" : _config.PowerSourceName;
                    _bleStatus = $"Connecting ({displayName})...";
                    PushCurrentSnapshot();

                    _isBleConnected = await _powerClient.ScanAndConnectAsync(20000, _config.PowerSourceMacAddress);
                    if (_isBleConnected)
                    {
                        _bleStatus = $"Connected ({displayName})";
                        Log($"[BLE] Cycling Power Meter connected: {displayName}.");
                        PushCurrentSnapshot();
                        return true;
                    }
                    else
                    {
                        _bleStatus = "Connection Failed (PowerMeter)";
                        PushCurrentSnapshot();
                        return false;
                    }
                }
                else
                {
                    _bleStatus = "Not Configured (Fallback 0W)";
                    PushCurrentSnapshot();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"[BLE-ERR] Connection error: {ex.Message}");
                _bleStatus = "Error";
                PushCurrentSnapshot();
                return false;
            }
        }

        // =========================================================================
        // セカンダリ ケイデンス / パワーメーター (Magene PES-P505 等) 接続管理
        // =========================================================================

        public async Task<bool> ConnectCadenceSensorAsync(ulong macAddress, string name)
        {
            _cadenceClient?.Disconnect();
            _cadenceClient?.Dispose();
            _cadenceClient = null;

            _isCadenceConnected = false;
            _config.CadenceSourceMacAddress = macAddress;
            _config.CadenceSourceName = name;
            _config.CadenceSourceType = SensorType.CyclingPower;
            _config.CadenceSourceEnabled = true;
            ConfigManager.Save(_config);

            Log($"[CADENCE] Target Cadence/Power Meter set to {name} ({macAddress:X}). Connecting...");
            bool res = await ConnectConfiguredCadenceAsync();
            PushCurrentSnapshot();
            return res;
        }

        public void DisconnectCadenceSensor()
        {
            _cadenceClient?.Disconnect();
            _cadenceClient?.Dispose();
            _cadenceClient = null;

            _isCadenceConnected = false;
            _cadenceStatus = "Disconnected";
            _cadenceSensorCadence = 0.0;
            _currentCadence = _ftmsCadence;
            _config.CadenceSourceMacAddress = 0;
            _config.CadenceSourceName = "";
            _config.CadenceSourceEnabled = false;
            ConfigManager.Save(_config);
            Log("[CADENCE] Cadence/Power Meter disconnected and unconfigured.");
            PushCurrentSnapshot();
        }

        public async Task<bool> ConnectConfiguredCadenceAsync()
        {
            try
            {
                if (!_config.CadenceSourceEnabled || _config.CadenceSourceMacAddress == 0)
                {
                    _cadenceStatus = "Not Configured";
                    PushCurrentSnapshot();
                    return false;
                }

                _cadenceClient?.Disconnect();
                _cadenceClient?.Dispose();
                _cadenceClient = new CyclingPowerClient();

                _cadenceClient.OnStatusMessage += msg => Log(msg);
                _cadenceClient.OnCadenceReceived += cadence =>
                {
                    _cadenceSensorCadence = cadence;
                    _currentCadence = cadence; // ケイデンスセンサーからの値を最優先で反映
                    if ((DateTime.UtcNow - _lastPacketTime).TotalMilliseconds >= 500)
                    {
                        PushCurrentSnapshot();
                    }
                };
                _cadenceClient.OnPowerReceived += power =>
                {
                    _cadencePower = power;
                    // PreferPowerMeterWatts が有効な場合、パワー値もP505を優先採用
                    if (_config.PreferPowerMeterWatts)
                    {
                        _currentPower = power;
                    }
                    if ((DateTime.UtcNow - _lastPacketTime).TotalMilliseconds >= 500)
                    {
                        PushCurrentSnapshot();
                    }
                };
                _cadenceClient.OnConnectionStateChanged += connected =>
                {
                    _isCadenceConnected = connected;
                    string devName = string.IsNullOrEmpty(_config.CadenceSourceName) ? $"{_config.CadenceSourceMacAddress:X}" : _config.CadenceSourceName;
                    _cadenceStatus = connected ? $"Connected ({devName})" : "Disconnected";
                    PushCurrentSnapshot();
                };

                string displayName = string.IsNullOrEmpty(_config.CadenceSourceName) ? $"{_config.CadenceSourceMacAddress:X}" : _config.CadenceSourceName;
                _cadenceStatus = $"Connecting ({displayName})...";
                PushCurrentSnapshot();

                _isCadenceConnected = await _cadenceClient.ScanAndConnectAsync(20000, _config.CadenceSourceMacAddress);
                if (_isCadenceConnected)
                {
                    _cadenceStatus = $"Connected ({displayName})";
                    Log($"[CADENCE] Cadence/Power Meter connected: {displayName}.");
                    PushCurrentSnapshot();
                    return true;
                }
                else
                {
                    _cadenceStatus = "Connection Failed";
                    PushCurrentSnapshot();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"[CADENCE-ERR] Connection error: {ex.Message}");
                _cadenceStatus = "Error";
                PushCurrentSnapshot();
                return false;
            }
        }

        public void SetPreferPowerMeterWatts(bool prefer)
        {
            _config.PreferPowerMeterWatts = prefer;
            ConfigManager.Save(_config);
            Log($"[CONFIG] Prefer Power Meter Watts set to {prefer}.");
        }

        public async Task<bool> ConnectHeartRateSensorAsync(ulong macAddress, string name)
        {
            _heartRateClient?.Disconnect();
            _heartRateClient?.Dispose();
            _heartRateClient = null;

            _isHrConnected = false;
            _config.HeartRateSourceMacAddress = macAddress;
            _config.HeartRateSourceName = name;
            _config.HeartRateEnabled = true;
            ConfigManager.Save(_config);

            Log($"[HRM] Target Heart Rate device set to {name} ({macAddress:X}). Connecting...");
            bool res = await ConnectConfiguredHeartRateAsync();
            PushCurrentSnapshot();
            return res;
        }

        public void DisconnectHeartRateSensor()
        {
            _heartRateClient?.Disconnect();
            _heartRateClient?.Dispose();
            _heartRateClient = null;

            _isHrConnected = false;
            _hrStatus = "Disconnected";
            _currentHeartRate = 0.0;
            _config.HeartRateSourceMacAddress = 0;
            _config.HeartRateSourceName = "";
            ConfigManager.Save(_config);
            Log("[HRM] Heart Rate Monitor disconnected and unconfigured.");
            PushCurrentSnapshot();
        }

        public async Task<bool> ConnectConfiguredHeartRateAsync()
        {
            try
            {
                if (!_config.HeartRateEnabled || _config.HeartRateSourceMacAddress == 0)
                {
                    _hrStatus = "Not Configured";
                    PushCurrentSnapshot();
                    return false;
                }

                _heartRateClient?.Disconnect();
                _heartRateClient?.Dispose();
                _heartRateClient = new HeartRateClient();

                _heartRateClient.OnStatusMessage += msg => Log(msg);
                _heartRateClient.OnHeartRateReceived += hr =>
                {
                    _currentHeartRate = hr;
                    if ((DateTime.UtcNow - _lastPacketTime).TotalMilliseconds >= 500)
                    {
                        PushCurrentSnapshot();
                    }
                };
                _heartRateClient.OnConnectionStateChanged += connected =>
                {
                    _isHrConnected = connected;
                    string devName = string.IsNullOrEmpty(_config.HeartRateSourceName) ? $"{_config.HeartRateSourceMacAddress:X}" : _config.HeartRateSourceName;
                    _hrStatus = connected ? $"Connected ({devName})" : "Disconnected";
                    PushCurrentSnapshot();
                };

                string displayName = string.IsNullOrEmpty(_config.HeartRateSourceName) ? $"{_config.HeartRateSourceMacAddress:X}" : _config.HeartRateSourceName;
                _hrStatus = $"Connecting ({displayName})...";
                PushCurrentSnapshot();

                _isHrConnected = await _heartRateClient.ScanAndConnectAsync(20000, _config.HeartRateSourceMacAddress);
                if (_isHrConnected)
                {
                    _hrStatus = $"Connected ({displayName})";
                    Log($"[HRM] Heart Rate Monitor connected: {displayName}.");
                    PushCurrentSnapshot();
                    return true;
                }
                else
                {
                    _hrStatus = "Connection Failed";
                    PushCurrentSnapshot();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"[HRM-ERR] Connection error: {ex.Message}");
                _hrStatus = "Error";
                PushCurrentSnapshot();
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

            if (_workoutEngine.IsActive)
            {
                _workoutEngine.Update(deltaSec, _config.Ftp, _currentPower, _currentCadence);
            }

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
            _isLastPositionValid = isPositionValid;

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
                        _isNextPointTeleport = true;
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

                // オートポーズ判定（車速 >= 1.0 km/h かつ 有効座標 かつ 非ファストトラベル）
                bool isMoving = packet.SpeedKmh >= 1.0f && !isFastTravel;
                _sessionManager.UpdateMovingState(isMoving, deltaSec, _currentPower, packet.SpeedKmh, _config.RiderWeightKg);

                // セッショントラックポイント記録 (走行中かつ有効座標のみ)
                double currentTargetSpeed = (_strategy as SimulationMappingStrategy)?.TargetSpeedKmh ?? 0.0;
                bool isPtTeleport = _isNextPointTeleport;
                _sessionManager.AddTrackPoint(lat, lon, pixelX, pixelY, _elevationTracker.CurrentElevation, packet.SpeedKmh, currentTargetSpeed, _currentPower, _currentCadence, _currentHeartRate, _filteredGrade, _elevationTracker.TotalDistanceMeters / 1000.0, isPtTeleport);
                if (isPtTeleport) _isNextPointTeleport = false;
            }
            else
            {
                // メニュー中や停止中は移動フラグをfalseにしてオートポーズ
                _sessionManager.UpdateMovingState(false, deltaSec, _currentPower, 0.0, _config.RiderWeightKg);
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

                // もし初回スタート地点がまだ記録されていなければ、停止中であっても現在地点をスタート地点として記録
                if (_sessionManager.PointCount == 0 && _hasValidPosition)
                {
                    _sessionManager.AddTrackPoint(lat, lon, pixelX, pixelY, _elevationTracker.CurrentElevation, 0.0, 0.0, _currentPower, _currentCadence, _currentHeartRate, _filteredGrade, 0.0);
                }
            }

            // スマートローラー負荷フィードバック (ワークアウト中はERGモード固定のため勾配送信をスキップ)
            if (_isBleConnected && _ftmsClient != null && _ftmsClient.IsConnected && !_workoutEngine.IsActive)
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

            _lastThrottle = control.Throttle;
            _lastBrake = control.Brake;

            // スナップショット通知
            PushCurrentSnapshot();
        }

        public BridgeStateSnapshot GetCurrentSnapshot()
        {
            double targetSpeedKmh = (_strategy is SimulationMappingStrategy sim) ? sim.TargetSpeedKmh : 0.0;
            bool isTelemetryActive = (DateTime.UtcNow - _lastPacketTime).TotalMilliseconds < 1500;
            var packet = _lastPacket;

            return new BridgeStateSnapshot
            {
                ModeName = _strategy is ArcadeMappingStrategy ? "ARCADE MODE" : "SIMULATION MODE",
                Mode = _strategy is ArcadeMappingStrategy ? 1 : 2,
                BleStatus = _bleStatus,
                ConnectedDeviceName = string.IsNullOrEmpty(_config.PowerSourceName) ? "None" : _config.PowerSourceName,
                IsBleConnected = _isBleConnected,

                IsHrConnected = _isHrConnected,
                HrStatus = _hrStatus,
                ConnectedHrDeviceName = string.IsNullOrEmpty(_config.HeartRateSourceName) ? "None" : _config.HeartRateSourceName,

                IsCadenceConnected = _isCadenceConnected,
                CadenceStatus = _cadenceStatus,
                ConnectedCadenceDeviceName = string.IsNullOrEmpty(_config.CadenceSourceName) ? "None" : _config.CadenceSourceName,

                IsVJoyActive = _vjoy.IsAcquired,
                IsTelemetryActive = isTelemetryActive,
                PedalBrakeEnabled = _strategy.PedalBrakeEnabled,
                Difficulty = _trainerDifficulty,
                Ftp = _config.Ftp,
                RiderWeightKg = _config.RiderWeightKg,

                Power = _currentPower,
                Wkg = _config.RiderWeightKg > 0 ? Math.Round(_currentPower / _config.RiderWeightKg, 1) : 0.0,
                CaloriesKcal = _sessionManager.CurrentCaloriesKcal,
                Cadence = _currentCadence,
                HeartRate = _currentHeartRate,
                TargetSpeedKmh = targetSpeedKmh,
                CarSpeedKmh = (isTelemetryActive && packet != null) ? packet.SpeedKmh : 0f,
                RawGrade = isTelemetryActive ? _filteredGrade : 0.0,
                SentGrade = _lastSentGrade == 999.0 ? 0.0 : _lastSentGrade,
                Throttle = _lastThrottle,
                Brake = _lastBrake,
                Gear = (isTelemetryActive && packet != null) ? packet.Gear : (byte)0,

                PositionX = _lastValidPositionX,
                PositionY = _lastValidPositionY,
                PositionZ = _lastValidPositionZ,
                Yaw = _lastValidYaw,
                MapPixelX = _lastValidPixelX,
                MapPixelY = _lastValidPixelY,
                Latitude = _lastValidLat,
                Longitude = _lastValidLon,
                IsPositionValid = isTelemetryActive ? _isLastPositionValid : _hasValidPosition,
                IsTeleport = isTelemetryActive ? _lastIsTeleport : false,

                ElevationMeters = _elevationTracker.CurrentElevation,
                ElevationGainMeters = _elevationTracker.ElevationGain,
                TotalDistanceKm = _elevationTracker.TotalDistanceMeters / 1000.0,

                SessionState = _sessionManager.State.ToString(),
                ElapsedSeconds = _sessionManager.ElapsedTime.TotalSeconds,

                IsWorkoutActive = _workoutEngine.IsActive,
                Workout = _workoutEngine.IsActive ? _workoutEngine.CreateSnapshot() : null
            };
        }

        public void PushCurrentSnapshot()
        {
            try
            {
                var snapshot = GetCurrentSnapshot();
                OnStateUpdated?.Invoke(snapshot);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PushCurrentSnapshotErr] {ex.Message}");
            }
        }

        private void Log(string message)
        {
            OnLogMessage?.Invoke(message);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _idleTimer?.Dispose();

            // アプリケーション終了時に有効な走行データがあれば自動保存
            try
            {
                if (_sessionManager.PointCount > 0 || _elevationTracker.TotalDistanceMeters > 0)
                {
                    var summary = _sessionManager.StopAndExport(ActivitiesDirectory, _elevationTracker.TotalDistanceMeters / 1000.0, _elevationTracker.ElevationGain, _config.RiderWeightKg);
                    if (!string.IsNullOrEmpty(summary.SavedFilePath))
                    {
                        Log($"[SESSION] Auto-saved on exit: {summary.SavedFilePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SESSION] Error saving on exit: {ex.Message}");
            }

            _udpReceiver.Stop();
            _vjoy.Dispose();
            _ftmsClient?.Dispose();
            _powerClient?.Dispose();
            _heartRateClient?.Dispose();
            _cadenceClient?.Dispose();
        }
    }
}
