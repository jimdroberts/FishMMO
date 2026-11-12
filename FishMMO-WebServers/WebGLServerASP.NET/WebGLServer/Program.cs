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
								// WebServer:HttpPort accepts both string ("8000") and number (8000) formats
								// through the configuration binder. This inconsistency is intentional
								// so operators can use either format in appsettings.json.
						var httpPort = context.Configuration["WebServer:HttpPort"] ?? "8000";
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
						// Hardening: WebGL host serves static files only — no request body expected.
						options.Limits.MaxRequestBodySize = 16 * 1024;
						_ = Log.Info("Kestrel", $"Kestrel configured to listen on localhost on port {httpPort}.");
					})
					.ConfigureServices((context, services) =>
					{
						_ = Log.Info("Services", "Registering services...");

						// Compress text responses on the fly (HTML, CSS, JavaScript, JSON, an uncompressed .wasm).
						// NOT application/octet-stream: Unity's build files are compressed when the build is made
						// and are served as stored (UnityBuildFiles), and Addressables bundles are compressed
						// already. Compressing either again cost CPU on every download and dropped the
						// Content-Length, so the loader showed no progress bar.
						services.AddResponseCompression(options =>
						{
							options.EnableForHttps = true;
							options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes;
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
								var allowedOrigins = ConfigurationLists.Get(context.Configuration, "Cors:AllowedOrigins");
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
							options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
							{
								string key = httpContext.GetClientIpKey();
								// The game's own files are fetched in hundreds when a world loads (one per Addressables
								// bundle, 700+ in a build); under the page's bucket they were refused with 429 and the load
								// failed. They get their own, larger bucket per client; everything else keeps the small one.
								// Sized for several whole passes at once (2026-11-10): on a fast link the content preloader's
								// pass over every bundle takes about a second, so a reload, or two browsers on one address,
								// emptied a 2000-token bucket in seconds, and Addressables, most of whose groups never retry,
								// failed the world's load on the first 429. These are static files, cached by the browser:
								// the bucket is there to cap a flood, not to pace a player.
								PathString path = httpContext.Request.Path;
								if (path.StartsWithSegments("/Build") || path.StartsWithSegments("/StreamingAssets") ||
									path.StartsWithSegments("/TemplateData"))
								{
									return RateLimitPartition.GetTokenBucketLimiter(key + "|content", _ => new TokenBucketRateLimiterOptions
									{
										TokenLimit = 20000,
										TokensPerPeriod = 2000,
										ReplenishmentPeriod = TimeSpan.FromSeconds(1),
										QueueLimit = 0,
										AutoReplenishment = true,
										QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
									});
								}
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
								context.HttpContext.Response.ContentType = "text/plain";
								// Retry-After is derived from the token bucket replenishment period:
								// TokenLimit=120, TokensPerPeriod=60, ReplenishmentPeriod=1s → 1 second.
								context.HttpContext.Response.Headers["Retry-After"] = "1";
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
						_ = Log.Error("WebGLServer", "Unhandled exception in WebGLServer request pipeline.", exFeature.Error);
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

						app.UseFishMMOSecurityHeaders(context.HostingEnvironment, extraHeaders: h =>
						{
							// Cross-Origin-Opener-Policy isolates the
							// top-level browsing context; Cross-Origin-Embedder-Policy is required
							// for SharedArrayBuffer (Unity WebGL 6 multi-threaded builds need it).
							if (!h.ContainsKey("Cross-Origin-Opener-Policy"))
								h["Cross-Origin-Opener-Policy"] = "same-origin";
							if (!h.ContainsKey("Cross-Origin-Embedder-Policy"))
								h["Cross-Origin-Embedder-Policy"] = "require-corp";

						}, configuration: context.Configuration,
                            profile: ContentSecurityPolicyProfile.WebGl,
                            webRoot: app.ApplicationServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider);
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
						// Brotli build files without decompression fallback (.wasm.br, .data.br, ...).
						contentTypeProvider.Mappings[".br"] = "application/octet-stream";

						// Compressed on the fly: what the browser fetches itself (the page, its scripts and styles). Never
						// StreamingAssets: the game reads those through UnityWebRequest, which on WebGL is the loader's
						// fetchWithProgress, and a response compressed here goes out chunked with no Content-Length, which
						// the loader warns about on every load ("[UnityCache] Response is served without Content-Length
						// header"). The Addressables files there are small (settings.json, catalog.hash) or already
						// compressed (the bundles, catalog.bin), so serving them as stored costs nothing.
						app.UseWhen(
							ctx => !ctx.Request.Path.StartsWithSegments("/StreamingAssets", StringComparison.OrdinalIgnoreCase),
							branch => branch.UseResponseCompression());
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
								// Pre-compressed Unity build files go out as stored, labelled with their encoding.
								UnityBuildFiles.PrepareResponse(ctx.Context, ctx.File);
							},
						});

						app.UseRouting();
						app.UseEndpoints(endpoints =>
						{
							endpoints.MapControllers();
							// The game server's host and address for the connection help page (help/connection.html):
							// some security software can only exclude an IP address.
							endpoints.MapGet("/help/game-address", async ctx =>
							{
								string host = context.Configuration["GameHost"] ?? string.Empty;
								string[] addresses = await GameAddress.ResolveAsync(host, ctx.RequestAborted);
								ctx.Response.Headers["Cache-Control"] = "no-store";
								await ctx.Response.WriteAsJsonAsync(new { host, addresses }, ctx.RequestAborted);
							});
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
