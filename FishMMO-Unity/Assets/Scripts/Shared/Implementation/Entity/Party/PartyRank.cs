namespace FishMMO.Shared
{
	/// <summary>
	/// Defines the rank of a character within a party.
	/// </summary>
	public enum PartyRank : byte
	{
		/// <summary>
		/// No party membership or rank assigned.
		/// </summary>
		None = 0,

		/// <summary>
		/// Standard party member.
		/// </summary>
		Member,

		/// <summary>
		/// Party leader with elevated permissions.
		/// </summary>
		Leader,
	}
}