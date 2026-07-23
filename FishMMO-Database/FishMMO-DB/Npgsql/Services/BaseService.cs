using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Monitoring.Diagnostics;
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
	/// This base type provides three primary execution paths:
	/// </para>
	/// <list type="bullet">
	/// <item>
	/// <description>
	/// <see cref="ExecuteTransactionAsync(Func{Task}, CancellationToken)"/> and
	/// <see cref="ExecuteTransactionAsync{TResult}(Func{Task{TResult}},string,CancellationToken)"/>
	/// create a fresh <see cref="NpgsqlDbContext"/>, begin an explicit transaction, execute the delegate,
	/// then call <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> and commit.
	/// </description>
	/// </item>
	/// <item>
	/// <description>
	/// <see cref="ExecuteWriteAsync(Func{Task},string,CancellationToken)"/> and
	/// <see cref="ExecuteWriteAsync{TResult}(Func{Task{TResult}},string,CancellationToken)"/>
	/// create a fresh <see cref="NpgsqlDbContext"/>, execute the delegate, then call <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>
	/// without starting an explicit transaction.
	/// </description>
	/// </item>
	/// <item>
	/// <description>
	/// <see cref="ExecuteReadAsync(Func{Task},string,CancellationToken)"/> and
	/// <see cref="ExecuteReadAsync{TResult}(Func{Task{TResult}},string,CancellationToken)"/>
	/// create a fresh <see cref="NpgsqlDbContext"/> but do not start an explicit transaction and do not call SaveChanges.
	/// </description>
	/// </item>
	/// </list>
	/// <para>
	/// <b>Standalone Execution (no ambient scope):</b> A new context is created per attempt to avoid EF change-tracker
	/// state leaking across retries. Transient database failures are retried with exponential backoff.
	/// Optimistic concurrency conflicts (Version-based authority) and <see cref="StaleStateException"/> are never retried;
	/// they are returned as non-transient failures so the caller can re-read and decide how to proceed.
	/// </para>
	/// <para>
	/// <b>Ambient Scope Execution (inside an existing Unit of Work):</b> When an operation detects an active
	/// <see cref="DatabaseExecutionScope"/>, it reuses the ambient <see cref="NpgsqlDbContext"/> and does NOT retry
	/// on transient failures. This is by design for the following reasons:
	/// </para>
	/// <list type="number">
	/// <item><description>
	/// <b>Transaction State Corruption:</b> PostgreSQL aborts the entire transaction on most transient failures.
	/// The connection and transaction become unusable, making retry with the same context impossible.
	/// </description></item>
	/// <item><description>
	/// <b>Context State Pollution:</b> The DbContext's change tracker accumulates state. Retrying with a polluted
	/// change tracker can cause duplicate key violations or incorrect updates.
	/// </description></item>
	/// <item><description>
	/// <b>Semantic Correctness:</b> If operation A succeeded and operation B failed transiently, retrying B alone
	/// without re-evaluating A's preconditions could violate business invariants.
	/// </description></item>
	/// </list>
	/// <para>
	/// When a transient failure occurs inside an ambient scope, the failure is returned immediately so the caller
	/// can restart the entire unit of work with fresh state. Savepoints are used for nested atomicity but cannot
	/// recover from connection-level failures.
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
		/// Gets the retry policy configuration for transient failure handling.
		/// </summary>
		protected RetryPolicyConfiguration RetryPolicy => DbContextFactory.RetryPolicy;

		private const string StaleStateDefaultMessage = "Write rejected due to an optimistic concurrency conflict.";
		private const string DuplicateReplayDefaultMessage = "Write rejected because the incoming Version equals the persisted Version (duplicate replay).";
		private const string MaxRetriesMessage = "Maximum retry attempts exceeded.";
		private const string RollbackFailedMessage = "Transaction rollback failed after an operation error.";

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
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static ExceptionOutcome ClassifyException(Exception ex, string? sqlState)
		{
			if (ex is DbUpdateConcurrencyException) return ExceptionOutcome.StaleState;
			if (ex is StaleStateException) return ExceptionOutcome.StaleState;
			if (ex is DuplicateReplayException) return ExceptionOutcome.DuplicateReplay;
			if (IsTransientDatabaseFailure(ex, sqlState)) return ExceptionOutcome.Transient;
			return ExceptionOutcome.Terminal;
		}

		/// <summary>
		/// Creates a <see cref="DatabaseResult{TResult}"/> failure based on exception classification.
		/// </summary>
		private static DatabaseResult<TResult> CreateExceptionResult<TResult>(Exception ex, ExceptionOutcome outcome)
		{
			var (code, message, isTransient) = MapExceptionOutcome(ex, outcome);
			return DatabaseResult<TResult>.Failure(code, message, isTransient);
		}

		/// <summary>
		/// Maps an exception outcome to error code, message, and transience.
		/// </summary>
		private static (string Code, string Message, bool IsTransient) MapExceptionOutcome(Exception ex, ExceptionOutcome outcome)
		{
			switch (outcome)
			{
				case ExceptionOutcome.StaleState:
					var staleMessage = ex is StaleStateException staleEx ? staleEx.Message : StaleStateDefaultMessage;
					return (DatabaseErrorCodes.StaleState, staleMessage, false);
				case ExceptionOutcome.DuplicateReplay:
					var dupMessage = string.IsNullOrWhiteSpace(ex.Message) ? DuplicateReplayDefaultMessage : ex.Message;
					return (DatabaseErrorCodes.DuplicateReplay, dupMessage, false);
				default:
					var sqlState = TryGetPostgresSqlState(ex);
					return MapFinalException(ex, sqlState);
			}
		}

		/// <summary>
		/// Performs rollback on savepoint or transaction as appropriate.
		/// Returns error details if rollback itself fails.
		/// </summary>
		/// <param name="savepoint">The savepoint scope, if any.</param>
		/// <param name="transaction">The transaction, if owned.</param>
		/// <param name="ownsTransaction">Whether this scope owns the transaction.</param>
		/// <param name="originalException">The original exception that triggered the rollback.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A tuple containing (ErrorCode, ErrorMessage) if rollback failed; null if rollback succeeded or was not needed.
		/// </returns>
		private static async Task<(string ErrorCode, string ErrorMessage)?> TryRollbackAsync(
			SavepointScope savepoint,
			IDbContextTransaction? transaction,
			bool ownsTransaction,
			Exception originalException,
			CancellationToken cancellationToken)
		{
			Exception? rollbackException = null;

			if (savepoint.HasSavepoint)
			{
				try
				{
					await savepoint.RollbackAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (Exception rbEx)
				{
					rollbackException = rbEx;
				}
			}
			else if (ownsTransaction && transaction != null)
			{
				try
				{
					await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
				}
				catch (Exception rbEx)
				{
					rollbackException = rbEx;
				}
			}

			if (rollbackException == null)
			{
				return null;
			}

			var originalSqlState = TryGetPostgresSqlState(originalException);
			var originalOutcome = ClassifyException(originalException, originalSqlState);
			var (originalCode, originalMessage, _) = MapExceptionOutcome(originalException, originalOutcome);

			return (
				DatabaseErrorCodes.RollbackFailed,
				$"{RollbackFailedMessage} Original error: {originalCode} - {originalMessage}. Rollback error: {rollbackException.Message}"
			);
		}

		/// <summary>
		/// Checks if an exception should trigger a retry attempt.
		/// </summary>
		/// <param name="ex">The exception that occurred.</param>
		/// <param name="sqlState">The PostgreSQL SQLSTATE code if available.</param>
		/// <param name="attempt">Current retry attempt number.</param>
		/// <returns>True if the exception is transient and retry attempts remain; otherwise false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool ShouldRetry(Exception ex, string? sqlState, int attempt)
		{
			var outcome = ClassifyException(ex, sqlState);
			return outcome == ExceptionOutcome.Transient && attempt < RetryPolicy.MaxRetries;
		}

		// Thread-local Random instance avoids lock contention while maintaining thread safety.
		// NOTE: Random.Shared (.NET 6+) is not available on netstandard2.1, so ThreadLocal<Random>
		// is the best alternative for non-cryptographic jitter.
		private static readonly ThreadLocal<Random> jitterRng = new ThreadLocal<Random>(() => new Random());

		private TimeSpan GetRetryDelay(int attempt)
		{
			if (attempt <= 0)
			{
				attempt = 1;
			}

			// Linear backoff (BaseDelay * attempt) with small jitter to reduce thundering herd retries.
			// For exponential backoff, use: BaseDelay * 2^(attempt-1).
			// RandomNumberGenerator.GetInt32 was introduced in .NET 6 and is not available on netstandard2.1.
			// Random.Shared is also .NET 6+; use ThreadLocal<Random> instead.
			int maxJitter = RetryPolicy.MaxJitterMs > 0 ? RetryPolicy.MaxJitterMs : 1;
			int jitterMs = jitterRng.Value!.Next(0, maxJitter);
			return TimeSpan.FromMilliseconds((RetryPolicy.BaseDelayMs * attempt) + jitterMs);
		}

		/// <summary>
		/// Initializes the service and caches the table name for <typeparamref name="TEntity"/>.
		/// </summary>
		/// <param name="contextFactory">Factory used to create new EF Core contexts.</param>
		/// <exception cref="ArgumentNullException"><paramref name="contextFactory"/> is null.</exception>
		protected BaseService(INpgsqlDbContextFactory contextFactory)
		{
			dbContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));

			// NOTE: Constructor creates (and disposes) a DbContext to resolve the table name. This couples
			// service initialization to database availability. The table name is cached for the service lifetime.
			try
			{
				// Cache table name once at construction - dispose context immediately
				using var dbContext = DbContextFactory.CreateDbContext();
				TableName = dbContext.GetTableName<TEntity>();
			}
			catch (Exception ex)
			{
				throw new DatabaseException(
					"Failed to initialize database service metadata.",
					ex,
					DatabaseErrorCodes.InvalidConfiguration);
			}
		}

		/// <summary>
		/// Executes a database operation with a fresh context and transaction.
		/// Retries transient database failures.
		/// Returns a <see cref="DatabaseResult"/> with success or failure information.
		/// </summary>
		/// <param name="action">The database operation to execute within the transaction.</param>
		/// <param name="saveChanges">
		/// When true (default), calls <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> before committing.
		/// Set to false when the operation only uses raw SQL (e.g., <c>ExecuteSqlRawAsync</c>) or when the delegate performs its own SaveChanges.
		/// </param>
		/// <param name="operationName">Operation name for metrics; defaults to caller member name.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult"/> describing the outcome.</returns>
		/// <remarks>
		/// Delegates to the generic overload with a unit return value to eliminate code duplication.
		/// A new context is created per attempt to avoid EF change-tracker state leaking across retries.
		/// <see cref="StaleStateException"/> is treated as a logical conflict and is not retried.
		/// </remarks>
		protected async Task<DatabaseResult> ExecuteTransactionAsync(
			Func<NpgsqlDbContext, Task> action,
			bool saveChanges = true,
			[CallerMemberName] string? operationName = null,
			CancellationToken cancellationToken = default)
		{
			var result = await ExecuteTransactionAsync(
				async ctx =>
				{
					await action(ctx).ConfigureAwait(false);
					return true;
				},
				saveChanges,
				operationName,
				cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <summary>
		/// Executes a database operation with a fresh context and transaction, returning a result.
		/// Retries transient database failures.
		/// Returns a <see cref="DatabaseResult{TResult}"/> with success or failure information.
		/// </summary>
		/// <typeparam name="TResult">The result type returned by the operation.</typeparam>
		/// <param name="action">The database operation to execute within the transaction.</param>
		/// <param name="saveChanges">
		/// When true (default), calls <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> before committing.
		/// Set to false when the operation only uses raw SQL (e.g., <c>ExecuteSqlRawAsync</c>) or when the delegate performs its own SaveChanges.
		/// </param>
		/// <param name="operationName">Operation name for metrics; defaults to caller member name.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult{TResult}"/> describing the outcome.</returns>
		/// <remarks>
		/// A new context is created per attempt to avoid EF change-tracker state leaking across retries.
		/// This wrapper begins an explicit transaction and always calls <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> on success.
		/// Prefer <see cref="ExecuteReadAsync{TResult}(Func{Task{TResult}},string,CancellationToken)"/>
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
						var rollbackError = await TryRollbackAsync(savepoint, transaction, ownsTransaction, ex, cancellationToken).ConfigureAwait(false);
						if (rollbackError != null)
						{
							return DatabaseResult<TResult>.Failure(rollbackError.Value.ErrorCode, rollbackError.Value.ErrorMessage);
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

			for (int attempt = 1; attempt <= RetryPolicy.MaxRetries; attempt++)
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
					Exception? rollbackException = null;
					if (transaction != null)
					{
						try
						{
							await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
						}
						catch (Exception rbEx)
						{
							rollbackException = rbEx;
						}
					}

					var sqlState = RecordFailureAndGetSqlState(ex, resolvedOperationName, stopwatch, attemptStartUtc);
					if (ShouldRetry(ex, sqlState, attempt))
					{
						await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
						continue;
					}

					var outcome = ClassifyException(ex, sqlState);
					var exResult = CreateExceptionResult<TResult>(ex, outcome);

					// If rollback failed and we're not retrying, include rollback failure details
					if (rollbackException != null)
					{
						return DatabaseResult<TResult>.Failure(
							DatabaseErrorCodes.RollbackFailed,
							$"{RollbackFailedMessage} Original error: {exResult.ErrorCode} - {exResult.ErrorMessage}. Rollback error: {rollbackException.Message}");
					}

					return exResult;
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

			return DatabaseResult<TResult>.Failure(DatabaseErrorCodes.MaxRetries, MaxRetriesMessage, isTransient: true);
		}

		/// <summary>
		/// Executes a write database operation with a fresh context.
		/// Does not create an explicit transaction, and calls SaveChanges by default.
		/// Retries transient database failures.
		/// Returns a <see cref="DatabaseResult"/> with success or failure information.
		/// </summary>
		/// <param name="action">The write operation to execute.</param>
		/// <param name="saveChanges">
		/// When true (default), calls <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> after <paramref name="action"/> completes.
		/// Set to false when the operation does not use EF change tracking (e.g., only <c>ExecuteSqlRawAsync</c>) or when the delegate performs its own SaveChanges.
		/// </param>
		/// <param name="operationName">Operation name for metrics; defaults to caller member name.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult"/> describing the outcome.</returns>
		/// <remarks>
		/// Delegates to the generic overload with a unit return value to eliminate code duplication.
		/// A new context is created per attempt to avoid EF change-tracker state leaking across retries.
		/// <see cref="StaleStateException"/> is treated as a logical conflict and is not retried.
		/// <para>
		/// IMPORTANT: This method does NOT create an explicit database transaction. Each raw SQL
		/// statement within the delegate commits independently. For multi-statement atomicity,
		/// use <see cref="ExecuteTransactionAsync(Func{NpgsqlDbContext, Task}, bool, string, CancellationToken)"/>
		/// or wrap calls in a <see cref="DatabaseExecutionScope"/>.
		/// </para>
		/// </remarks>
		protected async Task<DatabaseResult> ExecuteWriteAsync(
			Func<NpgsqlDbContext, Task> action,
			bool saveChanges = true,
			[CallerMemberName] string? operationName = null,
			CancellationToken cancellationToken = default)
		{
			var result = await ExecuteWriteAsync(
				async ctx =>
				{
					await action(ctx).ConfigureAwait(false);
					return true;
				},
				saveChanges,
				operationName,
				cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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
		/// When true (default), calls <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> after <paramref name="action"/> completes.
		/// Set to false when the operation does not use EF change tracking (e.g., only <c>ExecuteSqlRawAsync</c>) or when the delegate performs its own SaveChanges.
		/// </param>
		/// <param name="operationName">Operation name for metrics; defaults to caller member name.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult{TResult}"/> describing the outcome.</returns>
		/// <remarks>
		/// A new context is created per attempt to avoid EF change-tracker state leaking across retries.
		/// <see cref="StaleStateException"/> is treated as a logical conflict and is not retried.
		/// <para>
		/// IMPORTANT: This method does NOT create an explicit database transaction. Each raw SQL
		/// statement within the delegate commits independently. For multi-statement atomicity,
		/// use <see cref="ExecuteTransactionAsync{TResult}(Func{NpgsqlDbContext, Task{TResult}}, bool, string, CancellationToken)"/>
		/// or wrap calls in a <see cref="DatabaseExecutionScope"/>.
		/// </para>
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
							try
							{
								// Use CancellationToken.None to ensure rollback completes even if cancelled
								await savepoint.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
							}
							catch (Exception rbEx)
							{
								// Rollback failed - include in error result
								var sqlStateRb = TryGetPostgresSqlState(ex);
								var (originalCode, originalMessage, _) = MapExceptionOutcome(ex, ClassifyException(ex, sqlStateRb));
								return DatabaseResult<TResult>.Failure(
									DatabaseErrorCodes.RollbackFailed,
									$"{RollbackFailedMessage} Original error: {originalCode} - {originalMessage}. Rollback error: {rbEx.Message}");
							}
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

			for (int attempt = 1; attempt <= RetryPolicy.MaxRetries; attempt++)
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
					if (ShouldRetry(ex, sqlState, attempt))
					{
						await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
						continue;
					}
					return CreateExceptionResult<TResult>(ex, ClassifyException(ex, sqlState));
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

			return DatabaseResult<TResult>.Failure(DatabaseErrorCodes.MaxRetries, MaxRetriesMessage, isTransient: true);
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
		/// Delegates to the generic overload with a unit return value to eliminate code duplication.
		/// Use this for queries (Exists/Load/Get/Fetch) to avoid unnecessary transaction overhead.
		/// Prefer <see cref="EntityFrameworkQueryableExtensions.AsNoTracking{TEntity}(Linq.IQueryable{TEntity})"/> for pure reads.
		/// </remarks>
		protected async Task<DatabaseResult> ExecuteReadAsync(
			Func<NpgsqlDbContext, Task> action,
			[CallerMemberName] string? operationName = null,
			CancellationToken cancellationToken = default)
		{
			var result = await ExecuteReadAsync(
				async ctx =>
				{
					await action(ctx).ConfigureAwait(false);
					return true;
				},
				operationName,
				cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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
		/// Prefer <see cref="EntityFrameworkQueryableExtensions.AsNoTracking{TEntity}(Linq.IQueryable{TEntity})"/> for pure reads.
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

			for (int attempt = 1; attempt <= RetryPolicy.MaxRetries; attempt++)
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
					if (ShouldRetry(ex, sqlState, attempt))
					{
						await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
						continue;
					}
					return CreateExceptionResult<TResult>(ex, ClassifyException(ex, sqlState));
				}
			}

			return DatabaseResult<TResult>.Failure(DatabaseErrorCodes.MaxRetries, MaxRetriesMessage, isTransient: true);
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
			catch (Exception ex)
			{
				// Monitoring must never break core database execution.
				Debug.WriteLine($"[FishMMO-DB] Monitoring failure in RecordOperationAttempt: {ex.Message}");
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
			if (sqlState == PostgresSqlState.TooManyConnections)
			{
				PoolMetrics?.RecordPoolExhaustion();
			}
			else if (IsConnectionSqlState(sqlState))
			{
				PoolMetrics?.RecordConnectionError();
			}
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
				return (DatabaseErrorCodes.DuplicateReplay, message, false);
			}

			if (ex is OperationCanceledException)
			{
				return (DatabaseErrorCodes.Canceled, "The database operation was canceled.", false);
			}

			// Prefer explicit, safe database-layer exceptions.
			// These are designed to avoid leaking SQL/connection details to callers.
			if (ex is DatabaseException dbEx)
			{
				return (
					string.IsNullOrWhiteSpace(dbEx.ErrorCode) ? DatabaseErrorCodes.DatabaseError : dbEx.ErrorCode,
					string.IsNullOrWhiteSpace(dbEx.SafeMessage) ? "A database error occurred." : dbEx.SafeMessage,
					dbEx.IsTransient);
			}

			if (IsPgBouncerConfigurationSqlState(sqlState))
			{
				return (DatabaseErrorCodes.InvalidConfiguration, "Database authentication configuration is invalid.", false);
			}

			if (sqlState == PostgresSqlState.UniqueViolation)
			{
				return (DatabaseErrorCodes.UniqueViolation, "The record already exists.", false);
			}

			if (sqlState == PostgresSqlState.ForeignKeyViolation)
			{
				return (DatabaseErrorCodes.ForeignKeyViolation, "A referenced record was not found.", false);
			}

			if (sqlState == PostgresSqlState.NotNullViolation)
			{
				return (DatabaseErrorCodes.NotNullViolation, "A required field was missing.", false);
			}

			if (sqlState == PostgresSqlState.CheckViolation)
			{
				return (DatabaseErrorCodes.CheckViolation, "One or more values were invalid.", false);
			}

			if (ex is ArgumentException argEx)
			{
				return (DatabaseErrorCodes.InvalidArgument, SanitizeExceptionMessage(argEx.Message), false);
			}

			if (ex is InvalidOperationException invEx)
			{
				return (DatabaseErrorCodes.InvalidOperation, SanitizeExceptionMessage(invEx.Message), false);
			}

			// Avoid leaking internal DB details (SQL text, schema names, constraint names, etc.).
			// If the failure is likely transient, callers may choose to retry.
			var isTransient = IsTransientDatabaseFailure(ex, sqlState);
			return (DatabaseErrorCodes.DatabaseError, "A database error occurred.", isTransient);
		}

		/// <summary>
		/// Strips parameter names and internal details from .NET exception messages to prevent
		/// leaking implementation details (method parameter names, internal variable names) to
		/// remote callers via <see cref="DatabaseResult.ErrorMessage" />.
		/// <para>
		/// Handles both .NET 5+ format (<c>(Parameter 'paramName')</c>) and .NET Framework format
		/// (<c>Parameter name: paramName</c>), including the trailing actual-value line.
		/// </para>
		/// </summary>
		private static string SanitizeExceptionMessage(string message)
		{
			if (string.IsNullOrEmpty(message))
				return message;

			// Strip .NET 5+ trailing parameter annotation: " (Parameter 'paramName')"
			message = SanitizePatternRegex.Replace(message, string.Empty);

			// Strip .NET Framework "Parameter name: xxx" and optional "Actual value was yyy." lines
			int paramNameIdx = message.IndexOf("Parameter name: ", StringComparison.Ordinal);
			if (paramNameIdx >= 0)
			{
				message = message.Substring(0, paramNameIdx).TrimEnd();
			}

			// Strip newline trailing from Framework format if present after stripping
			return message.TrimEnd();
		}

		/// <summary>
		/// Determines whether an exception represents a transient failure that is safe to retry.
		/// </summary>
		private static bool IsTransientDatabaseFailure(Exception exception, string? sqlState) =>
			SqlStateHelper.IsTransientDatabaseFailure(exception, sqlState);

		/// <summary>
		/// Extracts the PostgreSQL SQLSTATE from an exception chain, if present.
		/// </summary>
		private static string? TryGetPostgresSqlState(Exception exception) =>
			SqlStateHelper.TryGetPostgresSqlState(exception);

		/// <summary>
		/// Determines whether a SQLSTATE represents a connection-level failure.
		/// </summary>
		private static bool IsConnectionSqlState(string? sqlState) =>
			SqlStateHelper.IsConnectionSqlState(sqlState);

		/// <summary>
		/// Determines whether a SQLSTATE is a PgBouncer configuration/authentication error.
		/// </summary>
		private static bool IsPgBouncerConfigurationSqlState(string? sqlState) =>
			SqlStateHelper.IsPgBouncerConfigurationSqlState(sqlState);

		/// <summary>
		/// Executes a raw SQL statement that produces a result set and maps the first returned row.
		/// Use this for non-composable DML statements with RETURNING (INSERT/UPDATE … RETURNING)
		/// where exactly one row is expected.
		/// <para>Reuses the ambient EF Core connection and transaction so the call is atomic when invoked
		/// inside <see cref="ExecuteWriteAsync{TResult}"/> or <see cref="ExecuteTransactionAsync{TResult}"/>.</para>
		/// </summary>
		/// <typeparam name="TResult">The type produced by the <paramref name="map"/> delegate.</typeparam>
		/// <param name="dbContext">The active DbContext providing the connection and ambient transaction.</param>
		/// <param name="sql">
		/// Parameterized SQL using <c>{0}</c>, <c>{1}</c>, … placeholders (same syntax as
		/// <see cref="Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync"/>).
		/// Must never embed user-controlled identifiers.
		/// </param>
		/// <param name="parameters">Positional parameter values corresponding to the SQL placeholders.</param>
		/// <param name="map">A delegate that reads one row from the <see cref="DbDataReader"/> and returns <typeparamref name="TResult"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The mapped result of the first row.</returns>
		/// <exception cref="DatabaseException">Thrown when no row is returned.</exception>
		protected static async Task<TResult> ExecuteReturningAsync<TResult>(
			NpgsqlDbContext dbContext,
			string sql,
			object[] parameters,
			Func<DbDataReader, TResult> map,
			CancellationToken cancellationToken)
		{
			var connection = dbContext.Database.GetDbConnection();
			if (connection.State != ConnectionState.Open)
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
			}
			using var command = connection.CreateCommand();
			command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
			command.CommandText = ParameterPlaceholderRegex.Replace(sql, "@p$1");
			for (int i = 0; i < parameters.Length; i++)
			{
				var param = command.CreateParameter();
				param.ParameterName = "@p" + i;
				param.Value = parameters[i] ?? DBNull.Value;
				command.Parameters.Add(param);
			}
			using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
			if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
			{
				throw new DatabaseException("The database command did not return a row.", errorCode: DatabaseErrorCodes.DatabaseError);
			}
			return map(reader);
		}

		/// <summary>
		/// Executes a raw SQL statement that produces a result set and maps the first returned row,
		/// or returns <c>default</c> when no row is returned.
		/// Use this for non-composable DML statements with RETURNING (INSERT/UPDATE … RETURNING)
		/// where zero or one rows are expected.
		/// <para>Reuses the ambient EF Core connection and transaction so the call is atomic when invoked
		/// inside <see cref="ExecuteWriteAsync{TResult}"/> or <see cref="ExecuteTransactionAsync{TResult}"/>.</para>
		/// </summary>
		/// <typeparam name="TResult">The type produced by the <paramref name="map"/> delegate.</typeparam>
		/// <param name="dbContext">The active DbContext providing the connection and ambient transaction.</param>
		/// <param name="sql">
		/// Parameterized SQL using <c>{0}</c>, <c>{1}</c>, … placeholders (same syntax as
		/// <see cref="Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync"/>).
		/// Must never embed user-controlled identifiers.
		/// </param>
		/// <param name="parameters">Positional parameter values corresponding to the SQL placeholders.</param>
		/// <param name="map">A delegate that reads one row from the <see cref="DbDataReader"/> and returns <typeparamref name="TResult"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The mapped result of the first row, or <c>default</c> when no row is returned.</returns>
		protected static async Task<TResult?> ExecuteReturningOrDefaultAsync<TResult>(
			NpgsqlDbContext dbContext,
			string sql,
			object[] parameters,
			Func<DbDataReader, TResult> map,
			CancellationToken cancellationToken)
		{
			var connection = dbContext.Database.GetDbConnection();
			if (connection.State != ConnectionState.Open)
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
			}
			using var command = connection.CreateCommand();
			command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
			command.CommandText = ParameterPlaceholderRegex.Replace(sql, "@p$1");
			for (int i = 0; i < parameters.Length; i++)
			{
				var param = command.CreateParameter();
				param.ParameterName = "@p" + i;
				param.Value = parameters[i] ?? DBNull.Value;
				command.Parameters.Add(param);
			}
			using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
			if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
			{
				return default;
			}
			return map(reader);
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

			var upsertStatement = sql.TrimEnd(';');
			upsertStatement = upsertStatement.Trim();

			var countSql = $@"WITH upserted AS (
				{upsertStatement}
				RETURNING 1
			)
			SELECT COUNT(*)::integer AS value FROM upserted";

			var count = await ExecuteScalarIntAsync(dbContext, countSql, parameters, cancellationToken).ConfigureAwait(false);

			if (count != expectedRowsAffected)
			{
				throw new StaleStateException(staleStateMessage);
			}
		}

		private static readonly Regex ParameterPlaceholderRegex =
			new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

		/// <summary>
		/// Matches the .NET 5+ trailing parameter annotation pattern appended to exception messages,
		/// e.g. <c>"Value cannot be null. (Parameter 'name')"</c>. Stripping this prevents leaking
		/// method parameter names to remote callers.
		/// </summary>
		private static readonly Regex SanitizePatternRegex =
			new Regex(@"\s*\(Parameter\s+'[^']*'\)\s*$", RegexOptions.Compiled);

		/// <summary>
		/// Executes a raw SQL query that returns a single integer scalar value using ADO.NET,
		/// bypassing EF Core entity mapping entirely.
		/// </summary>
		protected static async Task<int> ExecuteScalarIntAsync(
			NpgsqlDbContext dbContext,
			string sql,
			object[] parameters,
			CancellationToken cancellationToken)
		{
			var result = await ExecuteScalarCoreAsync(dbContext, sql, parameters, cancellationToken).ConfigureAwait(false);
			return Convert.ToInt32(result);
		}

		/// <summary>
		/// Executes a raw SQL query that returns a single bigint scalar value using ADO.NET,
		/// bypassing EF Core entity mapping entirely.
		/// </summary>
		protected static async Task<long> ExecuteScalarLongAsync(
			NpgsqlDbContext dbContext,
			string sql,
			object[] parameters,
			CancellationToken cancellationToken)
		{
			var result = await ExecuteScalarCoreAsync(dbContext, sql, parameters, cancellationToken).ConfigureAwait(false);
			return Convert.ToInt64(result);
		}

		private static async Task<object> ExecuteScalarCoreAsync(
			NpgsqlDbContext dbContext,
			string sql,
			object[] parameters,
			CancellationToken cancellationToken)
		{
			var connection = dbContext.Database.GetDbConnection();
			if (connection.State != ConnectionState.Open)
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
			}

			using var command = connection.CreateCommand();
			command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
			command.CommandText = ParameterPlaceholderRegex.Replace(sql, "@p$1");

			for (int i = 0; i < parameters.Length; i++)
			{
				var param = command.CreateParameter();
				param.ParameterName = "@p" + i;
				param.Value = parameters[i] ?? DBNull.Value;
				command.Parameters.Add(param);
			}

			return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
		}
	}
}