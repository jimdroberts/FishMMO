using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

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

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"INSERT INTO {TableName} (token_hash, account_name, login_server_id, expires_utc, revoked)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}})
					RETURNING id, token_hash, account_name, login_server_id, time_created, expires_utc, revoked";

				return await dbContext.AuthTokens
					.FromSqlRaw(sql, tokenHash, accountName, loginServerId, expiresUtc, false)
					.AsNoTracking()
					.FirstAsync(cancellationToken)
					.ConfigureAwait(false);
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
		public async Task<DatabaseResult> RevokeAllForAccountAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Account name must not be empty.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET revoked = TRUE WHERE account_name = {{0}} AND revoked = FALSE";
				await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { accountName },
					cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

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