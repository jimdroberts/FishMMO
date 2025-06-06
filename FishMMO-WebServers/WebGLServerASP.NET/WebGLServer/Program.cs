using Microsoft.AspNetCore.HttpOverrides;

namespace FishMMO.WebServer
{
	public class Program
	{
		private static readonly string HttpPort = "8000";

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
						services.AddControllers();

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
					.Configure((context, app) =>
					{
						app.UseForwardedHeaders();

						// Enable CORS with the configured policy
						app.UseCors("AllowAllOrigins");

						// Serve static files from the root directory
						app.UseDefaultFiles();
						app.UseStaticFiles();

						app.UseMiddleware<RangeRequestMiddleware>();

						app.UseRouting();

						app.UseEndpoints(endpoints =>
						{
							endpoints.MapControllers();
						});
					});
				});
	}
}