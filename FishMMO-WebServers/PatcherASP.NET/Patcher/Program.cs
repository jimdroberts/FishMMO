using Microsoft.AspNetCore.HttpOverrides;
using FishMMO.Logging;

namespace FishMMO.WebServer
{
	/// <summary>
	/// Program contains the application's entry point and host configuration.
	/// It initializes logging and builds the ASP.NET Core host that runs the web server.
	/// </summary>
	public class Program
	{
		/// <summary>
		/// Application entry point. Initializes logging, starts the web host, and
		/// performs a graceful shutdown when the host stops.
		/// </summary>
		/// <param name="args">Command-line arguments passed to the application.</param>
		/// <returns>A <see cref="Task"/> that completes when shutdown logic finishes.</returns>
		public static async Task Main(string[] args)
		{
			await Log.Initialize("logging.json");

			await Log.Info("Program", "Starting WebServer application...");

			CreateHostBuilder(args).Build().Run();

			await Log.Shutdown();
			await Log.Info("Program", "WebServer application shut down.");
		}

		/// <summary>
		/// Creates and configures the <see cref="IHostBuilder"/> used to run the web server.
		/// The builder sets up logging, Kestrel server options, DI services, CORS, forwarded headers,
		/// and the HTTP request pipeline including controllers and middleware.
		/// </summary>
		/// <param name="args">Command-line arguments forwarded to the host builder.</param>
		/// <returns>An <see cref="IHostBuilder"/> configured for this application.</returns>
		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureLogging((context, logging) =>
				{
					logging.ClearProviders();
				})
				.ConfigureWebHostDefaults(webBuilder =>
				{
					webBuilder.ConfigureKestrel((context, options) =>
					{
						// Get port from configuration
						var httpPort = context.Configuration["WebServer:HttpPort"] ?? "8090"; // Default to 8090 if not found
						options.ListenLocalhost(int.Parse(httpPort));
						Log.Info("Kestrel", $"Kestrel configured to listen on localhost on port {httpPort}.");
					})
					.ConfigureServices((context, services) =>
					{
						Log.Info("Services", "Registering services...");

						// Register HttpClientFactory
						// Register patch version tracking
						services.AddSingleton<PatchVersionService>();
						Log.Info("Services", "Registered PatchVersionService.");
						// Controllers
						services.AddControllers();
						Log.Info("Services", "Registered Controllers.");

						services.AddCors(options =>
						{
							options.AddPolicy("AllowXFishMMO", builder =>
							{
								builder
									.AllowAnyOrigin()
									.AllowAnyMethod()
									.WithHeaders("X-FishMMO");
							});
						});
						Log.Info("Services", "Configured CORS policy 'AllowXFishMMO' with AllowAnyOrigin.");

						services.Configure<ForwardedHeadersOptions>(options =>
						{
							options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
							// If your NGINX server is not on localhost (e.g., a separate VM or container),
							// you might need to add its IP address or network range here.
							// By default, loopback addresses (localhost) are trusted.
							// options.KnownProxies.Add(System.Net.IPAddress.Parse("YOUR_NGINX_SERVER_IP"));
							// options.KnownNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8));
						});
						Log.Info("Services", "Configured ForwardedHeadersOptions.");

						Log.Info("Services", "All services registered.");
					})
					.Configure(app =>
					{
						Log.Info("Middleware", "Configuring HTTP request pipeline...");

						app.UseForwardedHeaders();
						Log.Info("Middleware", "Added UseForwardedHeaders middleware.");

						app.UseCors("AllowXFishMMO");
						Log.Info("Middleware", "Added UseCors middleware with policy 'AllowXFishMMO'.");

						app.UseMiddleware<UnityOnlyMiddleware>();
						Log.Info("Middleware", "Added UnityOnlyMiddleware.");

						app.UseRouting();
						Log.Info("Middleware", "Added UseRouting middleware.");

						app.UseEndpoints(endpoints =>
						{
							endpoints.MapControllers();
						});
						Log.Info("Middleware", "Mapped controller endpoints.");
						Log.Info("Middleware", "HTTP request pipeline configured.");
					});
				});
	}
}