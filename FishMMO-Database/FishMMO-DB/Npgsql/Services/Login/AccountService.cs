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
						AND (verify_code_expires_utc IS NULL OR verify_code_expires_utc > timezone('UTC', CURRENT_TIMESTAMP))";
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

			return await ExecuteWriteAsync(async dbContext =>
			{
				var normalized = Authentication.NormalizeAccountLookup(accountName);
				var sql = $@"UPDATE {TableName}
					SET access_level = {{1}}
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
				var sql = $@"UPDATE {TableName}
					SET verified = true, verify_code = 0, verify_code_expires_utc = NULL
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
				var accountSql = $@"UPDATE {TableName}
					SET access_level = {{1}}
					WHERE name_lowercase = {{0}}";
				var accountRows = await dbContext.Database
					.ExecuteSqlRawAsync(accountSql, new object[] { normalized, (short)(byte)AccessLevel.Banned }, cancellationToken)
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
					SET access_level = {{1}}
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
	}
}