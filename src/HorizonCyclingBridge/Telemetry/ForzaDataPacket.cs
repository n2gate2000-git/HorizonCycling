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

        // 時速（km/h）への変換ヘルパー
        public float SpeedKmh => Math.Abs(VelocityZ) * 3.6f;

        /// <summary>
        /// 324バイトのForza UDPテレメトリパケット（リトルエンディアン）をパースします。
        /// </summary>
        public static ForzaDataPacket Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 324)
            {
                throw new ArgumentException("Packet must be at least 324 bytes long.");
            }

            var packet = new ForzaDataPacket();
            packet.IsRaceOn = BitConverter.ToInt32(bytes, 0) != 0;
            packet.TimestampMS = BitConverter.ToUInt32(bytes, 4);

            // 加速度データ（オフセット20〜31バイト）
            packet.AccelerationX = BitConverter.ToSingle(bytes, 20);
            packet.AccelerationY = BitConverter.ToSingle(bytes, 24);
            packet.AccelerationZ = BitConverter.ToSingle(bytes, 28);
            
            // 速度データ（オフセット32〜43バイト）
            packet.VelocityX = BitConverter.ToSingle(bytes, 32);
            packet.VelocityY = BitConverter.ToSingle(bytes, 36);
            packet.VelocityZ = BitConverter.ToSingle(bytes, 40);

            // 姿勢角データ（オフセット56〜67バイト）
            packet.Yaw = BitConverter.ToSingle(bytes, 56);
            packet.Pitch = BitConverter.ToSingle(bytes, 60);
            packet.Roll = BitConverter.ToSingle(bytes, 64);

            return packet;
        }
    }
}
