using System;
using System.Collections.Generic;

namespace HorizonCyclingBridge.Core
{
    public class ConsoleDashboard
    {
        private readonly object _lockObj = new object();
        private readonly Queue<string> _logBuffer = new Queue<string>();
        private const int MAX_LOG_COUNT = 6;
        private const int LOG_START_Y = 21;
        private const int LINE_WIDTH = 79; // 80列目への自動改行を防止するため79文字でクランプ

        // システムステータス
        private string _modeName = "SIMULATION MODE";
        private string _bleStatus = "Disconnected";
        private bool _isVJoyActive = false;
        private bool _isTelemetryActive = false;
        private bool _pedalBrakeEnabled = true;

        // リアルタイムメトリクス
        private double _currentPower = 0.0;
        private double _targetSpeedKmh = 0.0;
        private double _carSpeedKmh = 0.0;
        private double _rawGrade = 0.0;
        private double _sentGrade = 0.0;
        private double _difficulty = 0.5;
        private double _throttle = 0.0;
        private double _brake = 0.0;
        private bool _isArcadeMode = false;

        public string ModeName
        {
            get => _modeName;
            set { _modeName = value; UpdateSystemStatus(); }
        }

        public string BleStatus
        {
            get => _bleStatus;
            set { _bleStatus = value; UpdateSystemStatus(); }
        }

        public bool IsVJoyActive
        {
            get => _isVJoyActive;
            set { _isVJoyActive = value; UpdateSystemStatus(); }
        }

        public bool IsTelemetryActive
        {
            get => _isTelemetryActive;
            set { _isTelemetryActive = value; UpdateSystemStatus(); }
        }

        public bool PedalBrakeEnabled
        {
            get => _pedalBrakeEnabled;
            set { _pedalBrakeEnabled = value; UpdateSystemStatus(); }
        }

        public bool IsArcadeMode
        {
            get => _isArcadeMode;
            set => _isArcadeMode = value;
        }

        public void InitializeLayout()
        {
            lock (_lockObj)
            {
                try
                {
                    Console.Clear();
                    Console.CursorVisible = false;
                    DrawStaticFrame();
                    UpdateSystemStatusLocked();
                    UpdateMetricsLocked();
                    RedrawLogsLocked();
                }
                catch
                {
                    // フォールバック
                }
            }
        }

        public void UpdateMetrics(double power, double targetSpeed, double carSpeed, double rawGrade, double sentGrade, double difficulty, double throttle, double brake)
        {
            lock (_lockObj)
            {
                _currentPower = power;
                _targetSpeedKmh = targetSpeed;
                _carSpeedKmh = carSpeed;
                _rawGrade = rawGrade;
                _sentGrade = sentGrade;
                _difficulty = difficulty;
                _throttle = throttle;
                _brake = brake;

                UpdateMetricsLocked();
            }
        }

        public void AddLog(string message)
        {
            lock (_lockObj)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string formatted = $"[{timestamp}] {message}";
                
                _logBuffer.Enqueue(formatted);
                while (_logBuffer.Count > MAX_LOG_COUNT)
                {
                    _logBuffer.Dequeue();
                }

                RedrawLogsLocked();
            }
        }

        public void Cleanup()
        {
            lock (_lockObj)
            {
                try
                {
                    Console.SetCursorPosition(0, 28);
                    Console.CursorVisible = true;
                    Console.WriteLine("\n[BRIDGE] Dashboard stopped.");
                }
                catch
                {
                    // 無視
                }
            }
        }

