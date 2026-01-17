using System;
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
	/// <para><b>Exception Handling:</b></para>
	/// <list type="bullet">
	/// <item><description><see cref="OperationCanceledException"/> → <see cref="DatabaseTimeoutException"/></description></item>
	/// <item><description><see cref="PostgresException"/> (23505) → <see cref="DatabaseConstraintException"/> (Unique)</description></item>
	/// <item><description><see cref="PostgresException"/> (23503) → <see cref="DatabaseConstraintException"/> (ForeignKey)</description></item>
	/// <item><description><see cref="NpgsqlException"/> → <see cref="DatabaseConnectionException"/></description></item>
	/// <item><description><see cref="DbUpdateException"/> → <see cref="DatabaseQueryException"/></description></item>
	/// <item><description><see cref="Exception"/> → <see cref="DatabaseQueryException"/></description></item>
	/// </list>
	/// </remarks>
	public sealed class SceneServerService : ISceneServerService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of SceneServerService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public SceneServerService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
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

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var result = await strategy.ExecuteAsync(async () =>
				{
					// Atomic UPSERT - PostgreSQL specific using FormattableString
					var tableName = dbContext.GetTableName<SceneServerEntity>();
					return await dbContext.SceneServers
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
					return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Failure(
						"UPSERT_FAILED",
						"Failed to retrieve server data after upsert.");
				}

				var serverData = MapEntityToDto(result);
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Success((result.ID, serverData));
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.FromException(
					new DatabaseTimeoutException(
						"AddOrUpdateSceneServer",
						30,
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"scene_servers_name_key",
						"A server with this name already exists.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"scene_servers_foreign_key",
						"The referenced entity does not exist.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.FromException(
					new DatabaseConnectionException(
						dbContext?.Database.GetConnectionString() ?? "unknown",
						ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.FromException(
					new DatabaseQueryException(
						"AddOrUpdateSceneServer",
						"Failed to add or update scene server.",
						ex.Message,
						false,
						null,
						ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.FromException(
					new DatabaseQueryException(
						"AddOrUpdateSceneServer",
						"An unexpected error occurred while adding or updating scene server.",
						ex.Message,
						false,
						null,
						ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long serverId, int characterCount, bool locked, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than zero.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<SceneServerEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {tableName} 
						SET lastpulse = CURRENT_TIMESTAMP, character_count = {characterCount}, locked = {locked} 
						WHERE id = {serverId}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(new DatabaseEntityNotFoundException(
						"SceneServer",
						serverId.ToString(),
						"Scene server not found."));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult.FromException(new DatabaseTimeoutException(
					"PulseSceneServer",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"scene_servers_pkey",
					"A server with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"scene_servers_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"PulseSceneServer",
					"Failed to pulse scene server.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"PulseSceneServer",
					"An unexpected error occurred while pulsing scene server.",
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
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than zero.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<SceneServerEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$"DELETE FROM {tableName} WHERE id = {serverId}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(new DatabaseEntityNotFoundException(
						"SceneServer",
						serverId.ToString(),
						"Scene server not found."));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult.FromException(new DatabaseTimeoutException(
					"DeleteSceneServer",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"scene_servers_pkey",
					"A server with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"scene_servers_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"DeleteSceneServer",
					"Failed to delete scene server.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"DeleteSceneServer",
					"An unexpected error occurred while deleting scene server.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneServerData>> GetServerAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<SceneServerData>.Failure("INVALID_SERVER_ID", "Server ID must be greater than zero.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var server = await context.SceneServers
					.AsNoTracking()
					.FirstOrDefaultAsync(s => s.ID == serverId, cancellationToken);

				if (server == null)
				{
					return DatabaseResult<SceneServerData>.FromException(new DatabaseEntityNotFoundException(
						"SceneServer",
						serverId.ToString(),
						"Scene server not found."));
				}

				return DatabaseResult<SceneServerData>.Success(MapEntityToDto(server));
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<SceneServerData>.FromException(new DatabaseTimeoutException(
					"GetSceneServer",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<SceneServerData>.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"scene_servers_pkey",
					"A server with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<SceneServerData>.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"scene_servers_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<SceneServerData>.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<SceneServerData>.FromException(new DatabaseQueryException(
					"GetSceneServer",
					"Failed to get scene server.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<SceneServerData>.FromException(new DatabaseQueryException(
					"GetSceneServer",
					"An unexpected error occurred while getting scene server.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <summary>
		/// Maps SceneServerEntity to SceneServerData DTO.
		/// </summary>
		/// <param name="entity">Scene server entity from database.</param>
		/// <returns>Scene server data DTO.</returns>
		private SceneServerData MapEntityToDto(SceneServerEntity entity)
		{
			return new SceneServerData
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