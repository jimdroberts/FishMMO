using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FishMMO.DiscordBot.Modules;

namespace FishMMO.DiscordBot.Services
{
	/// <summary>
	/// Handles incoming Discord messages: routes commands to modules and
	/// delegates non-command messages in managed channels to <see cref="GameChatBridgeService"/>.
	/// </summary>
	public class CommandHandlingService
	{
		private readonly DiscordSocketClient discord;
		private readonly CommandService commands;
		private readonly ILogger<CommandHandlingService> logger;
		private readonly IServiceProvider serviceProvider;
		private readonly GameChatBridgeService gameChatBridge;
		private readonly BotConfigurationService botConfigService;

		/// <summary>
		/// Initializes a new instance of the <see cref="CommandHandlingService"/> class.
		/// </summary>
		/// <param name="discord">The Discord socket client.</param>
		/// <param name="commands">The command service for slash/prefix commands.</param>
		/// <param name="logger">Logger instance.</param>
		/// <param name="serviceProvider">Service provider for dependency resolution.</param>
		/// <param name="gameChatBridge">Service that bridges Discord messages to the game database.</param>
		public CommandHandlingService(
			DiscordSocketClient discord,
			CommandService commands,
			ILogger<CommandHandlingService> logger,
			IServiceProvider serviceProvider,
			GameChatBridgeService gameChatBridge,
			BotConfigurationService botConfigService)
		{
			this.discord = discord;
			this.commands = commands;
			this.logger = logger;
			this.serviceProvider = serviceProvider;
			this.gameChatBridge = gameChatBridge;
			this.botConfigService = botConfigService;

			discord.MessageReceived += MessageReceivedAsync;
			commands.CommandExecuted += CommandExecutedAsync;
		}

		/// <summary>
		/// Discovers and loads all command modules from the entry assembly.
		/// Must be called once during application startup.
		/// </summary>
		public async Task InitializeAsync()
		{
			await commands.AddModulesAsync(Assembly.GetEntryAssembly(), serviceProvider);
			logger.LogInformation("Command modules initialized.");
		}

		/// <summary>
		/// Processes incoming Discord messages. Routes commands to the command service
		/// and delegates non-command messages from managed channels to the bridge service.
		/// </summary>
		private async Task MessageReceivedAsync(SocketMessage rawMessage)
		{
			if (rawMessage.Source != MessageSource.User)
			{
				return;
			}

			if (!(rawMessage is SocketUserMessage message))
			{
				return;
			}

			int argPos = 0;
			bool isCommand = message.HasCharPrefix('/', ref argPos) ||
							 message.HasMentionPrefix(discord.CurrentUser, ref argPos);

			var context = new SocketCommandContext(discord, message);

			if (isCommand)
			{
				// Check guild command permissions before executing
				if (context.Guild != null)
				{
					var searchResult = commands.Search(context, argPos);
					if (searchResult.IsSuccess && searchResult.Commands.Count > 0)
					{
						var cmdInfo = searchResult.Commands[0].Command;
						string cmdKey = CommandListModule.BuildCommandKey(cmdInfo);
						var permConfig = botConfigService.GetCommandPermissionConfig(context.Guild.Id);

						if (permConfig != null)
						{
							if (permConfig.DisabledCommands.Contains(cmdKey))
							{
								logger.LogInformation(
									"Blocked disabled command '{CommandKey}' from {User} in {Guild}.",
									cmdKey, context.User.Username, context.Guild.Name);
								await context.Channel.SendMessageAsync("This command is disabled in this server.");
								return;
							}

							if (permConfig.RoleRequirements.TryGetValue(cmdKey, out ulong requiredRoleId))
							{
								var guildUser = context.User as IGuildUser;
								if (guildUser == null || !guildUser.RoleIds.Contains(requiredRoleId))
								{
									logger.LogInformation(
										"Blocked command '{CommandKey}' from {User} — missing required role {RoleId}.",
										cmdKey, context.User.Username, requiredRoleId);
									await context.Channel.SendMessageAsync("You don't have the required role to use this command.");
									return;
								}
							}
						}
					}
				}

				logger.LogInformation(
					"Processing command '{CommandText}' from {User} in channel {ChannelName}.",
					message.Content, context.User.Username, context.Channel.Name);

				var result = await commands.ExecuteAsync(context, argPos, serviceProvider);

				if (!result.IsSuccess && result.Error != CommandError.UnknownCommand)
				{
					logger.LogError(
						"Command execution failed for '{CommandText}': {ErrorReason} (Type: {ErrorType})",
						message.Content, result.ErrorReason, result.Error);
					// Do not expose internal error details to users
					await context.Channel.SendMessageAsync("An error occurred while processing the command.");
				}
			}
			else
			{
				if (context.Channel is SocketTextChannel textChannel && textChannel.Guild != null)
				{
					try
					{
						var bridgeResult = await gameChatBridge.BridgeMessageAsync(message, textChannel);

						switch (bridgeResult)
						{
							case BridgeResult.RateLimited:
								await message.AddReactionAsync(new Emoji("\u23F3")); // hourglass
								break;
							case BridgeResult.BridgeBanned:
								await message.AddReactionAsync(new Emoji("\uD83D\uDEAB")); // prohibited sign
								break;
							case BridgeResult.Success:
							case BridgeResult.NotManagedChannel:
							case BridgeResult.EmptyMessage:
								break;
							case BridgeResult.ParseFailure:
							case BridgeResult.Error:
								logger.LogWarning(
									"Bridge returned {Result} for message by {User} in {Channel}.",
									bridgeResult, context.User.Username, textChannel.Name);
								break;
						}
					}
					catch (Exception ex)
					{
						logger.LogError(ex,
							"Unexpected error bridging message from {User} in channel {Channel}.",
							context.User.Username, textChannel.Name);
					}
				}
			}
		}

		/// <summary>
		/// Logs the outcome of executed commands.
		/// </summary>
		private Task CommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
		{
			string commandName = command.IsSpecified ? command.Value.Name : "Unknown";
			string guildName = context.Guild?.Name ?? "DM";

			logger.LogInformation(
				"Command '{CommandName}' executed in '{GuildName}' by '{User}': Success={Success}, Reason='{Reason}'",
				commandName, guildName, context.User.Username, result.IsSuccess, result.ErrorReason);

			return Task.CompletedTask;
		}
	}
}