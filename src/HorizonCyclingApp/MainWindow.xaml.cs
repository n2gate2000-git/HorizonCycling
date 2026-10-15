using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using HorizonCyclingBridge.Core;
using Microsoft.Web.WebView2.Core;

namespace HorizonCyclingApp
{
    public partial class MainWindow : Window
    {
        private BridgeService? _bridgeService;
        private WorkoutOverlayWindow? _overlayWindow;
        private DateTime _lastUiUpdateTime = DateTime.MinValue;
        private const int UI_UPDATE_INTERVAL_MS = 33; // 約30HzでUIへスナップショット送信

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. WebView2 ランタイムの初期化
                await webView.EnsureCoreWebView2Async();

                // 2. 仮想ホスト名のフォルダマッピング設定
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string wwwrootDir = Path.Combine(baseDir, "wwwroot");

                // 開発環境時のフォールバック
                if (!Directory.Exists(wwwrootDir))
                {
                    string devWwwroot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "wwwroot"));
                    if (Directory.Exists(devWwwroot))
                    {
                        wwwrootDir = devWwwroot;
                    }
                }

                // マップおよびタイルキャッシュフォルダの探索（配布フォルダ・開発環境の両対応）
                string[] candidateMapDirs = new[]
                {
                    Path.Combine(baseDir, "map"),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "map")),
                    Path.Combine(Directory.GetCurrentDirectory(), "map"),
                    @"d:\develop\HorizonCycling\map"
                };
                string mapDir = candidateMapDirs.FirstOrDefault(d => Directory.Exists(d)) ?? Path.Combine(baseDir, "map");

                string[] candidateTileDirs = new[]
                {
                    Path.Combine(mapDir, ".tile_cache"),
                    Path.Combine(baseDir, "map", ".tile_cache"),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "map", ".tile_cache")),
                    @"d:\develop\HorizonCycling\map\.tile_cache"
                };
                string tileCacheDir = candidateTileDirs.FirstOrDefault(d => Directory.Exists(d)) ?? Path.Combine(mapDir, ".tile_cache");

                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "horizoncycling.local", 
                    wwwrootDir, 
                    CoreWebView2HostResourceAccessKind.Allow
                );

                if (Directory.Exists(tileCacheDir))
                {
                    webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "tiles.local", 
                        tileCacheDir, 
                        CoreWebView2HostResourceAccessKind.Allow
                    );
                }

                if (Directory.Exists(mapDir))
                {
                    webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "maps.local", 
                        mapDir, 
                        CoreWebView2HostResourceAccessKind.Allow
                    );
                }

                // 3. WebMessage ハンドラ登録
                webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                // 4. バックグラウンド BridgeService の起動
                _bridgeService = new BridgeService();
                _bridgeService.OnStateUpdated += BridgeService_OnStateUpdated;
                _bridgeService.OnLogMessage += BridgeService_OnLogMessage;
                _bridgeService.OnSessionSaved += BridgeService_OnSessionSaved;
                _bridgeService.OnRampTestResult += BridgeService_OnRampTestResult;

                await _bridgeService.StartAsync();

                // 5. ワークアウト最前面オーバーレイウィンドウの事前初期化
                try
                {
                    _overlayWindow = new WorkoutOverlayWindow();
                    _overlayWindow.SetLanguage(_bridgeService.Language);
                    _overlayWindow.OnOverlayCommand += HandleOverlayCommand;
                    // 最初は非表示
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Overlay Init Warning] {ex.Message}");
                }

                // 6. アプリケーションHTMLのロード
                webView.Source = new Uri("https://horizoncycling.local/index.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アプリケーションの初期化中にエラーが発生しました:\n{ex.Message}", "HorizonCycling Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleOverlayCommand(string action)
        {
            Dispatcher.Invoke(() =>
            {
                switch (action)
                {
                    case "pauseWorkout":
                        _bridgeService?.PauseWorkout();
                        break;
                    case "resumeWorkout":
                        _bridgeService?.ResumeWorkout();
                        break;
                    case "skipStep":
                        _bridgeService?.SkipWorkoutStep();
                        break;
                    case "prevStep":
                        _bridgeService?.PreviousWorkoutStep();
                        break;
                    case "stopWorkout":
                        _bridgeService?.StopWorkout();
                        _overlayWindow?.Hide();
                        if (_overlayWindow != null) _overlayWindow.IsUserForcedVisible = false;
                        break;
                    case "stopFreerideSession":
                        _bridgeService?.StopSession();
                        break;
                    case "hideOverlay":
                        if (_overlayWindow != null)
                        {
                            _overlayWindow.IsUserDismissed = true;
                            _overlayWindow.IsUserForcedVisible = false;
                            _overlayWindow.Hide();
                        }
                        break;
                    case "hotkeyStop":
                        if (_bridgeService != null && _bridgeService.GetCurrentSnapshot().IsWorkoutActive)
                        {
                            _bridgeService.StopWorkout();
                            _overlayWindow?.Hide();
                        }
                        else
                        {
                            if (_overlayWindow != null)
                            {
                                _overlayWindow.IsUserDismissed = true;
                                _overlayWindow.IsUserForcedVisible = false;
                                _overlayWindow.Hide();
                            }
                        }
                        break;
                }
            });
        }

        private void BridgeService_OnStateUpdated(BridgeStateSnapshot snapshot)
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastUiUpdateTime).TotalMilliseconds < UI_UPDATE_INTERVAL_MS) return;
            _lastUiUpdateTime = now;

            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    // メインGUIへ状態通知
                    if (webView.CoreWebView2 != null)
                    {
                        string json = JsonSerializer.Serialize(new
                        {
                            type = "state",
                            payload = snapshot
                        });
                        webView.CoreWebView2.PostWebMessageAsJson(json);
                    }

                    // 最前面オーバーレイへ状態通知
                    if (_overlayWindow != null && (_overlayWindow.IsVisible || (!_overlayWindow.IsUserDismissed && snapshot.FreerideOverlayEnabled) || _overlayWindow.IsUserForcedVisible))
                    {
                        _overlayWindow.SendState(snapshot, snapshot.Workout);
                    }
                }
                catch
                {
                    // UI通信中の軽微な例外は握りつぶす
                }
            });
        }

        private void BridgeService_OnLogMessage(string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (webView.CoreWebView2 != null)
                    {
                        string json = JsonSerializer.Serialize(new
                        {
                            type = "log",
                            payload = message
                        });
                        webView.CoreWebView2.PostWebMessageAsJson(json);
                    }
                }
                catch
                {
                }
            });
        }

        private void BridgeService_OnSessionSaved(HorizonCyclingBridge.Telemetry.SessionSummary summary)
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (webView.CoreWebView2 != null)
                    {
                        string json = JsonSerializer.Serialize(new
                        {
                            type = "sessionSaved",
                            payload = summary
                        });
                        webView.CoreWebView2.PostWebMessageAsJson(json);
                    }
                }
                catch
                {
                }
            });
        }

        private void BridgeService_OnRampTestResult(HorizonCyclingBridge.Workout.RampTestResult result)
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (webView.CoreWebView2 != null)
                    {
                        string json = JsonSerializer.Serialize(new
                        {
                            type = "rampTestResult",
                            payload = result
                        });
                        webView.CoreWebView2.PostWebMessageAsJson(json);
                    }
                }
                catch
                {
                }
            });
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                if (!root.TryGetProperty("action", out var actionProp)) return;

                string action = actionProp.GetString() ?? "";

                switch (action)
                {
                    case "ready":
                        SendInitialDataToWeb();
                        break;

                    case "setMode":
                        if (root.TryGetProperty("mode", out var modeProp))
                        {
                            _bridgeService?.SetMode(modeProp.GetInt32());
                        }
                        break;

                    case "setDifficulty":
                        if (root.TryGetProperty("difficulty", out var diffProp))
                        {
                            _bridgeService?.SetDifficulty(diffProp.GetDouble());
                        }
                        break;

                    case "startSession":
                        _bridgeService?.StartSession();
                        break;

                    case "pauseSession":
                        _bridgeService?.PauseSession();
                        break;

                    case "resumeSession":
                        _bridgeService?.ResumeSession();
                        break;

                    case "stopSession":
                        _bridgeService?.StopSession();
                        break;

                    case "clearTrack":
                        _bridgeService?.ClearTrackAndRestart();
                        break;

                    case "getHistoryList":
                        if (_bridgeService != null && webView.CoreWebView2 != null)
                        {
                            var historyList = _bridgeService.GetHistoryList();
                            string listJson = JsonSerializer.Serialize(new
                            {
                                type = "historyList",
                                payload = historyList
                            });
                            webView.CoreWebView2.PostWebMessageAsJson(listJson);
                        }
                        break;

                    case "getHistoryDetail":
                        if (_bridgeService != null && webView.CoreWebView2 != null &&
                            root.TryGetProperty("fileName", out var fnProp))
                        {
                            string fileName = fnProp.GetString() ?? "";
                            string? detailJson = _bridgeService.GetHistoryDetailJson(fileName);
                            if (detailJson != null)
                            {
                                var invZStr = _bridgeService.CoordinateEngine.InvertZ ? "true" : "false";
                                var ox = _bridgeService.CoordinateEngine.WorldOriginX.ToString(System.Globalization.CultureInfo.InvariantCulture);
                                var oz = _bridgeService.CoordinateEngine.WorldOriginZ.ToString(System.Globalization.CultureInfo.InvariantCulture);
                                var sc = _bridgeService.CoordinateEngine.WorldScale.ToString(System.Globalization.CultureInfo.InvariantCulture);
                                string msg = $"{{\"type\":\"historyDetail\",\"mapOriginX\":{ox},\"mapOriginZ\":{oz},\"mapScale\":{sc},\"invertZ\":{invZStr},\"payload\":{detailJson}}}";
                                webView.CoreWebView2.PostWebMessageAsJson(msg);
                            }
                        }
                        break;

                    case "deleteHistory":
                        if (_bridgeService != null && webView.CoreWebView2 != null &&
                            root.TryGetProperty("fileName", out var delProp))
                        {
                            string fileName = delProp.GetString() ?? "";
                            bool success = _bridgeService.DeleteHistory(fileName);
                            string delJson = JsonSerializer.Serialize(new
                            {
                                type = "historyDeleted",
                                fileName = fileName,
                                success = success
                            });
                            webView.CoreWebView2.PostWebMessageAsJson(delJson);
                        }
                        break;

                    case "openActivitiesFolder":
                        if (_bridgeService != null)
                        {
                            string actDir = _bridgeService.ActivitiesDirectory;
                            Directory.CreateDirectory(actDir);
                            Process.Start("explorer.exe", $"\"{actDir}\"");
                        }
                        break;

                    case "updateSettings":
                        if (_bridgeService != null)
                        {
                            if (root.TryGetProperty("ftp", out var ftpProp))
                            {
                                _bridgeService.SetFtp(ftpProp.GetDouble());
                            }
                            if (root.TryGetProperty("weight", out var weightProp))
                            {
                                _bridgeService.SetRiderWeight(weightProp.GetDouble());
                            }
                            if (root.TryGetProperty("difficulty", out var diffSettingProp))
                            {
                                _bridgeService.SetDifficulty(diffSettingProp.GetDouble());
                            }
                            if (root.TryGetProperty("pedalBrake", out var brakeProp))
                            {
                                _bridgeService.SetPedalBrake(brakeProp.GetBoolean());
                            }
                            if (root.TryGetProperty("freerideOverlay", out var foProp))
                            {
                                bool foEnabled = foProp.GetBoolean();
                                _bridgeService.SetFreerideOverlay(foEnabled);
                                if (_overlayWindow != null && foEnabled)
                                {
                                    _overlayWindow.IsUserDismissed = false;
                                }
                            }
                            if (root.TryGetProperty("maxHeartRate", out var maxHrProp))
                            {
                                _bridgeService.SetMaxHeartRate(maxHrProp.GetInt32());
                            }
                            if (root.TryGetProperty("restingHeartRate", out var restHrProp))
                            {
                                _bridgeService.SetRestingHeartRate(restHrProp.GetInt32());
                            }
                            if (root.TryGetProperty("profileRecentDistance", out var distProp))
                            {
                                _bridgeService.SetProfileRecentDistance(distProp.GetInt32());
                            }
                            if (root.TryGetProperty("language", out var langProp))
                            {
                                string lang = langProp.GetString() ?? "ja";
                                _bridgeService.SetLanguage(lang);
                                if (_overlayWindow != null)
                                {
                                    _overlayWindow.SetLanguage(lang);
                                }
                            }
                            SendCurrentConfig();
                        }
                        break;

                    case "setLanguage":
                        if (_bridgeService != null && root.TryGetProperty("language", out var setLangProp))
                        {
                            string newLang = setLangProp.GetString() ?? "ja";
                            _bridgeService.SetLanguage(newLang);
                            if (_overlayWindow != null)
                            {
                                _overlayWindow.SetLanguage(newLang);
                            }
                            SendCurrentConfig();
                        }
                        break;

                    case "setProfileRecentDistance":
                        if (_bridgeService != null && root.TryGetProperty("distance", out var pDistProp))
                        {
                            _bridgeService.SetProfileRecentDistance(pDistProp.GetInt32());
                            SendCurrentConfig();
                        }
                        break;

                    case "openLogFolder":
                        if (root.TryGetProperty("path", out var pathProp))
                        {
                            string path = pathProp.GetString() ?? "";
                            if (!string.IsNullOrEmpty(path))
                            {
                                if (!Path.IsPathRooted(path) && _bridgeService != null)
                                {
                                    path = Path.Combine(_bridgeService.ActivitiesDirectory, path);
                                }
                                if (File.Exists(path))
                                {
                                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                                }
                                else
                                {
                                    string dir = Path.GetDirectoryName(path) ?? "";
                                    if (Directory.Exists(dir))
                                    {
                                        Process.Start("explorer.exe", $"\"{dir}\"");
                                    }
                                    else if (_bridgeService != null && Directory.Exists(_bridgeService.ActivitiesDirectory))
                                    {
                                        Process.Start("explorer.exe", $"\"{_bridgeService.ActivitiesDirectory}\"");
                                    }
                                }
                            }
                        }
                        break;

                    case "calibrateMap":
                        if (_bridgeService != null &&
                            root.TryGetProperty("pixelX", out var pxProp) &&
                            root.TryGetProperty("pixelY", out var pyProp))
                        {
                            double px = pxProp.GetDouble();
                            double py = pyProp.GetDouble();
                            _bridgeService.CalibrateMap(px, py);
                            SendCurrentConfig();
                        }
                        break;

                    case "updateMapConfig":
                        if (_bridgeService != null)
                        {
                            double scale = root.TryGetProperty("scale", out var sProp) ? sProp.GetDouble() : _bridgeService.CoordinateEngine.WorldScale;
                            bool invertZ = root.TryGetProperty("invertZ", out var izProp) ? izProp.GetBoolean() : _bridgeService.CoordinateEngine.InvertZ;
                            double? origX = root.TryGetProperty("originX", out var oxProp) ? oxProp.GetDouble() : null;
                            double? origZ = root.TryGetProperty("originZ", out var ozProp) ? ozProp.GetDouble() : null;
                            _bridgeService.UpdateMapConfig(scale, invertZ, origX, origZ);
                            SendCurrentConfig();
                        }
                        break;

                    case "scanBle":
                        _ = Task.Run(async () =>
                        {
                            if (_bridgeService != null)
                            {
                                var devices = await _bridgeService.ScanBleDevicesAsync(8);
                                Dispatcher.Invoke(() =>
                                {
                                    string resultJson = JsonSerializer.Serialize(new
                                    {
                                        type = "bleScanResult",
                                        payload = devices
                                    });
                                    webView.CoreWebView2?.PostWebMessageAsJson(resultJson);
                                });
                            }
                        });
                        break;

                    case "connectSensor":
                        if (_bridgeService != null &&
                            root.TryGetProperty("type", out var typeProp) &&
                            root.TryGetProperty("mac", out var macProp) &&
                            root.TryGetProperty("name", out var nameProp))
                        {
                            SensorType sType = (SensorType)typeProp.GetInt32();
                            ulong mac = macProp.GetUInt64();
                            string name = nameProp.GetString() ?? "";
                            _ = Task.Run(async () =>
                            {
                                bool success = await _bridgeService.ConnectSensorAsync(sType, mac, name);
                                Dispatcher.Invoke(() =>
                                {
                                    string connJson = JsonSerializer.Serialize(new
                                    {
                                        type = "bleConnectResult",
                                        payload = new { success, name, mac }
                                    });
                                    webView.CoreWebView2?.PostWebMessageAsJson(connJson);
                                });
                            });
                        }
                        break;

                    case "connectHeartRate":
                        if (_bridgeService != null &&
                            root.TryGetProperty("mac", out var hrMacProp) &&
                            root.TryGetProperty("name", out var hrNameProp))
                        {
                            ulong mac = hrMacProp.GetUInt64();
                            string name = hrNameProp.GetString() ?? "";
                            _ = Task.Run(async () =>
                            {
                                bool success = await _bridgeService.ConnectHeartRateSensorAsync(mac, name);
                                Dispatcher.Invoke(() =>
                                {
                                    string connJson = JsonSerializer.Serialize(new
                                    {
                                        type = "hrConnectResult",
                                        payload = new { success, name, mac }
                                    });
                                    webView.CoreWebView2?.PostWebMessageAsJson(connJson);
                                });
                            });
                        }
                        break;

                    case "disconnectHeartRate":
                        _bridgeService?.DisconnectHeartRateSensor();
                        break;

                    case "connectCadence":
                        if (_bridgeService != null &&
                            root.TryGetProperty("mac", out var cadMacProp) &&
                            root.TryGetProperty("name", out var cadNameProp))
                        {
                            ulong mac = cadMacProp.GetUInt64();
                            string name = cadNameProp.GetString() ?? "";
                            _ = Task.Run(async () =>
                            {
                                bool success = await _bridgeService.ConnectCadenceSensorAsync(mac, name);
                                Dispatcher.Invoke(() =>
                                {
                                    string connJson = JsonSerializer.Serialize(new
                                    {
                                        type = "cadenceConnectResult",
                                        payload = new { success, name, mac }
                                    });
                                    webView.CoreWebView2?.PostWebMessageAsJson(connJson);
                                });
                            });
                        }
                        break;

                    case "disconnectCadence":
                        _bridgeService?.DisconnectCadenceSensor();
                        break;

                    case "setPreferPowerMeterWatts":
                        if (_bridgeService != null && root.TryGetProperty("prefer", out var prefProp))
                        {
                            _bridgeService.SetPreferPowerMeterWatts(prefProp.GetBoolean());
                        }
                        break;

                    #region ワークアウト関連アクション

                    case "getWorkoutLibrary":
                        if (_bridgeService != null && webView.CoreWebView2 != null)
                        {
                            var library = _bridgeService.GetWorkoutLibrary();
                            string libJson = JsonSerializer.Serialize(new
                            {
                                type = "workoutLibrary",
                                payload = library
                            });
                            webView.CoreWebView2.PostWebMessageAsJson(libJson);
                        }
                        break;

                    case "loadWorkoutDetail":
                    {
                        if (_bridgeService != null && webView.CoreWebView2 != null &&
                            root.TryGetProperty("filePath", out var detailPathProp))
                        {
                            string path = detailPathProp.GetString() ?? "";
                            var detail = _bridgeService.LoadWorkoutDetail(path);
                            string detailJson = JsonSerializer.Serialize(new
                            {
                                type = "workoutDetail",
                                payload = detail
                            });
                            webView.CoreWebView2.PostWebMessageAsJson(detailJson);
                        }
                        break;
                    }

                    case "startWorkout":
                    {
                        if (_bridgeService != null &&
                            root.TryGetProperty("filePath", out var startPathProp))
                        {
                            string path = startPathProp.GetString() ?? "";
                            bool started = _bridgeService.StartWorkout(path);
                            if (started)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    _overlayWindow?.Show();
                                    _overlayWindow?.Activate();
                                });
                            }
                        }
                        break;
                    }

                    case "startRampTest":
                    {
                        if (_bridgeService != null)
                        {
                            bool started = _bridgeService.StartRampTest();
                            if (started)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    _overlayWindow?.Show();
                                    _overlayWindow?.Activate();
                                });
                            }
                        }
                        break;
                    }

                    case "applyRampTestFtp":
                    {
                        if (_bridgeService != null && root.TryGetProperty("newFtp", out var ftpProp))
                        {
                            double newFtp = ftpProp.GetDouble();
                            _bridgeService.ApplyRampTestFtp(newFtp);
                            SendCurrentConfig();
                        }
                        break;
                    }

                    case "pauseWorkout":
                        _bridgeService?.PauseWorkout();
                        break;

                    case "resumeWorkout":
                        _bridgeService?.ResumeWorkout();
                        break;

                    case "stopWorkout":
                        _bridgeService?.StopWorkout();
                        Dispatcher.Invoke(() =>
                        {
                            _overlayWindow?.Hide();
                        });
                        break;

                    case "skipWorkoutStep":
                        _bridgeService?.SkipWorkoutStep();
                        break;

                    case "previousWorkoutStep":
                        _bridgeService?.PreviousWorkoutStep();
                        break;

                    case "setWorkoutIntensityBias":
                        if (_bridgeService != null && root.TryGetProperty("bias", out var biasProp))
                        {
                            _bridgeService.SetWorkoutIntensityBias(biasProp.GetDouble());
                        }
                        break;

                    case "toggleOverlay":
                        Dispatcher.Invoke(() =>
                        {
                            if (_overlayWindow != null)
                            {
                                if (_overlayWindow.IsVisible)
                                {
                                    _overlayWindow.IsUserDismissed = true;
                                    _overlayWindow.IsUserForcedVisible = false;
                                    _overlayWindow.Hide();
                                }
                                else
                                {
                                    _overlayWindow.IsUserDismissed = false;
                                    _overlayWindow.IsUserForcedVisible = true;
                                    _overlayWindow.Show();
                                    if (_bridgeService != null)
                                    {
                                        var snapshot = _bridgeService.GetCurrentSnapshot();
                                        _overlayWindow.SendState(snapshot, snapshot.Workout);
                                    }
                                }
                            }
                        });
                        break;

                    case "toggleOverlayClickThrough":
                        Dispatcher.Invoke(() =>
                        {
                            if (_overlayWindow != null)
                            {
                                _overlayWindow.SetClickThrough(!_overlayWindow.IsClickThrough);
                            }
                        });
                        break;

                    case "openWorkoutsFolder":
                        if (_bridgeService != null)
                        {
                            try
                            {
                                string dir = _bridgeService.WorkoutLibrary.WorkoutsDirectory;
                                if (Directory.Exists(dir))
                                {
                                    Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[OpenFolderErr] {ex.Message}");
                            }
                        }
                        break;

                    case "importWorkoutFilesDialog":
                        Dispatcher.Invoke(() =>
                        {
                            var ofd = new Microsoft.Win32.OpenFileDialog
                            {
                                Title = "ワークアウトファイルを選択 (複数選択可)",
                                Filter = "Workout Files (*.zwo;*.mrc;*.erg)|*.zwo;*.mrc;*.erg|Zwift Workout (*.zwo)|*.zwo|MRC/ERG Files (*.mrc;*.erg)|*.mrc;*.erg|All Files (*.*)|*.*",
                                Multiselect = true
                            };
                            if (ofd.ShowDialog() == true)
                            {
                                var imported = _bridgeService?.ImportWorkoutFiles(ofd.FileNames);
                                var library = _bridgeService?.GetWorkoutLibrary();
                                string libJson = JsonSerializer.Serialize(new
                                {
                                    type = "workoutLibrary",
                                    payload = library
                                });
                                webView.CoreWebView2?.PostWebMessageAsJson(libJson);
                            }
                        });
                        break;

                    #endregion
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebMessageErr] {ex.Message}");
            }
        }

        private void SendInitialDataToWeb()
        {
            if (_bridgeService == null || webView.CoreWebView2 == null) return;

            // POIピン一覧を送信
            var pins = _bridgeService.CoordinateEngine.Pins;
            string pinsJson = JsonSerializer.Serialize(new
            {
                type = "initPins",
                payload = pins
            });
            webView.CoreWebView2.PostWebMessageAsJson(pinsJson);

            SendCurrentConfig();

            // 初期状態スナップショット（BLE/HR接続状況含む）をUIに即時配信
            _bridgeService.PushCurrentSnapshot();
        }

        private void SendCurrentConfig()
        {
            if (_bridgeService == null || webView.CoreWebView2 == null) return;

            string configJson = JsonSerializer.Serialize(new
            {
                type = "config",
                payload = new
                {
                    defaultMode = _bridgeService.Config.DefaultMode,
                    difficulty = _bridgeService.Config.TrainerDifficulty,
                    ftp = _bridgeService.Config.Ftp,
                    weight = _bridgeService.Config.RiderWeightKg,
                    freerideOverlay = _bridgeService.Config.FreerideOverlayEnabled,
                    maxHeartRate = _bridgeService.Config.MaxHeartRate,
                    restingHeartRate = _bridgeService.Config.RestingHeartRate,
                    pedalBrake = _bridgeService.Config.PedalBrakeEnabled,
                    profileRecentDistance = _bridgeService.Config.ProfileRecentDistanceM,
                    powerSourceName = _bridgeService.Config.PowerSourceName,
                    cadenceSourceMacAddress = _bridgeService.Config.CadenceSourceMacAddress,
                    cadenceSourceName = _bridgeService.Config.CadenceSourceName,
                    cadenceSourceEnabled = _bridgeService.Config.CadenceSourceEnabled,
                    preferPowerMeterWatts = _bridgeService.Config.PreferPowerMeterWatts,
                    heartRateSourceMacAddress = _bridgeService.Config.HeartRateSourceMacAddress,
                    heartRateSourceName = _bridgeService.Config.HeartRateSourceName,
                    heartRateEnabled = _bridgeService.Config.HeartRateEnabled,
                    mapScale = _bridgeService.CoordinateEngine.WorldScale,
                    invertZ = _bridgeService.CoordinateEngine.InvertZ,
                    mapOriginX = _bridgeService.CoordinateEngine.WorldOriginX,
                    mapOriginZ = _bridgeService.CoordinateEngine.WorldOriginZ,
                    language = _bridgeService.Language
                }
            });
            webView.CoreWebView2.PostWebMessageAsJson(configJson);
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _overlayWindow?.Close();
            }
            catch { }
            _bridgeService?.Dispose();
        }
    }
}
