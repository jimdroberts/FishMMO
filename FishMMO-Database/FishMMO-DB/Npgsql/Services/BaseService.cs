using System;
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

			// Cache table name once at construction - dispose context immediately
			using var dbContext = DbContextFactory.CreateDbContext();
			TableName = dbContext.GetTableName<TEntity>();
		}

		/// <summary>
		/// Executes a database operation with execution strategy for automatic retry on transient failures.
		/// </summary>
		/// <typeparam name="TResult">The result type.</typeparam>
		/// <param name="operation">The operation to execute.</param>
		/// <param name="operationName">Name of the operation for error reporting.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult containing the operation result or error.</returns>
		protected async Task<DatabaseResult<TResult>> ExecuteWithStrategyAsync<TResult>(
			Func<NpgsqlDbContext, Task<TResult>> operation,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			await using var dbContext = DbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();
				var result = await strategy.ExecuteAsync(() => operation(dbContext));
				return DatabaseResult<TResult>.Success(result);
			}
			catch (Exception ex)
			{
				return DatabaseResult<TResult>.FromException(MapException(ex, operationName, dbContext));
			}
		}

		/// <summary>
		/// Executes a database operation without return value with execution strategy.
		/// </summary>
		/// <param name="operation">The operation to execute.</param>
		/// <param name="operationName">Name of the operation for error reporting.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult indicating success or error.</returns>
		protected async Task<DatabaseResult> ExecuteWithStrategyAsync(
			Func<NpgsqlDbContext, Task> operation,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			await using var dbContext = DbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();
				await strategy.ExecuteAsync(() => operation(dbContext));
				return DatabaseResult.Success();
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(MapException(ex, operationName, dbContext));
			}
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

				OperationCanceledException cancelEx => new DatabaseTimeoutException(
					operationName,
					30,
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
					dbContext?.Database.GetConnectionString() ?? "unknown",
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
		/// Executes a database operation with explicit control over execution strategy.
		/// Use this overload for raw SQL operations. TableName is available as a protected field.
		/// </summary>
		/// <typeparam name="TResult">The result type.</typeparam>
		/// <param name="operation">The operation to execute with dbContext and strategy.</param>
		/// <param name="operationName">Name of the operation for error reporting.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult containing the operation result or error.</returns>
		protected async Task<DatabaseResult<TResult>> ExecuteWithStrategyAsync<TResult>(
			Func<NpgsqlDbContext, IExecutionStrategy, Task<TResult>> operation,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			await using var dbContext = DbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();
				var result = await operation(dbContext, strategy);
				return DatabaseResult<TResult>.Success(result);
			}
			catch (Exception ex)
			{
				return DatabaseResult<TResult>.FromException(MapException(ex, operationName, dbContext));
			}
		}

		/// <summary>
		/// Executes a database operation without return value with explicit control over execution strategy.
		/// Use this overload for raw SQL operations. TableName is available as a protected field.
		/// </summary>
		/// <param name="operation">The operation to execute with dbContext and strategy.</param>
		/// <param name="operationName">Name of the operation for error reporting.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult indicating success or error.</returns>
		protected async Task<DatabaseResult> ExecuteWithStrategyAsync(
			Func<NpgsqlDbContext, IExecutionStrategy, Task> operation,
			string operationName,
			CancellationToken cancellationToken = default)
		{
			await using var dbContext = DbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();
				await operation(dbContext, strategy);
				return DatabaseResult.Success();
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(MapException(ex, operationName, dbContext));
			}
		}

		/// <summary>
	/// Executes a database operation within an explicit transaction scope.
	/// Use this for operations that span multiple tables and require atomicity.
	/// Note: Execution strategies cannot be used with user-controlled transactions.
	/// </summary>
	/// <typeparam name="TResult">The result type.</typeparam>
	/// <param name="operation">The operation to execute with dbContext and transaction.</param>
	/// <param name="operationName">Name of the operation for error reporting.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>DatabaseResult containing the operation result or error.</returns>
	protected async Task<DatabaseResult<TResult>> ExecuteInTransactionAsync<TResult>(
		Func<NpgsqlDbContext, IDbContextTransaction, Task<TResult>> operation,
		string operationName,
		CancellationToken cancellationToken = default)
	{
		await using var dbContext = DbContextFactory.CreateDbContext();

		try
		{
			// Begin explicit transaction for multi-table atomicity
			await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
			try
			{
				var result = await operation(dbContext, transaction);
				await transaction.CommitAsync(cancellationToken);
				return DatabaseResult<TResult>.Success(result);
			}
			catch
			{
				await transaction.RollbackAsync(cancellationToken);
				throw;
			}
		}
		catch (Exception ex)
		{
			return DatabaseResult<TResult>.FromException(MapException(ex, operationName, dbContext));
		}
	}

	/// <summary>
	/// Executes a database operation without return value within an explicit transaction scope.
	/// Use this for operations that span multiple tables and require atomicity.
	/// Note: Execution strategies cannot be used with user-controlled transactions.
	/// </summary>
	/// <param name="operation">The operation to execute with dbContext and transaction.</param>
	/// <param name="operationName">Name of the operation for error reporting.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>DatabaseResult indicating success or error.</returns>
	protected async Task<DatabaseResult> ExecuteInTransactionAsync(
		Func<NpgsqlDbContext, IDbContextTransaction, Task> operation,
		string operationName,
		CancellationToken cancellationToken = default)
	{
		await using var dbContext = DbContextFactory.CreateDbContext();

		try
		{
			// Begin explicit transaction for multi-table atomicity
			await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
			try
			{
				await operation(dbContext, transaction);
				await transaction.CommitAsync(cancellationToken);
				return DatabaseResult.Success();
			}
			catch
			{
				await transaction.RollbackAsync(cancellationToken);
				throw;
			}
		}
		catch (Exception ex)
		{
			return DatabaseResult.FromException(MapException(ex, operationName, dbContext));
		}
	}

	/// <summary>
		/// Returns DatabaseEntityNotFoundException if no rows were affected.
		/// </summary>
		/// <param name="rowsAffected">Number of rows affected by the operation.</param>
		/// <param name="entityName">Name of the entity for error message.</param>
		/// <param name="entityId">ID of the entity for error message.</param>
		/// <returns>DatabaseResult indicating success or EntityNotFound error.</returns>
		protected DatabaseResult ValidateRowsAffected(int rowsAffected, string entityName, object entityId)
		{
			if (rowsAffected == 0)
			{
				return DatabaseResult.FromException(
					new DatabaseEntityNotFoundException(entityName, entityId?.ToString() ?? "unknown"));
			}
			return DatabaseResult.Success();
		}

		/// <summary>
		/// Validates that an entity is not null.
		/// Returns DatabaseEntityNotFoundException if entity is null.
		/// </summary>
		/// <typeparam name="T">The entity type to validate.</typeparam>
		/// <param name="entity">The entity to validate.</param>
		/// <param name="entityName">Name of the entity for error message.</param>
		/// <param name="entityId">ID of the entity for error message.</param>
		/// <returns>DatabaseResult with entity or EntityNotFound error.</returns>
		protected DatabaseResult<T> ValidateEntityExists<T>(T entity, string entityName, object entityId)
			where T : class
		{
			if (entity == null)
			{
				return DatabaseResult<T>.FromException(
					new DatabaseEntityNotFoundException(entityName, entityId?.ToString() ?? "unknown"));
			}
			return DatabaseResult<T>.Success(entity);
		}
	}
}