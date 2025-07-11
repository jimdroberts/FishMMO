using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Npgsql;
using Microsoft.EntityFrameworkCore;
using Discord;
using System.Linq;

namespace FishMMO.DiscordBot.Services
{
	// IHostedService allows this service to run in the background as long as the application is running.
	public class ChatPollingService : IHostedService, IDisposable
	{
		private readonly DiscordSocketClient discordClient;
		private readonly NpgsqlDbContextFactory dbContextFactory; // Inject factory instead of direct DbContext
		private readonly ILogger<ChatPollingService> logger;
		private readonly IConfiguration configuration; // Inject IConfiguration to get settings
		private readonly DynamicChannelManagerService dynamicChannelManager; // Inject DynamicChannelManagerService
		private Timer? timer; // Nullable timer
		private int pollingIntervalSeconds;
		private long lastProcessedChatId = 0; // Keep track of the last processed chat entity ID
		private readonly ulong? defaultGuildId; // To store the configured default guild ID

		// Constructor with dependency injection
		public ChatPollingService(
			DiscordSocketClient discordClient,
			NpgsqlDbContextFactory dbContextFactory,
			ILogger<ChatPollingService> logger,
			IConfiguration configuration,
			DynamicChannelManagerService dynamicChannelManager)
		{
			this.discordClient = discordClient;
			this.dbContextFactory = dbContextFactory;
			this.logger = logger;
			this.configuration = configuration;
			this.dynamicChannelManager = dynamicChannelManager;

			// Retrieve polling interval from appsettings.json
			if (!int.TryParse(configuration["ChatPollingIntervalSeconds"], out pollingIntervalSeconds))
			{
				pollingIntervalSeconds = 5; // Default to 5 seconds if not found or invalid
				logger.LogWarning("ChatPollingIntervalSeconds not found or invalid in appsettings.json. Defaulting to {DefaultInterval} seconds.", pollingIntervalSeconds);
			}
			logger.LogInformation("ChatPollingService initialized with polling interval: {PollingInterval} seconds.", pollingIntervalSeconds);

			// Read the DefaultGuildId from configuration
			if (ulong.TryParse(configuration.GetSection("Discord")["DefaultGuildId"], out ulong parsedDefaultGuildId))
			{
				this.defaultGuildId = parsedDefaultGuildId;
			}
			else
			{
				this.defaultGuildId = null; // No default guild ID configured
				logger.LogWarning("DefaultGuildId not found or invalid in appsettings.json Discord section. Dynamic channel creation from game chat may fail.");
			}
		}

		public Task StartPolling()
		{
			return StartAsync(CancellationToken.None);
		}

		public Task StopPolling()
		{
			return StopAsync(CancellationToken.None);
		}

		public Task StartAsync(CancellationToken cancellationToken)
		{
			logger.LogInformation("ChatPollingService is starting.");
			timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromSeconds(pollingIntervalSeconds));
			return Task.CompletedTask;
		}

