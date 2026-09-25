using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service for managing authentication tokens.
	/// The LoginServer issues tokens; WorldServers and SceneServers validate and check revocation.
	/// Uses compiled queries for the hot validation path and raw SQL for writes.
	/// </summary>
	public sealed class AuthTokenService : BaseService<AuthTokenEntity>, IAuthTokenService
	{
#pragma warning disable CS8619
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<AuthTokenEntity?>> getByHashNoTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string tokenHash, CancellationToken ct) =>
				context.AuthTokens
					.AsNoTracking()
					.FirstOrDefault(t => t.TokenHash == tokenHash));
#pragma warning restore CS8619

		public AuthTokenService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AuthTokenData>> IssueAsync(
			string tokenHash,
			string accountName,
			long loginServerId,
			DateTime expiresUtc,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(tokenHash))
			{
				return DatabaseResult<AuthTokenData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Token hash must not be empty.");
			}

			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<AuthTokenData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Account name must not be empty.");
			}

			if (loginServerId <= 0)
			{
				return DatabaseResult<AuthTokenData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"LoginServer ID must be greater than 0.");
			}

			if (expiresUtc <= DateTime.UtcNow)
			{
				return DatabaseResult<AuthTokenData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Expiry must be in the future.");
			}

			// See CanonicalAccountName.
			string canonicalName = CanonicalAccountName(accountName);

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* The token hash is a digest of a random token, so a conflict on it can only be this
				 * call's own row, landed by an attempt whose reply was lost. The retry used to fail
				 * on the unique index and the sign-in was refused with ServerBusy for a token that
				 * was in fact recorded (issue #267); it now answers with that row. The fallback is
				 * matched on the account and issuer too, so it can only ever return this call's row. */
				var sql = $@"WITH ins AS (
						INSERT INTO {TableName} (token_hash, account_name, login_server_id, expires_utc, revoked)
						VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}})
						ON CONFLICT (token_hash) DO NOTHING
						RETURNING id, token_hash, account_name, login_server_id, time_created, expires_utc, revoked
					)
					SELECT id, token_hash, account_name, login_server_id, time_created, expires_utc, revoked FROM ins
					UNION ALL
					SELECT id, token_hash, account_name, login_server_id, time_created, expires_utc, revoked FROM {TableName}
					WHERE token_hash = {{0}} AND account_name = {{1}} AND login_server_id = {{2}}
						AND NOT EXISTS (SELECT 1 FROM ins)";

				return await ExecuteReturningAsync(
					dbContext,
					sql,
					new object[] { tokenHash, canonicalName, loginServerId, expiresUtc, false },
					reader => new AuthTokenEntity
					{
						ID = reader.GetInt64(0),
						TokenHash = reader.GetString(1),
						AccountName = reader.GetString(2),
						LoginServerId = reader.GetInt64(3),
						TimeCreated = reader.GetDateTime(4),
						ExpiresUtc = reader.GetDateTime(5),
						Revoked = reader.GetBoolean(6),
					},
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<AuthTokenData>.Success(MapEntityToDto(result.Data))
				: DatabaseResult<AuthTokenData>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AuthTokenData>> FetchByHashAsync(
			string tokenHash,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(tokenHash))
			{
				return DatabaseResult<AuthTokenData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Token hash must not be empty.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entity = await getByHashNoTrackingQuery(dbContext, tokenHash, cancellationToken).ConfigureAwait(false);
				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("AuthToken", tokenHash);
				}

				return MapEntityToDto(entity);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> RevokeByHashAsync(
			string tokenHash,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(tokenHash))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Token hash must not be empty.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET revoked = TRUE WHERE token_hash = {{0}} AND revoked = FALSE";
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { tokenHash },
					cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("AuthToken", tokenHash,
						"Token not found or already revoked.");
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> RevokeAllForAccountAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<int>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Account name must not be empty.");
			}

			// See CanonicalAccountName: an exact match on the name as given revoked nothing when the
			// caller's spelling differed from the row's, and reported success.
			string canonicalName = CanonicalAccountName(accountName);

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET revoked = TRUE WHERE account_name = {{0}} AND revoked = FALSE";
				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { canonicalName },
					cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// The account row's own spelling of <paramref name="accountName"/>.
		/// </summary>
		/// <remarks>
		/// <c>auth_tokens.account_name</c> references <c>accounts.name</c>, which is stored lowercase,
		/// while a sign-in proves the spelling the player registered with (SRP needs the exact
		/// identifier) and an operator may type any case. Accounts are looked up case-insensitively
		/// through the same normalisation (see <c>AccountService</c>), and a stored name is the
		/// lowercase form of a name that rule allows, so this is exactly the row's name.
		/// </remarks>
		internal static string CanonicalAccountName(string accountName) =>
			Authentication.NormalizeAccountLookup(accountName);

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> CleanupExpiredAsync(
			DateTime cutoffUtc,
			CancellationToken cancellationToken = default)
		{
			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE expires_utc < {{0}}";
				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { cutoffUtc },
					cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static AuthTokenData MapEntityToDto(AuthTokenEntity entity)
		{
			return new AuthTokenData(
				id: entity.ID,
				tokenHash: entity.TokenHash,
				accountName: entity.AccountName,
				loginServerId: entity.LoginServerId,
				timeCreated: entity.TimeCreated,
				expiresUtc: entity.ExpiresUtc,
				revoked: entity.Revoked
			);
		}
	}
}