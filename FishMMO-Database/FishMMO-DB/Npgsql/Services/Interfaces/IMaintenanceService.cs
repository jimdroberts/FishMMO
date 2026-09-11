using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Maintenance windows: locking a set of servers, draining them, and stopping them together.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>This service sequences the two controls that already exist; it does not reimplement
	/// them.</b> Every server is locked and scheduled through
	/// <see cref="IWorldServerService.SetShutdownAsync"/> and
	/// <see cref="ISceneServerService.SetShutdownAsync"/>, which write <c>locked = true</c> and
	/// <c>shutdown_at_utc</c> in one statement. Nothing here opens a connection to a process.
	/// </para>
	/// <para>
	/// <b>What makes a window durable is that it is fully actuated when it starts.</b> The drain
	/// is not a timer somebody has to keep running: the deadline is an absolute instant written
	/// onto each server's own row, and each server counts down to it inside its own process,
	/// warning its players as it goes. An operator can start a twenty minute drain, close their
	/// laptop, and the shard still goes down on time with the panel switched off. That is the
	/// whole reason the plan is rows.
	/// </para>
	/// <para>
	/// <b>What is <em>not</em> automatic is the bookkeeping.</b> <see cref="AdvanceAsync"/> is
	/// what moves an operation from Draining to ShuttingDown to Completed, and it only runs when
	/// something calls it — every read here calls it first, so the status is correct whenever
	/// anybody looks. With nothing polling, a finished window whose targets are long gone will
	/// still read "Draining" until the next person opens the page. The servers stopped on time
	/// regardless; it is the record that is late. Hosting this on a timer is one
	/// <c>IHostedService</c> that calls <see cref="AdvanceAsync"/> every few seconds.
	/// </para>
	/// <para>
	/// <b>Cancelling does not unlock anything.</b> <see cref="CancelAsync"/> clears the deadline
	/// column and nothing else, because that is all the underlying primitive does — the lock
	/// that scheduling applied stays applied. A cancelled window therefore leaves every target
	/// closed to new arrivals, and every caller has to say so, or a shard is left where nobody
	/// can log in and the cause is nowhere near where anyone will look.
	/// </para>
	/// </remarks>
	public interface IMaintenanceService
	{
		/// <summary>
		/// Plans a window, locks every target immediately, and schedules the shutdown for the end
		/// of the drain.
		/// </summary>
		/// <param name="name">What to call it, for the listing.</param>
		/// <param name="startedBy">The operator account planning it. Recorded.</param>
		/// <param name="reason">Why. Mandatory.</param>
		/// <param name="drainSeconds">How long players get. Zero stops at the next pulse.</param>
		/// <param name="targets">The world and scene servers to take down.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The planned window, with each target's outcome.</returns>
		/// <remarks>
		/// <para>
		/// The plan is committed <em>before</em> any server is touched, so a crash between the
		/// two leaves a visible half-written window rather than a locked shard with no record.
		/// <see cref="AdvanceAsync"/> finishes the writing on its next pass.
		/// </para>
		/// <para>
		/// A target that cannot be written is recorded as Failed and the rest of the window
		/// continues. Refusing the whole window because one server's row had gone would leave an
		/// operator to do the other eleven by hand, which is the thing this exists to prevent.
		/// </para>
		/// <para>
		/// Refused outright when: the reason is empty; there are no targets; the drain is
		/// negative or beyond a day; a named server does not exist; or a named server is already
		/// inside a live window, since two plans would fight over one deadline column.
		/// </para>
		/// </remarks>
		Task<DatabaseResult<MaintenanceOperationData>> StartAsync(
			string name,
			string startedBy,
			string reason,
			int drainSeconds,
			IReadOnlyList<MaintenanceTargetRequest> targets,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Clears the scheduled shutdowns. <b>The targets stay locked.</b>
		/// </summary>
		/// <param name="operationId">The window to call off.</param>
		/// <param name="cancelledBy">The operator account cancelling it. Recorded.</param>
		/// <param name="reason">Why. Mandatory.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// The cancelled window. Every target whose <c>LockWrittenUtc</c> is set is still locked,
		/// and the caller must say so.
		/// </returns>
		/// <remarks>
		/// A server that has already read its deadline and begun stopping does not come back
		/// because the column was cleared; the returned notes say which targets were past that
		/// point.
		/// </remarks>
		Task<DatabaseResult<MaintenanceOperationData>> CancelAsync(
			long operationId,
			string cancelledBy,
			string reason,
			CancellationToken cancellationToken = default);

		/// <summary>Windows, newest first, each with its targets.</summary>
		/// <param name="limit">How many to return. Clamped by the service.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <remarks>Advances every live window first, so the listing is current.</remarks>
		Task<DatabaseResult<IReadOnlyList<MaintenanceOperationData>>> ListAsync(
			int limit = 50,
			CancellationToken cancellationToken = default);

		/// <summary>One window, with per-target progress.</summary>
		/// <param name="operationId">The window to read.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <remarks>Advances it first, so what is returned reflects the servers as they are now.</remarks>
		Task<DatabaseResult<MaintenanceOperationData>> FetchAsync(
			long operationId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Brings every live window up to date with what its servers' rows now say, and finishes
		/// writing any target the start did not manage to write.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>How many live windows were brought up to date.</returns>
		/// <remarks>
		/// <para>
		/// Idempotent and safe to call concurrently: it re-derives each target's status from the
		/// server's own row rather than stepping a state machine forward, so two callers racing
		/// reach the same answer. Every read on this service calls it.
		/// </para>
		/// <para>
		/// It is <b>not</b> what makes a shutdown happen — the servers do that from the deadline
		/// on their own rows — so a deployment with nothing calling this still takes its shards
		/// down on time. What it costs is that the record only catches up when somebody looks.
		/// </para>
		/// </remarks>
		Task<DatabaseResult<int>> AdvanceAsync(CancellationToken cancellationToken = default);
	}
}
