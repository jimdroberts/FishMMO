using FishMMO.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FishMMO.WebShared
{
    /// <summary>
    /// Extension methods for common ASP.NET middleware used across FishMMO web servers.
    /// </summary>
    public static class MiddlewareExtensions
    {
        /// <summary>
        /// Adds security headers (HSTS, X-Content-Type-Options, Referrer-Policy,
        /// X-Frame-Options, Permissions-Policy, Cross-Origin-Resource-Policy) to
        /// every response. In non-Production environments, also adds diagnostic
        /// headers (X-Server-Version, Server-Timing).
        ///
        /// Use <paramref name="extraHeaders"/> to add server-specific headers such as
        /// Content-Security-Policy, Cross-Origin-Opener-Policy, or
        /// Cross-Origin-Embedder-Policy that are only needed by certain hosts.
        /// </summary>
        public static IApplicationBuilder UseFishMMOSecurityHeaders(
            this IApplicationBuilder app,
            IHostEnvironment environment,
            Action<IHeaderDictionary>? extraHeaders = null)
        {
            string serverVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0.0";
            bool exposeDiagnostics = !environment.IsProduction();

            app.Use(async (ctx, next) =>
            {
                var started = System.Diagnostics.Stopwatch.StartNew();
                ctx.Response.OnStarting(() =>
                {
                    var h = ctx.Response.Headers;
                    if (!h.ContainsKey("Strict-Transport-Security"))
                        h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
                    if (!h.ContainsKey("X-Content-Type-Options"))
                        h["X-Content-Type-Options"] = "nosniff";
                    if (!h.ContainsKey("Referrer-Policy"))
                        h["Referrer-Policy"] = "no-referrer";
                    if (!h.ContainsKey("X-Frame-Options"))
                        h["X-Frame-Options"] = "DENY";
                    if (!h.ContainsKey("Permissions-Policy"))
                        h["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
                    if (!h.ContainsKey("Cross-Origin-Resource-Policy"))
                        h["Cross-Origin-Resource-Policy"] = "same-origin";
                    if (exposeDiagnostics)
                    {
                        h["X-Server-Version"] = serverVersion;
                        h["Server-Timing"] = "ttfb;dur=" + started.Elapsed.TotalMilliseconds.ToString("F1");
                    }
                    extraHeaders?.Invoke(h);
                    return Task.CompletedTask;
                });
                await next();
            });

            return app;
        }

        /// <summary>
        /// Populates <see cref="ForwardedHeadersOptions.KnownProxies"/> and
        /// <see cref="ForwardedHeadersOptions.KnownNetworks"/> from configuration
        /// so the host only honours <c>X-Forwarded-*</c> headers from the local
        /// NGINX terminator. Without this, anyone reaching Kestrel directly can
        /// spoof their client IP and bypass per-IP rate limiting.
        ///
        /// Config keys (any combination):
        ///   ForwardedHeaders:KnownProxies   — array of single IPs ("127.0.0.1")
        ///   ForwardedHeaders:KnownNetworks  — array of CIDR strings ("10.0.0.0/8")
        /// If both lists are empty the defaults (loopback) are kept.
        /// </summary>
        public static void ConfigureTrustedProxies(ForwardedHeadersOptions options, IConfiguration configuration, IHostEnvironment environment)
        {
            var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
            var networks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>();

            bool changed = false;
            if (proxies != null && proxies.Length > 0)
            {
                options.KnownProxies.Clear();
                foreach (var raw in proxies)
                {
                    if (System.Net.IPAddress.TryParse(raw, out var ip))
                    {
                        options.KnownProxies.Add(ip);
                        changed = true;
                    }
                    else
                    {
                        Log.Warning("ForwardedHeaders", $"Ignoring invalid KnownProxies entry: {raw}");
                    }
                }
            }

            if (networks != null && networks.Length > 0)
            {
                options.KnownNetworks.Clear();
                foreach (var raw in networks)
                {
                    var parts = raw.Split('/');
                    if (parts.Length == 2
                        && System.Net.IPAddress.TryParse(parts[0], out var prefixIp)
                        && int.TryParse(parts[1], out var prefixLength))
                    {
                        options.KnownNetworks.Add(new IPNetwork(prefixIp, prefixLength));
                        changed = true;
                    }
                    else
                    {
                        Log.Warning("ForwardedHeaders", $"Ignoring invalid KnownNetworks entry: {raw}");
                    }
                }
            }

            if (!changed)
            {
                bool allowUnconfigured = configuration.GetValue("ForwardedHeaders:AllowUnconfigured", false);
                if (environment.IsProduction() && !allowUnconfigured)
                {
                    throw new InvalidOperationException(
                        "ForwardedHeaders:KnownProxies or ForwardedHeaders:KnownNetworks must be " +
                        "configured in Production. Set ForwardedHeaders:AllowUnconfigured=true to " +
                        "intentionally trust only loopback.");
                }

                Log.Warning("ForwardedHeaders",
                    "No trusted proxies configured. Defaults (loopback only) will be used. " +
                    "Set ForwardedHeaders:KnownProxies or ForwardedHeaders:KnownNetworks in appsettings.");
            }
        }

        /// <summary>
        /// Gets the client IP from the current HTTP context.
        /// RemoteIpAddress reflects X-Forwarded-For after UseForwardedHeaders runs.
        /// </summary>
        public static string GetClientIpKey(this HttpContext context)
        {
            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }
}