		private async void DoWork(object? state)
		{
			//logger.LogDebug("ChatPollingService is performing work (polling).");

			if (discordClient.ConnectionState != ConnectionState.Connected)
			{
				logger.LogWarning("Discord client is not connected. Skipping chat polling.");
				return;
			}

			try
			{
				using (var dbContext = dbContextFactory.CreateDbContext())
				{
					if (lastProcessedChatId == 0)
					{
						logger.LogDebug("lastProcessedChatId is 0. Initializing by getting highest existing chat ID.");
						var highestId = await dbContext.Chat
														.AsQueryable()
														.OrderByDescending(c => c.ID)
														.Select(c => c.ID)
														.FirstOrDefaultAsync();
						lastProcessedChatId = highestId;
						logger.LogInformation("Initialized lastProcessedChatId to {LastProcessedId}.", lastProcessedChatId);
					}

					var newChatMessages = await dbContext.Chat
														 .AsQueryable()
														 .Where(c => c.ID > lastProcessedChatId)
														 .Where(c => c.Channel != (byte)ChatChannel.Discord)
														 .OrderBy(c => c.ID)
														 .ToListAsync();

					if (newChatMessages.Count > 0)
					{
						// Update lastProcessedChatId immediately after fetching the batch.
						// This ensures that the next poll starts from the latest message ID
						// fetched, even if processing of the current batch is slow or fails.
						lastProcessedChatId = newChatMessages.Last().ID;
						logger.LogInformation("Last processed chat ID updated to {LastProcessedId} after fetching new batch.", lastProcessedChatId);

						logger.LogInformation("Found {Count} new chat messages to process.", newChatMessages.Count);

						foreach (var chatMessage in newChatMessages)
						{
							logger.LogDebug("Processing chat message ID: {ChatId}, Content: {Message}", chatMessage.ID, chatMessage.Message);

							DynamicGameChatChannelState? channelState = null;
							if (this.defaultGuildId.HasValue && this.defaultGuildId.Value != 0)
							{
								channelState = dynamicChannelManager.GetManagedChannelState(
									this.defaultGuildId.Value,
									chatMessage.WorldServerID,
									chatMessage.SceneServerID);
							}
							else
							{
								logger.LogError("No valid DefaultGuildId configured in appsettings.json. Cannot check for existing Discord channel for World {WorldId}, Scene {SceneId}.", chatMessage.WorldServerID, chatMessage.SceneServerID);
								continue;
							}

							if (channelState == null)
							{
								logger.LogWarning("Discord channel not found in cache for WorldServerId {WorldId}, SceneServerId {SceneId} in Guild {GuildId}. Attempting to create it.", chatMessage.WorldServerID, chatMessage.SceneServerID, this.defaultGuildId.Value);

								var worldServer = await dbContext.WorldServers.AsQueryable()
																  .FirstOrDefaultAsync(ws => ws.ID == chatMessage.WorldServerID);
								var sceneServer = await dbContext.SceneServers.AsQueryable()
																  .FirstOrDefaultAsync(ss => ss.ID == chatMessage.SceneServerID);

								if (worldServer == null)
								{
									logger.LogError("WorldServer with ID {WorldId} not found in database. Cannot create Discord channel.", chatMessage.WorldServerID);
									continue;
								}
								if (sceneServer == null)
								{
									logger.LogError("SceneServer with ID {SceneId} not found in database. Cannot create Discord channel.", chatMessage.SceneServerID);
									continue;
								}

								channelState = await dynamicChannelManager.GetOrCreateChannelState(
									this.defaultGuildId.Value,
									chatMessage.WorldServerID,
									worldServer.Name,
									chatMessage.SceneServerID,
									sceneServer.Name);

								if (channelState == null)
								{
									logger.LogError("Failed to create Discord channel for World {WorldId}, Scene {SceneId}. Skipping message forwarding.", chatMessage.WorldServerID, chatMessage.SceneServerID);
									continue;
								}
							}

							if (channelState != null)
							{
								// Fetch Character Name
								string characterName = "System"; // Default for system messages or if character not found
								if (chatMessage.CharacterID != 0) // Assuming CharacterID 0 implies a system message
								{
									var character = await dbContext.Characters.AsQueryable()
																	  .FirstOrDefaultAsync(c => c.ID == chatMessage.CharacterID);
									if (character != null)
									{
										characterName = character.Name;
									}
									else
									{
										logger.LogWarning("Character with ID {CharacterId} not found in database for chat message ID {ChatMessageId}. Using 'Unknown Character'.", chatMessage.CharacterID, chatMessage.ID);
										characterName = "Unknown Character";
									}
								}

								if (chatMessage.Message.StartsWith($"{chatMessage.WorldServerID} "))
								{
									chatMessage.Message = chatMessage.Message.Substring($"{chatMessage.WorldServerID} ".Length).Trim();
								}

								var discordChannel = discordClient.GetChannel(channelState.DiscordChannelId) as IMessageChannel;
								if (discordChannel != null)
								{
									await discordChannel.SendMessageAsync($"[{chatMessage.TimeCreated:HH:mm:ss}] [{((ChatChannel)chatMessage.Channel).ToString()}] {characterName}: {chatMessage.Message}");
									logger.LogInformation("Sent game chat '{Message}' from '{CharacterName}' to Discord channel ID {ChannelId}.", chatMessage.Message, characterName, channelState.DiscordChannelId);

									await dynamicChannelManager.UpdateChannelActivityAsync(channelState.DiscordCategoryId, chatMessage.WorldServerID, chatMessage.SceneServerID);
								}
								else
								{
									logger.LogWarning("Target Discord channel with ID {ChannelId} for World {WorldId}/Scene {SceneId} not found or not a message channel (Guild: {GuildId}).", channelState.DiscordChannelId, chatMessage.WorldServerID, chatMessage.SceneServerID, this.defaultGuildId.Value);
								}
							}
							else
							{
								logger.LogWarning("Could not determine or create target Discord channel for game chat from WorldServerId {WorldId}, SceneServerId {SceneId}. Message not forwarded to Discord. (Final Fallback)", chatMessage.WorldServerID, chatMessage.SceneServerID);
							}
						}
					}
					else
					{
						//logger.LogDebug("No new chat messages found in the database.");
					}
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error occurred during chat polling in ChatPollingService.");
				// If an exception occurs, lastProcessedChatId is NOT updated, so these messages will be re-attempted.
			}
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			logger.LogInformation("ChatPollingService is stopping.");
			timer?.Change(Timeout.Infinite, 0);
			return Task.CompletedTask;
		}

		public void Dispose()
		{
			timer?.Dispose();
			logger.LogInformation("ChatPollingService disposed.");
		}
	}
}