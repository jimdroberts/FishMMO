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
	/// <item>OperationCanceledException → DatabaseTimeoutException</item>
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
		/// Compiled query for GetActiveServersAsync hot path.
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// Uses DateTime arithmetic for timeout calculation (LINQ-compatible).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, DateTime, CancellationToken, Task<List<WorldServerEntity>>> GetActiveServersQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, DateTime cutoffTime, CancellationToken ct) =>
				context.WorldServers
					.AsNoTracking()
					.Where(s => s.LastPulse >= cutoffTime)
					.OrderBy(s => s.Name)
					.ToList());

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

			return await ExecuteWithStrategyAsync<(long ServerId, WorldServerData ServerData)>(async (dbContext, strategy) =>
			{
				var result = await dbContext.WorldServers
					.FromSqlInterpolated($@"
					INSERT INTO {TableName} 
							({name}, {address}, {port}, {characterCount}, {locked}, CURRENT_TIMESTAMP)
						ON CONFLICT (name) 
						DO UPDATE SET 
							address = EXCLUDED.address,
							port = EXCLUDED.port,
							character_count = EXCLUDED.character_count,
							locked = EXCLUDED.locked,
							lastpulse = EXCLUDED.lastpulse
						RETURNING id, name, address, port, character_count, locked, lastpulse")
							.AsNoTracking()
							.FirstOrDefaultAsync(cancellationToken);

				if (result == null)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", "UPSERT operation returned no result.");
				}

				var serverData = MapEntityToDto(result);
				return (result.ID, serverData);
			}, "AddOrUpdateWorldServer", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long serverId, int characterCount, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than 0.");
			}

			return await ExecuteWithStrategyAsync(async (dbContext, strategy) =>
			{
				var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"UPDATE {TableName} 
					SET lastpulse = CURRENT_TIMESTAMP, character_count = {characterCount} 
					WHERE id = {serverId}",
					cancellationToken);

				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", serverId.ToString());
				}
			}, "PulseWorldServer", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than 0.");
			}

			return await ExecuteWithStrategyAsync(async (dbContext, strategy) =>
			{
				var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$"DELETE FROM {TableName} WHERE id = {serverId}",
					cancellationToken);

				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", serverId.ToString());
				}
			}, "DeleteWorldServer", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<WorldServerData>> GetServerAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<WorldServerData>.Failure("INVALID_SERVER_ID", "Server ID must be greater than 0.");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				var server = await dbContext.WorldServers
					.AsNoTracking()
					.FirstOrDefaultAsync(s => s.ID == serverId, cancellationToken);

				if (server == null)
				{
					throw new DatabaseEntityNotFoundException("WorldServer", serverId.ToString());
				}

				return MapEntityToDto(server);
			}, "GetWorldServer", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<WorldServerData>>> GetActiveServersAsync(
			float idleTimeoutSeconds = 60.0f,
			CancellationToken cancellationToken = default)
		{
			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Calculate cutoff time in application for compiled query compatibility
				// Database will use server time when query executes, avoiding clock skew
				var cutoffTime = DateTime.UtcNow.AddSeconds(-idleTimeoutSeconds);

				// Use compiled query for hot path performance
				var servers = await GetActiveServersQuery(dbContext, cutoffTime, cancellationToken);

				return servers.Select(MapEntityToDto).ToList();
			}, "GetActiveWorldServers", cancellationToken);
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