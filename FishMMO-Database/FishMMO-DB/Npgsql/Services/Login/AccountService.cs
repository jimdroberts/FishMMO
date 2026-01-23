using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
	public sealed class AccountService : BaseService<AccountEntity>, IAccountService
	{
		/// <summary>
		/// Compiled query for AccountExistsAsync hot path.
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<bool>> AccountExistsByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string accountName, CancellationToken ct) =>
				context.Accounts
					.AsNoTracking()
					.Any(a => a.Name == accountName));

		/// <summary>	/// Compiled query for GetAccountForLoginAsync hot path (login authentication).
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<AccountEntity?>> GetAccountForLoginQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string accountName, CancellationToken ct) =>
				context.Accounts
					.AsNoTracking()
					.FirstOrDefault(a => a.Name == accountName));
#pragma warning restore CS8619

		/// <summary>		/// Initializes a new instance of AccountService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public AccountService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
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

			return await ExecuteSqlAsync(async (dbContext, ct) =>
			{
				var account = await dbContext.Accounts
					.AsNoTracking()
					.Where(a => a.Name == accountName)
					.Select(a => new { a.LastLogin })
					.FirstOrDefaultAsync(ct).ConfigureAwait(false);

				if (account == null)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}

				return account.LastLogin;
			}, "GetLastLogin", cancellationToken).ConfigureAwait(false);
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

			var result = await ExecuteSqlAsync(
				$@"INSERT INTO {TableName} 
					(name, salt, verifier, access_level, created, lastlogin) 
				VALUES 
					({accountName}, {salt}, {verifier}, {(byte)AccessLevel.Player}, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
				ON CONFLICT (name) DO NOTHING",
				"CreateAccount",
				cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!result.IsSuccess)
			{
				return DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			// INSERT ... ON CONFLICT DO NOTHING affects 0 rows on duplicate.
			if (result.Data == 0)
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"accounts_name_key",
						"An account with this name already exists."));
			}

			return DatabaseResult.Success();
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

			var result = await ExecuteSqlAsync(async (dbContext, ct) =>
				await GetAccountForLoginQuery(dbContext, accountName, ct).ConfigureAwait(false),
				"GetAccountForLogin",
				cancellationToken).ConfigureAwait(false);

			if (!result.IsSuccess)
			{
				return DatabaseResult<AccountData>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			var accountEntity = result.Data;
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

			// Map Entity to DTO
			var accountData = MapEntityToDto(accountEntity);
			return DatabaseResult<AccountData>.Success(accountData);
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

			var result = await ExecuteSqlAsync(
				$@"UPDATE {TableName} 
					SET lastlogin = CURRENT_TIMESTAMP 
					WHERE name = {accountName}",
				"UpdateLastLogin",
				entityName: "Account",
				entityId: accountName,
				requireRowsAffected: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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

			return await ExecuteSqlAsync(async (dbContext, ct) =>
			{
				// Use compiled query for hot path performance
				return await AccountExistsByNameQuery(dbContext, accountName, ct).ConfigureAwait(false);
			}, "AccountExists", cancellationToken).ConfigureAwait(false);
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
		/// unnecessary queries and reduce enumeration risk.
		/// </remarks>
		private static bool IsValidUsername(string username)
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
		/// </remarks>
		private static AccountData MapEntityToDto(AccountEntity entity)
		{
			return new AccountData(
				name: entity.Name,
				salt: entity.Salt,
				verifier: entity.Verifier,
				accessLevel: entity.AccessLevel,
				created: entity.TimeCreated,
				lastLogin: entity.LastLogin
			);
		}
	}
}