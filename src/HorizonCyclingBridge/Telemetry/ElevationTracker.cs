using System;
using System.Collections.Generic;

namespace HorizonCyclingBridge.Telemetry
{
    public struct ElevationPoint
    {
        public double DistanceMeters { get; set; }
        public double ElevationMeters { get; set; }
        public double GradePercent { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Forzaテレメトリの標高（PositionY）を追跡し、ノイズ除去、獲得標高積算、プロファイル記録を行うクラス
    /// </summary>
    public class ElevationTracker
    {
        // 平滑化フィルタ用パラメータ（指数移動平均 EMA）
        private const double SMOOTHING_ALPHA = 0.05; // 60Hz環境でおよそ1秒程度の時定数
        private const double GAIN_THRESHOLD_METERS = 0.3; // 登坂とみなす最小上昇しきい値

        private double _smoothedElevation = 0.0;
        private bool _isInitialized = false;

        private double _lastTrackedElevation = 0.0;
        private double _elevationGain = 0.0;
        private double _elevationLoss = 0.0;
        private double _totalDistanceMeters = 0.0;

        // 標高プロファイル履歴（直近最大2000点）
        private readonly List<ElevationPoint> _profileHistory = new List<ElevationPoint>();
        private readonly object _lockObj = new object();
        private const int MAX_HISTORY_POINTS = 2000;
        private DateTime _lastRecordTime = DateTime.MinValue;

        public double CurrentElevation => _smoothedElevation;
        public double ElevationGain => _elevationGain;
        public double ElevationLoss => _elevationLoss;
        public double TotalDistanceMeters => _totalDistanceMeters;

        public void Reset()
        {
            lock (_lockObj)
            {
                _isInitialized = false;
                _smoothedElevation = 0.0;
                _lastTrackedElevation = 0.0;
                _elevationGain = 0.0;
                _elevationLoss = 0.0;
                _totalDistanceMeters = 0.0;
                _profileHistory.Clear();
                _lastRecordTime = DateTime.MinValue;
            }
        }

        /// <summary>
        /// テレメトリから新しい標高と走行距離を受け取り、平滑化と獲得標高の積算を行う
        /// </summary>
        public void Update(float rawElevation, float currentSpeedKmh, double gradePercent, double deltaSeconds)
        {
            if (float.IsNaN(rawElevation) || float.IsInfinity(rawElevation)) return;

            lock (_lockObj)
            {
                if (!_isInitialized)
                {
                    _smoothedElevation = rawElevation;
                    _lastTrackedElevation = rawElevation;
                    _isInitialized = true;
                    return;
                }

                // 指数移動平均フィルタ
                _smoothedElevation = (_smoothedElevation * (1.0 - SMOOTHING_ALPHA)) + (rawElevation * SMOOTHING_ALPHA);

                // 走行距離の積算（時速からメートル換算）
                if (currentSpeedKmh > 0.5)
                {
                    _totalDistanceMeters += (currentSpeedKmh / 3.6) * deltaSeconds;
                }

                // 獲得標高の計算（微小ノイズによる過大評価を防止するしきい値処理）
                double diff = _smoothedElevation - _lastTrackedElevation;
                if (diff >= GAIN_THRESHOLD_METERS)
                {
                    _elevationGain += diff;
                    _lastTrackedElevation = _smoothedElevation;
                }
                else if (diff <= -GAIN_THRESHOLD_METERS)
                {
                    _elevationLoss += Math.Abs(diff);
                    _lastTrackedElevation = _smoothedElevation;
                }

                // プロファイル履歴の記録（0.5秒おきにサンプリング）
                DateTime now = DateTime.UtcNow;
                if ((now - _lastRecordTime).TotalMilliseconds >= 500)
                {
                    _lastRecordTime = now;
                    _profileHistory.Add(new ElevationPoint
                    {
                        DistanceMeters = _totalDistanceMeters,
                        ElevationMeters = _smoothedElevation,
                        GradePercent = gradePercent,
                        Timestamp = now
                    });

                    if (_profileHistory.Count > MAX_HISTORY_POINTS)
                    {
                        _profileHistory.RemoveAt(0);
                    }
                }
            }
        }

        /// <summary>
        /// UI描画用の直近標高プロファイルリストを取得
        /// </summary>
        public List<ElevationPoint> GetProfileSnapshot()
        {
            lock (_lockObj)
            {
                return new List<ElevationPoint>(_profileHistory);
            }
        }
    }
}
