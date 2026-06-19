using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using FishMMO.Logging;

namespace FishMMO.WebServer
{
	/// <summary>
	/// Host program for the FishMMO Patcher web server. Configures Kestrel,
	/// CORS, per-IP rate limiting, and the HTTP pipeline.
	/// </summary>
	public class Program
	{
		// Rate-limit policy name applied specifically to patch downloads.
		public const string PatchDownloadPolicy = "PatchDownload";

		public static async Task Main(string[] args)
		{
			string loggingConfigPath = Path.Combine(AppContext.BaseDirectory, "logging.json");
			await Log.Initialize(loggingConfigPath);
			await Log.Info("Program", "Starting WebServer application...");

			try
			{
				CreateHostBuilder(args).Build().Run();
			}
			catch (Exception ex)
			{
				await Log.Error("Program", $"Host terminated unexpectedly: {ex.Message}");
			}
			finally
			{
				await Log.Info("Program", "WebServer application shut down.");
				await Log.Shutdown();
			}
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureAppConfiguration((hostingContext, config) =>
				{
					string env = hostingContext.HostingEnvironment.EnvironmentName;
					config.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"), optional: true, reloadOnChange: true);
					config.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), $"appsettings.{env}.json"), optional: true, reloadOnChange: true);
				})
				.ConfigureLogging((context, logging) =>
				{
					logging.ClearProviders();
				})
				.ConfigureWebHostDefaults(webBuilder =>
				{
					webBuilder.UseContentRoot(AppContext.BaseDirectory);
					webBuilder.ConfigureKestrel((context, options) =>
					{
						var httpPort = context.Configuration["WebServer:HttpPort"] ?? "8090";
						// Refuse to start on a malformed port rather than crashing inside Parse.
						if (!int.TryParse(httpPort, out int port) || port <= 0 || port > 65535)
						{
							throw new InvalidOperationException($"WebServer:HttpPort '{httpPort}' is not a valid TCP port.");
						}
						options.ListenLocalhost(port);
						// Hardening: the patcher only serves GETs (latest_version + patch download);
						// no legitimate client uploads a body. Cap at 16 KiB.
						options.Limits.MaxRequestBodySize = 16 * 1024;
						Log.Info("Kestrel", $"Kestrel configured to listen on localhost on port {httpPort}.");
					})
					.ConfigureServices((context, services) =>
					{
						Log.Info("Services", "Registering services...");

						services.AddSingleton<PatchVersionService>();
						services.AddControllers();

						// CORS — defaults to deny (no Access-Control-Allow-Origin emitted) when
						// Cors:AllowedOrigins is unset. The patch endpoints are consumed by the
						// native launcher (UnityWebRequest, CORS-exempt); browsers should not be
						// scripting this API unless an operator explicitly opts in.
						services.AddCors(options =>
						{
							options.AddPolicy("Public", builder =>
							{
								var allowedOrigins = context.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
								if (allowedOrigins != null && allowedOrigins.Length > 0)
								{
									builder.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
								}
								else
								{
									builder.WithOrigins(System.Array.Empty<string>()).AllowAnyMethod().AllowAnyHeader();
									Log.Warning("CORS", "Cors:AllowedOrigins is unset; defaulting to deny. Configure explicit origins for browser clients.");
								}
							});
						});

						services.Configure<ForwardedHeadersOptions>(options =>
						{
							options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
							// Single hop only: NGINX is the *only* trusted proxy in our topology.
							options.ForwardLimit = 1;
							ConfigureTrustedProxies(options, context.Configuration, context.HostingEnvironment);
						});

						services.AddRateLimiter(options =>
						{
							options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

							// Global limiter for metadata endpoints (cheap requests).
							options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
							{
								string key = GetClientIpKey(httpContext);
								return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
								{
									TokenLimit = 30,
									TokensPerPeriod = 10,
									ReplenishmentPeriod = TimeSpan.FromSeconds(1),
									QueueLimit = 0,
									AutoReplenishment = true,
									QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
								});
							});

							// Dedicated stricter policy for patch binary downloads.
							options.AddPolicy(PatchDownloadPolicy, httpContext =>
							{
								string key = GetClientIpKey(httpContext);
								return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
								{
									PermitLimit = 6,
									Window = TimeSpan.FromMinutes(1),
									QueueLimit = 0,
									AutoReplenishment = true,
									QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
								});
							});

							options.OnRejected = async (context, token) =>
							{
								await Log.Warning("RateLimiter", $"Rejected {GetClientIpKey(context.HttpContext)} for {context.HttpContext.Request.Path}");
								context.HttpContext.Response.Headers["Retry-After"] = "1";
								await context.HttpContext.Response.WriteAsync("Too many requests.", token);
							};
						});

						Log.Info("Services", "All services registered.");
					})
					.Configure((context, app) =>
					{
						app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
						{
							ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
							ctx.Response.ContentType = "text/plain";
							await ctx.Response.WriteAsync("Internal Server Error");
						}));

						app.UseForwardedHeaders();
						UseSecurityHeaders(app, context.HostingEnvironment);
						// Client gate runs BEFORE the rate limiter so forged requests
						// from generic crawlers don't consume per-IP tokens. Loopback
						// /healthz is exempted so monitoring works without the secret.
						app.UseClientGate(context.HostingEnvironment, "/healthz");
						app.UseCors("Public");
						app.UseRateLimiter();
						app.UseRouting();
						app.UseEndpoints(endpoints =>
						{
							endpoints.MapControllers();

							// Patch file downloads are explicitly bound to the stricter download policy.
							endpoints.MapControllerRoute(
								name: "patch-download",
								pattern: "{version}",
								defaults: new { controller = "Patch", action = "GetPatch" })
								.RequireRateLimiting(PatchDownloadPolicy);

							endpoints.MapGet("/healthz", async ctx =>
							{
								var versionService = ctx.RequestServices.GetService(typeof(PatchVersionService)) as PatchVersionService;
								bool ready = versionService != null && !string.IsNullOrEmpty(versionService.LatestVersion);
								ctx.Response.ContentType = "application/json";
								ctx.Response.StatusCode = ready ? 200 : 503;
								// Loopback callers (NGINX + monitoring) get detail; external callers see
								// only status. We do NOT want to advertise latest_version on the public
								// healthz — that's a separate, intentionally-cacheable endpoint.
								bool isLoopback = ctx.Connection.RemoteIpAddress != null && System.Net.IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress);
								if (isLoopback)
								{
									await ctx.Response.WriteAsync(
										"{\"status\":\"" + (ready ? "ok" : "degraded") +
										"\",\"latest_version\":\"" + (versionService?.LatestVersion ?? "") +
										"\",\"time\":\"" + DateTime.UtcNow.ToString("o") + "\"}");
								}
								else
								{
									await ctx.Response.WriteAsync("{\"status\":\"" + (ready ? "ok" : "degraded") + "\"}");
								}
							});
						});
					});
				});

		/// <summary>
		/// Security-headers middleware. See IpFetch Program for rationale; intentionally
		/// duplicated rather than introducing a new shared library for the three small hosts.
		/// </summary>
		private static void UseSecurityHeaders(IApplicationBuilder app, IHostEnvironment environment)
		{
			string serverVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
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
						h["Server-Timing"] = "total;dur=" + started.Elapsed.TotalMilliseconds.ToString("F1");
					}
					return Task.CompletedTask;
				});
				await next();
			});
		}

		/// <summary>
		/// Populates <see cref="ForwardedHeadersOptions.KnownProxies"/> and
		/// <see cref="ForwardedHeadersOptions.KnownNetworks"/> from configuration
		/// so the host only honours <c>X-Forwarded-*</c> headers from the local
		/// NGINX terminator. Without this, anyone reaching Kestrel directly can
		/// spoof their client IP and bypass per-IP rate limiting.
		/// </summary>
		private static void ConfigureTrustedProxies(ForwardedHeadersOptions options, IConfiguration configuration, IHostEnvironment environment)
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
				// In Production we refuse to start with the default loopback-only
				// trust list: any host that can reach Kestrel directly could otherwise
				// spoof X-Forwarded-For and trivially bypass the per-IP rate limiter.
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

		private static string GetClientIpKey(HttpContext context)
		{
			return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		}
	}
}
