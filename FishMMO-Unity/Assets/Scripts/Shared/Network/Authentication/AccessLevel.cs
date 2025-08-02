namespace FishMMO.Shared
{
	/// <summary>
	/// Enum representing user access levels and permissions in the game.
	/// Used for authentication, moderation, and feature gating.
	/// </summary>
	public enum AccessLevel : byte
	{
		/// <summary>
		/// User is banned and cannot access the game.
		/// </summary>
		Banned = 0,
		/// <summary>
		/// Regular player with standard permissions.
		/// </summary>
		Player,
		/// <summary>
		/// Guide or helper with limited support permissions.
		/// </summary>
		Guide,
		/// <summary>
		/// Game master with advanced moderation and support permissions.
		/// </summary>
		GameMaster,
		/// <summary>
		/// Administrator with full permissions and access to all features.
		/// </summary>
		Administrator,
	}
}