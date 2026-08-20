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
	/// <inheritdoc/>
	/// <remarks>
	/// <para><b>Error Handling:</b> All exceptions are classified by <c>BaseService</c> and mapped to <see cref="DatabaseResult"/> error codes
	/// (e.g., UNIQUE_VIOLATION, FOREIGN_KEY_VIOLATION, STALE_STATE, DATABASE_ERROR). Transient failures are retried automatically.</para>
	/// <para>Unique constraint violations are treated as failures; this service generally avoids relying on 23505 by using atomic SQL (<c>ON CONFLICT</c>) where applicable.</para>
	/// </remarks>
	public sealed class WorldServerService : BaseService<WorldServerEntity>, IWorldServerService
	{
		/// <summary>
		/// Initializes a new instance of WorldServerService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public WorldServerService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<(long ServerId, WorldServerData ServerData)>> PersistAsync(
			string name,
			string address,
			ushort port,
			int characterCount,
			bool locked,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
			{
				return DatabaseResult<(long, WorldServerData)>.Failure(DatabaseErrorCodes.ValidationError, "Server name and address must not be empty.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* locked and shutdown_at_utc are deliberately NOT overwritten on conflict.
				 *
				 * They are operator state, and this statement runs on every server startup.
				 * Writing EXCLUDED.locked meant restarting a world server silently unlocked it —
				 * so the one action an operator takes during maintenance undid the lock they had
				 * set to make the maintenance safe. The row keeps whatever the operator last
				 * said; only SetLockedAsync and SetShutdownAsync change it. */
				var sql = $@"INSERT INTO {TableName} (name, address, port, character_count, locked)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}})
					ON CONFLICT (name)
					DO UPDATE SET
						address = EXCLUDED.address,
						port = EXCLUDED.port,
						character_count = EXCLUDED.character_count,
						last_pulse = CURRENT_TIMESTAMP
					RETURNING id, name, time_created, last_pulse, address, port, character_count, locked";

				return await ExecuteReturningAsync(
					dbContext,
					sql,
					new object[] { name, address, (int)port, characterCount, locked },
					reader => new WorldServerEntity
					{
						ID = reader.GetInt64(0),
						Name = reader.GetString(1),
						TimeCreated = reader.GetDateTime(2),
						LastPulse = reader.GetDateTime(3),
						Address = reader.GetString(4),
						Port = reader.GetInt32(5),
						CharacterCount = reader.GetInt32(6),
						Locked = reader.GetBoolean(7),
					},
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<(long ServerId, WorldServerData ServerData)>.Success((result.Data.ID, MapEntityToDto(result.Data)))
				: DatabaseResult<(long ServerId, WorldServerData ServerData)>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ServerControlState>> PulseAsync(long serverId, int characterCount, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<ServerControlState>.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than 0.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* The heartbeat is also how the server learns its own control state.
				 *
				 * RETURNING makes the read part of the write already being made, so adopting an
				 * operator's lock or shutdown costs no extra round trip and cannot observe a
				 * value from between two statements. */
				var sql = $@"UPDATE {TableName}
					SET last_pulse = CURRENT_TIMESTAMP, character_count = {{0}}
					WHERE id = {{1}}
					RETURNING locked, shutdown_at_utc";

				/* Nullable, so "no such row" is distinguishable from "row says unlocked".
				 *
				 * ExecuteReturningOrDefaultAsync yields default(T) when the UPDATE matched
				 * nothing, and default(ServerControlState) is a perfectly plausible healthy
				 * state — unlocked, no shutdown. A deregistered or deleted server would have
				 * read its own disappearance as an all-clear and carried on serving. */
				var state = await ExecuteReturningOrDefaultAsync(
					dbContext,
					sql,
					new object[] { characterCount, serverId },
					reader => (ServerControlState?)new ServerControlState(
						reader.GetBoolean(0),
						reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1)),
					cancellationToken).ConfigureAwait(false);

				if (!state.HasValue)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", serverId.ToString());
				}

				return state.Value;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SetLockedAsync(long serverId, bool locked, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET locked = {{0}} WHERE id = {{1}}";

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { locked, serverId },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", serverId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SetShutdownAsync(long serverId, DateTime? shutdownAtUtc, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Scheduling a shutdown locks the server in the same statement. A world that is
				 * about to stop must not be admitting anyone in the meantime, and leaving that
				 * to a second call would leave a window where it does — and a way for the two to
				 * disagree if the second one failed.
				 *
				 * Cancelling deliberately does NOT unlock. "Stop the shutdown" and "reopen to
				 * players" are different decisions: an operator who halts a shutdown because
				 * something looks wrong usually wants the world to stay closed while they look.
				 * Unlocking is its own command. */
				/* Each branch supplies exactly the parameters its statement references.
				 * Passing a placeholder the SQL never uses leaves an unbound parameter on the
				 * command, which is at best pointless and at worst provider-specific. */
				string sql;
				object[] parameters;
				if (shutdownAtUtc.HasValue)
				{
					sql = $@"UPDATE {TableName} SET shutdown_at_utc = {{0}}, locked = true WHERE id = {{1}}";
					parameters = new object[] { shutdownAtUtc.Value, serverId };
				}
				else
				{
					sql = $@"UPDATE {TableName} SET shutdown_at_utc = NULL WHERE id = {{0}}";
					parameters = new object[] { serverId };
				}

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					parameters,
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", serverId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ServerControlState>> FetchControlStateAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<ServerControlState>.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entity = await dbContext.WorldServers
					.AsNoTracking()
					.Where(w => w.ID == serverId)
					.Select(w => new { w.Locked, w.ShutdownAtUtc })
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", serverId.ToString());
				}

				return new ServerControlState(entity.Locked, entity.ShutdownAtUtc);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE id = {{0}}";
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { serverId }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", serverId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<WorldServerData>> FetchAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<WorldServerData>.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var server = await dbContext.WorldServers
					.AsNoTracking()
					.FirstOrDefaultAsync(s => s.ID == serverId, cancellationToken).ConfigureAwait(false);

				if (server == null)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", serverId.ToString());
				}

				return MapEntityToDto(server);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<WorldServerData>>> FetchActiveAsync(
			float idleTimeoutSeconds = 60.0f,
			CancellationToken cancellationToken = default)
		{
			return await ExecuteReadAsync(async dbContext =>
			{
				// Use database server time to avoid clock skew issues between application and database servers.
				// Use numeric * interval to keep the timeout value parameterized.
				var sql = $@"SELECT * FROM {TableName}
					WHERE last_pulse >= (CURRENT_TIMESTAMP - ({{0}} * INTERVAL '1 second'))";

				var servers = await dbContext.WorldServers
					.FromSqlRaw(sql, idleTimeoutSeconds)
					.AsNoTracking()
					.OrderBy(s => s.Name)
					.ToListAsync(cancellationToken).ConfigureAwait(false);
				return servers.Select(MapEntityToDto).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps WorldServerEntity to WorldServerData DTO.
		/// </summary>
		/// <param name="entity">World server entity from database.</param>
		/// <returns>World server data DTO.</returns>
		private WorldServerData MapEntityToDto(WorldServerEntity entity)
		{
			return new WorldServerData(
				id: entity.ID,
				name: entity.Name,
				lastPulse: entity.LastPulse,
				address: entity.Address,
				port: entity.Port,
				characterCount: entity.CharacterCount,
				locked: entity.Locked
			);
		}
	}
}