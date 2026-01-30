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
	/// <inheritdoc/>
	public sealed class SceneServerService : BaseService<SceneServerEntity>, ISceneServerService
	{
		/// <summary>
		/// Compiled query for retrieving a tracked server by name.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<SceneServerEntity?>> getByNameTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string name, CancellationToken ct) =>
				context.SceneServers.FirstOrDefault(s => s.Name == name));
#pragma warning restore CS8619

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

			var now = DateTime.UtcNow;
			var result = await ExecuteMirrorAsync(async dbContext =>
			{
				var server = await getByNameTrackingQuery(dbContext, name, cancellationToken).ConfigureAwait(false);
				if (server == null)
				{
					server = new SceneServerEntity
					{
						Name = name,
						TimeCreated = now,
						LastPulse = now,
						Address = address,
						Port = port,
						CharacterCount = characterCount,
						Locked = locked,
					};
					await dbContext.SceneServers.AddAsync(server, cancellationToken).ConfigureAwait(false);
					return server;
				}

				server.Address = address;
				server.Port = port;
				server.CharacterCount = characterCount;
				server.Locked = locked;
				server.LastPulse = now;
				return server;
			}).ConfigureAwait(false);

			if (!result.IsSuccess)
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			if (result.Data.ID <= 0)
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Failure("DATABASE_ERROR", "Failed to upsert scene server.", isTransient: true);
			}

			var serverData = MapEntityToDto(result.Data);
			return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Success((result.Data.ID, serverData));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long serverId, int characterCount, bool locked, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than zero.");
			}

			var now = DateTime.UtcNow;
			var result = await ExecuteMirrorAsync(async dbContext =>
			{
				var server = await dbContext.SceneServers
					.FirstOrDefaultAsync(s => s.ID == serverId, cancellationToken)
					.ConfigureAwait(false);
				if (server == null)
				{
					throw new DatabaseEntityNotFoundException("SceneServer", serverId.ToString());
				}
				server.LastPulse = now;
				server.CharacterCount = characterCount;
				server.Locked = locked;
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than zero.");
			}

			var result = await ExecuteMirrorAsync(async dbContext =>
			{
				var server = await dbContext.SceneServers
					.FirstOrDefaultAsync(s => s.ID == serverId, cancellationToken)
					.ConfigureAwait(false);
				if (server == null)
				{
					return;
				}
				dbContext.SceneServers.Remove(server);
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneServerData>> GetServerAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<SceneServerData>.Failure("INVALID_SERVER_ID", "Server ID must be greater than zero.");
			}

			var result = await ExecuteMirrorAsync(async dbContext =>
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
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<SceneServerData>.Success(result.Data)
				: DatabaseResult<SceneServerData>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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