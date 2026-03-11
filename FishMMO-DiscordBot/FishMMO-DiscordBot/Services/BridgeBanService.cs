using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using FishMMO.DiscordBot.Data;

namespace FishMMO.DiscordBot.Services
{
	/// <summary>
	/// Manages bridge bans — prevents specific game characters or accounts
	/// from having their messages forwarded between Discord and the game.
	/// </summary>
	public class BridgeBanService
	{
		private readonly BotConfigurationService botConfigService;
		private readonly ILogger<BridgeBanService> logger;

		/// <summary>
		/// Initializes a new instance of the <see cref="BridgeBanService"/> class.
		/// </summary>
		public BridgeBanService(
			BotConfigurationService botConfigService,
			ILogger<BridgeBanService> logger)
		{
			this.botConfigService = botConfigService;
			this.logger = logger;
			logger.LogInformation("BridgeBanService initialized.");
		}

		/// <summary>
		/// Checks whether a character name or account name is bridge-banned.
		/// </summary>
		/// <param name="characterName">The character name to check.</param>
		/// <param name="accountName">The account name to check.</param>
		/// <returns>True if either the character or account is bridge-banned.</returns>
		public bool IsBridgeBanned(string? characterName, string? accountName)
		{
			var bans = botConfigService.GetPersistentData().BridgeBans;

			if (!string.IsNullOrEmpty(characterName) && bans.ContainsKey(characterName.ToLowerInvariant()))
			{
				return true;
			}

			if (!string.IsNullOrEmpty(accountName) && bans.ContainsKey(accountName.ToLowerInvariant()))
			{
				return true;
			}

			return false;
		}

		/// <summary>
		/// Adds a bridge ban for the specified name.
		/// </summary>
		/// <param name="name">The character or account name to ban.</param>
		/// <param name="isAccountBan">True if this bans an account name; false for character name.</param>
		/// <param name="bannedBy">Who issued the ban.</param>
		/// <param name="reason">Reason for the ban.</param>
		/// <returns>True if the ban was added; false if already banned.</returns>
		public bool AddBridgeBan(string name, bool isAccountBan, string bannedBy, string reason)
		{
			string key = name.ToLowerInvariant();
			var bans = botConfigService.GetPersistentData().BridgeBans;

			if (bans.ContainsKey(key))
			{
				return false;
			}

			bans[key] = new BridgeBanEntry
			{
				Name = name,
				IsAccountBan = isAccountBan,
				BannedBy = bannedBy,
				Reason = reason,
				CreatedAtUtc = DateTime.UtcNow
			};

			logger.LogInformation(
				"Bridge ban added: '{Name}' (Account: {IsAccount}) by '{BannedBy}'. Reason: {Reason}",
				name, isAccountBan, bannedBy, reason);

			return true;
		}

		/// <summary>
		/// Removes a bridge ban for the specified name.
		/// </summary>
		/// <param name="name">The character or account name to unban.</param>
		/// <returns>True if the ban was removed; false if not found.</returns>
		public bool RemoveBridgeBan(string name)
		{
			string key = name.ToLowerInvariant();
			var bans = botConfigService.GetPersistentData().BridgeBans;
			bool removed = bans.Remove(key);

			if (removed)
			{
				logger.LogInformation("Bridge ban removed for '{Name}'.", name);
			}

			return removed;
		}

		/// <summary>
		/// Returns all current bridge bans.
		/// </summary>
		public IReadOnlyCollection<BridgeBanEntry> GetAllBans()
		{
			return botConfigService.GetPersistentData().BridgeBans.Values as IReadOnlyCollection<BridgeBanEntry>
				?? new List<BridgeBanEntry>(botConfigService.GetPersistentData().BridgeBans.Values);
		}
	}
}