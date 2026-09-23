using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HorizonCyclingBridge.Telemetry
{
    public class MapPin
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double PixelX { get; set; }
        public double PixelY { get; set; }
    }

    /// <summary>
    /// Forza Horizon 6 のワールド座標（メートル）と、地図画像ピクセル座標 / 擬似GPS座標を相互変換するエンジン
    /// </summary>
    public class CoordinateEngine
    {
        // 8192 x 8192 タイルマップの基準パラメータ
        public const double MAP_WIDTH = 8192.0;
        public const double MAP_HEIGHT = 8192.0;

        // Forza ワールド空間の原点オフセットとスケール（初期推定値：実走行時にキャリブレーション可能）
        // 一般的なForzaマップは中央が(0,0)、東西南北におよそ±4000〜6000m
        public double WorldOriginX { get; set; } = 0.0;
        public double WorldOriginZ { get; set; } = 0.0;
        public double WorldScale { get; set; } = 0.263; // ピクセル / メートル (8192pxで約31.1km四方相当、FH6日本マップ最適実寸)
        public bool InvertZ { get; set; } = true; // DirectX空間の+Z（北進）と地図画像（下向き+Y）の反転補正

        // GPS投影の基準原点（日本・富士山周辺の現実緯度経度）
        public double GpsBaseLat { get; set; } = 35.3606;
        public double GpsBaseLon { get; set; } = 138.7274;

        // 1度あたりの距離（概算：緯度1度≒111,000m、北緯35度での経度1度≒91,000m）
        private const double METERS_PER_LAT_DEGREE = 111139.0;
        private const double METERS_PER_LON_DEGREE = 91287.0;

        private readonly List<MapPin> _pins = new List<MapPin>();
        public IReadOnlyList<MapPin> Pins => _pins;

        public CoordinateEngine()
        {
        }

        /// <summary>
        /// ワールド座標（PositionX, PositionZ）を 8192x8192 ピクセル座標に変換
        /// </summary>
        public (double pixelX, double pixelY) WorldToPixel(float worldX, float worldZ)
        {
            // マップ中心 (4096, 4096) を原点とし、Xは東（右）
            double px = (MAP_WIDTH / 2.0) + (worldX - WorldOriginX) * WorldScale;

            // InvertZ が true の場合、ゲームの+Z（北）で画面上向き（py減少）にマッピング
            double py;
            if (InvertZ)
            {
                py = (MAP_HEIGHT / 2.0) - (worldZ - WorldOriginZ) * WorldScale;
            }
            else
            {
                py = (MAP_HEIGHT / 2.0) + (worldZ - WorldOriginZ) * WorldScale;
            }

            px = Math.Clamp(px, 0.0, MAP_WIDTH);
            py = Math.Clamp(py, 0.0, MAP_HEIGHT);

            return (px, py);
        }

        /// <summary>
        /// マップ上の特定ピクセル位置に、現在のゲーム内ワールド座標を吸着させるキャリブレーション
        /// </summary>
        public void Calibrate(double targetPixelX, double targetPixelY, float currentWorldX, float currentWorldZ)
        {
            if (WorldScale <= 0.0001) WorldScale = 0.263;

            // targetPixelX = (MAP_WIDTH / 2.0) + (currentWorldX - WorldOriginX) * WorldScale
            WorldOriginX = currentWorldX - ((targetPixelX - (MAP_WIDTH / 2.0)) / WorldScale);

            if (InvertZ)
            {
                // targetPixelY = (MAP_HEIGHT / 2.0) - (currentWorldZ - WorldOriginZ) * WorldScale
                WorldOriginZ = currentWorldZ - (((MAP_HEIGHT / 2.0) - targetPixelY) / WorldScale);
            }
            else
            {
                // targetPixelY = (MAP_HEIGHT / 2.0) + (currentWorldZ - WorldOriginZ) * WorldScale
                WorldOriginZ = currentWorldZ - ((targetPixelY - (MAP_HEIGHT / 2.0)) / WorldScale);
            }
        }

        /// <summary>
        /// ピクセル座標から推定ワールド座標を逆算
        /// </summary>
        public (float worldX, float worldZ) PixelToWorld(double pixelX, double pixelY)
        {
            float wx = (float)(WorldOriginX + (pixelX - (MAP_WIDTH / 2.0)) / WorldScale);
            float wz;
            if (InvertZ)
            {
                wz = (float)(WorldOriginZ + ((MAP_HEIGHT / 2.0) - pixelY) / WorldScale);
            }
            else
            {
                wz = (float)(WorldOriginZ + (pixelY - (MAP_HEIGHT / 2.0)) / WorldScale);
            }
            return (wx, wz);
        }

        /// <summary>
        /// ワールド座標を擬似GPS座標（緯度、経度）に変換（GPXエクスポート用）
        /// </summary>
        public (double latitude, double longitude) WorldToGps(float worldX, float worldZ)
        {
            // 北向きを+Zまたは-Zとして適正マッピング
            double lat = GpsBaseLat - (worldZ / METERS_PER_LAT_DEGREE);
            double lon = GpsBaseLon + (worldX / METERS_PER_LON_DEGREE);
            return (lat, lon);
        }

        /// <summary>
        /// map/fh6_japan_pins.json からPOIピン情報をロード
        /// </summary>
        public void LoadPins(string jsonPath)
        {
            try
            {
                if (!File.Exists(jsonPath)) return;

                string json = File.ReadAllText(jsonPath);
                using var doc = JsonDocument.Parse(json);
                _pins.Clear();

                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("item", out var item))
                    {
                        string name = item.GetProperty("name").GetString() ?? "";
                        string desc = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                        
                        if (item.TryGetProperty("geo", out var geo))
                        {
                            double lat = geo.GetProperty("latitude").GetDouble();
                            double lon = geo.GetProperty("longitude").GetDouble();

                            // Leaflet WebMercator座標系から8192x8192ピクセル座標への逆変換概算
                            // 経度 -180..180 -> 0..8192, 緯度 -85..85 -> 0..8192
                            double px = ((lon + 180.0) / 360.0) * MAP_WIDTH;
                            double latRad = lat * Math.PI / 180.0;
                            double mercN = Math.Log(Math.Tan((Math.PI / 4.0) + (latRad / 2.0)));
                            double py = (MAP_HEIGHT / 2.0) - (MAP_WIDTH * mercN / (2.0 * Math.PI));

                            _pins.Add(new MapPin
                            {
                                Name = name,
                                Description = desc,
                                Latitude = lat,
                                Longitude = lon,
                                PixelX = px,
                                PixelY = py
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to load pins from {jsonPath}: {ex.Message}");
            }
        }
    }
}
