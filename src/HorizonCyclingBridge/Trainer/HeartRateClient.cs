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
    /// <summary>
    /// Bluetooth LE Heart Rate Service (0x180D) / Measurement (0x2A37) クライアント
    /// </summary>
    public class HeartRateClient : IDisposable
    {
        public static readonly Guid HEART_RATE_SERVICE_UUID = Guid.Parse("0000180d-0000-1000-8000-00805f9b34fb");
        public static readonly Guid HEART_RATE_MEASUREMENT_CHAR_UUID = Guid.Parse("00002a37-0000-1000-8000-00805f9b34fb");

        private BluetoothLEDevice? _device;
        private GattCharacteristic? _heartRateMeasurementChar;
        private BluetoothLEAdvertisementWatcher? _watcher;
        private TaskCompletionSource<bool>? _connectTcs;
        private ulong _targetAddress = 0;

        public event Action<int>? OnHeartRateReceived;
        public event Action<string>? OnStatusMessage;
        public event Action<bool>? OnConnectionStateChanged;

        public bool IsConnected => _device != null && _device.ConnectionStatus == BluetoothConnectionStatus.Connected;
        public string DeviceName => _device != null ? (!string.IsNullOrEmpty(_device.Name) ? _device.Name : "Heart Rate Monitor") : "None";
        public ulong BluetoothAddress => _device?.BluetoothAddress ?? 0;

        /// <summary>
        /// 心拍計のAdvertisementをスキャンして接続
        /// </summary>
        public async Task<bool> ScanAndConnectAsync(int timeoutMs = 20000, ulong targetAddress = 0)
        {
            if (IsConnected)
            {
                OnStatusMessage?.Invoke("[HRM] Already connected.");
                return true;
            }

            _targetAddress = targetAddress;
            _connectTcs = new TaskCompletionSource<bool>();

            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            _watcher.Received += Watcher_Received;
            _watcher.Stopped += Watcher_Stopped;

            string targetStr = _targetAddress == 0 ? "any" : $"{_targetAddress:X}";
            OnStatusMessage?.Invoke($"[HRM] Scanning for Heart Rate Monitor (Target: {targetStr})...");
            _watcher.Start();

            var timeoutTask = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(_connectTcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                OnStatusMessage?.Invoke("[HRM] Scanning timed out. No Heart Rate Monitor found.");
                StopScanning();
                return false;
            }

            return await _connectTcs.Task;
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

        private async void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            if (_targetAddress != 0)
            {
                if (args.BluetoothAddress != _targetAddress)
                {
                    return;
                }
            }
            else
            {
                bool hasHr = args.Advertisement.ServiceUuids.Any(u => u == HEART_RATE_SERVICE_UUID || u.ToString().StartsWith("0000180d", StringComparison.OrdinalIgnoreCase));
                string upperName = (args.Advertisement.LocalName ?? string.Empty).ToUpperInvariant();
                bool isHrByName = upperName.Contains("HR") || upperName.Contains("HEART") || upperName.Contains("POLAR") || upperName.Contains("TICKR") || upperName.Contains("COOSPO") || upperName.Contains("MAGENE") || upperName.Contains("H10") || upperName.Contains("H64") || upperName.Contains("H303");
                if (!hasHr && !isHrByName)
                {
                    return;
                }
            }

            StopScanning();

            string deviceName = string.IsNullOrEmpty(args.Advertisement.LocalName) ? "Heart Rate Monitor" : args.Advertisement.LocalName;
            OnStatusMessage?.Invoke($"[HRM] Found HRM: '{deviceName}' Address: {args.BluetoothAddress:X}");

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
                    OnStatusMessage?.Invoke("[HRM] Failed to open Bluetooth device mapping.");
                    _connectTcs?.TrySetResult(false);
                }
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"[HRM] Connection error: {ex.Message}");
                _connectTcs?.TrySetException(ex);
            }
        }

        private void Watcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            OnStatusMessage?.Invoke("[HRM] Scanning stopped.");
        }

        private async Task<bool> SetupServicesAsync(BluetoothLEDevice device)
        {
            _device = device;
            OnStatusMessage?.Invoke($"[HRM] Querying GATT Services from {_device.Name}...");

            _device.ConnectionStatusChanged += Device_ConnectionStatusChanged;

            var servicesResult = await _device.GetGattServicesForUuidAsync(HEART_RATE_SERVICE_UUID);
            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                OnStatusMessage?.Invoke("[HRM] Failed to acquire Heart Rate GATT service.");
                return false;
            }

            var service = servicesResult.Services.FirstOrDefault();
            if (service == null)
            {
                OnStatusMessage?.Invoke("[HRM] Heart Rate Service list is empty.");
                return false;
            }

            GattCharacteristicsResult? charResult = null;
            int retryCount = 3;
            for (int i = 0; i < retryCount; i++)
            {
                charResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                if (charResult.Status == GattCommunicationStatus.Success && charResult.Characteristics.Count > 0)
                {
                    break;
                }
                OnStatusMessage?.Invoke($"[HRM] Querying characteristics failed. Retrying... ({i + 1}/{retryCount})");
                await Task.Delay(300);
            }

            if (charResult == null || charResult.Status != GattCommunicationStatus.Success)
            {
                OnStatusMessage?.Invoke("[HRM] Failed to acquire Heart Rate characteristics.");
                return false;
            }

            _heartRateMeasurementChar = charResult.Characteristics.FirstOrDefault(c => c.Uuid == HEART_RATE_MEASUREMENT_CHAR_UUID);
            if (_heartRateMeasurementChar == null)
            {
                OnStatusMessage?.Invoke("[HRM] Heart Rate Measurement characteristic (0x2A37) not found.");
                return false;
            }

            var notifyStatus = await _heartRateMeasurementChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);

            if (notifyStatus == GattCommunicationStatus.Success)
            {
                _heartRateMeasurementChar.ValueChanged += HeartRateMeasurementChar_ValueChanged;
                OnStatusMessage?.Invoke("[HRM] Subscribed to Heart Rate Measurement notifications.");
                OnConnectionStateChanged?.Invoke(true);
            }
            else
            {
                OnStatusMessage?.Invoke("[HRM] Failed to enable Notify on Heart Rate Measurement.");
                return false;
            }

            OnStatusMessage?.Invoke($"[HRM] Heart Rate Monitor '{_device.Name}' connected successfully!");
            return true;
        }

        private void HeartRateMeasurementChar_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);

            int hr = ParseHeartRate(data);
            if (hr > 0)
            {
                OnHeartRateReceived?.Invoke(hr);
            }
        }

        /// <summary>
        /// GATT 0x2A37 Heart Rate Measurement パケットから心拍数(BPM)を解析
        /// </summary>
        public static int ParseHeartRate(byte[] data)
        {
            if (data == null || data.Length < 2) return 0;

            byte flags = data[0];
            bool is16Bit = (flags & 0x01) != 0;

            if (is16Bit)
            {
                if (data.Length < 3) return 0;
                return BitConverter.ToUInt16(data, 1);
            }
            else
            {
                return data[1];
            }
        }

        private void Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            bool connected = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
            OnStatusMessage?.Invoke($"[HRM] Connection status changed: {sender.ConnectionStatus}");
            OnConnectionStateChanged?.Invoke(connected);
        }

        public void Disconnect()
        {
            StopScanning();

            if (_heartRateMeasurementChar != null)
            {
                _heartRateMeasurementChar.ValueChanged -= HeartRateMeasurementChar_ValueChanged;
                _heartRateMeasurementChar = null;
            }

            if (_device != null)
            {
                _device.ConnectionStatusChanged -= Device_ConnectionStatusChanged;
                _device.Dispose();
                _device = null;
            }
            OnConnectionStateChanged?.Invoke(false);
            OnStatusMessage?.Invoke("[HRM] Disconnected and cleaned up Heart Rate sessions.");
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
