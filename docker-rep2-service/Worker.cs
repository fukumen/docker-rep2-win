using System.Diagnostics;

namespace docker_rep2_service
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private const string DistroName = "docker-rep2-distro";

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("docker-rep2-service is starting.");

            try
            {
                // WSLディストリビューションを起動
                _logger.LogInformation("Starting WSL distro: {DistroName}", DistroName);
                StartWsl();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start WSL distro.");
            }

            // サービスが停止されるまで待機
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("docker-rep2-service is stopping.");

            try
            {
                // WSLディストリビューションを終了
                _logger.LogInformation("Terminating WSL distro: {DistroName}", DistroName);
                TerminateWsl();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to terminate WSL distro.");
            }

            await base.StopAsync(cancellationToken);
        }

        private void StartWsl()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = $"-d {DistroName} --exec true",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }

        private void TerminateWsl()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = $"--terminate {DistroName}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
    }
}
