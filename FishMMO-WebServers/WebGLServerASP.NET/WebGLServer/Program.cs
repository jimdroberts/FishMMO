using Microsoft.Extensions.FileProviders;

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
					})
					.Configure((context, app) =>
					{
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