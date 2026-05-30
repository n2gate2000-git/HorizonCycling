using System;
using HorizonCyclingBridge.Telemetry;

namespace HorizonCyclingBridge.Core
{
    public class ArcadeMappingStrategy : IPowerMappingStrategy
    {
        private readonly double _ftp;

        /// <summary>
        /// アーケードマッピング戦略を初期化します。
        /// </summary>
        /// <param name="ftp">ユーザーの基準パワー（FTP: Functional Threshold Power）（W）</param>
        public ArcadeMappingStrategy(double ftp = 200.0)
        {
            if (ftp <= 0) throw new ArgumentException("FTP must be greater than zero.", nameof(ftp));
            _ftp = ftp;
        }

        /// <summary>
        /// ペダルパワー（W）をFTP基準でアクセル開度（0.0〜1.0）にマッピングします。
        /// </summary>
        public ControlOutput CalculateOutput(double currentPower, ForzaDataPacket currentPacket)
        {
            // ペダルパワーが負の場合は0にクランプ
            double power = Math.Max(0.0, currentPower);

            // パワー / FTP でアクセル開度を算出 (例: 200WでFTP 200Wならアクセル100%)
            float throttle = (float)(power / _ftp);
            throttle = Math.Clamp(throttle, 0.0f, 1.0f);

            return new ControlOutput
            {
                Throttle = throttle,
                Brake = 0.0f // アーケードモードでは基本ペダリングでブレーキ操作は行わない
            };
        }
    }
}
