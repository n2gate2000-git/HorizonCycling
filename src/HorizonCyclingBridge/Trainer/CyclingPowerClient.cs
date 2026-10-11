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

        private static readonly Guid CSC_SERVICE_UUID = Guid.Parse("00001816-0000-1000-8000-00805f9b34fb");
        private static readonly Guid CSC_MEASUREMENT_CHAR_UUID = Guid.Parse("00002a5b-0000-1000-8000-00805f9b34fb");

        private BluetoothLEDevice? _device;
        private GattCharacteristic? _powerMeasurementChar;
        private GattCharacteristic? _cscMeasurementChar;
        private BluetoothLEAdvertisementWatcher? _watcher;
        private TaskCompletionSource<bool>? _connectTcs;
        private ulong _targetAddress = 0;

        // クランク回転数（ケイデンス計算用）トラッキング
        private ushort _lastCrankRevs = 0;
        private ushort _lastCrankTime = 0;
        private bool _hasLastCrankData = false;
        private DateTime _lastCrankEventTimestamp = DateTime.MinValue;
        private System.Threading.Timer? _cadenceZeroTimer;

        public event Action<int>? OnPowerReceived;
        public event Action<double>? OnCadenceReceived;
        public event Action<string>? OnStatusMessage;
        public event Action<bool>? OnConnectionStateChanged;

        public bool IsConnected => _device != null && _device.ConnectionStatus == BluetoothConnectionStatus.Connected;

        public CyclingPowerClient()
        {
            // 3.5秒間ペダリングが検知されない場合、ケイデンスを自動的に0にするタイマー
            _cadenceZeroTimer = new System.Threading.Timer(_ =>
            {
                if (_hasLastCrankData && _smoothedCadence > 0.0 && (DateTime.UtcNow - _lastCrankEventTimestamp).TotalSeconds > 3.5)
                {
                    _smoothedCadence = 0.0;
                    OnStatusMessage?.Invoke("[P505-CAD] Cadence zero timer fired (3.5s timeout). Setting cadence to 0.");
                    OnCadenceReceived?.Invoke(0.0);
                }
            }, null, 1000, 1000);
        }

        public async Task<bool> ScanAndConnectAsync(int timeoutMs = 20000, ulong targetAddress = 0)
        {
            if (IsConnected)
            {
                OnStatusMessage?.Invoke("[BLE] Already connected.");
                return true;
            }

            _targetAddress = targetAddress;

            // 1. targetAddress が指定されている場合、まず直接 Windows BLE デバイスオブジェクトの取得と接続を試行
            if (_targetAddress != 0)
            {
                OnStatusMessage?.Invoke($"[BLE] Attempting direct connection to Cadence/Power Meter ({_targetAddress:X})...");
                try
                {
                    var directDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(_targetAddress);
                    if (directDevice != null)
                    {
                        OnStatusMessage?.Invoke($"[BLE] Found device '{directDevice.Name}' ({_targetAddress:X}). Initializing GATT services...");
                        bool directSuccess = await SetupServicesAsync(directDevice);
                        if (directSuccess)
                        {
                            return true;
                        }
                        OnStatusMessage?.Invoke("[BLE] Direct GATT setup failed or not responding. Falling back to active advertisement watcher...");
                    }
                }
                catch (Exception ex)
                {
                    OnStatusMessage?.Invoke($"[BLE] Direct connection notice: {ex.Message}. Falling back to active scan...");
                }
            }

            // 2. 直接接続で完了しなかった場合、または targetAddress == 0 の場合、アクティブWatcherで検出
            _connectTcs = new TaskCompletionSource<bool>();

            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };
            // 重要: Magene PES-P505 等のパワーメーターはアドバタイズ本体に Service UUID を含めない場合があるため、
            // フィルタは設定せずコード側で判定する
            
            _watcher.Received += Watcher_Received;
            _watcher.Stopped += Watcher_Stopped;

            string targetStr = _targetAddress == 0 ? "any" : $"{_targetAddress:X}";
            OnStatusMessage?.Invoke($"[BLE] Active scanning for Cycling Power / Cadence Meter (Target: {targetStr}). Please spin the cranks to wake up P505...");
            _watcher.Start();

            var timeoutTask = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(_connectTcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                OnStatusMessage?.Invoke("[BLE] Scanning timed out. No Power / Cadence Meter found. Ensure sensor is awake by spinning pedals.");
                StopScanning();
                return false;
            }

            return await _connectTcs.Task;
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
                // targetAddress == 0 の場合、アドバタイズ情報からパワー/ケイデンスセンサーか判定
                bool hasPowerUuid = args.Advertisement.ServiceUuids.Any(u => u == CYCLING_POWER_SERVICE_UUID || u.ToString().StartsWith("00001818", StringComparison.OrdinalIgnoreCase));
                bool hasCscUuid = args.Advertisement.ServiceUuids.Any(u => u == CSC_SERVICE_UUID || u.ToString().StartsWith("00001816", StringComparison.OrdinalIgnoreCase));
                string localName = (args.Advertisement.LocalName ?? string.Empty).ToUpperInvariant();
                bool isPowerByName = localName.Contains("PES") || localName.Contains("P505") || localName.Contains("P325") || localName.Contains("POWER") || localName.Contains("CADENCE") || localName.Contains("MAGENE");

                if (!hasPowerUuid && !hasCscUuid && !isPowerByName)
                {
                    return;
                }
            }

            StopScanning();

            string deviceName = string.IsNullOrEmpty(args.Advertisement.LocalName) ? "Power Meter" : args.Advertisement.LocalName;
            OnStatusMessage?.Invoke($"[BLE] Found Power/Cadence Meter: '{deviceName}' Address: {args.BluetoothAddress:X}. Connecting...");

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
                    OnStatusMessage?.Invoke("[BLE] Failed to map Bluetooth device.");
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
            OnStatusMessage?.Invoke("[BLE] Cadence scanner stopped.");
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

        private GattSession? _gattSession;

        private async Task<bool> SetupServicesAsync(BluetoothLEDevice device)
        {
            _device = device;
            string devName = string.IsNullOrEmpty(_device.Name) ? $"{_device.BluetoothAddress:X}" : _device.Name;
            OnStatusMessage?.Invoke($"[BLE] Querying GATT Services from {devName}...");

            try
            {
                // アクセス権の確認
                var accessStatus = await _device.RequestAccessAsync();
                if (accessStatus != Windows.Devices.Enumeration.DeviceAccessStatus.Allowed)
                {
                    OnStatusMessage?.Invoke($"[BLE-WARN] Device access status: {accessStatus}");
                }
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"[BLE] Access check notice: {ex.Message}");
            }

            try
            {
                // GATT セッションを保持して Windows の意図しない自動切断を防止
                _gattSession = await GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId);
                if (_gattSession != null)
                {
                    _gattSession.MaintainConnection = true;
                }
            }
            catch { /* non-fatal */ }

            _device.ConnectionStatusChanged += Device_ConnectionStatusChanged;

            bool subscribedAny = false;

            // 1. Cycling Power サービス (0x1818) の探索 (Uncached)
            try
            {
                var powerServicesResult = await _device.GetGattServicesForUuidAsync(CYCLING_POWER_SERVICE_UUID, BluetoothCacheMode.Uncached);
                GattDeviceService? powerService = null;
                if (powerServicesResult.Status == GattCommunicationStatus.Success && powerServicesResult.Services.Count > 0)
                {
                    powerService = powerServicesResult.Services.FirstOrDefault();
                }
                else
                {
                    // フォールバック: 全サービス一覧から 0x1818 を検索
                    var allServices = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                    if (allServices.Status == GattCommunicationStatus.Success)
                    {
                        powerService = allServices.Services.FirstOrDefault(s => s.Uuid == CYCLING_POWER_SERVICE_UUID || s.Uuid.ToString().StartsWith("00001818", StringComparison.OrdinalIgnoreCase));
                    }
                }

                if (powerService != null)
                {
                    OnStatusMessage?.Invoke("[BLE] Found Cycling Power Service (0x1818). Requesting Characteristic (0x2A63)...");
                    var charResult = await powerService.GetCharacteristicsForUuidAsync(POWER_MEASUREMENT_CHAR_UUID, BluetoothCacheMode.Uncached);
                    GattCharacteristic? powerChar = null;
                    if (charResult.Status == GattCommunicationStatus.Success && charResult.Characteristics.Count > 0)
                    {
                        powerChar = charResult.Characteristics.FirstOrDefault();
                    }
                    else
                    {
                        var allChars = await powerService.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                        if (allChars.Status == GattCommunicationStatus.Success)
                        {
                            powerChar = allChars.Characteristics.FirstOrDefault(c => c.Uuid == POWER_MEASUREMENT_CHAR_UUID || c.Uuid.ToString().StartsWith("00002a63", StringComparison.OrdinalIgnoreCase));
                        }
                    }

                    if (powerChar != null)
                    {
                        _powerMeasurementChar = powerChar;
                        var notifyStatus = await _powerMeasurementChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.Notify);
                        if (notifyStatus == GattCommunicationStatus.Success)
                        {
                            _powerMeasurementChar.ValueChanged += PowerMeasurementChar_ValueChanged;
                            OnStatusMessage?.Invoke("[BLE] ✅ Subscribed to Cycling Power Measurement (Power + Cadence).");
                            subscribedAny = true;
                        }
                        else
                        {
                            OnStatusMessage?.Invoke($"[BLE-WARN] Failed to write Notify to Power char: {notifyStatus}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"[BLE] Cycling Power query notice: {ex.Message}");
            }

            // 2. CSC (Cycling Speed and Cadence: 0x1816) サービスの探索
            // 重要: Cycling Power (0x1818) に購読成功した場合は二重受信・競合を防ぐため CSC 購読をスキップ！
            if (!subscribedAny)
            {
                try
                {
                    OnStatusMessage?.Invoke("[BLE] Cycling Power (0x1818) not found. Probing CSC (0x1816) fallback...");
                    var cscServicesResult = await _device.GetGattServicesForUuidAsync(CSC_SERVICE_UUID, BluetoothCacheMode.Uncached);
                    GattDeviceService? cscService = null;
                    if (cscServicesResult.Status == GattCommunicationStatus.Success && cscServicesResult.Services.Count > 0)
                    {
                        cscService = cscServicesResult.Services.FirstOrDefault();
                    }
                    else
                    {
                        var allServices = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                        if (allServices.Status == GattCommunicationStatus.Success)
                        {
                            cscService = allServices.Services.FirstOrDefault(s => s.Uuid == CSC_SERVICE_UUID || s.Uuid.ToString().StartsWith("00001816", StringComparison.OrdinalIgnoreCase));
                        }
                    }

                    if (cscService != null)
                    {
                        OnStatusMessage?.Invoke("[BLE] Found CSC Service (0x1816). Requesting Characteristic (0x2A5B)...");
                        var charResult = await cscService.GetCharacteristicsForUuidAsync(CSC_MEASUREMENT_CHAR_UUID, BluetoothCacheMode.Uncached);
                        GattCharacteristic? cscChar = null;
                        if (charResult.Status == GattCommunicationStatus.Success && charResult.Characteristics.Count > 0)
                        {
                            cscChar = charResult.Characteristics.FirstOrDefault();
                        }
                        else
                        {
                            var allChars = await cscService.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                            if (allChars.Status == GattCommunicationStatus.Success)
                            {
                                cscChar = allChars.Characteristics.FirstOrDefault(c => c.Uuid == CSC_MEASUREMENT_CHAR_UUID || c.Uuid.ToString().StartsWith("00002a5b", StringComparison.OrdinalIgnoreCase));
                            }
                        }

                        if (cscChar != null)
                        {
                            _cscMeasurementChar = cscChar;
                            var notifyStatus = await _cscMeasurementChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.Notify);
                            if (notifyStatus == GattCommunicationStatus.Success)
                            {
                                _cscMeasurementChar.ValueChanged += CscMeasurementChar_ValueChanged;
                                OnStatusMessage?.Invoke("[BLE] ✅ Subscribed to CSC Measurement (Cadence).");
                                subscribedAny = true;
                            }
                            else
                            {
                                OnStatusMessage?.Invoke($"[BLE-WARN] Failed to write Notify to CSC char: {notifyStatus}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnStatusMessage?.Invoke($"[BLE] CSC query notice: {ex.Message}");
                }
            }

            if (!subscribedAny)
            {
                OnStatusMessage?.Invoke($"[BLE-ERR] Could not subscribe to Cycling Power (0x1818) or CSC (0x1816) on '{devName}'. Ensure device is awake and not connected to another app.");
                return false;
            }

            OnConnectionStateChanged?.Invoke(true);
            OnStatusMessage?.Invoke($"[BLE] 🎉 Connected to '{devName}' successfully. Cadence & Power streaming active!");
            return true;
        }

        private int _packetDebugCount = 0;
        private double _smoothedCadence = 0.0;

        private void PowerMeasurementChar_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);

            if (data.Length < 4) return;

            // Byte 0-1: Flags (UInt16)
            // Byte 2-3: Instantaneous Power (SInt16)
            ushort flags = BitConverter.ToUInt16(data, 0);
            short power = BitConverter.ToInt16(data, 2);
            OnPowerReceived?.Invoke(power);

            int offset = 4;

            // Flags Bit 0: Pedal Power Balance Present (1 byte)
            if ((flags & 0x0001) != 0) offset += 1;
            // Flags Bit 2: Accumulated Torque Present (2 bytes)
            if ((flags & 0x0004) != 0) offset += 2;
            // Flags Bit 4: Wheel Revolution Data Present (6 bytes: uint32 revs + uint16 time)
            if ((flags & 0x0010) != 0) offset += 6;

            bool hasCrank = (flags & 0x0020) != 0;

            // デバッグログ（最初の25パケットは生データをコンソールへ表示）
            if (_packetDebugCount < 25)
            {
                _packetDebugCount++;
                string hex = BitConverter.ToString(data);
                OnStatusMessage?.Invoke($"[P505-RAW #{_packetDebugCount}] len={data.Length} hex={hex} pwr={power}W flags=0x{flags:X4} hasCrank={hasCrank} off={offset}");
            }

            // Flags Bit 5: Crank Revolution Data Present (4 bytes: Cumulative Revs uint16 + Last Event Time uint16)
            if (hasCrank && data.Length >= offset + 4)
            {
                ushort crankRevs = BitConverter.ToUInt16(data, offset);
                ushort crankTime = BitConverter.ToUInt16(data, offset + 2);
                ProcessCrankRevolutionData(crankRevs, crankTime, "CP");
            }
            else if (!hasCrank && _packetDebugCount <= 25)
            {
                OnStatusMessage?.Invoke($"[P505-WARN] Packet has no Crank Revolution flag (Flags: 0x{flags:X4})");
            }
        }

        private void CscMeasurementChar_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);

            if (data.Length < 1) return;

            byte flags = data[0];
            int offset = 1;

            // Flags Bit 0: Wheel Revolution Data Present (4 bytes revs + 2 bytes time)
            if ((flags & 0x01) != 0) offset += 6;

            // Flags Bit 1: Crank Revolution Data Present (2 bytes revs + 2 bytes time)
            if ((flags & 0x02) != 0 && data.Length >= offset + 4)
            {
                ushort crankRevs = BitConverter.ToUInt16(data, offset);
                ushort crankTime = BitConverter.ToUInt16(data, offset + 2);
                ProcessCrankRevolutionData(crankRevs, crankTime, "CSC");
            }
        }

        private int _cadenceLogCount = 0;

        private void ProcessCrankRevolutionData(ushort crankRevs, ushort crankTime, string source)
        {
            DateTime now = DateTime.UtcNow;

            if (!_hasLastCrankData)
            {
                _lastCrankRevs = crankRevs;
                _lastCrankTime = crankTime;
                _hasLastCrankData = true;
                _lastCrankEventTimestamp = now;
                OnStatusMessage?.Invoke($"[CAD-INIT] Initialized crank baseline from {source}: Revs={crankRevs}, Time={crankTime}");
                return;
            }

            int deltaRevs = (crankRevs >= _lastCrankRevs)
                ? (crankRevs - _lastCrankRevs)
                : (crankRevs + 65536 - _lastCrankRevs);

            int deltaTime = (crankTime >= _lastCrankTime)
                ? (crankTime - _lastCrankTime)
                : (crankTime + 65536 - _lastCrankTime);

            // 1. 新しいクランク回転イベントが発生した場合（deltaRevs > 0 かつ deltaTime > 0）
            if (deltaRevs > 0 && deltaTime > 0)
            {
                // deltaTime は 1/1024 秒単位 -> 秒 = deltaTime / 1024.0
                // RPM = (deltaRevs / (deltaTime / 1024.0)) * 60.0
                double rawRpm = ((double)deltaRevs * 1024.0 * 60.0) / (double)deltaTime;

                // 正常なケイデンス範囲 (10〜240 RPM)
                if (rawRpm >= 10.0 && rawRpm <= 240.0)
                {
                    _lastCrankRevs = crankRevs;
                    _lastCrankTime = crankTime;
                    _lastCrankEventTimestamp = now;

                    // EMAで適度に平滑化
                    if (_smoothedCadence <= 0.0)
                    {
                        _smoothedCadence = rawRpm;
                    }
                    else
                    {
                        _smoothedCadence = (_smoothedCadence * 0.4) + (rawRpm * 0.6);
                    }

                    double finalCadence = Math.Round(_smoothedCadence, 0);

                    if (_cadenceLogCount < 20 || (_cadenceLogCount % 20 == 0))
                    {
                        _cadenceLogCount++;
                        OnStatusMessage?.Invoke($"[P505-CAD] {source} dRevs={deltaRevs} dTime={deltaTime} -> RPM={rawRpm:F1} (disp: {finalCadence})");
                    }

                    OnCadenceReceived?.Invoke(finalCadence);
                }
                else
                {
                    // 範囲外のスパイクノイズの場合は基準値のみ更新し、極端な値は破棄
                    _lastCrankRevs = crankRevs;
                    _lastCrankTime = crankTime;
                    _lastCrankEventTimestamp = now;
                }
            }
            else if (deltaRevs == 0)
            {
                // クランクがまだ1回転していない待機パケット
                // ペダリング中は deltaRevs == 0 のパケットが連続して届くのが正常なBLE仕様。
                // 3.5秒以上新しい回転イベントが来ない場合のみ停止とみなして 0.0 にリセット
                double idleSec = (now - _lastCrankEventTimestamp).TotalSeconds;
                if (idleSec > 3.5 && _smoothedCadence > 0.0)
                {
                    _smoothedCadence = 0.0;
                    OnStatusMessage?.Invoke($"[P505-CAD] Crank stopped (idle {idleSec:F1}s). Cadence reset to 0.");
                    OnCadenceReceived?.Invoke(0.0);
                }
            }
        }

        private void Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            bool isConn = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
            OnStatusMessage?.Invoke($"[BLE] Power/Cadence connection status: {sender.ConnectionStatus}");
            OnConnectionStateChanged?.Invoke(isConn);
        }

        public void Disconnect()
        {
            StopScanning();

            if (_powerMeasurementChar != null)
            {
                _powerMeasurementChar.ValueChanged -= PowerMeasurementChar_ValueChanged;
                _powerMeasurementChar = null;
            }

            if (_cscMeasurementChar != null)
            {
                _cscMeasurementChar.ValueChanged -= CscMeasurementChar_ValueChanged;
                _cscMeasurementChar = null;
            }

            if (_gattSession != null)
            {
                _gattSession.Dispose();
                _gattSession = null;
            }

            if (_device != null)
            {
                _device.ConnectionStatusChanged -= Device_ConnectionStatusChanged;
                _device.Dispose();
                _device = null;
            }

            _hasLastCrankData = false;
            OnConnectionStateChanged?.Invoke(false);
            OnStatusMessage?.Invoke("[BLE] Power/Cadence sensor disconnected.");
        }

        public void Dispose()
        {
            _cadenceZeroTimer?.Dispose();
            _cadenceZeroTimer = null;
            Disconnect();
        }
    }
}
