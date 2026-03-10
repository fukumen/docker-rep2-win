using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace docker_rep2_win
{
    public class CertificateInfo
    {
        public string Type { get; set; } = string.Empty; // "Certbot", "Caddy", "None"
        public DateTime? ExpirationDate { get; set; }
        public int? RemainingDays => ExpirationDate.HasValue ? (ExpirationDate.Value - DateTime.Now).Days : null;
    }

    public static class CertificateService
    {
        public static CertificateInfo GetCertificateInfo(AppSettings settings)
        {
            var info = new CertificateInfo { Type = "None" };
            string dataPath = settings.DataPath;
            string appDataPath = settings.WindowsDataPath;
            if (string.IsNullOrEmpty(dataPath) || string.IsNullOrEmpty(appDataPath)) return info;

            // Certbot
            if (settings.User.EnableCertbot)
            {
                string certbotArchiveDir = Path.Combine(appDataPath, "certbot", "conf", "archive");
                if (Directory.Exists(certbotArchiveDir))
                {
                    try
                    {
                        var certFiles = Directory.GetFiles(certbotArchiveDir, "cert*.pem", SearchOption.AllDirectories);
                        
                        // 各ドメイン(ディレクトリ)ごとに、更新日時が最も新しいファイルのみを対象にする
                        var latestFiles = certFiles
                            .GroupBy(f => Path.GetDirectoryName(f))
                            .Select(g => g.OrderByDescending(f => File.GetLastWriteTime(f)).First());

                        DateTime? latestExpiration = null;

                        foreach (var file in latestFiles)
                        {
                            try
                            {
                                var cert = X509CertificateLoader.LoadCertificateFromFile(file);
                                if (latestExpiration == null || cert.NotAfter > latestExpiration)
                                {
                                    latestExpiration = cert.NotAfter;
                                }
                            }
                            catch
                            {
                            }
                        }

                        if (latestExpiration.HasValue)
                        {
                            info.Type = "Certbot";
                            info.ExpirationDate = latestExpiration;
                            return info;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            // Caddy
            if (settings.User.IsCaddyfileMounted)
            {
                string caddyCertsDir = Path.Combine(dataPath, "caddy_data", "caddy", "certificates");
                if (Directory.Exists(caddyCertsDir))
                {
                    try
                    {
                        var crtFiles = Directory.GetFiles(caddyCertsDir, "*.crt", SearchOption.AllDirectories);
                        DateTime? latestExpiration = null;

                        foreach (var file in crtFiles)
                        {
                            try
                            {
                                var cert = X509CertificateLoader.LoadCertificateFromFile(file);
                                if (latestExpiration == null || cert.NotAfter > latestExpiration)
                                {
                                    latestExpiration = cert.NotAfter;
                                }
                            }
                            catch
                            {
                            }
                        }

                        if (latestExpiration.HasValue)
                        {
                            info.Type = "Caddy";
                            info.ExpirationDate = latestExpiration;
                            return info;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return info;
        }
    }
}
