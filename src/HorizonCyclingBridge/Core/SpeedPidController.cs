using System;

namespace HorizonCyclingBridge.Core
{
    public class SpeedPidController
    {
        private readonly float _kp;
        private readonly float _ki;
        private readonly float _kd;

        private float _integral = 0f;
        private float _previousError = 0f;
        private readonly float _maxIntegral; // アンチワインドアップ用の積分上限値

        /// <summary>
        /// PIDコントローラーを初期化します。
        /// </summary>
        /// <param name="kp">比例ゲイン (Proportional)</param>
        /// <param name="ki">積分ゲイン (Integral)</param>
        /// <param name="kd">微分ゲイン (Derivative)</param>
        /// <param name="maxIntegral">アンチワインドアップ積分上限値</param>
        public SpeedPidController(float kp = 0.5f, float ki = 0.1f, float kd = 0.05f, float maxIntegral = 5.0f)
        {
            _kp = kp;
            _ki = ki;
            _kd = kd;
            _maxIntegral = maxIntegral;
        }

        /// <summary>
        /// 目標速度と現在の車の速度から、最適なアクセルおよびブレーキ出力を計算します。
        /// </summary>
        /// <param name="targetSpeed">目標とする自転車速度 (m/s)</param>
        /// <param name="currentCarSpeed">現在のゲーム内の車の速度 (m/s)</param>
        /// <param name="deltaTime">前フレームからの経過時間 (秒)</param>
        /// <returns>アクセルとブレーキ開度の決定値 (0.0〜1.0)</returns>
        public ControlOutput Compute(float targetSpeed, float currentCarSpeed, float deltaTime)
        {
            // 時間差が極端に小さい、または負の場合のフォールバック (60FPS想定)
            if (deltaTime <= 0.0f)
            {
                deltaTime = 0.016f;
            }

            float error = targetSpeed - currentCarSpeed;

            // P (比例項)
            float p = _kp * error;

            // I (積分項) と アンチワインドアップ（積分飽和制限）
            _integral += error * deltaTime;
            _integral = Math.Clamp(_integral, -_maxIntegral, _maxIntegral);
            float i = _ki * _integral;

            // D (微分項)
            float derivative = (error - _previousError) / deltaTime;
            float d = _kd * derivative;

            // 合計制御出力の計算
            float output = p + i + d;
            _previousError = error;

            // 出力をアクセル(0〜1)とブレーキ(0〜1)にマッピングして返却します。
            // 正の出力はアクセル（ブレーキは0）、負の出力はブレーキ（アクセルは0）とします。
            return new ControlOutput
            {
                Throttle = Math.Clamp(output, 0f, 1f),
                Brake = 0.0f
            };
        }

        /// <summary>
        /// 積分値と前回誤差をクリアします。車両の衝突時やリスタート時に使用します。
        /// </summary>
        public void Reset()
        {
            _integral = 0f;
            _previousError = 0f;
        }
    }
}
