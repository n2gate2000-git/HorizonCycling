using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace HorizonCyclingBridge.Trainer
{
    public class FtmsClient : IDisposable
    {
        // Bluetooth 標準 GATT UUIDs (FTMS: Fitness Machine Service)
        private static readonly Guid FTMS_SERVICE_UUID = Guid.Parse("00001826-0000-1000-8000-00805f9b34fb");
        private static readonly Guid BIKE_DATA_CHAR_UUID = Guid.Parse("00002ad2-0000-1000-8000-00805f9b34fb");
        private static readonly Guid CONTROL_POINT_CHAR_UUID = Guid.Parse("00002ad9-0000-1000-8000-00805f9b34fb");

        private BluetoothLEDevice? _device;
        private GattCharacteristic? _bikeDataChar;
        private GattCharacteristic? _controlPointChar;
        private BluetoothLEAdvertisementWatcher? _watcher;
        private TaskCompletionSource<bool>? _connectTcs;

        /// <summary>
        /// スマートローラーから瞬時パワー (W) を受信したときに発生するイベント
        /// </summary>
        public event Action<int>? OnPowerReceived;

        /// <summary>
        /// スマートローラーからケイデンス (RPM) を受信したときに発生するイベント
        /// </summary>
        public event Action<double>? OnCadenceReceived;

        /// <summary>
        /// スマートローラーから瞬時速度 (km/h) を受信したときに発生するイベント
        /// </summary>
        public event Action<double>? OnSpeedReceived;

        /// <summary>
        /// BLEステータス変更時のログメッセージを受信するイベント
        /// </summary>
        public event Action<string>? OnStatusMessage;

        /// <summary>
        /// 現在スマートローラーと接続されているかどうか
        /// </summary>
        public bool IsConnected => _device != null && _device.ConnectionStatus == BluetoothConnectionStatus.Connected;

        /// <summary>
        /// 周辺のFTMSスマートローラーをスキャンし、最初に見つかったデバイスに自動接続します。
        /// </summary>
        /// <param name="timeoutMs">スキャンタイムアウト時間 (ミリ秒)</param>
        /// <returns>接続が成功した場合は真</returns>
        public async Task<bool> ScanAndConnectAsync(int timeoutMs = 20000)
        {
            if (IsConnected)
            {
                OnStatusMessage?.Invoke("[BLE] Already connected.");
                return true;
            }

            _connectTcs = new TaskCompletionSource<bool>();
            
            // BLEアドバタイズメント監視のセットアップ
            _watcher = new BluetoothLEAdvertisementWatcher();
            // FTMSサービスUUIDを持つアドバタイズのみにフィルタリング
            _watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(FTMS_SERVICE_UUID);
            
            _watcher.Received += Watcher_Received;
            _watcher.Stopped += Watcher_Stopped;

            OnStatusMessage?.Invoke("[BLE] Scanning for FTMS Smart Trainer. Please make sure the trainer is powered on and pairing-ready...");
            _watcher.Start();

            // タイムアウト時の自動停止タスク
            var timeoutTask = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(_connectTcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                OnStatusMessage?.Invoke("[BLE] Scanning timed out. No FTMS devices found.");
                StopScanning();
                return false;
            }

            return await _connectTcs.Task;
        }

        private async void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            // デバイスが見つかったら即座にスキャンを停止
            StopScanning();

            string deviceName = string.IsNullOrEmpty(args.Advertisement.LocalName) ? "Unknown Trainer" : args.Advertisement.LocalName;
            OnStatusMessage?.Invoke($"[BLE] Found FTMS Trainer: '{deviceName}' Address: {args.BluetoothAddress:X}");

            try
            {
                var device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
                if (device != null)
                {
                    bool success = await SetupServicesAsync(device);
                    _connectTcs?.TrySetResult(success);
                }
                else
                {
                    OnStatusMessage?.Invoke("[BLE] Failed to open Bluetooth device mapping.");
                    _connectTcs?.TrySetResult(false);
                }
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"[BLE] Connection error: {ex.Message}");
                _connectTcs?.TrySetException(ex);
            }
        }

        private void Watcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            OnStatusMessage?.Invoke("[BLE] Scanning stopped.");
        }

        private void StopScanning()
        {
            if (_watcher != null)
            {
                _watcher.Stop();
                _watcher.Received -= Watcher_Received;
                _watcher.Stopped -= Watcher_Stopped;
                _watcher = null;
            }
        }

        private async Task<bool> SetupServicesAsync(BluetoothLEDevice device)
        {
            _device = device;
            OnStatusMessage?.Invoke($"[BLE] Querying GATT Services from {_device.Name}...");

            // 接続状態変更監視
            _device.ConnectionStatusChanged += Device_ConnectionStatusChanged;

            // FTMSサービス取得
            var servicesResult = await _device.GetGattServicesForUuidAsync(FTMS_SERVICE_UUID);
            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                OnStatusMessage?.Invoke("[BLE] Failed to acquire FTMS GATT service.");
                return false;
            }

            var service = servicesResult.Services.FirstOrDefault();
            if (service == null)
            {
                OnStatusMessage?.Invoke("[BLE] Service list is empty.");
                return false;
            }

            // サービスの全特性を一度に取得（往復回数を減らし、GATT競合を回避する堅牢なアプローチ）
            GattCharacteristicsResult? charResult = null;
            int retryCount = 3;
            for (int i = 0; i < retryCount; i++)
            {
                // BluetoothCacheMode.Uncached により、OSの古いキャッシュを破棄して実機から直接ライブで取得
                charResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                if (charResult.Status == GattCommunicationStatus.Success && charResult.Characteristics.Count > 0)
                {
                    break;
                }
                OnStatusMessage?.Invoke($"[BLE] Querying characteristics failed or returned empty (Status: {charResult?.Status}). Retrying in 300ms... (Attempt {i + 1}/{retryCount})");
                await Task.Delay(300);
            }

            if (charResult == null || charResult.Status != GattCommunicationStatus.Success)
            {
                OnStatusMessage?.Invoke($"[BLE] Failed to acquire GATT characteristics after retries. Status: {charResult?.Status}");
                return false;
            }

            // 取得した全特性リストから、必要なUUIDを持つ特性を安全にマッピング
            _bikeDataChar = charResult.Characteristics.FirstOrDefault(c => c.Uuid == BIKE_DATA_CHAR_UUID);
            _controlPointChar = charResult.Characteristics.FirstOrDefault(c => c.Uuid == CONTROL_POINT_CHAR_UUID);

            if (_bikeDataChar == null)
            {
                OnStatusMessage?.Invoke("[BLE] FTMS Indoor Bike Data characteristic not found in the characteristic list.");
                return false;
            }

            // Notifyを有効化し、データ受信を開始
            var notifyStatus = await _bikeDataChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);

            if (notifyStatus == GattCommunicationStatus.Success)
            {
                _bikeDataChar.ValueChanged += BikeDataChar_ValueChanged;
                OnStatusMessage?.Invoke("[BLE] Subscribed to Indoor Bike Data telemetry notification.");
            }
            else
            {
                OnStatusMessage?.Invoke("[BLE] Failed to enable Notify on Indoor Bike Data.");
                return false;
            }

            // 負荷コントロール権の要求
            if (_controlPointChar != null)
            {
                // FTMS仕様に準拠するため、コントロール送信の前にまずIndication（応答通知）を有効化します
                try
                {
                    var cpConfigStatus = await _controlPointChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Indicate);
                    if (cpConfigStatus == GattCommunicationStatus.Success)
                    {
                        OnStatusMessage?.Invoke("[BLE] Enabled Indications on Trainer Control Point.");
                    }
                    else
                    {
                        OnStatusMessage?.Invoke("[BLE] WARNING: Failed to enable Indications on Trainer Control Point.");
                    }
                }
                catch (Exception ex)
                {
                    OnStatusMessage?.Invoke($"[BLE] WARNING: Error setting up Control Point indications: {ex.Message}");
                }

                bool controlAcquired = await RequestControlAsync();
                if (controlAcquired)
                {
                    OnStatusMessage?.Invoke("[BLE] Successfully acquired Trainer resistance control.");
                }
                else
                {
                    OnStatusMessage?.Invoke("[BLE] WARNING: Failed to acquire Trainer resistance control. Inclination feedback will not work.");
                }
            }

            OnStatusMessage?.Invoke($"[BLE] Connection to '{_device.Name}' completely established. Ride Ready!");
            return true;
        }

        private async Task<bool> RequestControlAsync()
        {
            if (_controlPointChar == null) return false;

            // FTMS Control Point: OpCode 0x00 = Request Control
            byte[] cmd = new byte[] { 0x00 };
            try
            {
                var result = await _controlPointChar.WriteValueAsync(cmd.AsBuffer(), GattWriteOption.WriteWithResponse);
                return result == GattCommunicationStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// スマートローラーに対して、ゲーム内の斜度に基づく負荷傾斜（-36.0% 〜 +36.0%）を送信します。
        /// </summary>
        /// <param name="inclinationPercent">道路斜度（パーセンテージ、例: 3.5% なら 3.5、下り坂 -2.0% なら -2.0）</param>
        /// <returns>送信成否</returns>
        public async Task<bool> SetTargetInclinationAsync(double inclinationPercent)
        {
            if (_controlPointChar == null || !IsConnected) return false;

            // FTMS仕様: Set Target Inclination (OpCode 0x11)
            // 引数: 16-bit signed integer in units of 0.1% (-360 to 360)
            short val = (short)Math.Clamp(inclinationPercent * 10.0, -360.0, 360.0);
            byte[] cmd = new byte[]
            {
                0x11,
                (byte)(val & 0xFF),
                (byte)((val >> 8) & 0xFF)
            };

            try
            {
                var result = await _controlPointChar.WriteValueAsync(cmd.AsBuffer(), GattWriteOption.WriteWithResponse);
                return result == GattCommunicationStatus.Success;
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"[BLE] Resistance update failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// スマートローラーの物理抵抗レベルを設定します。
        /// 0 を設定すると、速度依存の空気抵抗シミュレーションがオフになり、完全に負荷が解放されます（スピンフリー）。
        /// </summary>
        /// <param name="level">抵抗レベル（通常 0〜100、0が完全にフリー）</param>
        /// <returns>送信成否</returns>
        public async Task<bool> SetTargetResistanceLevelAsync(byte level)
        {
            if (_controlPointChar == null || !IsConnected) return false;

            // FTMS仕様: Set Target Resistance Level (OpCode 0x04)
            // 引数: 8-bit unsigned integer
            byte[] cmd = new byte[]
            {
                0x04,
                level
            };

            try
            {
                var result = await _controlPointChar.WriteValueAsync(cmd.AsBuffer(), GattWriteOption.WriteWithResponse);
                return result == GattCommunicationStatus.Success;
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"[BLE] Target resistance level update failed: {ex.Message}");
                return false;
            }
        }

        private void BikeDataChar_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);

            if (data.Length < 2) return;

            ushort flags = BitConverter.ToUInt16(data, 0);
            int offset = 2;

            // FTMS Indoor Bike Data 仕様パース
            // 速度データ(Instantaneous Speed: 0.01 km/h) は More Data (flags Bit 0) が0のとき常に先頭に存在
            if (data.Length >= offset + 2)
            {
                ushort rawSpeed = BitConverter.ToUInt16(data, offset);
                double speedKmh = rawSpeed * 0.01;
                OnSpeedReceived?.Invoke(speedKmh);
                offset += 2;
            }

            // フラグ Bit 2: Instantaneous Cadence Present (0.5 RPM単位)
            if ((flags & 0x0004) != 0 && data.Length >= offset + 2)
            {
                double cadence = BitConverter.ToUInt16(data, offset) * 0.5;
                OnCadenceReceived?.Invoke(cadence);
                offset += 2;
            }

            // フラグ Bit 6: Instantaneous Power Present (1 Watt単位)
            if ((flags & 0x0040) != 0 && data.Length >= offset + 2)
            {
                short power = BitConverter.ToInt16(data, offset);
                OnPowerReceived?.Invoke(power);
                offset += 2;
            }
        }

        private void Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            OnStatusMessage?.Invoke($"[BLE] Connection status changed: {sender.ConnectionStatus}");
        }

        public void Disconnect()
        {
            StopScanning();

            if (_bikeDataChar != null)
            {
                _bikeDataChar.ValueChanged -= BikeDataChar_ValueChanged;
                _bikeDataChar = null;
            }
            _controlPointChar = null;

            if (_device != null)
            {
                _device.ConnectionStatusChanged -= Device_ConnectionStatusChanged;
                _device.Dispose();
                _device = null;
            }
            OnStatusMessage?.Invoke("[BLE] Disconnected and cleaned up GATT sessions.");
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
