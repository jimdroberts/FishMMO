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

			// Insert-first strategy to avoid the race condition where two writers both observe
			// a missing row and attempt to insert simultaneously.
			var insertNow = DateTime.UtcNow;
			var insertResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var entity = new SceneServerEntity
				{
					Name = name,
					TimeCreated = insertNow,
					LastPulse = insertNow,
					Address = address,
					Port = port,
					CharacterCount = characterCount,
					Locked = locked,
				};

				await dbContext.SceneServers.AddAsync(entity, cancellationToken).ConfigureAwait(false);
				return entity;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (insertResult.IsSuccess)
			{
				if (insertResult.Data.ID <= 0)
				{
					return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Failure(
						"DATABASE_ERROR",
						"Failed to insert scene server.",
						isTransient: true);
				}

				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Success(
					(insertResult.Data.ID, MapEntityToDto(insertResult.Data)));
			}

			if (!string.Equals(insertResult.ErrorCode, "UNIQUE_VIOLATION", StringComparison.Ordinal))
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Failure(
					insertResult.ErrorCode,
					insertResult.ErrorMessage,
					insertResult.IsTransient);
			}

			var updateNow = DateTime.UtcNow;
			var updateResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var existing = await getByNameTrackingQuery(dbContext, name, cancellationToken).ConfigureAwait(false);
				if (existing == null)
				{
					throw new DatabaseEntityNotFoundException("SceneServer", name);
				}

				existing.Address = address;
				existing.Port = port;
				existing.CharacterCount = characterCount;
				existing.Locked = locked;
				existing.LastPulse = updateNow;
				return existing;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!updateResult.IsSuccess)
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Failure(
					updateResult.ErrorCode,
					updateResult.ErrorMessage,
					updateResult.IsTransient);
			}

			if (updateResult.Data.ID <= 0)
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Failure(
					"DATABASE_ERROR",
					"Failed to update scene server.",
					isTransient: true);
			}

			return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Success(
				(updateResult.Data.ID, MapEntityToDto(updateResult.Data)));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long serverId, int characterCount, bool locked, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure("INVALID_SERVER_ID", "Server ID must be greater than zero.");
			}

			var now = DateTime.UtcNow;
			var result = await ExecuteTransactionAsync(async dbContext =>
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

			var result = await ExecuteTransactionAsync(async dbContext =>
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