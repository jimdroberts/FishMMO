using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Database entity for one guild activity log row.
	/// </summary>
	/// <remarks>
	/// Append-only. Nothing updates a row once written, so the table carries no version column —
	/// the optimistic-concurrency machinery the rest of the schema uses exists to arbitrate
	/// competing writes to the same row, and there are none here.
	/// </remarks>
	public class GuildLogEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>Foreign key to the guild.</summary>
		public long GuildID { get; set; }

		/// <summary>Navigation to the owning guild.</summary>
		public GuildEntity Guild { get; set; }

		/// <summary>The event kind, as <c>GuildLogEventType</c>.</summary>
		public byte EventType { get; set; }

		/// <summary>The character who performed the action, or zero.</summary>
		public long ActorCharacterID { get; set; }

		/// <summary>The character the action was performed on, or zero.</summary>
		public long TargetCharacterID { get; set; }

		/// <summary>Optional short detail, such as a rank name.</summary>
		public string Detail { get; set; }

		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
	}
}
