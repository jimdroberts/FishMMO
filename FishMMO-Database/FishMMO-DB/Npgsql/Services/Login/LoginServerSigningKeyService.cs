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

		/// <summary>
		/// Verification overlap window (in days) during which rotated-out keys are still kept in
		/// the table so in-flight tokens signed before rotation can be validated. The value is
		/// injected at construction time and defaults to 7 days.
		/// </summary>
		public int KeyOverlapWindowDays { get; }

		public LoginServerSigningKeyService(INpgsqlDbContextFactory dbContextFactory)
			: this(dbContextFactory, 7)
		{
		}

		public LoginServerSigningKeyService(INpgsqlDbContextFactory dbContextFactory, int keyOverlapWindowDays)
			: base(dbContextFactory)
		{
			KeyOverlapWindowDays = keyOverlapWindowDays > 0 ? keyOverlapWindowDays : throw new ArgumentOutOfRangeException(nameof(keyOverlapWindowDays), "Key overlap window must be greater than 0.");
		}

		/// <summary>
		/// Upserts the active signing key for the specified LoginServer.
		///
		/// DUAL-LAYER EXECUTION CONTROL:
		///
		/// Outer layer — ExecuteWriteAsync retry loop (defined in BaseService):
		///   Wraps the entire operation in a configurable retry policy
		///   (up to 3 attempts with exponential backoff).  If the inner
		///   execution strategy or transaction fails transiently (e.g.
		///   serialisation error, deadlock victim), the outer loop retries
		///   from scratch — creating a fresh DbContext and re-entering the
		///   execution strategy.
		///
		/// Inner layer — execution strategy with explicit transaction:
		///   The DbContext.Database.CreateExecutionStrategy() provides
		///   Npgsql's built-in retry-on-serialisation-failure inside the
		///   transaction scope.  The explicit transaction itself ensures
		///   atomicity of the deactivate-INSERT pair: if the INSERT fails,
		///   the UPDATE is rolled back and no key is lost.  The partial
		///   UNIQUE index on (login_server_id) WHERE is_active=true
		///   additionally serialises concurrent rotations.
		///
		/// This dual-layer design was chosen because the outer retry
		/// handles connection-level failures (pool exhaustion, DNS flake,
		/// TCP reset) while the inner strategy handles database-level
		/// contention (serialisation failures, unique-index violations).
		/// Each layer retries at its own granularity without conflating
		/// the two failure modes.
		/// </summary>
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

			// NOTE: Dual-layer execution control — outer ExecuteWriteAsync retry loop, inner ExecutionStrategy
			// with transaction. If inner transaction rolls back, outer wrapper has no awareness but will retry fresh.
			var result = await ExecuteWriteAsync(async dbContext =>
			{
				// Wrap the deactivate + insert in an explicit transaction so a failure of the
				// INSERT after the UPDATE succeeds cannot leave the LoginServer with no active
				// signing key. The DB partial UNIQUE index on (login_server_id) WHERE
				// is_active=true additionally serialises concurrent rotations: the second writer's
				// INSERT is rejected and its transaction rolls back cleanly.
				var strategy = dbContext.Database.CreateExecutionStrategy();
				return await strategy.ExecuteAsync(async () =>
				{
					await using var tx = await dbContext.Database
						.BeginTransactionAsync(cancellationToken)
						.ConfigureAwait(false);

					// Mark all previously active keys for this LoginServer as rotated.
					// They remain in the table for the verification overlap window so in-flight tokens
					// signed with the old key can still be validated until DeleteAsync prunes them.
					var deactivateSql = $@"UPDATE {TableName}
						SET is_active = false, rotated_at_utc = timezone('UTC', CURRENT_TIMESTAMP)
						WHERE login_server_id = {{0}} AND is_active = true";
					await dbContext.Database
						.ExecuteSqlRawAsync(deactivateSql, new object[] { loginServerId }, cancellationToken)
						.ConfigureAwait(false);

					var sql = $@"INSERT INTO {TableName} (login_server_id, hmac_key, is_active, activated_at_utc)
						VALUES ({{0}}, {{1}}, true, timezone('UTC', CURRENT_TIMESTAMP))
						RETURNING id, login_server_id, hmac_key, time_created";

					var entity = await ExecuteReturningAsync(
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

					await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
					return entity;
				}).ConfigureAwait(false);
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
				int overlapDays = KeyOverlapWindowDays;
				if (overlapDays < 0)
				{
					overlapDays = 0;
				}
				await dbContext.Database.ExecuteSqlRawAsync(
					$"DELETE FROM {TableName} WHERE login_server_id = {{0}} AND is_active = false AND rotated_at_utc IS NOT NULL AND rotated_at_utc < (timezone('UTC', CURRENT_TIMESTAMP) - ({{1}} * INTERVAL '1 day'))",
					new object[] { loginServerId, overlapDays },
					cancellationToken)
					.ConfigureAwait(false);

				// Having no keys to prune is a valid state — there may be no rotated keys to clean up.
				// The operation is always considered successful from the caller's perspective;
				// zero affected rows simply means there was nothing to clean.
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