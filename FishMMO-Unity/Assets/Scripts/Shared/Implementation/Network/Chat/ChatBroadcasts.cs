using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for transmitting a chat message.
	/// Contains the chat channel, sender ID, and message text.
	/// </summary>
	public struct ChatBroadcast : IBroadcast
	{
		/// <summary>Maximum allowed length for the <see cref="Text"/> field in characters.</summary>
		public const int MaxTextLength = 128;
		/// <summary>Channel where the message is sent (e.g., global, party, guild).</summary>
		public ChatChannel Channel;
		/// <summary>Unique ID of the sender character.</summary>
		public long SenderID;
		/// <summary>Text content of the chat message.</summary>
		public string Text;
		/// <summary>
		/// Server-side UTC receive timestamp as ticks. Stamped at the network boundary
		/// for legal audit persistence. Not set by clients; ignored on the wire inbound.
		/// </summary>
		public long ReceivedUtcTicks;
	}
}