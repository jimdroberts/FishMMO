using System;

namespace FishMMO.DiscordBot.Data
{
	/// <summary>
	/// Represents a ban preventing a game character or account from using the Discord chat bridge.
	/// </summary>
	public class BridgeBanEntry
	{
		/// <summary>The banned identifier (character name or account name).</summary>
		public string Name { get; set; } = string.Empty;

		/// <summary>Whether this bans an account name (true) or character name (false).</summary>
		public bool IsAccountBan { get; set; }

		/// <summary>Discord user who issued the ban.</summary>
		public string BannedBy { get; set; } = string.Empty;

		/// <summary>Reason for the ban.</summary>
		public string Reason { get; set; } = string.Empty;

		/// <summary>When the ban was created.</summary>
		public DateTime CreatedAtUtc { get; set; }
	}
}
