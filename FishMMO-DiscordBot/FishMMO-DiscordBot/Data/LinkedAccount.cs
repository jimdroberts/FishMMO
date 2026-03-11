using System;

namespace FishMMO.DiscordBot.Data
{
	/// <summary>
	/// Represents a confirmed link between a Discord user and a game account/character.
	/// </summary>
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
