using System;

namespace HorizonCyclingBridge.Telemetry
{
    public class ForzaDataPacket
    {
        public bool IsRaceOn { get; private set; }
        public uint TimestampMS { get; private set; }
        public float VelocityX { get; private set; }
        public float VelocityY { get; private set; }
        public float VelocityZ { get; private set; } // m/s (Zは進行方向)
        public float Yaw { get; private set; }
        public float Pitch { get; private set; } // radians
        public float Roll { get; private set; }
        public float AccelerationX { get; private set; }
        public float AccelerationY { get; private set; }
        public float AccelerationZ { get; private set; }

        // ワールド座標・標高データ (メートル)
        public float PositionX { get; private set; } // オフセット 232
        public float PositionY { get; private set; } // オフセット 236 (標高 / 高度)
        public float PositionZ { get; private set; } // オフセット 240

        // 車両・走行データ
        public float Speed { get; private set; } // オフセット 244 (m/s)
        public float PowerWatts { get; private set; } // オフセット 248 (W)
        public float Torque { get; private set; } // オフセット 252 (Nm)
        public float DistanceTraveled { get; private set; } // オフセット 280 (m)
        public float CurrentRaceTime { get; private set; } // オフセット 296 (秒)
        public ushort LapNumber { get; private set; } // オフセット 300
        public byte RacePosition { get; private set; } // オフセット 302
        public byte Gear { get; private set; } // オフセット 307
        public sbyte Steer { get; private set; } // オフセット 308
        public int CarOrdinal { get; private set; } // オフセット 212

        // 時速（km/h）への変換ヘルパー
        public float SpeedKmh => Speed > 0.01f ? Speed * 3.6f : Math.Abs(VelocityZ) * 3.6f;

        /// <summary>
        /// Forza UDPテレメトリパケット（リトルエンディアン）をパースします。
        /// 324バイトのHorizonフォーマット（FH4/FH5/FH6）および 311バイトのMotorsportフォーマットに対応。
        /// </summary>
        public static ForzaDataPacket Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 311)
            {
                throw new ArgumentException("Packet must be at least 311 bytes long.");
            }

            var packet = new ForzaDataPacket();
            packet.IsRaceOn = BitConverter.ToInt32(bytes, 0) != 0;
            packet.TimestampMS = BitConverter.ToUInt32(bytes, 4);

            // 加速度データ（オフセット20〜31バイト）
            packet.AccelerationX = BitConverter.ToSingle(bytes, 20);
            packet.AccelerationY = BitConverter.ToSingle(bytes, 24);
            packet.AccelerationZ = BitConverter.ToSingle(bytes, 28);
            
            // 速度ベクトル（オフセット32〜43バイト）
            packet.VelocityX = BitConverter.ToSingle(bytes, 32);
            packet.VelocityY = BitConverter.ToSingle(bytes, 36);
            packet.VelocityZ = BitConverter.ToSingle(bytes, 40);

            // 姿勢角データ（オフセット56〜67バイト）
            packet.Yaw = BitConverter.ToSingle(bytes, 56);
            packet.Pitch = BitConverter.ToSingle(bytes, 60);
            packet.Roll = BitConverter.ToSingle(bytes, 64);

            // 車種ID（オフセット212バイト）
            packet.CarOrdinal = BitConverter.ToInt32(bytes, 212);

            // 324バイトのHorizonパケットと311バイトパケットのオフセット判定
            // Horizon 324バイトパケットでは、NumCylinders (228..231) の直後に 12バイトのパディング (HorizonPlaceholder 232..243) が入るため
            // 座標・速度・出力等のオフセットが +12 バイトシフトします。
            bool isHorizonFormat = bytes.Length >= 324;

            // セーフティ判定：もし324バイト以上であっても、244..255 がすべて0で 232..243 に非ゼロ座標が存在する場合は311バイト仕様と判定
            if (isHorizonFormat)
            {
                float posX324 = BitConverter.ToSingle(bytes, 244);
                float posY324 = BitConverter.ToSingle(bytes, 248);
                float posZ324 = BitConverter.ToSingle(bytes, 252);

                float posX311 = BitConverter.ToSingle(bytes, 232);
                float posY311 = BitConverter.ToSingle(bytes, 236);
                float posZ311 = BitConverter.ToSingle(bytes, 240);

                if (posX324 == 0.0f && posY324 == 0.0f && posZ324 == 0.0f &&
                    (posX311 != 0.0f || posY311 != 0.0f || posZ311 != 0.0f))
                {
                    isHorizonFormat = false;
                }
            }

            if (isHorizonFormat)
            {
                // FH4 / FH5 / FH6 (324 bytes format)
                packet.PositionX = BitConverter.ToSingle(bytes, 244);
                packet.PositionY = BitConverter.ToSingle(bytes, 248);
                packet.PositionZ = BitConverter.ToSingle(bytes, 252);

                packet.Speed = BitConverter.ToSingle(bytes, 256);
                packet.PowerWatts = BitConverter.ToSingle(bytes, 260);
                packet.Torque = BitConverter.ToSingle(bytes, 264);
                packet.DistanceTraveled = BitConverter.ToSingle(bytes, 292);

                packet.CurrentRaceTime = BitConverter.ToSingle(bytes, 308);
                packet.LapNumber = BitConverter.ToUInt16(bytes, 312);
                packet.RacePosition = bytes[314];
                packet.Gear = bytes[319];
                packet.Steer = (sbyte)bytes[320];
            }
            else
            {
                // FM7 / Sled CarDash (311 bytes format)
                packet.PositionX = BitConverter.ToSingle(bytes, 232);
                packet.PositionY = BitConverter.ToSingle(bytes, 236);
                packet.PositionZ = BitConverter.ToSingle(bytes, 240);

                packet.Speed = BitConverter.ToSingle(bytes, 244);
                packet.PowerWatts = BitConverter.ToSingle(bytes, 248);
                packet.Torque = BitConverter.ToSingle(bytes, 252);
                packet.DistanceTraveled = BitConverter.ToSingle(bytes, 280);

                packet.CurrentRaceTime = BitConverter.ToSingle(bytes, 296);
                packet.LapNumber = BitConverter.ToUInt16(bytes, 300);
                packet.RacePosition = bytes[302];
                packet.Gear = bytes[307];
                packet.Steer = (sbyte)bytes[308];
            }

            return packet;
        }
    }
}
