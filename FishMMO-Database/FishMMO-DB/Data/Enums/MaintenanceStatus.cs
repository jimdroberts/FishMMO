namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// Where a maintenance operation, or one of its targets, has got to.
	/// </summary>
	/// <remarks>
	/// <para>
	/// One enumeration for both the operation and its targets, because they are the same five
	/// answers and an operator reading a target row asks the same question they ask of the
	/// operation. The operation's value is derived from its targets on every read — see
	/// <c>IMaintenanceService.AdvanceAsync</c> — and is never a claim made independently of
	/// them.
	/// </para>
	/// <para>
	/// <b>None of these values mean "the panel did something to a process".</b> A maintenance
	/// operation writes the <c>locked</c> and <c>shutdown_at_utc</c> columns on each target's
	/// own row, and the servers adopt those on their next pulse. So <see cref="Draining"/> means
	/// the rows are written and the deadline has not arrived; <see cref="Completed"/> means the
	/// server rows now look the way a stopped server's rows look. Both are observations, which
	/// is the strongest thing a database can honestly say about a machine it cannot address.
	/// </para>
	/// </remarks>
	public enum MaintenanceStatus
	{
		/// <summary>
		/// Locked, with a shutdown deadline written, and the deadline has not arrived.
		/// </summary>
		/// <remarks>
		/// Players already on the servers are still playing; that is the point of the drain.
		/// Nobody new is admitted above Player, because scheduling the shutdown locked every
		/// target in the same statement.
		/// </remarks>
		Draining = 0,

		/// <summary>
		/// The deadline has passed and the target has not yet been observed to stop.
		/// </summary>
		/// <remarks>
		/// A short state in a healthy shutdown — the server acts on the deadline within a pulse
		/// — and a state that turns into <see cref="Failed"/> rather than lingering, so a server
		/// that ignored its deadline is not displayed as "shutting down" indefinitely.
		/// </remarks>
		ShuttingDown = 1,

		/// <summary>The target's rows now read the way a stopped server's rows read.</summary>
		/// <remarks>
		/// A world server deletes its registration as it exits, so a missing row is the
		/// completion signal there. A scene server keeps its row and instead clears its own
		/// consumed deadline and lock on teardown, so <em>that</em> is the completion signal for
		/// a scene target. The two tiers genuinely differ; see the service for the rules.
		/// </remarks>
		Completed = 2,

		/// <summary>
		/// The shutdown was called off. <b>The servers are still locked.</b>
		/// </summary>
		/// <remarks>
		/// Cancelling clears <c>shutdown_at_utc</c> and nothing else: the <c>locked</c> column
		/// that scheduling set in the same statement stays set, because halting a shutdown and
		/// reopening a world to players are separate decisions. A cancelled operation therefore
		/// leaves every target closed to new arrivals until somebody unlocks it, and every
		/// surface that reports this status has to say so.
		/// </remarks>
		Cancelled = 3,

		/// <summary>
		/// It did not happen, and the record says why rather than pretending otherwise.
		/// </summary>
		/// <remarks>
		/// The cases are: the row could not be written at all; the deadline passed and the
		/// server is still pulsing; or the target was not pulsing when the operation was planned
		/// and has not pulsed since, so nothing ever read the row that was written for it.
		/// </remarks>
		Failed = 4,
	}
}
