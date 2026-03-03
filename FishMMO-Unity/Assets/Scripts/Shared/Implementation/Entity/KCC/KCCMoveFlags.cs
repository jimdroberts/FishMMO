namespace FishMMO.Shared
{
	/// <summary>
	/// Flags representing movement actions for KCC input replication.
	/// Used as a bitmask for jump, crouch, sprint, and other actions.
	/// </summary>
	public enum KCCMoveFlags : int
	{
		/// <summary>
		/// Indicates this is actual movement data (default value).
		/// </summary>
		IsActualData = 0,
		/// <summary>
		/// Jump action flag.
		/// </summary>
		Jump,
		/// <summary>
		/// Crouch action flag.
		/// </summary>
		Crouch,
		/// <summary>
		/// Sprint action flag.
		/// </summary>
		Sprint,
	}
}