        private void DrawStaticFrame()
        {
            string bar = new string('=', LINE_WIDTH);
            string sep = new string('-', LINE_WIDTH);

            WriteAt(0, 0, bar);
            WriteAt(0, 1, "  HorizonCyclingBridge - Smart Trainer & Forza 6 Dual-Bridge");
            WriteAt(0, 2, bar);
            WriteAt(0, 3, " [SYSTEM STATUS]");
            WriteAt(0, 4, "  Mode             : ");
            WriteAt(0, 5, "  BLE Device       : ");
            WriteAt(0, 6, "  vJoy Controller  : ");
            WriteAt(0, 7, "  Telemetry Status : ");
            WriteAt(0, 8, "  Pedal Brake      : ");
            WriteAt(0, 9, sep);
            WriteAt(0, 10, " [REALTIME METRICS]");
            WriteAt(0, 11, "  Pedal Power      :           | Speed (Target/Car) :");
            WriteAt(0, 12, "  Road Grade (Raw) :           | Road Grade (Sent)  :");
            WriteAt(0, 13, "  Difficulty       :           | Out (Thr / Brk)    :");
            WriteAt(0, 14, sep);
            WriteAt(0, 15, " [CONTROLLER INSTRUCTIONS]");
            WriteAt(0, 16, "  [-] / [+] : Change Difficulty (±10%)   |  [M] : Switch Mode (Sim/Arcade)");
            WriteAt(0, 17, "  [T]       : Test Throttle (3 seconds)  |  [B] : Toggle Pedal Brake ON/OFF");
            WriteAt(0, 18, "  [Space]   : Emergency Brake test (3s)  |  [Q] : Quit Application");
            WriteAt(0, 19, sep);
            WriteAt(0, 20, " [RECENT LOGS]");
            for (int i = 0; i < MAX_LOG_COUNT; i++)
            {
                WriteAt(0, LOG_START_Y + i, "".PadRight(LINE_WIDTH));
            }
            WriteAt(0, 27, bar);
        }

        private void UpdateSystemStatus()
        {
            lock (_lockObj)
            {
                UpdateSystemStatusLocked();
            }
        }

        private void UpdateSystemStatusLocked()
        {
            WriteAt(21, 4, _modeName.PadRight(55));
            WriteAt(21, 5, _bleStatus.PadRight(55));
            WriteAt(21, 6, (_isVJoyActive ? "ACTIVE (Device 1)" : "DISABLED").PadRight(55));
            WriteAt(21, 7, (_isTelemetryActive ? "ACTIVE (Port 5000)" : "INITIALIZING...").PadRight(55));
            WriteAt(21, 8, (_pedalBrakeEnabled ? "ON  (Brake when stop pedaling)" : "OFF (Coast/Free when stop pedaling)").PadRight(55));
        }

        private void UpdateMetricsLocked()
        {
            // Pedal Power
            WriteAt(21, 11, $"{_currentPower,5:F0} W ");
            
            // Speed
            if (_isArcadeMode)
            {
                WriteAt(56, 11, $"Direct {(_throttle * 100.0),3:F0}%      ");
            }
            else
            {
                WriteAt(56, 11, $"{_targetSpeedKmh,5:F1} / {_carSpeedKmh,5:F1} km/h");
            }

            // Road Grade
            WriteAt(21, 12, $"{_rawGrade,+5:F1} % ");
            WriteAt(56, 12, $"{_sentGrade,+5:F1} % ");

            // Difficulty & Out
            WriteAt(21, 13, $"{(_difficulty * 100.0),5:F0} % ");
            WriteAt(56, 13, $"{(_throttle * 100.0),3:F0}% / {(_brake * 100.0),3:F0}%");
        }

        private void RedrawLogsLocked()
        {
            int index = 0;
            foreach (var log in _logBuffer)
            {
                string line = log.Length > 75 ? log.Substring(0, 75) : log.PadRight(75);
                WriteAt(2, LOG_START_Y + index, line);
                index++;
            }
            for (; index < MAX_LOG_COUNT; index++)
            {
                WriteAt(2, LOG_START_Y + index, "".PadRight(75));
            }
        }

        private void WriteAt(int x, int y, string text)
        {
            try
            {
                if (x >= 0 && y >= 0 && y < Console.BufferHeight)
                {
                    int maxLen = Math.Min(text.Length, LINE_WIDTH - x);
                    if (maxLen > 0)
                    {
                        Console.SetCursorPosition(x, y);
                        Console.Write(text.Substring(0, maxLen));
                    }
                }
            }
            catch
            {
                // ウィンドウリサイズ時等の例外をスキップ
            }
        }
    }
}
