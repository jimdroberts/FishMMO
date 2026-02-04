using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Monitoring.Diagnostics;
using FishMMO.Database.Npgsql.Monitoring.Metrics;
using FishMMO.Database.Exceptions;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Guards against nested database execution scopes within the same logical async flow.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This guard must be shared across all services, regardless of <c>TEntity</c> generic arguments.
	/// Therefore it is implemented as a non-generic static holder.
	/// </para>
	/// </remarks>
	internal static class DatabaseExecutionScope
	{
		private enum ExecutionMode
		{
			ReadOnly = 0,
			Write = 1,
			Transaction = 2,
		}

		private sealed class ScopeState
		{
			public int Depth;
			public ExecutionMode Mode;
			public NpgsqlDbContext? DbContext;
		}

		private static readonly AsyncLocal<ScopeState?> State = new AsyncLocal<ScopeState?>();

		public static bool TryGetCurrentDbContext(out NpgsqlDbContext dbContext)
		{
			var state = State.Value;
			if (state?.DbContext != null)
			{
				dbContext = state.DbContext;
				return true;
			}

			dbContext = null!;
			return false;
		}

		public static bool IsActive => State.Value?.Depth > 0;

		/// <summary>
		/// Enters a database execution scope for the current logical async flow.
		/// </summary>
		/// <returns>A token that must be disposed to exit the scope.</returns>
		/// <exception cref="DatabaseException">
		/// Thrown when a write scope is requested while the ambient scope is read-only.
		/// </exception>
		public static ScopeToken Enter(NpgsqlDbContext? dbContext, bool isTransactionScope)
		{
			var requestedMode = isTransactionScope ? ExecutionMode.Transaction : ExecutionMode.Write;
			var state = State.Value;
			if (state == null)
			{
				state = new ScopeState
				{
					Depth = 0,
					Mode = requestedMode,
					DbContext = dbContext,
				};
				State.Value = state;
			}
			else
			{
				if (state.Mode == ExecutionMode.ReadOnly && requestedMode != ExecutionMode.ReadOnly)
				{
					throw new DatabaseException(
						"A write operation was attempted inside an ambient read-only database scope. " +
						"Ensure the outermost scope is ExecuteWriteAsync/ExecuteTransactionAsync when writes are required.",
						"INVALID_OPERATION",
						isTransient: false);
				}

				// Promote mode from Write to Transaction when an inner scope requests it.
				// This is informational only; transaction ownership remains with the outermost transaction wrapper.
				if (requestedMode == ExecutionMode.Transaction && state.Mode == ExecutionMode.Write)
				{
					state.Mode = ExecutionMode.Transaction;
				}

				// Inner scopes never override the ambient DbContext.
			}

			state.Depth++;
			return new ScopeToken();
		}

		public static ScopeToken EnterReadOnly(NpgsqlDbContext? dbContext)
		{
			var state = State.Value;
			if (state == null)
			{
				state = new ScopeState
				{
					Depth = 0,
					Mode = ExecutionMode.ReadOnly,
					DbContext = dbContext,
				};
				State.Value = state;
			}
			else
			{
				// Read-only nested inside write/transaction is allowed and reuses the ambient context.
			}

			state.Depth++;
			return new ScopeToken();
		}

		/// <summary>
		/// Scope-exit token returned by <see cref="Enter"/>.
		/// </summary>
		public readonly struct ScopeToken : IDisposable
		{
			/// <inheritdoc />
			public void Dispose()
			{
				var state = State.Value;
				if (state == null)
				{
					return;
				}

				state.Depth = state.Depth <= 0 ? 0 : state.Depth - 1;
				if (state.Depth == 0)
				{
					state.DbContext = null;
					State.Value = null;
				}
			}
		}
	}

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
	/// This base type provides three primary execution paths:
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
	/// <see cref="ExecuteWriteAsync(System.Func{FishMMO.Database.Npgsql.NpgsqlDbContext,System.Threading.Tasks.Task},string,System.Threading.CancellationToken)"/> and
	/// <see cref="ExecuteWriteAsync{TResult}(System.Func{FishMMO.Database.Npgsql.NpgsqlDbContext,System.Threading.Tasks.Task{TResult}},string,System.Threading.CancellationToken)"/>
	/// create a fresh <see cref="NpgsqlDbContext"/>, execute the delegate, then call <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>
	/// without starting an explicit transaction.
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
		/// Transient database failures may be retried.
		/// Optimistic concurrency conflicts (Version-based authority) are never retried;
		/// they are returned as a non-transient stale-state result so the caller can re-read and decide how to proceed.
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
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Gets the factory used to create new <see cref="NpgsqlDbContext"/> instances.
		/// </summary>
		protected INpgsqlDbContextFactory DbContextFactory => dbContextFactory;

		/// <summary>
		/// Database table name for <typeparamref name="TEntity"/>, resolved from EF Core model metadata.
		/// </summary>
		/// <remarks>
		/// Cached at construction time to avoid repeating model metadata lookups on hot paths.
		/// </remarks>
		protected string TableName { get; }

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

		private const string StaleStateErrorCode = "STALE_STATE";
		private const string StaleStateDefaultMessage = "Write rejected due to an optimistic concurrency conflict.";
		private const string DuplicateReplayErrorCode = "DUPLICATE_REPLAY";
		private const string DuplicateReplayDefaultMessage = "Write rejected because the incoming Version equals the persisted Version (duplicate replay).";
		private const string MaxRetriesErrorCode = "MAX_RETRIES";
		private const string MaxRetriesMessage = "Maximum retry attempts exceeded";

		/// <summary>
		/// Outcome classification for exception handling in database operations.
		/// </summary>
		private enum ExceptionOutcome
		{
			/// <summary>A non-retryable stale state conflict (optimistic concurrency).</summary>
			StaleState,
			/// <summary>A duplicate replay exception (same version replay).</summary>
			DuplicateReplay,
			/// <summary>A transient failure that can be retried.</summary>
			Transient,
			/// <summary>A non-retryable terminal failure.</summary>
			Terminal
		}

		/// <summary>
		/// Classifies an exception to determine the appropriate handling strategy.
		/// </summary>
		private static ExceptionOutcome ClassifyException(Exception ex, string? sqlState)
		{
			if (ex is DbUpdateConcurrencyException) return ExceptionOutcome.StaleState;
			if (ex is StaleStateException) return ExceptionOutcome.StaleState;
			if (ex is DuplicateReplayException) return ExceptionOutcome.DuplicateReplay;
			if (IsTransientDatabaseFailure(ex, sqlState)) return ExceptionOutcome.Transient;
			return ExceptionOutcome.Terminal;
		}

		/// <summary>
		/// Creates a <see cref="DatabaseResult"/> failure based on exception classification.
		/// </summary>
		private static DatabaseResult CreateExceptionResult(Exception ex, ExceptionOutcome outcome)
		{
			switch (outcome)
			{
				case ExceptionOutcome.StaleState:
					var staleMessage = ex is StaleStateException staleEx ? staleEx.Message : StaleStateDefaultMessage;
					return DatabaseResult.Failure(StaleStateErrorCode, staleMessage, isTransient: false);
				case ExceptionOutcome.DuplicateReplay:
					var dupMessage = string.IsNullOrWhiteSpace(ex.Message) ? DuplicateReplayDefaultMessage : ex.Message;
					return DatabaseResult.Failure(DuplicateReplayErrorCode, dupMessage, isTransient: false);
				default:
					var sqlState = TryGetPostgresSqlState(ex);
					var (code, message, isTransient) = MapFinalException(ex, sqlState);
					return DatabaseResult.Failure(code, message, isTransient);
			}
		}

		/// <summary>
		/// Creates a <see cref="DatabaseResult{TResult}"/> failure based on exception classification.
		/// </summary>
		private static DatabaseResult<TResult> CreateExceptionResult<TResult>(Exception ex, ExceptionOutcome outcome)
		{
			switch (outcome)
			{
				case ExceptionOutcome.StaleState:
					var staleMessage = ex is StaleStateException staleEx ? staleEx.Message : StaleStateDefaultMessage;
					return DatabaseResult<TResult>.Failure(StaleStateErrorCode, staleMessage, isTransient: false);
				case ExceptionOutcome.DuplicateReplay:
					var dupMessage = string.IsNullOrWhiteSpace(ex.Message) ? DuplicateReplayDefaultMessage : ex.Message;
					return DatabaseResult<TResult>.Failure(DuplicateReplayErrorCode, dupMessage, isTransient: false);
				default:
					var sqlState = TryGetPostgresSqlState(ex);
					var (code, message, isTransient) = MapFinalException(ex, sqlState);
					return DatabaseResult<TResult>.Failure(code, message, isTransient);
			}
		}

		/// <summary>
		/// Performs rollback on savepoint or transaction as appropriate.
		/// </summary>
		private static async Task RollbackSavepointOrTransactionAsync(
			SavepointScope savepoint,
			IDbContextTransaction? transaction,
			bool ownsTransaction,
			CancellationToken cancellationToken)
		{
			if (savepoint.HasSavepoint)
			{
				try { await savepoint.RollbackAsync(cancellationToken).ConfigureAwait(false); } catch { /* Ignore rollback failures */ }
			}
			else if (ownsTransaction && transaction != null)
			{
				try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { /* Ignore rollback failures */ }
			}
		}

		/// <summary>
		/// Handles an exception in the retry loop, returning a result if the exception is terminal.
		/// </summary>
		/// <param name="ex">The exception that occurred.</param>
		/// <param name="sqlState">The PostgreSQL SQLSTATE code if available.</param>
		/// <param name="attempt">Current retry attempt number.</param>
		/// <param name="shouldRetry">Output indicating whether the caller should retry.</param>
		/// <returns>A failure result if the exception is terminal; null if retry is possible.</returns>
		private DatabaseResult? HandleRetryableException(Exception ex, string? sqlState, int attempt, out bool shouldRetry)
		{
			var outcome = ClassifyException(ex, sqlState);
			shouldRetry = false;

			if (outcome == ExceptionOutcome.Transient && attempt < MaxRetries)
			{
				shouldRetry = true;
				return null;
			}

			return CreateExceptionResult(ex, outcome);
		}

		/// <summary>
		/// Handles an exception in the retry loop, returning a result if the exception is terminal.
		/// </summary>
		private DatabaseResult<TResult>? HandleRetryableException<TResult>(Exception ex, string? sqlState, int attempt, out bool shouldRetry)
		{
			var outcome = ClassifyException(ex, sqlState);
			shouldRetry = false;

			if (outcome == ExceptionOutcome.Transient && attempt < MaxRetries)
			{
				shouldRetry = true;
				return null;
			}

			return CreateExceptionResult<TResult>(ex, outcome);
		}

		private static TimeSpan GetRetryDelay(int attempt)
		{
			if (attempt <= 0)
			{
				attempt = 1;
			}

			// Linear backoff with small jitter to reduce thundering herd retries.
			// Random.Shared is not available on netstandard2.1; use a thread-safe RNG API.
			var jitterMs = RandomNumberGenerator.GetInt32(0, 10);
			return TimeSpan.FromMilliseconds((20 * attempt) + jitterMs);
		}

		/// <summary>
		/// Initializes the service and caches the table name for <typeparamref name="TEntity"/>.
		/// </summary>
		/// <param name="contextFactory">Factory used to create new EF Core contexts.</param>
		/// <exception cref="ArgumentNullException"><paramref name="contextFactory"/> is null.</exception>
		protected BaseService(INpgsqlDbContextFactory contextFactory)
		{
			dbContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));

			try
			{
				// Cache table name once at construction - dispose context immediately
				using var dbContext = DbContextFactory.CreateDbContext();
				TableName = dbContext.GetTableName<TEntity>();
			}
			catch (DatabaseException)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new DatabaseException(
					"Failed to initialize database service metadata.",
					ex,
					"INVALID_CONFIGURATION",
					isTransient: false);
			}
		}

		/// <summary>
		/// Executes a database operation with a fresh context and transaction.
		/// Retries transient database failures.
		/// Returns a <see cref="DatabaseResult"/> with success or failure information.
		/// </summary>
		/// <param name="action">The database operation to execute within the transaction.</param>
		/// <param name="saveChanges">
		/// When true (default), calls <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/> before committing.
		/// Set to false when the operation only uses raw SQL (e.g., <c>ExecuteSqlRawAsync</c>) or when the delegate performs its own SaveChanges.
		/// </param>
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
			bool saveChanges = true,
			[CallerMemberName] string? operationName = null,
			CancellationToken cancellationToken = default)
		{
			var resolvedOperationName = ResolveOperationName(operationName);

			if (DatabaseExecutionScope.TryGetCurrentDbContext(out var ambientDbContext))
			{
				try
				{
					using var scope = DatabaseExecutionScope.Enter(dbContext: null, isTransactionScope: true);
					var existingTransaction = ambientDbContext.Database.CurrentTransaction;
					var ownsTransaction = existingTransaction == null;

					await using var transaction = ownsTransaction
						? await ambientDbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
						: null;

					var savepoint = !ownsTransaction && existingTransaction != null
						? await SavepointScope.CreateAsync(existingTransaction, cancellationToken).ConfigureAwait(false)
						: default;

					try
					{
						await action(ambientDbContext).ConfigureAwait(false);
						if (saveChanges)
						{
							await ambientDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
						}

						if (ownsTransaction && transaction != null)
						{
							await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
						}
						else if (savepoint.HasSavepoint)
						{
							await savepoint.ReleaseAsync(cancellationToken).ConfigureAwait(false);
						}

						return DatabaseResult.Success();
					}
					catch (Exception ex)
					{
						await RollbackSavepointOrTransactionAsync(savepoint, transaction, ownsTransaction, cancellationToken).ConfigureAwait(false);
						var sqlState = TryGetPostgresSqlState(ex);
						var outcome = ClassifyException(ex, sqlState);
						return CreateExceptionResult(ex, outcome);
					}
				}
				catch (Exception ex)
				{
					var sqlState = TryGetPostgresSqlState(ex);
					return HandleFinalException(ex, sqlState);
				}
			}

			for (int attempt = 1; attempt <= MaxRetries; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var stopwatch = PerformanceTracker?.StartTracking();
				var attemptStartUtc = stopwatch == null ? DateTime.UtcNow : default;

				// New context clears the cache and ensures we get fresh data on retry
				NpgsqlDbContext? context = null;
				DatabaseExecutionScope.ScopeToken scope = default;
				var scopeEntered = false;
				IDbContextTransaction? transaction = null;

				try
				{
					context = DbContextFactory.CreateDbContext();
					scope = DatabaseExecutionScope.Enter(context, isTransactionScope: true);
					scopeEntered = true;

					// New transaction required because Postgres kills the transaction on failure
					transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

					await action(context).ConfigureAwait(false);
					if (saveChanges)
					{
						await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
					}
					await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
					RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: true);
					return DatabaseResult.Success(); // Success!
				}
				catch (Exception ex)
				{
					if (transaction != null)
					{
						try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { /* Ignore socket errors */ }
					}

					var sqlState = RecordFailureAndGetSqlState(ex, resolvedOperationName, stopwatch, attemptStartUtc);
					var result = HandleRetryableException(ex, sqlState, attempt, out var shouldRetry);
					if (shouldRetry)
					{
						await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
						continue;
					}
					return result ?? DatabaseResult.Failure(MaxRetriesErrorCode, MaxRetriesMessage, isTransient: true);
				}
				finally
				{
					transaction?.Dispose();
					if (scopeEntered)
					{
						scope.Dispose();
					}
					context?.Dispose();
				}
			}

			return DatabaseResult.Failure("MAX_RETRIES", "Maximum retry attempts exceeded", isTransient: true);
		}

		/// <summary>
		/// Executes a database operation with a fresh context and transaction, returning a result.
		/// Retries transient database failures.
		/// Returns a <see cref="DatabaseResult{TResult}"/> with success or failure information.
		/// </summary>
		/// <typeparam name="TResult">The result type returned by the operation.</typeparam>
		/// <param name="action">The database operation to execute within the transaction.</param>
		/// <param name="saveChanges">
		/// When true (default), calls <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/> before committing.
		/// Set to false when the operation only uses raw SQL (e.g., <c>ExecuteSqlRawAsync</c>) or when the delegate performs its own SaveChanges.
		/// </param>
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
			bool saveChanges = true,
			[CallerMemberName] string? operationName = null,
			CancellationToken cancellationToken = default)
		{
			TResult result = default!;
			var resolvedOperationName = ResolveOperationName(operationName);

			if (DatabaseExecutionScope.TryGetCurrentDbContext(out var ambientDbContext))
			{
				try
				{
					using var scope = DatabaseExecutionScope.Enter(dbContext: null, isTransactionScope: true);
					var existingTransaction = ambientDbContext.Database.CurrentTransaction;
					var ownsTransaction = existingTransaction == null;

					await using var transaction = ownsTransaction
						? await ambientDbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
						: null;

					var savepoint = !ownsTransaction && existingTransaction != null
						? await SavepointScope.CreateAsync(existingTransaction, cancellationToken).ConfigureAwait(false)
						: default;

					try
					{
						result = await action(ambientDbContext).ConfigureAwait(false);
						if (saveChanges)
						{
							await ambientDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
						}

						if (ownsTransaction && transaction != null)
						{
							await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
						}
						else if (savepoint.HasSavepoint)
						{
							await savepoint.ReleaseAsync(cancellationToken).ConfigureAwait(false);
						}

						return DatabaseResult<TResult>.Success(result);
					}
					catch (Exception ex)
					{
						await RollbackSavepointOrTransactionAsync(savepoint, transaction, ownsTransaction, cancellationToken).ConfigureAwait(false);
						var sqlState = TryGetPostgresSqlState(ex);
						var outcome = ClassifyException(ex, sqlState);
						return CreateExceptionResult<TResult>(ex, outcome);
					}
				}
				catch (Exception ex)
				{
					var sqlState = TryGetPostgresSqlState(ex);
					return HandleFinalException<TResult>(ex, sqlState);
				}
			}

			for (int attempt = 1; attempt <= MaxRetries; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var stopwatch = PerformanceTracker?.StartTracking();
				var attemptStartUtc = stopwatch == null ? DateTime.UtcNow : default;

				NpgsqlDbContext? context = null;
				DatabaseExecutionScope.ScopeToken scope = default;
				var scopeEntered = false;
				IDbContextTransaction? transaction = null;

				try
				{
					context = DbContextFactory.CreateDbContext();
					scope = DatabaseExecutionScope.Enter(context, isTransactionScope: true);
					scopeEntered = true;
					transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

					result = await action(context).ConfigureAwait(false);
					if (saveChanges)
					{
						await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
					}
					await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
					RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: true);
					return DatabaseResult<TResult>.Success(result);
				}
				catch (Exception ex)
				{
					if (transaction != null)
					{
						try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
					}

					var sqlState = RecordFailureAndGetSqlState(ex, resolvedOperationName, stopwatch, attemptStartUtc);
					var exResult = HandleRetryableException<TResult>(ex, sqlState, attempt, out var shouldRetry);
					if (shouldRetry)
					{
						await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
						continue;
					}
					return exResult ?? DatabaseResult<TResult>.Failure(MaxRetriesErrorCode, MaxRetriesMessage, isTransient: true);
				}
				finally
				{
					transaction?.Dispose();
					if (scopeEntered)
					{
						scope.Dispose();
					}
					context?.Dispose();
				}
			}

			return DatabaseResult<TResult>.Failure("MAX_RETRIES", "Maximum retry attempts exceeded", isTransient: true);
		}

		/// <summary>
		/// Executes a write database operation with a fresh context.
		/// Does not create an explicit transaction, and calls SaveChanges by default.
		/// Retries transient database failures.
		/// Returns a <see cref="DatabaseResult"/> with success or failure information.
		/// </summary>
		/// <param name="action">The write operation to execute.</param>
		/// <param name="saveChanges">
		/// When true (default), calls <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/> after <paramref name="action"/> completes.
		/// Set to false when the operation does not use EF change tracking (e.g., only <c>ExecuteSqlRawAsync</c>) or when the delegate performs its own SaveChanges.
		/// </param>
		/// <param name="operationName">Operation name for metrics; defaults to caller member name.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult"/> describing the outcome.</returns>
		/// <remarks>
		/// <para>
		/// This wrapper is intended for small, single-statement writes where an explicit transaction is not required
		/// and where the operation is safe to retry on transient failures.
		/// </para>
		/// <para>
		/// A new context is created per attempt to avoid EF change-tracker state leaking across retries.
		/// <see cref="StaleStateException"/> is treated as a logical conflict and is not retried.
		/// </para>
		/// </remarks>
		protected async Task<DatabaseResult> ExecuteWriteAsync(
			Func<NpgsqlDbContext, Task> action,
			bool saveChanges = true,
			[CallerMemberName] string? operationName = null,
			CancellationToken cancellationToken = default)
		{
			var resolvedOperationName = ResolveOperationName(operationName);

			if (DatabaseExecutionScope.TryGetCurrentDbContext(out var ambientDbContext))
			{
				try
				{
					using var scope = DatabaseExecutionScope.Enter(dbContext: null, isTransactionScope: false);
					var existingTransaction = ambientDbContext.Database.CurrentTransaction;

					var savepoint = existingTransaction != null
						? await SavepointScope.CreateAsync(existingTransaction, cancellationToken).ConfigureAwait(false)
						: default;

					try
					{
						await action(ambientDbContext).ConfigureAwait(false);
						if (saveChanges)
						{
							await ambientDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
						}
						if (savepoint.HasSavepoint)
						{
							await savepoint.ReleaseAsync(cancellationToken).ConfigureAwait(false);
						}

						return DatabaseResult.Success();
					}
					catch (Exception ex)
					{
						if (savepoint.HasSavepoint)
						{
							try { await savepoint.RollbackAsync(cancellationToken).ConfigureAwait(false); } catch { }
						}
						var sqlState = TryGetPostgresSqlState(ex);
						var outcome = ClassifyException(ex, sqlState);
						return CreateExceptionResult(ex, outcome);
					}
				}
				catch (Exception ex)
				{
					var sqlState = TryGetPostgresSqlState(ex);
					return HandleFinalException(ex, sqlState);
				}
			}

			for (int attempt = 1; attempt <= MaxRetries; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var stopwatch = PerformanceTracker?.StartTracking();
				var attemptStartUtc = stopwatch == null ? DateTime.UtcNow : default;

				NpgsqlDbContext? context = null;
				DatabaseExecutionScope.ScopeToken scope = default;
				var scopeEntered = false;

				try
				{
					context = DbContextFactory.CreateDbContext();
					scope = DatabaseExecutionScope.Enter(context, isTransactionScope: false);
					scopeEntered = true;

					await action(context).ConfigureAwait(false);
					if (saveChanges)
					{
						await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
					}
					RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: true);
					return DatabaseResult.Success();
				}
				catch (Exception ex)
				{
					var sqlState = RecordFailureAndGetSqlState(ex, resolvedOperationName, stopwatch, attemptStartUtc);
					var result = HandleRetryableException(ex, sqlState, attempt, out var shouldRetry);
					if (shouldRetry)
					{
						await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
						continue;
					}
					return result ?? DatabaseResult.Failure(MaxRetriesErrorCode, MaxRetriesMessage, isTransient: true);
				}
				finally
				{
					if (scopeEntered)
					{
						scope.Dispose();
					}
					context?.Dispose();
				}
			}

			return DatabaseResult.Failure(MaxRetriesErrorCode, MaxRetriesMessage, isTransient: true);
		}

		/// <summary>
		/// Executes a write database operation with a fresh context and returns a value.
		/// Does not create an explicit transaction, and calls SaveChanges by default.
		/// Retries transient database failures.
		/// Returns a <see cref="DatabaseResult{TResult}"/> with success or failure information.
		/// </summary>
		/// <typeparam name="TResult">The result type returned by the operation.</typeparam>
		/// <param name="action">The write operation to execute.</param>
		/// <param name="saveChanges">
		/// When true (default), calls <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/> after <paramref name="action"/> completes.
		/// Set to false when the operation does not use EF change tracking (e.g., only <c>ExecuteSqlRawAsync</c>) or when the delegate performs its own SaveChanges.
		/// </param>
		/// <param name="operationName">Operation name for metrics; defaults to caller member name.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult{TResult}"/> describing the outcome.</returns>
		/// <remarks>
		/// A new context is created per attempt to avoid EF change-tracker state leaking across retries.
		/// <see cref="StaleStateException"/> is treated as a logical conflict and is not retried.
		/// </remarks>
		protected async Task<DatabaseResult<TResult>> ExecuteWriteAsync<TResult>(
			Func<NpgsqlDbContext, Task<TResult>> action,
			bool saveChanges = true,
			[CallerMemberName] string? operationName = null,
			CancellationToken cancellationToken = default)
		{
			var resolvedOperationName = ResolveOperationName(operationName);

			if (DatabaseExecutionScope.TryGetCurrentDbContext(out var ambientDbContext))
			{
				try
				{
					using var scope = DatabaseExecutionScope.Enter(dbContext: null, isTransactionScope: false);
					var existingTransaction = ambientDbContext.Database.CurrentTransaction;

					var savepoint = existingTransaction != null
						? await SavepointScope.CreateAsync(existingTransaction, cancellationToken).ConfigureAwait(false)
						: default;

					try
					{
						var nestedResult = await action(ambientDbContext).ConfigureAwait(false);
						if (saveChanges)
						{
							await ambientDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
						}
						if (savepoint.HasSavepoint)
						{
							await savepoint.ReleaseAsync(cancellationToken).ConfigureAwait(false);
						}

						return DatabaseResult<TResult>.Success(nestedResult);
					}
					catch (Exception ex)
					{
						if (savepoint.HasSavepoint)
						{
							try { await savepoint.RollbackAsync(cancellationToken).ConfigureAwait(false); } catch { }
						}
						var sqlState = TryGetPostgresSqlState(ex);
						var outcome = ClassifyException(ex, sqlState);
						return CreateExceptionResult<TResult>(ex, outcome);
					}
				}
				catch (Exception ex)
				{
					var sqlState = TryGetPostgresSqlState(ex);
					return HandleFinalException<TResult>(ex, sqlState);
				}
			}

			for (int attempt = 1; attempt <= MaxRetries; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var stopwatch = PerformanceTracker?.StartTracking();
				var attemptStartUtc = stopwatch == null ? DateTime.UtcNow : default;

				NpgsqlDbContext? context = null;
				DatabaseExecutionScope.ScopeToken scope = default;
				var scopeEntered = false;

				try
				{
					context = DbContextFactory.CreateDbContext();
					scope = DatabaseExecutionScope.Enter(context, isTransactionScope: false);
					scopeEntered = true;

					var result = await action(context).ConfigureAwait(false);
					if (saveChanges)
					{
						await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
					}
					RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: true);
					return DatabaseResult<TResult>.Success(result);
				}
				catch (Exception ex)
				{
					var sqlState = RecordFailureAndGetSqlState(ex, resolvedOperationName, stopwatch, attemptStartUtc);
					var exResult = HandleRetryableException<TResult>(ex, sqlState, attempt, out var shouldRetry);
					if (shouldRetry)
					{
						await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
						continue;
					}
					return exResult ?? DatabaseResult<TResult>.Failure(MaxRetriesErrorCode, MaxRetriesMessage, isTransient: true);
				}
				finally
				{
					if (scopeEntered)
					{
						scope.Dispose();
					}
					context?.Dispose();
				}
			}

			return DatabaseResult<TResult>.Failure(MaxRetriesErrorCode, MaxRetriesMessage, isTransient: true);
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
			[CallerMemberName] string? operationName = null,
			CancellationToken cancellationToken = default)
		{
			var resolvedOperationName = ResolveOperationName(operationName);

			if (DatabaseExecutionScope.TryGetCurrentDbContext(out var ambientDbContext))
			{
				using var scope = DatabaseExecutionScope.EnterReadOnly(dbContext: null);
				try
				{
					await action(ambientDbContext).ConfigureAwait(false);
					return DatabaseResult.Success();
				}
				catch (Exception ex)
				{
					var sqlState = TryGetPostgresSqlState(ex);
					return HandleFinalException(ex, sqlState);
				}
			}

			for (int attempt = 1; attempt <= MaxRetries; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var stopwatch = PerformanceTracker?.StartTracking();
				var attemptStartUtc = stopwatch == null ? DateTime.UtcNow : default;

				try
				{
					using var context = DbContextFactory.CreateDbContext();
					using var scope = DatabaseExecutionScope.EnterReadOnly(context);

					await action(context).ConfigureAwait(false);
					RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: true);
					return DatabaseResult.Success();
				}
				catch (Exception ex)
				{
					var sqlState = RecordFailureAndGetSqlState(ex, resolvedOperationName, stopwatch, attemptStartUtc);
					var result = HandleRetryableException(ex, sqlState, attempt, out var shouldRetry);
					if (shouldRetry)
					{
						await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
						continue;
					}
					return result ?? DatabaseResult.Failure(MaxRetriesErrorCode, MaxRetriesMessage, isTransient: true);
				}
			}

			return DatabaseResult.Failure(MaxRetriesErrorCode, MaxRetriesMessage, isTransient: true);
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
			[CallerMemberName] string? operationName = null,
			CancellationToken cancellationToken = default)
		{
			var resolvedOperationName = ResolveOperationName(operationName);

			if (DatabaseExecutionScope.TryGetCurrentDbContext(out var ambientDbContext))
			{
				using var scope = DatabaseExecutionScope.EnterReadOnly(dbContext: null);
				try
				{
					var nestedResult = await action(ambientDbContext).ConfigureAwait(false);
					return DatabaseResult<TResult>.Success(nestedResult);
				}
				catch (Exception ex)
				{
					var sqlState = TryGetPostgresSqlState(ex);
					return HandleFinalException<TResult>(ex, sqlState);
				}
			}

			for (int attempt = 1; attempt <= MaxRetries; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var stopwatch = PerformanceTracker?.StartTracking();
				var attemptStartUtc = stopwatch == null ? DateTime.UtcNow : default;

				try
				{
					using var context = DbContextFactory.CreateDbContext();
					using var scope = DatabaseExecutionScope.EnterReadOnly(context);

					var result = await action(context).ConfigureAwait(false);
					RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: true);
					return DatabaseResult<TResult>.Success(result);
				}
				catch (Exception ex)
				{
					var sqlState = RecordFailureAndGetSqlState(ex, resolvedOperationName, stopwatch, attemptStartUtc);
					var exResult = HandleRetryableException<TResult>(ex, sqlState, attempt, out var shouldRetry);
					if (shouldRetry)
					{
						await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
						continue;
					}
					return exResult ?? DatabaseResult<TResult>.Failure(MaxRetriesErrorCode, MaxRetriesMessage, isTransient: true);
				}
			}

			return DatabaseResult<TResult>.Failure(MaxRetriesErrorCode, MaxRetriesMessage, isTransient: true);
		}

		/// <summary>
		/// Resolves a stable operation name for metrics and diagnostics.
		/// </summary>
		/// <param name="operationName">The caller-provided operation name.</param>
		/// <returns>A name of the form "{ServiceType}.{MemberName}".</returns>
		private string ResolveOperationName(string? operationName)
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

		private string? RecordFailureAndGetSqlState(Exception ex, string resolvedOperationName, Stopwatch? stopwatch, DateTime attemptStartUtc)
		{
			var sqlState = TryGetPostgresSqlState(ex);
			RecordOperationAttempt(resolvedOperationName, stopwatch, attemptStartUtc, success: false);
			RecordPoolMetricsForFailure(sqlState);
			return sqlState;
		}

		private void RecordPoolMetricsForFailure(string? sqlState)
		{
			// If the failure indicates "too many connections", count it as pool exhaustion.
			if (sqlState == "53300")
			{
				PoolMetrics?.RecordPoolExhaustion();
			}
			else if (IsConnectionSqlState(sqlState))
			{
				PoolMetrics?.RecordConnectionError();
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
			if (ex is DuplicateReplayException duplicateEx)
			{
				var message = string.IsNullOrWhiteSpace(duplicateEx.Message) ? DuplicateReplayDefaultMessage : duplicateEx.Message;
				return (DuplicateReplayErrorCode, message, false);
			}

			if (ex is OperationCanceledException)
			{
				return ("DB_CANCELED", "The database operation was canceled.", false);
			}

			// Prefer explicit, safe database-layer exceptions.
			// These are designed to avoid leaking SQL/connection details to callers.
			if (ex is DatabaseException dbEx)
			{
				return (
					string.IsNullOrWhiteSpace(dbEx.ErrorCode) ? "DATABASE_ERROR" : dbEx.ErrorCode,
					string.IsNullOrWhiteSpace(dbEx.SafeMessage) ? "A database error occurred." : dbEx.SafeMessage,
					dbEx.IsTransient);
			}

			// 23505 = unique_violation
			if (sqlState == "23505")
			{
				return ("UNIQUE_VIOLATION", "The record already exists.", false);
			}

			// 23503 = foreign_key_violation
			if (sqlState == "23503")
			{
				return ("FOREIGN_KEY_VIOLATION", "A referenced record was not found.", false);
			}

			// 23502 = not_null_violation
			if (sqlState == "23502")
			{
				return ("NOT_NULL_VIOLATION", "A required field was missing.", false);
			}

			// 23514 = check_violation
			if (sqlState == "23514")
			{
				return ("CHECK_VIOLATION", "One or more values were invalid.", false);
			}

			if (ex is ArgumentException argEx)
			{
				return ("INVALID_ARGUMENT", argEx.Message, false);
			}

			if (ex is InvalidOperationException invEx)
			{
				return ("INVALID_OPERATION", invEx.Message, false);
			}

			// Avoid leaking internal DB details (SQL text, schema names, constraint names, etc.).
			// If the failure is likely transient, callers may choose to retry.
			var isTransient = IsTransientDatabaseFailure(ex, sqlState);
			return ("DATABASE_ERROR", "A database error occurred.", isTransient);
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

			var upsertStatement = sql.Trim();
			upsertStatement = upsertStatement.TrimEnd(';');

			var countSql = $@"WITH upserted AS (
				{upsertStatement}
				RETURNING 1
			)
			SELECT COUNT(*)::integer AS value FROM upserted";

			var countRow = await dbContext.Set<SqlIntValue>()
				.FromSqlRaw(countSql, parameters)
				.AsNoTracking()
				.SingleAsync(cancellationToken)
				.ConfigureAwait(false);

			if (countRow.Value != expectedRowsAffected)
			{
				throw new StaleStateException(staleStateMessage);
			}
		}
	}
}