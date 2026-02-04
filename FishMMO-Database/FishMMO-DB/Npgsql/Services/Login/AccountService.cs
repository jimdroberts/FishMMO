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
	/// Account service providing async operations for account creation and login.
	/// Uses EF Core compiled queries for hot paths and the BaseService execution strategy for retries.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// </summary>
	public sealed class AccountService : BaseService<AccountEntity>, IAccountService
	{
		/// <summary>
		/// Compiled query for ExistsAsync hot path.
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<bool>> accountExistsByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string accountName, CancellationToken ct) =>
				context.Accounts
					.AsNoTracking()
					.Any(a => a.Name == accountName));

		/// <summary>
		/// Compiled query for FetchForLoginAsync hot path (login authentication).
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<AccountEntity?>> getAccountForLoginQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string accountName, CancellationToken ct) =>
				context.Accounts
					.AsNoTracking()
					.FirstOrDefault(a => a.Name == accountName));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for FetchLastLoginAsync hot path.
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<DateTime?>> getLastLoginQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string accountName, CancellationToken ct) =>
				context.Accounts
					.AsNoTracking()
					.Where(a => a.Name == accountName)
					.Select(a => (DateTime?)a.LastLogin)
					.FirstOrDefault());
#pragma warning restore CS8619

		/// <summary>
		/// Initializes a new instance of AccountService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public AccountService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<DateTime>> FetchLastLoginAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<DateTime>.Failure(
					"VALIDATION_ERROR",
					Authentication.InvalidUsernameError,
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var lastLogin = await getLastLoginQuery(dbContext, accountName, cancellationToken).ConfigureAwait(false);
				if (lastLogin == null)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
				return lastLogin.Value;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(
			string accountName,
			string salt,
			string verifier,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					Authentication.InvalidUsernameError,
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

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"INSERT INTO {TableName} (name, salt, verifier, access_level)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}})
					ON CONFLICT (name) DO NOTHING";
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { accountName, salt, verifier, (byte)AccessLevel.Player },
					cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected <= 0)
				{
					throw new DatabaseException("Account name already exists.", "UNIQUE_VIOLATION", isTransient: false);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AccountData>> FetchForLoginAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<AccountData>.Failure(
					"VALIDATION_ERROR",
					Authentication.InvalidUsernameError,
					isTransient: false);
			}

			var result = await ExecuteReadAsync(async dbContext =>
				await getAccountForLoginQuery(dbContext, accountName, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);

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
		public async Task<DatabaseResult> PersistLastLoginAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					Authentication.InvalidUsernameError,
					isTransient: false);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName} SET last_login = {{0}} WHERE name = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, accountName }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> ExistsAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				// Return false (not failure) to prevent enumeration attacks
				return DatabaseResult<bool>.Success(false);
			}

			return await ExecuteReadAsync(async dbContext =>
				await accountExistsByNameQuery(dbContext, accountName, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
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