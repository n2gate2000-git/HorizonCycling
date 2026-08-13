using System;
using HorizonCyclingBridge.Telemetry;

namespace HorizonCyclingBridge.Core
{
    public class SimulationMappingStrategy : IPowerMappingStrategy
    {
        // 自転車の物理モデル定数（一般的なロードバイク＋ライダーを想定）
        private const double M = 75.0;      // 総重量 (ライダー + 自転車) (kg)
        private const double G = 9.81;      // 重力加速度 (m/s^2)
        private const double Crr = 0.004;   // 転がり抵抗係数
        private const double Eta = 0.97;    // ドライブトレイン効率（チェーン摩擦損失）
        private const double Rho = 1.225;   // 空気密度 (kg/m^3)
        private const double CdA = 0.32;    // 空気抵抗係数 × 前面投影面積 (m^2)

        private const double Aaero = 0.5 * Rho * CdA; // 空気抵抗項の係数 (約 0.196)

        private readonly SpeedPidController _pidController;
        private double _lastTargetSpeed = 0.0;
        private uint _lastTimestampMS = 0;

        /// <summary>
        /// スマートローラーから送られてくる現在の瞬時物理速度 (km/h)
        /// </summary>
        public double TrainerSpeedKmh { get; set; } = 0.0;

        /// <summary>
        /// サスペンション姿勢変化を相殺した「真の道路勾配(%)」
        /// </summary>
        public double RoadGradePercent { get; set; } = 0.0;

        /// <summary>
        /// 難易度（Trainer Difficulty）が適用されていない、ゲーム内の実際の道路勾配(%)
        /// </summary>
        public double TrueRoadGradePercent { get; set; } = 0.0;

        private double _filteredThrottle = 0.0;
        private const double THROTTLE_ALPHA = 0.08; // スロットルの滑らかさ平滑化係数 (約0.3秒で追従)

        private double _filteredBrake = 0.0;
        private const double BRAKE_RAMP_ALPHA = 0.04; // ★ブレーキ作動時の滑らかなランプアップ平滑化係数 (約0.8〜1.0秒かけてジワーッと100%へ到達)

        /// <summary>
        /// 目標速度の現在値 (m/s)。デバッグ表示などで利用可能
        /// </summary>
        public double TargetSpeedMps => _lastTargetSpeed;

        /// <summary>
        /// 目標速度の現在値 (km/h)
        /// </summary>
        public double TargetSpeedKmh => _lastTargetSpeed * 3.6;

        public SimulationMappingStrategy(float kp = 0.6f, float ki = 0.15f, float kd = 0.05f)
        {
            _pidController = new SpeedPidController(kp, ki, kd);
        }

