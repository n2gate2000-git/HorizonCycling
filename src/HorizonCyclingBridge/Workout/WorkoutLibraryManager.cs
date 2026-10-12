using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HorizonCyclingBridge.Workout
{
    /// <summary>
    /// workouts/ フォルダ内のワークアウトファイルの探索・管理・メタデータ算出を行うマネージャー
    /// </summary>
    public class WorkoutLibraryManager
    {
        private readonly string _workoutsRootDir;
        private double _ftp = 200.0;

        public WorkoutLibraryManager(string? baseDir = null, double ftp = 200.0)
        {
            _ftp = ftp > 0 ? ftp : 200.0;
            string root = baseDir ?? AppDomain.CurrentDomain.BaseDirectory;
            
            // 開発環境と配布環境の探索
            string candidate = Path.Combine(root, "workouts");
            if (!Directory.Exists(candidate))
            {
                // プロジェクトルート等の探索
                string devCandidate = Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "workouts"));
                if (Directory.Exists(devCandidate))
                {
                    candidate = devCandidate;
                }
            }

            _workoutsRootDir = candidate;
            EnsureDefaultWorkouts();
        }

        public string WorkoutsDirectory => _workoutsRootDir;

        public void SetFtp(double ftp)
        {
            if (ftp > 0) _ftp = ftp;
        }

        /// <summary>
        /// 全ワークアウトのサマリー一覧を取得（カテゴリ別にソート）
        /// </summary>
        public List<WorkoutSummaryItem> GetLibrary()
        {
            EnsureDefaultWorkouts();
            var list = new List<WorkoutSummaryItem>();

            if (!Directory.Exists(_workoutsRootDir)) return list;

            var files = Directory.GetFiles(_workoutsRootDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".zwo", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".mrc", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".erg", StringComparison.OrdinalIgnoreCase));

            foreach (var file in files)
            {
                try
                {
                    var item = ParseSummary(file);
                    if (item != null)
                    {
                        list.Add(item);
                    }
                }
                catch
                {
                    // パースエラーの個別ファイルはスキップ
                }
            }

            // カテゴリ順、タイトル順でソート
            return list.OrderBy(x => x.Category).ThenBy(x => x.Title).ToList();
        }

        /// <summary>
        /// 単一のワークアウト詳細定義をロード
        /// </summary>
        public WorkoutDefinition? LoadWorkout(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            WorkoutDefinition def;
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".zwo")
            {
                def = ZwoParser.ParseFile(filePath);
            }
            else
            {
                def = MrcParser.ParseFile(filePath, _ftp);
            }

            CalculateStats(def, _ftp);
            return def;
        }

        /// <summary>
        /// 標準のFTPランプテスト定義を動的に生成
        /// (ウォームアップ100W、以降1分毎に+20Wずつ段階上昇、ケイデンス50rpm未満で自動判定)
        /// </summary>
        public static WorkoutDefinition CreateStandardRampTest(double currentFtp)
        {
            double ftp = currentFtp > 0 ? currentFtp : 200.0;
            var def = new WorkoutDefinition
            {
                Title = "FTP Ramp Test (自動計測)",
                Author = "HorizonCycling Studio",
                Description = "100Wから開始し、1分ごとに20Wずつ目標負荷が上昇するFTP自動計測テスト。ケイデンスが50rpmを下回ると限界到達と判定され自動終了します。",
                SportType = "bike",
                Tags = new List<string> { "FTP", "Test", "RampTest" },
                IsRampTest = true
            };

            int startSec = 0;

            // 1. ウォームアップ (5分間, 100W固定)
            def.Segments.Add(new WorkoutSegment
            {
                SegmentIndex = 0,
                SegmentType = "Warmup",
                DurationSec = 300,
                StartSec = startSec,
                TargetPowerLowPct = 100.0 / ftp,
                TargetPowerHighPct = 100.0 / ftp,
                TargetCadence = 90,
                TextEvents = new List<WorkoutTextEvent>
                {
                    new WorkoutTextEvent { TimeOffsetSec = 0, Message = "⚡ FTP Ramp Test 開始！まずは100Wで5分間のウォームアップです。" },
                    new WorkoutTextEvent { TimeOffsetSec = 150, Message = "ケイデンス90rpm前後を意識してリラックスしましょう。" },
                    new WorkoutTextEvent { TimeOffsetSec = 280, Message = "まもなくランプステップ開始！1分ごとに20Wずつ上がります。" }
                }
            });
            startSec += 300;

            // 2. ランプステップ (100Wから20W刻みで最大500Wまで、各60秒)
            int stepIndex = 1;
            for (int watts = 100; watts <= 500; watts += 20)
            {
                var seg = new WorkoutSegment
                {
                    SegmentIndex = stepIndex++,
                    SegmentType = "SteadyState",
                    DurationSec = 60,
                    StartSec = startSec,
                    TargetPowerLowPct = watts / ftp,
                    TargetPowerHighPct = watts / ftp,
                    TargetCadence = 90
                };

                if (watts == 100)
                {
                    seg.TextEvents.Add(new WorkoutTextEvent { TimeOffsetSec = 0, Message = $"ステップ 1: {watts}W (1分間)" });
                }
                else if (watts == (int)Math.Round(ftp))
                {
                    seg.TextEvents.Add(new WorkoutTextEvent { TimeOffsetSec = 0, Message = $"🔥 現在のFTP({watts}W)に到達！ここからが本番の限界挑戦！" });
                }
                else if (watts % 40 == 0)
                {
                    seg.TextEvents.Add(new WorkoutTextEvent { TimeOffsetSec = 0, Message = $"ステップ: {watts}W！一定のケイデンスを維持！" });
                }

                def.Segments.Add(seg);
                startSec += 60;
            }

            // 3. 予備クールダウン (5分間, 80W)
            def.Segments.Add(new WorkoutSegment
            {
                SegmentIndex = stepIndex,
                SegmentType = "Cooldown",
                DurationSec = 300,
                StartSec = startSec,
                TargetPowerLowPct = 80.0 / ftp,
                TargetPowerHighPct = 70.0 / ftp,
                TargetCadence = 85,
                TextEvents = new List<WorkoutTextEvent>
                {
                    new WorkoutTextEvent { TimeOffsetSec = 0, Message = "テスト終了！お疲れ様でした。軽くペダルを回してクールダウンしましょう。" }
                }
            });

            CalculateStats(def, ftp);
            return def;
        }

        /// <summary>
        /// 外部ファイルを Custom カテゴリへインポート
        /// </summary>
        public List<string> ImportFiles(IEnumerable<string> filePaths)
        {
            string customDir = Path.Combine(_workoutsRootDir, "Custom");
            Directory.CreateDirectory(customDir);

            var imported = new List<string>();
            foreach (var path in filePaths)
            {
                if (!File.Exists(path)) continue;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".zwo" && ext != ".mrc" && ext != ".erg") continue;

                string destFileName = Path.GetFileName(path);
                string destPath = Path.Combine(customDir, destFileName);

                // 同名衝突回避
                int count = 1;
                while (File.Exists(destPath))
                {
                    string nameOnly = Path.GetFileNameWithoutExtension(path);
                    destPath = Path.Combine(customDir, $"{nameOnly}_{count}{ext}");
                    count++;
                }

                File.Copy(path, destPath);
                imported.Add(destPath);
            }

            return imported;
        }

        private WorkoutSummaryItem? ParseSummary(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            WorkoutDefinition def;

            if (ext == ".zwo")
            {
                def = ZwoParser.ParseFile(filePath);
            }
            else
            {
                def = MrcParser.ParseFile(filePath, _ftp);
            }

            CalculateStats(def, _ftp);

            // カテゴリの判定（サブディレクトリ名）
            string relativePath = Path.GetRelativePath(_workoutsRootDir, filePath);
            string[] parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string category = parts.Length > 1 ? parts[0] : "General";

            // "01_FTP_Test" などのプレフィックスがあればきれいに整形
            string prettyCategory = category;
            if (prettyCategory.Length > 3 && char.IsDigit(prettyCategory[0]) && char.IsDigit(prettyCategory[1]) && prettyCategory[2] == '_')
            {
                prettyCategory = prettyCategory.Substring(3).Replace('_', ' ');
            }

            return new WorkoutSummaryItem
            {
                Id = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(filePath)),
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Category = prettyCategory,
                Title = string.IsNullOrWhiteSpace(def.Title) ? Path.GetFileNameWithoutExtension(filePath) : def.Title,
                Author = def.Author,
                Description = def.Description,
                TotalDurationSec = def.TotalDurationSec,
                EstimatedTss = def.EstimatedTss,
                EstimatedIf = def.EstimatedIf,
                PrimaryZone = def.PrimaryZone,
                Tags = def.Tags
            };
        }

        public static void CalculateStats(WorkoutDefinition def, double ftp)
        {
            if (def.TotalDurationSec <= 0 || def.Segments.Count == 0) return;

            // 秒ごとのパワー（W）をサンプリング
            int totalSec = def.TotalDurationSec;
            double sumPower = 0.0;
            double sumPower4 = 0.0;

            int[] zoneSecs = new int[8]; // Z1〜Z7

            foreach (var seg in def.Segments)
            {
                for (int s = 0; s < seg.DurationSec; s++)
                {
                    double ratio = seg.DurationSec > 1 ? (double)s / (seg.DurationSec - 1) : 0.0;
                    double pct = seg.TargetPowerLowPct + (seg.TargetPowerHighPct - seg.TargetPowerLowPct) * ratio;
                    double watts = pct * ftp;

                    sumPower += watts;
                    sumPower4 += Math.Pow(watts, 4);

                    int z = GetPowerZone(pct);
                    zoneSecs[z]++;
                }
            }

            double avgPower = sumPower / totalSec;
            // 概算 NP
            double np = Math.Pow(sumPower4 / totalSec, 0.25);
            if (np < avgPower) np = avgPower;

            double intensityFactor = np / (ftp > 0 ? ftp : 200.0);
            double tss = (totalSec * np * intensityFactor) / ((ftp > 0 ? ftp : 200.0) * 3600.0) * 100.0;

            // 消費カロリー概算 (Kj = Watts * Sec / 1000, 効率25%換算で kcal ≒ Kj)
            double kcal = (avgPower * totalSec) / 1000.0;

            // 最大滞在ゾーンの特定 (ウォームアップ・レスト以外の主強度)
            int maxZone = 1;
            int maxSec = -1;
            for (int z = 2; z <= 7; z++)
            {
                if (zoneSecs[z] > maxSec)
                {
                    maxSec = zoneSecs[z];
                    maxZone = z;
                }
            }
            if (maxSec <= 0) maxZone = 1;

            def.EstimatedIf = Math.Round(intensityFactor, 2);
            def.EstimatedTss = Math.Round(tss, 1);
            def.EstimatedKcal = Math.Round(kcal, 0);
            def.PrimaryZone = $"Z{maxZone}";
        }

        public static int GetPowerZone(double pctOfFtp)
        {
            if (pctOfFtp < 0.55) return 1;      // Z1: Active Recovery
            if (pctOfFtp < 0.75) return 2;      // Z2: Endurance
            if (pctOfFtp < 0.90) return 3;      // Z3: Tempo
            if (pctOfFtp < 1.05) return 4;      // Z4: Lactate Threshold (SST: 0.88-0.94)
            if (pctOfFtp < 1.20) return 5;      // Z5: VO2Max
            if (pctOfFtp < 1.50) return 6;      // Z6: Anaerobic Capacity
            return 7;                           // Z7: Neuromuscular Power
        }

        /// <summary>
        /// 初回起動時などにサンプルワークアウト群を自動生成
        /// </summary>
        private void EnsureDefaultWorkouts()
        {
            try
            {
                if (!Directory.Exists(_workoutsRootDir))
                {
                    Directory.CreateDirectory(_workoutsRootDir);
                }

                // 1. FTPテスト
                string ftpDir = Path.Combine(_workoutsRootDir, "01_FTP_Test");
                Directory.CreateDirectory(ftpDir);
                string rampPath = Path.Combine(ftpDir, "Ramp_Test.zwo");
                if (!File.Exists(rampPath))
                {
                    File.WriteAllText(rampPath, SampleWorkouts.RampTestZwo);
                }

                // 2. スイートスポット (SST)
                string sstDir = Path.Combine(_workoutsRootDir, "02_SweetSpot_SST");
                Directory.CreateDirectory(sstDir);
                string sstPath = Path.Combine(sstDir, "SST_Short_45m.zwo");
                if (!File.Exists(sstPath))
                {
                    File.WriteAllText(sstPath, SampleWorkouts.SstShortZwo);
                }

                // 3. VO2Max
                string vo2Dir = Path.Combine(_workoutsRootDir, "03_VO2Max_Intervals");
                Directory.CreateDirectory(vo2Dir);
                string gorbyPath = Path.Combine(vo2Dir, "The_Gorby_5x5min.zwo");
                if (!File.Exists(gorbyPath))
                {
                    File.WriteAllText(gorbyPath, SampleWorkouts.GorbyZwo);
                }

                // 4. 回復走
                string recDir = Path.Combine(_workoutsRootDir, "04_Endurance_Recovery");
                Directory.CreateDirectory(recDir);
                string recPath = Path.Combine(recDir, "Recovery_Spin_30m.zwo");
                if (!File.Exists(recPath))
                {
                    File.WriteAllText(recPath, SampleWorkouts.RecoveryZwo);
                }

            }
            catch
            {
                // ディレクトリ作成失敗は無視
            }
        }
    }

    /// <summary>
    /// 内蔵サンプルワークアウト定義
    /// </summary>
    internal static class SampleWorkouts
    {
        public const string RampTestZwo = @"<workout_file>
  <author>HorizonCycling Studio</author>
  <name>FTP Ramp Test</name>
  <description>1分ごとに20Wずつ負荷が上昇するFTP自動計測テスト。限界まで踏み切ってください。</description>
  <sportType>bike</sportType>
  <tags><tag name=""FTP""/><tag name=""Test""/></tags>
  <workout>
    <Warmup Duration=""300"" PowerLow=""0.45"" PowerHigh=""0.60"" Cadence=""90"">
      <textevent timeoffset=""0"" message=""ウォームアップ開始。軽く脚を回して心拍を上げましょう。""/>
      <textevent timeoffset=""150"" message=""リラックスして深い呼吸を心がけてください。""/>
    </Warmup>
    <SteadyState Duration=""60"" Power=""0.65"" Cadence=""90""/>
    <SteadyState Duration=""60"" Power=""0.75"" Cadence=""90""/>
    <SteadyState Duration=""60"" Power=""0.85"" Cadence=""90""/>
    <SteadyState Duration=""60"" Power=""0.95"" Cadence=""90""/>
    <SteadyState Duration=""60"" Power=""1.05"" Cadence=""90"">
      <textevent timeoffset=""0"" message=""FTP領域突入！ここからが本番です。""/>
    </SteadyState>
    <SteadyState Duration=""60"" Power=""1.15"" Cadence=""90""/>
    <SteadyState Duration=""60"" Power=""1.25"" Cadence=""90""/>
    <SteadyState Duration=""60"" Power=""1.35"" Cadence=""90"">
      <textevent timeoffset=""0"" message=""粘れ！ペダリングを止めないで！""/>
    </SteadyState>
    <SteadyState Duration=""60"" Power=""1.45"" Cadence=""90""/>
    <SteadyState Duration=""60"" Power=""1.55"" Cadence=""90""/>
    <Cooldown Duration=""300"" PowerLow=""0.50"" PowerHigh=""0.35"" Cadence=""85"">
      <textevent timeoffset=""0"" message=""テスト終了！お疲れ様でした。呼吸を整えましょう。""/>
    </Cooldown>
  </workout>
</workout_file>";

        public const string SstShortZwo = @"<workout_file>
  <author>HorizonCycling Studio</author>
  <name>SST (Sweet Spot Training) 45m</name>
  <description>有酸素能力と閾値パワーを効率的に高める王道スイートスポットインターバル (88% FTP)。</description>
  <sportType>bike</sportType>
  <tags><tag name=""SST""/><tag name=""Threshold""/></tags>
  <workout>
    <Warmup Duration=""600"" PowerLow=""0.45"" PowerHigh=""0.75"" Cadence=""90"">
      <textevent timeoffset=""0"" message=""ウォームアップ開始。ケイデンス90を目安に徐々に負荷を上げます。""/>
      <textevent timeoffset=""300"" message=""中盤突入。背筋を伸ばし、体幹を安定させましょう。""/>
    </Warmup>
    <IntervalsT Repeat=""3"" OnDuration=""300"" OffDuration=""120"" OnPower=""0.88"" OffPower=""0.55"" Cadence=""90"" CadenceResting=""85"">
      <textevent timeoffset=""0"" message=""SSTインターバル開始！88% FTPをキープ！""/>
    </IntervalsT>
    <Cooldown Duration=""300"" PowerLow=""0.60"" PowerHigh=""0.40"" Cadence=""85"">
      <textevent timeoffset=""0"" message=""クールダウンです。脚の力を抜き、しっかり呼吸を整えてください。""/>
    </Cooldown>
  </workout>
</workout_file>";

        public const string GorbyZwo = @"<workout_file>
  <author>HorizonCycling Studio</author>
  <name>The Gorby (5x5min VO2Max)</name>
  <description>最大酸素摂取量(VO2Max)を限界まで鍛え上げる5分×5本の高強度インターバル。</description>
  <sportType>bike</sportType>
  <tags><tag name=""VO2Max""/><tag name=""Intervals""/></tags>
  <workout>
    <Warmup Duration=""600"" PowerLow=""0.40"" PowerHigh=""0.80"" Cadence=""90"">
      <textevent timeoffset=""0"" message=""ウォームアップ。脚を温めて高強度に備えましょう。""/>
    </Warmup>
    <IntervalsT Repeat=""5"" OnDuration=""300"" OffDuration=""300"" OnPower=""1.10"" OffPower=""0.55"" Cadence=""95"" CadenceResting=""85"">
      <textevent timeoffset=""0"" message=""VO2Maxアタック！110% FTP！ハイケイデンスで乗り切ろう！""/>
    </IntervalsT>
    <Cooldown Duration=""300"" PowerLow=""0.50"" PowerHigh=""0.35"" Cadence=""85"">
      <textevent timeoffset=""0"" message=""全セット完了！素晴らしい追い込みでした！""/>
    </Cooldown>
  </workout>
</workout_file>";

        public const string RecoveryZwo = @"<workout_file>
  <author>HorizonCycling Studio</author>
  <name>Recovery Spin 30m</name>
  <description>疲労抜き・アクティブリカバリーのための軽やかな30分スピンライド。</description>
  <sportType>bike</sportType>
  <tags><tag name=""Recovery""/></tags>
  <workout>
    <Warmup Duration=""300"" PowerLow=""0.40"" PowerHigh=""0.50"" Cadence=""90""/>
    <SteadyState Duration=""1200"" Power=""0.52"" Cadence=""90"">
      <textevent timeoffset=""0"" message=""心拍を上げず、ペダルの重さを感じない軽快な回転を意識しましょう。""/>
      <textevent timeoffset=""600"" message=""リラックス。風景を楽しみながらペダリングしましょう。""/>
    </SteadyState>
    <Cooldown Duration=""300"" PowerLow=""0.50"" PowerHigh=""0.35"" Cadence=""85""/>
  </workout>
</workout_file>";
    }
}
