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
		// NOTE: Some methods use Authentication.NormalizeAccountLookup() while others use
		// .ToLowerInvariant(). Both normalize to lowercase but may diverge in the future.
		// Consolidate when the authentication normalization logic stabilizes.

		/// <summary>
		/// Compiled query for ExistsAsync hot path.
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// Filters on the case-insensitive <c>NameLowercase</c> computed column.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<bool>> accountExistsByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string accountNameLower, CancellationToken ct) =>
				context.Accounts
					.AsNoTracking()
					.Any(a => a.NameLowercase == accountNameLower));

		/// <summary>
		/// Compiled query for FetchForLoginAsync by account name (case-insensitive).
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<AccountEntity?>> getAccountForLoginByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string accountNameLower, CancellationToken ct) =>
				context.Accounts
					.AsNoTracking()
					.FirstOrDefault(a => a.NameLowercase == accountNameLower));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for FetchForLoginAsync by email.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<AccountEntity?>> getAccountForLoginByEmailQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string email, CancellationToken ct) =>
				context.Accounts
					.AsNoTracking()
					.FirstOrDefault(a => a.Email != null && a.Email.ToLower() == email.ToLower()));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for FetchLastLoginAsync by account name (case-insensitive).
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<DateTime?>> getLastLoginByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string accountNameLower, CancellationToken ct) =>
				context.Accounts
					.AsNoTracking()
					.Where(a => a.NameLowercase == accountNameLower)
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
					.Where(a => a.Email != null && a.Email.ToLower() == email.ToLower())
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
					: await getLastLoginByNameQuery(dbContext, username.ToLowerInvariant(), cancellationToken).ConfigureAwait(false);
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
				// name is stored as lowercase via ToLowerInvariant(). Case-insensitive uniqueness
				// is enforced by the name_lowercase generated column.
				var sql = $@"INSERT INTO {TableName} (name, salt, verifier, access_level, email, age)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}})
					ON CONFLICT (name_lowercase) DO NOTHING";
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { accountName.ToLowerInvariant(), salt, verifier, (byte)AccessLevel.Player, email, age },
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
					: await getAccountForLoginByNameQuery(dbContext, username.ToLowerInvariant(), cancellationToken).ConfigureAwait(false),
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

			// Return generic error to prevent banned-account enumeration.
			// Same message as null account above so attackers cannot distinguish
			// a banned account from a non-existent one.
			if ((AccessLevel)accountEntity.AccessLevel == AccessLevel.Banned)
			{
				return DatabaseResult<AccountData>.Failure(
					DatabaseErrorCodes.Forbidden,
					"Invalid account credentials.");
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
				var sql = $@"UPDATE {TableName} SET last_login = {{0}} WHERE name_lowercase = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, accountName.ToLowerInvariant() }, cancellationToken)
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
				await accountExistsByNameQuery(dbContext, accountName.ToLowerInvariant(), cancellationToken).ConfigureAwait(false),
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
				totpEnabled: entity.TotpEnabled,
				totpSecret: entity.TotpSecret,
				totpVerifiedAt: entity.TotpVerifiedAt,
				lastTotpWindow: entity.LastTotpWindow,
				discordLinkCode: entity.DiscordLinkCode,
				verified: entity.Verified,
				verifyCode: entity.VerifyCode,
				verifyCodeExpiresUtc: entity.VerifyCodeExpiresUtc,
				verificationEmailSentAt: entity.VerificationEmailSentAt,
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
				// When email is changed, reset the verified flag and verification code.
				// The new email must be verified independently.
				var sql = $@"UPDATE {TableName} SET email = {{0}}, verified = FALSE, verify_code = NULL WHERE name_lowercase = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { (object?)email ?? DBNull.Value, accountName.ToLowerInvariant() }, cancellationToken)
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
				var sql = $@"UPDATE {TableName} SET age = {{0}} WHERE name_lowercase = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { age, accountName.ToLowerInvariant() }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistTotpSecretAsync(
			string accountName,
			string encryptedTotpSecret,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			if (string.IsNullOrWhiteSpace(encryptedTotpSecret) || encryptedTotpSecret.Length > 256)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"TOTP secret is required and must not exceed 256 characters.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET totp_secret = {{0}} WHERE name_lowercase = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { encryptedTotpSecret, accountName.ToLowerInvariant() }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistTotpEnabledAsync(
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
				var sql = $@"UPDATE {TableName} SET totp_enabled = {{0}} WHERE name_lowercase = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { enabled, accountName.ToLowerInvariant() }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistTotpVerifiedAtAsync(
			string accountName,
			long totpWindow,
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
				var sql = $@"UPDATE {TableName} SET totp_verified_at = {{0}}, totp_enabled = true, last_totp_window = {{1}} WHERE name_lowercase = {{2}} AND totp_verified_at IS NULL";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, totpWindow, accountName.ToLowerInvariant() }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseException("Account not found or TOTP already verified.", errorCode: DatabaseErrorCodes.ValidationError);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistLastTotpWindowAsync(
			string accountName,
			long totpWindow,
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
				var sql = $@"UPDATE {TableName} SET last_totp_window = {{0}} WHERE name_lowercase = {{1}} AND last_totp_window < {{0}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { totpWindow, accountName.ToLowerInvariant() }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseException("TOTP code replay detected or account not found.", errorCode: DatabaseErrorCodes.ValidationError);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> ClearTotpAsync(
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
				var sql = $@"UPDATE {TableName} SET totp_secret = NULL, totp_enabled = false, totp_verified_at = NULL, last_totp_window = 0 WHERE name_lowercase = {{0}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { accountName.ToLowerInvariant() }, cancellationToken)
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
				var sql = $@"UPDATE {TableName} SET discord_link_code = {{0}} WHERE name_lowercase = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { (object?)linkCode ?? DBNull.Value, accountName.ToLowerInvariant() }, cancellationToken)
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

			// Reject the sentinel verify_code=0 early. Allowing it through would let a caller
			// "verify" any account whose VerifyCode column still defaults to 0 (i.e. never had a
			// code generated, or was already verified previously).
			if (verifyCode == 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid verification code or account already verified.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				// Single atomic check-and-update: succeed only if the supplied code matches a
				// pending, unexpired verification request for this account. verify_code <> 0
				// rejects the sentinel column-default value defensively even though the caller
				// also pre-screens it above.
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				var sql = $@"UPDATE {TableName}
					SET verified = true, verify_code = 0, verify_code_expires_utc = NULL
					WHERE name_lowercase = {{0}}
						AND verify_code = {{1}}
						AND verify_code <> 0
						AND verified = false
						AND (verify_code_expires_utc IS NULL OR verify_code_expires_utc > CURRENT_TIMESTAMP)";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { normalized, verifyCode }, cancellationToken)
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
			DateTime expiresUtc,
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
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				var sql = $@"UPDATE {TableName}
					SET verify_code = {{0}}, verify_code_expires_utc = {{1}}
					WHERE name_lowercase = {{2}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { verifyCode, expiresUtc, normalized }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistVerificationEmailSentAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName) || accountName.Length < 3 || accountName.Length > 32)
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Account name must be 3-32 characters.");
			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $"UPDATE {TableName} SET verification_email_sent_at = CURRENT_TIMESTAMP WHERE name_lowercase = {{0}}";
				var affected = await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { accountName.ToLowerInvariant() }, cancellationToken).ConfigureAwait(false);
				if (affected == 0)
					throw new DatabaseEntityNotFoundException("Account", accountName);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}