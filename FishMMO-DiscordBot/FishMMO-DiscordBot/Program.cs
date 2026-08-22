using System;
using System.IO;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using FishMMO.Database.Npgsql;
using FishMMO.DiscordBot.Services;

namespace FishMMO.DiscordBot
{
	/// <summary>
	/// Application entry point. Builds and runs the host with all required services.
	/// </summary>
	public class Program
	{
		/// <summary>
		/// Main entry point. Builds the host, initializes command handling,
		/// connects the Discord client, and runs the host.
		/// </summary>
		public static async Task Main(string[] args)
		{
			var host = CreateHostBuilder(args).Build();

			var commandHandlingService = host.Services.GetRequiredService<CommandHandlingService>();
			await commandHandlingService.InitializeAsync();

			var discordClient = host.Services.GetRequiredService<DiscordSocketClient>();
			var config = host.Services.GetRequiredService<IConfiguration>();
			var logger = host.Services.GetRequiredService<ILogger<Program>>();

			// Read the bot token from environment variable only — never from appsettings.json.
			// The token is a full authentication credential and must not appear in config files.
			string? discordToken = Environment.GetEnvironmentVariable("FISHMMO_DISCORD_TOKEN");
			if (string.IsNullOrWhiteSpace(discordToken))
			{
				logger.LogCritical("FISHMMO_DISCORD_TOKEN environment variable is not set. Bot cannot connect.");
				return;
			}

			discordClient.Ready += () =>
			{
				logger.LogInformation(
					"Bot is connected as {Username}#{Discriminator}",
					discordClient.CurrentUser.Username,
					discordClient.CurrentUser.Discriminator);
				return Task.CompletedTask;
			};

			discordClient.Disconnected += (ex) =>
			{
				logger.LogWarning("Bot disconnected: {Message}", ex?.Message);
				return Task.CompletedTask;
			};

			await discordClient.LoginAsync(TokenType.Bot, discordToken);
			await discordClient.StartAsync();

			await host.RunAsync();
		}

		/// <summary>
		/// Configures and returns the host builder with all services registered.
		/// </summary>
		internal static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureAppConfiguration((hostingContext, config) =>
				{
					string env = hostingContext.HostingEnvironment.EnvironmentName;
					// Bundled defaults from FishMMO-Setup (copied to output directory).
					config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false, reloadOnChange: false);
					config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, $"appsettings.{env}.json"), optional: true, reloadOnChange: false);
					// Working-directory overrides take precedence.
					config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
					config.AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true);
					config.AddEnvironmentVariables();
				})
				.ConfigureServices((hostContext, services) =>
				{
					services.AddLogging(configure =>
					{
						configure.AddConsole();
						configure.SetMinimumLevel(LogLevel.Debug);
					});

					services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
					{
						GatewayIntents = GatewayIntents.AllUnprivileged |
										 GatewayIntents.MessageContent |
										 GatewayIntents.GuildMembers |
										 GatewayIntents.GuildPresences,
						LogLevel = LogSeverity.Debug,
						AlwaysDownloadUsers = true
					}));
					services.AddSingleton<CommandService>();

					services.AddSingleton<NpgsqlDbContextFactory>();
					services.AddTransient<NpgsqlDbContext>(provider =>
					{
						var factory = provider.GetRequiredService<NpgsqlDbContextFactory>();
						return factory.CreateDbContext();
					});

					services.AddSingleton<BotConfigurationService>();
					services.AddSingleton<ChatRelayPolicy>();
					services.AddSingleton<RateLimiterService>();
					services.AddSingleton<AccountLinkingService>();
					services.AddSingleton<BridgeBanService>();
					services.AddSingleton<DynamicChannelManagerService>();
					services.AddHostedService(sp => sp.GetRequiredService<DynamicChannelManagerService>());
					services.AddSingleton<GameChatBridgeService>();
					services.AddSingleton<CommandHandlingService>();
					services.AddHostedService<ChatPollingService>();
				});
	}
}