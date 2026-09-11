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
	/// <summary>
	/// Reads every server row for the operator board.
	/// </summary>
	/// <remarks>
	/// EF throughout, and every read is unfiltered by pulse age — see the interface.
	/// </remarks>
	public sealed class ServerBoardService : BaseService<WorldServerEntity>, IServerBoardService
	{
		/// <summary>The most scene instances one page will return.</summary>
		private const int MaxPageSize = 200;

		public ServerBoardService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<DateTime?>> FetchLastPulseAsync(
			string kind,
			string serverName,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(serverName))
			{
				return DatabaseResult<DateTime?>.Failure(DatabaseErrorCodes.ValidationError, "A server name is required.");
			}

			string name = serverName.Trim();
			string tier = (kind ?? string.Empty).Trim().ToLowerInvariant();

			return await ExecuteReadAsync(async dbContext =>
			{
				// An exact match on the name the server registered under, which comes from its
				// own ServerName configuration. Matching on address or port instead would be
				// ambiguous: the rows are global and two hosts can share a port number.
				switch (tier)
				{
					case "login":
						return await dbContext.LoginServers.AsNoTracking()
							.Where(s => s.Name == name)
							.Select(s => (DateTime?)s.LastPulse)
							.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

					case "world":
						return await dbContext.WorldServers.AsNoTracking()
							.Where(s => s.Name == name)
							.Select(s => (DateTime?)s.LastPulse)
							.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

					case "scene":
						return await dbContext.SceneServers.AsNoTracking()
							.Where(s => s.Name == name)
							.Select(s => (DateTime?)s.LastPulse)
							.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

					default:
						throw new DatabaseException(
							$"'{kind}' is not a server tier. Use login, world or scene.",
							errorCode: DatabaseErrorCodes.ValidationError);
				}
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ServerBoardData>> FetchBoardAsync(CancellationToken cancellationToken = default)
		{
			return await ExecuteReadAsync(async dbContext =>
			{
				/* Four reads on one context and one connection. The board is polled on a timer,
				 * and separate round trips would also let the three tiers be read at three
				 * different instants — so a world server could appear dead next to a scene
				 * server that was already reporting it alive. */
				var login = await dbContext.LoginServers
					.AsNoTracking()
					.OrderBy(s => s.Name)
					.Select(s => new ServerAdminData
					{
						Kind = "login",
						ID = s.ID,
						Name = s.Name,
						Address = s.Address,
						Port = s.Port,
						LastPulse = s.LastPulse,
						TimeCreated = s.TimeCreated,
						// A login server carries no players and has no lock: see the data type.
						CharacterCount = 0,
						Locked = false,
						ShutdownAtUtc = null,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var world = await dbContext.WorldServers
					.AsNoTracking()
					.OrderBy(s => s.Name)
					.Select(s => new ServerAdminData
					{
						Kind = "world",
						ID = s.ID,
						Name = s.Name,
						Address = s.Address,
						Port = s.Port,
						LastPulse = s.LastPulse,
						TimeCreated = s.TimeCreated,
						CharacterCount = s.CharacterCount,
						Locked = s.Locked,
						ShutdownAtUtc = s.ShutdownAtUtc,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var scene = await dbContext.SceneServers
					.AsNoTracking()
					.OrderBy(s => s.Name)
					.Select(s => new ServerAdminData
					{
						Kind = "scene",
						ID = s.ID,
						Name = s.Name,
						Address = s.Address,
						Port = s.Port,
						LastPulse = s.LastPulse,
						TimeCreated = s.TimeCreated,
						CharacterCount = s.CharacterCount,
						Locked = s.Locked,
						ShutdownAtUtc = s.ShutdownAtUtc,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				int instances = await dbContext.Scenes.AsNoTracking().CountAsync(cancellationToken).ConfigureAwait(false);

				return new ServerBoardData
				{
					LoginServers = login,
					WorldServers = world,
					SceneServers = scene,
					SceneInstanceCount = instances,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneInstancePage>> FetchScenesAsync(
			int? status,
			long? sceneServerId,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default)
		{
			if (page < 1)
			{
				page = 1;
			}
			if (pageSize < 1)
			{
				pageSize = 50;
			}
			if (pageSize > MaxPageSize)
			{
				pageSize = MaxPageSize;
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<SceneEntity> q = dbContext.Scenes.AsNoTracking();

				if (status.HasValue)
				{
					// SceneStatus is stored as an int on the entity, not the enum.
					int wanted = status.Value;
					q = q.Where(s => s.SceneStatus == wanted);
				}
				if (sceneServerId.HasValue)
				{
					long id = sceneServerId.Value;
					q = q.Where(s => s.SceneServerID == id);
				}

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				var rows = await q
					// Newest first: a stuck instance is usually a recent one, and the operator
					// looking at this page is looking for something that just went wrong.
					.OrderByDescending(s => s.TimeCreated)
					.ThenByDescending(s => s.ID)
					.Skip((page - 1) * pageSize)
					.Take(pageSize)
					.Select(s => new SceneInstanceAdminData
					{
						ID = s.ID,
						SceneServerID = s.SceneServerID,
						WorldServerID = s.WorldServerID,
						SceneName = s.SceneName,
						SceneHandle = s.SceneHandle,
						Status = s.SceneStatus,
						Type = s.SceneType,
						CharacterCount = s.CharacterCount,
						CharacterID = s.CharacterID,
						PartyID = s.PartyID,
						IsPrivate = s.IsPrivate,
						TimeCreated = s.TimeCreated,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return new SceneInstancePage
				{
					Items = rows,
					Page = page,
					PageSize = pageSize,
					TotalCount = total,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
