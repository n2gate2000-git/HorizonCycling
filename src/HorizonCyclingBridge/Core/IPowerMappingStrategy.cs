using HorizonCyclingBridge.Telemetry;

namespace HorizonCyclingBridge.Core
{
    public interface IPowerMappingStrategy
    {
        Action<string>? OnDebugLog { get; set; }

        /// <summary>
        /// 現在のスマートローラーの出力（W）とForzaから取得した最新のパケットデータを渡し、
        /// 仮想コントローラーに送信するべきアクセル・ブレーキ値を計算します。
        /// </summary>
        /// <param name="currentPower">現在のペダルパワー (W)</param>
        /// <param name="currentPacket">Forzaからのテレメトリデータ packet</param>
        /// <returns>アクセル・ブレーキ開度の出力</returns>
        ControlOutput CalculateOutput(double currentPower, ForzaDataPacket currentPacket);
    }
}
