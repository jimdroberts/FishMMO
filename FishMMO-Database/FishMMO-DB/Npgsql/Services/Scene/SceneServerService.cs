using System;
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
	public sealed class SceneServerService : BaseService<SceneServerEntity>, ISceneServerService
	{
		/// <summary>
		/// Initializes a new instance of SceneServerService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public SceneServerService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<(long ServerId, SceneServerData ServerData)>> AddOrUpdateAsync(
			string name,
			string address,
			ushort port,
			int characterCount,
			bool locked,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Failure(
					"INVALID_PARAMETERS",
					"Name and address must not be empty.");
			}

			return await ExecuteAsync<(long ServerId, SceneServerData ServerData)>(async (dbContext, ct) =>
			{
				// Atomic UPSERT - PostgreSQL specific using Raw SQL
				var result = await dbContext.SceneServers
					.FromSqlRaw($@"
					INSERT INTO {TableName} 
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
					RETURNING id, name, address, port, character_count, locked, lastpulse",
					name,
					address,
					port,
					characterCount,
					locked)
						.AsNoTracking()
						.FirstOrDefaultAsync(ct).ConfigureAwait(false);

				if (result == null)
				{
					throw new DatabaseQueryException(
						"AddOrUpdateSceneServer",
						"Failed to retrieve server data after upsert.",
						"UPSERT returned no results",
						false,
						null);
				}

				var serverData = MapEntityToDto(result);
				return (result.ID, serverData);
			}, "AddOrUpdateSceneServer", cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long serverId, int characterCount, bool locked, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than zero.");
			}

			var result = await ExecuteRawSqlAsync(
				$@"UPDATE {TableName} 
					SET lastpulse = CURRENT_TIMESTAMP, character_count = {{0}}, locked = {{1}} 
					WHERE id = {{2}}",
				"PulseSceneServer",
				new object[] { characterCount, locked, serverId },
				entityName: "SceneServer",
				entityId: serverId,
				requireRowsAffected: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than zero.");
			}

			var result = await ExecuteRawSqlAsync(
				$"DELETE FROM {TableName} WHERE id = {{0}}",
				"DeleteSceneServer",
				new object[] { serverId },
				entityName: "SceneServer",
				entityId: serverId,
				requireRowsAffected: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneServerData>> GetServerAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<SceneServerData>.Failure("INVALID_SERVER_ID", "Server ID must be greater than zero.");
			}

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				var server = await dbContext.SceneServers
					.AsNoTracking()
					.FirstOrDefaultAsync(s => s.ID == serverId, ct).ConfigureAwait(false);
				var existingServer = RequireEntityExists(server, "SceneServer", serverId);
				return MapEntityToDto(existingServer);
			}, "GetSceneServer", cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps SceneServerEntity to SceneServerData DTO.
		/// </summary>
		/// <param name="entity">Scene server entity from database.</param>
		/// <returns>Scene server data DTO.</returns>
		private SceneServerData MapEntityToDto(SceneServerEntity entity)
		{
			return new SceneServerData(
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