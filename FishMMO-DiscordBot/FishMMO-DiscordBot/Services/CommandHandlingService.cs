using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection;
using System.Threading.Tasks;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace FishMMO.DiscordBot.Services
{
	public class CommandHandlingService
	{
		private readonly DiscordSocketClient discord;
		private readonly CommandService commands;
		private readonly IConfiguration configuration;
		private readonly ILogger<CommandHandlingService> logger;
		private readonly IServiceProvider serviceProvider; // Correctly injected ServiceProvider
		private readonly DynamicChannelManagerService dynamicChannelManager;

		// Define a placeholder for Discord chat channel type
		// YOU MUST ENSURE ChatChannel.Discord IS DEFINED IN YOUR FishMMO.Shared PROJECT
		// For example: public enum ChatChannel : byte { ..., Discord = 9 }
		// If not, replace (byte)ChatChannel.Discord with a raw byte value like 255.
		private const byte DiscordChatChannelType = (byte)ChatChannel.Discord;

		public CommandHandlingService(
			DiscordSocketClient discord,
			CommandService commands,
			IConfiguration configuration,
			ILogger<CommandHandlingService> logger,
			IServiceProvider serviceProvider, // Now injecting IServiceProvider directly
			DynamicChannelManagerService dynamicChannelManager)
		{
			this.discord = discord;
			this.commands = commands;
			this.configuration = configuration;
			this.logger = logger;
			this.serviceProvider = serviceProvider; // Initialize ServiceProvider
			this.dynamicChannelManager = dynamicChannelManager;

			discord.MessageReceived += MessageReceivedAsync;
			commands.CommandExecuted += CommandExecutedAsync;
		}

		public async Task InitializeAsync()
		{
			// Discover all of the command modules in the entry assembly and load them.
			// Use the injected serviceProvider here
			await commands.AddModulesAsync(Assembly.GetEntryAssembly(), serviceProvider);
			logger.LogInformation("Command modules initialized.");
		}

		private async Task MessageReceivedAsync(SocketMessage rawMessage)
		{
			// Bots should not respond to other bots or webhooks.
			if (rawMessage.Source != MessageSource.User)
			{
				logger.LogDebug("Ignoring message from bot or webhook: {MessageId}", rawMessage.Id);
				return;
			}

			// Ensure the message is from a SocketUserMessage
			if (!(rawMessage is SocketUserMessage message))
			{
				logger.LogDebug("Ignoring non-user message: {MessageId}", rawMessage.Id);
				return;
			}

			// Create a number to track where the prefix ends.
			int argPos = 0;

			// Determine if the message is a command based on '/' or a custom prefix
			bool isCommand = message.HasCharPrefix('/', ref argPos) ||
							 message.HasMentionPrefix(discord.CurrentUser, ref argPos);

			// Create a WebSocketCommandContext
			var context = new SocketCommandContext(discord, message);

			if (isCommand)
			{
				logger.LogInformation("Processing command '{CommandText}' from {User} in channel {ChannelName}.", message.Content, context.User.Username, context.Channel.Name);
				// Attempt to execute the command.
				// Pass serviceProvider to ExecuteAsync for command module dependency resolution
				var result = await commands.ExecuteAsync(context, argPos, serviceProvider);

				if (!result.IsSuccess && result.Error != CommandError.UnknownCommand)
				{
					logger.LogError("Command execution failed for '{CommandText}': {ErrorReason} (Type: {ErrorType})", message.Content, result.ErrorReason, result.Error);
					await context.Channel.SendMessageAsync($"Error: {result.ErrorReason}");
				}
			}
			else
			{
				// This is a regular chat message from Discord.
				logger.LogDebug("Processing regular message from Discord: '{MessageContent}' by {User} in channel {ChannelName}.", message.Content, context.User.Username, context.Channel.Name);

				if (context.Channel is SocketTextChannel textChannel && textChannel.Guild != null)
				{
					// Check if this Discord channel is one of our managed dynamic game chat channels
					if (dynamicChannelManager.IsOurDynamicChannel(textChannel.Guild.Id, textChannel.Id))
					{
						// Use a new DbContext instance for each operation to ensure thread safety
						// IServiceScopeFactory is available via serviceProvider for creating scopes
						using (var scope = serviceProvider.CreateScope()) // Use serviceProvider to create scope
						{
							// Resolve DbContext from the scope
							var scopedDbContext = scope.ServiceProvider.GetRequiredService<NpgsqlDbContext>();
							var scopedLogger = scope.ServiceProvider.GetRequiredService<ILogger<CommandHandlingService>>();

							try
							{
								// Get World and Scene IDs from the channel.
								var (worldId, sceneId) = dynamicChannelManager.GetWorldAndSceneIdsFromChannel(textChannel);

								if (worldId.HasValue && sceneId.HasValue)
								{
									var chatEntity = new ChatEntity
									{
										// ID is [DatabaseGenerated(DatabaseGeneratedOption.Identity)] so we don't set it.
										CharacterID = 0L, // Placeholder: map Discord user ID to a game character ID, or use a system ID.
										WorldServerID = worldId.Value,
										SceneServerID = sceneId.Value,
										TimeCreated = DateTime.UtcNow,
										Channel = DiscordChatChannelType, // Set to the byte value for Discord chat
										Message = $"{context.User.Username} {message.Content}"
									};

									await scopedDbContext.Chat.AddAsync(chatEntity);
									await scopedDbContext.SaveChangesAsync();
									scopedLogger.LogInformation("Saved Discord message to game database: '{MessageContent}' by '{User}' in channel '{ChannelName}' (World: {WorldId}, Scene: {SceneId}).",
										message.Content, context.User.Username, textChannel.Name, worldId.Value, sceneId.Value);

									// Update channel activity to prevent it from being cleaned up
									await dynamicChannelManager.UpdateChannelActivityAsync(textChannel.Guild.Id, worldId.Value, sceneId.Value);
								}
								else
								{
									scopedLogger.LogWarning("Discord message received in dynamic channel '{ChannelName}' (ID: {ChannelId}) but could not extract valid World/Scene IDs from its name/category. Message not logged to game chat.", textChannel.Name, textChannel.Id);
								}
							}
							catch (Exception ex)
							{
								scopedLogger.LogError(ex, "Failed to save Discord message to game database: '{MessageContent}' from {User} in channel {ChannelName}.", message.Content, context.User.Username, textChannel.Name);
							}
						}
					}
					else
					{
						// Message is not in a dynamic game chat channel, so it's not logged to game DB
						logger.LogDebug("Ignoring non-game chat Discord message: '{MessageContent}' by {User} in channel {ChannelName}. Not a managed dynamic channel.", message.Content, context.User.Username, textChannel.Name);
					}
				}
			}
		}

		private async Task CommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
		{
			// Log command execution outcomes
			if (!string.IsNullOrEmpty(context.Guild?.Name))
			{
				logger.LogInformation("Command '{CommandName}' executed in Guild '{GuildName}' by '{User}': Success={Success}, Reason='{Reason}'",
									 command.IsSpecified ? command.Value.Name : "Unknown",
									 context.Guild.Name,
									 context.User.Username,
									 result.IsSuccess,
									 result.ErrorReason);
			}
			else
			{
				logger.LogInformation("Command '{CommandName}' executed in DM by '{User}': Success={Success}, Reason='{Reason}'",
									 command.IsSpecified ? command.Value.Name : "Unknown",
									 context.User.Username,
									 result.IsSuccess,
									 result.ErrorReason);
			}

			if (!result.IsSuccess && result.Error != CommandError.UnknownCommand)
			{
				// You can add more specific error handling here, e.g.,
				// if (result.Error == CommandError.BadArgCount)
				// {
				//     await context.Channel.SendMessageAsync("Incorrect number of arguments.");
				// }
			}
		}
	}
}