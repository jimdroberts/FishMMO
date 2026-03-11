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

			string? discordToken = config.GetSection("Discord")["Token"];
			if (string.IsNullOrWhiteSpace(discordToken))
			{
				logger.LogCritical("Discord token is missing in appsettings.json. Bot cannot connect.");
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
					config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
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