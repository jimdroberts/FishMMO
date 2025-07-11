using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Discord.Rest;
using FishMMO.DiscordBot.Data; // Ensure this is correct for DynamicGameChatChannelState
using FishMMO.Database.Npgsql; // Added for NpgsqlDbContextFactory
using Microsoft.EntityFrameworkCore; // Added for AsQueryable() and FirstOrDefaultAsync

namespace FishMMO.DiscordBot.Services
{
	// Represents the state of a dynamically created Discord channel linked to a game scene.
	public class DynamicGameChatChannelState
	{
		public ulong DiscordCategoryId { get; set; }
		public ulong DiscordChannelId { get; set; }
		public long WorldServerId { get; set; }
		public string WorldServerName { get; set; }
		public long SceneServerId { get; set; }
		public string SceneServerName { get; set; }
		public DateTime LastActivity { get; set; } // UTC timestamp of last message/activity
	}

	public class DynamicChannelManagerService
	{
		private readonly DiscordSocketClient discord;
		private readonly ILogger<DynamicChannelManagerService> logger;
		private readonly BotConfigurationService botConfigService;
		private readonly NpgsqlDbContextFactory dbContextFactory; // Added NpgsqlDbContextFactory
		private ConcurrentDictionary<ulong, Dictionary<long, Dictionary<long, DynamicGameChatChannelState>>> managedChannels; // GuildId -> WorldId -> SceneId -> State
		private Timer? cleanupTimer;
		private const int CleanupIntervalMinutes = 30; // How often to check for stale channels
		private const int InactivityThresholdMinutes = 120; // Channels inactive for this long will be cleaned up
															// Updated Regex to capture the name and the ID, assuming format "Name-ID"
		private readonly Regex channelNameRegex = new Regex(@"^(.+?)-(\d+)$", RegexOptions.Compiled);
		private readonly Regex categoryNameRegex = new Regex(@"^(.+?)-(\d+)$", RegexOptions.Compiled);

		public DynamicChannelManagerService(
			DiscordSocketClient discord,
			ILogger<DynamicChannelManagerService> logger,
			BotConfigurationService botConfigService,
			NpgsqlDbContextFactory dbContextFactory) // Injected NpgsqlDbContextFactory
		{
			this.discord = discord;
			this.logger = logger;
			this.botConfigService = botConfigService;
			this.dbContextFactory = dbContextFactory; // Initialized NpgsqlDbContextFactory
			managedChannels = botConfigService.GetDynamicChannelStates(); // Get reference to the shared state
		}

		public async Task LoadManagedChannelsAsync()
		{
			// This is primarily handled by BotConfigurationService.LoadConfigurationsAsync()
			// We just ensure our internal reference is correct.
			managedChannels = botConfigService.GetDynamicChannelStates();
			logger.LogInformation("DynamicChannelManagerService loaded {Count} managed channels from configuration.", managedChannels.Sum(g => g.Value.Sum(w => w.Value.Count)));
		}

		public void StartCleanupTask()
		{
			cleanupTimer = new Timer(CleanupStaleChannels, null, TimeSpan.Zero, TimeSpan.FromMinutes(CleanupIntervalMinutes));
			logger.LogInformation("Dynamic channel cleanup task started. Running every {Interval} minutes, cleaning up channels inactive for {Threshold} minutes.", CleanupIntervalMinutes, InactivityThresholdMinutes);
		}

		public void StopCleanupTask()
		{
			cleanupTimer?.Change(Timeout.Infinite, 0);
			logger.LogInformation("Dynamic channel cleanup task stopped.");
		}

		private async void CleanupStaleChannels(object? state)
		{
			logger.LogDebug("Running stale channel cleanup...");
			List<DynamicGameChatChannelState> channelsToDelete = new List<DynamicGameChatChannelState>();
			DateTime cutoff = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(InactivityThresholdMinutes));

			foreach (var guildEntry in managedChannels)
			{
				ulong guildId = guildEntry.Key;
				foreach (var worldEntry in guildEntry.Value)
				{
					long worldId = worldEntry.Key;
					foreach (var sceneEntry in worldEntry.Value)
					{
						long sceneId = sceneEntry.Key;
						DynamicGameChatChannelState channelState = sceneEntry.Value;

						if (channelState.LastActivity < cutoff)
						{
							channelsToDelete.Add(channelState);
							logger.LogInformation("Identified stale channel for deletion: Guild {GuildId}, World {WorldId}, Scene {SceneId}.", guildId, worldId, sceneId);
						}
					}
				}
			}

