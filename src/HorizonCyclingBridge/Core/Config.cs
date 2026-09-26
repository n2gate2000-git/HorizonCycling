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
        public int DefaultMode { get; set; } = 2;
        public double TrainerDifficulty { get; set; } = 0.5;
        public double Ftp { get; set; } = 200.0;
        public bool PedalBrakeEnabled { get; set; } = false; // ペダルブレーキ有効フラグ（デフォルトOFF）

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
