using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;

namespace docker_rep2_win_setup
{
    class Program
    {
        static void Main(string[] args)
        {
            string appName = "docker-rep2-win";
            string exeName = $"{appName}.exe";

            // --extract 引数がある場合は、カレントディレクトリに展開して終了
            if (args.Contains("--extract", StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string targetDir = Path.Combine(Environment.CurrentDirectory, "extracted");
                    Directory.CreateDirectory(targetDir);
                    ExtractPayload(targetDir);
                    ShowInfoDialog($"展開が完了しました:\n{targetDir}");
                }
                catch (Exception ex)
                {
                    ShowErrorDialog($"展開中にエラーが発生しました:\n{ex.Message}");
                }
                return;
            }
            
            string tempDir = Path.Combine(Path.GetTempPath(), $"{appName}_setup_{Guid.NewGuid().ToString("N").Substring(0, 8)}");

            try
            {
                Directory.CreateDirectory(tempDir);
                ExtractPayload(tempDir);

                string targetExe = Path.Combine(tempDir, exeName);

                if (!File.Exists(targetExe))
                {
                    ShowErrorDialog($"インストーラーの展開に失敗しました。ファイルが見つかりません:\n{targetExe}");
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = targetExe,
                    Arguments = string.Join(" ", args),
                    UseShellExecute = true,
                    WorkingDirectory = tempDir
                };

                Process? process = Process.Start(startInfo);
                if (process != null)
                {
                    process.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                ShowErrorDialog($"セットアップの起動中にエラーが発生しました。\n{ex.Message}");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch { }
            }
        }

        static void ExtractPayload(string destinationPath)
        {
            string resourceName = "Payload.zip";
            
            var assembly = Assembly.GetExecutingAssembly();
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            
            if (stream == null)
            {
                throw new Exception("ペイロードが見つかりませんでした。");
            }

            using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read);
            archive.ExtractToDirectory(destinationPath, overwriteFiles: true);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        static void ShowErrorDialog(string message)
        {
            const uint MB_OK = 0x00000000;
            const uint MB_ICONERROR = 0x00000010;
            MessageBox(IntPtr.Zero, message, "Setup Error", MB_OK | MB_ICONERROR);
        }

        static void ShowInfoDialog(string message)
        {
            const uint MB_OK = 0x00000000;
            const uint MB_ICONINFORMATION = 0x00000040;
            MessageBox(IntPtr.Zero, message, "Setup Info", MB_OK | MB_ICONINFORMATION);
        }
    }
}
