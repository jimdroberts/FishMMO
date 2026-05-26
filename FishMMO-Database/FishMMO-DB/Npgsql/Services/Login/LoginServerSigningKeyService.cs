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
	/// Service for managing per-LoginServer HMAC signing keys.
	/// Uses EF Core compiled queries for the hot validation path and raw SQL for inserts.
	/// </summary>
	public sealed class LoginServerSigningKeyService : BaseService<LoginServerSigningKeyEntity>, ILoginServerSigningKeyService
	{
#pragma warning disable CS8619
		/// <summary>
		/// Fetches the current active signing key for the given LoginServer. Old keys (IsActive=false)
		/// are kept in the table to verify tokens issued before the latest rotation but are NOT used
		/// to sign new tokens.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<LoginServerSigningKeyEntity?>> getByLoginServerIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long loginServerId, CancellationToken ct) =>
				context.LoginServerSigningKeys
					.AsNoTracking()
					.Where(k => k.LoginServerId == loginServerId && k.IsActive)
					.OrderByDescending(k => k.TimeCreated)
					.ThenByDescending(k => k.ID)
					.FirstOrDefault());

		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<LoginServerSigningKeyEntity?>> getByIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long signingKeyId, CancellationToken ct) =>
				context.LoginServerSigningKeys
					.AsNoTracking()
					.FirstOrDefault(k => k.ID == signingKeyId));
#pragma warning restore CS8619

		public LoginServerSigningKeyService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<LoginServerSigningKeyData>> UpsertAsync(
			long loginServerId,
			byte[] hmacKey,
			CancellationToken cancellationToken = default)
		{
			if (loginServerId <= 0)
			{
				return DatabaseResult<LoginServerSigningKeyData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"LoginServer ID must be greater than 0.");
			}

			if (hmacKey == null || hmacKey.Length == 0)
			{
				return DatabaseResult<LoginServerSigningKeyData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"HMAC key must not be empty.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				// Mark all previously active keys for this LoginServer as rotated.
				// They remain in the table for the verification overlap window so in-flight tokens
				// signed with the old key can still be validated until DeleteAsync prunes them.
				var deactivateSql = $@"UPDATE {TableName}
					SET is_active = false, rotated_at_utc = CURRENT_TIMESTAMP
					WHERE login_server_id = {{0}} AND is_active = true";
				await dbContext.Database
					.ExecuteSqlRawAsync(deactivateSql, new object[] { loginServerId }, cancellationToken)
					.ConfigureAwait(false);

				var sql = $@"INSERT INTO {TableName} (login_server_id, hmac_key, is_active, activated_at_utc)
					VALUES ({{0}}, {{1}}, true, CURRENT_TIMESTAMP)
					RETURNING id, login_server_id, hmac_key, time_created";

				return await ExecuteReturningAsync(
					dbContext,
					sql,
					new object[] { loginServerId, hmacKey },
					reader => new LoginServerSigningKeyEntity
					{
						ID = reader.GetInt64(0),
						LoginServerId = reader.GetInt64(1),
						HmacKey = (byte[])reader.GetValue(2),
						TimeCreated = reader.GetDateTime(3),
					},
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<LoginServerSigningKeyData>.Success(MapEntityToDto(result.Data))
				: DatabaseResult<LoginServerSigningKeyData>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<LoginServerSigningKeyData>> FetchByIdAsync(
			long signingKeyId,
			CancellationToken cancellationToken = default)
		{
			if (signingKeyId <= 0)
			{
				return DatabaseResult<LoginServerSigningKeyData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Signing key ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entity = await getByIdQuery(dbContext, signingKeyId, cancellationToken).ConfigureAwait(false);
				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("LoginServerSigningKey", signingKeyId.ToString());
				}

				return MapEntityToDto(entity);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<LoginServerSigningKeyData>> FetchByLoginServerIdAsync(
			long loginServerId,
			CancellationToken cancellationToken = default)
		{
			if (loginServerId <= 0)
			{
				return DatabaseResult<LoginServerSigningKeyData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"LoginServer ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entity = await getByLoginServerIdQuery(dbContext, loginServerId, cancellationToken).ConfigureAwait(false);
				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("LoginServerSigningKey", loginServerId.ToString());
				}

				return MapEntityToDto(entity);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(
			long loginServerId,
			CancellationToken cancellationToken = default)
		{
			if (loginServerId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"LoginServer ID must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				// Only prune keys that have already been rotated out AND have aged past the
				// verification overlap window. This prevents accidentally invalidating tokens
				// that were issued just before a rotation. Active keys are never deleted here —
				// callers wanting a hard purge must rotate first via UpsertAsync.
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$"DELETE FROM {TableName} WHERE login_server_id = {{0}} AND is_active = false AND rotated_at_utc IS NOT NULL AND rotated_at_utc < (CURRENT_TIMESTAMP - INTERVAL '7 days')",
					new object[] { loginServerId },
					cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("LoginServerSigningKey", loginServerId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static LoginServerSigningKeyData MapEntityToDto(LoginServerSigningKeyEntity entity)
		{
			return new LoginServerSigningKeyData(
				id: entity.ID,
				loginServerId: entity.LoginServerId,
				hmacKey: entity.HmacKey,
				timeCreated: entity.TimeCreated
			);
		}
	}
}
