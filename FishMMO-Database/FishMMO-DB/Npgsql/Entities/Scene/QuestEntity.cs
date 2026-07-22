using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Persistence entity for quest instances. Quest content (name, description,
	/// objectives, rewards) is defined externally via template/ScriptableObject
	/// assets referenced by <see cref="ID"/>. This table tracks only the fact
	/// that a quest instance exists and when it was created; runtime progress
	/// is tracked separately in character-specific tables.
	/// </summary>
	public class QuestEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Concurrency version for optimistic locking.</summary>
		public long Version { get; set; }
		/// <summary>Display name of the quest.</summary>
		public string Name { get; set; }
		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
	}
}