using System.Diagnostics;

namespace docker_rep2_win
{
    public static class FirewallService
    {
        /* mirroredでは通常のWindowsのファイアーウォールではなくHyper-Vファイアーウォールでポート開放する必要がある

        VMCreatorId取得 Get-NetFirewallHyperVVMCreator
        一覧            Get-NetFirewallHyperVRule -VMCreatorId '{40E0AC32-46A5-438A-A0B2-2B479E8F2E90}' | Format-Table Name, DisplayName, LocalPorts
        追加            New-NetFirewallHyperVRule -Name "docker-rep2-win-http-in" -DisplayName "docker-rep  HTTP (TCP-In)" -Direction Inbound -VMCreatorId '{40E0AC32-46A5-438A-A0B2-2B479E8F2E90}' -Protocol TCP -LocalPorts 80
        削除            Remove-NetFirewallHyperVRule -Name "docker-rep2-win-http-in"
        */

        // ルールの一致を識別するための固有の名前
        private static readonly string RuleName = $"{AppInfo.AppShortName}-http-in";
        private static readonly string DisplayName = $"{AppInfo.AppShortName} HTTP (TCP-In)";
        private static readonly string VmId = "{40E0AC32-46A5-438A-A0B2-2B479E8F2E90}";

        // ポートの開放
        public static void OpenPort(int port)
        {
            // まず古いルールがあれば削除（二重登録防止）
            RemovePortRule();

            // PowerShellを使って受信ルールを追加
            string script = $"New-NetFirewallHyperVRule -Name '{RuleName}' -DisplayName '{DisplayName}' " +
                            $"-Direction Inbound -VMCreatorId '{VmId}' -Protocol TCP -LocalPort {port}";
            
            RunPowerShell(script);
        }

        // ポートの閉鎖（アンインストール時に使用）
        public static void RemovePortRule()
        {
            string script = $"Remove-NetFirewallHyperVRule -Name '{RuleName}' -ErrorAction SilentlyContinue";
            RunPowerShell(script);
        }

        private static void RunPowerShell(string command)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
                // StandardErrorEncoding を指定しないことで、システムデフォルト（日本語環境なら Shift-JIS）を使用する
            };

            using (var process = Process.Start(startInfo))
            {
                process?.WaitForExit();
                if (process?.ExitCode != 0)
                {
                    string error = process?.StandardError.ReadToEnd() ?? "Unknown error";
                }
            }
        }
    }
}
