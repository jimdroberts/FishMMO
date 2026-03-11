using System.Collections.Generic;

namespace FishMMO.DiscordBot.Data
{
	/// <summary>
	/// Per-guild command permission configuration.
	/// Allows moderators to disable specific commands or require a Discord role to use them.
	/// Command keys are lowercase "group command" (e.g. "mod kick") or just "command" for ungrouped commands.
	/// </summary>
	public class CommandPermissionConfig
	{
		/// <summary>Set of disabled command keys. Commands in this set cannot be used in the guild.</summary>
		public HashSet<string> DisabledCommands { get; set; } = new HashSet<string>();

		/// <summary>Command key -> required Discord role ID. Users must have the role to use the command.</summary>
		public Dictionary<string, ulong> RoleRequirements { get; set; } = new Dictionary<string, ulong>();
	}
}
