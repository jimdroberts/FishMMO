using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.DiscordBot.Services;
using Microsoft.Extensions.Logging;

namespace FishMMO.DiscordBot
{
	public class Program
	{
		// Using a more standard IHost approach as recommended by .NET Core for background services
		public static async Task Main(string[] args)
		{
			// Create the host builder
			var host = CreateHostBuilder(args).Build();

			// Resolve and initialize CommandHandlingService early to hook into Discord events
			var commandHandlingService = host.Services.GetRequiredService<CommandHandlingService>();
			await commandHandlingService.InitializeAsync();

			// Start the Discord client and run the host
			var discordClient = host.Services.GetRequiredService<DiscordSocketClient>();
			var config = host.Services.GetRequiredService<IConfiguration>();
			string discordToken = config.GetSection("Discord")["Token"];

			discordClient.Ready += async () =>
			{
				var logger = host.Services.GetRequiredService<ILogger<Program>>();
				logger.LogInformation($"Bot is connected as {discordClient.CurrentUser.Username}#{discordClient.CurrentUser.Discriminator}");
			};

			discordClient.Disconnected += async (ex) =>
			{
				var logger = host.Services.GetRequiredService<ILogger<Program>>();
				logger.LogWarning($"Bot disconnected: {ex?.Message}");
				// Services implementing IHostedService will be stopped by the host automatically
			};

			await discordClient.LoginAsync(TokenType.Bot, discordToken);
			await discordClient.StartAsync();

			// Run the host, which will start all IHostedServices (like ChatPollingService)
			await host.RunAsync();
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureAppConfiguration((hostingContext, config) =>
				{
					config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
					config.AddEnvironmentVariables();
				})
				.ConfigureServices((hostContext, services) =>
				{
					// Configure logging
					services.AddLogging(configure =>
					{
						configure.AddConsole();
						// You can add more logging providers here, e.g., for file logging
						configure.SetMinimumLevel(LogLevel.Debug); // Set overall log level
					});

					// Discord.Net configuration
					services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
					{
						GatewayIntents = GatewayIntents.AllUnprivileged | // Start with all non-privileged intents
										 GatewayIntents.MessageContent |    // Required for reading message content
										 GatewayIntents.GuildMembers |      // Required for guild member events (if bot needs user info)
										 GatewayIntents.GuildPresences,     // Required for presence updates (online status, activity)
						LogLevel = LogSeverity.Debug, // Set Discord.Net's logging level
						AlwaysDownloadUsers = true // Ensure users are always downloaded (needed for certain member-related events)
					}));
					services.AddSingleton<CommandService>();

					// Add NpgsqlDbContext and its factory
					// Ensures that DbContext instances can be created correctly
					services.AddSingleton<NpgsqlDbContextFactory>();
					services.AddTransient<NpgsqlDbContext>(provider => // Transient lifetime is usually good for operations per request/task
					{
						var factory = provider.GetRequiredService<NpgsqlDbContextFactory>();
						return factory.CreateDbContext();
					});

					// Register custom services
					services.AddSingleton<BotConfigurationService>();
					services.AddSingleton<DynamicChannelManagerService>();
					services.AddSingleton<CommandHandlingService>(); // Handles Discord messages and commands
					services.AddHostedService<ChatPollingService>(); // Background service for polling game chat
				});
	}
}