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
        private readonly object _stateLock = new object();
        private float _lastThrottle = 0.0f;
        private float _lastBrake = 0.0f;
        private float _brakeOverride = 0.0f; // OpenBikeControl コントローラーのブレーキ入力 (0.0 to 1.0)

        /// <summary>
        /// ステータスメッセージ通知イベント
        /// </summary>
        public event Action<string>? OnStatusMessage;

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
                    OnStatusMessage?.Invoke("[vJoy] vJoy driver is not enabled/installed on this system.");
                    return false;
                }

                _acquired = AcquireVJD(_deviceId);
                if (!_acquired)
                {
                    OnStatusMessage?.Invoke($"[vJoy] Failed to acquire vJoy device {_deviceId}. It might be used by another app.");
                    return false;
                }

                ResetVJD(_deviceId);
                OnStatusMessage?.Invoke($"[vJoy] Successfully acquired and reset vJoy device {_deviceId}.");
                return true;
            }
            catch (DllNotFoundException)
            {
                OnStatusMessage?.Invoke("[vJoy] ERROR: vJoyInterface.dll not found. Please install vJoy or place the DLL in the execution folder.");
                return false;
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"[vJoy] ERROR: Initialization failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// アクセルおよびブレーキの出力値 (0.0 〜 1.0) を vJoy のジョイスティック軸 (1 〜 32768) にマッピングして送信します。
        /// </summary>
        /// <param name="throttle">アクセル開度 (0.0 to 1.0)</param>
        /// <param name="brake">ブレーキ開度 (0.0 to 1.0)</param>
        public void SendInputs(float throttle, float brake = 0.0f)
        {
            if (!_acquired) return;

            lock (_stateLock)
            {
                _lastThrottle = throttle;
                _lastBrake = brake;

                // コントローラーのブレーキ入力中はアクセルを戻し、強い方のブレーキ値を採用
                if (_brakeOverride > 0f)
                {
                    throttle = 0f;
                    brake = Math.Max(brake, _brakeOverride);
                }

                // 0.0〜1.0 の値を vJoy の標準範囲である 1〜32768 にマッピング
                int throttleVal = 1 + (int)(Math.Clamp(throttle, 0f, 1f) * 32767);
                int brakeVal = 1 + (int)(Math.Clamp(brake, 0f, 1f) * 32767);

                _state.AxisX = throttleVal;     // アクセル
                _state.AxisXRot = throttleVal;  // トリガー代用
                _state.AxisY = brakeVal;        // ブレーキ
                _state.AxisYRot = brakeVal;     // ブレーキトリガー代用

                UpdateVJD(_deviceId, ref _state);
            }
        }

        /// <summary>
        /// vJoy ボタン (1〜32) の押下状態を送信します。x360ce で Xbox コントローラーのボタンに割り当てて使用します。
        /// </summary>
        /// <param name="buttonNumber">vJoy ボタン番号 (1 to 32)</param>
        /// <param name="pressed">押下状態</param>
        public void SetButton(int buttonNumber, bool pressed)
        {
            if (!_acquired || buttonNumber < 1 || buttonNumber > 32) return;

            lock (_stateLock)
            {
                uint mask = 1u << (buttonNumber - 1);
                _state.Buttons = pressed ? (_state.Buttons | mask) : (_state.Buttons & ~mask);
                UpdateVJD(_deviceId, ref _state);
            }
        }

        /// <summary>
        /// コントローラーからのブレーキ入力 (0.0 〜 1.0) を設定し、直近のアクセル・ブレーキ出力に重ねて即時送信します。
        /// </summary>
        /// <param name="brake">ブレーキ開度 (0.0 to 1.0、0.0で解除)</param>
        public void SetBrakeOverride(float brake)
        {
            float throttle;
            float lastBrake;
            lock (_stateLock)
            {
                _brakeOverride = Math.Clamp(brake, 0f, 1f);
                throttle = _lastThrottle;
                lastBrake = _lastBrake;
            }
            SendInputs(throttle, lastBrake);
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
                OnStatusMessage?.Invoke($"[vJoy] Relinquished vJoy device {_deviceId}.");
            }
        }
    }
}
