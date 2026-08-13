using System;
using HorizonCyclingBridge.Telemetry;

namespace HorizonCyclingBridge.Core
{
    public class ArcadeMappingStrategy : IPowerMappingStrategy
    {
        private readonly double _ftp;
        private double _filteredBrake = 0.0;
        private bool _isBrakingActive = false;
        private bool _hasLogged100Percent = false;

        public Action<string>? OnDebugLog { get; set; }

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

            double pitchGrade = (currentPacket != null && currentPacket.IsRaceOn) 
                ? -Math.Tan(currentPacket.Pitch) * 100.0 
                : 0.0;
            bool isDownhill = pitchGrade < -3.0;

            float throttle = 0.0f;
            float finalBrake = 0.0f;

            if (isPedalingHard)
            {
                // ★ブレーキ解除の唯一の条件: しっかり漕ぐ (>15W)
                throttle = Math.Clamp((float)(power / _ftp), 0.0f, 1.0f);
                if (_isBrakingActive)
                {
                    _isBrakingActive = false;
                    _filteredBrake = 0.0;
                    _hasLogged100Percent = false;
                    OnDebugLog?.Invoke($"[ARCADE BRAKE] OFF -> Pedaling ({power:F1}W)");
                }
                finalBrake = 0.0f;
            }
            else if (_isBrakingActive)
            {
                // ★ブレーキ作動中: isDownhillが変動してもブレーキを絶対に継続
                _filteredBrake += (1.0 / 1.2) * 0.016;
                _filteredBrake = Math.Clamp(_filteredBrake, 0.0, 1.0);

                if (_filteredBrake >= 1.0 && !_hasLogged100Percent)
                {
                    _hasLogged100Percent = true;
                    OnDebugLog?.Invoke($"[ARCADE BRAKE] 100% Held -> Spd={currentPacket?.SpeedKmh:F1}km/h");
                }

                if (currentPacket != null && currentPacket.SpeedKmh > 8.0f && currentPacket.VelocityZ > 0.1f)
                {
                    finalBrake = (float)_filteredBrake;
                }
                else
                {
                    finalBrake = 0.0f;
                }
                throttle = 0.0f;
            }
            else
            {
                // ★ブレーキ非作動中: 新たにブレーキを開始するかどうか判定
                if (isDownhill && power < 1.0)
                {
                    // 下り坂で足を止める (0W): 下り坂自動滑走 → ブレーキ開始しない
                    throttle = 0.20f;
                    finalBrake = 0.0f;
                }
                else
                {
                    // 平地/上り坂で足を止める → ブレーキ新規開始
                    _isBrakingActive = true;
                    _filteredBrake = 0.0;
                    _hasLogged100Percent = false;
                    OnDebugLog?.Invoke($"[ARCADE BRAKE] ON -> P={power:F1}W, Spd={currentPacket?.SpeedKmh:F1}km/h");
                    throttle = 0.0f;
                    finalBrake = 0.0f;
                }
            }

            return new ControlOutput { Throttle = throttle, Brake = finalBrake };
        }
    }
}
