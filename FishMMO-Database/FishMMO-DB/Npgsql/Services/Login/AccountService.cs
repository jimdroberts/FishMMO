using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Account service with async operations, atomic SQL, and DTO pattern.
	/// Uses repository pattern with EF Core and raw SQL for race-condition-prone operations.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// Follows SOLID principles: SRP, OCP, LSP, ISP, DIP.
	/// </summary>
	/// <remarks>
	/// All methods that use ExecuteSqlInterpolatedAsync are wrapped in execution strategies
	/// to provide automatic retry logic (up to 3 attempts) for transient database failures
	/// such as connection timeouts, deadlocks, or network interruptions.
	/// 
	/// Exception Handling Strategy:
	/// - Catches specific exceptions (NpgsqlException, DbUpdateException, TimeoutException)
	/// - Converts to custom DatabaseException hierarchy with sanitized messages
	/// - Returns DatabaseResult for safe, typed error handling
	/// - Preserves detailed error information for logging while exposing safe messages to clients
	/// </remarks>
	public sealed class AccountService : IAccountService
	{
		/// <summary>
		/// Factory for creating database context instances with proper connection pooling and retry configuration.
		/// </summary>
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Compiled query for AccountExistsAsync hot path.
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<bool>> AccountExistsByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string accountName, CancellationToken ct) =>
				context.Accounts
					.AsNoTracking()
					.Any(a => a.Name == accountName));

		/// <summary>
		/// Initializes a new instance of AccountService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public AccountService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<DateTime>> GetLastLoginAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!IsValidUsername(accountName))
			{
				return DatabaseResult<DateTime>.Failure(
					"VALIDATION_ERROR",
					"Invalid username. Username must be between 3 and 32 characters.",
					isTransient: false);
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var account = await dbContext.Accounts
					.AsNoTracking()
					.Where(a => a.Name == accountName)
					.Select(a => new { a.Lastlogin })
					.FirstOrDefaultAsync(cancellationToken);

				if (account == null)
				{
					return DatabaseResult<DateTime>.FromException(
						new DatabaseEntityNotFoundException("Account", "by name", "Account not found."));
				}

				return DatabaseResult<DateTime>.Success(account.Lastlogin);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<DateTime>.FromException(
					new DatabaseTimeoutException("GetLastLogin", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<DateTime>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<DateTime>.FromException(
					new DatabaseQueryException(
						"GetLastLogin",
						"Failed to retrieve last login information.",
						$"Unexpected error in GetLastLoginAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> CreateAccountAsync(
			string accountName,
			string salt,
			string verifier,
			CancellationToken cancellationToken = default)
		{
			if (!IsValidUsername(accountName))
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid username. Username must be between 3 and 32 characters.",
					isTransient: false);
			}

			if (string.IsNullOrWhiteSpace(salt))
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Salt is required for account creation.",
					isTransient: false);
			}

			if (string.IsNullOrWhiteSpace(verifier))
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Verifier is required for account creation.",
					isTransient: false);
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				// Create execution strategy for automatic retry on transient database failures
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					// Use UPSERT (INSERT ON CONFLICT DO NOTHING) to prevent race conditions
					// PostgreSQL specific - atomic operation with automatic retry
					// Returns number of rows inserted (0 if conflict, 1 if created)
					var tableName = dbContext.GetTableName<AccountEntity>();
					return await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} 
						(name, salt, verifier, access_level, created, lastlogin) 
						VALUES 
							({accountName}, {salt}, {verifier}, {(byte)AccessLevel.Player}, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
						ON CONFLICT (name) DO NOTHING",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(
						new DatabaseConstraintException(
							ConstraintType.Unique,
							"accounts_name_key",
							"An account with this name already exists."));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("CreateAccount", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"accounts_name_key",
						"An account with this name already exists.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"CreateAccount",
						"Failed to create account due to a database error.",
						$"DbUpdateException in CreateAccountAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"CreateAccount",
						"An unexpected error occurred while creating the account.",
						$"Unexpected error in CreateAccountAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AccountData>> GetAccountForLoginAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!IsValidUsername(accountName))
			{
				return DatabaseResult<AccountData>.Failure(
					"VALIDATION_ERROR",
					"Invalid username. Username must be between 3 and 32 characters.",
					isTransient: false);
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var accountEntity = await dbContext.Accounts
					.AsNoTracking()
					.FirstOrDefaultAsync(a => a.Name == accountName, cancellationToken);

				if (accountEntity == null)
				{
					// Return generic error to prevent username enumeration
					return DatabaseResult<AccountData>.Failure(
						"ACCOUNT_NOT_FOUND",
						"Invalid account credentials.",
						isTransient: false);
				}

				if ((AccessLevel)accountEntity.AccessLevel == AccessLevel.Banned)
				{
					return DatabaseResult<AccountData>.Failure(
						"ACCOUNT_BANNED",
						"This account has been banned.",
						isTransient: false);
				}

				// Map Entity to DTO (manual mapping prevents entity tracking issues)
				var accountData = MapEntityToDto(accountEntity);

				return DatabaseResult<AccountData>.Success(accountData);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<AccountData>.FromException(
					new DatabaseTimeoutException("GetAccountForLogin", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<AccountData>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<AccountData>.FromException(
					new DatabaseQueryException(
						"GetAccountForLogin",
						"Failed to retrieve account information.",
						$"Unexpected error in GetAccountForLoginAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateLastLoginAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!IsValidUsername(accountName))
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid username. Username must be between 3 and 32 characters.",
					isTransient: false);
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				// Create execution strategy for automatic retry on transient database failures
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					// Atomic update without loading entity - prevents race conditions
					// Execution strategy provides automatic retry on transient failures
					var tableName = dbContext.GetTableName<AccountEntity>();
					return await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {tableName} 
						SET lastlogin = CURRENT_TIMESTAMP 
						WHERE name = {accountName}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(
						new DatabaseEntityNotFoundException("Account", "by name", "Account not found."));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("UpdateLastLogin", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"UpdateLastLogin",
						"Failed to update last login time.",
						$"DbUpdateException in UpdateLastLoginAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"UpdateLastLogin",
						"An unexpected error occurred while updating last login.",
						$"Unexpected error in UpdateLastLoginAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> AccountExistsAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!IsValidUsername(accountName))
			{
				// Return false (not failure) to prevent enumeration attacks
				return DatabaseResult<bool>.Success(false);
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				// Use compiled query for hot path performance
				var exists = await AccountExistsByNameQuery(dbContext, accountName, cancellationToken);

				return DatabaseResult<bool>.Success(exists);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<bool>.FromException(
					new DatabaseTimeoutException("AccountExists", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<bool>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<bool>.FromException(
					new DatabaseQueryException(
						"AccountExists",
						"Failed to check account existence.",
						$"Unexpected error in AccountExistsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <summary>
		/// Validates username according to business rules.
		/// </summary>
		/// <param name="username">The username to validate.</param>
		/// <returns>True if username meets all validation criteria; otherwise, false.</returns>
		/// <remarks>
		/// Username validation rules:
		/// - Must not be null, empty, or whitespace only
		/// - Minimum length: 3 characters
		/// - Maximum length: 32 characters
		/// 
		/// This validation is performed before any database operations to prevent
		/// unnecessary database calls and to provide consistent validation across all methods.
		/// </remarks>
		private bool IsValidUsername(string username)
		{
			return !string.IsNullOrWhiteSpace(username) &&
				   username.Length >= 3 &&
				   username.Length <= 32;
		}

		/// <summary>
		/// Maps AccountEntity to AccountData DTO.
		/// Performs defensive copying to prevent entity tracking issues.
		/// </summary>
		/// <param name="entity">The account entity retrieved from the database.</param>
		/// <returns>A data transfer object containing account information.</returns>
		/// <remarks>
		/// This method performs a shallow copy of entity data to a DTO.
		/// All properties are value types or immutable strings, so deep cloning is not required.
		/// 
		/// The DTO pattern is used to:
		/// - Decouple database entities from business logic
		/// - Prevent accidental entity modifications
		/// - Enable entity disposal without affecting returned data
		/// - Provide a clean API contract for service consumers
		/// </remarks>
		private AccountData MapEntityToDto(AccountEntity entity)
		{
			return new AccountData
			{
				Name = entity.Name,
				Salt = entity.Salt,
				Verifier = entity.Verifier,
				AccessLevel = entity.AccessLevel,
				Created = entity.TimeCreated,
				LastLogin = entity.Lastlogin
			};
		}
	}
}