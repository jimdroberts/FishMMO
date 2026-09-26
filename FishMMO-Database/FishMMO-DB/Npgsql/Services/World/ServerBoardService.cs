using System;
using System.Collections.Generic;
using System.Data.Common;
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
	/// Every read is unfiltered by pulse age — see the interface. The board's server rows are raw
	/// SQL so their ages are taken by the database clock (<see cref="TierSql"/>); the rest is EF.
	/// </remarks>
	public sealed class ServerBoardService : BaseService<WorldServerEntity>, IServerBoardService
	{
		/// <summary>The most scene instances one page will return.</summary>
		private const int MaxPageSize = 200;

		/// <summary>
		/// The database's UTC wall time: the clock that stamps every server's <c>last_pulse</c> and
		/// the clock every server counts its shutdown down against.
		/// </summary>
		/// <remarks>
		/// <c>now()</c> rather than <c>clock_timestamp()</c>: the board is read in one REPEATABLE
		/// READ transaction so the three tiers are one snapshot, and <c>now()</c> is that
		/// transaction's start, so every age on the board is measured against the same instant as
		/// well; for the single-statement pulse-age read it is simply that statement's start.
		/// <c>AT TIME ZONE 'UTC'</c> because the columns hold UTC without a zone.
		/// </remarks>
		private const string DatabaseUtcNowSql = "(now() AT TIME ZONE 'UTC')";

		public ServerBoardService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <summary>
		/// One tier's rows, laid out as <see cref="ReadServerRow"/> maps them, with both ages taken
		/// by the database clock.
		/// </summary>
		/// <param name="table">The tier's table, from the model.</param>
		/// <param name="controlled">
		/// False for the login tier, which has no players and no control columns: those come back
		/// as zero, false and null, which is what <see cref="ServerAdminData"/> says they are.
		/// </param>
		/// <remarks>
		/// The pulse age is clamped at zero. <c>now()</c> is when this transaction began and its
		/// snapshot is taken at its first statement, so a pulse that committed in between is
		/// visible and a few milliseconds newer than the instant it is measured against. The time
		/// to the shutdown is not clamped: negative is a deadline the row still carries after it
		/// passed. Ordered by name, as the board lists them.
		/// </remarks>
		private static string TierSql(string table, bool controlled)
		{
			string control = controlled
				? "s.character_count, s.locked, s.shutdown_at_utc, " +
					$"EXTRACT(EPOCH FROM (s.shutdown_at_utc - {DatabaseUtcNowSql}))::double precision"
				: "0, false, NULL::timestamp, NULL::double precision";

			return $@"SELECT s.id, s.name, s.address, s.port, s.last_pulse, s.time_created,
					GREATEST(0.0, EXTRACT(EPOCH FROM ({DatabaseUtcNowSql} - s.last_pulse))::double precision),
					{control}
				FROM {table} AS s
				ORDER BY s.name";
		}

		/// <summary>Maps one row laid out as <see cref="TierSql"/>.</summary>
		private static ServerAdminData ReadServerRow(DbDataReader reader, string kind)
		{
			return new ServerAdminData
			{
				Kind = kind,
				ID = reader.GetInt64(0),
				Name = reader.GetString(1),
				Address = reader.GetString(2),
				Port = reader.GetInt32(3),
				LastPulse = DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc),
				TimeCreated = DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc),
				PulseAgeSeconds = reader.IsDBNull(6) ? 0.0 : reader.GetDouble(6),
				CharacterCount = reader.GetInt32(7),
				Locked = reader.GetBoolean(8),
				ShutdownAtUtc = reader.IsDBNull(9) ? (DateTime?)null : DateTime.SpecifyKind(reader.GetDateTime(9), DateTimeKind.Utc),
				ShutdownInSeconds = reader.IsDBNull(10) ? (double?)null : reader.GetDouble(10),
			};
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<double?>> FetchPulseAgeAsync(
			string kind,
			string serverName,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(serverName))
			{
				return DatabaseResult<double?>.Failure(DatabaseErrorCodes.ValidationError, "A server name is required.");
			}

			string name = serverName.Trim();
			string tier = (kind ?? string.Empty).Trim().ToLowerInvariant();

			return await ExecuteReadAsync(async dbContext =>
			{
				string table;
				switch (tier)
				{
					case "login":
						table = dbContext.GetTableName<LoginServerEntity>();
						break;
					case "world":
						table = dbContext.GetTableName<WorldServerEntity>();
						break;
					case "scene":
						table = dbContext.GetTableName<SceneServerEntity>();
						break;
					default:
						throw new DatabaseException(
							$"'{kind}' is not a server tier. Use login, world or scene.",
							errorCode: DatabaseErrorCodes.ValidationError);
				}

				/* An exact match on the name the server registered under, which comes from its
				 * own ServerName configuration. Matching on address or port instead would be
				 * ambiguous: the rows are global and two hosts can share a port number.
				 *
				 * The age is taken here, by the clock that stamped the pulse, and clamped at zero
				 * as the board's is. See the interface for why the caller's clock must not. */
				var ages = await ReadRowsAsync(
					dbContext,
					$@"SELECT GREATEST(0.0, EXTRACT(EPOCH FROM ({DatabaseUtcNowSql} - s.last_pulse))::double precision)
						FROM {table} AS s
						WHERE s.name = {{0}}",
					new object[] { name },
					reader => reader.IsDBNull(0) ? 0.0 : reader.GetDouble(0),
					cancellationToken).ConfigureAwait(false);

				return ages.Count == 0 ? (double?)null : ages[0];
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ServerBoardData>> FetchBoardAsync(CancellationToken cancellationToken = default)
		{
			return await ExecuteReadAsync(async dbContext =>
			{
				/* Four reads in one REPEATABLE READ, read-only transaction, so all four see one
				 * snapshot: otherwise the three tiers are read at different instants and a world
				 * server can appear dead beside a scene server already reporting it alive. One
				 * context alone did not do it — this said it did (issue #267) — because under the
				 * default READ COMMITTED every statement takes its own snapshot. */
				await using var snapshot = await dbContext.Database
					.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken)
					.ConfigureAwait(false);
				await dbContext.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken).ConfigureAwait(false);

				/* Raw SQL so every age on the board is measured by the database clock, inside the
				 * statement that reads the stamp. See ServerAdminData and TierSql. */
				var login = await ReadRowsAsync(
					dbContext,
					TierSql(dbContext.GetTableName<LoginServerEntity>(), controlled: false),
					Array.Empty<object>(),
					reader => ReadServerRow(reader, "login"),
					cancellationToken).ConfigureAwait(false);

				var world = await ReadRowsAsync(
					dbContext,
					TierSql(dbContext.GetTableName<WorldServerEntity>(), controlled: true),
					Array.Empty<object>(),
					reader => ReadServerRow(reader, "world"),
					cancellationToken).ConfigureAwait(false);

				var scene = await ReadRowsAsync(
					dbContext,
					TierSql(dbContext.GetTableName<SceneServerEntity>(), controlled: true),
					Array.Empty<object>(),
					reader => ReadServerRow(reader, "scene"),
					cancellationToken).ConfigureAwait(false);

				int instances = await dbContext.Scenes.AsNoTracking().CountAsync(cancellationToken).ConfigureAwait(false);
				await snapshot.CommitAsync(cancellationToken).ConfigureAwait(false);

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
