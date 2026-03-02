using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace docker_rep2_win
{
    public static class VersionProvider
    {
#if DEBUG
        private const bool FetchAllPatchVersions = true;
#else
        private const bool FetchAllPatchVersions = false;
#endif

        private const string MirrorUrl = "https://dl-cdn.alpinelinux.org/alpine/";

        public static async Task<List<VersionInfo>> FetchVersionsAsync()
        {
            var resultDict = new Dictionary<string, VersionInfo>();
            string arch = GetAlpineArch();

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            try
            {
                var json = await client.GetStringAsync(AppInfo.ManifestUrl);
                var manifest = JsonSerializer.Deserialize<VersionManifest>(json);
                if (manifest?.Versions != null)
                {
                    foreach (var v in manifest.Versions)
                    {
                        v.ManifestUrl = v.Url;
                        v.Url = GenerateUrl(v.Version, arch);
                        resultDict[v.Version] = v;
                    }
                }
            }
            catch { /* JSON取得失敗はミラー補完でカバー */ }

            try
            {
                string rootHtml = await client.GetStringAsync(MirrorUrl);
                var branchMatches = Regex.Matches(rootHtml, @"href=""(v3\.\d+)/""");
                var branches = branchMatches
                    .Select(m => m.Groups[1].Value)
                    .OrderByDescending(v => 
                    {
                        if (Version.TryParse(v.TrimStart('v'), out var ver)) return ver;
                        return new Version(0, 0);
                    })
                    .Take(3)
                    .ToList();

                foreach (var branch in branches)
                {
                    try
                    {
                        string releaseUrl = $"{MirrorUrl}{branch}/releases/{arch}/";
                        string releaseHtml = await client.GetStringAsync(releaseUrl);
                        var versionMatches = Regex.Matches(releaseHtml, $@"alpine-minirootfs-(\d+\.\d+\.\d+)-{arch}\.tar\.gz")
                            .Select(m => m.Groups[1].Value)
                            .OrderByDescending(v => v);

                        var versionsToFetch = FetchAllPatchVersions ? versionMatches : versionMatches.Take(1);

                        foreach (var versionMatch in versionsToFetch)
                        {
                            if (versionMatch != null && !resultDict.ContainsKey(versionMatch))
                            {
                                resultDict[versionMatch] = new VersionInfo
                                {
                                    Version = versionMatch,
                                    Url = $"{releaseUrl}alpine-minirootfs-{versionMatch}-{arch}.tar.gz",
                                    IsTested = false
                                };
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            var list = resultDict.Values
                .OrderByDescending(v => 
                {
                    if (Version.TryParse(v.Version, out var ver)) return ver;
                    return new Version(0, 0, 0); // パース失敗時は最下位へ
                })
                .ToList();
            return list.Count > 0 ? list : GetFallbackVersions(arch);
        }

        private static string GetAlpineArch() => RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "aarch64",
            _ => "x86_64"
        };

        private static string GenerateUrl(string version, string arch)
        {
            var parts = version.Split('.');
            string branch = parts.Length >= 2 ? $"v{parts[0]}.{parts[1]}" : "latest-stable";
            return $"{MirrorUrl}{branch}/releases/{arch}/alpine-minirootfs-{version}-{arch}.tar.gz";
        }

        private static List<VersionInfo> GetFallbackVersions(string arch)
        {
            return new List<VersionInfo>
            {
                new VersionInfo 
                { 
                    Version = "3.23.3", 
                    IsTested = true, 
                    Url = GenerateUrl("3.23.3", arch)
                }
            };
        }
    }
}
