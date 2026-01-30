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

		/// <summary>
		/// Compiled query for retrieving a login server by ID with tracking.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<LoginServerEntity?>> getByIdTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long serverId, CancellationToken ct) =>
				context.LoginServers
					.FirstOrDefault(s => s.ID == serverId));

		/// <summary>
		/// Compiled query for retrieving a login server by unique name with tracking.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<LoginServerEntity?>> getByNameTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string name, CancellationToken ct) =>
				context.LoginServers
					.FirstOrDefault(s => s.Name == name));
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
					"Server name and address must not be empty.",
					isTransient: false);
			}

			// Fast path: attempt insert first; on unique violation, fall back to update.
			var insertResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var entity = new LoginServerEntity
				{
					Name = name,
					Address = address,
					Port = port,
					TimeCreated = DateTime.UtcNow,
					LastPulse = DateTime.UtcNow
				};

				await dbContext.LoginServers.AddAsync(entity, cancellationToken).ConfigureAwait(false);
				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				return MapEntityToDto(entity);
			}).ConfigureAwait(false);

			if (insertResult.IsSuccess)
			{
				return insertResult;
			}

			// If another writer inserted concurrently, retry as update.
			if (string.Equals(insertResult.ErrorCode, "UNIQUE_VIOLATION", StringComparison.Ordinal))
			{
				return await ExecuteTransactionAsync(async dbContext =>
				{
					var existing = await getByNameTrackingQuery(dbContext, name, cancellationToken).ConfigureAwait(false);
					if (existing == null)
					{
						throw new DatabaseEntityNotFoundException("LoginServer", name);
					}

					existing.Address = address;
					existing.Port = port;
					existing.LastPulse = DateTime.UtcNow;

					await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
					return MapEntityToDto(existing);
				}).ConfigureAwait(false);
			}

			return insertResult;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Server ID must be greater than 0.",
					isTransient: false);
			}

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var server = await getByIdTrackingQuery(dbContext, serverId, cancellationToken).ConfigureAwait(false);
				if (server == null)
				{
					throw new DatabaseEntityNotFoundException("LoginServer", serverId.ToString());
				}

				server.LastPulse = DateTime.UtcNow;
			}).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Server ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var server = await getByIdTrackingQuery(dbContext, serverId, cancellationToken).ConfigureAwait(false);
				if (server == null)
				{
					return;
				}

				dbContext.LoginServers.Remove(server);
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<LoginServerData>> GetServerAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<LoginServerData>.Failure(
					"VALIDATION_ERROR",
					"Server ID must be greater than 0.",
					isTransient: false);
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