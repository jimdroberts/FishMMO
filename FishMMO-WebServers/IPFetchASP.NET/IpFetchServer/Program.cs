using FishMMO.Database.Npgsql;
using Microsoft.AspNetCore.HttpOverrides;

namespace FishMMO.WebServer
{
	public class Program
	{
		private static readonly string HttpPort = "8080";

		public static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureLogging((context, logging) =>
				{
					// Configure the logger to log to the console
					logging.ClearProviders();
					logging.AddConsole();
					logging.SetMinimumLevel(LogLevel.Information);
				})
				.ConfigureWebHostDefaults(webBuilder =>
				{
					webBuilder.ConfigureKestrel((context, options) =>
					{
						options.ListenAnyIP(int.Parse(HttpPort));
					})
					.ConfigureServices((context, services) =>
					{
						// Register NpgsqlDbContextFactory
						services.AddSingleton<NpgsqlDbContextFactory>();

						// Register Memory Cache
						services.AddMemoryCache();

						// Add controllers (MVC) to the DI container
						services.AddControllers();

						// Configure CORS
						services.AddCors(options =>
						{
							options.AddPolicy("AllowXFishMMO", builder =>
							{
								builder
									.AllowAnyOrigin()  // Allow all origins (you can specify a list for production)
									.AllowAnyMethod()   // Allow all HTTP methods (GET, POST, OPTIONS)
									.WithHeaders("X-FishMMO");  // Only allow the X-FishMMO header
							});
						});

						services.Configure<ForwardedHeadersOptions>(options =>
						{
							options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
							// If your NGINX server is not on localhost (e.g., a separate VM or container),
							// you might need to add its IP address or network range here.
							// By default, loopback addresses (localhost) are trusted.
							// options.KnownProxies.Add(System.Net.IPAddress.Parse("YOUR_NGINX_SERVER_IP"));
							// options.KnownNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8));
						});
					})
					.Configure(app =>
					{
						app.UseForwardedHeaders();

						// Enable CORS with the configured policy
						app.UseCors("AllowXFishMMO");

						app.UseMiddleware<UnityOnlyMiddleware>();

						// Enable routing for API endpoints
						app.UseRouting();

						// Enable endpoints for controllers
						app.UseEndpoints(endpoints =>
						{
							// Map controllers to their respective routes
							endpoints.MapControllers();
						});
					});
				});
	}
}