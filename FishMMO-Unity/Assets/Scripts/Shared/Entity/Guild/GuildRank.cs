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
		Member,

		/// <summary>
		/// Guild officer with additional permissions.
		/// </summary>
		Officer,

		/// <summary>
		/// Guild leader with full permissions.
		/// </summary>
		Leader,
	}
}