namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// Where a support ticket has got to.
	/// </summary>
	/// <remarks>
	/// Five states, deliberately few. A support queue with a dozen statuses is one where nobody
	/// agrees what any of them mean, and the distinction that actually matters to a player is
	/// only ever "is somebody looking at this, and is it finished".
	/// </remarks>
	public enum SupportTicketStatus
	{
		/// <summary>Filed and waiting for staff. The default.</summary>
		Open = 0,

		/// <summary>A staff member has taken it and is working on it.</summary>
		InProgress = 1,

		/// <summary>
		/// Staff have asked the player something and are waiting for an answer.
		/// </summary>
		/// <remarks>
		/// Separate from <see cref="Open"/> so a queue sorted by age does not shame staff for
		/// tickets that are only old because the player has not replied.
		/// </remarks>
		AwaitingPlayer = 2,

		/// <summary>Dealt with. The player can still reply, which reopens it.</summary>
		Resolved = 3,

		/// <summary>Finished and locked. Nothing further is accepted.</summary>
		Closed = 4,
	}
}
