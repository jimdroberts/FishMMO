using FishMMO.Database.Npgsql;
using Microsoft.AspNetCore.HttpOverrides;
using FishMMO.Logging;

namespace FishMMO.WebServer
{
	/// <summary>
	/// Host entry point for the FishMMO web server application.
	/// Responsible for initializing logging, propagating environment variables
	/// and building/running the ASP.NET host.
	/// </summary>
	public class Program
	{
		/// <summary>
		/// Application entry point. Initializes logging, maps the custom
		/// <c>FISHMMO_ENVIRONMENT</c> variable to the standard .NET environment
		/// variables, then builds and runs the web host.
		/// </summary>
		/// <param name="args">Command-line arguments passed to the application.</param>
		/// <returns>A <see cref="Task"/> that completes when the host terminates.</returns>
		public static async Task Main(string[] args)
		{
			// Force the .NET Host to recognize FISHMMO_ENVIRONMENT
			// This ensures context.HostingEnvironment.EnvironmentName is correct globally.
			string? fishEnv = Environment.GetEnvironmentVariable("FISHMMO_ENVIRONMENT");
			if (!string.IsNullOrWhiteSpace(fishEnv))
			{
				Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", fishEnv);
				Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", fishEnv);
			}

			await Log.Initialize("logging.json");
			await Log.Info("Program", "Starting WebServer application...");

			try
			{
				CreateHostBuilder(args).Build().Run();
			}
			catch (Exception ex)
			{
				await Log.Error("Program", $"Host terminated unexpectedly: {ex.Message}");
			}
			finally
			{
				await Log.Shutdown();
			}
		}

		/// <summary>
		/// Creates and configures the <see cref="IHostBuilder"/> for the web host.
		/// This includes Kestrel configuration, DI service registrations, CORS
		/// policy setup and middleware configuration.
		/// </summary>
		/// <param name="args">Command-line arguments forwarded to the host builder.</param>
		/// <returns>An <see cref="IHostBuilder"/> configured for the FishMMO web server.</returns>
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
						int port = context.Configuration.GetValue<int>("WebServer:HttpPort", 8080);
						options.ListenLocalhost(port);
						Log.Info("Kestrel", $"Kestrel listening on localhost port {port} in {context.HostingEnvironment.EnvironmentName} mode.");
					})
					.ConfigureServices((context, services) =>
					{
						Log.Info("Services", "Registering services...");

						services.AddSingleton(new NpgsqlDbConfiguration(context.Configuration));
						services.AddSingleton<NpgsqlDbContextFactory>();
						services.AddMemoryCache();
						services.AddControllers();

						services.AddCors(options =>
						{
							options.AddPolicy("AllowXFishMMO", builder =>
							{
								builder.AllowAnyOrigin().AllowAnyMethod().WithHeaders("X-FishMMO");
							});
						});

						services.Configure<ForwardedHeadersOptions>(options =>
						{
							options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
						});

						Log.Info("Services", "All services registered.");
					})
					.Configure(app =>
					{
						app.UseForwardedHeaders();
						app.UseCors("AllowXFishMMO");
						app.UseMiddleware<UnityOnlyMiddleware>();
						app.UseRouting();
						app.UseAuthorization();
						app.UseEndpoints(endpoints => endpoints.MapControllers());
					});
				});
	}
}