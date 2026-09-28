using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace HorizonCyclingBridge.Telemetry
{
    public class TrackPoint
    {
        public DateTime Timestamp { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double PixelX { get; set; }
        public double PixelY { get; set; }
        public double Elevation { get; set; }
        public double SpeedKmh { get; set; }
        public double TargetSpeedKmh { get; set; }
        public double Power { get; set; }
        public double Cadence { get; set; }
        public double HeartRate { get; set; }
        public double Grade { get; set; }
        public double DistanceKm { get; set; }
    }

    public enum SessionState
    {
        Stopped,
        Recording,
        AutoPaused,
        Paused
    }

    public class SessionSummary
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public TimeSpan MovingDuration { get; set; }
        public TimeSpan Duration { get => MovingDuration; set => MovingDuration = value; } // 互換性維持
        public double DistanceKm { get; set; }
        public double ElevationGainMeters { get; set; }
        public double ElevationLossMeters { get; set; }
        public double AvgSpeedKmh { get; set; }
        public double MaxSpeedKmh { get; set; }
        public double AvgPowerWatts { get; set; }
        public double MaxPowerWatts { get; set; }
        public double NormalizedPowerWatts { get; set; }
        public double AvgCadence { get; set; }
        public double CaloriesKcal { get; set; }
        public string SavedFilePath { get; set; } = "";
        public string SavedJsonPath { get; set; } = "";
    }

    public class SessionManager
    {
        private readonly List<TrackPoint> _trackPoints = new List<TrackPoint>();
        private readonly object _lock = new object();
        private DateTime _sessionStartTime;
        private DateTime _lastRecordTime;
        private double _movingSeconds = 0.0;
        private bool _isManuallyPaused = false;
        private bool _isCurrentlyMoving = false;

        public SessionState State
        {
            get
            {
                lock (_lock)
                {
                    if (_sessionStartTime == DateTime.MinValue) return SessionState.Stopped;
                    if (_isManuallyPaused) return SessionState.Paused;
                    return _isCurrentlyMoving ? SessionState.Recording : SessionState.AutoPaused;
                }
            }
        }

        public TimeSpan ElapsedTime
        {
            get
            {
                lock (_lock)
                {
                    return TimeSpan.FromSeconds(_movingSeconds);
                }
            }
        }

        public TimeSpan TotalElapsedTime
        {
            get
            {
                lock (_lock)
                {
                    if (_sessionStartTime == DateTime.MinValue) return TimeSpan.Zero;
                    return DateTime.UtcNow - _sessionStartTime;
                }
            }
        }

        public int PointCount
        {
            get
            {
                lock (_lock) return _trackPoints.Count;
            }
        }

        public void Start()
        {
            lock (_lock)
            {
                _trackPoints.Clear();
                _sessionStartTime = DateTime.UtcNow;
                _movingSeconds = 0.0;
                _lastRecordTime = DateTime.MinValue;
                _isManuallyPaused = false;
                _isCurrentlyMoving = false;
            }
        }

        public void Pause()
        {
            lock (_lock)
            {
                _isManuallyPaused = true;
            }
        }

        public void Resume()
        {
            lock (_lock)
            {
                _isManuallyPaused = false;
            }
        }

        /// <summary>
        /// 走行状態の更新および移動時間の積算
        /// </summary>
        public void UpdateMovingState(bool isMoving, double deltaSeconds)
        {
            lock (_lock)
            {
                if (_sessionStartTime == DateTime.MinValue || _isManuallyPaused)
                {
                    _isCurrentlyMoving = false;
                    return;
                }

                _isCurrentlyMoving = isMoving;
                if (isMoving && deltaSeconds > 0 && deltaSeconds < 2.0)
                {
                    _movingSeconds += deltaSeconds;
                }
            }
        }

        /// <summary>
        /// 互換用オーバーロード (lat, lon, ele, speedKmh, power, cadence, heartRate)
        /// </summary>
        public void AddTrackPoint(double lat, double lon, double ele, double speedKmh, double power, double cadence, double heartRate = 0)
        {
            AddTrackPoint(lat, lon, ele, speedKmh, 0.0, power, cadence, heartRate, 0.0, 0.0);
        }

        /// <summary>
        /// トラックポイントを記録（Pixel座標指定付き）
        /// </summary>
        public void AddTrackPoint(double lat, double lon, double pixelX, double pixelY, double ele, double speedKmh, double targetSpeedKmh, double power, double cadence, double heartRate, double grade, double distKm)
        {
            lock (_lock)
            {
                if (_sessionStartTime == DateTime.MinValue || _isManuallyPaused) return;

                DateTime now = DateTime.UtcNow;

                // 初回ポイントがまだ無い場合はスタート地点として即時記録。それ以降は移動中かつ1秒間隔で記録
                bool isFirstPoint = _trackPoints.Count == 0;
                if (!isFirstPoint && !_isCurrentlyMoving) return;

                if (!isFirstPoint && (now - _lastRecordTime).TotalMilliseconds < 1000) return;

                // pixelX/Yが未指定の場合、GPSからの逆算
                if (pixelX == 0 && pixelY == 0 && (lat != 0 || lon != 0))
                {
                    double worldX = (lon - 138.7274) * 91287.0;
                    double worldZ = (35.3606 - lat) * 111139.0;
                    pixelX = 4096.0 + worldX * 0.263;
                    pixelY = 4096.0 - worldZ * 0.263;
                }

                _lastRecordTime = now;
                _trackPoints.Add(new TrackPoint
                {
                    Timestamp = now,
                    Latitude = lat,
                    Longitude = lon,
                    PixelX = Math.Round(pixelX, 1),
                    PixelY = Math.Round(pixelY, 1),
                    Elevation = ele,
                    SpeedKmh = speedKmh,
                    TargetSpeedKmh = targetSpeedKmh,
                    Power = power,
                    Cadence = cadence,
                    HeartRate = heartRate,
                    Grade = grade,
                    DistanceKm = distKm
                });
            }
        }

        /// <summary>
        /// トラックポイントを記録（初回スタート地点、または移動中に1秒間隔で記録）
        /// </summary>
        public void AddTrackPoint(double lat, double lon, double ele, double speedKmh, double targetSpeedKmh, double power, double cadence, double heartRate, double grade, double distKm)
        {
            AddTrackPoint(lat, lon, 0, 0, ele, speedKmh, targetSpeedKmh, power, cadence, heartRate, grade, distKm);
        }

        public SessionSummary StopAndExport(string outputDirectory, double totalDistanceKm, double elevationGainM)
        {
            lock (_lock)
            {
                DateTime endTime = DateTime.UtcNow;
                var summary = new SessionSummary
                {
                    StartTime = _sessionStartTime != DateTime.MinValue ? _sessionStartTime : endTime,
                    EndTime = endTime,
                    TotalDuration = _sessionStartTime != DateTime.MinValue ? (endTime - _sessionStartTime) : TimeSpan.Zero,
                    MovingDuration = TimeSpan.FromSeconds(_movingSeconds),
                    DistanceKm = totalDistanceKm,
                    ElevationGainMeters = elevationGainM,
                };

                _sessionStartTime = DateTime.MinValue;
                _isCurrentlyMoving = false;
                _isManuallyPaused = false;

                // トラックポイントが0件の場合でも、最低1点（開始・終了点）を設けて確実にファイルを出力
                if (_trackPoints.Count == 0)
                {
                    _trackPoints.Add(new TrackPoint
                    {
                        Timestamp = summary.StartTime,
                        Latitude = 35.3606,
                        Longitude = 138.7274,
                        PixelX = 4096.0,
                        PixelY = 4096.0,
                        Elevation = 0,
                        SpeedKmh = 0,
                        TargetSpeedKmh = 0,
                        Power = 0,
                        Cadence = 0,
                        Grade = 0,
                        DistanceKm = 0
                    });
                }

                if (_trackPoints.Count > 0)
                {
                    double sumPower = 0;
                    double maxPower = 0;
                    double maxSpeed = 0;
                    double sumCadence = 0;
                    int cadenceCount = 0;

                    // NP (Normalized Power) 計算用 (30s ローリング平均の4乗平均の4乗根)
                    List<double> rolling30s = new List<double>();
                    double sumFourthPower = 0;
                    int rollingCount = 0;

                    for (int i = 0; i < _trackPoints.Count; i++)
                    {
                        var pt = _trackPoints[i];
                        sumPower += pt.Power;
                        if (pt.Power > maxPower) maxPower = pt.Power;
                        if (pt.SpeedKmh > maxSpeed) maxSpeed = pt.SpeedKmh;
                        if (pt.Cadence > 0)
                        {
                            sumCadence += pt.Cadence;
                            cadenceCount++;
                        }

                        // 30秒ローリング平均
                        rolling30s.Add(pt.Power);
                        if (rolling30s.Count > 30) rolling30s.RemoveAt(0);

                        if (rolling30s.Count >= 10)
                        {
                            double rollAvg = 0;
                            for (int r = 0; r < rolling30s.Count; r++) rollAvg += rolling30s[r];
                            rollAvg /= rolling30s.Count;
                            sumFourthPower += Math.Pow(rollAvg, 4);
                            rollingCount++;
                        }
                    }

                    summary.AvgPowerWatts = sumPower / _trackPoints.Count;
                    summary.MaxPowerWatts = maxPower;
                    summary.MaxSpeedKmh = maxSpeed;
                    summary.AvgCadence = cadenceCount > 0 ? (sumCadence / cadenceCount) : 0;
                    summary.AvgSpeedKmh = summary.MovingDuration.TotalHours > 0 ? (totalDistanceKm / summary.MovingDuration.TotalHours) : 0;

                    if (rollingCount > 0)
                    {
                        summary.NormalizedPowerWatts = Math.Round(Math.Pow(sumFourthPower / rollingCount, 0.25));
                    }
                    else
                    {
                        summary.NormalizedPowerWatts = Math.Round(summary.AvgPowerWatts);
                    }

                    // カロリー概算: Work(kJ) = AvgPower(W) * MovingTime(s) / 1000, 運動効率20-25%のため kJ ≈ kcal
                    double workKj = (summary.AvgPowerWatts * summary.MovingDuration.TotalSeconds) / 1000.0;
                    summary.CaloriesKcal = Math.Round(workKj * 1.05);

                    Directory.CreateDirectory(outputDirectory);
                    string timeStampStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string gpxPath = Path.Combine(outputDirectory, $"HorizonCycling_{timeStampStr}.gpx");
                    string jsonPath = Path.Combine(outputDirectory, $"HorizonCycling_{timeStampStr}.json");

                    // 1. GPX 1.1 エクスポート
                    GpxExporter.ExportGpx(gpxPath, _trackPoints, summary);
                    summary.SavedFilePath = gpxPath;

                    // 2. JSON エクスポート
                    JsonExporter.ExportJson(jsonPath, _trackPoints, summary);
                    summary.SavedJsonPath = jsonPath;
                }

                return summary;
            }
        }

        public List<TrackPoint> GetRecentPoints(int maxPoints = 500)
        {
            lock (_lock)
            {
                if (_trackPoints.Count <= maxPoints) return new List<TrackPoint>(_trackPoints);
                return _trackPoints.GetRange(_trackPoints.Count - maxPoints, maxPoints);
            }
        }
    }

    public static class GpxExporter
    {
        public static void ExportGpx(string filePath, IReadOnlyList<TrackPoint> points, SessionSummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<gpx creator=\"HorizonCycling\" version=\"1.1\"");
            sb.AppendLine("     xmlns=\"http://www.topografix.com/GPX/1/1\"");
            sb.AppendLine("     xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"");
            sb.AppendLine("     xmlns:gpxtpx=\"http://www.garmin.com/xmlschemas/TrackPointExtension/v1\"");
            sb.AppendLine("     xsi:schemaLocation=\"http://www.topografix.com/GPX/1/1 http://www.topografix.com/GPX/1/1/gpx.xsd http://www.garmin.com/xmlschemas/TrackPointExtension/v1 http://www.garmin.com/xmlschemas/TrackPointExtensionv1.xsd\">");
            sb.AppendLine("  <metadata>");
            sb.AppendLine($"    <name>HorizonCycling Activity - {summary.StartTime:yyyy-MM-dd HH:mm}</name>");
            sb.AppendLine($"    <time>{summary.StartTime:yyyy-MM-ddTHH:mm:ssZ}</time>");
            sb.AppendLine("  </metadata>");
            sb.AppendLine("  <trk>");
            sb.AppendLine("    <name>Forza Horizon Virtual Ride</name>");
            sb.AppendLine("    <type>Cycling</type>");
            sb.AppendLine("    <trkseg>");

            var nfi = CultureInfo.InvariantCulture.NumberFormat;
            foreach (var pt in points)
            {
                sb.AppendLine($"      <trkpt lat=\"{pt.Latitude.ToString("F7", nfi)}\" lon=\"{pt.Longitude.ToString("F7", nfi)}\">");
                sb.AppendLine($"        <ele>{pt.Elevation.ToString("F2", nfi)}</ele>");
                sb.AppendLine($"        <time>{pt.Timestamp:yyyy-MM-ddTHH:mm:ssZ}</time>");
                sb.AppendLine("        <extensions>");
                sb.AppendLine($"          <power>{Math.Round(pt.Power)}</power>");
                sb.AppendLine($"          <targetSpeed>{pt.TargetSpeedKmh.ToString("F1", nfi)}</targetSpeed>");
                sb.AppendLine($"          <grade>{pt.Grade.ToString("F1", nfi)}</grade>");
                sb.AppendLine("          <gpxtpx:TrackPointExtension>");
                sb.AppendLine($"            <gpxtpx:speed>{(pt.SpeedKmh / 3.6).ToString("F2", nfi)}</gpxtpx:speed>");
                if (pt.Cadence > 0)
                {
                    sb.AppendLine($"            <gpxtpx:cad>{Math.Round(pt.Cadence)}</gpxtpx:cad>");
                }
                if (pt.HeartRate > 0)
                {
                    sb.AppendLine($"            <gpxtpx:hr>{Math.Round(pt.HeartRate)}</gpxtpx:hr>");
                }
                sb.AppendLine("          </gpxtpx:TrackPointExtension>");
                sb.AppendLine("        </extensions>");
                sb.AppendLine("      </trkpt>");
            }

            sb.AppendLine("    </trkseg>");
            sb.AppendLine("  </trk>");
            sb.AppendLine("</gpx>");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }
    }

    public static class JsonExporter
    {
        public static void ExportJson(string filePath, IReadOnlyList<TrackPoint> points, SessionSummary summary)
        {
            var data = new
            {
                sessionId = Path.GetFileNameWithoutExtension(filePath).Replace("HorizonCycling_", ""),
                startTime = summary.StartTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                endTime = summary.EndTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                summary = new
                {
                    totalDurationSec = summary.TotalDuration.TotalSeconds,
                    movingDurationSec = summary.MovingDuration.TotalSeconds,
                    distanceKm = Math.Round(summary.DistanceKm, 2),
                    elevationGainM = Math.Round(summary.ElevationGainMeters, 1),
                    avgSpeedKmh = Math.Round(summary.AvgSpeedKmh, 1),
                    maxSpeedKmh = Math.Round(summary.MaxSpeedKmh, 1),
                    avgPowerWatts = Math.Round(summary.AvgPowerWatts, 1),
                    maxPowerWatts = Math.Round(summary.MaxPowerWatts, 0),
                    normalizedPowerWatts = Math.Round(summary.NormalizedPowerWatts, 0),
                    avgCadence = Math.Round(summary.AvgCadence, 0),
                    caloriesKcal = Math.Round(summary.CaloriesKcal, 0)
                },
                points = points
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = false
            };

            string json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }
    }
}
