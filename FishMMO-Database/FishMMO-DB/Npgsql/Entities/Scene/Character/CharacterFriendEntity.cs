using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Database entity representing a character friend/block relationship.
	/// </summary>
	public class CharacterFriendEntity : IVersionedEntity
	{
		/// <summary>
		/// Primary key.
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// Logical version for optimistic concurrency control.
		/// </summary>
		public long Version { get; set; }

		/// <summary>
		/// Owning character identifier.
		/// </summary>
		public long CharacterID { get; set; }

		/// <summary>
		/// Navigation property to the owning character.
		/// </summary>
		public CharacterEntity Character { get; set; }

		/// <summary>
		/// Target character identifier.
		/// </summary>
		public long FriendCharacterID { get; set; }

		/// <summary>
		/// When false this is a friend relationship; when true the target is blocked.
		/// </summary>
		public bool IsBlocked { get; set; }

		/// <summary>
		/// Row creation timestamp (UTC).
		/// </summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>
		/// Soft-delete flag.
		/// </summary>
		public bool Deleted { get; set; }

		/// <summary>
		/// Soft-delete timestamp (UTC), null when not deleted.
		/// </summary>
		public DateTime? TimeDeleted { get; set; }
	}
}