using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Database entity for one editable guild rank.
	/// </summary>
	/// <remarks>
	/// Replaces the hard-coded <c>GuildRank</c> enum as the source of a member's powers, without
	/// replacing the value a membership row stores: <see cref="RankOrder"/> IS
	/// <c>character_guild.rank</c>. A guild's ladder is the set of rows it owns; the leader is the
	/// member holding the highest <see cref="RankOrder"/> present.
	/// </remarks>
	public class GuildRankEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>Application-level concurrency token. Incremented on every write.</summary>
		public long Version { get; set; }

		/// <summary>Foreign key to the owning guild.</summary>
		public long GuildID { get; set; }

		/// <summary>Navigation to the owning guild.</summary>
		public GuildEntity Guild { get; set; }

		/// <summary>Ordering position. Higher is more senior. Unique within a guild.</summary>
		public byte RankOrder { get; set; }

		/// <summary>Display name.</summary>
		public string Name { get; set; }

		/// <summary>
		/// Permission bit mask, matching the shared <c>GuildPermissions</c> flags enum.
		/// </summary>
		/// <remarks>
		/// <c>bigint</c>, not <c>integer</c>. Widening the column every permission check reads is
		/// not a migration worth running twice, and fourteen flags already fill a third of 32 bits.
		/// </remarks>
		public long Permissions { get; set; }

		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
	}
}
