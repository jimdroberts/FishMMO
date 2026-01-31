using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Monitoring.Diagnostics;
using FishMMO.Database.Npgsql.Monitoring.Health;
using FishMMO.Database.Npgsql.Monitoring.Metrics;
using FishMMO.Database.Exceptions;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Base class for database services that execute EF Core operations with consistent
	/// execution behavior (transactional and read-only), retry behavior for transient failures, and standardized
	/// error mapping into <see cref="DatabaseResult"/>.
	/// </summary>
	/// <typeparam name="TEntity">
	/// The entity type this service primarily operates on.
	/// </typeparam>
	/// <remarks>
	/// <para>
	/// This base type provides two primary execution paths:
	/// </para>
	/// <list type="bullet">
	/// <item>
	/// <description>
	/// <see cref="ExecuteTransactionAsync(System.Func{FishMMO.Database.Npgsql.NpgsqlDbContext,System.Threading.Tasks.Task},string,System.Threading.CancellationToken)"/> and
	/// <see cref="ExecuteTransactionAsync{TResult}(System.Func{FishMMO.Database.Npgsql.NpgsqlDbContext,System.Threading.Tasks.Task{TResult}},string,System.Threading.CancellationToken)"/>
	/// create a fresh <see cref="NpgsqlDbContext"/>, begin an explicit transaction, execute the delegate,
	/// then call <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/> and commit.
	/// </description>
	/// </item>
	/// <item>
	/// <description>
	/// <see cref="ExecuteReadAsync(System.Func{FishMMO.Database.Npgsql.NpgsqlDbContext,System.Threading.Tasks.Task},string,System.Threading.CancellationToken)"/> and
	/// <see cref="ExecuteReadAsync{TResult}(System.Func{FishMMO.Database.Npgsql.NpgsqlDbContext,System.Threading.Tasks.Task{TResult}},string,System.Threading.CancellationToken)"/>
	/// create a fresh <see cref="NpgsqlDbContext"/> but do not start an explicit transaction and do not call SaveChanges.
	/// </description>
	/// </item>
	/// </list>
	/// <para>
	/// A new context is created per attempt to avoid EF change-tracker state leaking across retries.
	/// Technical concurrency conflicts (e.g., <see cref="DbUpdateConcurrencyException"/>) and transient database failures may be retried.
	/// Logical stale-state conflicts (<see cref="StaleStateException"/>) are never retried.
	/// </para>
	/// </remarks>
	public abstract class BaseService<TEntity> where TEntity : class
	{
		/// <summary>
		/// Factory used to create new <see cref="NpgsqlDbContext"/> instances.
		/// </summary>
		/// <remarks>
		/// The factory is expected to be thread-safe and to return independent contexts.
		/// Each retry attempt uses a new context instance.
		/// </remarks>
		protected readonly INpgsqlDbContextFactory DbContextFactory;

		/// <summary>
		/// Database table name for <typeparamref name="TEntity"/>, resolved from EF Core model metadata.
		/// </summary>
		/// <remarks>
		/// Cached at construction time to avoid repeating model metadata lookups on hot paths.
		/// </remarks>
		protected readonly string TableName;

		/// <summary>
		/// Gets the connection pool metrics exposed by the current <see cref="INpgsqlDbContextFactory"/>.
		/// </summary>
		protected ConnectionPoolMetrics PoolMetrics => DbContextFactory.PoolMetrics;

		/// <summary>
		/// Gets the configured maximum pool size for utilization calculations.
		/// </summary>
		protected int MaxPoolSize => DbContextFactory.MaxPoolSize;

		/// <summary>
		/// Gets the query performance tracker used for operation-level timing and slow-query detection.
		/// </summary>
		protected QueryPerformanceTracker PerformanceTracker => DbContextFactory.PerformanceTracker;

		/// <summary>
		/// Maximum number of attempts for retryable operations.
		/// </summary>
		private const int MaxRetries = 3;

		/// <summary>
		/// Initializes the service and caches the table name for <typeparamref name="TEntity"/>.
		/// </summary>
		/// <param name="contextFactory">Factory used to create new EF Core contexts.</param>
		/// <exception cref="ArgumentNullException"><paramref name="contextFactory"/> is null.</exception>
		protected BaseService(INpgsqlDbContextFactory contextFactory)
		{
			DbContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));

			// Cache table name once at construction - dispose context immediately
			using var dbContext = DbContextFactory.CreateDbContext();
			TableName = dbContext.GetTableName<TEntity>();
		}

		/// <summary>
		/// Executes a database operation with a fresh context and transaction.
		/// Retries technical concurrency conflicts and transient database failures.
		/// Returns a <see cref="DatabaseResult"/> with success or failure information.
		/// </summary>
		/// <param name="action">The database operation to execute within the transaction.</param>
		/// <param name="operationName">Operation name for metrics; defaults to caller member name.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult"/> describing the outcome.</returns>
		/// <remarks>
		/// A new context is created per attempt to avoid EF change-tracker state leaking across retries.
		/// This wrapper begins an explicit transaction and always calls <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/> on success.
		/// Prefer <see cref="ExecuteReadAsync(System.Func{FishMMO.Database.Npgsql.NpgsqlDbContext,System.Threading.Tasks.Task},string,System.Threading.CancellationToken)"/>
		/// for query-only methods to avoid unnecessary transaction and SaveChanges overhead.
		/// <see cref="StaleStateException"/> is treated as a logical conflict and is not retried.
		/// </remarks>
		protected async Task<DatabaseResult> ExecuteTransactionAsync(
			Func<NpgsqlDbContext, Task> action,
			[CallerMemberName] string operationName = null,
			CancellationToken cancellationToken = default)
		{
			var resolvedOperationName = ResolveOperationName(operationName);

			for (int attempt = 1; attempt <= MaxRetries; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var stopwatch = PerformanceTracker?.StartTracking();
				var attemptStartUtc = stopwatch == null ? DateTime.UtcNow : default;

				// New context clears the cache and ensures we get fresh data on retry
				using var context = DbContextFactory.CreateDbContext();

				// New transaction required because Postgres kills the transaction on failure
				using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

				try
				{
					await action(context);
					await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
					await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
					RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: true);
					return DatabaseResult.Success(); // Success!
				}
				catch (Exception ex)
				{
					// Rollback the dead transaction immediately
					try { await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); } catch { /* Ignore socket errors */ }

					var sqlState = TryGetPostgresSqlState(ex);
					RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: false);

					// If the failure indicates "too many connections", count it as pool exhaustion.
					if (sqlState == "53300")
					{
						PoolMetrics?.RecordPoolExhaustion();
					}
					else if (IsConnectionSqlState(sqlState))
					{
						PoolMetrics?.RecordConnectionError();
					}

					// Logic failures (Stale Version) should NEVER be retried
					if (ex is StaleStateException)
					{
						return DatabaseResult.Failure("STALE_STATE", ex.Message, isTransient: false);
					}

					// Technical failures (xmin conflict, deadlock, timeout) are retryable
					if (attempt < MaxRetries && (ex is DbUpdateConcurrencyException || IsTransientDatabaseFailure(ex, sqlState)))
					{
						// Linear backoff with jitter to help the DB recover
						await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken).ConfigureAwait(false);
						continue;
					}

					// Max retries reached or non-retryable error (e.g., Name Taken)
					return HandleFinalException(ex, sqlState);
				}
			}

			return DatabaseResult.Failure("MAX_RETRIES", "Maximum retry attempts exceeded", isTransient: true);
		}

		/// <summary>
		/// Executes a database operation with a fresh context and transaction, returning a result.
		/// Retries technical concurrency conflicts and transient database failures.
		/// Returns a <see cref="DatabaseResult{TResult}"/> with success or failure information.
		/// </summary>
		/// <typeparam name="TResult">The result type returned by the operation.</typeparam>
		/// <param name="action">The database operation to execute within the transaction.</param>
		/// <param name="operationName">Operation name for metrics; defaults to caller member name.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult{TResult}"/> describing the outcome.</returns>
		/// <remarks>
		/// A new context is created per attempt to avoid EF change-tracker state leaking across retries.
		/// This wrapper begins an explicit transaction and always calls <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/> on success.
		/// Prefer <see cref="ExecuteReadAsync{TResult}(System.Func{FishMMO.Database.Npgsql.NpgsqlDbContext,System.Threading.Tasks.Task{TResult}},string,System.Threading.CancellationToken)"/>
		/// for query-only methods to avoid unnecessary transaction and SaveChanges overhead.
		/// <see cref="StaleStateException"/> is treated as a logical conflict and is not retried.
		/// </remarks>
		protected async Task<DatabaseResult<TResult>> ExecuteTransactionAsync<TResult>(
			Func<NpgsqlDbContext, Task<TResult>> action,
			[CallerMemberName] string operationName = null,
			CancellationToken cancellationToken = default)
		{
			TResult result = default!;
			var resolvedOperationName = ResolveOperationName(operationName);

			for (int attempt = 1; attempt <= MaxRetries; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var stopwatch = PerformanceTracker?.StartTracking();
				var attemptStartUtc = stopwatch == null ? DateTime.UtcNow : default;

				using var context = DbContextFactory.CreateDbContext();
				using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

				try
				{
					result = await action(context);
					await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
					await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
					RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: true);
					return DatabaseResult<TResult>.Success(result);
				}
				catch (Exception ex)
				{
					try { await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); } catch { }

					var sqlState = TryGetPostgresSqlState(ex);
					RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: false);

					if (sqlState == "53300")
					{
						PoolMetrics?.RecordPoolExhaustion();
					}
					else if (IsConnectionSqlState(sqlState))
					{
						PoolMetrics?.RecordConnectionError();
					}

					if (ex is StaleStateException)
					{
						return DatabaseResult<TResult>.Failure("STALE_STATE", ex.Message, isTransient: false);
					}

					if (attempt < MaxRetries && (ex is DbUpdateConcurrencyException || IsTransientDatabaseFailure(ex, sqlState)))
					{
						await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken).ConfigureAwait(false);
						continue;
					}

					return HandleFinalException<TResult>(ex, sqlState);
				}
			}

			return DatabaseResult<TResult>.Failure("MAX_RETRIES", "Maximum retry attempts exceeded", isTransient: true);
		}

		/// <summary>
		/// Executes a read-only database operation with a fresh context.
		/// Does not create an explicit transaction and does not call SaveChanges.
		/// Retries transient failures and maps exceptions into <see cref="DatabaseResult"/>.
		/// </summary>
		/// <param name="action">The read-only operation to execute.</param>
		/// <param name="operationName">Operation name for metrics; defaults to caller member name.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult"/> describing the outcome.</returns>
		/// <remarks>
		/// Use this for queries (Exists/Load/Get/Fetch) to avoid unnecessary transaction overhead.
		/// A new context is created per attempt to avoid state leaking across retries.
		/// Prefer <see cref="EntityFrameworkQueryableExtensions.AsNoTracking{TEntity}(System.Linq.IQueryable{TEntity})"/> for pure reads.
		/// </remarks>
		protected async Task<DatabaseResult> ExecuteReadAsync(
			Func<NpgsqlDbContext, Task> action,
			[CallerMemberName] string operationName = null,
			CancellationToken cancellationToken = default)
		{
			var resolvedOperationName = ResolveOperationName(operationName);

			for (int attempt = 1; attempt <= MaxRetries; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var stopwatch = PerformanceTracker?.StartTracking();
				var attemptStartUtc = stopwatch == null ? DateTime.UtcNow : default;

				using var context = DbContextFactory.CreateDbContext();

				try
				{
					await action(context).ConfigureAwait(false);
					RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: true);
					return DatabaseResult.Success();
				}
				catch (Exception ex)
				{
					var sqlState = TryGetPostgresSqlState(ex);
					RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: false);

					if (sqlState == "53300")
					{
						PoolMetrics?.RecordPoolExhaustion();
					}
					else if (IsConnectionSqlState(sqlState))
					{
						PoolMetrics?.RecordConnectionError();
					}

					if (ex is StaleStateException)
					{
						return DatabaseResult.Failure("STALE_STATE", ex.Message, isTransient: false);
					}

					if (attempt < MaxRetries && IsTransientDatabaseFailure(ex, sqlState))
					{
						await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken).ConfigureAwait(false);
						continue;
					}

					return HandleFinalException(ex, sqlState);
				}
			}

			return DatabaseResult.Failure("MAX_RETRIES", "Maximum retry attempts exceeded", isTransient: true);
		}

		/// <summary>
		/// Executes a read-only database operation with a fresh context and returns a value.
		/// Does not create an explicit transaction and does not call SaveChanges.
		/// Retries transient failures and maps exceptions into <see cref="DatabaseResult{TResult}"/>.
		/// </summary>
		/// <typeparam name="TResult">Result type returned by the operation.</typeparam>
		/// <param name="action">The read-only operation to execute.</param>
		/// <param name="operationName">Operation name for metrics; defaults to caller member name.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult{TResult}"/> describing the outcome.</returns>
		/// <remarks>
		/// Use this for query hot paths. Pair with compiled queries (EF.CompileAsyncQuery) where it helps.
		/// Prefer <see cref="EntityFrameworkQueryableExtensions.AsNoTracking{TEntity}(System.Linq.IQueryable{TEntity})"/> for pure reads.
		/// </remarks>
		protected async Task<DatabaseResult<TResult>> ExecuteReadAsync<TResult>(
			Func<NpgsqlDbContext, Task<TResult>> action,
			[CallerMemberName] string operationName = null,
			CancellationToken cancellationToken = default)
		{
			var resolvedOperationName = ResolveOperationName(operationName);

			for (int attempt = 1; attempt <= MaxRetries; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var stopwatch = PerformanceTracker?.StartTracking();
				var attemptStartUtc = stopwatch == null ? DateTime.UtcNow : default;

				using var context = DbContextFactory.CreateDbContext();

				try
				{
					var result = await action(context).ConfigureAwait(false);
					RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: true);
					return DatabaseResult<TResult>.Success(result);
				}
				catch (Exception ex)
				{
					var sqlState = TryGetPostgresSqlState(ex);
					RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: false);

					if (sqlState == "53300")
					{
						PoolMetrics?.RecordPoolExhaustion();
					}
					else if (IsConnectionSqlState(sqlState))
					{
						PoolMetrics?.RecordConnectionError();
					}

					if (ex is StaleStateException)
					{
						return DatabaseResult<TResult>.Failure("STALE_STATE", ex.Message, isTransient: false);
					}

					if (attempt < MaxRetries && IsTransientDatabaseFailure(ex, sqlState))
					{
						await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken).ConfigureAwait(false);
						continue;
					}

					return HandleFinalException<TResult>(ex, sqlState);
				}
			}

			return DatabaseResult<TResult>.Failure("MAX_RETRIES", "Maximum retry attempts exceeded", isTransient: true);
		}

		/// <summary>
		/// Resolves a stable operation name for metrics and diagnostics.
		/// </summary>
		/// <param name="operationName">The caller-provided operation name.</param>
		/// <returns>A name of the form "{ServiceType}.{MemberName}".</returns>
		private string ResolveOperationName(string operationName)
		{
			var memberName = string.IsNullOrWhiteSpace(operationName) ? "Execute" : operationName;
			return GetType().Name + "." + memberName;
		}

		/// <summary>
		/// Records the duration and success/failure of an operation attempt.
		/// </summary>
		/// <param name="operationName">Resolved operation name.</param>
		/// <param name="stopwatch">Stopwatch used to measure duration, if available.</param>
		/// <param name="attemptStartUtc">Start time used when stopwatch is not available.</param>
		/// <param name="success">Whether the attempt succeeded.</param>
		private void RecordOperationAttempt(string operationName, Stopwatch? stopwatch, DateTime attemptStartUtc, bool success)
		{
			try
			{
				var duration = stopwatch != null ? stopwatch.Elapsed : DateTime.UtcNow - attemptStartUtc;
				PerformanceTracker?.RecordQuery(operationName, duration, success);
			}
			catch
			{
				// Monitoring must never break core database execution.
			}
		}

		/// <summary>
		/// Maps a terminal (non-retryable) exception into a standardized <see cref="DatabaseResult"/>.
		/// </summary>
		/// <param name="ex">The exception.</param>
		/// <param name="sqlState">PostgreSQL SQLSTATE, if available.</param>
		/// <returns>A failure result.</returns>
		private DatabaseResult HandleFinalException(Exception ex, string? sqlState)
		{
			var (code, message, isTransient) = MapFinalException(ex, sqlState);
			return DatabaseResult.Failure(code, message, isTransient);
		}

		/// <summary>
		/// Maps a terminal (non-retryable) exception into a standardized <see cref="DatabaseResult{TResult}"/>.
		/// </summary>
		/// <typeparam name="TResult">Result type.</typeparam>
		/// <param name="ex">The exception.</param>
		/// <param name="sqlState">PostgreSQL SQLSTATE, if available.</param>
		/// <returns>A failure result.</returns>
		private DatabaseResult<TResult> HandleFinalException<TResult>(Exception ex, string? sqlState)
		{
			var (code, message, isTransient) = MapFinalException(ex, sqlState);
			return DatabaseResult<TResult>.Failure(code, message, isTransient);
		}

		/// <summary>
		/// Maps a terminal (non-retryable) exception into a standardized failure code/message/transience tuple.
		/// </summary>
		/// <param name="ex">The exception.</param>
		/// <param name="sqlState">PostgreSQL SQLSTATE, if available.</param>
		/// <returns>A tuple of (Code, Message, IsTransient).</returns>
		private static (string Code, string Message, bool IsTransient) MapFinalException(Exception ex, string? sqlState)
		{
			if (ex is OperationCanceledException)
			{
				return ("DB_CANCELED", "The database operation was canceled.", false);
			}

			// 23505 = unique_violation
			if (sqlState == "23505")
			{
				return ("UNIQUE_VIOLATION", "The record already exists.", false);
			}

			if (ex is ArgumentException argEx)
			{
				return ("INVALID_ARGUMENT", argEx.Message, false);
			}

			if (ex is InvalidOperationException invEx)
			{
				return ("INVALID_OPERATION", invEx.Message, false);
			}

			if (ex is DatabaseEntityNotFoundException notFoundEx)
			{
				return ("ENTITY_NOT_FOUND", notFoundEx.Message, false);
			}

			if (ex is DatabasePersistenceException persistEx)
			{
				return ("PERSISTENCE_FAILED", persistEx.Message, true);
			}

			return ("DATABASE_ERROR", ex.Message, true);
		}

		/// <summary>
		/// Extracts the PostgreSQL SQLSTATE from an exception chain, if present.
		/// </summary>
		/// <param name="exception">The exception to inspect.</param>
		/// <returns>The SQLSTATE code, or null if not found.</returns>
		private static string? TryGetPostgresSqlState(Exception exception)
		{
			for (var current = exception; current != null; current = current.InnerException)
			{
				if (current is PostgresException pgEx) return pgEx.SqlState;
			}
			return null;
		}

		/// <summary>
		/// Determines whether an exception represents a transient failure that is safe to retry.
		/// </summary>
		/// <param name="exception">The exception.</param>
		/// <param name="sqlState">The PostgreSQL SQLSTATE, if available.</param>
		/// <returns>True if the failure is considered transient; otherwise false.</returns>
		/// <remarks>
		/// Cancellation is never treated as transient.
		/// Transience is determined from <see cref="NpgsqlException.IsTransient"/>, well-known SQLSTATE codes,
		/// and certain exception types such as <see cref="TimeoutException"/>.
		/// </remarks>
		private static bool IsTransientDatabaseFailure(Exception exception, string? sqlState)
		{
			if (exception is OperationCanceledException) return false;

			for (var current = exception; current != null; current = current.InnerException)
			{
				if (current is NpgsqlException npgsqlEx && npgsqlEx.IsTransient) return true;
			}

			if (exception is TimeoutException) return true;

			if (!string.IsNullOrWhiteSpace(sqlState))
			{
				return IsTimeoutSqlState(sqlState) || IsConnectionSqlState(sqlState) || IsTransientSqlState(sqlState);
			}

			return false;
		}

		/// <summary>
		/// Determines whether a SQLSTATE represents a query cancellation/timeout.
		/// </summary>
		private static bool IsTimeoutSqlState(string? sqlState) =>
			string.Equals(sqlState, "57014", StringComparison.Ordinal);

		/// <summary>
		/// Determines whether a SQLSTATE represents a connection-level failure.
		/// </summary>
		private static bool IsConnectionSqlState(string? sqlState)
		{
			if (string.IsNullOrWhiteSpace(sqlState)) return false;
			return sqlState.StartsWith("08", StringComparison.Ordinal)
				|| sqlState == "57P01" || sqlState == "57P02" || sqlState == "57P03";
		}

		/// <summary>
		/// Determines whether a SQLSTATE represents a transient, retryable server-side failure.
		/// </summary>
		private static bool IsTransientSqlState(string? sqlState)
		{
			if (string.IsNullOrWhiteSpace(sqlState)) return false;
			return sqlState == "40P01" || sqlState == "40001" || sqlState == "55P03" || sqlState == "53300";
		}

		/// <summary>
		/// Validates and applies an incoming logical version onto an entity.
		/// </summary>
		/// <param name="entity">The tracked entity being updated or inserted.</param>
		/// <param name="incomingVersion">
		/// The version provided by the caller. Values &lt;= 0 are treated as "legacy/no version" and are ignored.
		/// </param>
		/// <remarks>
		/// For inserts (<c>entity.ID &lt;= 0</c>), a positive <paramref name="incomingVersion"/> is applied.
		/// For updates, the incoming version must be strictly greater than the stored version.
		/// </remarks>
		/// <exception cref="ArgumentNullException"><paramref name="entity"/> is null.</exception>
		/// <exception cref="StaleStateException">
		/// Thrown when <paramref name="incomingVersion"/> is not greater than the entity's current version.
		/// </exception>
		protected void ValidateVersion(IVersionedEntity entity, long incomingVersion)
		{
			if (entity == null) throw new ArgumentNullException(nameof(entity));

			// Allow legacy callers during rollout.
			if (incomingVersion <= 0) return;

			// New entity (insert): accept incoming version and stamp it.
			if (entity.ID <= 0)
			{
				entity.Version = incomingVersion;
				return;
			}

			if (entity.Version >= incomingVersion)
			{
				throw new StaleStateException(
					$"Version mismatch on {entity.GetType().Name}! " +
					$"DB: {entity.Version}, Incoming: {incomingVersion}.");
			}

			entity.Version = incomingVersion;
		}

		/// <summary>
		/// Executes a bulk UPSERT statement and enforces version/authority semantics by validating the affected row count.
		/// </summary>
		/// <param name="dbContext">The active DbContext for the current transaction.</param>
		/// <param name="sql">
		/// A fully-formed SQL statement (typically using UNNEST + INSERT ... ON CONFLICT DO UPDATE) built with <see cref="TableName"/>.
		/// The SQL should be parameterized for values and must never accept user-controlled identifiers.
		/// </param>
		/// <param name="expectedRowsAffected">
		/// The number of rows that must be inserted or updated for the operation to be considered successful.
		/// Callers should pre-filter inputs (e.g., skip non-active characters) so this expectation is stable.
		/// </param>
		/// <param name="parameters">SQL parameters to pass to EF Core.</param>
		/// <param name="staleStateMessage">Message used when version/authority is lost.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="dbContext"/> or <paramref name="sql"/> is null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expectedRowsAffected"/> is negative.</exception>
		/// <exception cref="StaleStateException">
		/// Thrown when fewer than <paramref name="expectedRowsAffected"/> rows were affected, indicating that at least one incoming row
		/// was rejected by version gating (e.g., <c>EXCLUDED.version &lt;= table.version</c>).
		/// </exception>
		protected static async Task ExecuteBulkUpsertAsync(
			NpgsqlDbContext dbContext,
			string sql,
			int expectedRowsAffected,
			object[] parameters,
			string staleStateMessage,
			CancellationToken cancellationToken)
		{
			if (dbContext == null) throw new ArgumentNullException(nameof(dbContext));
			if (sql == null) throw new ArgumentNullException(nameof(sql));
			if (expectedRowsAffected < 0) throw new ArgumentOutOfRangeException(nameof(expectedRowsAffected));

			if (expectedRowsAffected == 0)
			{
				return;
			}

			var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken)
				.ConfigureAwait(false);

			if (rowsAffected != expectedRowsAffected)
			{
				throw new StaleStateException(staleStateMessage);
			}
		}
	}
}