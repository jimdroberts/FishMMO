using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Chat message entity representing a persisted chat message from any channel.</summary>
	public class ChatEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Concurrency version for optimistic locking.</summary>
		public long Version { get; set; }
		/// <summary>Character ID of the message sender.</summary>
		public long CharacterID { get; set; }
		/// <summary>Character name of the message sender (denormalized for query convenience).</summary>
		public string CharacterName { get; set; }
		/// <summary>Account name of the message sender.</summary>
		public string AccountName { get; set; }
		/// <summary>World server ID where the message originated.</summary>
		public long WorldServerID { get; set; }
		/// <summary>Scene server ID where the message originated.</summary>
		public long SceneServerID { get; set; }
		/// <summary>UTC timestamp when the server received the message.</summary>
		public DateTime ServerReceivedTime { get; set; }
		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
		/// <summary>Chat channel (e.g. global, local, whisper, guild, party).</summary>
		public byte Channel { get; set; }
		/// <summary>The chat message body text.</summary>
		public string Message { get; set; }
	}
}