			foreach (var channelState in channelsToDelete)
			{
				try
				{
					var guild = discord.GetGuild(channelState.DiscordCategoryId);
					if (guild == null)
					{
						guild = discord.Guilds.FirstOrDefault(g => g.Id == channelState.DiscordCategoryId);
					}

					if (guild == null)
					{
						logger.LogWarning("Guild not found for stale channel cleanup. GuildID (from category or direct): {GuildId}", channelState.DiscordCategoryId);
						RemoveChannelStateFromConfig(channelState.DiscordCategoryId, channelState.WorldServerId, channelState.SceneServerId);
						continue;
					}

					var channel = guild.GetTextChannel(channelState.DiscordChannelId);
					if (channel != null)
					{
						await channel.DeleteAsync();
						logger.LogInformation("Deleted stale Discord channel: {ChannelName} (ID: {ChannelId}) from Guild {GuildId}.", channel.Name, channel.Id, guild.Id);
					}
					else
					{
						logger.LogWarning("Discord channel with ID {ChannelId} not found in Guild {GuildId} for cleanup. Removing from config only.", channelState.DiscordChannelId, guild.Id);
					}

					RemoveChannelStateFromConfig(channelState.DiscordCategoryId, channelState.WorldServerId, channelState.SceneServerId);
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Error during cleanup of Discord channel ID {ChannelId}.", channelState.DiscordChannelId);
				}
			}

			if (channelsToDelete.Count > 0)
			{
				await botConfigService.SaveConfigurationsAsync();
			}
			logger.LogDebug("Stale channel cleanup completed. {Count} channels removed.", channelsToDelete.Count);
		}

		private void RemoveChannelStateFromConfig(ulong guildId, long worldServerId, long sceneServerId)
		{
			if (managedChannels.TryGetValue(guildId, out var guildWorlds))
			{
				if (guildWorlds.TryGetValue(worldServerId, out var worldScenes))
				{
					if (worldScenes.Remove(sceneServerId))
					{
						logger.LogDebug("Removed channel state for Guild {GuildId}, World {WorldId}, Scene {SceneId} from in-memory cache.", guildId, worldServerId, sceneServerId);
					}
					if (worldScenes.Count == 0)
					{
						guildWorlds.Remove(worldServerId);
						logger.LogDebug("Removed World {WorldId} from Guild {GuildId} as it has no more scenes.", worldServerId, guildId);
					}
				}
				if (guildWorlds.Count == 0)
				{
					((ConcurrentDictionary<ulong, Dictionary<long, Dictionary<long, DynamicGameChatChannelState>>>)managedChannels).TryRemove(guildId, out _);
					logger.LogDebug("Removed Guild {GuildId} as it has no more worlds.", guildId);
				}
			}
		}

		// Method to get or create a Discord channel for a given game world/scene
		public async Task<DynamicGameChatChannelState> GetOrCreateChannelState(
			ulong guildId, long worldServerId, string? worldServerName, long sceneServerId, string? sceneServerName) // Made names nullable
		{
			var guild = discord.GetGuild(guildId);
			if (guild == null)
			{
				logger.LogError("Guild with ID {GuildId} not found. Cannot create/get channel.", guildId);
				return null;
			}

			// Check if already managed
			if (managedChannels.TryGetValue(guildId, out var guildWorlds) &&
				guildWorlds.TryGetValue(worldServerId, out var worldScenes) &&
				worldScenes.TryGetValue(sceneServerId, out var existingState))
			{
				logger.LogDebug("Found existing channel state in managed cache for Guild {GuildId}, World {WorldId}, Scene {SceneId}.", guildId, worldServerId, sceneServerId);
				existingState.LastActivity = DateTime.UtcNow;
				botConfigService.UpdateDynamicChannelState(guildId, worldServerId, sceneServerId, existingState);
				return existingState;
			}

			string actualWorldServerName = worldServerName;
			string actualSceneServerName = sceneServerName;

			using (var dbContext = dbContextFactory.CreateDbContext()) // Create a new DbContext for this operation
			{
				if (string.IsNullOrWhiteSpace(actualWorldServerName))
				{
					var worldEntity = await dbContext.WorldServers.AsQueryable()
																  .FirstOrDefaultAsync(ws => ws.ID == worldServerId);
					if (worldEntity != null)
					{
						actualWorldServerName = worldEntity.Name;
						logger.LogDebug("Fetched WorldServerName from DB: {WorldName} for ID {WorldId}", actualWorldServerName, worldServerId);
					}
					else
					{
						logger.LogError("WorldServer with ID {WorldId} not found in database. Cannot create channel with proper name.", worldServerId);
						actualWorldServerName = $"UnknownWorld"; // Fallback name
					}
				}

				if (string.IsNullOrWhiteSpace(actualSceneServerName))
				{
					var sceneEntity = await dbContext.SceneServers.AsQueryable()
																  .FirstOrDefaultAsync(ss => ss.ID == sceneServerId);
					if (sceneEntity != null)
					{
						actualSceneServerName = sceneEntity.Name;
						logger.LogDebug("Fetched SceneServerName from DB: {SceneName} for ID {SceneId}", actualSceneServerName, sceneServerId);
					}
					else
					{
						logger.LogError("SceneServer with ID {SceneId} not found in database. Cannot create channel with proper name.", sceneServerId);
						actualSceneServerName = $"UnknownScene"; // Fallback name
					}
				}
			}

			// If not found in cache, proceed to check Discord and potentially create
			// Use actual names fetched/provided
			var categoryName = $"{actualWorldServerName}-{worldServerId}";
			SocketCategoryChannel socketCategory = guild.CategoryChannels.FirstOrDefault(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));
			RestCategoryChannel restCategory = null;

