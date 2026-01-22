using System;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
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
	/// Base class for all Npgsql database services.
	/// Provides common functionality: context creation, exception handling, and execution strategies.
	/// Follows DRY principle by centralizing repeated patterns across all services.
	/// </summary>
	/// <typeparam name="TEntity">The entity type this service operates on.</typeparam>
	public abstract class BaseService<TEntity> where TEntity : class
	{
		private static readonly JsonSerializerOptions IdempotencyJsonOptions = new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};

		private readonly int maxTransientRetryCount;
		private readonly int baseRetryDelayMs;
		private readonly int maxRetryDelayMs;
		private readonly int maxIdempotencyOperationNameLength;
		private readonly int processedRequestsRetentionDays;
		private readonly int processedRequestsCleanupMaxRows;
		private readonly int processedRequestsCleanupMinIntervalMinutes;
		private readonly int processedRequestsInProgressTimeoutMinutes;

		/// <summary>
		/// Factory for creating database context instances with proper connection pooling and retry configuration.
		/// </summary>
		protected readonly INpgsqlDbContextFactory DbContextFactory;

		/// <summary>
		/// The cached table name for the entity type. Resolved once at construction.
		/// </summary>
		protected readonly string TableName;

		/// <summary>
		/// Initializes a new instance of BaseService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		protected BaseService(INpgsqlDbContextFactory dbContextFactory)
		{
			DbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));

			var settings = DbContextFactory.ServiceExecutionSettings ?? new DatabaseServiceExecutionSettings();
			maxTransientRetryCount = Math.Max(0, settings.MaxTransientRetryCount);
			baseRetryDelayMs = Math.Max(0, settings.BaseRetryDelayMs);
			maxRetryDelayMs = Math.Max(0, settings.MaxRetryDelayMs);
			maxIdempotencyOperationNameLength = Math.Max(1, settings.MaxIdempotencyOperationNameLength);
			processedRequestsRetentionDays = Math.Max(0, settings.ProcessedRequestsRetentionDays);
			processedRequestsCleanupMaxRows = Math.Max(1, settings.ProcessedRequestsCleanupMaxRows);
			processedRequestsCleanupMinIntervalMinutes = Math.Max(0, settings.ProcessedRequestsCleanupMinIntervalMinutes);
			processedRequestsInProgressTimeoutMinutes = Math.Max(0, settings.ProcessedRequestsInProgressTimeoutMinutes);

			// Cache table name once at construction - dispose context immediately
			using var dbContext = DbContextFactory.CreateDbContext();
			TableName = dbContext.GetTableName<TEntity>();
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
				await CleanupProcessedRequestsAsync(
					TimeSpan.FromDays(processedRequestsRetentionDays),
					processedRequestsCleanupMaxRows,
					CancellationToken.None);
			}
			catch
			{
				// Best-effort maintenance only.
			}
		}

		/// <summary>
		/// Unified execution engine for all database operations.
		/// Handles context lifetime, optional explicit transactions, exception mapping, and transient-only retries.
		/// </summary>
		/// <typeparam name="TResult">The result data type.</typeparam>
		/// <param name="operationName">Name of the operation for error reporting.</param>
		/// <param name="operation">Operation body. Should return a <see cref="DatabaseResult{T}"/> rather than throwing for expected failures.</param>
		/// <param name="useTransaction">Whether to wrap the operation in an explicit transaction.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <param name="onAttemptStart">Optional hook invoked at the start of each retry attempt.</param>
		/// <param name="onFailureAsync">Optional hook invoked after an exception is mapped (best-effort, never throws).</param>
		/// <returns>A <see cref="DatabaseResult{T}"/> describing success or failure.</returns>
		private async Task<DatabaseResult<TResult>> ExecuteInternalAsync<TResult>(
			string operationName,
			Func<NpgsqlDbContext, IDbContextTransaction?, CancellationToken, Task<DatabaseResult<TResult>>> operation,
			bool useTransaction = false,
			CancellationToken cancellationToken = default,
			Action? onAttemptStart = null,
			Func<NpgsqlDbContext, DatabaseException, bool, CancellationToken, Task>? onFailureAsync = null)
		{
			var performanceTracker = DbContextFactory.PerformanceTracker;

			for (var attempt = 0; attempt <= maxTransientRetryCount; attempt++)
			{
				await using var dbContext = DbContextFactory.CreateDbContext();
				IDbContextTransaction? transaction = null;
				Stopwatch? stopwatch = null;
				try
				{
					cancellationToken.ThrowIfCancellationRequested();
					onAttemptStart?.Invoke();
					stopwatch = performanceTracker?.StartTracking();

					if (useTransaction)
					{
						transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
					}

					try
					{
						var result = await operation(dbContext, transaction, cancellationToken);

						if (transaction != null)
						{
							if (result.IsSuccess)
							{
								await transaction.CommitAsync(cancellationToken);
							}
							else
							{
								await transaction.RollbackAsync(CancellationToken.None);
							}
						}

						if (stopwatch != null)
						{
							stopwatch.Stop();
							performanceTracker?.RecordQuery(operationName, stopwatch.Elapsed, result.IsSuccess);
						}

						return result;
					}
					catch
					{
						if (transaction != null)
						{
							await transaction.RollbackAsync(CancellationToken.None);
						}

						throw;
					}
				}
				catch (Exception ex)
				{
					if (stopwatch != null)
					{
						stopwatch.Stop();
						performanceTracker?.RecordQuery(operationName, stopwatch.Elapsed, success: false);
					}

					var dbEx = MapException(ex, operationName, dbContext);
					var willRetry = dbEx.IsTransient && attempt < maxTransientRetryCount;

					if (onFailureAsync != null)
					{
						try
						{
							await onFailureAsync(dbContext, dbEx, willRetry, cancellationToken);
						}
						catch
						{
							// Best-effort only. Never hide the original failure.
						}
					}

					if (!willRetry)
					{
						return DatabaseResult<TResult>.FromException(dbEx);
					}

					await Task.Delay(GetRetryDelay(attempt), cancellationToken);
				}
				finally
				{
					if (transaction != null)
					{
						await transaction.DisposeAsync();
					}
				}
			}

			return DatabaseResult<TResult>.Failure("DB_RETRY_FAILED", "Database operation failed after retries.", isTransient: true);
		}

		private TimeSpan GetRetryDelay(int attempt)
		{
			// Exponential backoff: base * 2^attempt, capped.
			var exponentialMs = baseRetryDelayMs * (1 << Math.Min(attempt, 10));
			var cappedMs = Math.Min(exponentialMs, maxRetryDelayMs);

			// Jitter: 0..50% additional delay.
			var jitterMs = RandomNumberGenerator.GetInt32(0, (cappedMs / 2) + 1);
			return TimeSpan.FromMilliseconds(cappedMs + jitterMs);
		}

		private sealed class IdempotencyEnvelope<T>
		{
			public bool IsSuccess { get; set; }
			public T Data { get; set; }
			public string ErrorCode { get; set; }
			public string ErrorMessage { get; set; }
			public bool IsTransient { get; set; }
		}

		private Task<DatabaseResult<TResult>> ExecuteWorkAsync<TResult>(
			Func<NpgsqlDbContext, CancellationToken, Task<DatabaseResult<TResult>>> work,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			if (work == null) throw new ArgumentNullException(nameof(work));

			return ExecuteInternalAsync(
				operationName,
				(dbContext, _, ct) => work(dbContext, ct),
				useTransaction: false,
				cancellationToken: cancellationToken);
		}

		private Task<DatabaseResult<TResult>> ExecuteWorkInTransactionAsync<TResult>(
			Func<NpgsqlDbContext, IDbContextTransaction, CancellationToken, Task<DatabaseResult<TResult>>> work,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			if (work == null) throw new ArgumentNullException(nameof(work));

			return ExecuteInternalAsync(
				operationName,
				(dbContext, transaction, ct) => work(dbContext, transaction!, ct),
				useTransaction: true,
				cancellationToken: cancellationToken);
		}

		/// <summary>
		/// Unified protected entrypoint for executing non-transactional operations.
		/// </summary>
		protected Task<DatabaseResult<TResult>> ExecuteSqlAsync<TResult>(
			Func<NpgsqlDbContext, CancellationToken, Task<TResult>> operation,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));

			return ExecuteWorkAsync(
				async (dbContext, ct) => DatabaseResult<TResult>.Success(await operation(dbContext, ct)),
				operationName,
				cancellationToken);
		}

		/// <summary>
		/// Unified protected entrypoint for executing non-transactional operations with no return value.
		/// </summary>
		protected async Task<DatabaseResult> ExecuteSqlAsync(
			Func<NpgsqlDbContext, CancellationToken, Task> operation,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));

			var result = await ExecuteWorkAsync(
				async (dbContext, ct) =>
				{
					await operation(dbContext, ct);
					return DatabaseResult<bool>.Success(true);
				},
				operationName,
				cancellationToken);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <summary>
		/// Unified protected entrypoint for executing transactional operations.
		/// </summary>
		protected Task<DatabaseResult<TResult>> ExecuteSqlAsync<TResult>(
			Func<NpgsqlDbContext, IDbContextTransaction, CancellationToken, Task<TResult>> operation,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));

			return ExecuteWorkInTransactionAsync(
				async (dbContext, transaction, ct) => DatabaseResult<TResult>.Success(await operation(dbContext, transaction, ct)),
				operationName,
				cancellationToken);
		}

		/// <summary>
		/// Unified protected entrypoint for executing transactional operations with no return value.
		/// </summary>
		protected async Task<DatabaseResult> ExecuteSqlAsync(
			Func<NpgsqlDbContext, IDbContextTransaction, CancellationToken, Task> operation,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));

			var result = await ExecuteWorkInTransactionAsync(
				async (dbContext, transaction, ct) =>
				{
					await operation(dbContext, transaction, ct);
					return DatabaseResult<bool>.Success(true);
				},
				operationName,
				cancellationToken);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <summary>
		/// Unified protected entrypoint for executing idempotent operations.
		/// </summary>
		protected Task<DatabaseResult<TResult>> ExecuteSqlAsync<TResult>(
			Guid requestId,
			long accountId,
			string operationName,
			Func<NpgsqlDbContext, IDbContextTransaction, CancellationToken, Task<TResult>> operation,
			CancellationToken cancellationToken = default)
		{
			return ExecuteIdempotentAsync(requestId, accountId, operationName, operation, cancellationToken);
		}

		/// <summary>
		/// Executes an operation with idempotency enforcement using the processed_requests table.
		/// If the same requestId is received again for the same account+operation, returns the cached response.
		/// </summary>
		/// <typeparam name="TResult">The operation result type.</typeparam>
		/// <param name="requestId">Client-provided idempotency key.</param>
		/// <param name="accountId">Account identifier associated with the request (used to prevent cross-tenant leaks).</param>
		/// <param name="operationName">Logical operation name (max 64 chars).</param>
		/// <param name="operation">The operation body to execute exactly-once per requestId.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Cached or newly computed result.</returns>
		private async Task<DatabaseResult<TResult>> ExecuteIdempotentAsync<TResult>(
			Guid requestId,
			long accountId,
			string operationName,
			Func<NpgsqlDbContext, IDbContextTransaction, CancellationToken, Task<TResult>> operation,
			CancellationToken cancellationToken = default)
		{
			if (requestId == Guid.Empty)
			{
				return DatabaseResult<TResult>.Failure("VALIDATION_ERROR", "RequestId is required.");
			}

			if (accountId <= 0)
			{
				return DatabaseResult<TResult>.Failure("VALIDATION_ERROR", "AccountId must be greater than 0.");
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

			await MaybeCleanupProcessedRequestsAsync();

			var didInsert = false;
			var requestsTableName = string.Empty;

			return await ExecuteInternalAsync(
				operationName,
				async (dbContext, transaction, ct) =>
				{
					didInsert = false;
					requestsTableName = dbContext.GetTableName<ProcessedRequestEntity>();

					var (insertedThisCall, existingAccountId, existingOperationName, status, existingResponse, errorCode, errorMessage) =
						await TryBeginIdempotentRequestAsync(
							dbContext,
							transaction!,
							requestsTableName,
							requestId,
							accountId,
							operationName,
							ct);

					didInsert = insertedThisCall;

					if (!insertedThisCall)
					{
						if (existingAccountId != accountId || !string.Equals(existingOperationName, operationName, StringComparison.Ordinal))
						{
							return DatabaseResult<TResult>.Failure(
								"IDEMPOTENCY_MISMATCH",
								"RequestId is already in use.");
						}

						// Prevent permanent request poisoning: if the request is stuck "in progress" (status=0)
						// due to a server crash, allow a later retry to reclaim it once it becomes stale.
						// This is done atomically in SQL (single UPDATE with a time predicate) so only one
						// caller can successfully take over the request.
						var tookOverStaleRequest = false;
						if (status == 0 && string.IsNullOrWhiteSpace(existingResponse) && processedRequestsInProgressTimeoutMinutes > 0)
						{
							tookOverStaleRequest = await TryTakeOverStaleIdempotentRequestAsync(
								dbContext,
								transaction!,
								requestsTableName,
								requestId,
								accountId,
								operationName,
								processedRequestsInProgressTimeoutMinutes,
								ct);

							if (tookOverStaleRequest)
							{
								didInsert = true;
							}
						}

						if (!tookOverStaleRequest)
						{

						if (status == 2)
						{
							if (!string.IsNullOrWhiteSpace(existingResponse))
							{
								IdempotencyEnvelope<TResult>? failureEnvelope = null;
								try
								{
									failureEnvelope = JsonSerializer.Deserialize<IdempotencyEnvelope<TResult>>(existingResponse, IdempotencyJsonOptions);
								}
								catch
								{
									// Fall back to error fields below.
								}

								if (failureEnvelope != null)
								{
									return failureEnvelope.IsSuccess
										? DatabaseResult<TResult>.Success(failureEnvelope.Data)
										: DatabaseResult<TResult>.Failure(
											failureEnvelope.ErrorCode,
											failureEnvelope.ErrorMessage,
											failureEnvelope.IsTransient);
								}
							}

							return DatabaseResult<TResult>.Failure(
								errorCode ?? "IDEMPOTENCY_FAILED",
								errorMessage ?? "This request previously failed.");
						}

						if (!string.IsNullOrWhiteSpace(existingResponse))
						{
							IdempotencyEnvelope<TResult>? envelope = null;
							try
							{
								envelope = JsonSerializer.Deserialize<IdempotencyEnvelope<TResult>>(existingResponse, IdempotencyJsonOptions);
							}
							catch
							{
								return DatabaseResult<TResult>.Failure(
									"IDEMPOTENCY_ERROR",
									"Cached idempotency response is invalid.");
							}

							if (envelope == null)
							{
								return DatabaseResult<TResult>.Failure(
									"IDEMPOTENCY_ERROR",
									"Cached idempotency response is missing.");
							}

							return envelope.IsSuccess
								? DatabaseResult<TResult>.Success(envelope.Data)
								: DatabaseResult<TResult>.Failure(envelope.ErrorCode, envelope.ErrorMessage, envelope.IsTransient);
						}

						if (status == 1)
						{
							return DatabaseResult<TResult>.Failure(
								"IDEMPOTENCY_COMPLETED",
								"This request has already completed.");
						}

						return DatabaseResult<TResult>.Failure(
							"IDEMPOTENCY_IN_PROGRESS",
							"This request is already being processed.");
						}
					}

					var data = await operation(dbContext, transaction!, ct);
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

					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {requestsTableName} 
						SET status = 1, completed_at = CURRENT_TIMESTAMP, response = {responseJson}, error_code = NULL, error_message = NULL
						WHERE request_id = {requestId}",
						ct);

					return DatabaseResult<TResult>.Success(data);
				},
				useTransaction: true,
				cancellationToken: cancellationToken,
				onAttemptStart: () => didInsert = false,
				onFailureAsync: async (dbContext, dbEx, willRetry, _) =>
				{
					if (willRetry || !didInsert)
					{
						return;
					}

					var requestsTable = string.IsNullOrWhiteSpace(requestsTableName)
						? dbContext.GetTableName<ProcessedRequestEntity>()
						: requestsTableName;

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

					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {requestsTable}
						SET status = 2, completed_at = CURRENT_TIMESTAMP, error_code = {dbEx.ErrorCode}, error_message = {dbEx.SafeMessage}, response = {failureJson}
						WHERE request_id = {requestId}",
						CancellationToken.None);
				});
		}

		/// <summary>
		/// Attempts to reclaim an idempotent request row that is stuck "in progress" (status=0) and has become stale.
		/// </summary>
		/// <remarks>
		/// This prevents permanent request-id poisoning after process crashes.
		/// The takeover is atomic (single UPDATE with time predicate) so only one caller succeeds.
		/// The row is reset to a fresh in-progress state with cleared response/error fields.
		/// </remarks>
		/// <param name="dbContext">Database context.</param>
		/// <param name="transaction">Transaction to bind the command to.</param>
		/// <param name="requestsTable">Fully-qualified processed_requests table identifier.</param>
		/// <param name="requestId">Request idempotency key.</param>
		/// <param name="accountId">Account id associated with the request.</param>
		/// <param name="operationName">Logical operation name.</param>
		/// <param name="timeoutMinutes">Staleness timeout in minutes.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>True if the caller successfully reclaimed the request; otherwise false.</returns>
		private static async Task<bool> TryTakeOverStaleIdempotentRequestAsync(
			NpgsqlDbContext dbContext,
			IDbContextTransaction transaction,
			string requestsTable,
			Guid requestId,
			long accountId,
			string operationName,
			int timeoutMinutes,
			CancellationToken cancellationToken)
		{
			if (timeoutMinutes <= 0)
			{
				return false;
			}

			await dbContext.Database.OpenConnectionAsync(cancellationToken);
			await using var command = dbContext.Database.GetDbConnection().CreateCommand();
			command.Transaction = transaction.GetDbTransaction();
			command.CommandText = $@"UPDATE {requestsTable}
				SET account_id = @account_id,
					operation_name = @operation_name,
					status = 0,
					response = NULL,
					error_code = NULL,
					error_message = NULL,
					created_at = CURRENT_TIMESTAMP,
					completed_at = NULL
				WHERE request_id = @request_id
					AND status = 0
					AND created_at < (CURRENT_TIMESTAMP - (@timeout_minutes * INTERVAL '1 minute'))
				RETURNING 1;";

			AddParameter(command, "@request_id", requestId);
			AddParameter(command, "@account_id", accountId);
			AddParameter(command, "@operation_name", operationName);
			AddParameter(command, "@timeout_minutes", timeoutMinutes);

			var result = await command.ExecuteScalarAsync(cancellationToken);
			return result != null && result != DBNull.Value;
		}

		private static async Task<(bool didInsert, long accountId, string operationName, byte status, string? response, string? errorCode, string? errorMessage)> TryBeginIdempotentRequestAsync(
			NpgsqlDbContext dbContext,
			IDbContextTransaction transaction,
			string requestsTable,
			Guid requestId,
			long accountId,
			string operationName,
			CancellationToken cancellationToken)
		{
			await dbContext.Database.OpenConnectionAsync(cancellationToken);
			await using var command = dbContext.Database.GetDbConnection().CreateCommand();
			command.Transaction = transaction.GetDbTransaction();
			command.CommandText = $@"WITH inserted AS (
										INSERT INTO {requestsTable} (request_id, account_id, operation_name, status, response, error_code, error_message, created_at, completed_at)
										VALUES (@request_id, @account_id, @operation_name, 0, NULL, NULL, NULL, CURRENT_TIMESTAMP, NULL)
										ON CONFLICT (request_id) DO NOTHING
										RETURNING 1 AS inserted
									)
									SELECT
										COALESCE((SELECT inserted FROM inserted), 0) AS inserted,
										account_id,
										operation_name,
										status,
										response,
										error_code,
										error_message
									FROM {requestsTable}
									WHERE request_id = @request_id
									LIMIT 1;";

			AddParameter(command, "@request_id", requestId);
			AddParameter(command, "@account_id", accountId);
			AddParameter(command, "@operation_name", operationName);

			await using var reader = await command.ExecuteReaderAsync(cancellationToken);
			if (!await reader.ReadAsync(cancellationToken))
			{
				throw new InvalidOperationException("Unable to resolve idempotent request state.");
			}

			var insertedFlag = Convert.ToInt32(reader.GetValue(0)) == 1;
			var existingAccountId = Convert.ToInt64(reader.GetValue(1));
			var existingOperationName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
			var existingStatus = Convert.ToByte(reader.GetValue(3));
			var existingResponse = reader.IsDBNull(4) ? null : reader.GetString(4);
			var existingErrorCode = reader.IsDBNull(5) ? null : reader.GetString(5);
			var existingErrorMessage = reader.IsDBNull(6) ? null : reader.GetString(6);
			return (insertedFlag, existingAccountId, existingOperationName, existingStatus, existingResponse, existingErrorCode, existingErrorMessage);
		}

		private static void AddParameter(DbCommand command, string name, object value)
		{
			var parameter = command.CreateParameter();
			parameter.ParameterName = name;
			parameter.Value = value ?? DBNull.Value;
			command.Parameters.Add(parameter);
		}

		/// <summary>
		/// Maps database exceptions to custom DatabaseException hierarchy with sanitized messages.
		/// Provides consistent exception handling across all services.
		/// </summary>
		/// <param name="ex">The exception to map.</param>
		/// <param name="operationName">Name of the operation for context.</param>
		/// <param name="dbContext">Database context for connection string retrieval.</param>
		/// <returns>Mapped DatabaseException.</returns>
		protected DatabaseException MapException(Exception ex, string operationName, NpgsqlDbContext dbContext)
		{
			var timeoutSeconds = GetCommandTimeoutSeconds(dbContext);
			var postgresSqlState = TryGetPostgresSqlState(ex);
			var isTransient = IsTransientDatabaseFailure(ex, postgresSqlState);

			return ex switch
			{
				// Pass through DatabaseExceptions unchanged (already sanitized)
				DatabaseException dbEx => dbEx,


				OperationCanceledException cancelEx => new DatabaseOperationCanceledException(
					operationName,
					cancelEx),

				TimeoutException timeoutEx => new DatabaseTimeoutException(
					operationName,
					timeoutSeconds,
					timeoutEx),

				PostgresException pgEx when pgEx.SqlState == "23505" => new DatabaseConstraintException(
					ConstraintType.Unique,
					pgEx.ConstraintName ?? "unknown_constraint",
					"A record with this unique value already exists.",
					pgEx),

				PostgresException pgEx when pgEx.SqlState == "23503" => new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					pgEx.ConstraintName ?? "unknown_constraint",
					"The referenced entity does not exist.",
					pgEx),

				PostgresException pgEx when IsTimeoutSqlState(pgEx.SqlState) => new DatabaseTimeoutException(
					operationName,
					timeoutSeconds,
					pgEx),

				PostgresException pgEx when IsConnectionSqlState(pgEx.SqlState) => new DatabaseConnectionException(
					GetSafeConnectionIdentifier(dbContext),
					pgEx),

				PostgresException pgEx when IsTransientSqlState(pgEx.SqlState) => new DatabaseQueryException(
					operationName,
					"The database is temporarily unavailable. Please try again.",
					pgEx.Message,
					isTransient: true,
					postgreSqlErrorCode: pgEx.SqlState,
					innerException: pgEx),

				NpgsqlException npgsqlEx => new DatabaseConnectionException(
					GetSafeConnectionIdentifier(dbContext),
					npgsqlEx),

				DbUpdateException dbUpdateEx => new DatabaseQueryException(
					operationName,
					"Database update failed.",
					dbUpdateEx.Message,
					isTransient,
					postgresSqlState,
					dbUpdateEx),

				_ => new DatabaseQueryException(
					operationName,
					"An unexpected database error occurred.",
					ex.Message,
					isTransient,
					postgresSqlState,
					ex)
			};
		}

		/// <summary>
		/// Attempts to extract a PostgreSQL SQLSTATE from the exception chain.
		/// </summary>
		/// <param name="exception">Exception to inspect.</param>
		/// <returns>SQLSTATE code if present; otherwise null.</returns>
		private static string? TryGetPostgresSqlState(Exception exception)
		{
			for (var current = exception; current != null; current = current.InnerException)
			{
				if (current is PostgresException pgEx)
				{
					return pgEx.SqlState;
				}
			}

			return null;
		}

		/// <summary>
		/// Determines whether a failure is transient (retryable) based on exception type and SQLSTATE.
		/// </summary>
		/// <param name="exception">Exception to inspect.</param>
		/// <param name="sqlState">Extracted SQLSTATE, if available.</param>
		/// <returns>True if the failure is considered transient; otherwise false.</returns>
		private static bool IsTransientDatabaseFailure(Exception exception, string? sqlState)
		{
			if (exception is OperationCanceledException)
			{
				return false;
			}

			if (exception is TimeoutException)
			{
				return true;
			}

			if (!string.IsNullOrWhiteSpace(sqlState))
			{
				return IsTimeoutSqlState(sqlState) || IsConnectionSqlState(sqlState) || IsTransientSqlState(sqlState);
			}

			if (exception is NpgsqlException)
			{
				return true;
			}

			return false;
		}

		/// <summary>
		/// Determines whether a SQLSTATE represents a statement timeout or query cancel.
		/// </summary>
		/// <param name="sqlState">PostgreSQL SQLSTATE code.</param>
		/// <returns>True if the SQLSTATE is considered a timeout.</returns>
		private static bool IsTimeoutSqlState(string? sqlState)
		{
			// 57014 = query_canceled (includes canceling statement due to statement timeout)
			return string.Equals(sqlState, "57014", StringComparison.Ordinal);
		}

		/// <summary>
		/// Determines whether a SQLSTATE indicates a connection-level failure.
		/// </summary>
		/// <param name="sqlState">PostgreSQL SQLSTATE code.</param>
		/// <returns>True if the SQLSTATE represents a connection-level failure.</returns>
		private static bool IsConnectionSqlState(string? sqlState)
		{
			if (string.IsNullOrWhiteSpace(sqlState))
			{
				return false;
			}

			// 08XXX = connection exception class
			if (sqlState.StartsWith("08", StringComparison.Ordinal))
			{
				return true;
			}

			// 57P01/57P02/57P03 = shutdown/crash/cannot_connect_now (often transient during restarts/failovers)
			return string.Equals(sqlState, "57P01", StringComparison.Ordinal)
				|| string.Equals(sqlState, "57P02", StringComparison.Ordinal)
				|| string.Equals(sqlState, "57P03", StringComparison.Ordinal);
		}

		/// <summary>
		/// Determines whether a SQLSTATE should be treated as transient (retryable).
		/// </summary>
		/// <param name="sqlState">PostgreSQL SQLSTATE code.</param>
		/// <returns>True if the SQLSTATE is considered retryable.</returns>
		private static bool IsTransientSqlState(string? sqlState)
		{
			if (string.IsNullOrWhiteSpace(sqlState))
			{
				return false;
			}

			// 40P01 = deadlock_detected
			// 40001 = serialization_failure
			// 55P03 = lock_not_available
			// 53300 = too_many_connections
			return string.Equals(sqlState, "40P01", StringComparison.Ordinal)
				|| string.Equals(sqlState, "40001", StringComparison.Ordinal)
				|| string.Equals(sqlState, "55P03", StringComparison.Ordinal)
				|| string.Equals(sqlState, "53300", StringComparison.Ordinal);
		}

		/// <summary>
		/// Attempts to read the configured command timeout (seconds) from the DbContext connection string.
		/// Returns 0 when unavailable.
		/// </summary>
		/// <param name="dbContext">DbContext to extract connection string from.</param>
		/// <returns>Command timeout in seconds, or 0 if unknown.</returns>
		private static int GetCommandTimeoutSeconds(NpgsqlDbContext dbContext)
		{
			try
			{
				var connectionString = dbContext?.Database.GetConnectionString();
				if (string.IsNullOrWhiteSpace(connectionString))
				{
					return 0;
				}

				var builder = new NpgsqlConnectionStringBuilder(connectionString);
				return builder.CommandTimeout;
			}
			catch
			{
				return 0;
			}
		}

		/// <summary>
		/// Creates a safe, non-sensitive connection identifier for logging.
		/// Never returns the raw connection string.
		/// </summary>
		/// <param name="dbContext">The database context used to retrieve connection details.</param>
		/// <returns>A redacted identifier such as "host:port/database" or "unknown".</returns>
		private static string GetSafeConnectionIdentifier(NpgsqlDbContext dbContext)
		{
			try
			{
				var connectionString = dbContext?.Database.GetConnectionString();
				if (string.IsNullOrWhiteSpace(connectionString))
				{
					return "unknown";
				}

				var builder = new NpgsqlConnectionStringBuilder(connectionString);
				var host = string.IsNullOrWhiteSpace(builder.Host) ? "unknown" : builder.Host;
				var database = string.IsNullOrWhiteSpace(builder.Database) ? "unknown" : builder.Database;
				var port = builder.Port <= 0 ? 5432 : builder.Port;
				return $"{host}:{port}/{database}";
			}
			catch
			{
				return "unknown";
			}
		}

		/// <summary>
		/// Executes a SQL command with automatic retry strategy and optional row validation.
		/// Provides a clean abstraction over ExecuteSqlInterpolatedAsync with built-in error handling.
		/// </summary>
		/// <param name="sql">The interpolated SQL query to execute (automatically parameterized).</param>
		/// <param name="operationName">Name of the operation for error reporting.</param>
		/// <param name="entityName">Name of the entity for error message (used when requireRowsAffected is true).</param>
		/// <param name="entityId">ID of the entity for error message (used when requireRowsAffected is true).</param>
		/// <param name="requireRowsAffected">If true, throws DatabaseEntityNotFoundException when no rows are affected.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult with the number of rows affected.</returns>
		/// <remarks>
		/// This method automatically:
		/// - Applies transient-only retry logic
		/// - Parameterizes the SQL query (FormattableString prevents SQL injection)
		/// - Validates rows affected if required
		/// - Maps exceptions to appropriate DatabaseException types
		/// 
		/// Example usage:
		/// <code>
		/// await ExecuteSqlAsync(
		///     $"UPDATE {TableName} SET lastlogin = CURRENT_TIMESTAMP WHERE name = {accountName}",
		///     "UpdateLastLogin",
		///     entityName: "Account",
		///     entityId: accountName,
		///     requireRowsAffected: true,
		///     cancellationToken: cancellationToken);
		/// </code>
		/// </remarks>
		protected async Task<DatabaseResult<int>> ExecuteSqlAsync(
			FormattableString sql,
			string operationName,
			string entityName = null,
			object entityId = null,
			bool requireRowsAffected = false,
			CancellationToken cancellationToken = default)
		{
			return await ExecuteSqlAsync(async (dbContext, ct) =>
			{
				var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync(sql, ct);

				if (requireRowsAffected && rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException(
						entityName ?? "Entity",
						entityId?.ToString() ?? "unknown");
				}

				return rowsAffected;
			}, operationName, cancellationToken);
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

			var cutoff = DateTime.UtcNow.Subtract(retention);

			return ExecuteWorkAsync(
				async (dbContext, ct) =>
				{
					var requestsTable = dbContext.GetTableName<ProcessedRequestEntity>();
					var rows = await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"
							DELETE FROM {requestsTable}
							WHERE request_id IN (
								SELECT request_id
								FROM {requestsTable}
								WHERE (completed_at IS NOT NULL AND completed_at < {cutoff})
								   OR (completed_at IS NULL AND created_at < {cutoff})
								ORDER BY created_at
								LIMIT {maxRows}
							)",
						ct);

					return DatabaseResult<int>.Success(rows);
				},
				"CleanupProcessedRequests",
				cancellationToken);
		}

		/// <summary>
		/// Ensures an entity exists.
		/// Throws a <see cref="DatabaseEntityNotFoundException"/> when the entity is null.
		/// </summary>
		/// <typeparam name="T">Entity type.</typeparam>
		/// <param name="entity">Entity instance returned from the database.</param>
		/// <param name="entityName">Entity name used for error reporting.</param>
		/// <param name="entityId">Entity identifier used for error reporting.</param>
		/// <returns>The non-null entity.</returns>
		/// <exception cref="DatabaseEntityNotFoundException">Thrown when <paramref name="entity"/> is null.</exception>
		protected static T RequireEntityExists<T>(T entity, string entityName, object entityId)
			where T : class
		{
			if (entity == null)
			{
				throw new DatabaseEntityNotFoundException(entityName, entityId?.ToString() ?? "unknown");
			}

			return entity;
		}
	}
}