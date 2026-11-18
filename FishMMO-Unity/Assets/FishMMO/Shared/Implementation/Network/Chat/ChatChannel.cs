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
		World = 1,
		/// <summary>Region chat, visible to players in the same region.</summary>
		Region = 2,
		/// <summary>Party chat, visible to party members.</summary>
		Party = 3,
		/// <summary>Guild chat, visible to guild members.</summary>
		Guild = 4,
		/// <summary>Private message (tell) between two players.</summary>
		Tell = 5,
		/// <summary>Trade chat, for trading-related messages.</summary>
		Trade = 6,
		/// <summary>System messages, such as notifications or alerts.</summary>
		System = 7,
		/// <summary>Command channel, for entering game commands.</summary>
		Command = 8,
		/// <summary>Discord integration channel.</summary>
		Discord = 9,
		/// <summary>
		/// Arena team chat. Reaches the sender's teammates in the arena match they are standing in,
		/// on the scene server hosting it, and nowhere else. Not persisted: it means nothing once
		/// the match is over.
		/// </summary>
		Team = 10,
	}
}
