using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Database entity for one pending application to join a guild.
	/// </summary>
	/// <remarks>
	/// Pending rows only — see <c>GuildApplicationData</c>. The unique index on
	/// <c>(guild_id, character_id)</c> is what enforces "one pending application per guild per
	/// player" at the storage layer rather than in application code, so two applications racing on
	/// two scene servers cannot both land.
	/// </remarks>
	public class GuildApplicationEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>Foreign key to the guild applied to.</summary>
		public long GuildID { get; set; }

		/// <summary>Navigation to the guild applied to.</summary>
		public GuildEntity Guild { get; set; }

		/// <summary>The applying character.</summary>
		public long CharacterID { get; set; }

		/// <summary>Navigation to the applying character.</summary>
		public CharacterEntity Character { get; set; }

		/// <summary>The applicant's message. May be empty.</summary>
		public string Message { get; set; }

		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
	}
}
