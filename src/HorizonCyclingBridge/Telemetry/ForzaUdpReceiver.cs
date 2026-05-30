using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace HorizonCyclingBridge.Telemetry
{
    public class ForzaUdpReceiver
    {
        private readonly int _port;
        private UdpClient? _udpClient;
        private CancellationTokenSource? _cts;
        private Task? _receiveTask;

        /// <summary>
        /// テレメトリパケットを正常に受信しパースしたときに発生するイベント
        /// </summary>
        public event Action<ForzaDataPacket>? OnPacketReceived;

        /// <summary>
        /// 受信またはパース中にエラーが発生したときに発生するイベント
        /// </summary>
        public event Action<Exception>? OnError;

        /// <summary>
        /// 受信ループが現在実行中であるかどうか
        /// </summary>
        public bool IsRunning => _receiveTask != null && !_receiveTask.IsCompleted;

        public ForzaUdpReceiver(int port)
        {
            _port = port;
        }

        /// <summary>
        /// UDP受信の待ち受けを開始します。
        /// </summary>
        public void Start()
        {
            if (IsRunning)
            {
                Console.WriteLine("[UDP] Receiver is already running.");
                return;
            }

            _cts = new CancellationTokenSource();
            _udpClient = new UdpClient(_port);
            
            // 非同期で受信ループを開始
            _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token));
        }

        /// <summary>
        /// UDP受信の待ち受けを停止し、接続を閉じます。
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            _cts?.Cancel();
            _udpClient?.Close();
            _udpClient = null;
            _receiveTask = null;
            Console.WriteLine("[UDP] Receiver stopped.");
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            Console.WriteLine($"[UDP] Listening for Forza telemetry on port {_port}...");

            try
            {
                while (!token.IsCancellationRequested)
                {
                    // UdpReceiveResultを非同期で取得
                    var result = await _udpClient!.ReceiveAsync(token);
                    
                    if (result.Buffer.Length >= 324)
                    {
                        try
                        {
                            var packet = ForzaDataPacket.Parse(result.Buffer);
                            OnPacketReceived?.Invoke(packet);
                        }
                        catch (Exception parseEx)
                        {
                            OnError?.Invoke(new Exception("Failed to parse Forza UDP packet", parseEx));
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[UDP] Listening canceled.");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }
    }
}