			if (socketCategory != null)
			{
				logger.LogInformation("Found existing SocketCategoryChannel '{CategoryName}' (ID: {CategoryId}). Attempting to fetch its REST equivalent for permissions.", socketCategory.Name, socketCategory.Id);
				var fetchedChannel = await discord.Rest.GetChannelAsync(socketCategory.Id);
				if (fetchedChannel is RestCategoryChannel fetchedRestCategory)
				{
					restCategory = fetchedRestCategory;
				}
				else
				{
					logger.LogWarning("Fetched channel for category ID {CategoryId} was not a RestCategoryChannel.", socketCategory.Id);
				}
			}

			if (restCategory == null)
			{
				logger.LogInformation("Creating new category '{CategoryName}' in Guild {GuildId} for World {WorldId}.", categoryName, guildId, worldServerId);
				restCategory = await guild.CreateCategoryChannelAsync(categoryName);
			}

			await restCategory.AddPermissionOverwriteAsync(guild.EveryoneRole, new OverwritePermissions(sendMessages: PermValue.Deny, viewChannel: PermValue.Allow));
			logger.LogInformation("Category '{CategoryName}' (ID: {CategoryId}) created/found and permissions set (Send: Deny, View: Allow for @everyone).", restCategory.Name, restCategory.Id);


			// Use actual names fetched/provided
			var channelName = $"{actualSceneServerName}-{sceneServerId}";
			logger.LogInformation("Creating new channel '{ChannelName}' in category '{CategoryName}' (ID: {CategoryId}).", channelName, restCategory.Name, restCategory.Id);
			RestTextChannel textChannel = await guild.CreateTextChannelAsync(channelName, props => props.CategoryId = restCategory.Id);

			await textChannel.AddPermissionOverwriteAsync(guild.EveryoneRole, new OverwritePermissions(sendMessages: PermValue.Allow, viewChannel: PermValue.Allow));
			logger.LogInformation("Channel '{ChannelName}' (ID: {ChannelId}) created and permissions set (Send: Allow, View: Allow for @everyone).", textChannel.Name, textChannel.Id);

			var newState = new DynamicGameChatChannelState
			{
				DiscordCategoryId = restCategory.Id,
				DiscordChannelId = textChannel.Id,
				WorldServerId = worldServerId,
				WorldServerName = actualWorldServerName, // Use the actual (fetched/provided) name
				SceneServerId = sceneServerId,
				SceneServerName = actualSceneServerName, // Use the actual (fetched/provided) name
				LastActivity = DateTime.UtcNow
			};

			botConfigService.UpdateDynamicChannelState(guildId, worldServerId, sceneServerId, newState);
			await botConfigService.SaveConfigurationsAsync();

			logger.LogInformation("Created and managed new Discord channel: {ChannelName} (ID: {ChannelId}) in Category {CategoryName} (ID: {CategoryId}) for World {WorldId}, Scene {SceneId}.",
				textChannel.Name, textChannel.Id, restCategory.Name, restCategory.Id, worldServerId, sceneServerId);

