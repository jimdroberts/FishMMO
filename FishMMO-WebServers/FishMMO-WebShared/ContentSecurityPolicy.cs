using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace FishMMO.WebShared;

public enum ContentSecurityPolicyProfile { Api, ControlPanel, WebGl }

/// <summary>One policy per response, owned by the application rather than the reverse proxy.</summary>
public sealed class ContentSecurityPolicy
{
    private static readonly Regex Scripts = new(
        @"<script\b(?<attributes>[^>]*)>(?<code>[\s\S]*?)</script\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ExternalScript = new(
        @"(?:^|\s)src\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private readonly ContentSecurityPolicyProfile profile;
    private readonly string connections;

    public ContentSecurityPolicy(ContentSecurityPolicyProfile profile,
        IConfiguration? configuration, IHostEnvironment environment)
    {
        this.profile = profile;
        var sources = new SortedSet<string>(StringComparer.Ordinal) { "'self'" };
        if (profile == ContentSecurityPolicyProfile.WebGl)
        {
            string gameHost = configuration?["GameHost"] ??
                (environment.IsDevelopment() ? "127.0.0.1" : "game.fishmmo.com");
            if (Uri.CheckHostName(gameHost) == UriHostNameType.Unknown)
                throw new InvalidOperationException("GameHost must be a DNS name or IP address, without a scheme, path or port.");
            string authority = gameHost.Contains(':') ? "[" + gameHost.Trim('[', ']') + "]" : gameHost;
            // WebTransport uses HTTPS, with dynamically allocated game-server ports.
            sources.Add("https://" + authority + ":*");
            var configured = configuration?.GetSection("ContentSecurityPolicy:ConnectOrigins")
                .Get<string[]>();
            foreach (string raw in configured ?? new[] { "https://api.fishmmo.com" })
            {
                if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != "https" && !(environment.IsDevelopment() && uri.Scheme == "http" && uri.IsLoopback)) ||
                    uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" ||
                    uri.Query.Length != 0 || uri.Fragment.Length != 0 || raw.Any(char.IsWhiteSpace) ||
                    raw.Contains(';') || raw.Contains('\''))
                    throw new InvalidOperationException("ContentSecurityPolicy:ConnectOrigins must contain HTTPS origins (HTTP loopback is allowed in Development).");
                sources.Add(uri.GetLeftPart(UriPartial.Authority));
            }
        }
        connections = string.Join(" ", sources);
    }

    public string GetHeader(HttpContext context, IFileProvider? webRoot = null)
    {
        if (profile == ContentSecurityPolicyProfile.Api)
            return "default-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

        string scripts = "'self'";
        if (profile == ContentSecurityPolicyProfile.WebGl)
        {
            // Unity's decompression fallback creates blob scripts and workers.
            scripts += " 'wasm-unsafe-eval' blob:";
            if (webRoot != null && context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
                foreach (string hash in GetInlineScriptHashes(context.Request.Path, webRoot))
                    scripts += " 'sha256-" + hash + "'";
        }
        string assets = profile == ContentSecurityPolicyProfile.WebGl ? " blob:" : "";
        return "default-src 'self'; script-src " + scripts + "; connect-src " + connections +
            "; style-src 'self' 'unsafe-inline'; img-src 'self' data:" + assets +
            "; font-src 'self' data:; media-src 'self'" + assets +
            "; worker-src 'self'" + assets + "; manifest-src 'self'; object-src 'none'; base-uri 'self'; " +
            "frame-ancestors 'none'; form-action " + (profile == ContentSecurityPolicyProfile.WebGl ? "'none'" : "'self'");
    }

    private static IEnumerable<string> GetInlineScriptHashes(PathString requestPath, IFileProvider webRoot)
    {
        string path = requestPath.Value ?? "/";
        if (path.EndsWith('/'))
        {
            foreach (string name in new[] { "default.htm", "default.html", "index.htm", "index.html" })
                if (webRoot.GetFileInfo(path + name).Exists) { path += name; break; }
        }
        if (!path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)) yield break;
        var file = webRoot.GetFileInfo(path);
        if (!file.Exists || file.IsDirectory) yield break;
        using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        // HTML parsing normalizes line endings before the browser computes CSP hashes.
        string html = reader.ReadToEnd().Replace("\r\n", "\n").Replace('\r', '\n');
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match script in Scripts.Matches(html))
        {
            if (ExternalScript.IsMatch(script.Groups["attributes"].Value)) continue;
            string hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script.Groups["code"].Value)));
            if (seen.Add(hash)) yield return hash;
        }
    }
}
