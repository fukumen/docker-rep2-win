using System.Net.Sockets;

namespace docker_rep2_win
{
    /// <summary>
    /// WSLの実行状態を監視する
    /// </summary>
    public class StatusMonitor : IDisposable
    {
        private int _port;
        private bool _isRunning;
        private CancellationTokenSource? _cts;
        private Task? _monitorTask;

        public event EventHandler<bool>? StatusChanged;

        public int Port
        {
            get => _port;
            set => _port = value;
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    StatusChanged?.Invoke(this, _isRunning);
                }
            }
        }

        public StatusMonitor(int port)
        {
            _port = port;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _monitorTask = RunLoop(_cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
        }

        private async Task RunLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                bool currentStatus = false;
                try
                {
                    using var client = new TcpClient();
                    var connectTask = client.ConnectAsync("127.0.0.1", _port, token).AsTask();
                    await Task.WhenAny(connectTask, Task.Delay(500, token));
                    
                    if (connectTask.IsCompletedSuccessfully && client.Connected)
                    {
                        currentStatus = true;
                    }
                }
                catch
                {
                    // 接続失敗時は停止中とみなす
                }

                IsRunning = currentStatus;

                try
                {
                    await Task.Delay(3000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _monitorTask?.Dispose();
        }
    }
}
