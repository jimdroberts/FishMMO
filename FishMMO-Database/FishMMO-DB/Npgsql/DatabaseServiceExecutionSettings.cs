namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Settings for <c>BaseService</c>'s unified execution engine.
	/// 
	/// These values are loaded by <c>NpgsqlDbContextFactory</c> from the <c>DatabaseServiceExecution</c>
	/// section of <c>appsettings.json</c> and used to control:
	/// - Idempotency validation constraints
	/// - Best-effort idempotency table cleanup (retention / bounded deletes)
	/// 
	/// Notes:
	/// - The factory clamps invalid values defensively (e.g., negative counts become 0).
	/// - Setting <see cref="ProcessedRequestsRetentionDays"/> to 0 disables cleanup.
	/// </summary>
	public sealed class DatabaseServiceExecutionSettings
	{
		/// <summary>
		/// Maximum allowed length of the logical idempotency operation name.
		/// </summary>
		public int MaxIdempotencyOperationNameLength { get; set; } = 64;

		/// <summary>
		/// How long to retain rows in <c>processed_requests</c> before they are eligible for cleanup.
		/// 
		/// Unit: days.
		/// Set to 0 to disable cleanup entirely.
		/// </summary>
		public int ProcessedRequestsRetentionDays { get; set; } = 30;

		/// <summary>
		/// Maximum number of rows to delete per cleanup invocation.
		/// 
		/// This bounds the work done per call to prevent long-running deletes.
		/// </summary>
		public int ProcessedRequestsCleanupMaxRows { get; set; } = 5000;

		/// <summary>
		/// Timeout for an idempotent request that is stuck in the "in progress" state.
		/// 
		/// If a server crashes while processing an idempotent operation, the row in
		/// <c>processed_requests</c> may remain in status 0 (in progress). Once the row is older than
		/// this timeout, a subsequent retry may reclaim the request and proceed.
		/// 
		/// Unit: minutes.
		/// Set to 0 to disable stale takeover (not recommended for production).
		/// </summary>
		public int ProcessedRequestsInProgressTimeoutMinutes { get; set; } = 5;

		/// <summary>
		/// Minimum interval between cleanup invocations.
		/// 
		/// Unit: minutes.
		/// Set to 0 to allow cleanup attempts on every eligible idempotent call (still bounded by
		/// <see cref="ProcessedRequestsCleanupMaxRows"/>).
		/// </summary>
		public int ProcessedRequestsCleanupMinIntervalMinutes { get; set; } = 15;
	}
}