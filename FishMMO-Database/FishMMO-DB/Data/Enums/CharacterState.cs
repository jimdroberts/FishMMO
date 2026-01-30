namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// Character session state used for distributed concurrency coordination.
	/// </summary>
	public enum CharacterState
	{
		/// <summary>
		/// Character is offline and may be claimed.
		/// </summary>
		Offline = 0,

		/// <summary>
		/// Character is online and owned by a server.
		/// </summary>
		Online = 1,

		/// <summary>
		/// Character is transitioning between servers.
		/// </summary>
		Transitioning = 2,
	}
}