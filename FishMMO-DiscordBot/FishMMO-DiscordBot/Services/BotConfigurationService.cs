using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FishMMO.DiscordBot.Data;

namespace FishMMO.DiscordBot.Services
{
	/// <summary>
	/// Manages loading and saving of persistent bot configuration,
	/// including the dynamic channel states mapping and persistent bot data
	/// (linked accounts, bridge bans, muted zones).
	/// All inner dictionaries use <see cref="ConcurrentDictionary{TKey,TValue}"/> for thread safety.
	/// </summary>
	public class BotConfigurationService
	{
		private readonly ILogger<BotConfigurationService> logger;
		private readonly string configFilePath = "botconfig.json";
		private readonly string dataFilePath = "botdata.json";
		private ConcurrentDictionary<ulong, ConcurrentDictionary<long, ConcurrentDictionary<long, DynamicGameChatChannelState>>> dynamicChannelStates;
		private BotPersistentData persistentData;

		/// <summary>
		/// Initializes a new instance of the <see cref="BotConfigurationService"/> class.
		/// </summary>
		/// <param name="logger">Logger instance.</param>
		public BotConfigurationService(ILogger<BotConfigurationService> logger)
		{
			this.logger = logger;
			dynamicChannelStates = new ConcurrentDictionary<ulong, ConcurrentDictionary<long, ConcurrentDictionary<long, DynamicGameChatChannelState>>>();
			persistentData = new BotPersistentData();
		}

		/// <summary>
		/// Returns the persistent bot data (linked accounts, bridge bans, muted zones).
		/// </summary>
		internal BotPersistentData GetPersistentData()
		{
			return persistentData;
		}

		/// <summary>
		/// Saves the persistent bot data to disk.
		/// </summary>
		public async Task SavePersistentDataAsync()
		{
			logger.LogDebug("Saving persistent bot data to {DataFilePath}...", dataFilePath);
			try
			{
				string json = JsonConvert.SerializeObject(persistentData, Formatting.Indented);
				string tempPath = dataFilePath + ".tmp";
				await File.WriteAllTextAsync(tempPath, json);
				File.Move(tempPath, dataFilePath, overwrite: true);
				logger.LogDebug("Successfully saved persistent bot data to {DataFilePath}.", dataFilePath);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error saving persistent bot data to {DataFilePath}.", dataFilePath);
			}
		}

		/// <summary>
		/// Loads persistent bot data from disk.
		/// </summary>
		private async Task LoadPersistentDataAsync()
		{
			logger.LogInformation("Loading persistent bot data from {DataFilePath}...", dataFilePath);
			if (File.Exists(dataFilePath))
			{
				try
				{
					string json = await File.ReadAllTextAsync(dataFilePath);
					var loaded = JsonConvert.DeserializeObject<BotPersistentData>(json);
					if (loaded != null)
					{
						persistentData = loaded;
						logger.LogInformation(
							"Loaded persistent data: {LinkCount} linked accounts, {BanCount} bridge bans, {MuteCount} muted zone users.",
							persistentData.LinkedAccounts.Count,
							persistentData.BridgeBans.Count,
							persistentData.MutedZones.Count);
					}
					else
					{
						logger.LogWarning("Persistent data file was empty or invalid. Starting with defaults.");
						persistentData = new BotPersistentData();
					}
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Error loading persistent data from {DataFilePath}. Starting with defaults.", dataFilePath);
					persistentData = new BotPersistentData();
				}
			}
			else
			{
				logger.LogWarning("Persistent data file {DataFilePath} not found. Starting with defaults.", dataFilePath);
				persistentData = new BotPersistentData();
			}
		}

