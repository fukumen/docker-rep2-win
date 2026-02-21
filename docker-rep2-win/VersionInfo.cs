using System.Text.Json.Serialization;

namespace docker_rep2_win
{
    public class VersionInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("hash")]
        public string Hash { get; set; } = string.Empty;

        [JsonPropertyName("is_tested")]
        public bool IsTested { get; set; } = false;

        [JsonIgnore]
        public bool IsCurrent { get; set; } = false;

        public override string ToString()
        {
            if (IsCurrent && IsTested) return $"{Version} (現在 / 確認済)";
            if (IsCurrent) return $"{Version} (現在のバージョン)";
            if (IsTested) return $"{Version} (確認済み)";
            return Version;
        }
    }

    public class VersionManifest
    {
        [JsonPropertyName("versions")]
        public List<VersionInfo> Versions { get; set; } = new();
    }
}
