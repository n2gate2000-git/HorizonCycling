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
    public class CyclingPowerClient : IDisposable
    {
        private static readonly Guid CYCLING_POWER_SERVICE_UUID = Guid.Parse("00001818-0000-1000-8000-00805f9b34fb");
        private static readonly Guid POWER_MEASUREMENT_CHAR_UUID = Guid.Parse("00002a63-0000-1000-8000-00805f9b34fb");

        private BluetoothLEDevice? _device;
        private GattCharacteristic? _powerMeasurementChar;
        private BluetoothLEAdvertisementWatcher? _watcher;
        private TaskCompletionSource<bool>? _connectTcs;
        private ulong _targetAddress = 0;

        public event Action<int>? OnPowerReceived;
        public event Action<string>? OnStatusMessage;

        public bool IsConnected => _device != null && _device.ConnectionStatus == BluetoothConnectionStatus.Connected;

        public async Task<bool> ScanAndConnectAsync(int timeoutMs = 20000, ulong targetAddress = 0)
        {
            if (IsConnected)
            {
                OnStatusMessage?.Invoke("[BLE] Already connected.");
                return true;
            }

            _targetAddress = targetAddress;
            _connectTcs = new TaskCompletionSource<bool>();
            
            _watcher = new BluetoothLEAdvertisementWatcher();
            _watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(CYCLING_POWER_SERVICE_UUID);
            
            _watcher.Received += Watcher_Received;
            _watcher.Stopped += Watcher_Stopped;

            string targetStr = _targetAddress == 0 ? "any" : $"{_targetAddress:X}";
            OnStatusMessage?.Invoke($"[BLE] Scanning for Cycling Power Meter (Target: {targetStr})...");
            _watcher.Start();

            var timeoutTask = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(_connectTcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                OnStatusMessage?.Invoke("[BLE] Scanning timed out. No Power Meter found.");
                StopScanning();
                return false;
            }

            return await _connectTcs.Task;
        }

        private async void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            if (_targetAddress != 0 && args.BluetoothAddress != _targetAddress)
            {
                // Not the target device
                return;
            }

            StopScanning();

            string deviceName = string.IsNullOrEmpty(args.Advertisement.LocalName) ? "Unknown Power Meter" : args.Advertisement.LocalName;
            OnStatusMessage?.Invoke($"[BLE] Found Power Meter: '{deviceName}' Address: {args.BluetoothAddress:X}");

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

            _device.ConnectionStatusChanged += Device_ConnectionStatusChanged;

            var servicesResult = await _device.GetGattServicesForUuidAsync(CYCLING_POWER_SERVICE_UUID);
            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                OnStatusMessage?.Invoke("[BLE] Failed to acquire Cycling Power GATT service.");
                return false;
            }

            var service = servicesResult.Services.FirstOrDefault();
            if (service == null)
            {
                OnStatusMessage?.Invoke("[BLE] Service list is empty.");
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
                OnStatusMessage?.Invoke($"[BLE] Querying characteristics failed. Retrying... ({i + 1}/{retryCount})");
                await Task.Delay(300);
            }

            if (charResult == null || charResult.Status != GattCommunicationStatus.Success)
            {
                OnStatusMessage?.Invoke($"[BLE] Failed to acquire GATT characteristics.");
                return false;
            }

            _powerMeasurementChar = charResult.Characteristics.FirstOrDefault(c => c.Uuid == POWER_MEASUREMENT_CHAR_UUID);

            if (_powerMeasurementChar == null)
            {
                OnStatusMessage?.Invoke("[BLE] Power Measurement characteristic not found.");
                return false;
            }

            var notifyStatus = await _powerMeasurementChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);

            if (notifyStatus == GattCommunicationStatus.Success)
            {
                _powerMeasurementChar.ValueChanged += PowerMeasurementChar_ValueChanged;
                OnStatusMessage?.Invoke("[BLE] Subscribed to Cycling Power Measurement notification.");
            }
            else
            {
                OnStatusMessage?.Invoke("[BLE] Failed to enable Notify on Power Measurement.");
                return false;
            }

            OnStatusMessage?.Invoke($"[BLE] Connection to '{_device.Name}' completely established. Ride Ready!");
            return true;
        }

        private void PowerMeasurementChar_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);

            if (data.Length < 4) return;

            // Byte 0-1: Flags (UInt16)
            // Byte 2-3: Instantaneous Power (SInt16)
            short power = BitConverter.ToInt16(data, 2);
            OnPowerReceived?.Invoke(power);
        }

        private void Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            OnStatusMessage?.Invoke($"[BLE] Connection status changed: {sender.ConnectionStatus}");
        }

        public void Disconnect()
        {
            StopScanning();

            if (_powerMeasurementChar != null)
            {
                _powerMeasurementChar.ValueChanged -= PowerMeasurementChar_ValueChanged;
                _powerMeasurementChar = null;
            }

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
