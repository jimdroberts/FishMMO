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

			/* A temporary ban ends here, at the first sign-in attempt after its instant, rather than
			 * on a schedule. Nothing has to run for a ban to lift, so there is no job to fall behind
			 * and no window in which a lapsed ban still refuses a player because a sweep has not
			 * reached them. The UPDATE re-checks every condition itself, so two concurrent sign-ins
			 * cannot both act on a row an operator re-banned permanently in between; only the one
			 * whose statement actually matched restores the level it then proceeds with. */
			if ((AccessLevel)accountEntity.AccessLevel == AccessLevel.Banned &&
				accountEntity.BannedUntil != null &&
				accountEntity.BannedUntil.Value <= DateTime.UtcNow)
			{
				DatabaseResult<bool> lifted = await LiftExpiredBanAsync(accountEntity.NameLowercase, cancellationToken).ConfigureAwait(false);
				if (lifted.IsSuccess && lifted.Data)
				{
					accountEntity.AccessLevel = (byte)AccessLevel.Player;
					accountEntity.BannedUntil = null;
					accountEntity.BannedBy = null;
					accountEntity.BanReason = null;
				}
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
				verified: entity.Verified,
				verifyCode: entity.VerifyCode,
				verifyCodeExpiresUtc: entity.VerifyCodeExpiresUtc,
				verificationEmailSentAt: entity.VerificationEmailSentAt,
				created: entity.TimeCreated,
				lastLogin: entity.LastLogin,
				verificationChannels: entity.VerificationChannels,
				emailVerified: entity.EmailVerified,
				phoneVerified: entity.PhoneVerified,
				phone: entity.Phone,
				loginLockedUntilUtc: entity.LoginLockedUntilUtc,
				twoFactorLockedUntilUtc: entity.TwoFactorLockedUntilUtc,
				phoneVerifyCodeExpiresUtc: entity.PhoneVerifyCodeExpiresUtc,
				discordUsername: entity.DiscordUsername,
				discordVerified: entity.DiscordVerified,
				discordVerifyCodeIssued: entity.DiscordVerifyCode != 0,
				discordDmSentAt: entity.DiscordDmSentAt
			);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistSrpCredentialsAsync(
			string accountName,
			string salt,
			string verifier,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			// Same bounds the account creation path enforces, so a password change cannot
			// store credentials that registration would have refused.
			if (string.IsNullOrWhiteSpace(salt) || salt.Length > 256)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid salt.");
			}
			if (string.IsNullOrWhiteSpace(verifier) || verifier.Length > 1024)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid verifier.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET salt = {{0}}, verifier = {{1}} WHERE name_lowercase = {{2}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { salt, verifier, accountName.ToLowerInvariant() }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
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
				/* verify_code is NOT NULL DEFAULT 0, so clearing it means writing 0, not NULL.
				 * Writing NULL violated the constraint and made every email change throw.
				 * The expiry is cleared alongside it, or an old deadline would outlive the
				 * code it belonged to. */
				var sql = $@"UPDATE {TableName} SET email = {{0}}, verified = FALSE, verify_code = 0, verify_code_expires_utc = NULL WHERE name_lowercase = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { (object?)email, accountName.ToLowerInvariant() }, cancellationToken)
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
		public async Task<DatabaseResult> PersistPendingTotpSecretAsync(
			string accountName,
			string encryptedTotpSecret,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}
			if (string.IsNullOrWhiteSpace(encryptedTotpSecret) || encryptedTotpSecret.Length > 256)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "TOTP secret is required and must not exceed 256 characters.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Only the pending column. The live secret — the one sign-in reads — is untouched until
				 * PromotePendingTotpSecretAsync, so a re-enrolment the player abandons changes nothing.
				 * A later staging simply replaces an earlier one. */
				var sql = $@"UPDATE {TableName} SET pending_totp_secret = {{0}} WHERE name_lowercase = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { encryptedTotpSecret, Authentication.NormalizeAccountLookup(accountName) }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<string?>> FetchPendingTotpSecretAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<string?>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			string normalized = Authentication.NormalizeAccountLookup(accountName);
			return await ExecuteReadAsync<string?>(async dbContext =>
			{
				var row = await dbContext.Accounts
					.AsNoTracking()
					.Where(a => a.NameLowercase == normalized)
					.Select(a => new { a.PendingTotpSecret })
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);
				if (row == null)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
				return row.PendingTotpSecret;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> PromotePendingTotpSecretAsync(
			string accountName,
			long totpWindow,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			DateTime now = DateTime.UtcNow;
			return await ExecuteWriteAsync(async dbContext =>
			{
				/* One statement: the new secret goes live, two-factor is on, the confirmation is
				 * recorded and the replay guard starts at the window that confirmed it — or none of
				 * it happens. The window is the confirming code's, so that very code cannot be played
				 * again at sign-in. */
				var sql = $@"UPDATE {TableName}
					SET totp_secret = pending_totp_secret,
						pending_totp_secret = NULL,
						totp_enabled = TRUE,
						totp_verified_at = {{2}},
						last_totp_window = {{1}}
					WHERE name_lowercase = {{0}} AND pending_totp_secret IS NOT NULL";
				int rows = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { Authentication.NormalizeAccountLookup(accountName), totpWindow, now }, cancellationToken)
					.ConfigureAwait(false);
				return rows == 1;
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
				// A staged re-enrolment goes with it: a pending secret must not outlive two-factor.
				var sql = $@"UPDATE {TableName} SET totp_secret = NULL, pending_totp_secret = NULL, totp_enabled = false, totp_verified_at = NULL, last_totp_window = 0 WHERE name_lowercase = {{0}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { accountName.ToLowerInvariant() }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>The one refusal for a verification code that did not verify anything.</summary>
		private const string InvalidVerificationCodeError = "Invalid verification code or account already verified.";

		/// <summary>Every verification code is six digits; zero is every code column's "none issued".</summary>
		private static bool IsVerificationCodeShape(int verifyCode) => verifyCode >= 100000 && verifyCode <= 999999;

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> PersistDiscordVerifyCodeAsync(
			string accountName,
			int verifyCode,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}
			if (!IsVerificationCodeShape(verifyCode))
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "A verification code is six digits.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Issued once. The WHERE is the whole of the once-only rule: a code already issued, a DM
				 * already sent or taken, or an account that verified some other way all match nothing —
				 * except the code this very call writes. That exception is what makes a retry safe: the
				 * update used to commit on its own, so a retry after its reply was lost (or after the
				 * notify below failed) found a code already issued and answered false for a code it had
				 * just issued (issue #267). A stored code equal to this one is this call's own, or, one
				 * time in a million, an earlier identical code, which re-issuing changes nothing about:
				 * the DM guards still hold.
				 *
				 * One statement, so the notification goes out exactly when the code lands: Postgres
				 * delivers a NOTIFY at commit, and the two used to be separate autocommit statements.
				 * pg_notify rather than NOTIFY, because NOTIFY takes no bind parameters. No payload: the
				 * bot re-reads what is owed, so a notification lost to a bot restart costs nothing but
				 * the wait for its next sweep. */
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				var sql = $@"WITH issued AS (
						UPDATE {TableName}
						SET discord_verify_code = {{1}}
						WHERE name_lowercase = {{0}}
							AND verified = false
							AND discord_username IS NOT NULL
							AND (discord_verify_code = 0 OR discord_verify_code = {{1}})
							AND discord_dm_sent_at IS NULL
							AND discord_dm_claimed_at IS NULL
						RETURNING 1
					),
					notified AS (
						SELECT pg_notify({{2}}, '') FROM issued
					)
					SELECT COUNT(*) FROM notified";
				long issuedRows = await ExecuteScalarLongAsync(
					dbContext, sql, new object[] { normalized, verifyCode, DiscordVerification.NotifyChannel }, cancellationToken)
					.ConfigureAwait(false);
				return issuedRows > 0;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AccountVerificationChannels>> PersistVerifiedByCodeAsync(
			string accountName,
			int verifyCode,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<AccountVerificationChannels>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}
			// Refused before the database is asked, so a stored 0 ("no code") can never be matched.
			if (!IsVerificationCodeShape(verifyCode))
			{
				return DatabaseResult<AccountVerificationChannels>.Failure(DatabaseErrorCodes.ValidationError, InvalidVerificationCodeError);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* One statement is the check and the write. The CTE locks the row and records which code
				 * matched against the values BEFORE the update, because RETURNING sees the codes already
				 * zeroed. Any one match verifies the account and clears every outstanding code.
				 *
				 * A Discord code only counts once its DM has been delivered (discord_dm_user_id is set), and
				 * not when the Discord user it reached has since been linked to a different account: the
				 * unique index on discord_user_id would otherwise fail the whole statement, email match
				 * and all. */
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				var sql = $@"WITH matched AS (
						SELECT acc.name_lowercase,
							(acc.verify_code = {{1}}
								AND (acc.verify_code_expires_utc IS NULL OR acc.verify_code_expires_utc > timezone('UTC', CURRENT_TIMESTAMP))) AS by_email,
							(acc.phone IS NOT NULL AND acc.phone_verify_code = {{1}}
								AND (acc.phone_verify_code_expires_utc IS NULL OR acc.phone_verify_code_expires_utc > timezone('UTC', CURRENT_TIMESTAMP))) AS by_phone,
							(acc.discord_verify_code = {{1}} AND acc.discord_dm_user_id IS NOT NULL
								AND NOT EXISTS (
									SELECT 1 FROM {TableName} other
									WHERE other.discord_user_id = acc.discord_dm_user_id
										AND other.name_lowercase <> acc.name_lowercase)) AS by_discord
						FROM {TableName} acc
						WHERE acc.name_lowercase = {{0}} AND acc.verified = false
						FOR UPDATE OF acc
					)
					UPDATE {TableName} AS a
					SET verified = true,
						email_verified = a.email_verified OR m.by_email,
						phone_verified = a.phone_verified OR m.by_phone,
						discord_verified = a.discord_verified OR m.by_discord,
						discord_user_id = CASE WHEN m.by_discord THEN a.discord_dm_user_id ELSE a.discord_user_id END,
						discord_linked_at = CASE WHEN m.by_discord THEN timezone('UTC', CURRENT_TIMESTAMP) ELSE a.discord_linked_at END,
						verify_failed_count = 0,
						verify_code = 0,
						verify_code_expires_utc = NULL,
						phone_verify_code = 0,
						phone_verify_code_expires_utc = NULL,
						discord_verify_code = 0
					FROM matched m
					WHERE a.name_lowercase = m.name_lowercase
						AND (m.by_email OR m.by_phone OR m.by_discord)
					RETURNING (CASE WHEN m.by_email THEN 1 ELSE 0 END)
						| (CASE WHEN m.by_phone THEN 2 ELSE 0 END)
						| (CASE WHEN m.by_discord THEN 4 ELSE 0 END)";
				int proven = await ExecuteReturningOrDefaultAsync(
					dbContext, sql, new object[] { normalized, verifyCode },
					reader => reader.GetInt32(0),
					cancellationToken).ConfigureAwait(false);
				if (proven == 0)
				{
					throw new DatabaseException(InvalidVerificationCodeError, errorCode: DatabaseErrorCodes.ValidationError);
				}
				return (AccountVerificationChannels)(byte)proven;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> RecordVerificationFailureAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				// Counted against nothing, and not an error: the caller answers every wrong code the same way.
				return DatabaseResult<int>.Success(0);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				var sql = $@"UPDATE {TableName}
					SET verify_failed_count = verify_failed_count + 1
					WHERE name_lowercase = {{0}} AND verified = false
					RETURNING verify_failed_count";
				return await ExecuteReturningOrDefaultAsync(
					dbContext, sql, new object[] { normalized },
					reader => reader.GetInt32(0),
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAccessLevelAsync(
			string accountName,
			byte accessLevel,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			/* Bounded against the enum rather than trusted. A byte reaches here from a chat
			 * command and from an HTTP body; 200 is not an access level, and a column that
			 * accepts it would grant more than Admin to anything comparing with >=. */
			// The byte, not an int: AccessLevel's underlying type is byte, and Enum.IsDefined
			// THROWS when handed a value of a different underlying type rather than returning
			// false. An int here would turn a bad level into an unhandled exception.
			if (!Enum.IsDefined(typeof(AccessLevel), accessLevel))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					$"'{accessLevel}' is not a valid access level.");
			}

			/* Banned is refused, however the caller spells it, and refused HERE rather than only in
			 * the callers so that a new caller inherits the refusal instead of having to remember it.
			 *
			 * The statement below clears banned_until, banned_by and ban_reason. That is right when a
			 * level change LIFTS a ban and destructive when it applies one. Setting the level to Banned
			 * through here produced a row indistinguishable from a permanent ban with no actor and no
			 * reason; run over an account already banned it ERASED who banned them and why, which is
			 * exactly the overwrite BanAsync's callers refuse to allow; and it revoked no auth token,
			 * ended no Control Panel session and wrote no kick request, so the "banned" account kept
			 * playing on the session it already held until it chose to reconnect.
			 *
			 * BanAsync is the only way to Banned. It writes the level and the ban columns in one
			 * statement inside a transaction with the token revocation, the panel-session revocation
			 * and the kick, so a ban cannot exist half-applied or unattributed. UnbanAsync is the only
			 * way back to Player for a banned account, for the same reason in reverse. */
			if ((AccessLevel)accessLevel == AccessLevel.Banned)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"An account is banned with BanAsync, which records who banned it and why, revokes its " +
					"sessions and writes a kick. A level change to Banned would record none of that.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				/* The ban columns are cleared in the same statement as the level, and the guard above
				 * has already established that the new level is not Banned — so reaching here always
				 * means the ban, if there was one, is being lifted, and leaving a banned_until behind
				 * would leave an expiry pointing at a ban that no longer exists for LiftExpiredBanAsync
				 * to trip over. Promoting a banned account straight to GameMaster or Admin is an unban
				 * and a grant in one deliberate action, which is why it clears them too. */
				var sql = $@"UPDATE {TableName}
					SET access_level = {{1}},
						banned_until = NULL,
						banned_by = NULL,
						ban_reason = NULL
					WHERE name_lowercase = {{0}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { normalized, (short)accessLevel }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		public async Task<DatabaseResult> PersistAutoVerifiedAsync(
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
				// No verify_code predicate: the caller is the server itself, not a client
				// redeeming a code. Any pending code is cleared so a stale one cannot be
				// replayed later against an already-verified account.
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				// Every channel at once: the server verifies nothing, so nothing is waiting to be proven.
				var sql = $@"UPDATE {TableName}
					SET verified = true,
						email_verified = true,
						phone_verified = CASE WHEN phone IS NOT NULL THEN true ELSE phone_verified END,
						verify_code = 0,
						verify_code_expires_utc = NULL,
						phone_verify_code = 0,
						phone_verify_code_expires_utc = NULL,
						discord_verify_code = 0,
						verify_failed_count = 0
					WHERE name_lowercase = {{0}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { normalized }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
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
		public Task<DatabaseResult<bool>> ExpireVerifyCodeAsync(string accountName, int verifyCode, CancellationToken cancellationToken = default)
			=> ExpireCodeIfCurrentAsync(accountName, verifyCode, "verify_code", "verify_code_expires_utc", cancellationToken);

		/// <inheritdoc/>
		public Task<DatabaseResult<bool>> ExpirePhoneVerifyCodeAsync(string accountName, int verifyCode, CancellationToken cancellationToken = default)
			=> ExpireCodeIfCurrentAsync(accountName, verifyCode, "phone_verify_code", "phone_verify_code_expires_utc", cancellationToken);

		/// <summary>
		/// Expires one verification code column pair, pinned to the code the caller stored.
		/// </summary>
		/// <remarks>
		/// The column names are compile-time constants chosen by the two public methods above, never
		/// caller input; the code and the account are parameters.
		/// </remarks>
		private async Task<DatabaseResult<bool>> ExpireCodeIfCurrentAsync(string accountName, int verifyCode, string codeColumn, string expiresColumn, CancellationToken cancellationToken)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<bool>.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				var sql = $@"UPDATE {TableName}
					SET {expiresColumn} = timezone('UTC', CURRENT_TIMESTAMP)
					WHERE name_lowercase = {{0}} AND {codeColumn} = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { normalized, verifyCode }, cancellationToken)
					.ConfigureAwait(false);
				return rowsAffected > 0;
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
				var sql = $"UPDATE {TableName} SET verification_email_sent_at = timezone('UTC', CURRENT_TIMESTAMP) WHERE name_lowercase = {{0}}";
				var affected = await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { accountName.ToLowerInvariant() }, cancellationToken).ConfigureAwait(false);
				if (affected == 0)
					throw new DatabaseEntityNotFoundException("Account", accountName);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>The most rows one operator search will read, whatever it asks for.</summary>
		private const int MaxAdminPageSize = 100;

		/// <inheritdoc/>
		public async Task<DatabaseResult<AccountAdminPage>> SearchAdminAsync(
			AccountAdminQuery query,
			CancellationToken cancellationToken = default)
		{
			// A null filter is an operator opening the list with nothing typed, not an error.
			query ??= new AccountAdminQuery();

			// A caller that asks for page 0 or ten thousand rows is a bug or a probe; neither
			// gets to choose how much of the table this reads.
			int page = query.Page < 1 ? 1 : query.Page;
			int pageSize = query.PageSize < 1 ? 25 : query.PageSize;
			if (pageSize > MaxAdminPageSize)
			{
				pageSize = MaxAdminPageSize;
			}

			// Normalized exactly as every other lookup in this service normalizes, so a search
			// for "Bob" finds the row the registration path stored as "bob". Returns the empty
			// string for null, which is the "no text filter" case.
			string prefix = Authentication.NormalizeAccountLookup(query.Query ?? string.Empty);
			byte? accessLevelFilter = query.AccessLevel;
			bool includeBanned = query.IncludeBanned;
			byte bannedLevel = (byte)AccessLevel.Banned;

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<AccountEntity> q = dbContext.Accounts.AsNoTracking();

				if (!includeBanned)
				{
					q = q.Where(a => a.AccessLevel != bannedLevel);
				}
				if (accessLevelFilter.HasValue)
				{
					byte level = accessLevelFilter.Value;
					q = q.Where(a => a.AccessLevel == level);
				}
				if (prefix.Length > 0)
				{
					/* StartsWith, never Contains, on both arms. name_lowercase carries the
					 * UNIQUE index and email carries a partial unique one, and an anchored
					 * pattern (LIKE 'x%') is the only shape a btree can ever serve; a leading
					 * wildcard forecloses it unconditionally and leaves a sequential scan of
					 * every account row as the only plan. A search box reachable by anyone who
					 * reaches the panel does not get to guarantee that on every keystroke.
					 *
					 * Honest caveat: it is not an index seek today either. The database collates
					 * en_US.UTF-8, and PostgreSQL will only use a btree for LIKE 'x%' when the
					 * index is built with text_pattern_ops (or the C collation), which these are
					 * not. The anchored form is what keeps that a one-migration fix instead of a
					 * rewrite — and it still bounds the match, which Contains does not.
					 *
					 * StartsWith rather than a hand-written EF.Functions.Like: the provider
					 * emits an extra left(col, length(p)) = p alongside the LIKE, which is what
					 * makes a prefix containing _ or % — both legal in a username — match
					 * literally instead of as a wildcard. */
					q = q.Where(a => a.NameLowercase.StartsWith(prefix) ||
									 (a.Email != null && a.Email.ToLower().StartsWith(prefix)));
				}

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				/* Grouped once and joined once, rather than counted per row. A page of 25
				 * accounts must cost one statement, not 26 — the N+1 shape is invisible in C#
				 * and shows up only as a page that gets slower the more of it you ask for.
				 * The join is on the exact name because characters.account is a foreign key to
				 * accounts.name, so the two are the same string by construction; no case
				 * folding is needed and any would defeat the index. */
				var characterCounts = dbContext.Characters
					.AsNoTracking()
					.Where(c => !c.Deleted)
					.GroupBy(c => c.Account)
					.Select(g => new { Account = g.Key, Count = g.Count() });

				/* Projected in SQL rather than mapped from a materialized AccountEntity. That
				 * is the strongest available form of the guarantee AccountAdminData's remarks
				 * make: the salt, the verifier and the TOTP secret are never named in the
				 * SELECT, so they never cross the wire, never enter the change tracker and
				 * cannot be leaked by a later edit to a shared mapper. The cost is that the
				 * column list is written out here and again in FetchAdminAsync; a shared mapper
				 * would have to take a whole entity, which is precisely what is being avoided. */
				var rows = await q
					.OrderBy(a => a.NameLowercase)
					.Skip((page - 1) * pageSize)
					.Take(pageSize)
					.Select(a => new AccountAdminData
					{
						Name = a.Name,
						Email = a.Email,
						AccessLevel = a.AccessLevel,
						Age = a.Age,
						Verified = a.Verified,
						TotpEnabled = a.TotpEnabled,
						TotpVerifiedAt = a.TotpVerifiedAt,
						Created = a.TimeCreated,
						LastLogin = a.LastLogin,
						BannedUntil = a.BannedUntil,
						BannedBy = a.BannedBy,
						BanReason = a.BanReason,
						Muted = a.Muted,
						MutedUntil = a.MutedUntil,
						MutedBy = a.MutedBy,
						MuteReason = a.MuteReason,
						Phone = a.Phone,
						PhoneVerified = a.PhoneVerified,
						EmailVerified = a.EmailVerified,
						VerificationChannels = a.VerificationChannels,
						RealName = a.RealName,
						Country = a.Country,
						Address = a.Address,
						ReferralAccount = a.ReferralAccount,
						LoginLockedUntilUtc = a.LoginLockedUntilUtc,
						TwoFactorLockedUntilUtc = a.TwoFactorLockedUntilUtc,
						// FirstOrDefault, not Single: an account with no characters has no group
						// at all, and the default zero is the right answer for it.
						CharacterCount = characterCounts
							.Where(cc => cc.Account == a.Name)
							.Select(cc => cc.Count)
							.FirstOrDefault(),
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return new AccountAdminPage
				{
					Items = rows,
					Page = page,
					PageSize = pageSize,
					TotalCount = total,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AccountAdminData>> FetchAdminAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<AccountAdminData>.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				/* Deliberately not routed through FetchForLoginAsync. That method refuses a
				 * banned account and reports "no such account" and "banned" with the same
				 * message, because a login path that distinguished them would be an
				 * enumeration oracle for anyone who can reach the login endpoint. Both
				 * behaviours are wrong here: the operator opening an account page is usually
				 * looking at the ban, and one who cannot tell a missing account from a banned
				 * one cannot answer the ticket in front of them. The enumeration risk is
				 * carried by the caller's access-level check instead, which is where it
				 * belongs — this is not an anonymous surface. */
				string normalized = Authentication.NormalizeAccountLookup(accountName);

				var data = await dbContext.Accounts
					.AsNoTracking()
					.Where(a => a.NameLowercase == normalized)
					.Select(a => new AccountAdminData
					{
						Name = a.Name,
						Email = a.Email,
						AccessLevel = a.AccessLevel,
						Age = a.Age,
						Verified = a.Verified,
						TotpEnabled = a.TotpEnabled,
						TotpVerifiedAt = a.TotpVerifiedAt,
						Created = a.TimeCreated,
						LastLogin = a.LastLogin,
						BannedUntil = a.BannedUntil,
						BannedBy = a.BannedBy,
						BanReason = a.BanReason,
						Muted = a.Muted,
						MutedUntil = a.MutedUntil,
						MutedBy = a.MutedBy,
						MuteReason = a.MuteReason,
						Phone = a.Phone,
						PhoneVerified = a.PhoneVerified,
						EmailVerified = a.EmailVerified,
						VerificationChannels = a.VerificationChannels,
						RealName = a.RealName,
						Country = a.Country,
						Address = a.Address,
						ReferralAccount = a.ReferralAccount,
						LoginLockedUntilUtc = a.LoginLockedUntilUtc,
						TwoFactorLockedUntilUtc = a.TwoFactorLockedUntilUtc,
						VerificationEmailSentAt = a.VerificationEmailSentAt,
						VerifyFailedCount = a.VerifyFailedCount,
						DiscordUsername = a.DiscordUsername,
						DiscordVerified = a.DiscordVerified,
						DiscordUserId = a.DiscordUserId,
						DiscordDmSentAt = a.DiscordDmSentAt,
						DiscordDmClaimedAt = a.DiscordDmClaimedAt,
						DiscordDmLastError = a.DiscordDmLastError,
						// One row, so a correlated count is one indexed lookup and the grouped
						// form the search needs would buy nothing here.
						CharacterCount = dbContext.Characters.Count(c => c.Account == a.Name && !c.Deleted),
					})
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				if (data == null)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
				return data;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> BanAsync(
			string accountName,
			DateTime? bannedUntilUtc,
			string? bannedBy,
			string? reason,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			/* ExecuteTransactionAsync, not four ExecuteWriteAsync calls. Each of those takes its
			 * own context, its own connection and its own implicit transaction, so four of them
			 * are four commits with three windows in between where the process can die, the
			 * connection can drop, or the cancellation token can fire — and every one of those
			 * windows leaves a ban half-applied.
			 *
			 * Half-applied means the account is still playing. The access level is the login
			 * gate and nothing more: lowering it alone stops the next sign-in and does not touch
			 * the session already connected. Revoking the tokens alone lets the player sign
			 * straight back in. Queuing the kick alone disconnects someone who reconnects a
			 * second later. Only all four together end the session and keep it ended — and only
			 * a transaction makes "all four" something the operator's success message can mean.
			 *
			 * This is also why the service-level RevokeAllForAccountAsync methods are not called
			 * here: each opens its own connection and commits on its own, which is exactly the
			 * property being eliminated. The equivalent statements are issued on this
			 * transaction's context instead. */
			return await ExecuteTransactionAsync(async dbContext =>
			{
				string normalized = Authentication.NormalizeAccountLookup(accountName);

				/* Read the row's own spelling of the name first. auth_tokens.account_name and
				 * web_sessions.account_name are both foreign keys to accounts.name, so they hold
				 * that exact string and an exact predicate on it is both correct and indexed;
				 * matching on LOWER() would be neither. It doubles as the existence check. */
				// FirstOrDefaultAsync over a non-nullable string column is typed string, but it
				// still yields null when there is no row; the declaration says so.
				string? storedName = await dbContext.Accounts
					.AsNoTracking()
					.Where(a => a.NameLowercase == normalized)
					.Select(a => a.Name)
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				if (storedName == null)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}

				// 1. The login gate. access_level is a byte in the model and smallint in
				// PostgreSQL, so the parameter is a short — the same cast PersistAccessLevelAsync
				// makes.
				// The ban columns are written in the same statement as the level, so a ban can never
				// exist without its expiry: a permanent ban explicitly clears a stale banned_until
				// left behind by an earlier temporary one, which would otherwise lift it.
				var accountSql = $@"UPDATE {TableName}
					SET access_level = {{1}},
						banned_until = {{2}},
						banned_by = {{3}},
						ban_reason = {{4}}
					WHERE name_lowercase = {{0}}";
				var accountRows = await dbContext.Database
					.ExecuteSqlRawAsync(accountSql, new object[]
					{
						normalized,
						(short)(byte)AccessLevel.Banned,
						(object?)bannedUntilUtc,
						(object?)Clip(bannedBy, ModerationActorMaxLength),
						(object?)Clip(reason, ModerationReasonMaxLength),
					}, cancellationToken)
					.ConfigureAwait(false);
				if (accountRows == 0)
				{
					// The row existed a statement ago, so this is a concurrent delete. Throwing
					// rolls back everything written above it.
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}

				/* GetTableName for the other three tables, not a bare "auth_tokens". Raw SQL is
				 * resolved against the connection's search_path, while the model is bound to a
				 * configured schema; on a deployment where those differ, an unqualified
				 * statement silently updates nothing, and a revoke that silently does nothing is
				 * the whole failure this method exists to prevent. TableName above is already
				 * qualified for the same reason. */

				// 2. Every auth token. No rows-affected check: an account with no live tokens is
				// the normal case, not a failure.
				var authTokenSql = $@"UPDATE {dbContext.GetTableName<AuthTokenEntity>()}
					SET revoked = TRUE
					WHERE account_name = {{0}} AND revoked = FALSE";
				await dbContext.Database
					.ExecuteSqlRawAsync(authTokenSql, new object[] { storedName }, cancellationToken)
					.ConfigureAwait(false);

				// 3. Every Control Panel session, for the same reason and with the same
				// tolerance for zero rows.
				var webSessionSql = $@"UPDATE {dbContext.GetTableName<WebSessionEntity>()}
					SET revoked = TRUE
					WHERE account_name = {{0}} AND revoked = FALSE";
				await dbContext.Database
					.ExecuteSqlRawAsync(webSessionSql, new object[] { storedName }, cancellationToken)
					.ConfigureAwait(false);

				// 4. The kick the game servers poll for. Upserted rather than inserted:
				// account_name is unique, a ban re-issued over an unconsumed request must not
				// fail on the constraint, and refreshing the timestamp keeps the row inside the
				// TTL that KickRequestService treats as still pending.
				var kickSql = $@"INSERT INTO {dbContext.GetTableName<KickRequestEntity>()}
					(account_name, time_created)
					VALUES ({{0}}, timezone('UTC', CURRENT_TIMESTAMP))
					ON CONFLICT (account_name)
					DO UPDATE SET time_created = timezone('UTC', CURRENT_TIMESTAMP)";
				await dbContext.Database
					.ExecuteSqlRawAsync(kickSql, new object[] { storedName }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UnbanAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			/* One statement, and no transaction, because an unban is deliberately not the
			 * inverse of a ban. The tokens and panel sessions the ban revoked stay revoked: they
			 * may be the reason for the ban, they have had the whole ban to be shared or stolen,
			 * and reviving them would be this layer silently re-issuing credentials it is not
			 * the job of this layer to mint. The player signs in again and the login path issues
			 * fresh ones under whatever checks it applies today. The kick request is likewise
			 * left alone; it is consumed or expires on its own, and by the time an unban happens
			 * there is no session left for it to end.
			 *
			 * Player specifically, not the level the account held before. Nothing here records
			 * that, and a wrong guess in the upward direction hands back operator access. An
			 * operator who needs the old level back sets it explicitly through
			 * PersistAccessLevelAsync, where the change is deliberate and audited as its own
			 * action. */
			return await ExecuteWriteAsync(async dbContext =>
			{
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				var sql = $@"UPDATE {TableName}
					SET access_level = {{1}},
						banned_until = NULL,
						banned_by = NULL,
						ban_reason = NULL
					WHERE name_lowercase = {{0}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { normalized, (short)(byte)AccessLevel.Player }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>Longest operator account name the moderation columns store.</summary>
		private const int ModerationActorMaxLength = 50;

		/// <summary>Longest reason the moderation columns store.</summary>
		private const int ModerationReasonMaxLength = 256;

		/// <summary>
		/// Restores <c>Player</c> on a banned account whose temporary ban has lapsed.
		/// </summary>
		/// <returns>True when this call lifted the ban; false when the row no longer qualified.</returns>
		private async Task<DatabaseResult<bool>> LiftExpiredBanAsync(string accountNameLowercase, CancellationToken cancellationToken)
		{
			return await ExecuteWriteAsync(async dbContext =>
			{
				// Database time, not this process's clock, decides the lapse: the statement is the
				// one place every login server agrees on what "now" is.
				var sql = $@"UPDATE {TableName}
					SET access_level = {{1}},
						banned_until = NULL,
						banned_by = NULL,
						ban_reason = NULL
					WHERE name_lowercase = {{0}}
						AND access_level = {{2}}
						AND banned_until IS NOT NULL
						AND banned_until <= timezone('UTC', CURRENT_TIMESTAMP)";
				int rows = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[]
					{
						accountNameLowercase,
						(short)(byte)AccessLevel.Player,
						(short)(byte)AccessLevel.Banned,
					}, cancellationToken)
					.ConfigureAwait(false);
				return rows > 0;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistMuteAsync(
			string accountName,
			DateTime? mutedUntilUtc,
			string? mutedBy,
			string? reason,
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
					SET muted = TRUE,
						muted_until = {{1}},
						muted_by = {{2}},
						mute_reason = {{3}}
					WHERE name_lowercase = {{0}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[]
					{
						normalized,
						(object?)mutedUntilUtc,
						(object?)Clip(mutedBy, ModerationActorMaxLength),
						(object?)Clip(reason, ModerationReasonMaxLength),
					}, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> ClearMuteAsync(
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
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				var sql = $@"UPDATE {TableName}
					SET muted = FALSE,
						muted_until = NULL,
						muted_by = NULL,
						mute_reason = NULL
					WHERE name_lowercase = {{0}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { normalized }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistProfileAsync(
			string accountName,
			AccountProfileData profile,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}
			if (profile == null)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "A profile is required.");
			}
			if (!AccountProfileRules.TryValidate(profile, out AccountProfileData clean, out string error))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, error);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* A changed number is an unproven number: its verification and any outstanding code go
				 * with the old one. `verified` is deliberately left alone — changing a phone number must
				 * not lock a player out of signing in; what a changed number costs is the channel's
				 * own proof, and the caller decides whether to send a new code. */
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				var sql = $@"UPDATE {TableName}
					SET phone_verified = CASE WHEN phone IS DISTINCT FROM {{1}} THEN false ELSE phone_verified END,
						phone_verify_code = CASE WHEN phone IS DISTINCT FROM {{1}} THEN 0 ELSE phone_verify_code END,
						phone_verify_code_expires_utc = CASE WHEN phone IS DISTINCT FROM {{1}} THEN NULL ELSE phone_verify_code_expires_utc END,
						phone = {{1}},
						real_name = {{2}},
						country = {{3}},
						address = {{4}},
						referral_account = {{5}},
						discord_username = {{7}},
						verification_channels = {{6}}
					WHERE name_lowercase = {{0}}";
				/* A null is passed as null, never as DBNull.Value. EF Core 5's raw-SQL parameters look up
				 * a type mapping for the CLR type of each value, and there is none for DBNull: the call
				 * throws before anything is sent. A plain null becomes a database NULL on its own. That
				 * pattern made every profile with a blank field unwritable, and permanent bans and
				 * mute clears with it, until 2026-09-14. */
				int rows = await dbContext.Database.ExecuteSqlRawAsync(sql, new object[]
				{
					normalized,
					(object?)clean.Phone,
					(object?)clean.RealName,
					(object?)clean.Country,
					(object?)clean.Address,
					(object?)clean.ReferralAccount,
					(short)(byte)clean.VerificationChannels,
					(object?)clean.DiscordUsername,
				}, cancellationToken).ConfigureAwait(false);
				if (rows == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistChannelsVerifiedAsync(
			string accountName,
			AccountVerificationChannels channels,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Verifies without a code, for an account no enabled channel can reach (see
				 * AccountVerificationRules.IsWaived). The SMS channel is only marked when there is a number,
				 * so a waiver cannot make an account look as if it proved a phone it never gave. */
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				var sql = $@"UPDATE {TableName}
					SET verified = true,
						verify_failed_count = 0,
						email_verified = (email_verified OR ({{1}} & 1) <> 0),
						phone_verified = (phone_verified OR (({{1}} & 2) <> 0 AND phone IS NOT NULL))
					WHERE name_lowercase = {{0}}";
				int rows = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { normalized, (int)channels }, cancellationToken)
					.ConfigureAwait(false);
				if (rows == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistPhoneVerifyCodeAsync(
			string accountName,
			int verifyCode,
			DateTime expiresUtc,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}
			if (verifyCode == 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "A verification code cannot be zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				var sql = $@"UPDATE {TableName}
					SET phone_verify_code = {{1}}, phone_verify_code_expires_utc = {{2}}
					WHERE name_lowercase = {{0}} AND phone IS NOT NULL";
				int rows = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { normalized, verifyCode, expiresUtc }, cancellationToken)
					.ConfigureAwait(false);
				if (rows == 0)
				{
					throw new DatabaseException("That account has no phone number to verify.", errorCode: DatabaseErrorCodes.ValidationError);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<DateTime?>> RecordAuthFailureAsync(
			string accountName,
			AuthFailureKind kind,
			int threshold,
			TimeSpan window,
			TimeSpan lockout,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				// Not a failure the caller should surface: an unknown or malformed name is locked out of
				// nothing, and answering differently would say which names exist.
				return DatabaseResult<DateTime?>.Success(null);
			}
			if (threshold < 1 || window <= TimeSpan.Zero || lockout <= TimeSpan.Zero)
			{
				return DatabaseResult<DateTime?>.Failure(DatabaseErrorCodes.ValidationError, "A lockout needs a threshold, a window and a length.");
			}

			(string count, string since, string until) = LockoutColumns(kind);
			DateTime now = DateTime.UtcNow;

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* One statement counts, windows and locks, so two login servers failing the same account
				 * at once cannot both read a count of four and both write five. A failure after the
				 * window has passed starts a new window at one. Reaching the threshold locks, and resets
				 * the count, so the next window after the lockout starts from nothing rather than
				 * relocking on the first wrong guess. SET expressions read the row as it was before the
				 * statement, which is what lets `next` be written out three times and mean one value. */
				string fresh = $"({since} IS NULL OR {since} < {{2}})";
				string next = $"(CASE WHEN {fresh} THEN 1 ELSE {count} + 1 END)";
				var sql = $@"UPDATE {TableName}
					SET {count} = CASE WHEN {next} >= {{3}} THEN 0 ELSE {next} END,
						{since} = CASE WHEN {next} >= {{3}} THEN NULL WHEN {fresh} THEN {{1}} ELSE {since} END,
						{until} = CASE WHEN {next} >= {{3}} THEN {{4}} ELSE {until} END
					WHERE name_lowercase = {{0}}
					RETURNING CASE WHEN {until} > {{1}} THEN {until} END";

				/* Only a lockout still in force. The column keeps a lockout's end after it passes,
				 * so returning it as stored answered every later failure with a lock that had
				 * already expired: contrary to "null when not locked", and logged by the login
				 * server as a live lock on every wrong password (issue #267). */
				return await ExecuteReturningOrDefaultAsync(
					dbContext,
					sql,
					new object[] { Authentication.NormalizeAccountLookup(accountName), now, now - window, threshold, now + lockout },
					reader => reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0),
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> ClearAuthFailuresAsync(
			string accountName,
			AuthFailureKind kind,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Success();
			}

			(string count, string since, string until) = LockoutColumns(kind);
			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET {count} = 0, {since} = NULL, {until} = NULL
					WHERE name_lowercase = {{0}} AND ({count} <> 0 OR {until} IS NOT NULL)";
				await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { Authentication.NormalizeAccountLookup(accountName) }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> ClearAuthLockoutAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				var sql = $@"UPDATE {TableName}
					SET failed_login_count = 0, failed_login_since_utc = NULL, login_locked_until_utc = NULL,
						failed_two_factor_count = 0, failed_two_factor_since_utc = NULL, two_factor_locked_until_utc = NULL
					WHERE name_lowercase = {{0}}
						AND (login_locked_until_utc > timezone('UTC', CURRENT_TIMESTAMP)
							OR two_factor_locked_until_utc > timezone('UTC', CURRENT_TIMESTAMP))";
				int rows = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { normalized }, cancellationToken)
					.ConfigureAwait(false);
				return rows > 0;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AuthLockoutState>> FetchAuthLockoutAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<AuthLockoutState>.Success(default);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				string normalized = Authentication.NormalizeAccountLookup(accountName);
				var row = await dbContext.Accounts
					.AsNoTracking()
					.Where(a => a.NameLowercase == normalized)
					.Select(a => new { a.LoginLockedUntilUtc, a.TwoFactorLockedUntilUtc })
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);
				// An unknown account is locked out of nothing; see RecordAuthFailureAsync.
				return row == null
					? default
					: new AuthLockoutState(row.LoginLockedUntilUtc, row.TwoFactorLockedUntilUtc);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>The counter, window and lock columns for one kind of failure.</summary>
		/// <remarks>Column names from a closed switch, never from input, so interpolating them into SQL is safe.</remarks>
		private static (string Count, string Since, string Until) LockoutColumns(AuthFailureKind kind) => kind switch
		{
			AuthFailureKind.TwoFactor => ("failed_two_factor_count", "failed_two_factor_since_utc", "two_factor_locked_until_utc"),
			_ => ("failed_login_count", "failed_login_since_utc", "login_locked_until_utc"),
		};

		/// <summary>Cuts operator-supplied text to a column's length rather than failing the write on it.</summary>
		private static string? Clip(string? text, int maxLength)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}
			text = text.Trim();
			return text.Length <= maxLength ? text : text.Substring(0, maxLength);
		}
	}
}