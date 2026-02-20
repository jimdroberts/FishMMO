using Microsoft.AspNetCore.HttpOverrides;
using FishMMO.Logging;

/// <summary>
/// Entry point and host configuration for the FishMMO WebServer application.
/// This file contains the <see cref="Program"/> class which is responsible
/// for initializing logging and building the application's host.
/// </summary>
namespace FishMMO.WebServer
{
	/// <summary>
	/// Host program for the WebServer. Initializes logging, configures the
	/// web host and middleware, and starts the ASP.NET Core Kestrel server.
	/// </summary>
	public class Program
	{
		/// <summary>
		/// Application entry point. Initializes logging, starts the web host,
		/// and performs a clean shutdown of logging once the host exits.
		/// </summary>
		/// <param name="args">Command-line arguments passed to the application.</param>
		public static async Task Main(string[] args)
		{
			await Log.Initialize("logging.json");

			await Log.Info("Program", "Starting WebServer application...");

			CreateHostBuilder(args).Build().Run();

			await Log.Shutdown();
			await Log.Info("Program", "WebServer application shut down.");
		}

		/// <summary>
		/// Creates and configures an <see cref="IHostBuilder"/> for the web host.
		/// The builder configures logging, Kestrel endpoints, services (including
		/// CORS and forwarded headers), and the HTTP request pipeline (middleware).
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
						var httpPort = context.Configuration["WebServer:HttpPort"] ?? "8000"; // Default to 8000 if not found
						options.ListenLocalhost(int.Parse(httpPort));
						Log.Info("Kestrel", $"Kestrel configured to listen on localhost on port {httpPort}.");
					})
					.ConfigureServices((context, services) =>
					{
						Log.Info("Services", "Registering services...");

						services.AddControllers();
						Log.Info("Services", "Registered Controllers.");

						// Configure CORS
						services.AddCors(options =>
						{
							options.AddPolicy("AllowAllOrigins", builder =>
							{
								builder.AllowAnyOrigin()            // Allow any origin
									.AllowAnyMethod()            // Allow any HTTP method
									.AllowAnyHeader();           // Allow any header
							});
						});
						Log.Info("Services", "Configured CORS policy with AllowAnyOrigin.");

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
					.Configure((context, app) =>
					{
						Log.Info("Middleware", "Configuring HTTP request pipeline...");

						app.UseForwardedHeaders();
						Log.Info("Middleware", "Added UseForwardedHeaders middleware.");

						// Enable CORS with the configured policy
						app.UseCors("AllowAllOrigins");
						Log.Info("Middleware", "Added UseCors middleware with policy 'AllowAllOrigins'.");

						// Serve static files from the root directory
						app.UseDefaultFiles();
						app.UseStaticFiles();
						Log.Info("Middleware", "Serving static files from the root directory.");

						app.UseMiddleware<RangeRequestMiddleware>();
						Log.Info("Middleware", "Added RangeRequestMiddleware.");

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