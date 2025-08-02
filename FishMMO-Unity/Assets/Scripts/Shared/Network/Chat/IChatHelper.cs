namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for chat helper functionality, providing methods for handling chat commands and processing messages for each chat channel.
	/// </summary>
	public interface IChatHelper
	{
		/// <summary>
		/// Gets the chat command delegate for a specific chat channel.
		/// </summary>
		/// <param name="channel">Chat channel to get the command for.</param>
		/// <returns>ChatCommand delegate for the channel.</returns>
		ChatCommand GetChannelCommand(ChatChannel channel);

		/// <summary>
		/// Handles a world chat message.
		/// </summary>
		/// <param name="sender">Sender character.</param>
		/// <param name="msg">Chat message broadcast.</param>
		/// <returns>True if the message should be written to the database.</returns>
		bool OnWorldChat(IPlayerCharacter sender, ChatBroadcast msg);

		/// <summary>
		/// Handles a region chat message.
		/// </summary>
		/// <param name="sender">Sender character.</param>
		/// <param name="msg">Chat message broadcast.</param>
		/// <returns>True if the message should be written to the database.</returns>
		bool OnRegionChat(IPlayerCharacter sender, ChatBroadcast msg);

		/// <summary>
		/// Handles a party chat message.
		/// </summary>
		/// <param name="sender">Sender character.</param>
		/// <param name="msg">Chat message broadcast.</param>
		/// <returns>True if the message should be written to the database.</returns>
		bool OnPartyChat(IPlayerCharacter sender, ChatBroadcast msg);

		/// <summary>
		/// Handles a guild chat message.
		/// </summary>
		/// <param name="sender">Sender character.</param>
		/// <param name="msg">Chat message broadcast.</param>
		/// <returns>True if the message should be written to the database.</returns>
		bool OnGuildChat(IPlayerCharacter sender, ChatBroadcast msg);

		/// <summary>
		/// Handles a tell (private) chat message.
		/// </summary>
		/// <param name="sender">Sender character.</param>
		/// <param name="msg">Chat message broadcast.</param>
		/// <returns>True if the message should be written to the database.</returns>
		bool OnTellChat(IPlayerCharacter sender, ChatBroadcast msg);

		/// <summary>
		/// Handles a trade chat message.
		/// </summary>
		/// <param name="sender">Sender character.</param>
		/// <param name="msg">Chat message broadcast.</param>
		/// <returns>True if the message should be written to the database.</returns>
		bool OnTradeChat(IPlayerCharacter sender, ChatBroadcast msg);

		/// <summary>
		/// Handles a say (local) chat message.
		/// </summary>
		/// <param name="sender">Sender character.</param>
		/// <param name="msg">Chat message broadcast.</param>
		/// <returns>True if the message should be written to the database.</returns>
		bool OnSayChat(IPlayerCharacter sender, ChatBroadcast msg);
	}
}