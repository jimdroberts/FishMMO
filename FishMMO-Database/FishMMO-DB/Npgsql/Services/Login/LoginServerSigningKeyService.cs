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
	/// Uses EF Core compiled queries for the hot validation path and raw SQL for upserts.
	/// </summary>
	public sealed class LoginServerSigningKeyService : BaseService<LoginServerSigningKeyEntity>, ILoginServerSigningKeyService
	{
#pragma warning disable CS8619
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<LoginServerSigningKeyEntity?>> getByLoginServerIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long loginServerId, CancellationToken ct) =>
				context.LoginServerSigningKeys
					.AsNoTracking()
					.FirstOrDefault(k => k.LoginServerId == loginServerId));
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
				var sql = $@"INSERT INTO {TableName} (login_server_id, hmac_key)
					VALUES ({{0}}, {{1}})
					ON CONFLICT (login_server_id)
					DO UPDATE SET
						hmac_key = EXCLUDED.hmac_key,
						time_created = CURRENT_TIMESTAMP
					RETURNING id, login_server_id, hmac_key, time_created";

				return await dbContext.LoginServerSigningKeys
					.FromSqlRaw(sql, loginServerId, hmacKey)
					.AsNoTracking()
					.FirstAsync(cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<LoginServerSigningKeyData>.Success(MapEntityToDto(result.Data))
				: DatabaseResult<LoginServerSigningKeyData>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$"DELETE FROM {TableName} WHERE login_server_id = {{0}}",
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
