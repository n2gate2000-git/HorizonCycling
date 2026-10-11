using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HorizonCyclingBridge.Core
{
    public enum SensorType
    {
        None,
        Ftms,
        CyclingPower
    }

    public class AppConfig
    {
        public SensorType PowerSourceType { get; set; } = SensorType.None;
        public ulong PowerSourceMacAddress { get; set; } = 0;
        public string PowerSourceName { get; set; } = string.Empty;

        // 心拍計 (Heart Rate Monitor) 設定
        public ulong HeartRateSourceMacAddress { get; set; } = 0;
        public string HeartRateSourceName { get; set; } = string.Empty;
        public bool HeartRateEnabled { get; set; } = true;
        public int MaxHeartRate { get; set; } = 190;
        public int RestingHeartRate { get; set; } = 60; // 安静時心拍数 (デフォルト60bpm)

        // セカンダリ ケイデンス / パワーメーター (Magene PES-P505 等) 設定
        public ulong CadenceSourceMacAddress { get; set; } = 0;
        public string CadenceSourceName { get; set; } = string.Empty;
        public SensorType CadenceSourceType { get; set; } = SensorType.None;
        public bool CadenceSourceEnabled { get; set; } = true;
        public bool PreferPowerMeterWatts { get; set; } = true; // パワーメーター接続時、パワー値もパワーメーターを優先するか

        public int DefaultMode { get; set; } = 2;
        public double TrainerDifficulty { get; set; } = 0.5;
        public double Ftp { get; set; } = 200.0;
        public double RiderWeightKg { get; set; } = 70.0; // ライダー体重 (デフォルト 70.0kg)
        public bool PedalBrakeEnabled { get; set; } = false; // ペダルブレーキ有効フラグ（デフォルトOFF）
        public bool FreerideOverlayEnabled { get; set; } = false; // 通常走行時の最前面HUDオーバーレイ表示（オプション、デフォルトOFF）
        public int ProfileRecentDistanceM { get; set; } = 200; // 直近プロファイル表示距離（100m〜1000m、100m刻み、デフォルト200m）
        public string Language { get; set; } = "ja"; // 表示言語設定 ("ja" または "en")

        // マップ座標変換パラメータ
        public double MapOriginX { get; set; } = 0.0;
        public double MapOriginZ { get; set; } = 0.0;
        public double MapScale { get; set; } = 0.263;
        public bool InvertZ { get; set; } = true; // DirectX Z軸（北奥）と地図画像Y軸（南下）の整合のためデフォルトTrue
    }

    public static class ConfigManager
    {
        private static string GetConfigFilePath()
        {
            string baseDirFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            if (File.Exists(baseDirFile))
            {
                return baseDirFile;
            }
            if (File.Exists("config.json"))
            {
                return Path.GetFullPath("config.json");
            }
            return baseDirFile;
        }

        public static AppConfig Load()
        {
            string path = GetConfigFilePath();
            if (!File.Exists(path))
            {
                return new AppConfig();
            }

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Failed to load config.json: {ex.Message}");
                return new AppConfig();
            }
        }

        public static void Save(AppConfig config)
        {
            try
            {
                string path = GetConfigFilePath();
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Failed to save config.json: {ex.Message}");
            }
        }
    }
}
