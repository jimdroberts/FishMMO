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
	public sealed class LoginServerService : BaseService<LoginServerEntity>, ILoginServerService
	{
		/// <summary>
		/// Initializes a new instance of LoginServerService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public LoginServerService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
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

			return await ExecuteAsync<LoginServerData>(async (dbContext, ct) =>
			{
				var result = await dbContext.LoginServers
					.FromSqlRaw($@"
						INSERT INTO {TableName} (name, address, port, lastpulse)
						VALUES ({{0}}, {{1}}, {{2}}, CURRENT_TIMESTAMP)
						ON CONFLICT (name) 
						DO UPDATE SET 
							address = EXCLUDED.address,
							port = EXCLUDED.port,
							lastpulse = EXCLUDED.lastpulse
						RETURNING id, name, address, port, lastpulse",
						name,
						address,
						port)
					.AsNoTracking()
						.FirstOrDefaultAsync(ct).ConfigureAwait(false);

				if (result == null)
				{
					throw new DatabaseQueryException(
						"AddOrUpdateLoginServer",
						"Failed to retrieve server data after upsert.",
						"UPSERT returned no result.",
						false,
						null,
						null);
				}

				return MapEntityToDto(result);
			}, "AddOrUpdateLoginServer", cancellationToken).ConfigureAwait(false);
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

			var result = await ExecuteRawSqlAsync(
				$@"UPDATE {TableName} 
				SET lastpulse = CURRENT_TIMESTAMP 
				WHERE id = {{0}}",
				"PulseLoginServer",
				new object[] { serverId },
				entityName: "LoginServer",
				entityId: serverId.ToString(),
				requireRowsAffected: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);
			
			return result.IsSuccess 
				? DatabaseResult.Success() 
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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

			var result = await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE id = {{0}}",
				"DeleteLoginServer",
				new object[] { serverId },
				entityName: "LoginServer",
				entityId: serverId.ToString(),
				requireRowsAffected: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);
			
			return result.IsSuccess 
				? DatabaseResult.Success() 
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				var server = await dbContext.LoginServers
					.AsNoTracking()
					.FirstOrDefaultAsync(s => s.ID == serverId, ct).ConfigureAwait(false);

				if (server == null)
				{
					throw new DatabaseEntityNotFoundException("LoginServer", serverId.ToString());
				}

				return MapEntityToDto(server);
			}, "GetLoginServer", cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps LoginServerEntity to LoginServerData DTO.
		/// </summary>
		/// <param name="entity">Login server entity from database.</param>
		/// <returns>Login server data DTO.</returns>
		private LoginServerData MapEntityToDto(LoginServerEntity entity)
		{
			return new LoginServerData(
				id: entity.ID,
				name: entity.Name,
				lastPulse: entity.LastPulse,
				address: entity.Address,
				port: entity.Port
			);
		}
	}
}