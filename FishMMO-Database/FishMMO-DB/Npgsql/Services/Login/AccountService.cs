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
using FishMMO.Shared;

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
		/// Compiled query for FetchForLoginAsync by account name.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<AccountEntity?>> getAccountForLoginByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string accountName, CancellationToken ct) =>
				context.Accounts
					.AsNoTracking()
					.FirstOrDefault(a => a.Name == accountName));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for FetchForLoginAsync by email.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<AccountEntity?>> getAccountForLoginByEmailQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string email, CancellationToken ct) =>
				context.Accounts
					.AsNoTracking()
					.FirstOrDefault(a => a.Email == email));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for FetchLastLoginAsync by account name.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<DateTime?>> getLastLoginByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string accountName, CancellationToken ct) =>
				context.Accounts
					.AsNoTracking()
					.Where(a => a.Name == accountName)
					.Select(a => (DateTime?)a.LastLogin)
					.FirstOrDefault());
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for FetchLastLoginAsync by email.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<DateTime?>> getLastLoginByEmailQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string email, CancellationToken ct) =>
				context.Accounts
					.AsNoTracking()
					.Where(a => a.Email == email)
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
			string username,
			bool email = false,
			CancellationToken cancellationToken = default)
		{
			if (email)
			{
				if (string.IsNullOrWhiteSpace(username) || username.Length > 320)
				{
					return DatabaseResult<DateTime>.Failure(
						DatabaseErrorCodes.ValidationError,
						"Email is required and must not exceed 320 characters.");
				}
			}
			else
			{
				if (!Authentication.IsAllowedUsername(username))
				{
					return DatabaseResult<DateTime>.Failure(
						DatabaseErrorCodes.ValidationError,
						Authentication.InvalidUsernameError);
				}
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var lastLogin = email
					? await getLastLoginByEmailQuery(dbContext, username, cancellationToken).ConfigureAwait(false)
					: await getLastLoginByNameQuery(dbContext, username, cancellationToken).ConfigureAwait(false);
				if (lastLogin == null)
				{
					throw new DatabaseEntityNotFoundException("Account", username);
				}
				return lastLogin.Value;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(
			string accountName,
			string salt,
			string verifier,
			string email,
			int age,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			if (string.IsNullOrWhiteSpace(salt))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Salt is required for account creation.");
			}

			if (string.IsNullOrWhiteSpace(verifier))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Verifier is required for account creation.");
			}

			if (string.IsNullOrWhiteSpace(email) || email.Length > 320)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Email is required and must not exceed 320 characters.");
			}

			if (age < 0 || age > 200)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Age must be between 0 and 200.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"INSERT INTO {TableName} (name, salt, verifier, access_level, email, age)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}})
					ON CONFLICT (name) DO NOTHING";
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { accountName, salt, verifier, (byte)AccessLevel.Player, email, age },
					cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected <= 0)
				{
					throw new DatabaseException("Account name already exists.", errorCode: DatabaseErrorCodes.UniqueViolation);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AccountData>> FetchForLoginAsync(
			string username,
			bool email = false,
			CancellationToken cancellationToken = default)
		{
			if (email)
			{
				if (string.IsNullOrWhiteSpace(username) || username.Length > 320)
				{
					return DatabaseResult<AccountData>.Failure(
						DatabaseErrorCodes.ValidationError,
						"Email is required and must not exceed 320 characters.");
				}
			}
			else
			{
				if (!Authentication.IsAllowedUsername(username))
				{
					return DatabaseResult<AccountData>.Failure(
						DatabaseErrorCodes.ValidationError,
						Authentication.InvalidUsernameError);
				}
			}

			var result = await ExecuteReadAsync(async dbContext =>
				email
					? await getAccountForLoginByEmailQuery(dbContext, username, cancellationToken).ConfigureAwait(false)
					: await getAccountForLoginByNameQuery(dbContext, username, cancellationToken).ConfigureAwait(false),
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
					DatabaseErrorCodes.NotFound,
					"Invalid account credentials.");
			}

			if ((AccessLevel)accountEntity.AccessLevel == AccessLevel.Banned)
			{
				return DatabaseResult<AccountData>.Failure(
					DatabaseErrorCodes.Forbidden,
					"This account has been banned.");
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
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
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
		private static AccountData MapEntityToDto(AccountEntity entity)
		{
			return new AccountData(
				name: entity.Name,
				salt: entity.Salt,
				verifier: entity.Verifier,
				accessLevel: entity.AccessLevel,
				email: entity.Email,
				age: entity.Age,
				twoFactorEnabled: entity.TwoFactorEnabled,
				twoFactorCode: entity.TwoFactorCode,
				discordLinkCode: entity.DiscordLinkCode,
				verified: entity.Verified,
				verifyCode: entity.VerifyCode,
				created: entity.TimeCreated,
				lastLogin: entity.LastLogin
			);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistEmailAsync(
			string accountName,
			string? email,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			if (email != null && email.Length > 320)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Email must not exceed 320 characters.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET email = {{0}} WHERE name = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { (object?)email ?? DBNull.Value, accountName }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAgeAsync(
			string accountName,
			int age,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			if (age < 0 || age > 200)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Age must be between 0 and 200.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET age = {{0}} WHERE name = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { age, accountName }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistTwoFactorEnabledAsync(
			string accountName,
			bool enabled,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET two_factor_enabled = {{0}} WHERE name = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { enabled, accountName }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistTwoFactorCodeAsync(
			string accountName,
			string? code,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			if (code != null && code.Length > 64)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Two-factor code must not exceed 64 characters.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET two_factor_code = {{0}} WHERE name = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { (object?)code ?? DBNull.Value, accountName }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistDiscordLinkCodeAsync(
			string accountName,
			string? linkCode,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			if (linkCode != null && linkCode.Length > 64)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Discord link code must not exceed 64 characters.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET discord_link_code = {{0}} WHERE name = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { (object?)linkCode ?? DBNull.Value, accountName }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AccountData?>> FetchByDiscordLinkCodeAsync(
			string linkCode,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(linkCode) || linkCode.Length > 64)
			{
				return DatabaseResult<AccountData?>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Discord link code must be a non-empty string of at most 64 characters.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var account = await dbContext.Accounts
					.AsNoTracking()
					.FirstOrDefaultAsync(a => a.DiscordLinkCode == linkCode, cancellationToken)
					.ConfigureAwait(false);

				return account != null ? (AccountData?)MapEntityToDto(account) : null;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistVerifiedAsync(
			string accountName,
			int verifyCode,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET verified = true, verify_code = 0 WHERE name = {{0}} AND verify_code = {{1}} AND verified = false";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { accountName, verifyCode }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseException("Invalid verification code or account already verified.", errorCode: DatabaseErrorCodes.ValidationError);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistVerifyCodeAsync(
			string accountName,
			int verifyCode,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET verify_code = {{0}} WHERE name = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { verifyCode, accountName }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}