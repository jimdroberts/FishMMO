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
				var sql = $@"INSERT INTO {TableName} (name, address, port, character_count, locked)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}})
					ON CONFLICT (name)
					DO UPDATE SET
						address = EXCLUDED.address,
						port = EXCLUDED.port,
						character_count = EXCLUDED.character_count,
						locked = EXCLUDED.locked,
						last_pulse = CURRENT_TIMESTAMP
					RETURNING id, name, time_created, last_pulse, address, port, character_count, locked";

				return await dbContext.WorldServers
					.FromSqlRaw(sql, name, address, port, characterCount, locked)
					.AsNoTracking()
					.FirstAsync(cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<(long ServerId, WorldServerData ServerData)>.Success((result.Data.ID, MapEntityToDto(result.Data)))
				: DatabaseResult<(long ServerId, WorldServerData ServerData)>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long serverId, int characterCount, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET last_pulse = CURRENT_TIMESTAMP, character_count = {{0}}
					WHERE id = {{1}}";

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { characterCount, serverId },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", serverId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
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