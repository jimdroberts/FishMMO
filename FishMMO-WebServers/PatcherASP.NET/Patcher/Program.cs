using System.Threading.RateLimiting;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Services;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using FishMMO.Logging;
using FishMMO.WebShared;

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
			// Propagate FISHMMO_ENVIRONMENT to standard ASP.NET environment variables.
			// This allows operators to set a single env var to control all servers.
			string? fishEnv = Environment.GetEnvironmentVariable("FISHMMO_ENVIRONMENT");
			if (!string.IsNullOrWhiteSpace(fishEnv))
			{
				Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", fishEnv);
				Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", fishEnv);
			}

			// NOTE: The logging configuration (logging.json) typically writes to /var/log/fishmmo/.
			// This directory MUST exist before the application starts. Deployment scripts (docker-compose,
			// systemd unit, etc.) should ensure it is created with appropriate ownership and permissions.
			// Failure to create this directory will cause Log.Initialize to fail silently or throw.
			string loggingConfigPath = Path.Combine(AppContext.BaseDirectory, "logging.json");
			await Log.Initialize(loggingConfigPath);
			await Log.Info("Program", "Starting WebServer application...");

			try
			{
				var host = CreateHostBuilder(args).Build();

				// Load gate secret from database (deployment_secrets table).
				// This is the ONLY source — no env var or .env file fallback.
				using (var scope = host.Services.CreateScope())
				{
					var svc = scope.ServiceProvider.GetRequiredService<IDeploymentSecretService>();
					var result = await svc.FetchAsync("client_gate_secret", CancellationToken.None);
					if (!result.IsSuccess || string.IsNullOrEmpty(result.Data))
						throw new InvalidOperationException(
							"Gate secret not found in deployment_secrets database table. " +
							"Run 'fishmmo-installer → Database → Configure Server Keys' to populate it.");
					var holder = scope.ServiceProvider.GetRequiredService<GateSecretHolder>();
					holder.Secret = result.Data;
					await Log.Info("Program", "Gate secret loaded from database.");

					/* Force construction of the manifest signer while we are still inside the
					 * try block. Resolving a singleton from a scope returns the root instance, so
					 * this is the same object the controllers will use. In Production its
					 * constructor throws when no signing key is configured; that lands in the
					 * catch below as a non-zero exit rather than becoming a 500 on some player's
					 * first update check. */
					var manifestSigner = scope.ServiceProvider.GetRequiredService<VersionManifestSigner>();
					if (!manifestSigner.IsConfigured)
					{
						await Log.Error("Program",
							"Version manifest signing is NOT configured. This is only permitted outside Production.");
					}
				}

				host.Run();
			}
			catch (Exception ex)
			{
				await Log.Error("Program", $"Host terminated unexpectedly: {ex.Message}");
				Environment.ExitCode = 1;
			}
			finally
			{
				await Log.Info("Program", "WebServer application shut down.");
				await Log.Shutdown();
			}
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				// NOTE: Host.CreateDefaultBuilder already loads appsettings.json and appsettings.{env}.json
				// from the content root (AppContext.BaseDirectory). The explicit AddJsonFile calls are
				// intentionally omitted to avoid unexpected config resolution when CWD differs.
				.ConfigureLogging((context, logging) =>
				{
					logging.ClearProviders();
				})
				.ConfigureWebHostDefaults(webBuilder =>
				{
					webBuilder.UseContentRoot(AppContext.BaseDirectory);
					webBuilder.ConfigureKestrel((context, options) =>
					{
							// WebServer:HttpPort accepts both string ("8090") and number (8090) formats
							// through the configuration binder. This inconsistency is intentional
							// so operators can use either format in appsettings.json.
						var httpPort = context.Configuration["WebServer:HttpPort"] ?? "8090";
						// Refuse to start on a malformed port rather than crashing inside Parse.
						if (!int.TryParse(httpPort, out int port) || port <= 0 || port > 65535)
						{
							throw new InvalidOperationException($"WebServer:HttpPort '{httpPort}' is not a valid TCP port.");
						}
						options.ListenLocalhost(port);
						// NOTE: TLS is intentionally not configured on the Kestrel listener.
						// TLS termination is handled by the upstream NGINX reverse proxy, which
						// manages certificate provisioning (Let's Encrypt via certbot) and
						// termination at the network edge. Kestrel listens on localhost only,
						// so traffic between NGINX and Kestrel never leaves the host.
						// This is the standard reverse-proxy pattern (edge TLS termination).
						// Hardening: the patcher only serves GETs (latest_version + patch download);
						// no legitimate client uploads a body. Cap at 16 KiB.
						options.Limits.MaxRequestBodySize = 16 * 1024;
						_ = Log.Info("Kestrel", $"Kestrel configured to listen on localhost on port {httpPort}.");
					})
					.ConfigureServices((context, services) =>
					{
						_ = Log.Info("Services", "Registering services...");

						ValidateNpgsqlSslMode(context.Configuration, context.HostingEnvironment);

						// Explicit factory ctor — AddSingleton<NpgsqlDbContextFactory>() is ambiguous
						// between .ctor(IConfiguration) and .ctor(NpgsqlDbConfiguration) (same class of
						// crash as IPFetch 502: host never starts, nginx upstream refused).
						services.AddSingleton(new NpgsqlDbConfiguration(context.Configuration));
						services.AddSingleton(sp =>
							new NpgsqlDbContextFactory(sp.GetRequiredService<NpgsqlDbConfiguration>()));
						services.AddSingleton<INpgsqlDbContextFactory>(sp =>
							sp.GetRequiredService<NpgsqlDbContextFactory>());
						services.AddScoped<IDeploymentSecretService, DeploymentSecretService>();

						services.AddSingleton<GateSecretHolder>();

						/* Ed25519 release key for signing the version manifest. Registered as a
						 * singleton and force-resolved in Main below, BEFORE host.Run(), so that a
						 * Production deployment with no key configured fails at STARTUP rather than
						 * lazily on the first request — see VersionManifestSigner's remarks for why
						 * "start anyway and log" was rejected. */
						services.AddSingleton<VersionManifestSigner>();
						services.AddSingleton<PatchVersionService>(sp =>
						{
							var env = sp.GetRequiredService<IHostEnvironment>();
							var config = sp.GetRequiredService<IConfiguration>();
							var holder = sp.GetRequiredService<GateSecretHolder>();
							return new PatchVersionService(env, config, holder.Secret);
						});
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
									_ = Log.Warning("CORS", "Cors:AllowedOrigins is unset; defaulting to deny. Configure explicit origins for browser clients.");
								}
							});
						});

						services.Configure<ForwardedHeadersOptions>(options =>
						{
							options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
							// Single hop only: NGINX is the *only* trusted proxy in our topology.
							options.ForwardLimit = 1;
							MiddlewareExtensions.ConfigureTrustedProxies(options, context.Configuration, context.HostingEnvironment);
						});

						services.AddRateLimiter(options =>
						{
							options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

							// Global limiter for metadata endpoints (cheap requests).
							options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
							{
								string key = httpContext.GetClientIpKey();
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
							// Sliding window prevents the fixed-window boundary burst
							// (12 requests in 2 s across a window boundary).
							options.AddPolicy(PatchDownloadPolicy, httpContext =>
							{
								string key = httpContext.GetClientIpKey();
								return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
								{
									PermitLimit = 6,
									Window = TimeSpan.FromMinutes(1),
									SegmentsPerWindow = 6,
									QueueLimit = 0,
									QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
								});
							});

							options.OnRejected = async (context, token) =>
							{
								await Log.Warning("RateLimiter", $"Rejected {context.HttpContext.GetClientIpKey()} for {context.HttpContext.Request.Path}");
								context.HttpContext.Response.ContentType = "text/plain";
								// Use the metadata from the rate limit lease to determine the actual re-try after time.
								// This correctly reflects whichever policy (global token bucket with ~1s,
								// or PatchDownload sliding window with ~10s) actually triggered the rejection.
								if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
								{
									context.HttpContext.Response.Headers["Retry-After"] = ((int)retryAfter.TotalSeconds).ToString();
								}
								else
								{
									// Fallback to the most restrictive policy
									// (PatchDownload sliding window: 6/60s = 10s per permit).
									context.HttpContext.Response.Headers["Retry-After"] = "10";
								}
								await context.HttpContext.Response.WriteAsync("Too many requests.", token);
							};
						});

						_ = Log.Info("Services", "All services registered.");
					})
					.Configure((context, app) =>
					{
						app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
						{
							ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
					var exFeature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
					if (exFeature?.Error != null)
					{
						_ = Log.Error("Patcher", "Unhandled exception in Patcher request pipeline.", exFeature.Error);
					}
							ctx.Response.ContentType = "text/plain";
							await ctx.Response.WriteAsync("Internal Server Error");
						}));

						app.UseForwardedHeaders();

						// After UseForwardedHeaders has resolved the real client IP, reject any
						// request still missing a RemoteIpAddress. This only happens when the proxy
						// chain is misconfigured or the request bypassed NGINX entirely; both cases
						// would silently coalesce into a single "unknown" rate-limit bucket and let
						// an attacker escape per-IP throttling.
						app.Use(async (ctx, next) =>
						{
							if (ctx.Connection.RemoteIpAddress == null)
							{
								ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
								await Log.Warning("Program", $"Request rejected: unresolved client IP for {ctx.Request.Path}");
								return;
							}
							await next();
						});

						app.UseFishMMOSecurityHeaders(context.HostingEnvironment);
						// Client gate runs BEFORE the rate limiter so forged requests
						// from generic crawlers don't consume per-IP tokens. Loopback
						// /healthz is exempted so monitoring works without the secret.
						var gateSecret = app.ApplicationServices.GetRequiredService<GateSecretHolder>().Secret;
						app.UseFishMMOClientGate(context.HostingEnvironment, gateSecret, "/healthz");
						app.UseCors("Public");
						app.UseRateLimiter();
						app.UseRouting();
						app.UseEndpoints(endpoints =>
						{
							endpoints.MapControllers();

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





		private static void ValidateNpgsqlSslMode(IConfiguration configuration, IHostEnvironment environment)
		{
			if (!environment.IsProduction()) return;

			bool allowInsecure = configuration.GetValue("ConnectionStrings:AllowInsecureNpgsql", false);
			if (allowInsecure) return;

			string? connectionString = configuration.GetConnectionString("NpgsqlConnection");
			if (string.IsNullOrWhiteSpace(connectionString))
				return; // Other startup code will fail with clearer error

			try
			{
				var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
				if (builder.SslMode != Npgsql.SslMode.Require)
				{
					throw new InvalidOperationException(
						"In Production, the Npgsql ConnectionStrings:NpgsqlConnection must use " +
						"'Ssl Mode=Require'. The default 'Prefer' silently " +
						"falls back to plaintext, exposing credentials. Set " +
						"ConnectionStrings:AllowInsecureNpgsql=true to deliberately allow an " +
						"unencrypted connection (e.g., loopback Unix socket). NOTE: Upgrade to Npgsql 6.0+ for VerifyCA and VerifyFull modes.");
				}
			}
			catch (InvalidOperationException) { throw; }
			catch (Exception ex)
			{
				throw new InvalidOperationException(
					$"Failed to parse Npgsql connection string for SSL validation: {ex.Message}", ex);
			}
		}

	}

	/// <summary>
	/// Holds the gate secret loaded from the database at startup.
	/// Registered as a singleton so the secret can be populated after the host is built
	/// but before it starts running, and consumed by middlewares configured during build.
	/// </summary>
	internal sealed class GateSecretHolder
	{
		public string? Secret { get; set; }
	}
}
