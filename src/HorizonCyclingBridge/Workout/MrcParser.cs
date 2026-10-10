using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace HorizonCyclingBridge.Workout
{
    /// <summary>
    /// MRC / ERG テキスト形式のパーサー
    /// </summary>
    public static class MrcParser
    {
        public static WorkoutDefinition ParseFile(string filePath, double defaultFtp = 200.0)
        {
            string content = File.ReadAllText(filePath);
            var workout = Parse(content, defaultFtp);
            if (string.IsNullOrWhiteSpace(workout.Title) || workout.Title == "Untitled Workout")
            {
                workout.Title = Path.GetFileNameWithoutExtension(filePath);
            }
            return workout;
        }

        public static WorkoutDefinition Parse(string content, double defaultFtp = 200.0)
        {
            var workout = new WorkoutDefinition();
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            bool inHeader = false;
            bool inData = false;
            bool isWatts = false;

            var rawPoints = new List<(double Minutes, double Value)>();

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                if (line.Equals("[COURSE HEADER]", StringComparison.OrdinalIgnoreCase))
                {
                    inHeader = true;
                    inData = false;
                    continue;
                }
                if (line.Equals("[COURSE DATA]", StringComparison.OrdinalIgnoreCase))
                {
                    inHeader = false;
                    inData = true;
                    continue;
                }
                if (line.Equals("[END COURSE DATA]", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("[END COURSE HEADER]", StringComparison.OrdinalIgnoreCase))
                {
                    inHeader = false;
                    inData = false;
                    continue;
                }

                if (inHeader)
                {
                    var eqIdx = line.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        string key = line.Substring(0, eqIdx).Trim().ToUpperInvariant();
                        string val = line.Substring(eqIdx + 1).Trim();

                        if (key == "DESCRIPTION") workout.Description = val;
                        else if (key == "FILE NAME") workout.Title = val;
                    }
                    else if (line.Contains("MINUTES WATTS", StringComparison.OrdinalIgnoreCase))
                    {
                        isWatts = true;
                    }
                }
                else if (inData)
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 &&
                        double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double min) &&
                        double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                    {
                        rawPoints.Add((min, val));
                    }
                }
            }

            if (rawPoints.Count < 2) return workout;

            // ポイント列からセグメント列を生成
            int segIndex = 0;
            for (int i = 0; i < rawPoints.Count - 1; i += 2)
            {
                var p1 = rawPoints[i];
                var p2 = rawPoints[i + 1];

                int startSec = (int)Math.Round(p1.Minutes * 60);
                int endSec = (int)Math.Round(p2.Minutes * 60);
                int duration = endSec - startSec;
                if (duration <= 0) continue;

                double pLow = isWatts ? (p1.Value / defaultFtp) : (p1.Value / 100.0);
                double pHigh = isWatts ? (p2.Value / defaultFtp) : (p2.Value / 100.0);

                string type = "Steady";
                if (i == 0) type = "Warmup";
                else if (i >= rawPoints.Count - 2) type = "Cooldown";
                else if (Math.Abs(pHigh - pLow) > 0.05) type = "Ramp";
                else if (pLow >= 1.0) type = "IntervalOn";
                else if (pLow <= 0.65) type = "IntervalOff";

                workout.Segments.Add(new WorkoutSegment
                {
                    SegmentIndex = segIndex++,
                    SegmentType = type,
                    StartSec = startSec,
                    DurationSec = duration,
                    TargetPowerLowPct = pLow,
                    TargetPowerHighPct = pHigh
                });
            }

            if (workout.Segments.Count > 0)
            {
                var last = workout.Segments[^1];
                workout.TotalDurationSec = last.StartSec + last.DurationSec;
            }

            return workout;
        }
    }
}
