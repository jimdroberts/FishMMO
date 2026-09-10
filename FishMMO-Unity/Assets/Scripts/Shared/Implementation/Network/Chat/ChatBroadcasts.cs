using FishNet.Broadcast;
using FishNet.Serializing;

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
		/// for legal audit persistence. Not set by clients, and never serialized in either
		/// direction — see <see cref="ChatBroadcastSerializer"/>.
		/// </summary>
		public long ReceivedUtcTicks;
	}

	/// <summary>
	/// Hand written wire format for <see cref="ChatBroadcast"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Exists for one reason: to keep <see cref="ChatBroadcast.ReceivedUtcTicks"/> off the wire.
	/// </para>
	/// <para>
	/// The field is server-internal. <c>ChatSystem.OnServerChatBroadcastReceived</c> stamps it at
	/// the network boundary and it is read only by the spam token bucket and the audit
	/// persistence, all server side — no client has ever read it. But the server re-broadcasts the
	/// same struct it received, so the generated serializer put a full <c>DateTime.UtcNow.Ticks</c>
	/// on every outbound message: roughly 6.4e17, which zigzags to 61 significant bits and costs
	/// NINE varint bytes. On a short line of say chat that was about half the packet, paid to every
	/// observer of the sender, on the most fanned-out message in the game.
	/// </para>
	/// <para>
	/// Not written and not read, rather than zeroed at each fanout site: there are more than twenty
	/// send sites and a new one would silently reintroduce the cost. Leaving it out of the format
	/// makes the field's own documentation true in both directions — a client cannot assert a
	/// receive time it did not observe, and the server always stamps its own.
	/// </para>
	/// <para>
	/// Discovered by FishNet's codegen through the <c>Write*</c>/<c>Read*</c> naming convention.
	/// </para>
	/// </remarks>
	public static class ChatBroadcastSerializer
	{
		/// <summary>Writes a <see cref="ChatBroadcast"/>, omitting the server-only timestamp.</summary>
		public static void WriteChatBroadcast(this Writer writer, ChatBroadcast value)
		{
			writer.WriteUInt8Unpacked((byte)value.Channel);
			writer.WriteInt64(value.SenderID);
			writer.WriteString(value.Text);
		}

		/// <summary>Reads a <see cref="ChatBroadcast"/>. <c>ReceivedUtcTicks</c> comes back zero.</summary>
		public static ChatBroadcast ReadChatBroadcast(this Reader reader)
		{
			return new ChatBroadcast()
			{
				Channel = (ChatChannel)reader.ReadUInt8Unpacked(),
				SenderID = reader.ReadInt64(),
				Text = reader.ReadStringAllocated(),
			};
		}
	}
}
