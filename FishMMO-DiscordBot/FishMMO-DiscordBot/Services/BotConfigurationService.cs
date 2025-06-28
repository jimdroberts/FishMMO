using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FishMMO.DiscordBot.Data;

namespace FishMMO.DiscordBot.Services
{
	// This class is assumed to handle loading/saving general bot configurations,
	// including the DynamicChannelStates.
	public class BotConfigurationService
	{
		private readonly ILogger<BotConfigurationService> logger;
		private readonly string configFilePath = "botconfig.json"; // Path to your dynamic config file
		private ConcurrentDictionary<ulong, Dictionary<long, Dictionary<long, DynamicGameChatChannelState>>> dynamicChannelStates;

		public BotConfigurationService(ILogger<BotConfigurationService> logger)
		{
			this.logger = logger;
			dynamicChannelStates = new ConcurrentDictionary<ulong, Dictionary<long, Dictionary<long, DynamicGameChatChannelState>>>();
		}

		public async Task LoadConfigurationsAsync()
		{
			logger.LogInformation("Loading bot configurations from {ConfigFilePath}...", configFilePath);
			if (File.Exists(configFilePath))
			{
				try
				{
					string json = await File.ReadAllTextAsync(configFilePath);
					// Temporarily use a plain dictionary for deserialization, then convert
					var loadedStates = JsonConvert.DeserializeObject<Dictionary<ulong, Dictionary<long, Dictionary<long, DynamicGameChatChannelState>>>>(json);
					if (loadedStates != null)
					{
						dynamicChannelStates = new ConcurrentDictionary<ulong, Dictionary<long, Dictionary<long, DynamicGameChatChannelState>>>(loadedStates);
						logger.LogInformation("Successfully loaded bot configurations from {ConfigFilePath}.", configFilePath);
					}
					else
					{
						logger.LogWarning("Loaded botconfig.json was empty or invalid. Starting with empty configuration.");
						dynamicChannelStates = new ConcurrentDictionary<ulong, Dictionary<long, Dictionary<long, DynamicGameChatChannelState>>>();
					}
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Error loading bot configurations from {ConfigFilePath}. Starting with empty configuration.", configFilePath);
					dynamicChannelStates = new ConcurrentDictionary<ulong, Dictionary<long, Dictionary<long, DynamicGameChatChannelState>>>();
				}
			}
			else
			{
				logger.LogWarning("Bot configuration file {ConfigFilePath} not found. Starting with empty configuration.", configFilePath);
				dynamicChannelStates = new ConcurrentDictionary<ulong, Dictionary<long, Dictionary<long, DynamicGameChatChannelState>>>();
			}
		}

		public async Task SaveConfigurationsAsync()
		{
			logger.LogInformation("Saving bot configurations to {ConfigFilePath}...", configFilePath);
			try
			{
				string json = JsonConvert.SerializeObject(dynamicChannelStates, Formatting.Indented);
				await File.WriteAllTextAsync(configFilePath, json);
				logger.LogInformation("Successfully saved bot configurations to {ConfigFilePath}.", configFilePath);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error saving bot configurations to {ConfigFilePath}.", configFilePath);
			}
		}

		// Method to get the current state for direct access by DynamicChannelManagerService
		public ConcurrentDictionary<ulong, Dictionary<long, Dictionary<long, DynamicGameChatChannelState>>> GetDynamicChannelStates()
		{
			return dynamicChannelStates;
		}

		// Method to update a channel state (called by DynamicChannelManagerService)
		// Refactored to use standard Dictionary operations for nested Dictionaries
		public void UpdateDynamicChannelState(ulong guildId, long worldServerId, long sceneServerId, DynamicGameChatChannelState state)
		{
			// Get or add the top-level (guild) dictionary
			var guildWorlds = dynamicChannelStates.GetOrAdd(
				guildId,
				new Dictionary<long, Dictionary<long, DynamicGameChatChannelState>>());

			// Lock to ensure thread-safe modification of the inner dictionaries
			// ConcurrentDictionary only handles its direct children concurrently
			lock (guildWorlds) // Lock on the specific guild's dictionary
			{
				// Get or add the second-level (world) dictionary
				if (!guildWorlds.TryGetValue(worldServerId, out var worldScenes))
				{
					worldScenes = new Dictionary<long, DynamicGameChatChannelState>();
					guildWorlds[worldServerId] = worldScenes; // Add the new worldScenes dictionary
				}

				// Update or add the third-level (scene) state
				worldScenes[sceneServerId] = state;
			}
			logger.LogDebug("Updated dynamic channel state for Guild {GuildId}, World {WorldId}, Scene {SceneId}.", guildId, worldServerId, sceneServerId);
		}
	}
}