using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HorizonCyclingBridge.Telemetry
{
    public class SessionHistoryItem
    {
        public string SessionId { get; set; } = "";
        public string FileName { get; set; } = "";
        public string StartTime { get; set; } = "";
        public string EndTime { get; set; } = "";
        public double MovingDurationSec { get; set; }
        public double TotalDurationSec { get; set; }
        public double DistanceKm { get; set; }
        public double ElevationGainM { get; set; }
        public double AvgSpeedKmh { get; set; }
        public double MaxSpeedKmh { get; set; }
        public double AvgPowerWatts { get; set; }
        public double MaxPowerWatts { get; set; }
        public double NormalizedPowerWatts { get; set; }
        public double AvgCadence { get; set; }
        public double CaloriesKcal { get; set; }
        public string GpxFileName { get; set; } = "";
    }

    public static class SessionHistoryManager
    {
        /// <summary>
        /// 保存されたセッション履歴の一覧を取得します（最新順）
        /// </summary>
        public static List<SessionHistoryItem> GetHistoryList(string activitiesDirectory)
        {
            var list = new List<SessionHistoryItem>();
            if (!Directory.Exists(activitiesDirectory))
            {
                return list;
            }

            var jsonFiles = Directory.GetFiles(activitiesDirectory, "HorizonCycling_*.json");

            foreach (var filePath in jsonFiles)
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var item = new SessionHistoryItem
                    {
                        FileName = Path.GetFileName(filePath),
                        GpxFileName = Path.ChangeExtension(Path.GetFileName(filePath), ".gpx")
                    };

                    if (root.TryGetProperty("sessionId", out var sid)) item.SessionId = sid.GetString() ?? "";
                    if (root.TryGetProperty("startTime", out var st)) item.StartTime = st.GetString() ?? "";
                    if (root.TryGetProperty("endTime", out var et)) item.EndTime = et.GetString() ?? "";

                    if (root.TryGetProperty("summary", out var sum))
                    {
                        if (sum.TryGetProperty("movingDurationSec", out var md)) item.MovingDurationSec = md.GetDouble();
                        if (sum.TryGetProperty("totalDurationSec", out var td)) item.TotalDurationSec = td.GetDouble();
                        if (sum.TryGetProperty("distanceKm", out var d)) item.DistanceKm = d.GetDouble();
                        if (sum.TryGetProperty("elevationGainM", out var eg)) item.ElevationGainM = eg.GetDouble();
                        if (sum.TryGetProperty("avgSpeedKmh", out var spd)) item.AvgSpeedKmh = spd.GetDouble();
                        if (sum.TryGetProperty("maxSpeedKmh", out var mspd)) item.MaxSpeedKmh = mspd.GetDouble();
                        if (sum.TryGetProperty("avgPowerWatts", out var pwr)) item.AvgPowerWatts = pwr.GetDouble();
                        if (sum.TryGetProperty("maxPowerWatts", out var mpwr)) item.MaxPowerWatts = mpwr.GetDouble();
                        if (sum.TryGetProperty("normalizedPowerWatts", out var np)) item.NormalizedPowerWatts = np.GetDouble();
                        if (sum.TryGetProperty("avgCadence", out var cad)) item.AvgCadence = cad.GetDouble();
                        if (sum.TryGetProperty("caloriesKcal", out var cal)) item.CaloriesKcal = cal.GetDouble();
                    }

                    list.Add(item);
                }
                catch
                {
                    // 破損したファイルがあってもスキップ
                }
            }

            // 新しい順（ファイル名または開始日時の降順）にソート
            return list.OrderByDescending(x => x.StartTime).ThenByDescending(x => x.FileName).ToList();
        }

        /// <summary>
        /// 特定のセッション詳細（座標点・全サマリー）のJSON文字列を取得します
        /// </summary>
        public static string? GetHistoryDetailJson(string activitiesDirectory, string fileName)
        {
            string cleanName = Path.GetFileName(fileName); // ディレクトリトラバーサル防止
            string filePath = Path.Combine(activitiesDirectory, cleanName);
            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }
            return null;
        }

        /// <summary>
        /// セッション履歴（JSON および GPX）を削除します
        /// </summary>
        public static bool DeleteHistory(string activitiesDirectory, string fileName)
        {
            string cleanName = Path.GetFileName(fileName);
            string jsonPath = Path.Combine(activitiesDirectory, cleanName);
            string gpxPath = Path.Combine(activitiesDirectory, Path.ChangeExtension(cleanName, ".gpx"));

            bool deleted = false;
            if (File.Exists(jsonPath))
            {
                File.Delete(jsonPath);
                deleted = true;
            }
            if (File.Exists(gpxPath))
            {
                File.Delete(gpxPath);
                deleted = true;
            }
            return deleted;
        }
    }
}
