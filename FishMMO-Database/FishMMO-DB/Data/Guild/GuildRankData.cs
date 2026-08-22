namespace FishMMO.Database.Data
{
	/// <summary>
	/// One editable guild rank: an ordering position, a display name and a permission mask.
	/// </summary>
	/// <remarks>
	/// <see cref="RankOrder"/> is the same byte <c>character_guild.rank</c> has always stored, so
	/// the move from the hard-coded <c>GuildRank</c> enum to per-guild rank rows rewrites no
	/// membership row. Higher order is more senior; the guild's leader is the member holding the
	/// highest order that exists in that guild.
	///
	/// <see cref="Permissions"/> is the <c>GuildPermissions</c> flag mask, carried as a plain
	/// <c>long</c> because the database project deliberately does not depend on the shared
	/// assembly's enum for storage — the column is a bitfield either way, and the server casts.
	/// </remarks>
	public struct GuildRankData
	{
		/// <summary>Primary key. Zero for a row that has not been written yet.</summary>
		public readonly long ID;

		/// <summary>Application-level concurrency token.</summary>
		public readonly long Version;

		/// <summary>The guild that owns this rank.</summary>
		public readonly long GuildID;

		/// <summary>Ordering position. Higher is more senior. Unique within a guild.</summary>
		public readonly byte RankOrder;

		/// <summary>Display name.</summary>
		public readonly string Name;

		/// <summary>Permission bit mask.</summary>
		public readonly long Permissions;

		/// <summary>
		/// Initializes a new guild rank row.
		/// </summary>
		/// <param name="id">Primary key, or zero when not yet written.</param>
		/// <param name="version">Concurrency token.</param>
		/// <param name="guildID">Owning guild.</param>
		/// <param name="rankOrder">Ordering position.</param>
		/// <param name="name">Display name.</param>
		/// <param name="permissions">Permission bit mask.</param>
		public GuildRankData(long id, long version, long guildID, byte rankOrder, string name, long permissions)
		{
			ID = id;
			Version = version;
			GuildID = guildID;
			RankOrder = rankOrder;
			Name = name;
			Permissions = permissions;
		}
	}
}
