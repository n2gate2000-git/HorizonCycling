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
    }

    public static class ConfigManager
    {
        private const string CONFIG_FILE = "config.json";

        public static AppConfig Load()
        {
            if (!File.Exists(CONFIG_FILE))
            {
                return new AppConfig();
            }

            try
            {
                string json = File.ReadAllText(CONFIG_FILE);
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
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(CONFIG_FILE, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Failed to save config.json: {ex.Message}");
            }
        }
    }
}
