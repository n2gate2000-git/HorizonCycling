namespace HorizonCyclingBridge.Core
{
    public class ControlOutput
    {
        /// <summary>
        /// アクセル開度 (0.0 から 1.0)
        /// </summary>
        public float Throttle { get; set; } = 0.0f;

        /// <summary>
        /// ブレーキ開度 (0.0 から 1.0)
        /// </summary>
        public float Brake { get; set; } = 0.0f;
    }
}
