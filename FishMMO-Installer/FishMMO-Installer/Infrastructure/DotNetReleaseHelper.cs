using System.Text.Json;

namespace FishMMO.Installer
{
    /// <summary>
    /// Resolves the latest ASP.NET Core Runtime download URLs and SHA512 hashes
    /// dynamically from the official .NET release metadata API. Falls back to
    /// hardcoded constants in <see cref="InstallationConstants"/> when the API
    /// is unreachable.
    /// </summary>
    public static class DotNetReleaseHelper
    {
        /// <summary>Format string for a channel's releases.json.</summary>
        private const string ChannelReleasesUrlFormat =
            "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/{0}/releases.json";

        /// <summary>
        /// Resolves the latest Windows x64 Hosting Bundle / ASP.NET Runtime installer URL
        /// and its SHA512 hash from Microsoft's official release metadata.
        /// </summary>
        /// <param name="channel">.NET channel (e.g. "8.0").</param>
        /// <returns>Tuple of (download URL, SHA512 hex hash), or (null, null) on failure.</returns>
        public static async Task<(string? url, string? sha512Hash)> ResolveAspNetRuntimeInstallerUrlAsync(string channel)
        {
            try
            {
                string releasesUrl = string.Format(ChannelReleasesUrlFormat, channel);
                string json = await DownloadHelper.Client.GetStringAsync(releasesUrl);

                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("releases", out JsonElement releases))
                    return (null, null);

                foreach (JsonElement release in releases.EnumerateArray())
                {
                    if (!release.TryGetProperty("aspnetcore-runtime", out JsonElement aspnet))
                        continue;
                    if (!aspnet.TryGetProperty("files", out JsonElement files))
                        continue;

                    foreach (JsonElement file in files.EnumerateArray())
                    {
                        string? rid = file.TryGetProperty("rid", out JsonElement ridElem)
                            ? ridElem.GetString() : null;
                        string? name = file.TryGetProperty("name", out JsonElement nameElem)
                            ? nameElem.GetString() : null;

                        if (rid == "win-x64" && name != null
                            && name.Contains("hosting", StringComparison.OrdinalIgnoreCase)
                            && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            string? url = file.TryGetProperty("url", out JsonElement urlElem)
                                ? urlElem.GetString() : null;
                            string? hash = file.TryGetProperty("hash", out JsonElement hashElem)
                                ? hashElem.GetString() : null;
                            return (url, hash);
                        }
                    }
                }

                return (null, null);
            }
            catch
            {
                return (null, null);
            }
        }

        /// <summary>
        /// Resolves the latest Linux x64 ASP.NET Core Runtime tarball URL and SHA512 hash
        /// from Microsoft's official release metadata.
        /// </summary>
        /// <param name="channel">.NET channel (e.g. "8.0").</param>
        /// <returns>Tuple of (download URL, SHA512 hex hash), or (null, null) on failure.</returns>
        public static async Task<(string? url, string? sha512Hash)> ResolveLinuxRuntimeUrlAsync(string channel)
        {
            try
            {
                string releasesUrl = string.Format(ChannelReleasesUrlFormat, channel);
                string json = await DownloadHelper.Client.GetStringAsync(releasesUrl);

                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("releases", out JsonElement releases))
                    return (null, null);

                foreach (JsonElement release in releases.EnumerateArray())
                {
                    if (!release.TryGetProperty("aspnetcore-runtime", out JsonElement aspnet))
                        continue;
                    if (!aspnet.TryGetProperty("files", out JsonElement files))
                        continue;

                    foreach (JsonElement file in files.EnumerateArray())
                    {
                        string? rid = file.TryGetProperty("rid", out JsonElement ridElem)
                            ? ridElem.GetString() : null;
                        string? name = file.TryGetProperty("name", out JsonElement nameElem)
                            ? nameElem.GetString() : null;

                        if (rid == "linux-x64" && name != null
                            && name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                        {
                            string? url = file.TryGetProperty("url", out JsonElement urlElem)
                                ? urlElem.GetString() : null;
                            string? hash = file.TryGetProperty("hash", out JsonElement hashElem)
                                ? hashElem.GetString() : null;
                            return (url, hash);
                        }
                    }
                }

                return (null, null);
            }
            catch
            {
                return (null, null);
            }
        }

        /// <summary>
        /// Resolves the latest ASP.NET Core runtime version for a given channel.
        /// </summary>
        /// <param name="channel">.NET channel (e.g. "8.0").</param>
        /// <returns>Full runtime version string, or the hardcoded fallback.</returns>
        public static async Task<string> ResolveLatestRuntimeVersionAsync(string channel)
        {
            try
            {
                string releasesUrl = string.Format(ChannelReleasesUrlFormat, channel);
                string json = await DownloadHelper.Client.GetStringAsync(releasesUrl);

                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("latest-runtime", out JsonElement latestRuntime))
                {
                    string? version = latestRuntime.GetString();
                    if (!string.IsNullOrWhiteSpace(version))
                        return version;
                }
            }
            catch
            {
                // Fall through to default
            }

            return InstallationConstants.AspNetRuntimeLinuxVersion;
        }
    }
}