        public ControlOutput CalculateOutput(double currentPower, ForzaDataPacket currentPacket)
        {
            // レース中でない場合は、PIDをリセットし、アクセル0・ブレーキ軽めで待機
            if (!currentPacket.IsRaceOn)
            {
                _pidController.Reset();
                _lastTargetSpeed = 0.0;
                _lastTimestampMS = currentPacket.TimestampMS;
                _filteredThrottle = 0.0;
                _filteredBrake = 0.0;
                return new ControlOutput { Throttle = 0.0f, Brake = 0.0f };
            }

            // DeltaTime (前フレームからの秒数) の計算
            float deltaTime = 0.016f; // 60FPS基準のデフォルト
            if (_lastTimestampMS != 0)
            {
                long diff = (long)currentPacket.TimestampMS - (long)_lastTimestampMS;
                // タイムスタンプの逆流や、極端な遅延（1秒以上）を弾く
                if (diff > 0 && diff < 1000)
                {
                    deltaTime = diff / 1000f;
                }
            }
            _lastTimestampMS = currentPacket.TimestampMS;

            // 1. 物理モデルに基づき、自転車としての目標速度 (m/s) を計算
            // サスペンション姿勢変化を完全に相殺した「真の道路勾配(%)」を物理エンジンに適用します
            double targetSpeedMps = CalculateTargetSpeed(currentPower, RoadGradePercent);

            // ★スマートローラーの物理速度による目標速度の底上げ（下り坂での自動滑走アシスト連動）
            // 下り坂（RoadGradePercent < 0）において、スマートローラーがモーター等で自動で前に進む（回転する）速度信号を送ってきた場合、
            // その速度（m/s）を自転車の目標速度の最低保証値（下限）として適用し、車のアクセル開度を自動調整します。
            if (RoadGradePercent < 0.0 && TrainerSpeedKmh > 0.0)
            {
                double trainerSpeedMps = TrainerSpeedKmh / 3.6;
                targetSpeedMps = Math.Max(targetSpeedMps, trainerSpeedMps);
            }

            // 2. Forzaの現在速度 (VelocityZはローカル前方向速度) を取得
            // (後退している場合はマイナス値になるため、進行方向の絶対速度を追う)
            float currentCarSpeedMps = Math.Abs(currentPacket.VelocityZ);

            // 3. PID制御で目標速度に合わせるスロットル・ブレーキ値を計算
            ControlOutput output = _pidController.Compute((float)targetSpeedMps, currentCarSpeedMps, deltaTime);

            // ★地形適応型 3ゾーン・ペダルパワー制御 (Terrain-Aware Control)
            bool isPedalingHard = currentPower > 15.0;
            bool isFeathering = currentPower >= 1.0 && currentPower <= 15.0;
            bool isDownhill = TrueRoadGradePercent < -3.0;

            float targetBrake = 0.0f;

            if (isPedalingHard)
            {
                // 1. しっかり漕ぐ (>15W): 加速・速度維持モード
                targetBrake = 0.0f;
                output.Throttle = Math.Max(output.Throttle, 0.20f);
            }
            else if (isFeathering)
            {
                if (isDownhill)
                {
                    // 2-A. 下り坂で軽く足を回す (1W〜15W): 【下り坂ブレーキ】
                    output.Throttle = 0.0f;
                    _pidController.Reset();

                    if (currentPacket != null && currentPacket.IsRaceOn && currentPacket.VelocityZ > 0.2f && currentPacket.SpeedKmh > 1.0f)
                    {
                        targetBrake = Math.Clamp(currentPacket.SpeedKmh / 10.0f, 0.15f, 1.0f);
                    }
                }
                else
                {
                    // 2-B. 平地・上り坂で軽く足を回す (1W〜15W): 【平地惰性走行（コースティング）】
                    output.Throttle = 0.0f;
                    targetBrake = 0.0f;
                    _pidController.Reset();
                }
            }
            else
            {
                // 3. ペダル完全停止 (<1W / 0W)
                if (isDownhill && currentCarSpeedMps > 1.0f)
                {
                    // 3-A. 下り坂で足を止める (0W): 【下り坂自動滑走（オートグライド）】
                    targetBrake = 0.0f;
                    double baseThrottle = 0.20;
                    double additionalThrottle = Math.Abs(TrueRoadGradePercent) * 0.05;
                    output.Throttle = (float)Math.Min(baseThrottle + additionalThrottle, 0.80);
                }
                else
                {
                    // 3-B. 平地・上り坂で足を止める (0W): 【平地車両停止ブレーキ】
                    output.Throttle = 0.0f;
                    _pidController.Reset();

                    if (currentPacket != null && currentPacket.IsRaceOn && currentPacket.VelocityZ > 0.2f && currentPacket.SpeedKmh > 1.0f)
                    {
                        targetBrake = Math.Clamp(currentPacket.SpeedKmh / 10.0f, 0.15f, 1.0f);
                    }
                }
            }

            // ★ブレーキ作動時の「じわ〜っと100%」ランプアップ平滑化フィルタ
            if (targetBrake > 0.0f)
            {
                // ブレーキが作動した場合は、EMAフィルタでジワーッと滑らかにビルドアップ（約0.8〜1.0秒でターゲット値に連動）
                _filteredBrake = (_filteredBrake * (1.0 - BRAKE_RAMP_ALPHA)) + (targetBrake * BRAKE_RAMP_ALPHA);
                output.Brake = (float)Math.Clamp(_filteredBrake, 0.0, targetBrake);

                // ブレーキ中はスロットルを即座にゼロリセット
                _filteredThrottle = 0.0;
                output.Throttle = 0.0f;
            }
            else
            {
                // ブレーキ解除時は即座にブレーキフィルタをリセットし、ペダリング開始時の応答性を確保
                _filteredBrake = 0.0;
                output.Brake = 0.0f;

                _filteredThrottle = (_filteredThrottle * (1.0 - THROTTLE_ALPHA)) + (output.Throttle * THROTTLE_ALPHA);
                output.Throttle = (float)_filteredThrottle;
            }

            return output;
        }

