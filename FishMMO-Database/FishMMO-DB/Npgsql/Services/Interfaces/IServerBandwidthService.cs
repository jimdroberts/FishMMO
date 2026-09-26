using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// What a server process writes about its own bandwidth: <c>server_bandwidth_minute</c> rows.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Separate from <see cref="IServerBandwidthReportService"/> for the reason
	/// <see cref="IServerBoardService"/> is separate from the per-tier services: a game server
	/// talks about itself here and has no business reading every other server's traffic.
	/// </para>
	/// <para>
	/// <b>The key is decided by the caller, before the write, from the database clock.</b> The
	/// process reads <see cref="FetchDatabaseUtcNowAsync"/>, truncates it to the minute, and
	/// passes finished rows to <see cref="RecordAsync"/>. The upsert never reads a clock itself:
	/// <c>ExecuteWriteAsync</c> retries a lost connection, and a retry that re-read
	/// <c>now()</c> could land a second after the minute turned and write the same traffic
	/// under the next minute's key, where the SET-upsert no longer protects it.
	/// </para>
	/// </remarks>
	public interface IServerBandwidthService
	{
		/// <summary>The database's UTC wall time, for keying the next minute row.</summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<DateTime>> FetchDatabaseUtcNowAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Writes a process's minute rows: <c>INSERT … ON CONFLICT DO UPDATE SET col = EXCLUDED.col</c>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Idempotent: writing the same rows twice leaves the table as writing them once, so the
		/// caller may re-send rows whose previous write failed or whose reply was lost. A row for a
		/// minute already stored REPLACES it (the row carries the minute's running total), unless
		/// its interval is shorter than the stored one — the running total of one minute only
		/// grows, so a shorter one is an older write arriving late and is ignored.
		/// </para>
		/// <para>
		/// Failure cases: VALIDATION_ERROR for a bad kind, name, empty instance id, a duplicate
		/// minute in the batch, or a row whose <see cref="ServerBandwidthSample.Invalid"/> says why;
		/// otherwise the database's own error.
		/// </para>
		/// </remarks>
		/// <param name="serverKind">The tier (<see cref="ServerBandwidthKind"/>).</param>
		/// <param name="serverName">The server's configured name.</param>
		/// <param name="instanceId">The process's instance id, taken once at start.</param>
		/// <param name="samples">The rows, at most one per minute.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The number of rows inserted or replaced.</returns>
		Task<DatabaseResult<int>> RecordAsync(
			int serverKind,
			string serverName,
			Guid instanceId,
			IReadOnlyList<ServerBandwidthSample> samples,
			CancellationToken cancellationToken = default);
	}
}
