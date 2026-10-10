using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace HorizonCyclingBridge.Workout
{
    /// <summary>
    /// Zwift Workout XML (.zwo) のパーサー
    /// </summary>
    public static class ZwoParser
    {
        public static WorkoutDefinition Parse(string xmlContent)
        {
            var workout = new WorkoutDefinition();
            var doc = XDocument.Parse(xmlContent);

            var root = doc.Root;
            if (root == null) return workout;

            workout.Author = root.Element("author")?.Value?.Trim() ?? "Unknown";
            workout.Title = root.Element("name")?.Value?.Trim() ?? "Untitled Workout";
            workout.Description = root.Element("description")?.Value?.Trim() ?? "";
            workout.SportType = root.Element("sportType")?.Value?.Trim() ?? "bike";

            var tagsElem = root.Element("tags");
            if (tagsElem != null)
            {
                foreach (var tag in tagsElem.Elements("tag"))
                {
                    string? name = tag.Attribute("name")?.Value;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        workout.Tags.Add(name.Trim());
                    }
                }
            }

            workout.IsRampTest = workout.Tags.Any(t => t.Equals("RampTest", StringComparison.OrdinalIgnoreCase) || t.Equals("FTPTest", StringComparison.OrdinalIgnoreCase))
                || workout.Title.Contains("Ramp Test", StringComparison.OrdinalIgnoreCase)
                || workout.Title.Contains("FTP Test", StringComparison.OrdinalIgnoreCase);

            var workoutElem = root.Element("workout");
            if (workoutElem == null) return workout;

            int currentStartSec = 0;
            int segmentIndex = 0;

            foreach (var elem in workoutElem.Elements())
            {
                string tag = elem.Name.LocalName.ToLowerInvariant();

                switch (tag)
                {
                    case "warmup":
                    {
                        int duration = ParseInt(elem.Attribute("Duration")?.Value, 600);
                        double pLow = ParseDouble(elem.Attribute("PowerLow")?.Value, 0.40);
                        double pHigh = ParseDouble(elem.Attribute("PowerHigh")?.Value, 0.75);
                        int? cad = ParseNullableInt(elem.Attribute("Cadence")?.Value);

                        var seg = new WorkoutSegment
                        {
                            SegmentIndex = segmentIndex++,
                            SegmentType = "Warmup",
                            StartSec = currentStartSec,
                            DurationSec = duration,
                            TargetPowerLowPct = pLow,
                            TargetPowerHighPct = pHigh,
                            TargetCadence = cad,
                            TextEvents = ParseTextEvents(elem)
                        };
                        workout.Segments.Add(seg);
                        currentStartSec += duration;
                        break;
                    }

                    case "cooldown":
                    {
                        int duration = ParseInt(elem.Attribute("Duration")?.Value, 300);
                        double pLow = ParseDouble(elem.Attribute("PowerLow")?.Value, 0.60);
                        double pHigh = ParseDouble(elem.Attribute("PowerHigh")?.Value, 0.40);
                        int? cad = ParseNullableInt(elem.Attribute("Cadence")?.Value);

                        var seg = new WorkoutSegment
                        {
                            SegmentIndex = segmentIndex++,
                            SegmentType = "Cooldown",
                            StartSec = currentStartSec,
                            DurationSec = duration,
                            TargetPowerLowPct = pLow,
                            TargetPowerHighPct = pHigh,
                            TargetCadence = cad,
                            TextEvents = ParseTextEvents(elem)
                        };
                        workout.Segments.Add(seg);
                        currentStartSec += duration;
                        break;
                    }

                    case "steadystate":
                    {
                        int duration = ParseInt(elem.Attribute("Duration")?.Value, 300);
                        double power = ParseDouble(elem.Attribute("Power")?.Value, 0.75);
                        int? cad = ParseNullableInt(elem.Attribute("Cadence")?.Value);

                        var seg = new WorkoutSegment
                        {
                            SegmentIndex = segmentIndex++,
                            SegmentType = "Steady",
                            StartSec = currentStartSec,
                            DurationSec = duration,
                            TargetPowerLowPct = power,
                            TargetPowerHighPct = power,
                            TargetCadence = cad,
                            TextEvents = ParseTextEvents(elem)
                        };
                        workout.Segments.Add(seg);
                        currentStartSec += duration;
                        break;
                    }

                    case "ramp":
                    {
                        int duration = ParseInt(elem.Attribute("Duration")?.Value, 300);
                        double pLow = ParseDouble(elem.Attribute("PowerLow")?.Value, 0.50);
                        double pHigh = ParseDouble(elem.Attribute("PowerHigh")?.Value, 0.90);
                        int? cad = ParseNullableInt(elem.Attribute("Cadence")?.Value);

                        var seg = new WorkoutSegment
                        {
                            SegmentIndex = segmentIndex++,
                            SegmentType = "Ramp",
                            StartSec = currentStartSec,
                            DurationSec = duration,
                            TargetPowerLowPct = pLow,
                            TargetPowerHighPct = pHigh,
                            TargetCadence = cad,
                            TextEvents = ParseTextEvents(elem)
                        };
                        workout.Segments.Add(seg);
                        currentStartSec += duration;
                        break;
                    }

                    case "intervalst":
                    {
                        int repeat = ParseInt(elem.Attribute("Repeat")?.Value, 1);
                        int onDuration = ParseInt(elem.Attribute("OnDuration")?.Value, 60);
                        int offDuration = ParseInt(elem.Attribute("OffDuration")?.Value, 60);
                        double onPower = ParseDouble(elem.Attribute("OnPower")?.Value, 1.0);
                        double offPower = ParseDouble(elem.Attribute("OffPower")?.Value, 0.5);
                        int? cad = ParseNullableInt(elem.Attribute("Cadence")?.Value);
                        int? cadRest = ParseNullableInt(elem.Attribute("CadenceResting")?.Value);

                        var allTexts = ParseTextEvents(elem);

                        for (int r = 0; r < repeat; r++)
                        {
                            // On Interval
                            var onSeg = new WorkoutSegment
                            {
                                SegmentIndex = segmentIndex++,
                                SegmentType = "IntervalOn",
                                StartSec = currentStartSec,
                                DurationSec = onDuration,
                                TargetPowerLowPct = onPower,
                                TargetPowerHighPct = onPower,
                                TargetCadence = cad
                            };
                            // 最初のセットにテキストイベントを配置（または時間オフセットによるマッピング）
                            if (r == 0)
                            {
                                onSeg.TextEvents = allTexts.FindAll(t => t.TimeOffsetSec < onDuration);
                            }
                            workout.Segments.Add(onSeg);
                            currentStartSec += onDuration;

                            // Off Interval (レスト)
                            if (offDuration > 0)
                            {
                                var offSeg = new WorkoutSegment
                                {
                                    SegmentIndex = segmentIndex++,
                                    SegmentType = "IntervalOff",
                                    StartSec = currentStartSec,
                                    DurationSec = offDuration,
                                    TargetPowerLowPct = offPower,
                                    TargetPowerHighPct = offPower,
                                    TargetCadence = cadRest
                                };
                                workout.Segments.Add(offSeg);
                                currentStartSec += offDuration;
                            }
                        }
                        break;
                    }

                    case "freeride":
                    {
                        int duration = ParseInt(elem.Attribute("Duration")?.Value, 600);
                        var seg = new WorkoutSegment
                        {
                            SegmentIndex = segmentIndex++,
                            SegmentType = "FreeRide",
                            StartSec = currentStartSec,
                            DurationSec = duration,
                            TargetPowerLowPct = 0.60,
                            TargetPowerHighPct = 0.60,
                            TextEvents = ParseTextEvents(elem)
                        };
                        workout.Segments.Add(seg);
                        currentStartSec += duration;
                        break;
                    }
                }
            }

            workout.TotalDurationSec = currentStartSec;
            return workout;
        }

        public static WorkoutDefinition ParseFile(string filePath)
        {
            string xml = File.ReadAllText(filePath);
            var workout = Parse(xml);
            if (string.IsNullOrWhiteSpace(workout.Title) || workout.Title == "Untitled Workout")
            {
                workout.Title = Path.GetFileNameWithoutExtension(filePath);
            }
            return workout;
        }

        private static List<WorkoutTextEvent> ParseTextEvents(XElement parent)
        {
            var list = new List<WorkoutTextEvent>();
            foreach (var te in parent.Elements("textevent"))
            {
                int offset = ParseInt(te.Attribute("timeoffset")?.Value, 0);
                string msg = te.Attribute("message")?.Value?.Trim() ?? "";
                int dur = ParseInt(te.Attribute("duration")?.Value, 8);
                if (!string.IsNullOrEmpty(msg))
                {
                    list.Add(new WorkoutTextEvent
                    {
                        TimeOffsetSec = offset,
                        Message = msg,
                        DisplayDurationSec = dur > 0 ? dur : 8
                    });
                }
            }
            return list;
        }

        private static int ParseInt(string? val, int defaultVal)
        {
            if (string.IsNullOrWhiteSpace(val)) return defaultVal;
            return int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out int result) ? result : defaultVal;
        }

        private static int? ParseNullableInt(string? val)
        {
            if (string.IsNullOrWhiteSpace(val)) return null;
            return int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out int result) ? result : null;
        }

        private static double ParseDouble(string? val, double defaultVal)
        {
            if (string.IsNullOrWhiteSpace(val)) return defaultVal;
            return double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double result) ? result : defaultVal;
        }
    }
}
