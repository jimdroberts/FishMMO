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
	public sealed class LoginServerService : ILoginServerService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of LoginServerService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public LoginServerService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<LoginServerData>> AddOrUpdateAsync(
			string name,
			string address,
			ushort port,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
			{
				return DatabaseResult<LoginServerData>.Failure(
					"VALIDATION_ERROR",
					"Server name and address must not be empty.");
			}

			try
			{
				await using var context = dbContextFactory.CreateDbContext();
				var strategy = context.Database.CreateExecutionStrategy();

				var result = await strategy.ExecuteAsync(async () =>
				{
					// Atomic UPSERT - PostgreSQL specific using FormattableString
					var tableName = context.GetTableName<LoginServerEntity>();
					return await context.LoginServers
						.FromSqlInterpolated($@"
							INSERT INTO {tableName} (name, address, port, lastpulse)
							VALUES ({name}, {address}, {port}, CURRENT_TIMESTAMP)
							ON CONFLICT (name) 
							DO UPDATE SET 
								address = EXCLUDED.address,
								port = EXCLUDED.port,
								lastpulse = EXCLUDED.lastpulse
							RETURNING id, name, address, port, lastpulse")
						.AsNoTracking()
						.FirstOrDefaultAsync(cancellationToken);
				});

				if (result == null)
				{
					return DatabaseResult<LoginServerData>.Failure(
						"DB_QUERY_FAILED",
						"Failed to retrieve server data after upsert.");
				}

				var serverData = MapEntityToDto(result);
				return DatabaseResult<LoginServerData>.Success(serverData);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<LoginServerData>.FromException(
					new DatabaseTimeoutException("AddOrUpdateLoginServer", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult<LoginServerData>.FromException(
					new DatabaseQueryException(
						"AddOrUpdateLoginServer",
						"A database error occurred.",
						$"Database query error (SQL State: {pgEx.SqlState}): {pgEx.Message}",
						false,
						pgEx.SqlState,
						pgEx));
			}
			catch (NpgsqlException npgsqlEx)
			{
				return DatabaseResult<LoginServerData>.FromException(
					new DatabaseConnectionException("Failed to connect to the database.", npgsqlEx));
			}
			catch (DbUpdateException dbEx)
			{
				return DatabaseResult<LoginServerData>.FromException(
					new DatabaseQueryException(
						"AddOrUpdateLoginServer",
						"A database error occurred.",
						$"Database update failed: {dbEx.Message}",
						false,
						null,
						dbEx));
			}
			catch (Exception ex)
			{
				return DatabaseResult<LoginServerData>.FromException(
					new DatabaseException("An unexpected error occurred.", ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Server ID must be greater than 0.");
			}

			try
			{
				await using var context = dbContextFactory.CreateDbContext();
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<LoginServerEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {tableName} 
						SET lastpulse = CURRENT_TIMESTAMP 
						WHERE id = {serverId}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(
						new DatabaseEntityNotFoundException("LoginServer", $"ID {serverId}", "Login server not found."));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("PulseLoginServer", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"PulseLoginServer",
						"A database error occurred.",
						$"Database query error (SQL State: {pgEx.SqlState}): {pgEx.Message}",
						false,
						pgEx.SqlState,
						pgEx));
			}
			catch (NpgsqlException npgsqlEx)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("Failed to connect to the database.", npgsqlEx));
			}
			catch (DbUpdateException dbEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"PulseLoginServer",
						"A database error occurred.",
						$"Database update failed: {dbEx.Message}",
						false,
						null,
						dbEx));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseException("An unexpected error occurred.", ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Server ID must be greater than 0.");
			}

			try
			{
				await using var context = dbContextFactory.CreateDbContext();
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<LoginServerEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE id = {serverId}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(
						new DatabaseEntityNotFoundException("LoginServer", $"ID {serverId}", "Login server not found."));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteLoginServer", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteLoginServer",
						"A database error occurred.",
						$"Database query error (SQL State: {pgEx.SqlState}): {pgEx.Message}",
						false,
						pgEx.SqlState,
						pgEx));
			}
			catch (NpgsqlException npgsqlEx)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("Failed to connect to the database.", npgsqlEx));
			}
			catch (DbUpdateException dbEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteLoginServer",
						"A database error occurred.",
						$"Database update failed: {dbEx.Message}",
						false,
						null,
						dbEx));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseException("An unexpected error occurred.", ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<LoginServerData>> GetServerAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<LoginServerData>.Failure(
					"VALIDATION_ERROR",
					"Server ID must be greater than 0.");
			}

			try
			{
				await using var context = dbContextFactory.CreateDbContext();

				var server = await context.LoginServers
					.AsNoTracking()
					.FirstOrDefaultAsync(s => s.ID == serverId, cancellationToken);

				if (server == null)
				{
					return DatabaseResult<LoginServerData>.FromException(
						new DatabaseEntityNotFoundException("LoginServer", $"ID {serverId}", "Login server not found."));
				}

				var serverData = MapEntityToDto(server);
				return DatabaseResult<LoginServerData>.Success(serverData);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<LoginServerData>.FromException(
					new DatabaseTimeoutException("GetLoginServer", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult<LoginServerData>.FromException(
					new DatabaseQueryException(
						"GetLoginServer",
						"A database error occurred.",
						$"Database query error (SQL State: {pgEx.SqlState}): {pgEx.Message}",
						false,
						pgEx.SqlState,
						pgEx));
			}
			catch (NpgsqlException npgsqlEx)
			{
				return DatabaseResult<LoginServerData>.FromException(
					new DatabaseConnectionException("Failed to connect to the database.", npgsqlEx));
			}
			catch (Exception ex)
			{
				return DatabaseResult<LoginServerData>.FromException(
					new DatabaseException("An unexpected error occurred.", ex));
			}
		}

		/// <summary>
		/// Maps LoginServerEntity to LoginServerData DTO.
		/// </summary>
		/// <param name="entity">Login server entity from database.</param>
		/// <returns>Login server data DTO.</returns>
		private LoginServerData MapEntityToDto(LoginServerEntity entity)
		{
			return new LoginServerData
			{
				ID = entity.ID,
				Name = entity.Name,
				Address = entity.Address,
				Port = entity.Port,
				LastPulse = entity.LastPulse
			};
		}
	}
}