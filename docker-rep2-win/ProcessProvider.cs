using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace docker_rep2_win
{
    public static class ProcessProvider
    {
        /// <summary>
        /// 管理者権限（昇格状態）から、現在ログインしているユーザーの一般権限（非昇格）でプロセスを起動します。
        /// </summary>
        public static unsafe void StartNonElevated(string exePath, string arguments)
        {
            if (!OperatingSystem.IsWindows()) return;

            HWND hShellWindow = PInvoke.GetShellWindow();
            if (hShellWindow == HWND.Null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath, arguments) { UseShellExecute = true });
                return;
            }

            uint shellProcessId;
            PInvoke.GetWindowThreadProcessId(hShellWindow, &shellProcessId);

            HANDLE hShellProcess = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION, false, shellProcessId);
            if (hShellProcess == HANDLE.Null) return;

            try
            {
                HANDLE hToken = default;
                if (!PInvoke.OpenProcessToken(hShellProcess, TOKEN_ACCESS_MASK.TOKEN_DUPLICATE, &hToken)) return;

                try
                {
                    HANDLE hNewToken = default;
                    if (!PInvoke.DuplicateTokenEx(hToken, TOKEN_ACCESS_MASK.TOKEN_ALL_ACCESS, null, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, &hNewToken)) return;

                    try
                    {
                        STARTUPINFOW si = default;
                        si.cb = (uint)sizeof(STARTUPINFOW);
                        PROCESS_INFORMATION pi = default;

                        string fullCommand = $"\"{exePath}\" {arguments}";
                        // 生の Win32 API オーバーロードを使用するため、PWSTR (char*) で渡す
                        fixed (char* pCommand = fullCommand)
                        {
                            if (PInvoke.CreateProcessWithToken(
                                hNewToken,
                                CREATE_PROCESS_LOGON_FLAGS.LOGON_WITH_PROFILE,
                                null,
                                new PWSTR(pCommand),
                                default,
                                null,
                                null,
                                &si,
                                &pi))
                            {
                                PInvoke.CloseHandle(pi.hProcess);
                                PInvoke.CloseHandle(pi.hThread);
                            }
                        }
                    }
                    finally
                    {
                        if (hNewToken != HANDLE.Null) PInvoke.CloseHandle(hNewToken);
                    }
                }
                finally
                {
                    if (hToken != HANDLE.Null) PInvoke.CloseHandle(hToken);
                }
            }
            finally
            {
                if (hShellProcess != HANDLE.Null) PInvoke.CloseHandle(hShellProcess);
            }
        }
    }
}
