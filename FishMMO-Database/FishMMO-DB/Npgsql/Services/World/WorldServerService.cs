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
	/// <para><b>Exception Handling Order:</b></para>
	/// <list type="number">
	/// <item>OperationCanceledException → DatabaseOperationCanceledException</item>
	/// <item>PostgresException (SqlState "23505") → DatabaseConstraintException (Unique)</item>
	/// <item>PostgresException (SqlState "23503") → DatabaseConstraintException (ForeignKey)</item>
	/// <item>NpgsqlException → DatabaseConnectionException</item>
	/// <item>DbUpdateException → DatabaseQueryException</item>
	/// <item>Exception → DatabaseQueryException</item>
	/// </list>
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
		public async Task<DatabaseResult<(long ServerId, WorldServerData ServerData)>> AddOrUpdateAsync(
			string name,
			string address,
			ushort port,
			int characterCount,
			bool locked,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
			{
				return DatabaseResult<(long, WorldServerData)>.Failure("INVALID_PARAMETERS", "Server name and address must not be empty.");
			}

			return await ExecuteAsync<(long ServerId, WorldServerData ServerData)>(async (dbContext, ct) =>
			{
				var sql = $@"INSERT INTO {TableName}
					(name, address, port, character_count, locked, lastpulse)
				VALUES
					({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, CURRENT_TIMESTAMP)
				ON CONFLICT (name)
				DO UPDATE SET
					address = EXCLUDED.address,
					port = EXCLUDED.port,
					character_count = EXCLUDED.character_count,
					locked = EXCLUDED.locked,
					lastpulse = EXCLUDED.lastpulse
				RETURNING id, name, address, port, character_count, locked, lastpulse";

				var result = await dbContext.WorldServers
					.FromSqlRaw(sql, name, address, port, characterCount, locked)
					.AsNoTracking()
					.FirstOrDefaultAsync(ct).ConfigureAwait(false);

				if (result == null)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", "UPSERT operation returned no result.");
				}

				var serverData = MapEntityToDto(result);
				return (result.ID, serverData);
			}, "AddOrUpdateWorldServer", cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long serverId, int characterCount, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than 0.");
			}

			var sql = $@"UPDATE {TableName}
			SET lastpulse = CURRENT_TIMESTAMP, character_count = {{0}}
			WHERE id = {{1}}";

			var result = await ExecuteRawSqlAsync(
				sql,
				"PulseWorldServer",
				new object[] { characterCount, serverId },
				entityName: "WorldServer",
				entityId: serverId.ToString(),
				requireRowsAffected: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than 0.");
			}

			var sql = $@"DELETE FROM {TableName} WHERE id = {{0}}";

			var result = await ExecuteRawSqlAsync(
				sql,
				"DeleteWorldServer",
				new object[] { serverId },
				entityName: "WorldServer",
				entityId: serverId.ToString(),
				requireRowsAffected: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<WorldServerData>> GetServerAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<WorldServerData>.Failure("INVALID_SERVER_ID", "Server ID must be greater than 0.");
			}

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				var server = await dbContext.WorldServers
					.AsNoTracking()
					.FirstOrDefaultAsync(s => s.ID == serverId, ct).ConfigureAwait(false);

				if (server == null)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", serverId.ToString());
				}

				return MapEntityToDto(server);
			}, "GetWorldServer", cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<WorldServerData>>> GetActiveServersAsync(
			float idleTimeoutSeconds = 60.0f,
			CancellationToken cancellationToken = default)
		{
			return await ExecuteAsync(async (dbContext, ct) =>
			{
				// Use database server time to avoid clock skew issues between application and database servers.
				// Use numeric * interval to keep the timeout value parameterized.
				var sql = $@"SELECT * FROM {TableName}
					WHERE lastpulse >= (CURRENT_TIMESTAMP - ({{0}} * INTERVAL '1 second'))";

				var servers = await dbContext.WorldServers
					.FromSqlRaw(sql, idleTimeoutSeconds)
					.OrderBy(s => s.Name)
					.ToListAsync(ct).ConfigureAwait(false);
				return servers.Select(MapEntityToDto).ToList();
			}, "GetActiveWorldServers", cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps WorldServerEntity to WorldServerData DTO.		/// </summary>
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