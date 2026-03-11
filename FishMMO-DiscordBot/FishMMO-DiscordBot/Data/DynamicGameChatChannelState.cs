using System;

namespace FishMMO.DiscordBot.Data
{
	/// <summary>
	/// Persistent state for a dynamically created Discord channel linked to a game world/scene.
	/// Serialized to and from botconfig.json by <see cref="FishMMO.DiscordBot.Services.BotConfigurationService"/>.
	/// </summary>
	public class DynamicGameChatChannelState
	{
		/// <summary>
		/// The Discord category channel snowflake that groups channels for a game world.
		/// </summary>
		public ulong DiscordCategoryId { get; set; }

		/// <summary>
		/// The Discord text channel snowflake for this game scene's chat bridge.
		/// </summary>
		public ulong DiscordChannelId { get; set; }

		/// <summary>
		/// The game world server database ID.
		/// </summary>
		public long WorldServerId { get; set; }

		/// <summary>
		/// The display name of the game world server.
		/// </summary>
		public string WorldServerName { get; set; } = string.Empty;

		/// <summary>
		/// The game scene server database ID.
		/// </summary>
		public long SceneServerId { get; set; }

		/// <summary>
		/// The display name of the game scene server.
		/// </summary>
		public string SceneServerName { get; set; } = string.Empty;

		/// <summary>
		/// UTC timestamp of the last message or activity on this channel.
		/// Used by cleanup to determine stale channels.
		/// </summary>
		public DateTime LastActivity { get; set; }
	}
}