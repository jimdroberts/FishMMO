namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the rank of a character within a guild.
	/// </summary>
	public enum GuildRank : byte
	{
		/// <summary>
		/// No rank assigned.
		/// </summary>
		None = 0,

		/// <summary>
		/// Standard guild member.
		/// </summary>
		Member = 1,

		/// <summary>
		/// Guild officer with additional permissions.
		/// </summary>
		Officer = 2,

		/// <summary>
		/// Guild leader with full permissions.
		/// </summary>
		Leader = 3,
	}
}
