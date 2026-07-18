using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using FishMMO.Logging;
using FishMMO.WebShared;

namespace FishMMO.WebServer
{
	/// <summary>
	/// Host program for the WebGL static-asset server. Serves the Unity WebGL
	/// build via ASP.NET Core's built-in <c>UseStaticFiles</c> (which supports
	/// HTTP Range requests, ETags, conditional GETs, and is safe against
	/// directory traversal) plus a per-IP rate limiter.
	/// </summary>
	public class Program
	{
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
								// WebServer:HttpPort accepts both string ("8000") and number (8000) formats
								// through the configuration binder. This inconsistency is intentional
								// so operators can use either format in appsettings.json.
						var httpPort = context.Configuration["WebServer:HttpPort"] ?? "8000";
						if (!int.TryParse(httpPort, out int port) || port <= 0 || port > 65535)
						{
							throw new InvalidOperationException($"WebServer:HttpPort '{httpPort}' is not a valid TCP port.");
						}
						options.ListenLocalhost(port);
						// Hardening: WebGL host serves static files only — no request body expected.
						options.Limits.MaxRequestBodySize = 16 * 1024;
						Log.Info("Kestrel", $"Kestrel configured to listen on localhost on port {httpPort}.");
					})
					.ConfigureServices((context, services) =>
					{
						Log.Info("Services", "Registering services...");

						// Compress static assets (.wasm, .data, .unityweb) to reduce
						// bandwidth for large WebGL builds (20-50 MB uncompressed).
						services.AddResponseCompression(options =>
						{
							options.EnableForHttps = true;
							options.MimeTypes = new[]
							{
								"application/wasm",
								"application/octet-stream",
								"application/x-gzip",
							};
						});

						services.AddControllers();

						// CORS — defaults to deny (no Access-Control-Allow-Origin emitted) when
						// Cors:AllowedOrigins is unset. The WebGL build is loaded same-origin in
						// the browser, so default-deny does not affect normal play. Operators who
						// host the build assets on a separate origin must opt in explicitly.
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
							MiddlewareExtensions.ConfigureTrustedProxies(options, context.Configuration, context.HostingEnvironment);
						});

						services.AddRateLimiter(options =>
						{
							options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
							options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
							{
								string key = httpContext.GetClientIpKey();
								return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
								{
									TokenLimit = 120,
									TokensPerPeriod = 60,
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
						app.UseFishMMOSecurityHeaders(context.HostingEnvironment, extraHeaders: h =>
						{
							// Cross-Origin-Opener-Policy isolates the
							// top-level browsing context; Cross-Origin-Embedder-Policy is required
							// for SharedArrayBuffer (Unity WebGL 6 multi-threaded builds need it).
							if (!h.ContainsKey("Cross-Origin-Opener-Policy"))
								h["Cross-Origin-Opener-Policy"] = "same-origin";
							if (!h.ContainsKey("Cross-Origin-Embedder-Policy"))
								h["Cross-Origin-Embedder-Policy"] = "require-corp";
							if (!h.ContainsKey("Content-Security-Policy"))
								h["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; connect-src 'self' wss://game.fishmmo.com:* https://game.fishmmo.com:*; img-src 'self' data:; style-src 'self' 'unsafe-inline'; worker-src 'self' blob:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
						});
						// NOTE: ClientGate (UseFishMMOClientGate) is intentionally NOT used
						// here. Browsers cannot add custom headers (X-FishMMO-Client) to static
						// file requests served via <script> / <link> / <img> tags, so the gate
						// would reject legitimate WebGL asset loads. Access control relies on the
						// rate limiter and CSP headers instead.
						app.UseCors("Public");
						app.UseRateLimiter();

						// Configure additional MIME mappings used by Unity WebGL.
						var contentTypeProvider = new FileExtensionContentTypeProvider();
						contentTypeProvider.Mappings[".wasm"] = "application/wasm";
						contentTypeProvider.Mappings[".unityweb"] = "application/octet-stream";
						contentTypeProvider.Mappings[".bundle"] = "application/octet-stream";
						contentTypeProvider.Mappings[".bin"] = "application/octet-stream";
						contentTypeProvider.Mappings[".hash"] = "text/plain";
						contentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json";
						contentTypeProvider.Mappings[".data"] = "application/octet-stream";

						app.UseResponseCompression();
						app.UseDefaultFiles();
						// Built-in static-file handler: safe path resolution, ETag/If-Modified-Since,
						// Range requests, and Last-Modified are all handled correctly.
						app.UseStaticFiles(new StaticFileOptions
						{
							ContentTypeProvider = contentTypeProvider,
							ServeUnknownFileTypes = false,
							OnPrepareResponse = ctx =>
							{
								ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
							},
						});

						app.UseRouting();
						app.UseEndpoints(endpoints =>
						{
							endpoints.MapControllers();
							endpoints.MapGet("/healthz", async ctx =>
							{
								ctx.Response.ContentType = "application/json";
								ctx.Response.StatusCode = 200;
								bool isLoopback = ctx.Connection.RemoteIpAddress != null && System.Net.IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress);
								if (isLoopback)
								{
									await ctx.Response.WriteAsync("{\"status\":\"ok\",\"time\":\"" + DateTime.UtcNow.ToString("o") + "\"}");
								}
								else
								{
									await ctx.Response.WriteAsync("{\"status\":\"ok\"}");
								}
							});
						});
					});
				});


	}
}
