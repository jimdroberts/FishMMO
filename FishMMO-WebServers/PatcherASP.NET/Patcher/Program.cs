using FishMMO.Database.Npgsql;
using System.Security.Cryptography.X509Certificates;

namespace FishMMO.WebServer
{
	public class Program
	{
		private static readonly string HttpsPort = "8000";
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

						// Register patch version tracking and background heartbeat service
						services.AddSingleton<PatchVersionService>();
						services.AddHostedService<PatchServerHeartbeatService>();

						// Controllers
						services.AddControllers();
					})
					.Configure(app =>
					{
						// Custom middleware to allow only Unity clients
						app.UseMiddleware<UnityOnlyMiddleware>();

						// Ensure that PatchVersionService loads version file on app start
						var versionService = app.ApplicationServices.GetRequiredService<PatchVersionService>();
						var versionFilePath = Path.Combine(Directory.GetCurrentDirectory(), "Configuration.cfg");
						versionService.LoadFromFile(versionFilePath);  // Load version during app startup

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