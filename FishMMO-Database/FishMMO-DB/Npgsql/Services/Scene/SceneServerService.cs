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
		public async Task<DatabaseResult<(long ServerId, SceneServerData ServerData)>> PersistAsync(
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
					DatabaseErrorCodes.ValidationError,
					"Name and address must not be empty.");
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

				return await dbContext.SceneServers
					.FromSqlRaw(sql, name, address, port, characterCount, locked)
					.AsNoTracking()
					.FirstAsync(cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!result.IsSuccess)
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Failure(
					result.ErrorCode,
					result.ErrorMessage,
					result.IsTransient);
			}

			if (result.Data.ID <= 0)
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Failure(
					DatabaseErrorCodes.DatabaseError,
					"Failed to upsert scene server.",
					isTransient: true);
			}

			return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Success(
				(result.Data.ID, MapEntityToDto(result.Data)));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long serverId, int characterCount, bool locked, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than zero.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET last_pulse = CURRENT_TIMESTAMP,
						character_count = {{0}},
						locked = {{1}}
					WHERE id = {{2}}";
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { characterCount, locked, serverId },
					cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("SceneServer", serverId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than zero.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE id = {{0}}";
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { serverId }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("SceneServer", serverId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneServerData>> FetchAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<SceneServerData>.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than zero.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var server = await dbContext.SceneServers
					.AsNoTracking()
					.FirstOrDefaultAsync(s => s.ID == serverId, cancellationToken)
					.ConfigureAwait(false);
				if (server == null)
				{
					throw new DatabaseEntityNotFoundException("SceneServer", serverId.ToString());
				}
				return MapEntityToDto(server);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
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