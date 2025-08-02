using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for transmitting a chat message.
	/// Contains the chat channel, sender ID, and message text.
	/// </summary>
	public struct ChatBroadcast : IBroadcast
	{
		/// <summary>Channel where the message is sent (e.g., global, party, guild).</summary>
		public ChatChannel Channel;
		/// <summary>Unique ID of the sender character.</summary>
		public long SenderID;
		/// <summary>Text content of the chat message.</summary>
		public string Text;
	}
}