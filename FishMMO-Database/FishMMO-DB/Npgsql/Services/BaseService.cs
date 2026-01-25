using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using FishMMO.Database.Exceptions;

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
		#region Fields
		/// <summary>
		/// Factory for creating database context instances with proper connection pooling and retry configuration.
		/// </summary>
		protected readonly INpgsqlDbContextFactory DbContextFactory;

		/// <summary>
		/// The cached table name for the entity type. Resolved once at construction.
		/// </summary>
		protected readonly string TableName;
		#endregion

		#region Construction

		/// <summary>
		/// Initializes a new instance of BaseService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		protected BaseService(INpgsqlDbContextFactory dbContextFactory)
		{
			DbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));

			// Cache table name once at construction - dispose context immediately
			using var dbContext = DbContextFactory.CreateDbContext();
			TableName = dbContext.GetTableName<TEntity>();
		}
		#endregion

		#region Execution

		/// <summary>
		/// Unified execution engine for all database operations.
		/// Handles context lifetime, optional explicit transactions, exception mapping, and provider-configured retries.
		/// </summary>
		/// <typeparam name="TResult">The result data type.</typeparam>
		/// <param name="operationName">Name of the operation for error reporting.</param>
		/// <param name="operation">Operation body. Should return a <see cref="DatabaseResult{T}"/> rather than throwing for expected failures.</param>
		/// <param name="useTransaction">Whether to wrap the operation in an explicit transaction.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <param name="onFailureAsync">
		/// Optional best-effort hook invoked when an exception is mapped.
		/// The <c>willRetry</c> argument is a best-effort signal indicating whether the configured execution strategy
		/// expects to retry the failure.
		/// </param>
		/// <returns>A <see cref="DatabaseResult{T}"/> describing success or failure.</returns>
		protected async Task<DatabaseResult<TResult>> ExecuteInternalAsync<TResult>(
			string operationName,
			Func<NpgsqlDbContext, IDbContextTransaction?, CancellationToken, Task<DatabaseResult<TResult>>> operation,
			bool useTransaction = false,
			CancellationToken cancellationToken = default,
			Func<NpgsqlDbContext, DatabaseException, bool, CancellationToken, Task>? onFailureAsync = null)
		{
			var performanceTracker = DbContextFactory.PerformanceTracker;
			Stopwatch? stopwatch = null;
			Exception? lastAttemptException = null;
			DatabaseException? lastAttemptMappedException = null;

			async Task InvokeFailureHookAsync(DatabaseException dbException, bool willRetry, CancellationToken hookToken)
			{
				if (onFailureAsync == null)
				{
					return;
				}

				try
				{
					// Always run failure hooks on a fresh context. The attempt context may have
					// a broken connection or an invalid internal state after an exception.
					await using var failureContext = DbContextFactory.CreateDbContext();
					await onFailureAsync(failureContext, dbException, willRetry, hookToken).ConfigureAwait(false);
				}
				catch
				{
					// Best-effort only. Never hide the original failure.
				}
			}

			try
			{
				stopwatch = performanceTracker?.StartTracking();

				// Create a context once to obtain the provider-configured execution strategy.
				// The actual work must use a fresh DbContext per attempt to avoid retries
				// reusing a potentially corrupted change tracker/connection state.
				await using var bootstrapContext = DbContextFactory.CreateDbContext();
				var strategy = bootstrapContext.Database.CreateExecutionStrategy();

				var result = await strategy.ExecuteAsync<DatabaseResult<TResult>>(async ct =>
				{
					await using var attemptContext = DbContextFactory.CreateDbContext();
					try
					{
						ct.ThrowIfCancellationRequested();

						if (!useTransaction)
						{
							return await operation(attemptContext, null, ct).ConfigureAwait(false);
						}

						await using var transaction = await attemptContext.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
						try
						{
							var operationResult = await operation(attemptContext, transaction, ct).ConfigureAwait(false);

							if (operationResult.IsSuccess)
							{
								await transaction.CommitAsync(ct).ConfigureAwait(false);
							}
							else
							{
								await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
							}

							return operationResult;
						}
						catch
						{
							await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
							throw;
						}
					}
					catch (Exception ex)
					{
						lastAttemptException = ex;
						lastAttemptMappedException = MapException(ex, operationName, attemptContext);
						var willRetry = strategy.RetriesOnFailure && lastAttemptMappedException.IsTransient;

						if (willRetry)
						{
							await InvokeFailureHookAsync(lastAttemptMappedException, willRetry: true, ct).ConfigureAwait(false);
						}

						throw;
					}
				}, cancellationToken).ConfigureAwait(false);

				if (stopwatch != null)
				{
					stopwatch.Stop();
					performanceTracker?.RecordQuery(operationName, stopwatch.Elapsed, result.IsSuccess);
				}

				return result;
			}
			catch (Exception ex)
			{
				if (stopwatch != null)
				{
					stopwatch.Stop();
					performanceTracker?.RecordQuery(operationName, stopwatch.Elapsed, success: false);
				}

				DatabaseException dbEx;
				if (ex == lastAttemptException && lastAttemptMappedException != null)
				{
					dbEx = lastAttemptMappedException;
				}
				else
				{
					// Map using a fresh context to ensure we can read provider settings safely.
					await using var mappingContext = DbContextFactory.CreateDbContext();
					dbEx = MapException(ex, operationName, mappingContext);
				}

				// The native execution strategy has already exhausted retries by this point.
				await InvokeFailureHookAsync(dbEx, willRetry: false, cancellationToken).ConfigureAwait(false);

				return DatabaseResult<TResult>.FromException(dbEx);
			}
		}


		/// <summary>
		/// Unified protected entrypoint for executing non-transactional operations.
		/// </summary>
		protected Task<DatabaseResult<TResult>> ExecuteAsync<TResult>(
			Func<NpgsqlDbContext, CancellationToken, Task<TResult>> operation,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));

			return ExecuteInternalAsync(
				operationName,
				async (dbContext, _, ct) => DatabaseResult<TResult>.Success(await operation(dbContext, ct).ConfigureAwait(false)),
				useTransaction: false,
				cancellationToken: cancellationToken);
		}

		/// <summary>
		/// Unified protected entrypoint for executing transactional operations.
		/// </summary>
		protected Task<DatabaseResult<TResult>> ExecuteTransactionAsync<TResult>(
			Func<NpgsqlDbContext, IDbContextTransaction, CancellationToken, Task<TResult>> operation,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));

			return ExecuteInternalAsync(
				operationName,
				async (dbContext, transaction, ct) => DatabaseResult<TResult>.Success(await operation(dbContext, transaction!, ct).ConfigureAwait(false)),
				useTransaction: true,
				cancellationToken: cancellationToken);
		}
		#endregion

		#region Exception Mapping

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

			// Npgsql already identifies transient failures.
			for (var current = exception; current != null; current = current.InnerException)
			{
				if (current is NpgsqlException npgsqlEx)
				{
					return npgsqlEx.IsTransient;
				}
			}

			if (exception is TimeoutException)
			{
				return true;
			}

			// Fallback for wrapped PostgresException cases where NpgsqlException isn't directly visible.
			if (!string.IsNullOrWhiteSpace(sqlState))
			{
				return IsTimeoutSqlState(sqlState) || IsConnectionSqlState(sqlState) || IsTransientSqlState(sqlState);
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
		#endregion

		#region Raw SQL Helpers

		/// <summary>
		/// Executes a raw SQL command with automatic retry strategy and optional row validation.
		/// </summary>
		/// <param name="sql">
		/// The SQL query to execute.
		/// The SQL text may embed identifiers (e.g., <see cref="TableName"/>) but must use parameter placeholders
		/// (<c>{0}</c>, <c>{1}</c>, ...) for values.
		/// </param>
		/// <param name="operationName">Name of the operation for error reporting.</param>
		/// <param name="parameters">SQL parameter values for placeholders (<c>{0}</c>, <c>{1}</c>, ...).</param>
		/// <param name="entityName">Name of the entity for error message (used when requireRowsAffected is true).</param>
		/// <param name="entityId">ID of the entity for error message (used when requireRowsAffected is true).</param>
		/// <param name="requireRowsAffected">If true, throws <see cref="DatabaseEntityNotFoundException"/> when no rows are affected.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult with the number of rows affected.</returns>
		/// <remarks>
		/// This method automatically:
		/// - Applies transient-only retry logic
		/// - Parameterizes value placeholders via EF Core (prevents SQL injection)
		/// - Validates rows affected if required
		/// - Maps exceptions to appropriate DatabaseException types
		/// 
		/// Example usage:
		/// <code>
		/// var sql = $@"UPDATE {TableName} SET lastlogin = CURRENT_TIMESTAMP WHERE name = {{0}}";
		/// await ExecuteRawSqlAsync(
		///     sql,
		///     "UpdateLastLogin",
		///     new object[] { accountName },
		///     entityName: "Account",
		///     entityId: accountName,
		///     requireRowsAffected: true,
		///     cancellationToken: cancellationToken);
		/// </code>
		/// </remarks>
		protected Task<DatabaseResult<int>> ExecuteRawSqlAsync(
			string sql,
			string operationName,
			object[] parameters = null,
			string entityName = null,
			object entityId = null,
			bool requireRowsAffected = false,
			CancellationToken cancellationToken = default)
		{
			return ExecuteRawSqlInternalAsync(
				sql,
				operationName,
				parameters,
				entityName,
				entityId,
				requireRowsAffected,
				useTransaction: false,
				cancellationToken);
		}

		/// <summary>
		/// Executes a raw SQL command inside an explicit transaction, with automatic retry and optional row validation.
		/// </summary>
		/// <remarks>
		/// Prefer this helper when you need a single command executed under an explicit transaction.
		/// For multi-step transactional flows, keep using the transactional ExecuteTransactionAsync lambda wrapper.
		/// </remarks>
		protected Task<DatabaseResult<int>> ExecuteRawSqlTransactionAsync(
			string sql,
			string operationName,
			object[] parameters = null,
			string entityName = null,
			object entityId = null,
			bool requireRowsAffected = false,
			CancellationToken cancellationToken = default)
		{
			return ExecuteRawSqlInternalAsync(
				sql,
				operationName,
				parameters,
				entityName,
				entityId,
				requireRowsAffected,
				useTransaction: true,
				cancellationToken);
		}

		private Task<DatabaseResult<int>> ExecuteRawSqlInternalAsync(
			string sql,
			string operationName,
			object[] parameters,
			string entityName,
			object entityId,
			bool requireRowsAffected,
			bool useTransaction,
			CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(sql))
			{
				return Task.FromResult(DatabaseResult<int>.Failure("VALIDATION_ERROR", "SQL is required."));
			}

			return ExecuteInternalAsync(
				operationName,
				async (dbContext, _, ct) =>
				{
					var rowsAffected = parameters == null || parameters.Length == 0
						? await dbContext.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false)
						: await dbContext.Database.ExecuteSqlRawAsync(sql, parameters, ct).ConfigureAwait(false);

					if (requireRowsAffected && rowsAffected == 0)
					{
						throw new DatabaseEntityNotFoundException(
							entityName ?? "Entity",
							entityId?.ToString() ?? "unknown");
					}

					return DatabaseResult<int>.Success(rowsAffected);
				},
				useTransaction: useTransaction,
				cancellationToken: cancellationToken);
		}
		#endregion

		#region Entity Guards

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
		#endregion
	}
}