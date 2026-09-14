using System;

namespace FishMMO.DiscordBot.Data
{
	/// <summary>
	/// A Discord link as the bot used to store it in botdata.json.
	/// </summary>
	/// <remarks>
	/// Links now live in the database (the account's Discord user column), where a verified Discord code
	/// and a <c>/link</c> are the same link. This type survives only so old botdata.json files still
	/// deserialize: <see cref="FishMMO.DiscordBot.Services.AccountLinkingService.ImportLegacyLinksAsync"/>
	/// moves each entry into the database on startup and removes it from the file.
	/// </remarks>
	public class LinkedAccount
	{
		/// <summary>Discord user snowflake ID.</summary>
		public ulong DiscordUserId { get; set; }

		/// <summary>Game account name linked to this Discord user.</summary>
		public string GameAccountName { get; set; } = string.Empty;

		/// <summary>Primary character name used during the link.</summary>
		public string CharacterName { get; set; } = string.Empty;

		/// <summary>When the link was confirmed.</summary>
		public DateTime LinkedAtUtc { get; set; }
	}
}
