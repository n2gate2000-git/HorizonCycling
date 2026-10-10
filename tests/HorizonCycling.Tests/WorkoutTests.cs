using System;
using System.IO;
using System.Linq;
using HorizonCyclingBridge.Workout;
using Xunit;

namespace HorizonCycling.Tests
{
    public class WorkoutTests
    {
        [Fact]
        public void ZwoParser_ParsesAllBlocksCorrectly()
        {
            string zwo = @"<workout_file>
  <author>Test Coach</author>
  <name>SST 30m Test</name>
  <description>Test workout for SST</description>
  <sportType>bike</sportType>
  <tags><tag name=""Intervals""/><tag name=""SST""/></tags>
  <workout>
    <Warmup Duration=""300"" PowerLow=""0.50"" PowerHigh=""0.75"" Cadence=""90"">
      <textevent timeoffset=""10"" message=""Welcome to SST""/>
    </Warmup>
    <IntervalsT Repeat=""2"" OnDuration=""180"" OffDuration=""60"" OnPower=""0.88"" OffPower=""0.50"" Cadence=""92"" CadenceResting=""85""/>
    <SteadyState Duration=""120"" Power=""0.90"" Cadence=""90""/>
    <Cooldown Duration=""180"" PowerLow=""0.60"" PowerHigh=""0.40"" Cadence=""80""/>
  </workout>
</workout_file>";

            var def = ZwoParser.Parse(zwo);

            Assert.Equal("Test Coach", def.Author);
            Assert.Equal("SST 30m Test", def.Title);
            Assert.Equal(2, def.Tags.Count);

            // ブロック数: Warmup(1) + IntervalsT(2オン+2オフ = 4) + Steady(1) + Cooldown(1) = 7
            Assert.Equal(7, def.Segments.Count);

            // 総時間: 300 + (180+60)*2 + 120 + 180 = 300 + 480 + 120 + 180 = 1080秒 (18分)
            Assert.Equal(1080, def.TotalDurationSec);

            // Warmup
            var w = def.Segments[0];
            Assert.Equal("Warmup", w.SegmentType);
            Assert.Equal(300, w.DurationSec);
            Assert.Equal(0.50, w.TargetPowerLowPct);
            Assert.Equal(0.75, w.TargetPowerHighPct);
            Assert.Single(w.TextEvents);
            Assert.Equal("Welcome to SST", w.TextEvents[0].Message);

            // Interval 1 On
            var on1 = def.Segments[1];
            Assert.Equal("IntervalOn", on1.SegmentType);
            Assert.Equal(180, on1.DurationSec);
            Assert.Equal(0.88, on1.TargetPowerLowPct);
            Assert.Equal(92, on1.TargetCadence);

            // Interval 1 Off
            var off1 = def.Segments[2];
            Assert.Equal("IntervalOff", off1.SegmentType);
            Assert.Equal(60, off1.DurationSec);
            Assert.Equal(0.50, off1.TargetPowerLowPct);
            Assert.Equal(85, off1.TargetCadence);
        }

        [Fact]
        public void MrcParser_ParsesValidMrc()
        {
            string mrc = @"[COURSE HEADER]
VERSION = 2
UNITS = METRIC
DESCRIPTION = Test MRC workout
FILE NAME = 2x10min Intervals
MINUTES PERCENT
[END COURSE HEADER]
[COURSE DATA]
0.0 50.0
5.0 75.0
5.0 100.0
15.0 100.0
15.0 50.0
20.0 50.0
[END COURSE DATA]";

            var def = MrcParser.Parse(mrc, 200.0);

            Assert.Equal("2x10min Intervals", def.Title);
            Assert.Equal(3, def.Segments.Count);
            // 0〜5m (Warmup, 50%->75%), 5〜15m (100%), 15〜20m (50%)
            Assert.Equal(1200, def.TotalDurationSec); // 20分 = 1200秒

            Assert.Equal("Warmup", def.Segments[0].SegmentType);
            Assert.Equal(0.50, def.Segments[0].TargetPowerLowPct);
            Assert.Equal(0.75, def.Segments[0].TargetPowerHighPct);

            Assert.Equal(1.00, def.Segments[1].TargetPowerLowPct);
            Assert.Equal(600, def.Segments[1].DurationSec); // 10分
        }

        [Fact]
        public void WorkoutLibraryManager_CalculatesStatsCorrectly()
        {
            var def = new WorkoutDefinition
            {
                TotalDurationSec = 3600 // 1時間
            };
            // 1時間ずっと 100% FTP (200W) で走った場合: IF = 1.0, TSS = 100
            def.Segments.Add(new WorkoutSegment
            {
                StartSec = 0,
                DurationSec = 3600,
                TargetPowerLowPct = 1.0,
                TargetPowerHighPct = 1.0
            });

            WorkoutLibraryManager.CalculateStats(def, 200.0);

            Assert.Equal(1.0, def.EstimatedIf);
            Assert.Equal(100.0, def.EstimatedTss);
            Assert.Equal("Z4", def.PrimaryZone); // 100%はZ4 (閾値)
        }

        [Fact]
        public void WorkoutEngine_RunsAndCalculatesTargetPower()
        {
            var def = new WorkoutDefinition
            {
                TotalDurationSec = 600,
                Segments = new()
                {
                    new WorkoutSegment
                    {
                        SegmentIndex = 0,
                        SegmentType = "Warmup",
                        StartSec = 0,
                        DurationSec = 300,
                        TargetPowerLowPct = 0.50,
                        TargetPowerHighPct = 0.50,
                        TargetCadence = 90,
                        TextEvents = new() { new WorkoutTextEvent { TimeOffsetSec = 0, Message = "Start!" } }
                    },
                    new WorkoutSegment
                    {
                        SegmentIndex = 1,
                        SegmentType = "Steady",
                        StartSec = 300,
                        DurationSec = 300,
                        TargetPowerLowPct = 1.00,
                        TargetPowerHighPct = 1.00,
                        TargetCadence = 95
                    }
                }
            };

            var engine = new WorkoutEngine();
            string? capturedMsg = null;
            engine.OnMessageAlert += msg => capturedMsg = msg;

            int lastTargetWatts = 0;
            engine.OnTargetPowerChanged += w => lastTargetWatts = w;

            // FTP 200W で開始
            engine.Start(def, 200.0);

            Assert.True(engine.IsRunning);
            Assert.Equal("Start!", capturedMsg);
            // 50% of 200W = 100W
            Assert.Equal(100, lastTargetWatts);

            // 100秒進める
            engine.Update(100.0, 200.0);
            var snap = engine.CreateSnapshot();
            Assert.Equal(100, snap.ElapsedSec);
            Assert.Equal(0, snap.CurrentSegmentIndex);
            Assert.Equal(100, snap.TargetWatts);

            // バイアスを110%にする (100W * 1.1 = 110W)
            engine.SetIntensityBias(1.1);
            Assert.Equal(110, engine.CreateSnapshot().TargetWatts);

            // 次のステップ（100% FTP = 200W * 1.1 = 220W）にスキップ
            engine.SkipStep();
            snap = engine.CreateSnapshot();
            Assert.Equal(1, snap.CurrentSegmentIndex);
            Assert.Equal(220, snap.TargetWatts);
            Assert.Equal(95, snap.TargetCadence);

            // 終了
            engine.Stop();
            Assert.False(engine.IsActive);
        }
    }
}
