using System;
using System.IO;

namespace docker_rep2_win
{
    public class ConfigSessionContext
    {
        public string LocalComposeText { get; set; } = string.Empty;

        private string? _originalLocalCompose;

        public bool LocalComposeChanged => CheckChanged(LocalComposeText, _originalLocalCompose, DefaultLocalCompose);

        public bool HasSavedLocalCompose => _originalLocalCompose != null;

        public bool IsChanged => LocalComposeChanged;

        private const string LocalComposeFile = "docker-compose.local.yml";
        public const string DefaultLocalCompose = """
            services:
              rep2php8:
                volumes:
                  # Caddy本体をプラグイン入りにしてDNS-01チャンレンジを使用する場合の例:
                  # https://github.com/fukumen/docker-rep2/blob/php8/doc/caddy.md を参考に
                  # rep2-data/win に ./caddy-local/caddy_linux_amd64_custom と Caddyfile を用意してください
                  - ./caddy-local/caddy_linux_amd64_custom:/usr/bin/caddy
                  - ./caddy-local/Caddyfile:/etc/Caddyfile
                environment:
                  # docker-rep2-win で Caddy に証明書を管理するには CADDY_USER: "root" が必要です
                  CADDY_USER: "root"

                  # Cloudflare を利用する場合
                  # CLOUDFLARE_API_TOKEN: "your_api_token_here"
            """;

        private bool CheckChanged(string current, string? original, string @default)
        {
            if (original == null) return !string.IsNullOrWhiteSpace(current) && current != @default;
            return current != original;
        }

        public void Load(string appDataPath)
        {
            string localPath = Path.Combine(appDataPath, LocalComposeFile);
            if (File.Exists(localPath))
            {
                _originalLocalCompose = File.ReadAllText(localPath);
                LocalComposeText = _originalLocalCompose;
            }
            else
            {
                _originalLocalCompose = null;
                LocalComposeText = DefaultLocalCompose;
            }
        }

        public void Save(string appDataPath)
        {
            string path = Path.Combine(appDataPath, LocalComposeFile);

            if (string.IsNullOrWhiteSpace(LocalComposeText))
            {
                if (File.Exists(path)) File.Delete(path);
            }
            else if (_originalLocalCompose == null && LocalComposeText == DefaultLocalCompose)
            {
            }
            else if (LocalComposeText != _originalLocalCompose)
            {
                File.WriteAllText(path, LocalComposeText);
            }
        }
    }
}
