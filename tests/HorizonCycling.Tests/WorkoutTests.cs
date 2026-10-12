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

        [Fact]
        public void WorkoutLibraryManager_CreatesStandardRampTestCorrectly()
        {
            var ramp = WorkoutLibraryManager.CreateStandardRampTest(200.0);

            Assert.True(ramp.IsRampTest);
            Assert.Contains("Ramp Test", ramp.Title);
            Assert.True(ramp.Segments.Count >= 20);

            // 最初は Warmup 300秒 100W (100 / 200 = 0.5)
            Assert.Equal("Warmup", ramp.Segments[0].SegmentType);
            Assert.Equal(300, ramp.Segments[0].DurationSec);
            Assert.Equal(0.5, ramp.Segments[0].TargetPowerLowPct);

            // ステップ1: 100W (60秒)
            Assert.Equal(60, ramp.Segments[1].DurationSec);
            Assert.Equal(0.5, ramp.Segments[1].TargetPowerLowPct);

            // ステップ2: 120W (60秒) (120 / 200 = 0.6)
            Assert.Equal(60, ramp.Segments[2].DurationSec);
            Assert.Equal(0.6, ramp.Segments[2].TargetPowerLowPct);
        }

        [Fact]
        public void WorkoutEngine_RampTest_LowCadenceTriggersFailureAndCalculatesFtp()
        {
            var ramp = WorkoutLibraryManager.CreateStandardRampTest(200.0);
            var engine = new WorkoutEngine();

            RampTestResult? capturedResult = null;
            engine.OnRampTestCompleted += res => capturedResult = res;

            engine.Start(ramp, 200.0);
            Assert.True(engine.IsRunning);

            // 1. ウォームアップをスキップして本番ランプステップへ
            engine.SkipStep();
            var snap = engine.CreateSnapshot();
            Assert.Equal(1, snap.CurrentSegmentIndex);

            // 2. 60秒間 320W で順調にペダリング (ケイデンス90rpm)
            for (int i = 0; i < 60; i++)
            {
                engine.Update(1.0, 200.0, actualPower: 320.0, actualCadence: 90.0);
            }

            Assert.Null(capturedResult); // まだ継続中

            // 3. 疲労困憊でケイデンスが45rpmに低下 (< 50rpm)
            // 1秒目
            engine.Update(1.0, 200.0, actualPower: 300.0, actualCadence: 45.0);
            Assert.Null(capturedResult);

            // 2秒目
            engine.Update(1.0, 200.0, actualPower: 280.0, actualCadence: 42.0);
            Assert.Null(capturedResult);

            // 3秒目 (3秒継続で限界判定到達！)
            engine.Update(1.0, 200.0, actualPower: 250.0, actualCadence: 40.0);

            // 限界判定が自動発火したことを検証
            Assert.NotNull(capturedResult);
            Assert.Equal(200.0, capturedResult.OldFtp);
            // 直近60秒最大平均はおよそ320W -> 320 * 0.75 = 240W
            Assert.True(capturedResult.NewFtp >= 235.0 && capturedResult.NewFtp <= 245.0);
            Assert.True(capturedResult.FtpDelta > 0);

            // クールダウン（軽負荷）に自動切り替えされたことを確認
            var cooldownSnap = engine.CreateSnapshot();
            Assert.True(cooldownSnap.TargetWatts <= 100);
            Assert.NotNull(cooldownSnap.RampResult);
        }

        [Fact]
        public void WorkoutEngine_CountdownAndSegmentTransitions_ProvideAccurateTimingForSoundCues()
        {
            var def = new WorkoutDefinition
            {
                Title = "Interval & Rest Transition Test",
                Segments = new System.Collections.Generic.List<WorkoutSegment>
                {
                    new WorkoutSegment { SegmentType = "Warmup", DurationSec = 10, TargetPowerLowPct = 0.5, TargetPowerHighPct = 0.5 },
                    new WorkoutSegment { SegmentType = "IntervalOn", DurationSec = 10, TargetPowerLowPct = 1.0, TargetPowerHighPct = 1.0 },
                    new WorkoutSegment { SegmentType = "IntervalOff", DurationSec = 10, TargetPowerLowPct = 0.5, TargetPowerHighPct = 0.5 }
                }
            };
            def.TotalDurationSec = 30;

            var engine = new WorkoutEngine();
            engine.Start(def, 200.0);

            // 7秒経過時 (残り3秒) -> カウントダウン 3
            for (int i = 0; i < 7; i++) engine.Update(1.0, 200.0);
            var snap7 = engine.CreateSnapshot();
            Assert.Equal(0, snap7.CurrentSegmentIndex);
            Assert.Equal(3, snap7.SegmentRemainingSec);

            // 8秒経過時 (残り2秒) -> カウントダウン 2
            engine.Update(1.0, 200.0);
            var snap8 = engine.CreateSnapshot();
            Assert.Equal(2, snap8.SegmentRemainingSec);

            // 9秒経過時 (残り1秒) -> カウントダウン 1
            engine.Update(1.0, 200.0);
            var snap9 = engine.CreateSnapshot();
            Assert.Equal(1, snap9.SegmentRemainingSec);

            // 10秒経過時 (セグメント0完了 ➔ セグメント1 IntervalOn 突入)
            engine.Update(1.0, 200.0);
            var snap10 = engine.CreateSnapshot();
            Assert.Equal(1, snap10.CurrentSegmentIndex);
            Assert.Equal("IntervalOn", snap10.CurrentSegmentType);
            Assert.Equal(200, snap10.TargetWatts); // 1.0 * 200W

            // IntervalOn の残り3秒カウントダウン (7秒後)
            for (int i = 0; i < 7; i++) engine.Update(1.0, 200.0);
            var snap17 = engine.CreateSnapshot();
            Assert.Equal(1, snap17.CurrentSegmentIndex);
            Assert.Equal(3, snap17.SegmentRemainingSec);

            // セグメント1完了 ➔ セグメント2 IntervalOff (Rest) 突入 (3秒後)
            for (int i = 0; i < 3; i++) engine.Update(1.0, 200.0);
            var snap20 = engine.CreateSnapshot();
            Assert.Equal(2, snap20.CurrentSegmentIndex);
            Assert.Equal("IntervalOff", snap20.CurrentSegmentType);
            Assert.Equal(100, snap20.TargetWatts); // 0.5 * 200W

            // 全セグメント完了 (Finished)
            for (int i = 0; i < 10; i++) engine.Update(1.0, 200.0);
            var snapFinal = engine.CreateSnapshot();
            Assert.Equal("Finished", snapFinal.State);
        }

        [Fact]
        public void WorkoutEngine_NextSegmentPreview_AccuratelyPopulatesNextSegmentInfoAt30Seconds()
        {
            var def = new WorkoutDefinition
            {
                Title = "Next Segment 30s Preview Test",
                Segments = new System.Collections.Generic.List<WorkoutSegment>
                {
                    new WorkoutSegment 
                    { 
                        SegmentType = "Warmup", 
                        DurationSec = 60, 
                        TargetPowerLowPct = 0.5, 
                        TargetPowerHighPct = 0.5 
                    },
                    new WorkoutSegment 
                    { 
                        SegmentType = "IntervalOn", 
                        DurationSec = 180, 
                        TargetPowerLowPct = 1.05, 
                        TargetPowerHighPct = 1.05,
                        TargetCadence = 95
                    }
                }
            };
            def.TotalDurationSec = 240;

            var engine = new WorkoutEngine();
            engine.Start(def, 200.0); // FTP = 200W

            // 30秒経過 (残り30秒地点)
            for (int i = 0; i < 30; i++) engine.Update(1.0, 200.0);

            var snap30 = engine.CreateSnapshot();
            Assert.Equal(0, snap30.CurrentSegmentIndex);
            Assert.Equal(30, snap30.SegmentRemainingSec);

            // 次のセグメントの予告情報が正しくセットされていること
            Assert.Equal("IntervalOn", snap30.NextSegmentType);
            Assert.Equal(180, snap30.NextDurationSec);
            Assert.Equal(210, snap30.NextTargetWatts); // 1.05 * 200W = 210W
            Assert.Equal(95, snap30.NextTargetCadence);
            Assert.Equal(5, snap30.NextPowerZone); // 1.05 = Z5 (VO2Max)
        }
    }
}
