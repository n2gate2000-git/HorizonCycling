using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using HorizonCyclingBridge.Controller;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace HorizonCyclingBridge.Trainer
{
    /// <summary>
    /// OpenBikeControl (https://openbikecontrol.org) BLE コントローラー クライアント
    /// ボタン状態 (d273f681) を購読し、接続直後に App Information (d273f683) を送信
    /// </summary>
    public class OpenBikeControlClient : IDisposable
    {
        public static readonly Guid OBC_SERVICE_UUID = Guid.Parse("d273f680-d548-419d-b9d1-fa0472345229");
        public static readonly Guid OBC_BUTTON_STATE_CHAR_UUID = Guid.Parse("d273f681-d548-419d-b9d1-fa0472345229");
        public static readonly Guid OBC_APP_INFO_CHAR_UUID = Guid.Parse("d273f683-d548-419d-b9d1-fa0472345229");

        public const string APP_ID = "horizoncycling";

        private BluetoothLEDevice? _device;
        private GattCharacteristic? _buttonStateChar;
        private GattCharacteristic? _appInfoChar;
        private BluetoothLEAdvertisementWatcher? _watcher;
        private TaskCompletionSource<bool>? _connectTcs;
        private ulong _targetAddress = 0;
        private readonly IReadOnlyList<byte> _supportedButtonIds;

        public event Action<byte[]>? OnButtonStateReceived;
        public event Action<string>? OnStatusMessage;
        public event Action<bool>? OnConnectionStateChanged;

        public bool IsConnected => _device != null && _device.ConnectionStatus == BluetoothConnectionStatus.Connected;
        public string DeviceName => _device != null ? (!string.IsNullOrEmpty(_device.Name) ? _device.Name : "OpenBikeControl") : "None";
        public ulong BluetoothAddress => _device?.BluetoothAddress ?? 0;

        public OpenBikeControlClient(IReadOnlyList<byte> supportedButtonIds)
        {
            _supportedButtonIds = supportedButtonIds;
        }

        /// <summary>
        /// OpenBikeControl サービスUUIDをアドバタイズしているコントローラーをスキャンして接続
        /// </summary>
        public async Task<bool> ScanAndConnectAsync(int timeoutMs = 20000, ulong targetAddress = 0)
        {
            if (IsConnected)
            {
                OnStatusMessage?.Invoke("[OBC] Already connected.");
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
            OnStatusMessage?.Invoke($"[OBC] Scanning for OpenBikeControl controller (Target: {targetStr})...");
            _watcher.Start();

            var timeoutTask = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(_connectTcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                OnStatusMessage?.Invoke("[OBC] Scanning timed out. No OpenBikeControl controller found.");
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
            else if (!args.Advertisement.ServiceUuids.Contains(OBC_SERVICE_UUID))
            {
                // デバイス名ではなくサービスUUIDのみで判定
                return;
            }

            StopScanning();

            string deviceName = string.IsNullOrEmpty(args.Advertisement.LocalName) ? "OpenBikeControl" : args.Advertisement.LocalName;
            OnStatusMessage?.Invoke($"[OBC] Found controller: '{deviceName}' Address: {args.BluetoothAddress:X}");

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
                    OnStatusMessage?.Invoke("[OBC] Failed to open Bluetooth device mapping.");
                    _connectTcs?.TrySetResult(false);
                }
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"[OBC] Connection error: {ex.Message}");
                _connectTcs?.TrySetException(ex);
            }
        }

        private void Watcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            OnStatusMessage?.Invoke("[OBC] Scanning stopped.");
        }

        private async Task<bool> SetupServicesAsync(BluetoothLEDevice device)
        {
            _device = device;
            OnStatusMessage?.Invoke($"[OBC] Querying GATT Services from {_device.Name}...");

            _device.ConnectionStatusChanged += Device_ConnectionStatusChanged;

            var servicesResult = await _device.GetGattServicesForUuidAsync(OBC_SERVICE_UUID, BluetoothCacheMode.Uncached);
            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                OnStatusMessage?.Invoke("[OBC] Failed to acquire OpenBikeControl GATT service.");
                return false;
            }

            var service = servicesResult.Services.FirstOrDefault();
            if (service == null)
            {
                OnStatusMessage?.Invoke("[OBC] OpenBikeControl Service list is empty.");
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
                OnStatusMessage?.Invoke($"[OBC] Querying characteristics failed. Retrying... ({i + 1}/{retryCount})");
                await Task.Delay(300);
            }

            if (charResult == null || charResult.Status != GattCommunicationStatus.Success)
            {
                OnStatusMessage?.Invoke("[OBC] Failed to acquire OpenBikeControl characteristics.");
                return false;
            }

            _buttonStateChar = charResult.Characteristics.FirstOrDefault(c => c.Uuid == OBC_BUTTON_STATE_CHAR_UUID);
            if (_buttonStateChar == null)
            {
                OnStatusMessage?.Invoke("[OBC] Button State characteristic (d273f681) not found.");
                return false;
            }

            var notifyStatus = await _buttonStateChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);

            if (notifyStatus == GattCommunicationStatus.Success)
            {
                _buttonStateChar.ValueChanged += ButtonStateChar_ValueChanged;
                OnStatusMessage?.Invoke("[OBC] Subscribed to Button State notifications.");
            }
            else
            {
                OnStatusMessage?.Invoke("[OBC] Failed to enable Notify on Button State.");
                return false;
            }

            // 接続直後に App Information を送信 (任意機能のため失敗しても接続は継続)
            _appInfoChar = charResult.Characteristics.FirstOrDefault(c => c.Uuid == OBC_APP_INFO_CHAR_UUID);
            await SendAppInfoAsync();

            OnConnectionStateChanged?.Invoke(true);
            OnStatusMessage?.Invoke($"[OBC] OpenBikeControl controller '{_device.Name}' connected successfully!");
            return true;
        }

        private async Task SendAppInfoAsync()
        {
            if (_appInfoChar == null) return;

            try
            {
                byte[] appInfo = OpenBikeControlMapper.BuildAppInfo(APP_ID, GetAppVersion(), _supportedButtonIds);
                var writeOption = _appInfoChar.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                    ? GattWriteOption.WriteWithoutResponse
                    : GattWriteOption.WriteWithResponse;
                var writeStatus = await _appInfoChar.WriteValueAsync(appInfo.AsBuffer(), writeOption);
                OnStatusMessage?.Invoke($"[OBC] App Information sent ({writeStatus}).");
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"[OBC] App Information write failed: {ex.Message}");
            }
        }

        private void ButtonStateChar_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);

            OnButtonStateReceived?.Invoke(data);
        }

        private static string GetAppVersion()
        {
            var version = typeof(OpenBikeControlClient).Assembly.GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
        }

        private void Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            bool connected = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
            OnStatusMessage?.Invoke($"[OBC] Connection status changed: {sender.ConnectionStatus}");
            OnConnectionStateChanged?.Invoke(connected);

            // App Information は切断ごとにデバイス側で破棄されるため、再接続時に再送信
            if (connected)
            {
                _ = SendAppInfoAsync();
            }
        }

        public void Disconnect()
        {
            StopScanning();

            if (_buttonStateChar != null)
            {
                _buttonStateChar.ValueChanged -= ButtonStateChar_ValueChanged;
                _buttonStateChar = null;
            }
            _appInfoChar = null;

            if (_device != null)
            {
                _device.ConnectionStatusChanged -= Device_ConnectionStatusChanged;
                _device.Dispose();
                _device = null;
            }
            OnConnectionStateChanged?.Invoke(false);
            OnStatusMessage?.Invoke("[OBC] Disconnected and cleaned up OpenBikeControl sessions.");
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