			return newState;
		}

		// Retrieves the managed channel state for a given World/Scene combination within a specific guild.
		public DynamicGameChatChannelState? GetManagedChannelState(ulong guildId, long worldServerId, long sceneServerId)
		{
			if (managedChannels.TryGetValue(guildId, out var guildWorlds))
			{
				if (guildWorlds.TryGetValue(worldServerId, out var worldScenes))
				{
					if (worldScenes.TryGetValue(sceneServerId, out var channelState))
					{
						logger.LogDebug("GetManagedChannelState found existing channel for Guild {GuildId}, World {WorldId}, Scene {SceneId}.", guildId, worldServerId, sceneServerId);
						return channelState;
					}
				}
			}
			logger.LogDebug("GetManagedChannelState did NOT find existing channel for Guild {GuildId}, World {WorldId}, Scene {SceneId}.", guildId, worldServerId, sceneServerId);
			return null;
		}


		// Method to check if a Discord channel is one of our dynamically managed channels (by iterating all managed guilds)
		public bool IsOurDynamicChannel(ulong guildId, ulong channelId)
		{
			if (managedChannels.TryGetValue(guildId, out var guildWorlds))
			{
				foreach (var worldEntry in guildWorlds.Values)
				{
					foreach (var sceneEntry in worldEntry.Values)
					{
						if (sceneEntry.DiscordChannelId == channelId)
						{
							return true;
						}
					}
				}
			}
			return false;
		}

		// Extracts World and Scene IDs from a Discord channel's name and its category's name
		public (long? WorldId, long? SceneId) GetWorldAndSceneIdsFromChannel(SocketTextChannel channel)
		{
			long? worldId = null;
			long? sceneId = null;

			// Try to get world ID from category name (e.g., "WorldName-1")
			if (channel.Category != null)
			{
				Match categoryMatch = categoryNameRegex.Match(channel.Category.Name);
				if (categoryMatch.Success && long.TryParse(categoryMatch.Groups[2].Value, out long parsedWorldId)) // Group 2 for ID
				{
					worldId = parsedWorldId;
					// You could also extract the name here: string worldName = categoryMatch.Groups[1].Value;
				}
				else
				{
					logger.LogWarning("Category name '{CategoryName}' for channel '{ChannelName}' does not match expected pattern 'Name-ID'.", channel.Category.Name, channel.Name);
				}
			}
			else
			{
				logger.LogWarning("Channel '{ChannelName}' does not have a category. Cannot extract World ID.", channel.Name);
			}

			// Try to get scene ID from channel name (e.g., "SceneName-101")
			Match channelMatch = channelNameRegex.Match(channel.Name);
			if (channelMatch.Success && long.TryParse(channelMatch.Groups[2].Value, out long parsedSceneId)) // Group 2 for ID
			{
				sceneId = parsedSceneId;
				// You could also extract the name here: string sceneName = channelMatch.Groups[1].Value;
			}
			else
			{
				logger.LogWarning("Channel name '{ChannelName}' does not match expected pattern 'Name-ID'.", channel.Name);
			}

			return (worldId, sceneId);
		}

		// Method to update LastActivity timestamp for a channel
		public async Task UpdateChannelActivityAsync(ulong guildId, long worldServerId, long sceneServerId)
		{
			if (managedChannels.TryGetValue(guildId, out var guildWorlds) &&
				guildWorlds.TryGetValue(worldServerId, out var worldScenes) &&
				worldScenes.TryGetValue(sceneServerId, out var channelState))
			{
				channelState.LastActivity = DateTime.UtcNow;
				botConfigService.UpdateDynamicChannelState(guildId, worldServerId, sceneServerId, channelState); // Notify config service of update
				await botConfigService.SaveConfigurationsAsync(); // Save changes
				logger.LogDebug("Updated last activity for channel Guild {GuildId}, World {WorldId}, Scene {SceneId}.", guildId, worldServerId, sceneServerId);
			}
			else
			{
				logger.LogWarning("Attempted to update activity for unmanaged channel: Guild {GuildId}, World {WorldId}, Scene {SceneId}. Channel may not exist or is not managed.", guildId, worldServerId, sceneServerId);
				// Optionally, if it's not found, try to create it here or log a critical error if it should exist.
			}
		}
	}
}