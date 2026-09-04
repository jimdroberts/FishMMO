using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// A character's arena queue lock: until when, and why.
	/// </summary>
	/// <remarks>
	/// Written when a player deserts a live match or declines a ready check, cleared when a
	/// disconnected player returns within the grace, and read at queue time. One row per character;
	/// a later lock replaces an earlier one.
	/// </remarks>
	public class ArenaPenaltyEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>The character.</summary>
		public long CharacterID { get; set; }
		/// <summary>The character may not queue before this instant (UTC).</summary>
		public DateTime LockedUntilUtc { get; set; }
		/// <summary>Why, for the log and the player.</summary>
		public string Reason { get; set; }
		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
	}
}
