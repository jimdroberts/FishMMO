using FishMMO.Database.Npgsql;
using System.Security.Cryptography.X509Certificates;

namespace FishMMO.WebServer
{
	public class Program
	{
		private static readonly string HttpsPort = "8080";
		private static readonly string PfxCertificatePath = Path.Combine(Directory.GetCurrentDirectory(), "certificate.pfx");
		private static readonly string PfxPassword = "testpassword";

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
						// Use the .pfx certificate directly
						var certificate = new X509Certificate2(PfxCertificatePath, PfxPassword);

						// Configure Kestrel to use the loaded certificate for HTTPS
						options.ListenAnyIP(int.Parse(HttpsPort), listenOptions =>
						{
							listenOptions.UseHttps(certificate);
						});
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
					})
					.Configure(app =>
					{
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