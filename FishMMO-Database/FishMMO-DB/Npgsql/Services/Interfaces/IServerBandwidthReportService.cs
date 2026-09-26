using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// The operator's side of the bandwidth tables: the page's read, and the rollup and retention
	/// the Control Panel runs on a timer.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One pass at a time across every panel.</b> The rollup and each retention batch run in a
	/// transaction that first takes <c>pg_try_advisory_xact_lock</c>; a panel that does not get
	/// it skips the pass. The numbers would be right without it — every hour row is recomputed
	/// from its minutes and SET, never added to — but two panels upserting the same hour rows in
	/// whatever order their GROUP BY produced can deadlock each other, and two retention passes
	/// would fight over the same victims. It is a TRANSACTION-level lock on purpose: pooled
	/// connections are returned without a reset (<c>NoResetOnClose</c>), so a session-level lock
	/// would outlive the pass on a connection somebody else later borrows.
	/// </para>
	/// </remarks>
	public interface IServerBandwidthReportService
	{
		/// <summary>
		/// Recomputes hour rows from the minutes: the last
		/// <see cref="ServerBandwidthMath.RollupWindowHours"/> hours, or every hour that still has
		/// minutes when <paramref name="catchUp"/> is set (the first pass after a panel starts,
		/// covering however long no panel was running).
		/// </summary>
		/// <param name="catchUp">Roll every hour that has minute rows, not just the recent window.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<ServerBandwidthRollupResult>> RollupAsync(bool catchUp, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes minute rows older than <see cref="ServerBandwidthMath.MinuteRetentionDays"/> and
		/// hour rows older than <see cref="ServerBandwidthMath.HourRetentionMonths"/>, in bounded
		/// batches.
		/// </summary>
		/// <remarks>
		/// Minutes go one whole hour per transaction, and that transaction first rolls the hour
		/// it is about to delete, so pruning can never lose traffic the rollup had not reached,
		/// and no hour is ever left with some of its minutes gone (which a later recompute would
		/// read as a smaller hour).
		/// </remarks>
		/// <param name="maxMinuteHours">The most whole hours of minutes to prune this pass.</param>
		/// <param name="maxHourBatches">The most delete batches of expired hour rows this pass.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<ServerBandwidthPruneResult>> PruneAsync(int maxMinuteHours, int maxHourBatches, CancellationToken cancellationToken = default);

		/// <summary>
		/// Everything the bandwidth page shows, read in one REPEATABLE READ snapshot against one
		/// database clock.
		/// </summary>
		/// <param name="range">The charted range.</param>
		/// <param name="scopeKind">Chart one tier (<see cref="ServerBandwidthKind"/>), or 0 for all.</param>
		/// <param name="scopeName">Chart one server of <paramref name="scopeKind"/>, or null.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<ServerBandwidthReport>> FetchReportAsync(
			ServerBandwidthRange range,
			int scopeKind,
			string? scopeName,
			CancellationToken cancellationToken = default);
	}
}
