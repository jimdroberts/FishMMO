using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Threading.Tasks;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Exceptions;

namespace FishMMO.Database.Npgsql.Services
{
	public abstract class BaseService<TEntity> where TEntity : class
	{
		protected readonly INpgsqlDbContextFactory DbContextFactory;
		protected readonly string TableName;
		private const int MaxRetries = 3;

		protected BaseService(INpgsqlDbContextFactory contextFactory)
		{
			DbContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));

			// Cache table name once at construction - dispose context immediately
			using var dbContext = DbContextFactory.CreateDbContext();
			TableName = dbContext.GetTableName<TEntity>();
		}

		/// <summary>
		/// Executes a database operation with a fresh context and transaction.
		/// Handles retries for transient failures and technical concurrency conflicts.
		/// Returns DatabaseResult with success or failure information.
		/// </summary>
		protected async Task<DatabaseResult> ExecuteMirrorAsync(Func<NpgsqlDbContext, Task> action)
		{
			for (int attempt = 1; attempt <= MaxRetries; attempt++)
			{
				// NEW Context: Clears the cache and ensures we get fresh data on retry
				using var context = DbContextFactory.CreateDbContext();

				// NEW Transaction: Required because Postgres kills the transaction on failure
				using var transaction = await context.Database.BeginTransactionAsync();

				try
				{
					await action(context);
					await context.SaveChangesAsync();
					await transaction.CommitAsync();
					return DatabaseResult.Success(); // Success!
				}
				catch (Exception ex)
				{
					// Rollback the dead transaction immediately
					try { await transaction.RollbackAsync(); } catch { /* Ignore socket errors */ }

					var sqlState = TryGetPostgresSqlState(ex);

					// Logic failures (Stale Version) should NEVER be retried
					if (ex is StaleStateException)
					{
						return DatabaseResult.Failure("STALE_STATE", ex.Message, isTransient: false);
					}

					// Technical failures (xmin conflict, deadlock, timeout) are retryable
					if (attempt < MaxRetries && (ex is DbUpdateConcurrencyException || IsTransientDatabaseFailure(ex, sqlState)))
					{
						// Linear backoff with jitter to help the DB recover
						await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt));
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
		/// Returns DatabaseResult<TResult> with success or failure information.
		/// </summary>
		protected async Task<DatabaseResult<TResult>> ExecuteMirrorAsync<TResult>(Func<NpgsqlDbContext, Task<TResult>> action)
		{
			TResult result = default!;

			for (int attempt = 1; attempt <= MaxRetries; attempt++)
			{
				using var context = DbContextFactory.CreateDbContext();
				using var transaction = await context.Database.BeginTransactionAsync();

				try
				{
					result = await action(context);
					await context.SaveChangesAsync();
					await transaction.CommitAsync();
					return DatabaseResult<TResult>.Success(result);
				}
				catch (Exception ex)
				{
					try { await transaction.RollbackAsync(); } catch { }

					var sqlState = TryGetPostgresSqlState(ex);

					if (ex is StaleStateException)
					{
						return DatabaseResult<TResult>.Failure("STALE_STATE", ex.Message, isTransient: false);
					}

					if (attempt < MaxRetries && (ex is DbUpdateConcurrencyException || IsTransientDatabaseFailure(ex, sqlState)))
					{
						await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt));
						continue;
					}

					return HandleFinalException<TResult>(ex, sqlState);
				}
			}

			return DatabaseResult<TResult>.Failure("MAX_RETRIES", "Maximum retry attempts exceeded", isTransient: true);
		}

		private DatabaseResult HandleFinalException(Exception ex, string? sqlState)
		{
			// 23505 = unique_violation
			if (sqlState == "23505")
			{
				return DatabaseResult.Failure("UNIQUE_VIOLATION", "The record already exists.", isTransient: false);
			}

			if (ex is ArgumentException argEx)
			{
				return DatabaseResult.Failure("INVALID_ARGUMENT", argEx.Message, isTransient: false);
			}

			if (ex is InvalidOperationException invEx)
			{
				return DatabaseResult.Failure("INVALID_OPERATION", invEx.Message, isTransient: false);
			}

			if (ex is DatabaseEntityNotFoundException notFoundEx)
			{
				return DatabaseResult.Failure("ENTITY_NOT_FOUND", notFoundEx.Message, isTransient: false);
			}

			if (ex is DatabasePersistenceException persistEx)
			{
				return DatabaseResult.Failure("PERSISTENCE_FAILED", persistEx.Message, isTransient: true);
			}

			return DatabaseResult.Failure("DATABASE_ERROR", ex.Message, isTransient: true);
		}

		private DatabaseResult<TResult> HandleFinalException<TResult>(Exception ex, string? sqlState)
		{
			// 23505 = unique_violation
			if (sqlState == "23505")
			{
				return DatabaseResult<TResult>.Failure("UNIQUE_VIOLATION", "The record already exists.", isTransient: false);
			}

			if (ex is ArgumentException argEx)
			{
				return DatabaseResult<TResult>.Failure("INVALID_ARGUMENT", argEx.Message, isTransient: false);
			}

			if (ex is InvalidOperationException invEx)
			{
				return DatabaseResult<TResult>.Failure("INVALID_OPERATION", invEx.Message, isTransient: false);
			}

			if (ex is DatabaseEntityNotFoundException notFoundEx)
			{
				return DatabaseResult<TResult>.Failure("ENTITY_NOT_FOUND", notFoundEx.Message, isTransient: false);
			}

			if (ex is DatabasePersistenceException persistEx)
			{
				return DatabaseResult<TResult>.Failure("PERSISTENCE_FAILED", persistEx.Message, isTransient: true);
			}

			return DatabaseResult<TResult>.Failure("DATABASE_ERROR", ex.Message, isTransient: true);
		}

		private static string? TryGetPostgresSqlState(Exception exception)
		{
			for (var current = exception; current != null; current = current.InnerException)
			{
				if (current is PostgresException pgEx) return pgEx.SqlState;
			}
			return null;
		}

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

		private static bool IsTimeoutSqlState(string? sqlState) =>
			string.Equals(sqlState, "57014", StringComparison.Ordinal);

		private static bool IsConnectionSqlState(string? sqlState)
		{
			if (string.IsNullOrWhiteSpace(sqlState)) return false;
			return sqlState.StartsWith("08", StringComparison.Ordinal)
				|| sqlState == "57P01" || sqlState == "57P02" || sqlState == "57P03";
		}

		private static bool IsTransientSqlState(string? sqlState)
		{
			if (string.IsNullOrWhiteSpace(sqlState)) return false;
			return sqlState == "40P01" || sqlState == "40001" || sqlState == "55P03" || sqlState == "53300";
		}

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
	}
}