using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HorizonCyclingBridge.Telemetry
{
    public class TrackPoint
    {
        public DateTime Timestamp { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Elevation { get; set; }
        public double SpeedKmh { get; set; }
        public double Power { get; set; }
        public double Cadence { get; set; }
        public double HeartRate { get; set; }
    }

    public enum SessionState
    {
        Stopped,
        Recording,
        Paused
    }

    public class SessionSummary
    {
        public TimeSpan Duration { get; set; }
        public double DistanceKm { get; set; }
        public double ElevationGainMeters { get; set; }
        public double AvgSpeedKmh { get; set; }
        public double MaxSpeedKmh { get; set; }
        public double AvgPowerWatts { get; set; }
        public double MaxPowerWatts { get; set; }
        public string SavedFilePath { get; set; } = "";
    }

    public class SessionManager
    {
        private readonly List<TrackPoint> _trackPoints = new List<TrackPoint>();
        private readonly object _lock = new object();
        private DateTime _sessionStartTime;
        private DateTime _lastRecordTime;
        private TimeSpan _elapsedTime = TimeSpan.Zero;
        private DateTime _resumeTime;

        public SessionState State { get; private set; } = SessionState.Stopped;
        public TimeSpan ElapsedTime
        {
            get
            {
                if (State == SessionState.Recording)
                {
                    return _elapsedTime + (DateTime.UtcNow - _resumeTime);
                }
                return _elapsedTime;
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
                _resumeTime = _sessionStartTime;
                _elapsedTime = TimeSpan.Zero;
                _lastRecordTime = DateTime.MinValue;
                State = SessionState.Recording;
            }
        }

        public void Pause()
        {
            lock (_lock)
            {
                if (State == SessionState.Recording)
                {
                    _elapsedTime += (DateTime.UtcNow - _resumeTime);
                    State = SessionState.Paused;
                }
            }
        }

        public void Resume()
        {
            lock (_lock)
            {
                if (State == SessionState.Paused)
                {
                    _resumeTime = DateTime.UtcNow;
                    State = SessionState.Recording;
                }
            }
        }

        public SessionSummary StopAndExport(string outputDirectory, double totalDistanceKm, double elevationGainM)
        {
            lock (_lock)
            {
                if (State == SessionState.Recording)
                {
                    _elapsedTime += (DateTime.UtcNow - _resumeTime);
                }
                State = SessionState.Stopped;

                var summary = new SessionSummary
                {
                    Duration = _elapsedTime,
                    DistanceKm = totalDistanceKm,
                    ElevationGainMeters = elevationGainM,
                };

                if (_trackPoints.Count > 0)
                {
                    double sumPower = 0;
                    double maxPower = 0;
                    double maxSpeed = 0;

                    foreach (var pt in _trackPoints)
                    {
                        sumPower += pt.Power;
                        if (pt.Power > maxPower) maxPower = pt.Power;
                        if (pt.SpeedKmh > maxSpeed) maxSpeed = pt.SpeedKmh;
                    }

                    summary.AvgPowerWatts = sumPower / _trackPoints.Count;
                    summary.MaxPowerWatts = maxPower;
                    summary.MaxSpeedKmh = maxSpeed;
                    summary.AvgSpeedKmh = summary.Duration.TotalHours > 0 ? (totalDistanceKm / summary.Duration.TotalHours) : 0;

                    Directory.CreateDirectory(outputDirectory);
                    string fileName = $"HorizonCycling_{DateTime.Now:yyyyMMdd_HHmmss}.gpx";
                    string filePath = Path.Combine(outputDirectory, fileName);
                    GpxExporter.ExportGpx(filePath, _trackPoints, summary);
                    summary.SavedFilePath = filePath;
                }

                return summary;
            }
        }

        /// <summary>
        /// 1秒おきにトラックポイントを記録
        /// </summary>
        public void AddTrackPoint(double lat, double lon, double ele, double speedKmh, double power, double cadence, double heartRate = 0)
        {
            if (State != SessionState.Recording) return;

            DateTime now = DateTime.UtcNow;
            if ((now - _lastRecordTime).TotalMilliseconds < 1000) return;

            lock (_lock)
            {
                _lastRecordTime = now;
                _trackPoints.Add(new TrackPoint
                {
                    Timestamp = now,
                    Latitude = lat,
                    Longitude = lon,
                    Elevation = ele,
                    SpeedKmh = speedKmh,
                    Power = power,
                    Cadence = cadence,
                    HeartRate = heartRate
                });
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
            sb.AppendLine($"    <name>HorizonCycling Activity</name>");
            sb.AppendLine($"    <time>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</time>");
            sb.AppendLine("  </metadata>");
            sb.AppendLine("  <trk>");
            sb.AppendLine("    <name>Forza Horizon 6 Virtual Ride</name>");
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
}
