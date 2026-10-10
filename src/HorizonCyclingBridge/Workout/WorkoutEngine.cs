using System;
using System.Collections.Generic;
using System.Linq;

namespace HorizonCyclingBridge.Workout
{
    /// <summary>
    /// ワークアウトのリアルタイム実行・ERG目標ワット算出・セグメント遷移管理を行うエンジン
    /// </summary>
    public class WorkoutEngine
    {
        private WorkoutDefinition? _currentWorkout;
        private string _state = "Idle"; // Idle, Running, Paused, Finished
        private double _ftp = 200.0;
        private double _intensityBias = 1.0;

        private double _elapsedSecondsTotal = 0.0;
        private double _elapsedSecondsSegment = 0.0;
        private int _currentSegmentIndex = 0;
        private int _lastReportedTargetWatts = -1;
        private string? _activeMessage = null;
        private double _messageRemainingSec = 0.0;

        public event Action<int>? OnTargetPowerChanged;
        public event Action<WorkoutStateSnapshot>? OnStateUpdated;
        public event Action<string>? OnMessageAlert;
        public event Action? OnWorkoutCompleted;

        public string State => _state;
        public bool IsRunning => _state == "Running";
        public bool IsActive => _state == "Running" || _state == "Paused";
        public WorkoutDefinition? CurrentWorkout => _currentWorkout;
        public double IntensityBias => _intensityBias;

        public void Start(WorkoutDefinition workout, double ftp)
        {
            if (workout == null || workout.Segments.Count == 0) return;

            _currentWorkout = workout;
            _ftp = ftp > 0 ? ftp : 200.0;
            _intensityBias = 1.0;
            _state = "Running";
            _elapsedSecondsTotal = 0.0;
            _elapsedSecondsSegment = 0.0;
            _currentSegmentIndex = 0;
            _lastReportedTargetWatts = -1;
            _activeMessage = null;
            _messageRemainingSec = 0.0;

            CheckCurrentTextEvents(0);
            Update(0.0, _ftp);
        }

        public void Pause()
        {
            if (_state == "Running")
            {
                _state = "Paused";
                NotifyState();
            }
        }

        public void Resume()
        {
            if (_state == "Paused")
            {
                _state = "Running";
                NotifyState();
            }
        }

        public void Stop()
        {
            if (_state != "Idle")
            {
                _state = "Idle";
                _currentWorkout = null;
                NotifyState();
            }
        }

        public void SkipStep()
        {
            if (!IsActive || _currentWorkout == null) return;

            if (_currentSegmentIndex < _currentWorkout.Segments.Count - 1)
            {
                _currentSegmentIndex++;
                var seg = _currentWorkout.Segments[_currentSegmentIndex];
                _elapsedSecondsSegment = 0.0;
                _elapsedSecondsTotal = seg.StartSec;
                _activeMessage = null;
                CheckCurrentTextEvents(0);
                Update(0.0, _ftp);
            }
            else
            {
                Finish();
            }
        }

        public void PreviousStep()
        {
            if (!IsActive || _currentWorkout == null) return;

            if (_currentSegmentIndex > 0)
            {
                _currentSegmentIndex--;
                var seg = _currentWorkout.Segments[_currentSegmentIndex];
                _elapsedSecondsSegment = 0.0;
                _elapsedSecondsTotal = seg.StartSec;
                _activeMessage = null;
                CheckCurrentTextEvents(0);
                Update(0.0, _ftp);
            }
            else
            {
                _elapsedSecondsSegment = 0.0;
                _elapsedSecondsTotal = 0.0;
                Update(0.0, _ftp);
            }
        }

        public void SetIntensityBias(double bias)
        {
            _intensityBias = Math.Clamp(bias, 0.70, 1.30); // 70%〜130%
            Update(0.0, _ftp);
        }

        public void Update(double deltaSec, double currentFtp)
        {
            if (_currentWorkout == null) return;
            if (currentFtp > 0) _ftp = currentFtp;

            if (_state == "Running" && deltaSec > 0)
            {
                _elapsedSecondsTotal += deltaSec;
                _elapsedSecondsSegment += deltaSec;

                // メッセージ表示時間の減衰
                if (_messageRemainingSec > 0)
                {
                    _messageRemainingSec -= deltaSec;
                    if (_messageRemainingSec <= 0)
                    {
                        _activeMessage = null;
                    }
                }

                // セグメント進行判定
                if (_currentSegmentIndex < _currentWorkout.Segments.Count)
                {
                    var seg = _currentWorkout.Segments[_currentSegmentIndex];
                    if (_elapsedSecondsSegment >= seg.DurationSec)
                    {
                        _currentSegmentIndex++;
                        _elapsedSecondsSegment = 0.0;

                        if (_currentSegmentIndex >= _currentWorkout.Segments.Count)
                        {
                            Finish();
                            return;
                        }

                        CheckCurrentTextEvents((int)_elapsedSecondsSegment);
                    }
                    else
                    {
                        CheckCurrentTextEvents((int)_elapsedSecondsSegment);
                    }
                }
                else
                {
                    Finish();
                    return;
                }
            }

            // 現在の目標ワット数の算出
            int targetWatts = CalculateCurrentTargetWatts();
            if (targetWatts != _lastReportedTargetWatts)
            {
                _lastReportedTargetWatts = targetWatts;
                OnTargetPowerChanged?.Invoke(targetWatts);
            }

            NotifyState();
        }

