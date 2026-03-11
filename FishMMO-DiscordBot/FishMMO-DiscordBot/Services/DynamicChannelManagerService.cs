using Discord;
using Discord.WebSocket;
using Discord.Rest;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.DiscordBot.Data;
using FishMMO.Database.Npgsql;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.DiscordBot.Services
{
	/// <summary>
	/// Manages dynamically created Discord channels that bridge game world/scene chat to Discord.
	/// Implements <see cref="IHostedService"/> for lifecycle management of the cleanup timer.
	/// All inner dictionaries are <see cref="ConcurrentDictionary{TKey,TValue}"/> for thread safety.
	/// </summary>
	public class DynamicChannelManagerService : IHostedService, IDisposable
	{
		private readonly DiscordSocketClient discord;
		private readonly ILogger<DynamicChannelManagerService> logger;
		private readonly BotConfigurationService botConfigService;
		private readonly NpgsqlDbContextFactory dbContextFactory;

		/// <summary>GuildId -> WorldId -> SceneId -> State. All levels are thread-safe.</summary>
		private ConcurrentDictionary<ulong, ConcurrentDictionary<long, ConcurrentDictionary<long, DynamicGameChatChannelState>>> managedChannels;

		/// <summary>Reverse lookup: DiscordChannelId -> (GuildId, WorldId, SceneId).</summary>
		private readonly ConcurrentDictionary<ulong, (ulong GuildId, long WorldId, long SceneId)> channelIdLookup;

		/// <summary>Serializes channel creation to prevent duplicate Discord channels from race conditions.</summary>
		private readonly SemaphoreSlim createChannelLock = new SemaphoreSlim(1, 1);

		/// <summary>Prevents overlapping cleanup timer callbacks.</summary>
		private readonly SemaphoreSlim cleanupLock = new SemaphoreSlim(1, 1);

		private Timer? cleanupTimer;
		private int disposed;

		/// <summary>
		/// How often the stale-channel cleanup task runs, in minutes.
		/// </summary>
		private const int CleanupIntervalMinutes = 30;

		/// <summary>
		/// Channels inactive for longer than this threshold (in minutes) are deleted.
		/// </summary>
		private const int InactivityThresholdMinutes = 120;

		/// <summary>
		/// Matches channel and category names in the format "Name-ID".
		/// </summary>
		private static readonly Regex IdSuffixRegex = new Regex(@"^(.+?)-(\d+)$", RegexOptions.Compiled);

		/// <summary>
		/// Returns the total number of managed channels across all guilds.
		/// </summary>
		public int TotalManagedChannelCount => channelIdLookup.Count;

		/// <summary>
		/// Initializes a new instance of the <see cref="DynamicChannelManagerService"/> class.
		/// </summary>
		/// <param name="discord">The Discord socket client.</param>
		/// <param name="logger">Logger instance.</param>
		/// <param name="botConfigService">Configuration service for persistent channel state.</param>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		public DynamicChannelManagerService(
			DiscordSocketClient discord,
			ILogger<DynamicChannelManagerService> logger,
			BotConfigurationService botConfigService,
			NpgsqlDbContextFactory dbContextFactory)
		{
			this.discord = discord;
			this.logger = logger;
			this.botConfigService = botConfigService;
			this.dbContextFactory = dbContextFactory;
			channelIdLookup = new ConcurrentDictionary<ulong, (ulong, long, long)>();
			managedChannels = botConfigService.GetDynamicChannelStates();
		}

		/// <inheritdoc />
		public async Task StartAsync(CancellationToken cancellationToken)
		{
			await botConfigService.LoadConfigurationsAsync();
			managedChannels = botConfigService.GetDynamicChannelStates();
			RebuildReverseLookup();

			cleanupTimer = new Timer(
				OnCleanupTimerElapsed,
				null,
				TimeSpan.FromMinutes(CleanupIntervalMinutes),
				TimeSpan.FromMinutes(CleanupIntervalMinutes));

			logger.LogInformation(
				"DynamicChannelManagerService started. Cleanup every {Interval} min, inactivity threshold {Threshold} min.",
				CleanupIntervalMinutes,
				InactivityThresholdMinutes);
		}

		/// <inheritdoc />
		public Task StopAsync(CancellationToken cancellationToken)
		{
			cleanupTimer?.Change(Timeout.Infinite, 0);
			logger.LogInformation("DynamicChannelManagerService stopped.");
			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
			{
				cleanupTimer?.Dispose();
				createChannelLock.Dispose();
				cleanupLock.Dispose();
			}
		}

		/// <summary>
		/// Rebuilds the reverse lookup dictionary from the current managed channels state.
		/// </summary>
		private void RebuildReverseLookup()
		{
			channelIdLookup.Clear();
			foreach (var guildEntry in managedChannels)
			{
				ulong guildId = guildEntry.Key;
				foreach (var worldEntry in guildEntry.Value)
				{
					long worldId = worldEntry.Key;
					foreach (var sceneEntry in worldEntry.Value)
					{
						long sceneId = sceneEntry.Key;
						channelIdLookup[sceneEntry.Value.DiscordChannelId] = (guildId, worldId, sceneId);
					}
				}
			}
			logger.LogInformation("Rebuilt reverse channel lookup with {Count} entries.", channelIdLookup.Count);
		}

		/// <summary>
		/// Timer callback wrapper with reentrancy guard. Skips execution if a previous cleanup is still running.
		/// </summary>
		private async void OnCleanupTimerElapsed(object? state)
		{
			if (!cleanupLock.Wait(0))
			{
				logger.LogDebug("Skipping cleanup — previous cleanup is still running.");
				return;
			}

			try
			{
				await CleanupStaleChannelsAsync();
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Unhandled exception during stale channel cleanup.");
			}
			finally
			{
				cleanupLock.Release();
			}
		}

		/// <summary>
		/// Removes Discord channels that have been inactive beyond the threshold.
		/// Also cleans up empty parent category channels.
		/// </summary>
		/// <returns>The number of channels removed.</returns>
		private async Task<int> CleanupStaleChannelsAsync()
		{
			logger.LogDebug("Running stale channel cleanup...");
			var channelsToDelete = new List<(ulong GuildId, long WorldId, long SceneId, DynamicGameChatChannelState State)>();
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
							channelsToDelete.Add((guildId, worldId, sceneId, channelState));
							logger.LogInformation(
								"Identified stale channel for deletion: Guild {GuildId}, World {WorldId}, Scene {SceneId}.",
								guildId, worldId, sceneId);
						}
					}
				}
			}

			var emptyCategoryIds = new HashSet<ulong>();

			foreach (var (guildId, worldId, sceneId, channelState) in channelsToDelete)
			{
				try
				{
					var guild = discord.GetGuild(guildId);
					if (guild == null)
					{
						logger.LogWarning(
							"Guild {GuildId} not found for stale channel cleanup. Removing from config only.",
							guildId);
						RemoveChannelState(guildId, worldId, sceneId, channelState.DiscordChannelId);
						continue;
					}

					var channel = guild.GetTextChannel(channelState.DiscordChannelId);
					if (channel != null)
					{
						await channel.DeleteAsync();
						logger.LogInformation(
							"Deleted stale Discord channel: {ChannelName} (ID: {ChannelId}) from Guild {GuildId}.",
							channel.Name, channel.Id, guild.Id);
					}
					else
					{
						logger.LogWarning(
							"Discord channel ID {ChannelId} not found in Guild {GuildId} for cleanup. Removing from config only.",
							channelState.DiscordChannelId, guild.Id);
					}

					RemoveChannelState(guildId, worldId, sceneId, channelState.DiscordChannelId);

					if (channelState.DiscordCategoryId != 0)
					{
						emptyCategoryIds.Add(channelState.DiscordCategoryId);
					}
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Error during cleanup of Discord channel ID {ChannelId}.", channelState.DiscordChannelId);
				}
			}

			// Clean up empty parent categories
			foreach (ulong categoryId in emptyCategoryIds)
			{
				await TryDeleteEmptyCategoryAsync(categoryId);
			}

			if (channelsToDelete.Count > 0)
			{
				await botConfigService.SaveConfigurationsAsync();
			}

			logger.LogDebug("Stale channel cleanup completed. {Count} channels removed.", channelsToDelete.Count);
			return channelsToDelete.Count;
		}

		/// <summary>
		/// Forces an immediate cleanup of stale channels. Called from admin commands.
		/// </summary>
		/// <returns>The number of channels removed.</returns>
		public async Task<int> ForceCleanupAsync()
		{
			await cleanupLock.WaitAsync();
			try
			{
				return await CleanupStaleChannelsAsync();
			}
			finally
			{
				cleanupLock.Release();
			}
		}

		/// <summary>
		/// Deletes a Discord category channel if it has no remaining child channels.
		/// </summary>
		/// <param name="categoryId">The Discord category channel snowflake.</param>
		private async Task TryDeleteEmptyCategoryAsync(ulong categoryId)
		{
			try
			{
				var fetchedChannel = await discord.Rest.GetChannelAsync(categoryId);
				if (fetchedChannel is RestCategoryChannel restCategory)
				{
					// Check if any managed channel still references this category
					bool hasChildren = false;
					foreach (var guildEntry in managedChannels)
					{
						foreach (var worldEntry in guildEntry.Value)
						{
							foreach (var sceneEntry in worldEntry.Value)
							{
								if (sceneEntry.Value.DiscordCategoryId == categoryId)
								{
									hasChildren = true;
									break;
								}
							}
							if (hasChildren) break;
						}
						if (hasChildren) break;
					}

					if (!hasChildren)
					{
						await restCategory.DeleteAsync();
						logger.LogInformation(
							"Deleted empty category channel: {CategoryName} (ID: {CategoryId}).",
							restCategory.Name, restCategory.Id);
					}
				}
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Failed to delete potentially empty category ID {CategoryId}.", categoryId);
			}
		}

		/// <summary>
		/// Removes a channel state entry from the in-memory cache and reverse lookup.
		/// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/> operations.
		/// </summary>
		/// <param name="guildId">The Discord guild ID.</param>
		/// <param name="worldServerId">The game world server ID.</param>
		/// <param name="sceneServerId">The game scene server ID.</param>
		/// <param name="discordChannelId">The Discord channel snowflake to remove from reverse lookup.</param>
		private void RemoveChannelState(ulong guildId, long worldServerId, long sceneServerId, ulong discordChannelId)
		{
			channelIdLookup.TryRemove(discordChannelId, out _);

			if (managedChannels.TryGetValue(guildId, out var guildWorlds))
			{
				if (guildWorlds.TryGetValue(worldServerId, out var worldScenes))
				{
					worldScenes.TryRemove(sceneServerId, out _);
					if (worldScenes.IsEmpty)
					{
						guildWorlds.TryRemove(worldServerId, out _);
					}
				}
				if (guildWorlds.IsEmpty)
				{
					managedChannels.TryRemove(guildId, out _);
				}
			}

			logger.LogDebug(
				"Removed channel state for Guild {GuildId}, World {WorldId}, Scene {SceneId}.",
				guildId, worldServerId, sceneServerId);
		}

		/// <summary>
		/// Gets or creates a Discord channel for a given game world/scene combination.
		/// Creates the Discord category and text channel if they do not already exist.
		/// Serialized via <see cref="createChannelLock"/> to prevent duplicate channel creation.
		/// </summary>
		/// <param name="guildId">The Discord guild in which to manage channels.</param>
		/// <param name="worldServerId">The game world server ID.</param>
		/// <param name="worldServerName">The world server name (fetched from DB if null or empty).</param>
		/// <param name="sceneServerId">The game scene server ID.</param>
		/// <param name="sceneServerName">The scene server name (fetched from DB if null or empty).</param>
		/// <returns>The channel state, or <c>null</c> if the guild was not found.</returns>
		public async Task<DynamicGameChatChannelState?> GetOrCreateChannelState(
			ulong guildId,
			long worldServerId,
			string? worldServerName,
			long sceneServerId,
			string? sceneServerName)
		{
			// Fast path: check without lock
			if (managedChannels.TryGetValue(guildId, out var guildWorlds) &&
				guildWorlds.TryGetValue(worldServerId, out var worldScenes) &&
				worldScenes.TryGetValue(sceneServerId, out var existingState))
			{
				existingState.LastActivity = DateTime.UtcNow;
				return existingState;
			}

			var guild = discord.GetGuild(guildId);
			if (guild == null)
			{
				logger.LogError("Guild with ID {GuildId} not found. Cannot create/get channel.", guildId);
				return null;
			}

			await createChannelLock.WaitAsync();
			try
			{
				// Double-check after acquiring lock to avoid duplicate creation
				if (managedChannels.TryGetValue(guildId, out guildWorlds) &&
					guildWorlds.TryGetValue(worldServerId, out worldScenes) &&
					worldScenes.TryGetValue(sceneServerId, out existingState))
				{
					existingState.LastActivity = DateTime.UtcNow;
					return existingState;
				}

				string actualWorldServerName = worldServerName ?? string.Empty;
				string actualSceneServerName = sceneServerName ?? string.Empty;

				using (var dbContext = dbContextFactory.CreateDbContext())
				{
					if (string.IsNullOrWhiteSpace(actualWorldServerName))
					{
						var worldEntity = await dbContext.WorldServers.AsQueryable()
							.FirstOrDefaultAsync(ws => ws.ID == worldServerId);
						actualWorldServerName = worldEntity?.Name ?? "UnknownWorld";
					}

					if (string.IsNullOrWhiteSpace(actualSceneServerName))
					{
						var sceneEntity = await dbContext.SceneServers.AsQueryable()
							.FirstOrDefaultAsync(ss => ss.ID == sceneServerId);
						actualSceneServerName = sceneEntity?.Name ?? "UnknownScene";
					}
				}

				var categoryName = $"{actualWorldServerName}-{worldServerId}";
				SocketCategoryChannel? socketCategory = null;
				foreach (var cat in guild.CategoryChannels)
				{
					if (cat.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
					{
						socketCategory = cat;
						break;
					}
				}

				RestCategoryChannel? restCategory = null;

				if (socketCategory != null)
				{
					var fetchedChannel = await discord.Rest.GetChannelAsync(socketCategory.Id);
					if (fetchedChannel is RestCategoryChannel fetchedRestCategory)
					{
						restCategory = fetchedRestCategory;
					}
				}

				if (restCategory == null)
				{
					logger.LogInformation(
						"Creating new category '{CategoryName}' in Guild {GuildId} for World {WorldId}.",
						categoryName, guildId, worldServerId);
					restCategory = await guild.CreateCategoryChannelAsync(categoryName);
					await restCategory.AddPermissionOverwriteAsync(
						guild.EveryoneRole,
						new OverwritePermissions(sendMessages: PermValue.Deny, viewChannel: PermValue.Allow));
				}

				var channelName = $"{actualSceneServerName}-{sceneServerId}";
				RestTextChannel textChannel = await guild.CreateTextChannelAsync(
					channelName,
					props => props.CategoryId = restCategory.Id);

				await textChannel.AddPermissionOverwriteAsync(
					guild.EveryoneRole,
					new OverwritePermissions(sendMessages: PermValue.Allow, viewChannel: PermValue.Allow));

				var newState = new DynamicGameChatChannelState
				{
					DiscordCategoryId = restCategory.Id,
					DiscordChannelId = textChannel.Id,
					WorldServerId = worldServerId,
					WorldServerName = actualWorldServerName,
					SceneServerId = sceneServerId,
					SceneServerName = actualSceneServerName,
					LastActivity = DateTime.UtcNow
				};

				botConfigService.UpdateDynamicChannelState(guildId, worldServerId, sceneServerId, newState);
				channelIdLookup[textChannel.Id] = (guildId, worldServerId, sceneServerId);
				await botConfigService.SaveConfigurationsAsync();

				logger.LogInformation(
					"Created Discord channel: {ChannelName} (ID: {ChannelId}) in Category {CategoryName} (ID: {CategoryId}) for World {WorldId}, Scene {SceneId}.",
					textChannel.Name, textChannel.Id, restCategory.Name, restCategory.Id, worldServerId, sceneServerId);

				return newState;
			}
			finally
			{
				createChannelLock.Release();
			}
		}

		/// <summary>
		/// Retrieves the managed channel state for a given World/Scene combination within a guild.
		/// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/> reads.
		/// </summary>
		/// <param name="guildId">The Discord guild ID.</param>
		/// <param name="worldServerId">The game world server ID.</param>
		/// <param name="sceneServerId">The game scene server ID.</param>
		/// <returns>The channel state, or <c>null</c> if not managed.</returns>
		public DynamicGameChatChannelState? GetManagedChannelState(ulong guildId, long worldServerId, long sceneServerId)
		{
			if (managedChannels.TryGetValue(guildId, out var guildWorlds) &&
				guildWorlds.TryGetValue(worldServerId, out var worldScenes) &&
				worldScenes.TryGetValue(sceneServerId, out var channelState))
			{
				return channelState;
			}
			return null;
		}

		/// <summary>
		/// Returns a snapshot list of all managed channels for the specified guild.
		/// </summary>
		/// <param name="guildId">The Discord guild ID.</param>
		/// <returns>A list of tuples containing WorldId, SceneId, and the channel state.</returns>
		public List<(long WorldId, long SceneId, DynamicGameChatChannelState State)> GetManagedChannelsForGuild(ulong guildId)
		{
			var result = new List<(long, long, DynamicGameChatChannelState)>();
			if (managedChannels.TryGetValue(guildId, out var guildWorlds))
			{
				foreach (var worldEntry in guildWorlds)
				{
					foreach (var sceneEntry in worldEntry.Value)
					{
						result.Add((worldEntry.Key, sceneEntry.Key, sceneEntry.Value));
					}
				}
			}
			return result;
		}

		/// <summary>
		/// Checks whether a Discord channel is one of the dynamically managed game chat channels.
		/// Uses O(1) reverse lookup instead of scanning all channels.
		/// </summary>
		/// <param name="guildId">The Discord guild ID.</param>
		/// <param name="channelId">The Discord channel ID to check.</param>
		/// <returns><c>true</c> if the channel is managed by this service.</returns>
		public bool IsOurDynamicChannel(ulong guildId, ulong channelId)
		{
			return channelIdLookup.TryGetValue(channelId, out var entry) && entry.GuildId == guildId;
		}

		/// <summary>
		/// Extracts World and Scene IDs from a Discord channel's name and its category's name.
		/// Expects the format "Name-ID" for both category (World) and channel (Scene).
		/// </summary>
		/// <param name="channel">The Discord text channel to extract IDs from.</param>
		/// <returns>A tuple of (WorldId, SceneId), either of which may be null on parse failure.</returns>
		public (long? WorldId, long? SceneId) GetWorldAndSceneIdsFromChannel(SocketTextChannel channel)
		{
			// Fast path: use reverse lookup if available
			if (channelIdLookup.TryGetValue(channel.Id, out var entry))
			{
				return (entry.WorldId, entry.SceneId);
			}

			long? worldId = null;
			long? sceneId = null;

			if (channel.Category != null)
			{
				Match categoryMatch = IdSuffixRegex.Match(channel.Category.Name);
				if (categoryMatch.Success && long.TryParse(categoryMatch.Groups[2].Value, out long parsedWorldId))
				{
					worldId = parsedWorldId;
				}
				else
				{
					logger.LogWarning(
						"Category name '{CategoryName}' for channel '{ChannelName}' does not match expected pattern 'Name-ID'.",
						channel.Category.Name, channel.Name);
				}
			}
			else
			{
				logger.LogWarning("Channel '{ChannelName}' does not have a category. Cannot extract World ID.", channel.Name);
			}

			Match channelMatch = IdSuffixRegex.Match(channel.Name);
			if (channelMatch.Success && long.TryParse(channelMatch.Groups[2].Value, out long parsedSceneId))
			{
				sceneId = parsedSceneId;
			}
			else
			{
				logger.LogWarning("Channel name '{ChannelName}' does not match expected pattern 'Name-ID'.", channel.Name);
			}

			return (worldId, sceneId);
		}

		/// <summary>
		/// Updates the last-activity timestamp for a managed channel.
		/// Does not persist to disk — saves are batched during cleanup or channel creation.
		/// </summary>
		/// <param name="guildId">The Discord guild ID.</param>
		/// <param name="worldServerId">The game world server ID.</param>
		/// <param name="sceneServerId">The game scene server ID.</param>
		public void UpdateChannelActivity(ulong guildId, long worldServerId, long sceneServerId)
		{
			if (managedChannels.TryGetValue(guildId, out var guildWorlds) &&
				guildWorlds.TryGetValue(worldServerId, out var worldScenes) &&
				worldScenes.TryGetValue(sceneServerId, out var channelState))
			{
				channelState.LastActivity = DateTime.UtcNow;
			}
		}
	}
}