		/// <summary>
		/// Loads dynamic channel state from the JSON configuration file on disk.
		/// If the file does not exist or is invalid, starts with an empty state.
		/// </summary>
		public async Task LoadConfigurationsAsync()
		{
			logger.LogInformation("Loading bot configurations from {ConfigFilePath}...", configFilePath);
			await LoadPersistentDataAsync();
			if (File.Exists(configFilePath))
			{
				try
				{
					string json = await File.ReadAllTextAsync(configFilePath);
					var loadedStates = JsonConvert.DeserializeObject<
						Dictionary<ulong, Dictionary<long, Dictionary<long, DynamicGameChatChannelState>>>>(json);

					if (loadedStates != null)
					{
						var converted = new ConcurrentDictionary<ulong, ConcurrentDictionary<long, ConcurrentDictionary<long, DynamicGameChatChannelState>>>();
						foreach (var guildEntry in loadedStates)
						{
							var worldDict = new ConcurrentDictionary<long, ConcurrentDictionary<long, DynamicGameChatChannelState>>();
							foreach (var worldEntry in guildEntry.Value)
							{
								worldDict[worldEntry.Key] = new ConcurrentDictionary<long, DynamicGameChatChannelState>(worldEntry.Value);
							}
							converted[guildEntry.Key] = worldDict;
						}
						dynamicChannelStates = converted;
						logger.LogInformation("Loaded bot configurations from {ConfigFilePath}.", configFilePath);
					}
					else
					{
						logger.LogWarning("Loaded botconfig.json was empty or invalid. Starting with empty configuration.");
						dynamicChannelStates = new ConcurrentDictionary<ulong, ConcurrentDictionary<long, ConcurrentDictionary<long, DynamicGameChatChannelState>>>();
					}
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Error loading bot configurations from {ConfigFilePath}. Starting with empty configuration.", configFilePath);
					dynamicChannelStates = new ConcurrentDictionary<ulong, ConcurrentDictionary<long, ConcurrentDictionary<long, DynamicGameChatChannelState>>>();
				}
			}
			else
			{
				logger.LogWarning("Bot configuration file {ConfigFilePath} not found. Starting with empty configuration.", configFilePath);
			}
		}

		/// <summary>
		/// Persists the current dynamic channel state to the JSON configuration file.
		/// Uses atomic write-to-temp-then-rename to prevent corruption on crash.
		/// </summary>
		public async Task SaveConfigurationsAsync()
		{
			logger.LogDebug("Saving bot configurations to {ConfigFilePath}...", configFilePath);
			try
			{
				string json = JsonConvert.SerializeObject(dynamicChannelStates, Formatting.Indented);
				string tempPath = configFilePath + ".tmp";
				await File.WriteAllTextAsync(tempPath, json);
				File.Move(tempPath, configFilePath, overwrite: true);
				logger.LogDebug("Successfully saved bot configurations to {ConfigFilePath}.", configFilePath);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error saving bot configurations to {ConfigFilePath}.", configFilePath);
			}
		}

		/// <summary>
		/// Returns a reference to the live dynamic channel states dictionary.
		/// All levels are thread-safe <see cref="ConcurrentDictionary{TKey,TValue}"/>.
		/// Callers must not replace entries without using <see cref="UpdateDynamicChannelState"/>.
		/// </summary>
		/// <returns>The thread-safe channel state dictionary.</returns>
		internal ConcurrentDictionary<ulong, ConcurrentDictionary<long, ConcurrentDictionary<long, DynamicGameChatChannelState>>> GetDynamicChannelStates()
		{
			return dynamicChannelStates;
		}

		/// <summary>
		/// Gets the command permission configuration for a guild, or null if none exists.
		/// </summary>
		internal CommandPermissionConfig? GetCommandPermissionConfig(ulong guildId)
		{
			persistentData.CommandPermissions.TryGetValue(guildId, out var config);
			return config;
		}

		/// <summary>
		/// Gets or creates the command permission configuration for a guild.
		/// </summary>
		internal CommandPermissionConfig GetOrCreateCommandPermissionConfig(ulong guildId)
		{
			if (!persistentData.CommandPermissions.TryGetValue(guildId, out var config))
			{
				config = new CommandPermissionConfig();
				persistentData.CommandPermissions[guildId] = config;
			}
			return config;
		}

		/// <summary>
		/// Updates or inserts a channel state entry for the given guild/world/scene combination.
		/// Thread-safe for concurrent access from multiple services.
		/// </summary>
		/// <param name="guildId">The Discord guild ID.</param>
		/// <param name="worldServerId">The game world server ID.</param>
		/// <param name="sceneServerId">The game scene server ID.</param>
		/// <param name="state">The channel state to store.</param>
		public void UpdateDynamicChannelState(ulong guildId, long worldServerId, long sceneServerId, DynamicGameChatChannelState state)
		{
			var guildWorlds = dynamicChannelStates.GetOrAdd(
				guildId,
				_ => new ConcurrentDictionary<long, ConcurrentDictionary<long, DynamicGameChatChannelState>>());

			var worldScenes = guildWorlds.GetOrAdd(
				worldServerId,
				_ => new ConcurrentDictionary<long, DynamicGameChatChannelState>());

			worldScenes[sceneServerId] = state;

			logger.LogDebug("Updated dynamic channel state for Guild {GuildId}, World {WorldId}, Scene {SceneId}.", guildId, worldServerId, sceneServerId);
		}
	}
}