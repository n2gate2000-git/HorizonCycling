using System;
using System.Collections.Generic;

namespace HorizonCyclingBridge.Workout
{
    /// <summary>
    /// ワークアウト全体の定義
    /// </summary>
    public class WorkoutDefinition
    {
        public string Title { get; set; } = "Untitled Workout";
        public string Author { get; set; } = "Unknown";
        public string Description { get; set; } = "";
        public string SportType { get; set; } = "bike";
        public List<string> Tags { get; set; } = new();
        public int TotalDurationSec { get; set; }
        public List<WorkoutSegment> Segments { get; set; } = new();

        // 算出スタッツ (FTPに基づく)
        public double EstimatedTss { get; set; }
        public double EstimatedIf { get; set; }
        public double EstimatedKcal { get; set; }
        public string PrimaryZone { get; set; } = "Z3";
    }

    /// <summary>
    /// ワークアウトを構成する個々のセグメント（ブロック）
    /// </summary>
    public class WorkoutSegment
    {
        public int SegmentIndex { get; set; }
        /// <summary>
        /// セグメント種別: Warmup, Steady, IntervalOn, IntervalOff, Ramp, Cooldown, FreeRide
        /// </summary>
        public string SegmentType { get; set; } = "Steady";
        public int StartSec { get; set; }
        public int DurationSec { get; set; }
        public double TargetPowerLowPct { get; set; }   // 0.88 = 88% FTP
        public double TargetPowerHighPct { get; set; }  // ランプ時は異なる
        public int? TargetCadence { get; set; }
        public List<WorkoutTextEvent> TextEvents { get; set; } = new();

        /// <summary>
        /// ランプ（傾斜）ブロックかどうか
        /// </summary>
        public bool IsRamp => Math.Abs(TargetPowerHighPct - TargetPowerLowPct) > 0.001;
    }

    /// <summary>
    /// セグメント内の指定秒数で表示するコーチングテキスト
    /// </summary>
    public class WorkoutTextEvent
    {
        public int TimeOffsetSec { get; set; }
        public string Message { get; set; } = "";
        public int DisplayDurationSec { get; set; } = 8;
    }

    /// <summary>
    /// ワークアウトライブラリ一覧表示用サマリー
    /// </summary>
    public class WorkoutSummaryItem
    {
        public string Id { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Category { get; set; } = "Custom";
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public int TotalDurationSec { get; set; }
        public string FormattedDuration => $"{TotalDurationSec / 60}m {TotalDurationSec % 60:D2}s";
        public double EstimatedTss { get; set; }
        public double EstimatedIf { get; set; }
        public string PrimaryZone { get; set; } = "Z3";
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>
    /// UIおよびオーバーレイへ30Hz/1Hzで通知されるワークアウト実行中スナップショット
    /// </summary>
    public class WorkoutStateSnapshot
    {
        public string State { get; set; } = "Idle"; // Idle, Running, Paused, Finished
        public bool IsActive => State == "Running" || State == "Paused";
        public string WorkoutTitle { get; set; } = "";
        public int TotalDurationSec { get; set; }
        public int ElapsedSec { get; set; }
        public int RemainingSec { get; set; }
        public double ProgressPct => TotalDurationSec > 0 ? Math.Clamp((double)ElapsedSec / TotalDurationSec * 100.0, 0.0, 100.0) : 0.0;

        // 現在セグメント情報
        public int CurrentSegmentIndex { get; set; }
        public int TotalSegmentsCount { get; set; }
        public string CurrentSegmentType { get; set; } = "Steady";
        public int SegmentElapsedSec { get; set; }
        public int SegmentRemainingSec { get; set; }
        public int SegmentTotalDurationSec { get; set; }

        // 目標値と実測値
        public int TargetWatts { get; set; }
        public int? TargetCadence { get; set; }
        public double TargetPowerPct { get; set; } // % of FTP
        public int CurrentPowerZone { get; set; } = 1; // 1〜7
        public double IntensityBias { get; set; } = 1.0; // 0.8〜1.2

        // アクティブなメッセージ（字幕）
        public string? ActiveMessage { get; set; }

        // 次ブロックの予告
        public int? NextTargetWatts { get; set; }
        public int? NextDurationSec { get; set; }

        // 各ブロックのリスト（プロファイル用）
        public List<WorkoutSegmentSnapshotItem> Segments { get; set; } = new();
    }

    public class WorkoutSegmentSnapshotItem
    {
        public int Index { get; set; }
        public string Type { get; set; } = "Steady";
        public int StartSec { get; set; }
        public int DurationSec { get; set; }
        public int TargetWattsLow { get; set; }
        public int TargetWattsHigh { get; set; }
        public int Zone { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsCurrent { get; set; }
    }
}
