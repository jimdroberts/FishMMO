using System;
using System.Collections.Generic;
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
	/// Service for managing connection token verification keys.
	/// LoginServers use this to discover verification keys at startup and to
	/// periodically poll for new keys from new regions.
	/// </summary>
	public sealed class ConnectionTokenKeyService : BaseService<ConnectionTokenKeyEntity>, IConnectionTokenKeyService
	{
		/// <summary>
		/// Initializes a new instance of ConnectionTokenKeyService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public ConnectionTokenKeyService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ConnectionTokenKeyData[]>> FetchAllActiveAsync(
			CancellationToken cancellationToken = default)
		{
			// Do not use EF.CompileAsyncQuery with OrderByDescending(TimeCreated): on some
			// EF Core / Npgsql versions the compiled form fails translation at runtime even
			// though the same LINQ works via normal IQueryable, breaking World/Scene token
			// key load while rows exist in connection_token_keys.
			return await ExecuteReadAsync(async dbContext =>
			{
				List<ConnectionTokenKeyEntity> entities = await dbContext.ConnectionTokenKeys
					.AsNoTracking()
					.Where(k => k.IsActive)
					.OrderByDescending(k => k.TimeCreated)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				return entities.Select(MapEntityToDto).ToArray();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ConnectionTokenKeyData>> FetchByKeyIdAsync(
			string keyId,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(keyId))
			{
				return DatabaseResult<ConnectionTokenKeyData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Key ID must not be null or empty.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				ConnectionTokenKeyEntity? entity = await dbContext.ConnectionTokenKeys
					.AsNoTracking()
					.FirstOrDefaultAsync(k => k.KeyId == keyId, cancellationToken)
					.ConfigureAwait(false);
				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("ConnectionTokenKey", keyId);
				}

				return MapEntityToDto(entity);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ConnectionTokenKeyData>> UpsertAsync(
			string keyId,
			byte[] hmacKey,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(keyId))
			{
				return DatabaseResult<ConnectionTokenKeyData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Key ID must not be null or empty.");
			}

			if (hmacKey == null || hmacKey.Length == 0)
			{
				return DatabaseResult<ConnectionTokenKeyData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"HMAC key must not be null or empty.");
			}

			// Encode the raw key bytes to base64 for storage
			string hmacKeyBase64 = Convert.ToBase64String(hmacKey);

			return await ExecuteWriteAsync(async dbContext =>
			{
				// Use INSERT ... ON CONFLICT to atomically insert or update.
				var sql = $@"INSERT INTO {TableName} (key_id, hmac_key_base64, is_active, time_created)
					VALUES ({{0}}, {{1}}, true, CURRENT_TIMESTAMP)
					ON CONFLICT (key_id) DO UPDATE SET
						hmac_key_base64 = EXCLUDED.hmac_key_base64,
						is_active = true,
						deactivated_at = NULL
					RETURNING id, key_id, hmac_key_base64, is_active, time_created, deactivated_at";

				var entity = await ExecuteReturningAsync(
					dbContext,
					sql,
					new object[] { keyId, hmacKeyBase64 },
					reader => new ConnectionTokenKeyEntity
					{
						ID = reader.GetInt64(0),
						KeyId = reader.GetString(1),
						HmacKeyBase64 = reader.GetString(2),
						IsActive = reader.GetBoolean(3),
						TimeCreated = reader.GetDateTime(4),
						DeactivatedAt = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
					},
					cancellationToken).ConfigureAwait(false);

				return MapEntityToDto(entity);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<Dictionary<string, byte[]>>> GetConnectionTokenKeyMapAsync(
			CancellationToken cancellationToken = default)
		{
			var result = await FetchAllActiveAsync(cancellationToken).ConfigureAwait(false);
			if (!result.IsSuccess)
			{
				return DatabaseResult<Dictionary<string, byte[]>>.Failure(
					result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			var map = new Dictionary<string, byte[]>(result.Data.Length);
			foreach (var data in result.Data)
			{
				map[data.KeyId] = data.HmacKey;
			}

			return DatabaseResult<Dictionary<string, byte[]>>.Success(map);
		}

		private static ConnectionTokenKeyData MapEntityToDto(ConnectionTokenKeyEntity entity)
		{
			return new ConnectionTokenKeyData(
				id: entity.ID,
				keyId: entity.KeyId,
				hmacKey: Convert.FromBase64String(entity.HmacKeyBase64),
				isActive: entity.IsActive,
				timeCreated: entity.TimeCreated
			);
		}
	}
}
