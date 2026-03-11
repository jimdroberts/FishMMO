using System.Collections.Generic;

namespace FishMMO.DiscordBot.Data
{
	/// <summary>
	/// Container for persistent bot data that is saved alongside the dynamic channel states.
	/// Serialized to/from botdata.json.
	/// </summary>
	public class BotPersistentData
	{
		/// <summary>Discord user ID -> linked game account.</summary>
		public Dictionary<ulong, LinkedAccount> LinkedAccounts { get; set; } = new Dictionary<ulong, LinkedAccount>();

		/// <summary>Lowercased name -> bridge ban entry.</summary>
		public Dictionary<string, BridgeBanEntry> BridgeBans { get; set; } = new Dictionary<string, BridgeBanEntry>();

		/// <summary>Discord user ID -> set of muted Discord channel IDs.</summary>
		public Dictionary<ulong, HashSet<ulong>> MutedZones { get; set; } = new Dictionary<ulong, HashSet<ulong>>();

		/// <summary>Guild ID -> command permission configuration.</summary>
		public Dictionary<ulong, CommandPermissionConfig> CommandPermissions { get; set; } = new Dictionary<ulong, CommandPermissionConfig>();
	}
}
