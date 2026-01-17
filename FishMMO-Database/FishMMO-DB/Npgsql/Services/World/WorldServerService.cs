using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
	public sealed class WorldServerService : IWorldServerService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

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
		public WorldServerService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
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

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var result = await strategy.ExecuteAsync(async () =>
				{
					// Atomic UPSERT - PostgreSQL specific using FormattableString
					var tableName = dbContext.GetTableName<WorldServerEntity>();
					return await dbContext.WorldServers
						.FromSqlInterpolated($@"
						INSERT INTO {tableName} 
							(name, address, port, character_count, locked, lastpulse)
						VALUES 
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
				});

				if (result == null)
				{
					return DatabaseResult<(long, WorldServerData)>.FromException(new DatabaseEntityNotFoundException(
						"WorldServer",
						"UPSERT operation returned no result."));
				}

				var serverData = MapEntityToDto(result);
				return DatabaseResult<(long, WorldServerData)>.Success((result.ID, serverData));
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<(long, WorldServerData)>.FromException(new DatabaseTimeoutException(
					"AddOrUpdateWorldServer",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<(long, WorldServerData)>.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"world_servers_name_key",
					"A server with this name already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<(long, WorldServerData)>.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"world_servers_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<(long, WorldServerData)>.FromException(new DatabaseConnectionException(
					dbContext?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<(long, WorldServerData)>.FromException(new DatabaseQueryException(
					"AddOrUpdateWorldServer",
					"Failed to add or update world server.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<(long, WorldServerData)>.FromException(new DatabaseQueryException(
					"AddOrUpdateWorldServer",
					"An unexpected error occurred while adding or updating world server.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long serverId, int characterCount, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than 0.");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<WorldServerEntity>();
					return await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {tableName} 
						SET lastpulse = CURRENT_TIMESTAMP, character_count = {characterCount} 
						WHERE id = {serverId}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(new DatabaseEntityNotFoundException(
						"WorldServer",
						serverId.ToString(),
						"Server not found."));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult.FromException(new DatabaseTimeoutException(
					"PulseWorldServer",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"world_servers_pkey",
					"A server with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"world_servers_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(new DatabaseConnectionException(
					dbContext?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"PulseWorldServer",
					"Failed to pulse world server.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"PulseWorldServer",
					"An unexpected error occurred while pulsing world server.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than 0.");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<WorldServerEntity>();
					return await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$"DELETE FROM {tableName} WHERE id = {serverId}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(new DatabaseEntityNotFoundException(
						"WorldServer",
						serverId.ToString(),
						"Server not found."));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult.FromException(new DatabaseTimeoutException(
					"DeleteWorldServer",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"world_servers_pkey",
					"A server with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"world_servers_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(new DatabaseConnectionException(
					dbContext?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"DeleteWorldServer",
					"Failed to delete world server.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"DeleteWorldServer",
					"An unexpected error occurred while deleting world server.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<WorldServerData>> GetServerAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<WorldServerData>.Failure("INVALID_SERVER_ID", "Server ID must be greater than 0.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var server = await context.WorldServers
					.AsNoTracking()
					.FirstOrDefaultAsync(s => s.ID == serverId, cancellationToken);

				if (server == null)
				{
					return DatabaseResult<WorldServerData>.FromException(new DatabaseEntityNotFoundException(
						"WorldServer",
						serverId.ToString(),
						"Server not found."));
				}

				return DatabaseResult<WorldServerData>.Success(MapEntityToDto(server));
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<WorldServerData>.FromException(new DatabaseTimeoutException(
					"GetWorldServer",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<WorldServerData>.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"world_servers_pkey",
					"A server with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<WorldServerData>.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"world_servers_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<WorldServerData>.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<WorldServerData>.FromException(new DatabaseQueryException(
					"GetWorldServer",
					"Failed to get world server.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<WorldServerData>.FromException(new DatabaseQueryException(
					"GetWorldServer",
					"An unexpected error occurred while getting world server.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<WorldServerData>>> GetActiveServersAsync(
			float idleTimeoutSeconds = 60.0f,
			CancellationToken cancellationToken = default)
		{
			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				// Calculate cutoff time in application for compiled query compatibility
				// Database will use server time when query executes, avoiding clock skew
				var cutoffTime = DateTime.UtcNow.AddSeconds(-idleTimeoutSeconds);
				
				// Use compiled query for hot path performance
				var servers = await GetActiveServersQuery(context, cutoffTime, cancellationToken);

				return DatabaseResult<List<WorldServerData>>.Success(servers.Select(MapEntityToDto).ToList());
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<List<WorldServerData>>.FromException(new DatabaseTimeoutException(
					"GetActiveWorldServers",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<List<WorldServerData>>.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"world_servers_pkey",
					"A server with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<List<WorldServerData>>.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"world_servers_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<List<WorldServerData>>.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<List<WorldServerData>>.FromException(new DatabaseQueryException(
					"GetActiveWorldServers",
					"Failed to get active world servers.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<List<WorldServerData>>.FromException(new DatabaseQueryException(
					"GetActiveWorldServers",
					"An unexpected error occurred while getting active world servers.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <summary>
		/// Maps WorldServerEntity to WorldServerData DTO.
		/// </summary>
		/// <param name="entity">World server entity from database.</param>
		/// <returns>World server data DTO.</returns>
		private WorldServerData MapEntityToDto(WorldServerEntity entity)
		{
			return new WorldServerData
			{
				ID = entity.ID,
				Name = entity.Name,
				Address = entity.Address,
				Port = entity.Port,
				CharacterCount = entity.CharacterCount,
				Locked = entity.Locked,
				LastPulse = entity.LastPulse
			};
		}
	}
}