using System;
using System.IO;
using HorizonCyclingBridge.Telemetry;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HorizonCycling.Tests
{
    public class TelemetryAndSessionTests
    {
        [Fact]
        public void ForzaDataPacket_Parse_Horizon324Bytes_ShouldExtractPositionAndTelemetry()
        {
            byte[] packetBytes = new byte[324];

            // 1. IsRaceOn (0: int32) = 1
            BitConverter.GetBytes(1).CopyTo(packetBytes, 0);

            // 2. TimestampMS (4: uint32) = 12345
            BitConverter.GetBytes((uint)12345).CopyTo(packetBytes, 4);

            // 3. VelocityZ (40: float) = 10.0 m/s (36 km/h)
            BitConverter.GetBytes(10.0f).CopyTo(packetBytes, 40);

            // 4. Pitch (60: float) = 0.05 rad (約5%勾配)
            BitConverter.GetBytes(0.05f).CopyTo(packetBytes, 60);

            // 5. PositionX (244: float) = 1500.0f
            BitConverter.GetBytes(1500.0f).CopyTo(packetBytes, 244);

            // 6. PositionY (248: float) = 450.0f (標高)
            BitConverter.GetBytes(450.0f).CopyTo(packetBytes, 248);

            // 7. PositionZ (252: float) = -2000.0f
            BitConverter.GetBytes(-2000.0f).CopyTo(packetBytes, 252);

            // 8. Speed (256: float) = 10.0f
            BitConverter.GetBytes(10.0f).CopyTo(packetBytes, 256);

            // 9. PowerWatts (260: float) = 220.0f
            BitConverter.GetBytes(220.0f).CopyTo(packetBytes, 260);

            // 10. DistanceTraveled (292: float) = 5432.1f
            BitConverter.GetBytes(5432.1f).CopyTo(packetBytes, 292);

            // Parse
            var packet = ForzaDataPacket.Parse(packetBytes);

            Assert.True(packet.IsRaceOn);
            Assert.Equal((uint)12345, packet.TimestampMS);
            Assert.Equal(1500.0f, packet.PositionX);
            Assert.Equal(450.0f, packet.PositionY);
            Assert.Equal(-2000.0f, packet.PositionZ);
            Assert.Equal(10.0f, packet.Speed);
            Assert.Equal(220.0f, packet.PowerWatts);
            Assert.Equal(5432.1f, packet.DistanceTraveled);
            Assert.True(packet.SpeedKmh > 35.0f && packet.SpeedKmh < 37.0f);
        }

        [Fact]
        public void ForzaDataPacket_Parse_Motorsport311Bytes_ShouldExtractPositionAndTelemetry()
        {
            byte[] packetBytes = new byte[311];

            BitConverter.GetBytes(1).CopyTo(packetBytes, 0);
            BitConverter.GetBytes((uint)54321).CopyTo(packetBytes, 4);
            BitConverter.GetBytes(15.0f).CopyTo(packetBytes, 40);

            // Position (232, 236, 240)
            BitConverter.GetBytes(750.0f).CopyTo(packetBytes, 232);
            BitConverter.GetBytes(300.0f).CopyTo(packetBytes, 236);
            BitConverter.GetBytes(-1200.0f).CopyTo(packetBytes, 240);

            // Speed, Power, Distance (244, 248, 280)
            BitConverter.GetBytes(15.0f).CopyTo(packetBytes, 244);
            BitConverter.GetBytes(180.0f).CopyTo(packetBytes, 248);
            BitConverter.GetBytes(3000.0f).CopyTo(packetBytes, 280);

            var packet = ForzaDataPacket.Parse(packetBytes);

            Assert.True(packet.IsRaceOn);
            Assert.Equal((uint)54321, packet.TimestampMS);
            Assert.Equal(750.0f, packet.PositionX);
            Assert.Equal(300.0f, packet.PositionY);
            Assert.Equal(-1200.0f, packet.PositionZ);
            Assert.Equal(15.0f, packet.Speed);
            Assert.Equal(180.0f, packet.PowerWatts);
            Assert.Equal(3000.0f, packet.DistanceTraveled);
        }

        [Fact]
        public void CoordinateEngine_WorldToPixel_ShouldMapWithinBounds()
        {
            var engine = new CoordinateEngine();

            var (px, py) = engine.WorldToPixel(0, 0);
            Assert.Equal(4096.0, px);
            Assert.Equal(4096.0, py);

            // InvertZ=true の場合、ワールド+Z（北進）でピクセルpyは小さくなる（画面上方向）
            var (_, pyNorth) = engine.WorldToPixel(0, 1000);
            Assert.True(pyNorth < 4096.0);

            // InvertZ=false の場合、ワールド+Zでピクセルpyは大きくなる（画面下方向）
            engine.InvertZ = false;
            var (_, pySouth) = engine.WorldToPixel(0, 1000);
            Assert.True(pySouth > 4096.0);

            var (lat, lon) = engine.WorldToGps(1000, 1000);
            Assert.True(lat > 0 && lat < 90);
            Assert.True(lon > 0 && lon < 180);
        }

        [Fact]
        public void CoordinateEngine_Calibrate_ShouldSnapCarToTargetPixel()
        {
            var engine = new CoordinateEngine { InvertZ = true, WorldScale = 0.70 };

            // 現在ワールド座標 (1500, -2000) にいる車を、マップ上のターゲットピクセル (3500, 5000) に吸着
            float currentWorldX = 1500.0f;
            float currentWorldZ = -2000.0f;
            double targetPx = 3500.0;
            double targetPy = 5000.0;

            engine.Calibrate(targetPx, targetPy, currentWorldX, currentWorldZ);

            var (px, py) = engine.WorldToPixel(currentWorldX, currentWorldZ);
            Assert.Equal(targetPx, px, 0.001);
            Assert.Equal(targetPy, py, 0.001);
        }

        [Fact]
        public void ElevationTracker_ShouldAccumulateGainAndFilterNoise()
        {
            var tracker = new ElevationTracker();

            // 初期化
            tracker.Update(100.0f, 30.0f, 0.0, 0.016);

            // 登坂シミュレーション (100m -> 150m、60Hzで約5秒間の登坂)
            for (int i = 1; i <= 300; i++)
            {
                float elev = 100.0f + (i * (50.0f / 300.0f));
                tracker.Update(elev, 25.0f, 5.0, 0.016);
            }

            Assert.True(tracker.CurrentElevation > 145.0);
            Assert.True(tracker.ElevationGain > 45.0);
        }

        [Fact]
        public void SessionManager_ExportGpx_ShouldCreateValidXml()
        {
            var session = new SessionManager();
            session.Start();

            session.AddTrackPoint(35.3606, 138.7274, 500.0, 30.0, 200.0, 85.0, 140.0);
            session.AddTrackPoint(35.3610, 138.7280, 505.0, 28.0, 220.0, 84.0, 142.0);

            string tempDir = Path.Combine(Path.GetTempPath(), "HorizonCyclingTest_" + Guid.NewGuid().ToString("N"));
            var summary = session.StopAndExport(tempDir, 0.5, 5.0);

            Assert.True(File.Exists(summary.SavedFilePath));
            string content = File.ReadAllText(summary.SavedFilePath);
            Assert.Contains("<gpx", content);
            Assert.Contains("<trkpt", content);
            Assert.Contains("<power>200</power>", content);

            // クリーンアップ
            try { Directory.Delete(tempDir, true); } catch { }
        }

        private static ForzaDataPacket CreateDummyPacket(bool isRaceOn, float posX, float posY, float posZ, float speed, uint timestampMs = 1000)
        {
            byte[] packetBytes = new byte[324];
            BitConverter.GetBytes(isRaceOn ? 1 : 0).CopyTo(packetBytes, 0);
            BitConverter.GetBytes(timestampMs).CopyTo(packetBytes, 4);
            BitConverter.GetBytes(posX).CopyTo(packetBytes, 244);
            BitConverter.GetBytes(posY).CopyTo(packetBytes, 248);
            BitConverter.GetBytes(posZ).CopyTo(packetBytes, 252);
            BitConverter.GetBytes(speed).CopyTo(packetBytes, 256);
            return ForzaDataPacket.Parse(packetBytes);
        }

        [Fact]
        public void BridgeService_ZeroOrMenuPosition_ShouldPreserveLastValidPositionAndSkipGpx()
        {
            var config = new HorizonCyclingBridge.Core.AppConfig
            {
                MapScale = 0.263,
                MapOriginX = 0,
                MapOriginZ = 0
            };

            using var bridge = new HorizonCyclingBridge.Core.BridgeService(config);
            bridge.StartSession();

            HorizonCyclingBridge.Core.BridgeStateSnapshot? latestSnapshot = null;
            bridge.OnStateUpdated += s => latestSnapshot = s;

            // 1. 有効な走行パケット送信 (Position: 1500, 300, -800)
            var validPacket = CreateDummyPacket(true, 1500.0f, 300.0f, -800.0f, 10.0f, 1000);
            bridge.HandlePacketReceived(validPacket);

            Assert.NotNull(latestSnapshot);
            Assert.True(latestSnapshot.IsPositionValid);
            Assert.Equal(1500.0f, latestSnapshot.PositionX);
            Assert.Equal(300.0f, latestSnapshot.PositionY);
            Assert.Equal(-800.0f, latestSnapshot.PositionZ);

            // 2. メニューを開いたときの (0, 0, 0) パケット送信
            var menuPacket = CreateDummyPacket(false, 0.0f, 0.0f, 0.0f, 0.0f, 1050);
            bridge.HandlePacketReceived(menuPacket);

            // 位置が無効と判定され、かつ直前の有効座標 (1500, 300, -800) が保持されていること
            Assert.NotNull(latestSnapshot);
            Assert.False(latestSnapshot.IsPositionValid);
            Assert.Equal(1500.0f, latestSnapshot.PositionX);
            Assert.Equal(300.0f, latestSnapshot.PositionY);
            Assert.Equal(-800.0f, latestSnapshot.PositionZ);

            // 3. GPXエクスポートでトラックポイントが1点のみ（0,0,0 が除外されたこと）を確認
            string tempDir = Path.Combine(Path.GetTempPath(), "HorizonCyclingTest_" + Guid.NewGuid().ToString("N"));
            var summary = bridge.SessionManager.StopAndExport(tempDir, 0.1, 0.0);
            string gpxText = File.ReadAllText(summary.SavedFilePath);

            int trkptCount = System.Text.RegularExpressions.Regex.Matches(gpxText, "<trkpt").Count;
            Assert.Equal(1, trkptCount);

            try { Directory.Delete(tempDir, true); } catch { }
        }

        [Fact]
        public void BridgeService_SetModeAndDifficulty_ShouldPersistToConfig()
        {
            var config = new HorizonCyclingBridge.Core.AppConfig
            {
                DefaultMode = 2,
                TrainerDifficulty = 0.5
            };

            using (var bridge = new HorizonCyclingBridge.Core.BridgeService(config))
            {
                // モード変更 (Arcade = 1)
                bridge.SetMode(1);
                Assert.Equal(1, bridge.Config.DefaultMode);

                // DIFFICULTY変更 (75% = 0.75)
                bridge.SetDifficulty(0.75);
                Assert.Equal(0.75, bridge.Config.TrainerDifficulty);
            }

            // 次回起動シミュレーション: 同じconfigを渡して初期化
            using (var bridgeNext = new HorizonCyclingBridge.Core.BridgeService(config))
            {
                // 初期状態でモード1（Arcade）、難易度0.75が反映されていること
                HorizonCyclingBridge.Core.BridgeStateSnapshot? snapshot = null;
                bridgeNext.OnStateUpdated += s => snapshot = s;

                var packet = CreateDummyPacket(true, 100f, 10f, 100f, 20f);
                bridgeNext.HandlePacketReceived(packet);

                Assert.NotNull(snapshot);
                Assert.Equal(1, snapshot.Mode);
                Assert.Equal("ARCADE MODE", snapshot.ModeName);
                Assert.Equal(0.75, snapshot.Difficulty);
            }
        }

        [Fact]
        public void VJoyVehicleController_ShouldInitializeAndAcquireDevice()
        {
            using var vjoy = new HorizonCyclingBridge.Controller.VJoyVehicleController(1);
            string lastMsg = "";
            vjoy.OnStatusMessage += msg => lastMsg = msg;

            bool success = vjoy.Initialize();

            // vJoyInterface.dll がロードされ、ドライバーが有効であることを検証
            Assert.DoesNotContain("vJoyInterface.dll not found", lastMsg);
            Assert.DoesNotContain("not enabled/installed", lastMsg);

            // アプリ実行中でない場合はアクワイア成功、実行中の場合は専有中メッセージ
            Assert.True(success || lastMsg.Contains("used by another app"), $"Unexpected status message: {lastMsg}");
        }

        [Fact]
        public void BridgeService_SetFtp_ShouldUpdateArcadeThrottleScaling()
        {
            var config = new HorizonCyclingBridge.Core.AppConfig
            {
                DefaultMode = 1, // ARCADE MODE
                Ftp = 200.0
            };

            using var bridge = new HorizonCyclingBridge.Core.BridgeService(config);

            HorizonCyclingBridge.Core.BridgeStateSnapshot? snapshot = null;
            bridge.OnStateUpdated += s => snapshot = s;

            // 1. FTP 200W のとき、パワー 200W でスロットル 100% (1.0f)
            // (BleClient を介さず直接リフレクションまたはテスト用パケット処理)
            // BridgeService の _currentPower は BLE から入るため、リフレクションで設定
            var powerField = typeof(HorizonCyclingBridge.Core.BridgeService).GetField("_currentPower", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(powerField);

            powerField.SetValue(bridge, 200.0);
            var packet = CreateDummyPacket(true, 100f, 10f, 100f, 20f);
            bridge.HandlePacketReceived(packet);

            Assert.NotNull(snapshot);
            Assert.Equal(1.0f, snapshot.Throttle, 2); // 200/200 = 1.0
            Assert.Equal(200.0, snapshot.Ftp);

            // 2. FTP を 250W に変更
            bridge.SetFtp(250.0);
            Assert.Equal(250.0, bridge.Config.Ftp);

            // 3. パワー 200W のとき、スロットルは 200/250 = 0.80f (80%) になること
            powerField.SetValue(bridge, 200.0);
            bridge.HandlePacketReceived(packet);
            Assert.Equal(0.80f, snapshot.Throttle, 2);
            Assert.Equal(250.0, snapshot.Ftp);

            // 4. パワー 250W でちょうど 100% (1.0f) になること
            powerField.SetValue(bridge, 250.0);
            bridge.HandlePacketReceived(packet);
            Assert.Equal(1.0f, snapshot.Throttle, 2);
        }

        [Fact]
        public void BridgeService_GradeFeedback_ShouldCalculateGradeAndResetOnStop()
        {
            var config = new HorizonCyclingBridge.Core.AppConfig
            {
                DefaultMode = 1,
                TrainerDifficulty = 0.5
            };

            using var bridge = new HorizonCyclingBridge.Core.BridgeService(config);
            HorizonCyclingBridge.Core.BridgeStateSnapshot? snapshot = null;
            bridge.OnStateUpdated += s => snapshot = s;

            // 1. 上り坂（Pitch = -0.05 rad, 約5%勾配）、速度 30 km/h で複数回パケット処理（EMA追従）
            for (int i = 0; i < 60; i++)
            {
                byte[] bytes = new byte[324];
                BitConverter.GetBytes(1).CopyTo(bytes, 0); // IsRaceOn = 1
                BitConverter.GetBytes((uint)(1000 + i * 16)).CopyTo(bytes, 4);
                BitConverter.GetBytes(-0.05f).CopyTo(bytes, 60); // Pitch = -0.05 (登坂)
                BitConverter.GetBytes(100f + i).CopyTo(bytes, 244);
                BitConverter.GetBytes(50f).CopyTo(bytes, 248);
                BitConverter.GetBytes(100f + i).CopyTo(bytes, 252);
                BitConverter.GetBytes(8.33f).CopyTo(bytes, 256); // Speed = 30 km/h
                var packet = ForzaDataPacket.Parse(bytes);
                bridge.HandlePacketReceived(packet);
            }

            Assert.NotNull(snapshot);
            // 登坂勾配が計算されていること (登坂 > 1.0%)
            Assert.True(snapshot.RawGrade > 1.0, $"Expected RawGrade > 1.0%, but was {snapshot.RawGrade}");

            // 2. 停車（Speed = 0 km/h）すると勾配負荷が 0.0% にリセットされること
            byte[] stopBytes = new byte[324];
            BitConverter.GetBytes(1).CopyTo(stopBytes, 0); // IsRaceOn = 1
            BitConverter.GetBytes((uint)5000).CopyTo(stopBytes, 4);
            BitConverter.GetBytes(-0.05f).CopyTo(stopBytes, 60);
            BitConverter.GetBytes(200f).CopyTo(stopBytes, 244);
            BitConverter.GetBytes(50f).CopyTo(stopBytes, 248);
            BitConverter.GetBytes(200f).CopyTo(stopBytes, 252);
            BitConverter.GetBytes(0.0f).CopyTo(stopBytes, 256); // Speed = 0 km/h
            var stopPacket = ForzaDataPacket.Parse(stopBytes);
            bridge.HandlePacketReceived(stopPacket);

            // 停止時は停車解放によって SentGrade が 0.0 になること
            Assert.Equal(0.0, snapshot.SentGrade);
        }

        [Fact]
        public void BridgeService_ClearTrack_ShouldResetElevationGain()
        {
            var config = new HorizonCyclingBridge.Core.AppConfig();
            using var bridge = new HorizonCyclingBridge.Core.BridgeService(config);
            HorizonCyclingBridge.Core.BridgeStateSnapshot? snapshot = null;
            bridge.OnStateUpdated += s => snapshot = s;

            // 標高を上昇させて獲得標高を積算 (50m -> 150m)
            for (int i = 0; i < 30; i++)
            {
                byte[] bytes = new byte[324];
                BitConverter.GetBytes(1).CopyTo(bytes, 0);
                BitConverter.GetBytes((uint)(1000 + i * 50)).CopyTo(bytes, 4);
                BitConverter.GetBytes(-0.05f).CopyTo(bytes, 60);
                BitConverter.GetBytes(100f + i).CopyTo(bytes, 244);
                BitConverter.GetBytes(50f + (i * 3.0f)).CopyTo(bytes, 248); // 標高上昇
                BitConverter.GetBytes(100f + i).CopyTo(bytes, 252);
                BitConverter.GetBytes(10.0f).CopyTo(bytes, 256);
                var packet = ForzaDataPacket.Parse(bytes);
                bridge.HandlePacketReceived(packet);
            }

            Assert.NotNull(snapshot);
            Assert.True(snapshot.ElevationGainMeters > 5.0, $"Expected ElevationGainMeters > 5.0, got {snapshot.ElevationGainMeters}");

            // ClearTrack 実行
            bridge.ClearTrack();

            // 直後のパケット受信で獲得標高がリセット（0m近辺）されていること
            byte[] nextBytes = new byte[324];
            BitConverter.GetBytes(1).CopyTo(nextBytes, 0);
            BitConverter.GetBytes((uint)3000).CopyTo(nextBytes, 4);
            BitConverter.GetBytes(0.0f).CopyTo(nextBytes, 60);
            BitConverter.GetBytes(200f).CopyTo(nextBytes, 244);
            BitConverter.GetBytes(140f).CopyTo(nextBytes, 248);
            BitConverter.GetBytes(200f).CopyTo(nextBytes, 252);
            BitConverter.GetBytes(10.0f).CopyTo(nextBytes, 256);
            var nextPacket = ForzaDataPacket.Parse(nextBytes);
            bridge.HandlePacketReceived(nextPacket);

            Assert.Equal(0.0, snapshot.ElevationGainMeters);
        }

        [Fact]
        public void BridgeService_FastTravel_ShouldNotIncreaseElevationGain()
        {
            var config = new HorizonCyclingBridge.Core.AppConfig();
            using var bridge = new HorizonCyclingBridge.Core.BridgeService(config);
            HorizonCyclingBridge.Core.BridgeStateSnapshot? snapshot = null;
            bridge.OnStateUpdated += s => snapshot = s;

            // 1. 通常の登坂走行（標高 50m -> 65m、獲得標高 約 15m）
            for (int i = 0; i < 20; i++)
            {
                byte[] bytes = new byte[324];
                BitConverter.GetBytes(1).CopyTo(bytes, 0);
                BitConverter.GetBytes((uint)(1000 + i * 50)).CopyTo(bytes, 4);
                BitConverter.GetBytes(-0.05f).CopyTo(bytes, 60);
                BitConverter.GetBytes(100f + (i * 2.0f)).CopyTo(bytes, 244);
                BitConverter.GetBytes(50f + (i * 0.75f)).CopyTo(bytes, 248);
                BitConverter.GetBytes(100f + (i * 2.0f)).CopyTo(bytes, 252);
                BitConverter.GetBytes(10.0f).CopyTo(bytes, 256);
                var packet = ForzaDataPacket.Parse(bytes);
                bridge.HandlePacketReceived(packet);
            }

            Assert.NotNull(snapshot);
            double gainBeforeFastTravel = snapshot.ElevationGainMeters;
            Assert.True(gainBeforeFastTravel > 5.0, $"Expected Gain > 5.0, got {gainBeforeFastTravel}");

            // 2. ファストトラベル発生（水平位置が1500mジャンプし、標高が 65m から 600m へ急変）
            for (int i = 0; i < 10; i++)
            {
                byte[] jumpBytes = new byte[324];
                BitConverter.GetBytes(1).CopyTo(jumpBytes, 0);
                BitConverter.GetBytes((uint)(2500 + i * 50)).CopyTo(jumpBytes, 4);
                BitConverter.GetBytes(0.0f).CopyTo(jumpBytes, 60);
                BitConverter.GetBytes(2000f + (i * 2.0f)).CopyTo(jumpBytes, 244); // 100m -> 2000m (水平1900mジャンプ)
                BitConverter.GetBytes(600f).CopyTo(jumpBytes, 248);                // 65m -> 600m (垂直535mジャンプ)
                BitConverter.GetBytes(2000f + (i * 2.0f)).CopyTo(jumpBytes, 252);
                BitConverter.GetBytes(10.0f).CopyTo(jumpBytes, 256);
                var jumpPacket = ForzaDataPacket.Parse(jumpBytes);
                bridge.HandlePacketReceived(jumpPacket);
            }

            // ファストトラベルの535mが獲得標高に加算されておらず、直前とほぼ同値であること
            Assert.True(Math.Abs(snapshot.ElevationGainMeters - gainBeforeFastTravel) < 1.0,
                $"Gain after fast travel ({snapshot.ElevationGainMeters:F1}) should not jump from ({gainBeforeFastTravel:F1})");

            // 3. ファストトラベル先の新標高 (600m) で再び登坂 (600m -> 615m)
            for (int i = 0; i < 20; i++)
            {
                byte[] climbBytes = new byte[324];
                BitConverter.GetBytes(1).CopyTo(climbBytes, 0);
                BitConverter.GetBytes((uint)(3500 + i * 50)).CopyTo(climbBytes, 4);
                BitConverter.GetBytes(-0.05f).CopyTo(climbBytes, 60);
                BitConverter.GetBytes(2020f + (i * 2.0f)).CopyTo(climbBytes, 244);
                BitConverter.GetBytes(600f + (i * 0.75f)).CopyTo(climbBytes, 248);
                BitConverter.GetBytes(2020f + (i * 2.0f)).CopyTo(climbBytes, 252);
                BitConverter.GetBytes(10.0f).CopyTo(climbBytes, 256);
                var climbPacket = ForzaDataPacket.Parse(climbBytes);
                bridge.HandlePacketReceived(climbPacket);
            }

            // 新地点での登坂分が正常に加算されていること
            Assert.True(snapshot.ElevationGainMeters > gainBeforeFastTravel + 5.0,
                $"Expected Gain to increase after new climb, got {snapshot.ElevationGainMeters:F1}");
        }
    }
}
