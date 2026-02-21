using System;
using System.Threading;
using System.Threading.Tasks;

namespace docker_rep2_win
{
    public static class DockerService
    {
        public static async Task StartDockerdAsync(CancellationToken cancellationToken = default)
        {
            int retryCount = 0;
            string lastError = string.Empty;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // docker info が成功すれば起動完了とみなす
                    // デーモンが応答しない場合に備えて 5 秒のタイムアウトを設定
                    await WslService.RunCommandAsync("docker info", 5000, false, cancellationToken);
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    retryCount++;
                    if (retryCount > 60) 
                    {
                        // WslService の public メソッド経由でログを取得
                        string logContent = await WslService.GetDockerdLogAsync(cancellationToken);

                        var errorMsg = $"Dockerデーモンの起動待機がタイムアウトしました。\n詳細: {lastError}\n\n--- dockerd.log ---\n{logContent}";
                        Logger.Log(ex, "Docker daemon startup timeout");
                        throw new Exception(errorMsg);
                    }
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        public static async Task<WslService.WslResult> PullAsync(CancellationToken cancellationToken = default)
        {
            string wslPath = WslService.GetAppDataWslPath() ?? throw new Exception("設定ファイルの保存先が見つかりません。");
            return await WslService.RunCommandAsync($"cd \"{wslPath}\" && docker compose pull", -1, false, cancellationToken);
        }

        public static async Task UpAsync(CancellationToken cancellationToken = default)
        {
            string wslPath = WslService.GetAppDataWslPath() ?? throw new Exception("設定ファイルの保存先が見つかりません。");
            await WslService.RunCommandAsync($"cd \"{wslPath}\" && docker compose up -d", -1, false, cancellationToken);
        }

        public static async Task DownAsync(int timeoutMs = 30000, CancellationToken cancellationToken = default)
        {
            string wslPath = WslService.GetAppDataWslPath() ?? throw new Exception("設定ファイルの保存先が見つかりません。");
            await WslService.RunCommandAsync($"cd \"{wslPath}\" && docker compose down", timeoutMs, false, cancellationToken);
        }

        public static async Task PruneAsync(CancellationToken cancellationToken = default)
        {
            await WslService.RunCommandAsync("docker image prune -f", -1, false, cancellationToken);
        }
    }
}
