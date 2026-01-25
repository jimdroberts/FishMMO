using System;
using System.Data;
using System.Data.Common;
using System.Threading;
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
	/// Base service that adds idempotency support via the processed_requests table.
	/// </summary>
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
			maxIdempotencyOperationNameLength = Math.Max(1, settings.MaxIdempotencyOperationNameLength);
			processedRequestsRetentionDays = Math.Max(0, settings.ProcessedRequestsRetentionDays);
			processedRequestsCleanupMaxRows = Math.Max(1, settings.ProcessedRequestsCleanupMaxRows);
			processedRequestsCleanupMinIntervalMinutes = Math.Max(0, settings.ProcessedRequestsCleanupMinIntervalMinutes);
			processedRequestsInProgressTimeoutMinutes = Math.Max(0, settings.ProcessedRequestsInProgressTimeoutMinutes);
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
					CancellationToken.None).ConfigureAwait(false);
			}
			catch
			{
				// Best-effort maintenance only.
			}
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
			public IdempotencyState(bool didInsert, long accountId, string operationName, byte status, string? response, string? errorCode, string? errorMessage)
			{
				DidInsert = didInsert;
				AccountId = accountId;
				OperationName = operationName;
				Status = status;
				Response = response;
				ErrorCode = errorCode;
				ErrorMessage = errorMessage;
			}

			public bool DidInsert { get; }
			public long AccountId { get; }
			public string OperationName { get; }
			public byte Status { get; }
			public string? Response { get; }
			public string? ErrorCode { get; }
			public string? ErrorMessage { get; }
		}

		/// <summary>
		/// Unified protected entrypoint for executing idempotent operations.
		/// </summary>
		protected async Task<DatabaseResult<TResult>> ExecuteIdempotentAsync<TResult>(
			Guid requestId,
			long accountId,
			string operationName,
			Func<NpgsqlDbContext, IDbContextTransaction, CancellationToken, Task<TResult>> operation,
			CancellationToken cancellationToken = default)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));

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

			await MaybeCleanupProcessedRequestsAsync().ConfigureAwait(false);

			// Acquire or read idempotency state in a durable, non-transactional step.
			// This must not be rolled back by the business operation transaction.
			var beginResult = await ExecuteInternalAsync(
				$"{operationName}.IdempotencyBegin",
				async (dbContext, _, ct) =>
				{
					var requestsTable = dbContext.GetTableName<ProcessedRequestEntity>();
					var state = await TryBeginIdempotentRequestAsync(
						dbContext,
						dbTransaction: null,
						requestsTable,
						requestId,
						accountId,
						operationName,
						ct).ConfigureAwait(false);

					if (!state.DidInsert && state.AccountId != accountId)
					{
						return DatabaseResult<IdempotencyState>.Failure("IDEMPOTENCY_MISMATCH", "RequestId is already in use.");
					}

					if (!state.DidInsert && !string.Equals(state.OperationName, operationName, StringComparison.Ordinal))
					{
						return DatabaseResult<IdempotencyState>.Failure("IDEMPOTENCY_MISMATCH", "RequestId is already in use.");
					}

					// If another node is currently processing this request and it isn't stale, fail fast.
					if (!state.DidInsert && state.Status == 0 && string.IsNullOrWhiteSpace(state.Response))
					{
						if (processedRequestsInProgressTimeoutMinutes <= 0)
						{
							return DatabaseResult<IdempotencyState>.Failure("IDEMPOTENCY_IN_PROGRESS", "This request is already being processed.");
						}

						var tookOver = await TryTakeOverStaleIdempotentRequestAsync(
							dbContext,
							dbTransaction: null,
							requestsTable,
							requestId,
							accountId,
							operationName,
							processedRequestsInProgressTimeoutMinutes,
							ct).ConfigureAwait(false);

						if (!tookOver)
						{
							return DatabaseResult<IdempotencyState>.Failure("IDEMPOTENCY_IN_PROGRESS", "This request is already being processed.");
						}

						state = new IdempotencyState(true, accountId, operationName, status: 0, response: null, errorCode: null, errorMessage: null);
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
				if (TryGetCachedIdempotencyResponse<TResult>(beginState.Status, beginState.Response, beginState.ErrorCode, beginState.ErrorMessage, out var cached))
				{
					return cached;
				}
			}

			// Run the business operation and mark completion within the SAME transaction.
			// This ensures we never commit side effects without also persisting the cached response.
			return await ExecuteInternalAsync(
				operationName,
				async (dbContext, transaction, ct) =>
				{
					var requestsTable = dbContext.GetTableName<ProcessedRequestEntity>();

					// Re-check state in the operation transaction so transient retries do not double-execute.
					var state = await TryBeginIdempotentRequestAsync(
						dbContext,
						transaction?.GetDbTransaction(),
						requestsTable,
						requestId,
						accountId,
						operationName,
						ct).ConfigureAwait(false);

					if (!state.DidInsert)
					{
						if (TryGetCachedIdempotencyResponse<TResult>(state.Status, state.Response, state.ErrorCode, state.ErrorMessage, out var cached))
						{
							return cached;
						}
					}

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
						accountId,
						operationName,
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
						return;
					}

					// Best-effort: persist failure to enable deterministic retries.
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
						await UpdateProcessedRequestAsync(
							dbContext,
							dbTransaction: null,
							requestsTable,
							requestId,
							accountId,
							operationName,
							status: 2,
							failureJson,
							dbEx.ErrorCode,
							dbEx.SafeMessage,
							CancellationToken.None).ConfigureAwait(false);
					}
					catch
					{
						// Best-effort only.
					}
				}).ConfigureAwait(false);
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
				result = DatabaseResult<TResult>.Failure("IDEMPOTENCY_COMPLETED", "This request has already completed.");
				return true;
			}

			return false;
		}

		private static async Task<bool> TryTakeOverStaleIdempotentRequestAsync(
			NpgsqlDbContext dbContext,
			DbTransaction? dbTransaction,
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

			await EnsureConnectionOpenAsync(dbContext, cancellationToken).ConfigureAwait(false);
			await using var command = dbContext.Database.GetDbConnection().CreateCommand();
			command.Transaction = dbTransaction;
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
			long accountId,
			string operationName,
			CancellationToken cancellationToken)
		{
			await EnsureConnectionOpenAsync(dbContext, cancellationToken).ConfigureAwait(false);
			await using var command = dbContext.Database.GetDbConnection().CreateCommand();
			command.Transaction = dbTransaction;
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
			var existingAccountId = Convert.ToInt64(reader.GetValue(1));
			var existingOperationName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
			var existingStatus = Convert.ToByte(reader.GetValue(3));
			var existingResponse = reader.IsDBNull(4) ? null : reader.GetString(4);
			var existingErrorCode = reader.IsDBNull(5) ? null : reader.GetString(5);
			var existingErrorMessage = reader.IsDBNull(6) ? null : reader.GetString(6);
			return new IdempotencyState(insertedFlag, existingAccountId, existingOperationName, existingStatus, existingResponse, existingErrorCode, existingErrorMessage);
		}

		private static async Task<int> UpdateProcessedRequestAsync(
			NpgsqlDbContext dbContext,
			DbTransaction? dbTransaction,
			string requestsTable,
			Guid requestId,
			long accountId,
			string operationName,
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
					response = @response,
					error_code = @error_code,
					error_message = @error_message
				WHERE request_id = @request_id
					AND account_id = @account_id
					AND operation_name = @operation_name
					AND status = 0;";

			var timeoutSeconds = dbContext.Database.GetCommandTimeout() ?? 0;
			if (timeoutSeconds > 0)
			{
				command.CommandTimeout = timeoutSeconds;
			}

			AddParameter(command, "@request_id", requestId);
			AddParameter(command, "@account_id", accountId);
			AddParameter(command, "@operation_name", operationName);
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

			var cutoff = DateTime.UtcNow.Subtract(retention);

			return ExecuteInternalAsync(
				"CleanupProcessedRequests",
				async (dbContext, _, ct) =>
				{
					var requestsTable = dbContext.GetTableName<ProcessedRequestEntity>();
					await EnsureConnectionOpenAsync(dbContext, ct).ConfigureAwait(false);
					await using var command = dbContext.Database.GetDbConnection().CreateCommand();
					command.CommandText = $@"DELETE FROM {requestsTable}
						WHERE request_id IN (
							SELECT request_id
							FROM {requestsTable}
							WHERE (completed_at IS NOT NULL AND completed_at < @cutoff)
							   OR (completed_at IS NULL AND created_at < @cutoff)
							ORDER BY created_at
							LIMIT @max_rows
						);";

					var timeoutSeconds = dbContext.Database.GetCommandTimeout() ?? 0;
					if (timeoutSeconds > 0)
					{
						command.CommandTimeout = timeoutSeconds;
					}

					AddParameter(command, "@cutoff", cutoff);
					AddParameter(command, "@max_rows", maxRows);

					var rows = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
					return DatabaseResult<int>.Success(rows);
				},
				useTransaction: false,
				cancellationToken: cancellationToken);
		}
	}
}