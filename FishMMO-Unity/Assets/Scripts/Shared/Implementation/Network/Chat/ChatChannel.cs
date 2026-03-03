namespace FishMMO.Shared
{
	/// <summary>
	/// Enum representing the different chat channels available in the game.
	/// </summary>
	public enum ChatChannel : byte
	{
		/// <summary>Local chat, visible to nearby players.</summary>
		Say = 0,
		/// <summary>Global chat, visible to all players in the world.</summary>
		World,
		/// <summary>Region chat, visible to players in the same region.</summary>
		Region,
		/// <summary>Party chat, visible to party members.</summary>
		Party,
		/// <summary>Guild chat, visible to guild members.</summary>
		Guild,
		/// <summary>Private message (tell) between two players.</summary>
		Tell,
		/// <summary>Trade chat, for trading-related messages.</summary>
		Trade,
		/// <summary>System messages, such as notifications or alerts.</summary>
		System,
		/// <summary>Command channel, for entering game commands.</summary>
		Command,
		/// <summary>Discord integration channel.</summary>
		Discord,
	}
}