        /// <summary>
        /// 物理方程式: P * eta = v * (m * g * Crr * cos(θ) + m * g * sin(θ)) + Aaero * v^3 
        /// を ニュートン・ラフソン法で解き、目標速度 v (m/s) を算出します。
        /// </summary>
        private double CalculateTargetSpeed(double power, double roadGradePercent)
        {
            // ペダルパワーが負の場合は0に丸める
            double p = Math.Max(0.0, currentPowerCalculated(power));

            // 勾配パーセンテージから正確に cos(θ) と sin(θ) を逆算します
            double grade = roadGradePercent / 100.0;
            double cosPitch = 1.0 / Math.Sqrt(1.0 + grade * grade);
            double sinPitch = grade / Math.Sqrt(1.0 + grade * grade);

            // 勾配・転がり抵抗項 (B * v) の係数 B
            // B = m * g * (Crr * cos(θ) + sin(θ))
            double B = M * G * (Crr * cosPitch + sinPitch);

            // 定数項 C = -P * eta
            double C = -p * Eta;

            // ニュートン法で 3次方程式: Aaero * v^3 + B * v + C = 0 を解く
            // 【究極のデッドロック回避設計】
            // B < 0 (下り坂) の場合、初期値が小さすぎると途中で負の領域に落ちて永久に 0.5 m/s (1.8 km/h) に張り付くバグが発生します。
            // そのため、下り坂のときは「重力による最低滑走速度 (Math.Sqrt(-B / Aaero))」を初期値の基準とすることで、
            // 常に導関数 f'(v) が確実に正になる「正しい実数解のすぐ手前」から探索を安全にスタートさせます。
            double v;
            if (B < 0.0)
            {
                v = Math.Max(5.0, Math.Sqrt(-B / Aaero));
            }
            else
            {
                v = _lastTargetSpeed > 1.0 ? _lastTargetSpeed : 5.0;
            }

            for (int i = 0; i < 15; i++)
            {
                double f = Aaero * v * v * v + B * v + C;
                double df = 3.0 * Aaero * v * v + B; // 導関数 f'(v)

                // 傾きが極端に0に近い場合は微小値で割るエラーをガード
                if (Math.Abs(df) < 1e-4)
                {
                    df = df >= 0 ? 1e-4 : -1e-4;
                }

                double nextV = v - f / df;

                // 万が一負 of 領域に飛んでしまった場合は、大きな正の値 (15.0 m/s) にワープさせてデッドロックを防止します
                if (nextV <= 0.0)
                {
                    nextV = 15.0;
                }

                // 収束判定
                if (Math.Abs(nextV - v) < 1e-4)
                {
                    v = nextV;
                    break;
                }

                v = nextV;
            }

            // 平地または上り坂（B >= 0）において、パワーが0の場合は完全停止（v = 0）とする
            if (p <= 0.01 && B >= 0)
            {
                v = 0.0;
            }

            _lastTargetSpeed = v;
            return v;
        }

        private double currentPowerCalculated(double rawPower)
        {
            return rawPower;
        }
    }
}
