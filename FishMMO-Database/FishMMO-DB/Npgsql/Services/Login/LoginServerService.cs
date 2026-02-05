using System;
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
	/// <summary>
	/// Login server registration and management service.
	/// Uses EF Core compiled queries for hot paths and the BaseService execution strategy for retries.
	/// </summary>
	public sealed class LoginServerService : BaseService<LoginServerEntity>, ILoginServerService
	{
		/// <summary>
		/// Compiled query for retrieving a login server by ID without tracking.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<LoginServerEntity?>> getByIdNoTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long serverId, CancellationToken ct) =>
				context.LoginServers
					.AsNoTracking()
					.FirstOrDefault(s => s.ID == serverId));

#pragma warning restore CS8619

		/// <summary>
		/// Initializes a new instance of LoginServerService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public LoginServerService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<LoginServerData>> PersistAsync(
			string name,
			string address,
			ushort port,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
			{
				return DatabaseResult<LoginServerData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Server name and address must not be empty.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"INSERT INTO {TableName} (name, address, port)
					VALUES ({{0}}, {{1}}, {{2}})
					ON CONFLICT (name)
					DO UPDATE SET
						address = EXCLUDED.address,
						port = EXCLUDED.port,
						last_pulse = CURRENT_TIMESTAMP
					RETURNING id, name, time_created, last_pulse, address, port";

				return await dbContext.LoginServers
					.FromSqlRaw(sql, name, address, port)
					.AsNoTracking()
					.FirstAsync(cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<LoginServerData>.Success(MapEntityToDto(result.Data))
				: DatabaseResult<LoginServerData>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Server ID must be greater than 0.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET last_pulse = {{0}}
					WHERE id = {{1}}";

				var affected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { DateTime.UtcNow, serverId },
					cancellationToken)
					.ConfigureAwait(false);

				if (affected == 0)
				{
					throw new DatabaseEntityNotFoundException("LoginServer", serverId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Server ID must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$"DELETE FROM {TableName} WHERE id = {{0}}",
					new object[] { serverId },
					cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("LoginServer", serverId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<LoginServerData>> FetchAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<LoginServerData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Server ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var server = await getByIdNoTrackingQuery(dbContext, serverId, cancellationToken).ConfigureAwait(false);
				if (server == null)
				{
					throw new DatabaseEntityNotFoundException("LoginServer", serverId.ToString());
				}

				return MapEntityToDto(server);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps LoginServerEntity to LoginServerData DTO.
		/// </summary>
		/// <param name="entity">Login server entity from database.</param>
		/// <returns>Login server data DTO.</returns>
		private static LoginServerData MapEntityToDto(LoginServerEntity entity)
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