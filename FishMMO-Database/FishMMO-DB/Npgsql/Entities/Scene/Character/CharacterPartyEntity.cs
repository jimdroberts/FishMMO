using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity representing a character's party membership data in the database.
	/// </summary>
	public class CharacterPartyEntity : IVersionedEntity
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
		/// Foreign key to the party this character belongs to.
		/// </summary>
		public long PartyID { get; set; }
		public PartyEntity Party { get; set; }
		/// <summary>
		/// Rank of the character within the party.
		/// </summary>
		public byte Rank { get; set; }
		public DateTime TimeCreated { get; set; }
		/// <summary>
		/// Health percentage of the character, used for party frame display.
		/// </summary>
		public float HealthPCT { get; set; }
	}
}