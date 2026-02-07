namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// Character session state used for distributed concurrency coordination.
	/// </summary>
	public enum CharacterSessionState
	{
		/// <summary>
		/// Character is offline and may be claimed.
		/// </summary>
		Offline = 0,

		/// <summary>
		/// Character is online and owned by a server.
		/// </summary>
		Online = 1,
	}
}