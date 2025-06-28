using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.DiscordBot.Services
{
	// This Hosted Service manages the Discord bot's connection and lifecycle
	public class BotService : IHostedService
	{
		private readonly DiscordSocketClient discordClient;
		private readonly CommandService commands;
		private readonly IConfiguration configuration;
		private readonly ILogger<BotService> logger;
		private readonly CommandHandlingService commandHandlingService; // Inject CommandHandlingService

		public BotService(
			DiscordSocketClient discordClient,
			CommandService commands,
			IConfiguration configuration,
			ILogger<BotService> logger,
			CommandHandlingService commandHandlingService) // Inject CommandHandlingService
		{
			this.discordClient = discordClient;
			this.commands = commands;
			this.configuration = configuration;
			this.logger = logger;
			this.commandHandlingService = commandHandlingService; // Initialize CommandHandlingService

			// Logging events for Discord.Net
			discordClient.Log += LogAsync;
			commands.Log += LogAsync;
		}

		private Task LogAsync(LogMessage msg)
		{
			// Map Discord.Net's LogSeverity to Microsoft.Extensions.Logging.LogLevel
			var logLevel = msg.Severity switch
			{
				LogSeverity.Critical => LogLevel.Critical,
				LogSeverity.Error => LogLevel.Error,
				LogSeverity.Warning => LogLevel.Warning,
				LogSeverity.Info => LogLevel.Information,
				LogSeverity.Verbose => LogLevel.Debug,
				LogSeverity.Debug => LogLevel.Trace,
				_ => LogLevel.Information
			};
			logger.Log(logLevel, msg.Exception, "{Source}: {Message}", msg.Source, msg.Message);
			return Task.CompletedTask;
		}

		public async Task StartAsync(CancellationToken cancellationToken)
		{
			logger.LogInformation("BotService is starting.");

			// Initialize the command handling service
			await commandHandlingService.InitializeAsync();

			// Get Discord token from configuration
			string discordToken = configuration.GetSection("Discord")["Token"];
			if (string.IsNullOrEmpty(discordToken))
			{
				logger.LogCritical("Discord token is missing in appsettings.json. Bot cannot connect.");
				throw new InvalidOperationException("Discord token is missing.");
			}

			// Login and start the Discord bot
			await discordClient.LoginAsync(TokenType.Bot, discordToken);
			await discordClient.StartAsync();

			logger.LogInformation("Discord client started. Awaiting connection...");
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			logger.LogInformation("BotService is stopping.");
			// Stop the Discord client
			await discordClient.StopAsync();
			logger.LogInformation("Discord client stopped.");
		}
	}
}