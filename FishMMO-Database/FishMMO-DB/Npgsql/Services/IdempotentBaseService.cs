using System;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Threading;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Provides a process-wide throttle for background maintenance work.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is intentionally non-generic so throttling is global across all services.
	/// Some maintenance tasks (e.g., idempotency table cleanup) operate on a shared table and
	/// must not run once per closed generic <see cref="BaseService{TEntity}"/>.
	/// </para>
	/// </remarks>
	internal static class GlobalMaintenanceThrottle
	{
		private static long processedRequestsCleanupLastRunTicks;

		/// <summary>
		/// Attempts to begin a throttled maintenance operation.
		/// </summary>
		/// <param name="minInterval">
		/// Minimum time that must elapse between successful begins.
		/// Use <see cref="TimeSpan.Zero"/> to disable throttling.
		/// </param>
		/// <returns>
		/// True if the caller should proceed with the operation; otherwise false.
		/// </returns>
		public static bool TryBeginProcessedRequestsCleanup(TimeSpan minInterval)
		{
			if (minInterval <= TimeSpan.Zero)
			{
				return true;
			}

			var nowTicks = DateTime.UtcNow.Ticks;
			var lastTicks = Interlocked.Read(ref processedRequestsCleanupLastRunTicks);
			if (nowTicks - lastTicks < minInterval.Ticks)
			{
				return false;
			}

			return Interlocked.CompareExchange(ref processedRequestsCleanupLastRunTicks, nowTicks, lastTicks) == lastTicks;
		}
	}

	/// <summary>
	/// Base service that adds DSL-level idempotency support via the processed_requests table.
	/// </summary>
	/// <remarks>
	/// <para><b>Purpose: DSL-Level Idempotency for Non-Idempotent Operations</b></para>
	/// <para>
	/// This service extends <see cref="BaseService{TEntity}"/> to provide database-tracked idempotency for operations
	/// that are NOT naturally idempotent at the SQL level (e.g., account creation, party creation, chat message logging).
	/// </para>
	/// 
	/// <para><b>Architecture: In-Process DSL with Database Network Hop</b></para>
	/// <para>
	/// This Database Service Layer (DSL) is integrated directly into the Application Server Layer (no network hop between them).
	/// However, there IS a network hop from the DSL to the PostgreSQL database. This architecture requires DSL-level
	/// idempotency tracking because:
	/// </para>
	/// <list type="bullet">
	/// <item><description>Network timeouts between DSL and database can occur</description></item>
	/// <item><description>Client retries after timeouts must not cause duplicate operations</description></item>
	/// <item><description>EF Core execution strategy retries must not cause duplicate operations</description></item>
	/// <item><description>The Application Server Layer cannot determine if a timed-out operation completed on the database</description></item>
	/// </list>
	/// 
	/// <para><b>Request ID Contract: Caller Provides Stable Identifiers</b></para>
	/// <para>
	/// <b>CRITICAL REQUIREMENT:</b> The <c>requestId</c> parameter MUST be provided by the Application Server Layer,
	/// NOT generated within the DSL service methods. The Application Server Layer is responsible for:
	/// </para>
	/// <list type="number">
	/// <item><description>Generating a unique <c>Guid</c> per logical operation at the API boundary</description></item>
	/// <item><description>Passing the same <c>requestId</c> on all retries of the same logical operation</description></item>
	/// <item><description>Using different <c>requestId</c> values for distinct operations (even if they have identical parameters)</description></item>
	/// </list>
	/// <para>
	/// <b>Why this matters:</b> If the DSL generates the <c>requestId</c> internally, each client retry will get a
	/// new identifier, defeating idempotency protection. The cached response in <c>processed_requests</c> will be
	/// bypassed, and the operation may execute multiple times.
	/// </para>
	/// 
	/// <para><b>Idempotency Mechanism: processed_requests Table</b></para>
	/// <para>
	/// The <c>processed_requests</c> table tracks:
	/// </para>
	/// <list type="bullet">
	/// <item><description><b>request_id:</b> Unique identifier provided by the Application Server Layer</description></item>
	/// <item><description><b>scope_id:</b> Logical scope (e.g., account ID, character ID) to prevent request ID reuse across entities</description></item>
	/// <item><description><b>operation_name:</b> Operation being performed (e.g., "CreateAccount", "SavePet")</description></item>
	/// <item><description><b>status:</b> 0=in-progress, 1=success, 2=failure</description></item>
	/// <item><description><b>owner_id:</b> Identifies which process instance owns the in-progress request</description></item>
	/// <item><description><b>lease_expires_at:</b> Timestamp for lease expiration (allows takeover of stale requests)</description></item>
	/// <item><description><b>response:</b> Cached response (success data or failure details) stored as JSONB</description></item>
	/// </list>
	/// 
	/// <para><b>Execution Flow: Two-Phase Commit with Cached Responses</b></para>
	/// <para>
	/// When an idempotent operation is invoked via <see cref="ExecuteIdempotentAsync{TResult}"/>:
	/// </para>
	/// <list type="number">
	/// <item>
	/// <term>Phase 1: Acquire or Read Idempotency State (Non-Transactional)</term>
	/// <description>
	/// <list type="bullet">
	/// <item><description>Attempts to INSERT the request row with status=in-progress, owner_id=new Guid, and lease expiration</description></item>
	/// <item><description>If row already exists (ON CONFLICT DO NOTHING), reads the existing state</description></item>
	/// <item><description>If existing row has status=success or status=failure, returns cached response immediately (fast path)</description></item>
	/// <item><description>If existing row has status=in-progress but lease expired, attempts to take over ownership</description></item>
	/// <item><description>If another process owns the in-progress request and lease is active, returns IDEMPOTENCY_IN_PROGRESS error</description></item>
	/// </list>
	/// </description>
	/// </item>
	/// <item>
	/// <term>Phase 2: Execute Business Operation and Finalize (Transactional)</term>
	/// <description>
	/// <list type="bullet">
	/// <item><description>Re-checks idempotency state inside the transaction (prevents double execution on retry)</description></item>
	/// <item><description>If another attempt already finalized the request, returns the cached response</description></item>
	/// <item><description>Otherwise, executes the business operation delegate</description></item>
	/// <item><description>Serializes the result to JSONB and updates the request row to status=success or status=failure</description></item>
	/// <item><description>Commits the transaction, atomically finalizing both the business operation and the cached response</description></item>
	/// </list>
	/// </description>
	/// </item>
	/// </list>
	/// 
	/// <para><b>Retry Safety: EF Core Execution Strategy and Caller Retries</b></para>
	/// <para>
	/// This design is safe under two retry scenarios:
	/// </para>
	/// <list type="bullet">
	/// <item><description><b>EF Core execution strategy retries (same call):</b> Uses the same requestId, re-checks cached state before re-executing</description></item>
	/// <item><description><b>Client/Application Server retries (separate calls):</b> Uses the same requestId, reads cached response if available</description></item>
	/// </list>
	/// <para>
	/// If a transient failure occurs AFTER the business operation commits but BEFORE the cached response is persisted,
	/// a subsequent retry will re-check the idempotency state, find the finalized cached response, and return it.
	/// </para>
	/// 
	/// <para><b>Lease Management and Stale Request Handling</b></para>
	/// <para>
	/// In-progress requests have a configurable lease timeout (default: several minutes). If a process crashes or times out
	/// while holding an in-progress lease, subsequent requests with the same requestId can take over ownership after the
	/// lease expires. This prevents indefinite blocking while maintaining safety.
	/// </para>
	/// 
	/// <para><b>Cleanup: Automatic Pruning of Old Requests</b></para>
	/// <para>
	/// The <see cref="CleanupProcessedRequestsAsync"/> method removes completed and stale in-progress requests older than
	/// a configured retention period. This prevents unbounded growth of the <c>processed_requests</c> table. Cleanup is
	/// throttled globally to avoid excessive concurrent cleanup operations.
	/// </para>
	/// 
	/// <para><b>Best Practices for Service Implementers</b></para>
	/// <list type="bullet">
	/// <item><description><b>Always accept requestId as a parameter</b> in public service methods that require idempotency</description></item>
	/// <item><description><b>Validate requestId is not Guid.Empty</b> before calling ExecuteIdempotentAsync</description></item>
	/// <item><description><b>Use stable scopeId values</b> (e.g., hash of entity identifier) to prevent requestId reuse across entities</description></item>
	/// <item><description><b>Keep operation names short and stable</b> (max 64 characters, no versioning in name)</description></item>
	/// <item><description><b>Ensure operation delegates are database-only</b> - no external API calls, message publishes, or file I/O</description></item>
	/// </list>
	/// </remarks>
	public abstract class IdempotentBaseService<TEntity> : BaseService<TEntity> where TEntity : class
	{
		private static readonly JsonSerializerOptions IdempotencyJsonOptions = new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};

		private readonly int maxIdempotencyOperationNameLength;
		private readonly int processedRequestsRetentionDays;
		private readonly int processedRequestsCleanupMaxRows;
		private readonly int processedRequestsCleanupMinIntervalMinutes;
		private readonly int processedRequestsInProgressTimeoutMinutes;

		private static async Task EnsureConnectionOpenAsync(NpgsqlDbContext dbContext, CancellationToken cancellationToken)
		{
			var connection = dbContext.Database.GetDbConnection();
			if (connection.State != ConnectionState.Open)
			{
				await dbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
			}
		}

		protected IdempotentBaseService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
			var settings = DbContextFactory.ServiceExecutionSettings ?? new DatabaseServiceExecutionSettings();
			maxIdempotencyOperationNameLength = Math.Min(64, Math.Max(1, settings.MaxIdempotencyOperationNameLength));
			processedRequestsRetentionDays = Math.Max(0, settings.ProcessedRequestsRetentionDays);
			processedRequestsCleanupMaxRows = Math.Max(1, settings.ProcessedRequestsCleanupMaxRows);
			processedRequestsCleanupMinIntervalMinutes = Math.Max(0, settings.ProcessedRequestsCleanupMinIntervalMinutes);
			processedRequestsInProgressTimeoutMinutes = Math.Max(1, settings.ProcessedRequestsInProgressTimeoutMinutes);
		}

		private async Task MaybeCleanupProcessedRequestsAsync()
		{
			if (processedRequestsRetentionDays <= 0)
			{
				return;
			}

			var minInterval = processedRequestsCleanupMinIntervalMinutes <= 0
				? TimeSpan.Zero
				: TimeSpan.FromMinutes(processedRequestsCleanupMinIntervalMinutes);

			if (!GlobalMaintenanceThrottle.TryBeginProcessedRequestsCleanup(minInterval))
			{
				return;
			}

			try
			{
				using var bestEffortToken = CreateBestEffortCancellationToken(TimeSpan.FromSeconds(2));
				await CleanupProcessedRequestsAsync(
					TimeSpan.FromDays(processedRequestsRetentionDays),
					processedRequestsCleanupMaxRows,
					bestEffortToken.Token).ConfigureAwait(false);
			}
			catch
			{
				// Best-effort maintenance only.
			}
		}

		/// <summary>
		/// Creates a best-effort cancellation token source with a short timeout.
		/// </summary>
		/// <remarks>
		/// This is used for cleanup/failure-persistence paths that should not hang the caller
		/// indefinitely when the database/network is unhealthy.
		/// </remarks>
		/// <param name="timeout">Maximum time to allow the best-effort operation to run.</param>
		/// <returns>A disposable <see cref="CancellationTokenSource"/> with the given timeout.</returns>
		private static CancellationTokenSource CreateBestEffortCancellationToken(TimeSpan timeout)
		{
			return new CancellationTokenSource(timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(2) : timeout);
		}

		private static readonly long ProcessedRequestsCleanupLockKey = ComputeAdvisoryLockKey(
			"FishMMO.Database.Npgsql.Services.IdempotentBaseService.ProcessedRequestsCleanup");

		/// <summary>
		/// Computes a stable, positive scope identifier from a string value.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This method is intended for idempotency scoping (e.g., to prevent reusing the same <c>requestId</c>
		/// across different logical resources).
		/// </para>
		/// <para>
		/// The implementation must be stable across processes/runtimes. Do not use <see cref="string.GetHashCode"/>.
		/// </para>
		/// </remarks>
		/// <param name="value">Input value used to derive the scope.</param>
		/// <returns>A positive, non-zero scope identifier.</returns>
		protected static long ComputeScopeId(string value)
		{
			return ComputeStablePositiveSha25664(value);
		}

		private static long ComputeAdvisoryLockKey(string value)
		{
			return ComputeStablePositiveFnv1a64(value);
		}

		private static long ComputeStablePositiveFnv1a64(string value)
		{
			// Must be stable across processes/runtimes. Do not use string.GetHashCode/HashCode.
			// FNV-1a 64-bit over UTF8 bytes.
			const ulong offsetBasis = 14695981039346656037;
			const ulong prime = 1099511628211;

			var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
			ulong hash = offsetBasis;
			for (var i = 0; i < bytes.Length; i++)
			{
				hash ^= bytes[i];
				hash *= prime;
			}

			var key = (long)(hash & 0x7FFFFFFFFFFFFFFF);
			return key == 0 ? 1 : key;
		}

		private static long ComputeStablePositiveSha25664(string value)
		{
			// Stable across processes/runtimes. Uses SHA-256 to reduce collision risk.
			// Derives a positive non-zero long from the first 8 bytes interpreted as big-endian.
			var input = Encoding.UTF8.GetBytes(value ?? string.Empty);
			using var sha256 = SHA256.Create();
			var digest = sha256.ComputeHash(input);

			ulong unsignedValue = 0;
			for (var i = 0; i < 8; i++)
			{
				unsignedValue = (unsignedValue << 8) | digest[i];
			}

			var key = (long)(unsignedValue & 0x7FFFFFFFFFFFFFFF);
			return key == 0 ? 1 : key;
		}

		private sealed class IdempotencyEnvelope<T>
		{
			public bool IsSuccess { get; set; }
			public T Data { get; set; } = default!;
			public string? ErrorCode { get; set; }
			public string? ErrorMessage { get; set; }
			public bool IsTransient { get; set; }
		}

		private readonly struct IdempotencyState
		{
			public IdempotencyState(bool didInsert, long scopeId, string operationName, Guid ownerId, DateTime leaseExpiresAt, byte status, string? response, string? errorCode, string? errorMessage)
			{
				DidInsert = didInsert;
				ScopeId = scopeId;
				OperationName = operationName;
				OwnerId = ownerId;
				LeaseExpiresAt = leaseExpiresAt;
				Status = status;
				Response = response;
				ErrorCode = errorCode;
				ErrorMessage = errorMessage;
			}

			public bool DidInsert { get; }
			public long ScopeId { get; }
			public string OperationName { get; }
			public Guid OwnerId { get; }
			public DateTime LeaseExpiresAt { get; }
			public byte Status { get; }
			public string? Response { get; }
			public string? ErrorCode { get; }
			public string? ErrorMessage { get; }
		}

		/// <summary>
		/// Unified protected entrypoint for executing idempotent operations.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This method provides request-scoped idempotency using the <c>processed_requests</c> table.
		/// </para>
		/// <para>
		/// <b>Typical usage in this codebase:</b> the calling service method generates a new <paramref name="requestId"/>
		/// once per API call, then passes it through the full execution pipeline.
		/// If EF Core's execution strategy retries due to a transient failure, the same <paramref name="requestId"/> is
		/// reused and the cached response prevents duplicate writes.
		/// </para>
		/// <para>
		/// <b>Important:</b> this design assumes the server does not intentionally replay the same logical request across
		/// separate API calls after failures. If that changes in the future (e.g., explicit client retries with the same
		/// request semantics), a caller-stable idempotency key would be required.
		/// </para>
		/// <para>
		/// <b>Retry semantics:</b> The underlying execution pipeline uses EF Core's execution strategy
		/// (<see cref="Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy"/>) which may retry the operation delegate
		/// on transient failures (e.g., deadlocks, serialization failures, or connection interruptions).
		/// For this reason, the <paramref name="operation"/> delegate must be safe to invoke more than once.
		/// </para>
		/// <para>
		/// <b>Enforcement guidance:</b> The <paramref name="operation"/> delegate must be <b>database-only</b> and must not perform
		/// side effects outside of PostgreSQL (e.g., network calls, message publishing, file I/O, or writes to other datastores).
		/// This library intentionally keeps these services DB-only to preserve correctness under retries.
		/// </para>
		/// <para>
		/// <b>How double execution is prevented:</b> The idempotency state is checked once before the business transaction
		/// and then re-checked inside the transactional attempt. If a prior attempt already finalized the request,
		/// the cached response is returned and the business operation body is not executed again.
		/// </para>
		/// <para>
		/// <b>Execution flow (where retries/re-checks occur):</b>
		/// <list type="number">
		/// <item><description>
		/// <b>Begin (non-transactional):</b> <c>ExecuteInternalAsync($"{operationName}.IdempotencyBegin", ...)</c>
		/// creates/reads the <c>processed_requests</c> row and may be retried by the provider execution strategy.
		/// </description></item>
		/// <item><description>
		/// <b>Fast-path:</b> If <c>processed_requests</c> already contains a completed response, this method returns it
		/// immediately (no business transaction is started).
		/// </description></item>
		/// <item><description>
		/// <b>Business transaction:</b> <c>ExecuteInternalAsync(operationName, ... useTransaction: true ...)</c>
		/// runs the business operation and finalizes the cached response in the same transaction.
		/// This entire block may be retried on transient failures.
		/// </description></item>
		/// <item><description>
		/// <b>Transactional re-check:</b> At the start of each transactional attempt, the code calls
		/// <c>TryBeginIdempotentRequestAsync(..., dbTransaction: transaction)</c> again. If a previous attempt already
		/// finalized the request, the cached response is returned and <paramref name="operation"/> is not invoked.
		/// </description></item>
		/// </list>
		/// </para>
		/// </remarks>
		protected async Task<DatabaseResult<TResult>> ExecuteIdempotentAsync<TResult>(
			Guid requestId,
			long scopeId,
			string operationName,
			Func<NpgsqlDbContext, IDbContextTransaction, CancellationToken, Task<TResult>> operation,
			CancellationToken cancellationToken = default)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));

			if (requestId == Guid.Empty)
			{
				return DatabaseResult<TResult>.Failure("VALIDATION_ERROR", "RequestId is required.");
			}

			if (scopeId <= 0)
			{
				return DatabaseResult<TResult>.Failure("VALIDATION_ERROR", "ScopeId must be greater than 0.");
			}

			if (string.IsNullOrWhiteSpace(operationName))
			{
				return DatabaseResult<TResult>.Failure("VALIDATION_ERROR", "OperationName is required.");
			}

			if (operationName.Length > maxIdempotencyOperationNameLength)
			{
				return DatabaseResult<TResult>.Failure(
					"VALIDATION_ERROR",
					$"OperationName must be {maxIdempotencyOperationNameLength} characters or less.");
			}

			await MaybeCleanupProcessedRequestsAsync().ConfigureAwait(false);

			var ownerId = Guid.NewGuid();
			var leaseTimeoutMinutes = processedRequestsInProgressTimeoutMinutes;

			// Acquire or read idempotency state in a durable, non-transactional step.
			// This must not be rolled back by the business operation transaction.
			// NOTE: ExecuteInternalAsync uses EF Core's execution strategy. This begin step may be invoked multiple
			// times under transient retries, but it is safe because it only inserts/reads the idempotency row.
			var beginResult = await ExecuteInternalAsync(
				$"{operationName}.IdempotencyBegin",
				async (dbContext, transaction, ct) =>
				{
					var requestsTable = dbContext.GetTableName<ProcessedRequestEntity>();
					var state = await TryBeginIdempotentRequestAsync(
						dbContext,
						dbTransaction: null,
						requestsTable,
						requestId,
						scopeId,
						operationName,
						ownerId,
						leaseTimeoutMinutes,
						ct).ConfigureAwait(false);

					if (!state.DidInsert && state.ScopeId != scopeId)
					{
						return DatabaseResult<IdempotencyState>.Failure("IDEMPOTENCY_MISMATCH", "RequestId is already in use.");
					}

					if (!state.DidInsert && !string.Equals(state.OperationName, operationName, StringComparison.Ordinal))
					{
						return DatabaseResult<IdempotencyState>.Failure("IDEMPOTENCY_MISMATCH", "RequestId is already in use.");
					}

					// If another node is currently processing this request and the lease hasn't expired, fail fast.
					if (!state.DidInsert && state.Status == 0 && string.IsNullOrWhiteSpace(state.Response) && state.OwnerId != ownerId)
					{
						var tookOver = await TryTakeOverStaleIdempotentRequestAsync(
							dbContext,
							dbTransaction: null,
							requestsTable,
							requestId,
							scopeId,
							operationName,
							ownerId,
							leaseTimeoutMinutes,
							ct).ConfigureAwait(false);

						if (!tookOver)
						{
							return DatabaseResult<IdempotencyState>.Failure("IDEMPOTENCY_IN_PROGRESS", "This request is already being processed.");
						}

						state = new IdempotencyState(true, scopeId, operationName, ownerId, DateTime.UtcNow.AddMinutes(leaseTimeoutMinutes), status: 0, response: null, errorCode: null, errorMessage: null);
					}

					if (state.Status == 0 && state.OwnerId == ownerId)
					{
						await RefreshIdempotencyLeaseAsync(
							dbContext,
							dbTransaction: null,
							requestsTable,
							requestId,
							ownerId,
							leaseTimeoutMinutes,
							ct).ConfigureAwait(false);
					}

					return DatabaseResult<IdempotencyState>.Success(state);
				},
				useTransaction: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!beginResult.IsSuccess)
			{
				return DatabaseResult<TResult>.Failure(beginResult.ErrorCode, beginResult.ErrorMessage, beginResult.IsTransient);
			}

			var beginState = beginResult.Data;
			if (!beginState.DidInsert)
			{
				// Fast-path: the request row already existed. If it already contains a cached response (success or failure),
				// return it immediately. This prevents starting the business transaction and guarantees idempotent behavior
				// for duplicate caller retries.
				if (TryGetCachedIdempotencyResponse<TResult>(beginState.Status, beginState.Response, beginState.ErrorCode, beginState.ErrorMessage, out var cached))
				{
					return cached;
				}
			}

			// Run the business operation and mark completion within the SAME transaction.
			// This ensures we never commit side effects without also persisting the cached response.
			// NOTE: ExecuteInternalAsync may retry this entire delegate on transient failures.
			// The transactional re-check at the top of the delegate is what prevents double execution across retries
			// when a prior attempt already committed.
			return await ExecuteInternalAsync(
				operationName,
				async (dbContext, transaction, ct) =>
				{
					var requestsTable = dbContext.GetTableName<ProcessedRequestEntity>();

					// Re-check state inside THIS transaction attempt.
					// If a previous attempt already finalized the request (status/response set), we return the cached response
					// and do NOT invoke the business operation delegate again.
					var state = await TryBeginIdempotentRequestAsync(
						dbContext,
						transaction?.GetDbTransaction(),
						requestsTable,
						requestId,
						scopeId,
						operationName,
						ownerId,
						leaseTimeoutMinutes,
						ct).ConfigureAwait(false);

					if (!state.DidInsert)
					{
						if (TryGetCachedIdempotencyResponse<TResult>(state.Status, state.Response, state.ErrorCode, state.ErrorMessage, out var cached))
						{
							return cached;
						}

						if (state.Status == 0 && string.IsNullOrWhiteSpace(state.Response) && state.OwnerId != ownerId)
						{
							return DatabaseResult<TResult>.Failure("IDEMPOTENCY_IN_PROGRESS", "This request is already being processed.", isTransient: false);
						}
					}

					if (state.Status == 0 && state.OwnerId == ownerId)
					{
						await RefreshIdempotencyLeaseAsync(
							dbContext,
							transaction?.GetDbTransaction(),
							requestsTable,
							requestId,
							ownerId,
							leaseTimeoutMinutes,
							ct).ConfigureAwait(false);
					}

					// At this point, this attempt owns the request in-progress state and is responsible for producing
					// the authoritative cached response in processed_requests.
					var data = await operation(dbContext, transaction!, ct).ConfigureAwait(false);
					var responseJson = JsonSerializer.Serialize(
						new IdempotencyEnvelope<TResult>
						{
							IsSuccess = true,
							Data = data,
							IsTransient = false,
							ErrorCode = null,
							ErrorMessage = null
						},
						IdempotencyJsonOptions);

					var rows = await UpdateProcessedRequestAsync(
						dbContext,
						transaction?.GetDbTransaction(),
						requestsTable,
						requestId,
						scopeId,
						operationName,
						ownerId,
						status: 1,
						responseJson,
						errorCode: null,
						errorMessage: null,
						ct).ConfigureAwait(false);

					if (rows == 0)
					{
						throw new InvalidOperationException("Unable to finalize idempotent request state.");
					}

					return DatabaseResult<TResult>.Success(data);
				},
				useTransaction: true,
				cancellationToken: cancellationToken,
				onFailureAsync: async (dbContext, dbEx, willRetry, ct) =>
				{
					if (willRetry)
					{
						// The execution strategy intends to retry. Avoid persisting a failure record for this attempt.
						// A later successful retry should be able to finalize the request as success.
						return;
					}

					// Do not cache cancellations as permanent failures. Cancellations should remain retryable
					// via the lease/takeover mechanism rather than poisoning the idempotency key.
					if (dbEx is DatabaseOperationCanceledException)
					{
						return;
					}

					// Best-effort: persist failure to enable deterministic retries.
					// This hook is invoked after retries are exhausted (or for non-retryable errors).
					var requestsTable = dbContext.GetTableName<ProcessedRequestEntity>();
					var failureJson = JsonSerializer.Serialize(
						new IdempotencyEnvelope<TResult>
						{
							IsSuccess = false,
							Data = default!,
							IsTransient = dbEx.IsTransient,
							ErrorCode = dbEx.ErrorCode,
							ErrorMessage = dbEx.SafeMessage
						},
						IdempotencyJsonOptions);

					try
					{
						using var bestEffortToken = CreateBestEffortCancellationToken(TimeSpan.FromSeconds(2));
						await UpdateProcessedRequestAsync(
							dbContext,
							dbTransaction: null,
							requestsTable,
							requestId,
							scopeId,
							operationName,
							ownerId,
							status: 2,
							failureJson,
							dbEx.ErrorCode,
							dbEx.SafeMessage,
							bestEffortToken.Token).ConfigureAwait(false);
					}
					catch
					{
						// Best-effort only.
					}
				}).ConfigureAwait(false);
		}

		/// <summary>
		/// Executes an idempotent operation that itself returns a <see cref="DatabaseResult"/>.
		/// </summary>
		/// <remarks>
		/// This is a convenience wrapper for services that already model business-rule failures as
		/// <see cref="DatabaseResult"/> values (e.g., capacity checks) rather than exceptions.
		/// The idempotency pipeline needs failures to be represented as exceptions so they can be
		/// cached and replayed correctly across transient retries.
		/// </remarks>
		protected async Task<DatabaseResult> ExecuteIdempotentResultAsync(
			Guid requestId,
			long scopeId,
			string operationName,
			Func<NpgsqlDbContext, IDbContextTransaction, CancellationToken, Task<DatabaseResult>> operation,
			CancellationToken cancellationToken = default)
		{
			var result = await ExecuteIdempotentResultAsync<bool>(
				requestId,
				scopeId,
				operationName,
				async (dbContext, transaction, ct) =>
				{
					var inner = await operation(dbContext, transaction, ct).ConfigureAwait(false);
					return inner.IsSuccess
						? DatabaseResult<bool>.Success(true)
						: DatabaseResult<bool>.Failure(inner.ErrorCode, inner.ErrorMessage, inner.IsTransient);
				},
				cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <summary>
		/// Executes an idempotent operation that itself returns a <see cref="DatabaseResult{T}"/>.
		/// </summary>
		protected async Task<DatabaseResult<TResult>> ExecuteIdempotentResultAsync<TResult>(
			Guid requestId,
			long scopeId,
			string operationName,
			Func<NpgsqlDbContext, IDbContextTransaction, CancellationToken, Task<DatabaseResult<TResult>>> operation,
			CancellationToken cancellationToken = default)
		{
			return await ExecuteIdempotentAsync(
				requestId,
				scopeId,
				operationName,
				async (dbContext, transaction, ct) =>
				{
					var inner = await operation(dbContext, transaction, ct).ConfigureAwait(false);
					if (!inner.IsSuccess)
					{
						throw new DatabaseOperationFailedException(
							operation: operationName,
							errorCode: inner.ErrorCode ?? "DATABASE_ERROR",
							safeMessage: inner.ErrorMessage ?? "Operation failed.",
							isTransient: inner.IsTransient);
					}

					return inner.Data;
				},
				cancellationToken).ConfigureAwait(false);
		}

		private static bool TryGetCachedIdempotencyResponse<TResult>(
			byte status,
			string? responseJson,
			string? errorCode,
			string? errorMessage,
			out DatabaseResult<TResult> result)
		{
			result = default;

			if (!string.IsNullOrWhiteSpace(responseJson))
			{
				IdempotencyEnvelope<TResult>? envelope;
				try
				{
					envelope = JsonSerializer.Deserialize<IdempotencyEnvelope<TResult>>(responseJson, IdempotencyJsonOptions);
				}
				catch
				{
					result = DatabaseResult<TResult>.Failure("IDEMPOTENCY_ERROR", "Cached idempotency response is invalid.");
					return true;
				}

				if (envelope == null)
				{
					result = DatabaseResult<TResult>.Failure("IDEMPOTENCY_ERROR", "Cached idempotency response is missing.");
					return true;
				}

				result = envelope.IsSuccess
					? DatabaseResult<TResult>.Success(envelope.Data)
					: DatabaseResult<TResult>.Failure(envelope.ErrorCode ?? "IDEMPOTENCY_FAILED", envelope.ErrorMessage ?? "This request previously failed.", envelope.IsTransient);
				return true;
			}

			if (status == 2)
			{
				result = DatabaseResult<TResult>.Failure(errorCode ?? "IDEMPOTENCY_FAILED", errorMessage ?? "This request previously failed.");
				return true;
			}

			if (status == 1)
			{
				result = DatabaseResult<TResult>.Failure(
					"IDEMPOTENCY_CORRUPT",
					"Cached idempotency record is incomplete.");
				return true;
			}

			return false;
		}

		private static async Task<bool> TryTakeOverStaleIdempotentRequestAsync(
			NpgsqlDbContext dbContext,
			DbTransaction? dbTransaction,
			string requestsTable,
			Guid requestId,
			long scopeId,
			string operationName,
			Guid ownerId,
			int timeoutMinutes,
			CancellationToken cancellationToken)
		{
			if (timeoutMinutes <= 0)
			{
				return false;
			}

			await EnsureConnectionOpenAsync(dbContext, cancellationToken).ConfigureAwait(false);
			await using var command = dbContext.Database.GetDbConnection().CreateCommand();
			command.Transaction = dbTransaction;
			command.CommandText = $@"UPDATE {requestsTable}
				SET scope_id = @scope_id,
					operation_name = @operation_name,
					owner_id = @owner_id,
					lease_expires_at = (CURRENT_TIMESTAMP + (@timeout_minutes * INTERVAL '1 minute')),
					status = 0,
					response = NULL,
					error_code = NULL,
					error_message = NULL,
					created_at = CURRENT_TIMESTAMP,
					completed_at = NULL
				WHERE request_id = @request_id
					AND status = 0
					AND lease_expires_at < CURRENT_TIMESTAMP
				RETURNING 1;";

			AddParameter(command, "@request_id", requestId);
			AddParameter(command, "@scope_id", scopeId);
			AddParameter(command, "@operation_name", operationName);
			AddParameter(command, "@owner_id", ownerId);
			AddParameter(command, "@timeout_minutes", timeoutMinutes);

			var timeoutSeconds = dbContext.Database.GetCommandTimeout() ?? 0;
			if (timeoutSeconds > 0)
			{
				command.CommandTimeout = timeoutSeconds;
			}

			var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
			return result != null && result != DBNull.Value;
		}

		private static async Task<IdempotencyState> TryBeginIdempotentRequestAsync(
			NpgsqlDbContext dbContext,
			DbTransaction? dbTransaction,
			string requestsTable,
			Guid requestId,
			long scopeId,
			string operationName,
			Guid ownerId,
			int leaseTimeoutMinutes,
			CancellationToken cancellationToken)
		{
			await EnsureConnectionOpenAsync(dbContext, cancellationToken).ConfigureAwait(false);
			await using var command = dbContext.Database.GetDbConnection().CreateCommand();
			command.Transaction = dbTransaction;
			command.CommandText = $@"WITH inserted AS (
								INSERT INTO {requestsTable} (request_id, scope_id, operation_name, status, owner_id, lease_expires_at, response, error_code, error_message, created_at, completed_at)
								VALUES (@request_id, @scope_id, @operation_name, 0, @owner_id, (CURRENT_TIMESTAMP + (@lease_minutes * INTERVAL '1 minute')), NULL, NULL, NULL, CURRENT_TIMESTAMP, NULL)
								ON CONFLICT (request_id) DO NOTHING
								RETURNING 1 AS inserted
								)
								SELECT
									COALESCE((SELECT inserted FROM inserted), 0) AS inserted,
									scope_id,
									operation_name,
									owner_id,
									lease_expires_at,
									status,
									response,
									error_code,
									error_message
								FROM {requestsTable}
								WHERE request_id = @request_id
								LIMIT 1;";

			AddParameter(command, "@request_id", requestId);
			AddParameter(command, "@scope_id", scopeId);
			AddParameter(command, "@operation_name", operationName);
			AddParameter(command, "@owner_id", ownerId);
			AddParameter(command, "@lease_minutes", Math.Max(1, leaseTimeoutMinutes));

			var timeoutSeconds = dbContext.Database.GetCommandTimeout() ?? 0;
			if (timeoutSeconds > 0)
			{
				command.CommandTimeout = timeoutSeconds;
			}

			await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
			if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
			{
				throw new InvalidOperationException("Unable to resolve idempotent request state.");
			}

			var insertedFlag = Convert.ToInt32(reader.GetValue(0)) == 1;
			var existingScopeId = Convert.ToInt64(reader.GetValue(1));
			var existingOperationName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
			var existingOwnerId = reader.IsDBNull(3) ? Guid.Empty : reader.GetGuid(3);
			var existingLeaseExpiresAt = reader.IsDBNull(4) ? DateTime.MinValue : reader.GetDateTime(4);
			var existingStatus = Convert.ToByte(reader.GetValue(5));
			var existingResponse = reader.IsDBNull(6) ? null : reader.GetString(6);
			var existingErrorCode = reader.IsDBNull(7) ? null : reader.GetString(7);
			var existingErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8);
			return new IdempotencyState(insertedFlag, existingScopeId, existingOperationName, existingOwnerId, existingLeaseExpiresAt, existingStatus, existingResponse, existingErrorCode, existingErrorMessage);
		}

		private static async Task<int> RefreshIdempotencyLeaseAsync(
			NpgsqlDbContext dbContext,
			DbTransaction? dbTransaction,
			string requestsTable,
			Guid requestId,
			Guid ownerId,
			int leaseTimeoutMinutes,
			CancellationToken cancellationToken)
		{
			await EnsureConnectionOpenAsync(dbContext, cancellationToken).ConfigureAwait(false);
			await using var command = dbContext.Database.GetDbConnection().CreateCommand();
			command.Transaction = dbTransaction;
			command.CommandText = $@"UPDATE {requestsTable}
				SET lease_expires_at = (CURRENT_TIMESTAMP + (@lease_minutes * INTERVAL '1 minute'))
				WHERE request_id = @request_id
					AND owner_id = @owner_id
					AND status = 0;";

			var timeoutSeconds = dbContext.Database.GetCommandTimeout() ?? 0;
			if (timeoutSeconds > 0)
			{
				command.CommandTimeout = timeoutSeconds;
			}

			AddParameter(command, "@request_id", requestId);
			AddParameter(command, "@owner_id", ownerId);
			AddParameter(command, "@lease_minutes", Math.Max(1, leaseTimeoutMinutes));

			return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
		}

		private static async Task<int> UpdateProcessedRequestAsync(
			NpgsqlDbContext dbContext,
			DbTransaction? dbTransaction,
			string requestsTable,
			Guid requestId,
			long scopeId,
			string operationName,
			Guid ownerId,
			byte status,
			string? responseJson,
			string? errorCode,
			string? errorMessage,
			CancellationToken cancellationToken)
		{
			await EnsureConnectionOpenAsync(dbContext, cancellationToken).ConfigureAwait(false);
			await using var command = dbContext.Database.GetDbConnection().CreateCommand();
			command.Transaction = dbTransaction;
			command.CommandText = $@"UPDATE {requestsTable}
				SET status = @status,
					completed_at = CURRENT_TIMESTAMP,
					lease_expires_at = CURRENT_TIMESTAMP,
					response = @response,
					error_code = @error_code,
					error_message = @error_message
				WHERE request_id = @request_id
					AND scope_id = @scope_id
					AND operation_name = @operation_name
					AND owner_id = @owner_id
					AND status = 0;";

			var timeoutSeconds = dbContext.Database.GetCommandTimeout() ?? 0;
			if (timeoutSeconds > 0)
			{
				command.CommandTimeout = timeoutSeconds;
			}

			AddParameter(command, "@request_id", requestId);
			AddParameter(command, "@scope_id", scopeId);
			AddParameter(command, "@operation_name", operationName);
			AddParameter(command, "@owner_id", ownerId);
			AddParameter(command, "@status", status);
			AddJsonbParameter(command, "@response", responseJson);
			AddParameter(command, "@error_code", (object?)errorCode ?? DBNull.Value);
			AddParameter(command, "@error_message", (object?)errorMessage ?? DBNull.Value);

			return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
		}

		private static void AddJsonbParameter(DbCommand command, string name, string? json)
		{
			var parameter = command.CreateParameter();
			parameter.ParameterName = name;
			parameter.Value = json == null ? DBNull.Value : json;

			if (parameter is NpgsqlParameter npgsqlParameter)
			{
				npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
			}

			command.Parameters.Add(parameter);
		}

		private static void AddParameter(DbCommand command, string name, object value)
		{
			var parameter = command.CreateParameter();
			parameter.ParameterName = name;
			parameter.Value = value ?? DBNull.Value;
			command.Parameters.Add(parameter);
		}

		/// <summary>
		/// Best-effort cleanup helper for the processed_requests idempotency table.
		///
		/// Idempotency responses are stored as jsonb (see ProcessedRequestEntityConfiguration). If responses can be large,
		/// keep retention short and regularly purge old rows to avoid table/index bloat.
		/// </summary>
		/// <param name="retention">How long to retain completed/in-progress requests.</param>
		/// <param name="maxRows">Maximum number of rows to delete per call.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Number of rows deleted.</returns>
		protected Task<DatabaseResult<int>> CleanupProcessedRequestsAsync(
			TimeSpan retention,
			int maxRows = 5000,
			CancellationToken cancellationToken = default)
		{
			if (retention <= TimeSpan.Zero)
			{
				return Task.FromResult(DatabaseResult<int>.Failure("VALIDATION_ERROR", "Retention must be greater than 0."));
			}

			if (maxRows <= 0)
			{
				return Task.FromResult(DatabaseResult<int>.Failure("VALIDATION_ERROR", "MaxRows must be greater than 0."));
			}

			var retentionSeconds = (long)Math.Ceiling(retention.TotalSeconds);
			if (retentionSeconds <= 0)
			{
				return Task.FromResult(DatabaseResult<int>.Failure("VALIDATION_ERROR", "Retention must be greater than 0."));
			}

			return ExecuteInternalAsync(
				"CleanupProcessedRequests",
				async (dbContext, transaction, ct) =>
				{
					var requestsTable = dbContext.GetTableName<ProcessedRequestEntity>();
					await EnsureConnectionOpenAsync(dbContext, ct).ConfigureAwait(false);
					await using var command = dbContext.Database.GetDbConnection().CreateCommand();
					// Concurrency notes:
					// - Cleanup may run on multiple nodes. Use a transaction-scoped advisory lock so only one node
					//   performs work per attempt (no lock leakage risk with pooled connections).
					// - Use FOR UPDATE SKIP LOCKED so overlapping cleanup attempts (or other writers) don't block.
					// - Perform selection + deletion in one statement for atomicity and efficiency.
					command.CommandText = $@"WITH cleanup_lock AS (
						SELECT pg_try_advisory_xact_lock(@cleanup_lock_key) AS got
					), candidates AS (
						SELECT request_id
						FROM {requestsTable}
						WHERE (SELECT got FROM cleanup_lock)
						  AND (
								(completed_at IS NOT NULL AND completed_at < (CURRENT_TIMESTAMP - (@retention_seconds * INTERVAL '1 second')))
							 OR (completed_at IS NULL AND lease_expires_at < (CURRENT_TIMESTAMP - (@retention_seconds * INTERVAL '1 second')))
						  )
						ORDER BY created_at, request_id
						LIMIT @max_rows
						FOR UPDATE SKIP LOCKED
					), deleted AS (
						DELETE FROM {requestsTable} pr
						USING candidates c
						WHERE pr.request_id = c.request_id
						RETURNING 1
					)
					SELECT COUNT(*) FROM deleted;";

					var timeoutSeconds = dbContext.Database.GetCommandTimeout() ?? 0;
					if (timeoutSeconds > 0)
					{
						command.CommandTimeout = timeoutSeconds;
					}

					AddParameter(command, "@cleanup_lock_key", ProcessedRequestsCleanupLockKey);
					AddParameter(command, "@retention_seconds", retentionSeconds);
					AddParameter(command, "@max_rows", maxRows);

					var scalar = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
					var rows = scalar is int i ? i : Convert.ToInt32(scalar);
					return DatabaseResult<int>.Success(rows);
				},
				useTransaction: false,
				cancellationToken: cancellationToken);
		}
	}
}