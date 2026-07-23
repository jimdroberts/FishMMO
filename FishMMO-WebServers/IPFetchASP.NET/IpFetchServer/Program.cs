using System.Threading.RateLimiting;
using FishMMO.Database.Npgsql;
using FishMMO.WebShared;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using FishMMO.Logging;

namespace FishMMO.WebServer
{
	/// <summary>
	/// Host entry point for the FishMMO IP-fetch web server.
	/// Responsible for initializing logging, propagating environment variables
	/// and building/running the ASP.NET host.
	/// </summary>
	public class Program
	{
		public static async Task Main(string[] args)
		{
			// Force the .NET Host to recognize FISHMMO_ENVIRONMENT
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
				CreateHostBuilder(args).Build().Run();
			}
			catch (Exception ex)
			{
				await Log.Error("Program", $"Host terminated unexpectedly: {ex.Message}");
				Environment.ExitCode = 1;
			}
			finally
			{
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
					// Bundled defaults from FishMMO-Setup (copied to output directory).
					webBuilder.UseContentRoot(AppContext.BaseDirectory);
					webBuilder.ConfigureKestrel((context, options) =>
					{
						// Parse port as a string to distinguish "missing" from "invalid".
						// GetValue<int?>() silently returns 0 for malformed (non-null) values,
						// which would bind Kestrel to a random OS port instead of the fallback.
						int port = 8080;
						string? portSetting = context.Configuration["WebServer:HttpPort"];
						if (!string.IsNullOrEmpty(portSetting))
						{
							if (!int.TryParse(portSetting, out port) || port < 1 || port > 65535)
							{
								_ = Log.Warning("Kestrel", $"WebServer:HttpPort '{portSetting}' is invalid; falling back to port 8080.");
								port = 8080;
							}
						}
						// WebServer:HttpPort accepts both string ("8080") and number (8080) formats
						// through the configuration binder. This inconsistency is intentional
						// so operators can use either format in appsettings.json.
						options.ListenLocalhost(port);
						// Hardening: cap request body for this metadata-only endpoint.
						// /loginserver is GET-only; legitimate clients never upload anything.
						options.Limits.MaxRequestBodySize = 16 * 1024; // 16 KiB.
						_ = Log.Info("Kestrel", $"Kestrel listening on localhost port {port} in {context.HostingEnvironment.EnvironmentName} mode.");
					})
					.ConfigureServices((context, services) =>
					{
						_ = Log.Info("Services", "Registering services...");

						ValidateNpgsqlSslMode(context.Configuration, context.HostingEnvironment);

						services.AddSingleton(new NpgsqlDbConfiguration(context.Configuration));
						services.AddSingleton<NpgsqlDbContextFactory>();
						services.AddMemoryCache();

						services.AddControllers()
							.AddJsonOptions(options =>
							{
								// Preserve PascalCase property names so Unity's JsonUtility (which
								// requires exact-name matching) can deserialize responses without
								// post-hoc string rewriting on the client.
								options.JsonSerializerOptions.PropertyNamingPolicy = null;
								options.JsonSerializerOptions.DictionaryKeyPolicy = null;
							});

						// CORS — defaults to deny (no Access-Control-Allow-Origin emitted) when
						// Cors:AllowedOrigins is unset. Native UnityWebRequest clients ignore
						// CORS entirely, so the only callers affected are browsers; the WebGL
						// build is loaded same-origin and likewise unaffected. Operators that
						// genuinely need cross-origin access must list the origins explicitly.
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
									// Empty origin list → CORS middleware will not match any
									// preflight, effectively denying cross-origin requests.
									builder.WithOrigins(System.Array.Empty<string>()).AllowAnyMethod().AllowAnyHeader();
									_ = Log.Warning("CORS", "Cors:AllowedOrigins is unset; defaulting to deny. Configure explicit origins for browser clients.");
								}
							});
						});

						services.Configure<ForwardedHeadersOptions>(options =>
						{
							options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
							// Single hop only: NGINX is the *only* trusted proxy in our topology. Any
							// additional X-Forwarded-For values are attacker-controlled and would shift
							// RemoteIpAddress further left, breaking per-IP rate limiting.
							options.ForwardLimit = 1;
							MiddlewareExtensions.ConfigureTrustedProxies(options, context.Configuration, context.HostingEnvironment);
						});

						// Per-IP rate limiting (token bucket). NGINX should layer additional global throttling.
						services.AddRateLimiter(options =>
						{
							options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
							options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
							{
								string partitionKey = httpContext.GetClientIpKey();
								return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
								{
									TokenLimit = 30,
									TokensPerPeriod = 10,
									ReplenishmentPeriod = TimeSpan.FromSeconds(1),
									QueueLimit = 0,
									AutoReplenishment = true,
									QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
								});
							});
							options.OnRejected = async (context, token) =>
							{
								await Log.Warning("RateLimiter", $"Rejected {context.HttpContext.GetClientIpKey()} for {context.HttpContext.Request.Path}");
								context.HttpContext.Response.Headers["Retry-After"] = "1";
								await context.HttpContext.Response.WriteAsync("Too many requests.", token);
							};
						});

						_ = Log.Info("Services", "All services registered.");
					})
					.Configure((context, app) =>
					{
						// A non-cleared exception handler must run before any other middleware
						// so unhandled framework exceptions surface as opaque 500s instead of
						// leaking stack traces in the dev default page.
						app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
						{
							ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
							ctx.Response.ContentType = "text/plain";
							await ctx.Response.WriteAsync("Internal Server Error");
						}));

						app.UseForwardedHeaders();

						// After UseForwardedHeaders has resolved the real client IP, reject any
						// request still missing a RemoteIpAddress. This only happens when the proxy
						// chain is misconfigured or the request bypassed NGINX entirely; both cases
						// would silently coalesce into a single “unknown” rate-limit bucket and let
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
						app.UseFishMMOClientGate(context.HostingEnvironment, "/healthz");
						app.UseCors("Public");
						app.UseRateLimiter();
						app.UseRouting();
						app.UseAuthorization();
						app.UseEndpoints(endpoints =>
						{
							endpoints.MapControllers();
							endpoints.MapGet("/healthz", async ctx =>
							{
								bool db = false;
								try
								{
									var factory = ctx.RequestServices.GetService(typeof(NpgsqlDbContextFactory)) as NpgsqlDbContextFactory;
									if (factory != null)
									{
										using var c = factory.CreateDbContext();
										db = await c.Database.CanConnectAsync(ctx.RequestAborted);
									}
								}
								catch { db = false; }
								ctx.Response.ContentType = "application/json";
								ctx.Response.StatusCode = db ? 200 : 503;
								// Only loopback (NGINX/monitoring on the same host) gets detailed health.
								// Public callers see status-only so we don't leak DB liveness or clock drift
								// that helps fingerprint outages or correlate distributed attacks.
								bool isLoopback = ctx.Connection.RemoteIpAddress != null && System.Net.IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress);
								if (isLoopback)
								{
									await ctx.Response.WriteAsync("{\"status\":\"" + (db ? "ok" : "degraded") + "\",\"db\":" + (db ? "true" : "false") + ",\"time\":\"" + DateTime.UtcNow.ToString("o") + "\"}");
								}
								else
								{
									await ctx.Response.WriteAsync("{\"status\":\"" + (db ? "ok" : "degraded") + "\"}");
								}
							});
						});
					});
				});







		/// <summary>
		/// Refuses to start in Production unless the Npgsql connection string
		/// explicitly sets <c>Ssl Mode=Require</c> or stricter (<c>VerifyCA</c>,
		/// <c>VerifyFull</c>). A missing <c>Ssl Mode</c> defaults to <c>Prefer</c>
		/// which silently falls back to plaintext if the server doesn't advertise
		/// TLS, exposing credentials and result rows to any host on-path between
		/// the web tier and the database. Operators that genuinely need to opt out
		/// (e.g., over an IPC socket) must set
		/// <c>ConnectionStrings:AllowInsecureNpgsql=true</c>.
		/// </summary>
		private static void ValidateNpgsqlSslMode(IConfiguration configuration, IHostEnvironment environment)
		{
			if (!environment.IsProduction()) return;

			bool allowInsecure = configuration.GetValue("ConnectionStrings:AllowInsecureNpgsql", false);
			if (allowInsecure) return;

			string? connectionString = configuration.GetConnectionString("NpgsqlConnection");
			if (string.IsNullOrWhiteSpace(connectionString))
				return; // Other startup code will fail with clearer error

			// Use Npgsql's connection string builder for authoritative parsing
			// instead of fragile regex matching that can be bypassed by quoted values
			// or duplicate keys.
			try
			{
				var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
				if (builder.SslMode != Npgsql.SslMode.Require &&
					builder.SslMode != Npgsql.SslMode.VerifyCA &&
					builder.SslMode != Npgsql.SslMode.VerifyFull)
				{
					throw new InvalidOperationException(
						"In Production, the Npgsql ConnectionStrings:NpgsqlConnection must use " +
						"'Ssl Mode=Require' (or VerifyCA / VerifyFull). The default 'Prefer' silently " +
						"falls back to plaintext, exposing credentials. Set " +
						"ConnectionStrings:AllowInsecureNpgsql=true to deliberately allow an " +
						"unencrypted connection (e.g., loopback Unix socket).");
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
}