        private int CalculateCurrentTargetWatts()
        {
            if (_currentWorkout == null || _currentSegmentIndex >= _currentWorkout.Segments.Count)
            {
                return (int)Math.Round(_ftp * 0.55 * _intensityBias);
            }

            var seg = _currentWorkout.Segments[_currentSegmentIndex];
            double ratio = 0.0;
            if (seg.DurationSec > 1)
            {
                ratio = Math.Clamp(_elapsedSecondsSegment / seg.DurationSec, 0.0, 1.0);
            }

            double pct = seg.TargetPowerLowPct + (seg.TargetPowerHighPct - seg.TargetPowerLowPct) * ratio;
            double watts = _ftp * pct * _intensityBias;
            return Math.Max(30, (int)Math.Round(watts));
        }

        private void CheckCurrentTextEvents(int currentSecInSeg)
        {
            if (_currentWorkout == null || _currentSegmentIndex >= _currentWorkout.Segments.Count) return;

            var seg = _currentWorkout.Segments[_currentSegmentIndex];
            foreach (var te in seg.TextEvents)
            {
                // 1秒以内の誤差でトリガー
                if (Math.Abs(te.TimeOffsetSec - currentSecInSeg) < 1)
                {
                    _activeMessage = te.Message;
                    _messageRemainingSec = te.DisplayDurationSec > 0 ? te.DisplayDurationSec : 8.0;
                    OnMessageAlert?.Invoke(te.Message);
                    break;
                }
            }
        }

        private void Finish()
        {
            _state = "Finished";
            _activeMessage = "🎉 ワークアウト完了！素晴らしい走りでした！";
            _messageRemainingSec = 30.0;
            OnWorkoutCompleted?.Invoke();
            NotifyState();
        }

        public WorkoutStateSnapshot CreateSnapshot()
        {
            var snap = new WorkoutStateSnapshot
            {
                State = _state,
                IntensityBias = Math.Round(_intensityBias, 2)
            };

            if (_currentWorkout == null) return snap;

            snap.WorkoutTitle = _currentWorkout.Title;
            snap.TotalDurationSec = _currentWorkout.TotalDurationSec;
            snap.ElapsedSec = (int)Math.Round(_elapsedSecondsTotal);
            snap.RemainingSec = Math.Max(0, _currentWorkout.TotalDurationSec - snap.ElapsedSec);
            snap.TotalSegmentsCount = _currentWorkout.Segments.Count;
            snap.CurrentSegmentIndex = _currentSegmentIndex;
            snap.ActiveMessage = _activeMessage;

            if (_currentSegmentIndex < _currentWorkout.Segments.Count)
            {
                var seg = _currentWorkout.Segments[_currentSegmentIndex];
                snap.CurrentSegmentType = seg.SegmentType;
                snap.SegmentTotalDurationSec = seg.DurationSec;
                snap.SegmentElapsedSec = (int)Math.Round(_elapsedSecondsSegment);
                snap.SegmentRemainingSec = Math.Max(0, seg.DurationSec - snap.SegmentElapsedSec);
                snap.TargetCadence = seg.TargetCadence;

                double ratio = seg.DurationSec > 1 ? Math.Clamp(_elapsedSecondsSegment / seg.DurationSec, 0.0, 1.0) : 0.0;
                double pct = seg.TargetPowerLowPct + (seg.TargetPowerHighPct - seg.TargetPowerLowPct) * ratio;
                snap.TargetPowerPct = Math.Round(pct * _intensityBias, 2);
                snap.TargetWatts = CalculateCurrentTargetWatts();
                snap.CurrentPowerZone = WorkoutLibraryManager.GetPowerZone(snap.TargetPowerPct);
            }

            // 次のセグメントの予告
            if (_currentSegmentIndex + 1 < _currentWorkout.Segments.Count)
            {
                var nextSeg = _currentWorkout.Segments[_currentSegmentIndex + 1];
                snap.NextDurationSec = nextSeg.DurationSec;
                snap.NextTargetWatts = (int)Math.Round(_ftp * nextSeg.TargetPowerLowPct * _intensityBias);
            }

            // セグメント一覧
            for (int i = 0; i < _currentWorkout.Segments.Count; i++)
            {
                var s = _currentWorkout.Segments[i];
                snap.Segments.Add(new WorkoutSegmentSnapshotItem
                {
                    Index = i,
                    Type = s.SegmentType,
                    StartSec = s.StartSec,
                    DurationSec = s.DurationSec,
                    TargetWattsLow = (int)Math.Round(_ftp * s.TargetPowerLowPct * _intensityBias),
                    TargetWattsHigh = (int)Math.Round(_ftp * s.TargetPowerHighPct * _intensityBias),
                    Zone = WorkoutLibraryManager.GetPowerZone(s.TargetPowerLowPct),
                    IsCompleted = i < _currentSegmentIndex,
                    IsCurrent = i == _currentSegmentIndex
                });
            }

            return snap;
        }

        private void NotifyState()
        {
            var snap = CreateSnapshot();
            OnStateUpdated?.Invoke(snap);
        }
    }
}
