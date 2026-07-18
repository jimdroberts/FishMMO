using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity representing a character's guild membership data in the database.
	/// </summary>
	public class CharacterGuildEntity : IVersionedEntity
	{
		/// <summary>
		/// Primary key.
		/// </summary>
		public long ID { get; set; }
		/// <summary>
		/// Application-level concurrency token. Incremented on every write to detect stale updates.
		/// </summary>
		public long Version { get; set; }
		/// <summary>
		/// Foreign key to the owning character.
		/// </summary>
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		/// <summary>
		/// Foreign key to the guild.
		/// </summary>
		public long GuildID { get; set; }
		public GuildEntity Guild { get; set; }
		/// <summary>
		/// The character's rank within the guild.
		/// </summary>
		public byte Rank { get; set; }
		/// <summary>
		/// The character's current location within the guild context.
		/// </summary>
		public string Location { get; set; }
		public DateTime TimeCreated { get; set; }
	}
}