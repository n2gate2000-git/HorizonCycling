using System;
using System.Runtime.InteropServices;

namespace HorizonCyclingBridge.Controller
{
    public class VJoyVehicleController : IDisposable
    {
        private const string DLL_NAME = "vJoyInterface.dll";

        [StructLayout(LayoutKind.Sequential)]
        public struct JoystickState
        {
            public byte bDevice; // 1-16
            public int Throttle; // 0x1 to 0x8000 (32768)
            public int Rudder;
            public int Aileron;
            public int AxisX;       // アクセル用（0〜32768にマッピング）
            public int AxisY;       // ブレーキ用（0〜32768にマッピング）
            public int AxisZ;
            public int AxisXRot;    // アクセル代替（トリガーエミュレーション用）
            public int AxisYRot;    // ブレーキ代替
            public int AxisZRot;
            public int Slider;
            public int Dial;
            public int Wheel;
            public int AxisVX;
            public int AxisVY;
            public int AxisVZ;
            public int AxisVBRX;
            public int AxisVBRY;
            public int AxisVBRZ;
            public uint Buttons;
            public uint ButtonsEx1;
            public uint ButtonsEx2;
            public uint ButtonsEx3;
            public uint Hat;
            public uint Hat2;
            public uint Hat3;
            public uint Hat4;
        }

        [DllImport(DLL_NAME, EntryPoint = "vJoyEnabled")]
        private static extern bool vJoyEnabled();

        [DllImport(DLL_NAME, EntryPoint = "AcquireVJD")]
        private static extern bool AcquireVJD(uint rID);

        [DllImport(DLL_NAME, EntryPoint = "RelinquishVJD")]
        private static extern void RelinquishVJD(uint rID);

        [DllImport(DLL_NAME, EntryPoint = "UpdateVJD")]
        private static extern bool UpdateVJD(uint rID, ref JoystickState pPosition);

        [DllImport(DLL_NAME, EntryPoint = "ResetVJD")]
        private static extern bool ResetVJD(uint rID);

        private readonly uint _deviceId;
        private bool _acquired;
        private JoystickState _state;

        public bool IsAcquired => _acquired;

        public VJoyVehicleController(uint deviceId = 1)
        {
            _deviceId = deviceId;
            _state = new JoystickState { bDevice = (byte)_deviceId };
        }

        /// <summary>
        /// vJoyデバイスの初期化および専有（アクワイア）を行います。
        /// </summary>
        public bool Initialize()
        {
            try
            {
                if (!vJoyEnabled())
                {
                    Console.WriteLine("[vJoy] vJoy driver is not enabled/installed on this system.");
                    return false;
                }

                _acquired = AcquireVJD(_deviceId);
                if (!_acquired)
                {
                    Console.WriteLine($"[vJoy] Failed to acquire vJoy device {_deviceId}. It might be used by another app.");
                    return false;
                }

                ResetVJD(_deviceId);
                Console.WriteLine($"[vJoy] Successfully acquired and reset vJoy device {_deviceId}.");
                return true;
            }
            catch (DllNotFoundException)
            {
                Console.WriteLine("[vJoy] ERROR: vJoyInterface.dll not found. Please install vJoy or place the DLL in the execution folder.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[vJoy] ERROR: Initialization failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// アクセルの出力値 (0.0 〜 1.0) を vJoy のジョイスティック軸 (1 〜 32768) にマッピングして送信します。
        /// </summary>
        /// <param name="throttle">アクセル開度 (0.0 to 1.0)</param>
        public void SendInputs(float throttle)
        {
            if (!_acquired) return;

            // 0.0〜1.0 の値を vJoy の標準範囲である 1〜32768 にマッピング
            // 32767 のスパンにマッピングして +1
            int throttleVal = 1 + (int)(Math.Clamp(throttle, 0f, 1f) * 32767);

            // 複数の汎用軸に同時にマッピングしておくことで、ゲーム側での検出性を高めます。
            _state.AxisX = throttleVal;     // アクセル
            _state.AxisXRot = throttleVal;  // トリガー代用

            // ブレーキ用の軸（AxisY/AxisYRot）は送信対象から除外（常にブレーキ0%相当のニュートラル値1に固定）
            _state.AxisY = 1;
            _state.AxisYRot = 1;

            UpdateVJD(_deviceId, ref _state);
        }

        /// <summary>
        /// vJoyデバイスを解放します。
        /// </summary>
        public void Dispose()
        {
            if (_acquired)
            {
                ResetVJD(_deviceId);
                RelinquishVJD(_deviceId);
                _acquired = false;
                Console.WriteLine($"[vJoy] Relinquished vJoy device {_deviceId}.");
            }
        }
    }
}
