using System;
using System.Data.Common;
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
	/// Base class for all Npgsql database services.
	/// Provides common functionality: context creation, exception handling, and execution strategies.
	/// Follows DRY principle by centralizing repeated patterns across all services.
	/// </summary>
	/// <typeparam name="TEntity">The entity type this service operates on.</typeparam>
	public abstract class BaseService<TEntity> where TEntity : class
	{
		private static string? cachedTableName;
		private static long processedRequestsCleanupLastRunTicks;

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

			// Cache table name once at construction - dispose context immediately
			if (cachedTableName == null)
			{
				using var dbContext = DbContextFactory.CreateDbContext();
				cachedTableName = dbContext.GetTableName<TEntity>();
			}
			TableName = cachedTableName;
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

			var nowTicks = DateTime.UtcNow.Ticks;
			var lastTicks = Interlocked.Read(ref processedRequestsCleanupLastRunTicks);
			if (minInterval > TimeSpan.Zero && nowTicks - lastTicks < minInterval.Ticks)
			{
				return;
			}

			if (Interlocked.CompareExchange(ref processedRequestsCleanupLastRunTicks, nowTicks, lastTicks) != lastTicks)
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
			for (var attempt = 0; attempt <= maxTransientRetryCount; attempt++)
			{
				await using var dbContext = DbContextFactory.CreateDbContext();
				IDbContextTransaction? transaction = null;
				try
				{
					cancellationToken.ThrowIfCancellationRequested();
					onAttemptStart?.Invoke();

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
			Func<NpgsqlDbContext, Task<TResult>> operation,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));

			return ExecuteWorkAsync(
				async (dbContext, ct) => DatabaseResult<TResult>.Success(await operation(dbContext)),
				operationName,
				cancellationToken);
		}

		/// <summary>
		/// Unified protected entrypoint for executing non-transactional operations with no return value.
		/// </summary>
		protected async Task<DatabaseResult> ExecuteSqlAsync(
			Func<NpgsqlDbContext, Task> operation,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));

			var result = await ExecuteWorkAsync(
				async (dbContext, ct) =>
				{
					await operation(dbContext);
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
			Func<NpgsqlDbContext, IDbContextTransaction, Task<TResult>> operation,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));

			return ExecuteWorkInTransactionAsync(
				async (dbContext, transaction, ct) => DatabaseResult<TResult>.Success(await operation(dbContext, transaction)),
				operationName,
				cancellationToken);
		}

		/// <summary>
		/// Unified protected entrypoint for executing transactional operations with no return value.
		/// </summary>
		protected async Task<DatabaseResult> ExecuteSqlAsync(
			Func<NpgsqlDbContext, IDbContextTransaction, Task> operation,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));

			var result = await ExecuteWorkInTransactionAsync(
				async (dbContext, transaction, ct) =>
				{
					await operation(dbContext, transaction);
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
			Func<NpgsqlDbContext, IDbContextTransaction, Task<TResult>> operation,
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
			Func<NpgsqlDbContext, IDbContextTransaction, Task<TResult>> operation,
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

					var data = await operation(dbContext, transaction!);
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
			return ex switch
			{
				// Pass through DatabaseExceptions unchanged (already sanitized)
				DatabaseException dbEx => dbEx,


				OperationCanceledException cancelEx => new DatabaseOperationCanceledException(
					operationName,
					cancelEx),

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

				NpgsqlException npgsqlEx => new DatabaseConnectionException(
					GetSafeConnectionIdentifier(dbContext),
					npgsqlEx),

				DbUpdateException dbUpdateEx => new DatabaseQueryException(
					operationName,
					"Database update failed.",
					dbUpdateEx.Message,
					false,
					null,
					dbUpdateEx),

				_ => new DatabaseQueryException(
					operationName,
					"An unexpected database error occurred.",
					ex.Message,
					false,
					null,
					ex)
			};
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
			return await ExecuteSqlAsync(async dbContext =>
			{
				var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);

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