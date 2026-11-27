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
		/// <summary>
		/// The kit template of the mercenary this member has seated in the party, or 0 for none (the Mercenaries module).
		/// </summary>
		/// <remarks>
		/// A hired mercenary takes a party seat, and a seat is counted wherever members are: the capacity checks count
		/// every row plus every row with a seated mercenary. It lives on its owner's row because a mercenary is not a
		/// character and has no row of its own, and so that the owner leaving (the row deleted) takes the seat with it.
		/// At most one row of a party has one.
		/// </remarks>
		public int MercenaryKitID { get; set; }
		/// <summary>The seated mercenary's name, for the party frame of members who cannot see it; empty for none.</summary>
		public string MercenaryName { get; set; }
	}
}