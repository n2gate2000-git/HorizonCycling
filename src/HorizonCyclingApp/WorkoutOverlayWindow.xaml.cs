using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using HorizonCyclingBridge.Core;
using HorizonCyclingBridge.Workout;

namespace HorizonCyclingApp
{
    public partial class WorkoutOverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private const int HOTKEY_TOGGLE_ID = 9001;
        private const int HOTKEY_STOP_ID = 9002;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_O = 0x4F; // 'O' key
        private const uint VK_Q = 0x51; // 'Q' key (Quit/Stop)
        private const int WM_HOTKEY = 0x0312;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        private static int GetWindowLong(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return (int)GetWindowLongPtr64(hWnd, nIndex);
            return GetWindowLong32(hWnd, nIndex);
        }

        private static int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong)
        {
            if (IntPtr.Size == 8)
                return (int)SetWindowLongPtr64(hWnd, nIndex, new IntPtr(dwNewLong));
            return SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        private HwndSource? _hwndSource;
        private bool _isClickThrough = true;
        private string _lastRenderedTitle = "";
        private string _currentLanguage = "ja";

        public event Action<string>? OnOverlayCommand;
        public bool IsClickThrough => _isClickThrough;
        public bool IsUserForcedVisible { get; set; } = false;
        public bool IsUserDismissed { get; set; } = false;
        public string CurrentLanguage => _currentLanguage;

        public void SetLanguage(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return;
            _currentLanguage = lang.ToLowerInvariant().StartsWith("en") ? "en" : "ja";
            UpdateLanguageTexts();
        }

        private void UpdateLanguageTexts()
        {
            bool isEn = _currentLanguage == "en";
            btnStopWorkout.Content = isEn ? "⏹ Stop" : "⏹ 終了";
            btnStopFreeride.Content = isEn ? "💾 Save & Exit" : "💾 終了・保存";
            btnHideFreeride.Content = isEn ? "✕ Hide" : "✕ 非表示";
            btnToggleClickThrough.Content = _isClickThrough 
                ? (isEn ? "🛡️ Pass-through" : "🛡️ 操作透過中") 
                : (isEn ? "🔓 Edit Mode (Clickable)" : "🔓 編集モード (クリック可)");
            
            lblRampCadenceWarning.Text = isEn ? "⚠️ Low Cadence! Maintain 50+ RPM! " : "⚠️ ケイデンス低下！ 50 RPM以上を維持してください！ ";
            lblRampResultTitle.Text = isEn ? "🎉 Ramp Test Complete! New FTP: " : "🎉 ランプテスト完了！ 新測定FTP: ";
            lblEditModeHint.Text = isEn 
                ? "⚠️ Edit Mode (Mouse Enabled). Click \"⏹ Stop\" or press Ctrl + Shift + Q to exit." 
                : "⚠️ 編集モード中（マウス操作可能）。「⏹ 終了」ボタン、または Ctrl + Shift + Q で終了できます";
            lblFreerideElevation.Text = isEn ? "Elev: " : "標高: ";
            lblFreerideGain.Text = isEn ? "Gain: " : "獲得: ";
        }

        public WorkoutOverlayWindow()
        {
            InitializeComponent();
            Loaded += WorkoutOverlayWindow_Loaded;
            Closed += WorkoutOverlayWindow_Closed;
            SizeChanged += WorkoutOverlayWindow_SizeChanged;
        }

        private void WorkoutOverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    _hwndSource = HwndSource.FromHwnd(hwnd);
                    _hwndSource?.AddHook(WndProc);

                    // グローバルホットキー登録: Ctrl+Shift+O (トグル), Ctrl+Shift+Q (即時終了)
                    RegisterHotKey(hwnd, HOTKEY_TOGGLE_ID, MOD_CONTROL | MOD_SHIFT, VK_O);
                    RegisterHotKey(hwnd, HOTKEY_STOP_ID, MOD_CONTROL | MOD_SHIFT, VK_Q);

                    // 基本ウィンドウスタイル（最前面、Alt+Tab除外）
                    int style = GetWindowLong(hwnd, GWL_EXSTYLE);
                    style |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW;
                    SetWindowLong(hwnd, GWL_EXSTYLE, style);
                    SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE);
                }

                SetClickThrough(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Overlay Init Error] {ex.Message}");
            }
        }

        private void WorkoutOverlayWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    UnregisterHotKey(hwnd, HOTKEY_TOGGLE_ID);
                    UnregisterHotKey(hwnd, HOTKEY_STOP_ID);
                }
                _hwndSource?.RemoveHook(WndProc);
            }
            catch { }
        }

        private void WorkoutOverlayWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 画面リサイズ時にリボンを再計算
            _lastRenderedTitle = ""; // 次回スナップショットで再描画を促す
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_TOGGLE_ID)
                {
                    if (Visibility != Visibility.Visible)
                    {
                        // 非表示中なら再表示して編集モードにする
                        IsUserDismissed = false;
                        IsUserForcedVisible = true;
                        Visibility = Visibility.Visible;
                        SetClickThrough(false);
                    }
                    else
                    {
                        // 表示中なら 操作透過 ⇔ 編集モード
                        SetClickThrough(!_isClickThrough);
                    }
                    handled = true;
                    return IntPtr.Zero;
                }
                else if (id == HOTKEY_STOP_ID)
                {
                    // Ctrl + Shift + Q: ワークアウト終了またはHUD非表示
                    OnOverlayCommand?.Invoke("hotkeyStop");
                    handled = true;
                    return IntPtr.Zero;
                }
            }

            if (msg == WM_NCHITTEST && _isClickThrough)
            {
                // 操作透過モード：マウス入力を背後のゲームへ素通り
                handled = true;
                return new IntPtr(HTTRANSPARENT);
            }

            return IntPtr.Zero;
        }

        public void SetClickThrough(bool enable)
        {
            _isClickThrough = enable;

            if (enable)
            {
                // 透過モード
                toolbarBorder.Opacity = 0.25;
                toolbarBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
                btnToggleClickThrough.Content = _currentLanguage == "en" ? "🛡️ Pass-through" : "🛡️ 操作透過中";
                btnToggleClickThrough.Foreground = new SolidColorBrush(Color.FromRgb(165, 243, 252));
                editModeBanner.Visibility = Visibility.Collapsed;
            }
            else
            {
                // 編集モード（クリック可能）
                toolbarBorder.Opacity = 1.0;
                toolbarBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(6, 182, 212));
                btnToggleClickThrough.Content = _currentLanguage == "en" ? "🔓 Edit Mode (Clickable)" : "🔓 編集モード (クリック可)";
                btnToggleClickThrough.Foreground = new SolidColorBrush(Color.FromRgb(252, 165, 165));
                editModeBanner.Visibility = Visibility.Visible;
                Activate();
                Focus();
            }
        }

        #region UI Event Handlers

        private void BtnPrevStep_Click(object sender, RoutedEventArgs e)
        {
            OnOverlayCommand?.Invoke("prevStep");
        }

        private void BtnPauseWorkout_Click(object sender, RoutedEventArgs e)
        {
            if (btnPauseWorkout.Content?.ToString() == "▶")
            {
                OnOverlayCommand?.Invoke("resumeWorkout");
            }
            else
            {
                OnOverlayCommand?.Invoke("pauseWorkout");
            }
        }

        private void BtnSkipStep_Click(object sender, RoutedEventArgs e)
        {
            OnOverlayCommand?.Invoke("skipStep");
        }

        private void BtnStopWorkout_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[Overlay] Stop button clicked!");
            OnOverlayCommand?.Invoke("stopWorkout");
        }

        private void BtnToggleClickThrough_Click(object sender, RoutedEventArgs e)
        {
            SetClickThrough(!_isClickThrough);
        }

        private void BtnStopFreeride_Click(object sender, RoutedEventArgs e)
        {
            OnOverlayCommand?.Invoke("stopFreerideSession");
        }

        private void BtnHideFreeride_Click(object sender, RoutedEventArgs e)
        {
            IsUserDismissed = true;
            IsUserForcedVisible = false;
            Visibility = Visibility.Collapsed;
            OnOverlayCommand?.Invoke("hideOverlay");
        }

        #endregion

        #region State Update

        public void SendState(BridgeStateSnapshot bridgeState, WorkoutStateSnapshot? workoutState)
        {
            Dispatcher.InvokeAsync(() =>
            {
                bool isWorkoutActive = workoutState != null && workoutState.IsActive;
                bool isFreerideAllowed = !IsUserDismissed && (bridgeState.FreerideOverlayEnabled || IsUserForcedVisible);

                if (!isWorkoutActive && !isFreerideAllowed)
                {
                    if (Visibility == Visibility.Visible)
                    {
                        Visibility = Visibility.Collapsed;
                    }
                    return;
                }

                if (Visibility != Visibility.Visible)
                {
                    Visibility = Visibility.Visible;
                }

                if (isWorkoutActive)
                {
                    panelWorkoutToolbarControls.Visibility = Visibility.Visible;
                    panelFreerideToolbarControls.Visibility = Visibility.Collapsed;
                    txtToolbarTitle.Text = "HORIZON WORKOUT HUD";
                    segmentListWidget.Visibility = Visibility.Visible;
                    workoutArchContainer.Visibility = Visibility.Visible;
                    workoutRibbonWidget.Visibility = Visibility.Visible;
                    freerideHudWidget.Visibility = Visibility.Collapsed;

                    UpdateWorkoutMetrics(bridgeState, workoutState!);
                }
                else
                {
                    panelWorkoutToolbarControls.Visibility = Visibility.Collapsed;
                    panelFreerideToolbarControls.Visibility = Visibility.Visible;
                    txtToolbarTitle.Text = "HORIZON CYCLING HUD";
                    segmentListWidget.Visibility = Visibility.Collapsed;
                    workoutArchContainer.Visibility = Visibility.Collapsed;
                    workoutRibbonWidget.Visibility = Visibility.Collapsed;
                    freerideHudWidget.Visibility = Visibility.Visible;

                    UpdateFreerideMetrics(bridgeState);
                }
            });
        }

        private void UpdateFreerideMetrics(BridgeStateSnapshot s)
        {
            // 1. セッション状態 & 経過時間
            txtFreerideTime.Text = $"⏱ {FormatSeconds(s.ElapsedSeconds)}";
            if (s.SessionState == "Recording")
            {
                txtFreerideState.Text = "REC";
                txtFreerideState.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                borderFreerideStateBadge.Background = new SolidColorBrush(Color.FromArgb(38, 16, 185, 129));
            }
            else if (s.SessionState == "AutoPaused")
            {
                txtFreerideState.Text = "AUTO-PAUSE";
                txtFreerideState.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                borderFreerideStateBadge.Background = new SolidColorBrush(Color.FromArgb(38, 245, 158, 11));
            }
            else if (s.SessionState == "Paused")
            {
                txtFreerideState.Text = "PAUSED";
                txtFreerideState.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                borderFreerideStateBadge.Background = new SolidColorBrush(Color.FromArgb(38, 245, 158, 11));
            }
            else
            {
                txtFreerideState.Text = "IDLE";
                txtFreerideState.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                borderFreerideStateBadge.Background = new SolidColorBrush(Color.FromArgb(38, 100, 116, 139));
            }

            // 2. パワー & W/kg
            int powerWatts = (int)Math.Round(s.Power);
            txtFreeridePower.Text = powerWatts.ToString();
            SolidColorBrush powerBrush = GetPowerZoneBrush(powerWatts, s.Ftp);
            txtFreeridePower.Foreground = powerBrush;

            double riderWeight = s.RiderWeightKg > 0 ? s.RiderWeightKg : 70.0;
            double wkg = s.Wkg > 0 ? s.Wkg : (powerWatts / riderWeight);
            txtFreerideWkg.Text = $"{wkg:F1} W/kg";

            double zonePercent = Math.Clamp((powerWatts / Math.Max(1.0, s.Ftp * 1.5)) * 200.0, 0.0, 200.0);
            rectFreeridePowerZoneBar.Width = zonePercent;
            rectFreeridePowerZoneBar.Fill = powerBrush;

            // 3. ケイデンス & 心拍数
            txtFreerideCadence.Text = s.Cadence > 0 ? Math.Round(s.Cadence).ToString() : "--";
            if (s.HeartRate > 0)
            {
                int hr = (int)Math.Round(s.HeartRate);
                txtFreerideHeartRate.Text = $"{hr} bpm";

                // HRゾーンバッジ
                int hrZone = GetHrZoneNumber(hr);
                txtFreerideHrZone.Text = $"Z{hrZone}";
                txtFreerideHrZone.Foreground = GetZoneColorBrush(Math.Clamp(hrZone + 1, 1, 7));
            }
            else
            {
                txtFreerideHeartRate.Text = "-- bpm";
                txtFreerideHrZone.Text = "Z0";
                txtFreerideHrZone.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            // 4. 勾配 & 標高・獲得標高
            string sign = s.RawGrade >= 0 ? "+" : "";
            txtFreerideGrade.Text = $"{sign}{s.RawGrade:F1}";
            txtFreerideGrade.Foreground = GetGradeBrush(s.RawGrade);

            txtFreerideElevation.Text = $"{Math.Round(s.ElevationMeters)} m";
            txtFreerideGain.Text = $"+{Math.Round(s.ElevationGainMeters)} m";

            // 5. 距離 & カロリー
            txtFreerideDistance.Text = $"{s.TotalDistanceKm:F2} km";
            txtFreerideCalories.Text = $"{Math.Round(s.CaloriesKcal)} kcal";

            // 6. スロットル & ブレーキ開度
            int thrPct = (int)Math.Round(s.Throttle * 100.0);
            int brkPct = (int)Math.Round(s.Brake * 100.0);
            txtFreerideThrottle.Text = $"{thrPct}%";
            rectFreerideThrottleBar.Width = Math.Clamp(s.Throttle * 70.0, 0.0, 70.0);
            txtFreerideBrake.Text = $"{brkPct}%";
            rectFreerideBrakeBar.Width = Math.Clamp(s.Brake * 70.0, 0.0, 70.0);
        }

        private static int GetHrZoneNumber(int hr)
        {
            if (hr < 115) return 1;
            if (hr < 135) return 2;
            if (hr < 155) return 3;
            if (hr < 172) return 4;
            return 5;
        }

        private static SolidColorBrush GetGradeBrush(double grade)
        {
            if (grade > 8.0) return new SolidColorBrush(Color.FromRgb(239, 68, 68));   // 激坂 赤
            if (grade > 4.0) return new SolidColorBrush(Color.FromRgb(249, 115, 22));  // 登坂 橙
            if (grade > 0.0) return new SolidColorBrush(Color.FromRgb(245, 158, 11));  // 緩斜面 黄
            return new SolidColorBrush(Color.FromRgb(56, 189, 248));                  // 下り 水色
        }

        private static string FormatSeconds(double totalSec)
        {
            int s = (int)Math.Floor(totalSec);
            int hrs = s / 3600;
            int mins = (s % 3600) / 60;
            int secs = s % 60;
            if (hrs > 0)
            {
                return $"{hrs:D2}:{mins:D2}:{secs:D2}";
            }
            return $"{mins:D2}:{secs:D2}";
        }

        private void UpdateWorkoutMetrics(BridgeStateSnapshot bridgeState, WorkoutStateSnapshot workoutState)
        {
            // 1. タイトル & 時間
            txtWorkoutTitle.Text = workoutState.WorkoutTitle ?? "WORKOUT";
            txtOverallTime.Text = $"{FormatTime(workoutState.ElapsedSec)} / {FormatTime(workoutState.TotalDurationSec)}";

            // ポーズボタン表示切替
            btnPauseWorkout.Content = workoutState.State == "Paused" ? "▶" : "⏸";

            // 2. ブロック情報
            txtBlockTitle.Text = $"BLOCK {workoutState.CurrentSegmentIndex + 1}/{workoutState.TotalSegmentsCount}: {workoutState.CurrentSegmentType.ToUpper()}";
            txtBlockRemaining.Text = $"⏱ {FormatTime(workoutState.SegmentRemainingSec)}";

            // 3. パワー数値 (実測パワーはゾーンカラーで超大型表示、ターゲットも明瞭表示)
            int actualWatts = (int)Math.Round(bridgeState.Power);
            int targetWatts = workoutState.TargetWatts;
            int diff = actualWatts - targetWatts;

            txtActualPower.Text = actualWatts.ToString();
            txtActualPower.Foreground = GetPowerZoneBrush(actualWatts, bridgeState.Ftp);
            txtTargetPower.Text = $"{targetWatts}W";

            string diffStr = diff >= 0 ? $"+{diff}W" : $"{diff}W";
            txtPowerDiff.Text = diffStr;

            // 差分色 & バッジ背景 & ポインター位置計算
            SolidColorBrush diffBrush;
            if (Math.Abs(diff) <= 5)
            {
                diffBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // 緑 (Match)
            }
            else if (diff < -5)
            {
                diffBrush = new SolidColorBrush(Color.FromRgb(234, 179, 8)); // 黄 (Low)
            }
            else
            {
                diffBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // 赤 (High)
            }
            txtPowerDiff.Foreground = diffBrush;
            borderDiffBadge.Background = new SolidColorBrush(Color.FromArgb(38, diffBrush.Color.R, diffBrush.Color.G, diffBrush.Color.B));
            gaugePointer.Background = diffBrush;

            // 差分ゲージポインター移動 (中央は 79px、±30Wで端まで移動)
            double clampedDiff = Math.Clamp((double)diff, -30.0, 30.0);
            double offset = (clampedDiff / 30.0) * 70.0;
            Canvas.SetLeft(gaugePointer, Math.Clamp(79.0 + offset, 4.0, 154.0));

            // 4. ケイデンス & 心拍数 & 速度
            int targetCad = workoutState.TargetCadence ?? 90;
            txtTargetCadence.Text = $"{targetCad} RPM";
            txtActualCadence.Text = bridgeState.Cadence > 0 ? Math.Round(bridgeState.Cadence).ToString() : "--";

            txtSpeed.Text = $"{bridgeState.CarSpeedKmh:F1} km/h";
            txtHeartRate.Text = bridgeState.HeartRate > 0 ? $"{bridgeState.HeartRate} bpm" : "-- bpm";

            // 5. ブロック進捗バー (幅 616px)
            if (workoutState.SegmentTotalDurationSec > 0)
            {
                double ratio = (double)(workoutState.SegmentTotalDurationSec - workoutState.SegmentRemainingSec) / workoutState.SegmentTotalDurationSec;
                rectProgressBarFill.Width = Math.Clamp(ratio * 616.0, 0.0, 616.0);
            }

            // 6. カウントダウン
            if (workoutState.SegmentRemainingSec is >= 1 and <= 5 && workoutState.CurrentSegmentIndex + 1 < workoutState.TotalSegmentsCount)
            {
                txtCountdown.Text = workoutState.SegmentRemainingSec.ToString();
                countdownBanner.Visibility = Visibility.Visible;
            }
            else
            {
                countdownBanner.Visibility = Visibility.Collapsed;
            }

            // 6.5 ランプテスト特有のバナー処理 (限界警告・新FTP結果)
            if (workoutState.IsRampTest)
            {
                if (workoutState.RampResult != null)
                {
                    txtRampNewFtp.Text = $"{workoutState.RampResult.NewFtp:F0} W";
                    string sgn = workoutState.RampResult.FtpDelta >= 0 ? "+" : "";
                    txtRampFtpDelta.Text = $"({sgn}{workoutState.RampResult.FtpDelta:F0}W)";
                    rampResultBanner.Visibility = Visibility.Visible;
                    rampCadenceWarningBanner.Visibility = Visibility.Collapsed;
                }
                else if (workoutState.LowCadenceSeconds > 0.3)
                {
                    double remain = Math.Max(0.0, 3.0 - workoutState.LowCadenceSeconds);
                    txtRampLowCadenceSec.Text = _currentLanguage == "en" ? $"[{remain:F1}s until failure]" : $"[あと {remain:F1}秒で限界判定]";
                    rampCadenceWarningBanner.Visibility = Visibility.Visible;
                    rampResultBanner.Visibility = Visibility.Collapsed;
                }
                else
                {
                    rampCadenceWarningBanner.Visibility = Visibility.Collapsed;
                    rampResultBanner.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                rampCadenceWarningBanner.Visibility = Visibility.Collapsed;
                rampResultBanner.Visibility = Visibility.Collapsed;
            }

            // 7. コーチメッセージ
            if (!string.IsNullOrEmpty(workoutState.ActiveMessage))
            {
                txtCoachMessage.Text = workoutState.ActiveMessage;
                coachMessageBorder.Visibility = Visibility.Visible;
            }
            else
            {
                coachMessageBorder.Visibility = Visibility.Collapsed;
            }

            // 8. セグメントリスト描画 (タイトルが変わった時のみ再構築)
            if (_lastRenderedTitle != (workoutState.WorkoutTitle ?? ""))
            {
                _lastRenderedTitle = workoutState.WorkoutTitle ?? "";
                RenderSegmentList(workoutState);
                RenderRibbon(workoutState);
            }
            else
            {
                UpdateSegmentListHighlight(workoutState.CurrentSegmentIndex);
            }

            // 9. リボン上のピン位置 & 時間更新
            if (workoutState.TotalDurationSec > 0)
            {
                double totalWidth = canvasRibbon.ActualWidth > 0 ? canvasRibbon.ActualWidth : (ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth);
                double ratio = (double)workoutState.ElapsedSec / workoutState.TotalDurationSec;
                double pinX = Math.Clamp(ratio * totalWidth, 0, totalWidth);
                Canvas.SetLeft(pinPointer, pinX);
                txtPinTime.Text = FormatTime(workoutState.ElapsedSec);
            }
        }

        private void RenderSegmentList(WorkoutStateSnapshot workout)
        {
            segmentItemsStackPanel.Children.Clear();
            int currentIdx = workout.CurrentSegmentIndex;

            for (int i = 0; i < workout.Segments.Count; i++)
            {
                var seg = workout.Segments[i];
                bool isCur = (i == currentIdx);
                bool isDone = (i < currentIdx);

                var itemBorder = new Border
                {
                    Margin = new Thickness(0, 2, 0, 2),
                    Padding = new Thickness(8, 5, 8, 5),
                    CornerRadius = new CornerRadius(6),
                    Background = isCur ? new SolidColorBrush(Color.FromArgb(38, 6, 182, 212)) : new SolidColorBrush(Color.FromArgb(8, 255, 255, 255)),
                    BorderBrush = isCur ? new SolidColorBrush(Color.FromRgb(6, 182, 212)) : Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    Opacity = isDone ? 0.35 : 1.0,
                    Tag = i
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // ゾーンピルバッジ (Z1〜Z7)
                var zonePill = new Border
                {
                    Background = GetZoneColorBrush(seg.Zone),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                zonePill.Child = new TextBlock
                {
                    Text = $"Z{seg.Zone}",
                    Foreground = Brushes.White,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold
                };
                Grid.SetColumn(zonePill, 0);
                grid.Children.Add(zonePill);

                // セグメント種別
                var txtType = new TextBlock
                {
                    Text = $"{seg.Type}",
                    FontSize = 11,
                    FontWeight = isCur ? FontWeights.Bold : FontWeights.SemiBold,
                    Foreground = isCur ? new SolidColorBrush(Color.FromRgb(6, 182, 212)) : new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(txtType, 1);
                grid.Children.Add(txtType);

                // 時間
                var txtTime = new TextBlock
                {
                    Text = FormatTime(seg.DurationSec),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Consolas, Segoe UI Mono"),
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(txtTime, 2);
                grid.Children.Add(txtTime);

                itemBorder.Child = grid;
                segmentItemsStackPanel.Children.Add(itemBorder);
            }
        }

        private void UpdateSegmentListHighlight(int currentIdx)
        {
            for (int i = 0; i < segmentItemsStackPanel.Children.Count; i++)
            {
                if (segmentItemsStackPanel.Children[i] is Border border)
                {
                    bool isCur = (i == currentIdx);
                    bool isDone = (i < currentIdx);
                    border.Background = isCur ? new SolidColorBrush(Color.FromArgb(38, 6, 182, 212)) : new SolidColorBrush(Color.FromArgb(8, 255, 255, 255));
                    border.BorderBrush = isCur ? new SolidColorBrush(Color.FromRgb(6, 182, 212)) : Brushes.Transparent;
                    border.Opacity = isDone ? 0.35 : 1.0;
                }
            }
        }

        private void RenderRibbon(WorkoutStateSnapshot workout)
        {
            canvasRibbon.Children.Clear();
            if (workout.TotalDurationSec <= 0) return;

            void BuildBlocks()
            {
                canvasRibbon.Children.Clear();
                double totalWidth = canvasRibbon.ActualWidth > 0 ? canvasRibbon.ActualWidth : (ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth);
                if (totalWidth <= 0) totalWidth = 1920;

                double currentLeft = 0;
                foreach (var seg in workout.Segments)
                {
                    double w = ((double)seg.DurationSec / workout.TotalDurationSec) * totalWidth;
                    var rect = new Rectangle
                    {
                        Width = Math.Max(w, 1.0),
                        Height = 28,
                        Fill = GetZoneColorBrush(seg.Zone),
                        Opacity = 0.85
                    };
                    Canvas.SetLeft(rect, currentLeft);
                    Canvas.SetTop(rect, 0);
                    canvasRibbon.Children.Add(rect);
                    currentLeft += w;
                }
            }

            if (canvasRibbon.ActualWidth > 0)
            {
                BuildBlocks();
            }
            else
            {
                canvasRibbon.Loaded += (s, e) => BuildBlocks();
            }
        }

        private static string FormatTime(int seconds)
        {
            int m = seconds / 60;
            int s = seconds % 60;
            return $"{m:D2}:{s:D2}";
        }

        private static SolidColorBrush GetPowerZoneBrush(int watts, double ftp)
        {
            if (ftp <= 0) ftp = 200;
            double pct = (double)watts / ftp;

            if (pct < 0.55) return new SolidColorBrush(Color.FromRgb(100, 116, 139)); // Z1
            if (pct < 0.75) return new SolidColorBrush(Color.FromRgb(59, 130, 246));  // Z2
            if (pct < 0.90) return new SolidColorBrush(Color.FromRgb(16, 185, 129));  // Z3
            if (pct < 1.05) return new SolidColorBrush(Color.FromRgb(234, 179, 8));   // Z4
            if (pct < 1.20) return new SolidColorBrush(Color.FromRgb(249, 115, 22));  // Z5
            if (pct < 1.50) return new SolidColorBrush(Color.FromRgb(239, 68, 68));   // Z6
            return new SolidColorBrush(Color.FromRgb(168, 85, 247));                 // Z7
        }

        private static SolidColorBrush GetZoneColorBrush(int zone)
        {
            return zone switch
            {
                1 => new SolidColorBrush(Color.FromRgb(100, 116, 139)), // Z1: グレー
                2 => new SolidColorBrush(Color.FromRgb(59, 130, 246)),  // Z2: ブルー
                3 => new SolidColorBrush(Color.FromRgb(16, 185, 129)),  // Z3: グリーン
                4 => new SolidColorBrush(Color.FromRgb(234, 179, 8)),   // Z4: イエロー
                5 => new SolidColorBrush(Color.FromRgb(249, 115, 22)),  // Z5: オレンジ
                6 => new SolidColorBrush(Color.FromRgb(239, 68, 68)),   // Z6: レッド
                7 => new SolidColorBrush(Color.FromRgb(168, 85, 247)),  // Z7: パープル
                _ => new SolidColorBrush(Color.FromRgb(6, 182, 212))
            };
        }

        #endregion
    }
}
