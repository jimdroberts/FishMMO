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
		/// Compiled query for retrieving a world server by unique name with tracking.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<WorldServerEntity?>> getByNameTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string serverName, CancellationToken ct) =>
				context.WorldServers
					.FirstOrDefault(s => s.Name == serverName));
#pragma warning restore CS8619

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

			var now = DateTime.UtcNow;
			var insertResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var entity = new WorldServerEntity
				{
					Name = name,
					Address = address,
					Port = port,
					CharacterCount = characterCount,
					Locked = locked,
					TimeCreated = now,
					LastPulse = now
				};

				await dbContext.WorldServers.AddAsync(entity, cancellationToken).ConfigureAwait(false);
				return entity;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (insertResult.IsSuccess)
			{
				var entity = insertResult.Data;
				return DatabaseResult<(long ServerId, WorldServerData ServerData)>.Success((entity.ID, MapEntityToDto(entity)));
			}

			if (!string.Equals(insertResult.ErrorCode, "UNIQUE_VIOLATION", StringComparison.Ordinal))
			{
				return DatabaseResult<(long ServerId, WorldServerData ServerData)>.Failure(insertResult.ErrorCode, insertResult.ErrorMessage, insertResult.IsTransient);
			}

			var updateNow = DateTime.UtcNow;
			var updateResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var entity = await getByNameTrackingQuery(dbContext, name, cancellationToken).ConfigureAwait(false);
				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", name);
				}

				entity.Address = address;
				entity.Port = port;
				entity.CharacterCount = characterCount;
				entity.Locked = locked;
				entity.LastPulse = updateNow;
				return entity;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return updateResult.IsSuccess
				? DatabaseResult<(long ServerId, WorldServerData ServerData)>.Success((updateResult.Data.ID, MapEntityToDto(updateResult.Data)))
				: DatabaseResult<(long ServerId, WorldServerData ServerData)>.Failure(updateResult.ErrorCode, updateResult.ErrorMessage, updateResult.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long serverId, int characterCount, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than 0.");
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET lastpulse = CURRENT_TIMESTAMP, character_count = {{0}}
					WHERE id = {{1}}";

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { characterCount, serverId },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", serverId.ToString());
				}
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than 0.");
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE id = {{0}}";
				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { serverId }, cancellationToken)
					.ConfigureAwait(false);
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<WorldServerData>> GetServerAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<WorldServerData>.Failure("INVALID_SERVER_ID", "Server ID must be greater than 0.");
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
		public async Task<DatabaseResult<List<WorldServerData>>> GetActiveServersAsync(
			float idleTimeoutSeconds = 60.0f,
			CancellationToken cancellationToken = default)
		{
			return await ExecuteReadAsync(async dbContext =>
			{
				// Use database server time to avoid clock skew issues between application and database servers.
				// Use numeric * interval to keep the timeout value parameterized.
				var sql = $@"SELECT * FROM {TableName}
					WHERE lastpulse >= (CURRENT_TIMESTAMP - ({{0}} * INTERVAL '1 second'))";

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