namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// Where a group finder queue row is in its life.
	/// </summary>
	/// <remarks>
	/// Two states, deliberately. Forming a group — claiming the waiters, creating their party,
	/// opening their instance and marking them matched — is one database transaction, so there is
	/// no window in which a row is "being matched" for anything to observe, and therefore no state
	/// for it. A row is either still waiting or it names the party and instance it is bound for.
	/// </remarks>
	public enum GroupFinderQueueStatus : int
	{
		/// <summary>Waiting for enough players, or for a run with room.</summary>
		Waiting = 0,

		/// <summary>
		/// Matched. <c>party_id</c> and <c>instance_id</c> are set, and the scene server hosting
		/// the character will move them on its next pump.
		/// </summary>
		Matched = 1,
	}
}
