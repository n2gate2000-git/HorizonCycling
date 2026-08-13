using System;
using HorizonCyclingBridge.Telemetry;

namespace HorizonCyclingBridge.Core
{
    public class ArcadeMappingStrategy : IPowerMappingStrategy
    {
        private readonly double _ftp;
        private double _filteredBrake = 0.0;
        private const double BRAKE_RAMP_ALPHA = 0.04; // ブレーキランプアップ平滑化係数

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
            double power = Math.Max(0.0, currentPower);

            bool isPedalingHard = power > 15.0;
            bool isFeathering = power >= 1.0 && power <= 15.0;

            double pitchGrade = (currentPacket != null && currentPacket.IsRaceOn) 
                ? -Math.Tan(currentPacket.Pitch) * 100.0 
                : 0.0;
            bool isDownhill = pitchGrade < -3.0;

            float throttle = 0.0f;
            float targetBrake = 0.0f;

            if (isPedalingHard)
            {
                // しっかり漕ぐ (>15W): パワー/FTP でアクセル開度算出
                throttle = Math.Clamp((float)(power / _ftp), 0.0f, 1.0f);
                targetBrake = 0.0f;
            }
            else if (isFeathering)
            {
                if (isDownhill)
                {
                    // 下り坂で軽く足を回す (1〜15W): 下り坂減速ブレーキ
                    if (currentPacket != null && currentPacket.IsRaceOn && currentPacket.VelocityZ > 0.2f && currentPacket.SpeedKmh > 1.0f)
                    {
                        targetBrake = Math.Clamp(currentPacket.SpeedKmh / 10.0f, 0.15f, 1.0f);
                    }
                }
                else
                {
                    // 平地で軽く足を回す (1〜15W): 平地惰性走行（コースティング）
                    throttle = 0.0f;
                    targetBrake = 0.0f;
                }
            }
            else
            {
                // ペダル完全停止 (<1W / 0W)
                if (isDownhill && currentPacket != null && currentPacket.SpeedKmh > 1.0f)
                {
                    // 下り坂で足を止める (0W): 下り坂自動滑走（オートグライド）
                    throttle = 0.20f;
                    targetBrake = 0.0f;
                }
                else
                {
                    // 平地・上り坂で足を止める (0W): 平地車両停止ブレーキ
                    if (currentPacket != null && currentPacket.IsRaceOn && currentPacket.VelocityZ > 0.2f && currentPacket.SpeedKmh > 1.0f)
                    {
                        targetBrake = Math.Clamp(currentPacket.SpeedKmh / 10.0f, 0.15f, 1.0f);
                    }
                }
            }

            // ブレーキ滑らかランプアップフィルタ
            float finalBrake = 0.0f;
            if (targetBrake > 0.0f)
            {
                _filteredBrake = (_filteredBrake * (1.0 - BRAKE_RAMP_ALPHA)) + (targetBrake * BRAKE_RAMP_ALPHA);
                finalBrake = (float)Math.Clamp(_filteredBrake, 0.0, targetBrake);
                throttle = 0.0f;
            }
            else
            {
                _filteredBrake = 0.0;
                finalBrake = 0.0f;
            }

            return new ControlOutput { Throttle = throttle, Brake = finalBrake };
        }
    }
}
