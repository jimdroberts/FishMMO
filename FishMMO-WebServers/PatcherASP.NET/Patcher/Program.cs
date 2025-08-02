using FishMMO.Database.Npgsql;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using FishMMO.Logging;

namespace FishMMO.WebServer
{
	public class Program
	{
		public static async Task Main(string[] args)
		{
			await Log.Initialize("logging.json");

			await Log.Info("Program", "Starting WebServer application...");

			CreateHostBuilder(args).Build().Run();

			await Log.Shutdown();
			await Log.Info("Program", "WebServer application shut down.");
		}

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
						options.ListenAnyIP(int.Parse(httpPort));
						Log.Info("Kestrel", $"Kestrel configured to listen on any IP on port {httpPort}.");
					})
					.ConfigureServices((context, services) =>
					{
						Log.Info("Services", "Registering services...");

						// Register NpgsqlDbContextFactory
						services.AddSingleton<NpgsqlDbContextFactory>();
						Log.Info("Services", "Registered NpgsqlDbContextFactory.");

						// Register HttpClientFactory
						services.AddHttpClient();
						Log.Info("Services", "Registered HttpClientFactory.");

						// Register patch version tracking and background heartbeat service
						services.AddSingleton<PatchVersionService>();
						Log.Info("Services", "Registered PatchVersionService.");
						services.AddHostedService<PatchServerHeartbeatService>();
						Log.Info("Services", "Registered PatchServerHeartbeatService.");

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