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

                // タイルキャッシュフォルダの探索
                string tileCacheDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "map", ".tile_cache"));
                if (!Directory.Exists(tileCacheDir))
                {
                    tileCacheDir = @"d:\develop\HorizonCycling\map\.tile_cache";
                }

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

                string mapDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "map"));
                if (!Directory.Exists(mapDir))
                {
                    mapDir = @"d:\develop\HorizonCycling\map";
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

                await _bridgeService.StartAsync();

                // 5. アプリケーションHTMLのロード
                webView.Source = new Uri("https://horizoncycling.local/index.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アプリケーションの初期化中にエラーが発生しました:\n{ex.Message}", "HorizonCycling Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                    if (webView.CoreWebView2 != null)
                    {
                        string json = JsonSerializer.Serialize(new
                        {
                            type = "state",
                            payload = snapshot
                        });
                        webView.CoreWebView2.PostWebMessageAsJson(json);
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

                    case "updateSettings":
                        if (_bridgeService != null)
                        {
                            if (root.TryGetProperty("ftp", out var ftpProp))
                            {
                                _bridgeService.SetFtp(ftpProp.GetDouble());
                            }
                            if (root.TryGetProperty("pedalBrake", out var brakeProp))
                            {
                                _bridgeService.SetPedalBrake(brakeProp.GetBoolean());
                            }
                        }
                        break;

                    case "openLogFolder":
                        if (root.TryGetProperty("path", out var pathProp))
                        {
                            string path = pathProp.GetString() ?? "";
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
                        }
                        break;

                    case "scanBle":
                        _ = Task.Run(async () =>
                        {
                            if (_bridgeService != null)
                            {
                                var devices = await _bridgeService.ScanBleDevicesAsync(7);
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

            // コンフィグ情報を送信
            string configJson = JsonSerializer.Serialize(new
            {
                type = "config",
                payload = new
                {
                    defaultMode = _bridgeService.Config.DefaultMode,
                    difficulty = _bridgeService.Config.TrainerDifficulty,
                    ftp = _bridgeService.Config.Ftp,
                    pedalBrake = _bridgeService.Config.PedalBrakeEnabled,
                    mapScale = _bridgeService.CoordinateEngine.WorldScale,
                    invertZ = _bridgeService.CoordinateEngine.InvertZ,
                    mapOriginX = _bridgeService.CoordinateEngine.WorldOriginX,
                    mapOriginZ = _bridgeService.CoordinateEngine.WorldOriginZ
                }
            });
            webView.CoreWebView2.PostWebMessageAsJson(configJson);
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _bridgeService?.Dispose();
        }
    }
}
