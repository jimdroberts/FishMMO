using FishMMO.Database.Npgsql;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using FishMMO.Logging;
using System.Threading.Tasks;

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
						var httpPort = context.Configuration["WebServer:HttpPort"] ?? "8080"; // Default to 8080 if not found
						options.ListenAnyIP(int.Parse(httpPort));
						Log.Info("Kestrel", $"Kestrel configured to listen on any IP on port {httpPort}.");
					})
					.ConfigureServices((context, services) =>
					{
						Log.Info("Services", "Registering services...");

						services.AddSingleton<NpgsqlDbContextFactory>();
						Log.Info("Services", "Registered NpgsqlDbContextFactory.");

						services.AddMemoryCache();
						Log.Info("Services", "Registered IMemoryCache.");

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

						app.UseAuthorization();
						Log.Info("Middleware", "Added UseAuthorization middleware.");

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