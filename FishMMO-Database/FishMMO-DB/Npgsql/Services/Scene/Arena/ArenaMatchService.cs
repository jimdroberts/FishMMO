using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service for arena matches. See <see cref="IArenaMatchService"/>.
	/// </summary>
	public sealed class ArenaMatchService : BaseService<ArenaMatchEntity>, IArenaMatchService
	{
		private const int MaxBatchIds = 1024;

		/// <summary>
		/// Initializes a new instance of ArenaMatchService.
		/// </summary>
		public ArenaMatchService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ArenaMatchData?>> FetchByInstanceAsync(long instanceId, CancellationToken cancellationToken = default)
		{
			if (instanceId <= 0)
			{
				return DatabaseResult<ArenaMatchData?>.Failure(DatabaseErrorCodes.ValidationError, "Instance ID must be greater than zero.");
			}

			var result = await ExecuteReadAsync<ArenaMatchData?>(async dbContext =>
			{
				var sql = $@"SELECT * FROM {TableName} WHERE instance_id = {{0}} LIMIT 1";
				var rows = await dbContext.ArenaMatches
					.FromSqlRaw(sql, instanceId)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return rows.Count > 0 ? MapMatch(rows[0]) : (ArenaMatchData?)null;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ArenaMatchData?>> FetchAsync(long matchId, CancellationToken cancellationToken = default)
		{
			if (matchId <= 0)
			{
				return DatabaseResult<ArenaMatchData?>.Failure(DatabaseErrorCodes.ValidationError, "Match ID must be greater than zero.");
			}

			var result = await ExecuteReadAsync<ArenaMatchData?>(async dbContext =>
			{
				var sql = $@"SELECT * FROM {TableName} WHERE id = {{0}} LIMIT 1";
				var rows = await dbContext.ArenaMatches
					.FromSqlRaw(sql, matchId)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return rows.Count > 0 ? MapMatch(rows[0]) : (ArenaMatchData?)null;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<ArenaMatchMemberData>>> FetchMembersAsync(long matchId, CancellationToken cancellationToken = default)
		{
			if (matchId <= 0)
			{
				return DatabaseResult<IReadOnlyList<ArenaMatchMemberData>>.Failure(DatabaseErrorCodes.ValidationError, "Match ID must be greater than zero.");
			}

			var result = await ExecuteReadAsync<IReadOnlyList<ArenaMatchMemberData>>(async dbContext =>
			{
				string memberTable = dbContext.GetTableName<ArenaMatchMemberEntity>();
				var sql = $@"SELECT * FROM {memberTable} WHERE match_id = {{0}} ORDER BY team, id";
				var rows = await dbContext.ArenaMatchMembers
					.FromSqlRaw(sql, matchId)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return rows.Select(MapMember).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> UpdateStatusAsync(long matchId, ArenaMatchStatus status, int winnerTeam = -1, CancellationToken cancellationToken = default)
		{
			if (matchId <= 0)
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "Match ID must be greater than zero.");
			}

			var now = DateTime.UtcNow;

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* Forward only. Started is stamped on the move to Live and never overwritten;
				 * ended is stamped on Ended or Cancelled. A status at or below the current one is
				 * refused by the WHERE, so a straggling write cannot reopen a finished match. */
				var sql = $@"UPDATE {TableName}
					SET status = {{1}},
						winner_team = CASE WHEN {{1}} = {{3}} THEN {{2}} ELSE winner_team END,
						time_started = CASE WHEN {{1}} = {{4}} AND time_started IS NULL THEN {{5}} ELSE time_started END,
						time_ended = CASE WHEN {{1}} >= {{3}} AND time_ended IS NULL THEN {{5}} ELSE time_ended END
					WHERE id = {{0}} AND status < {{1}}";

				int affected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { matchId, (int)status, winnerTeam, (int)ArenaMatchStatus.Ended, (int)ArenaMatchStatus.Live, now },
					cancellationToken).ConfigureAwait(false);

				return affected > 0;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> UpdateMemberTalliesAsync(long matchId, IReadOnlyList<(long characterId, int kills, int deaths, int score)> tallies, CancellationToken cancellationToken = default)
		{
			if (matchId <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Match ID must be greater than zero.");
			}

			if (tallies == null || tallies.Count == 0)
			{
				return DatabaseResult<int>.Success(0);
			}

			int count = Math.Min(tallies.Count, MaxBatchIds);
			var characters = new long[count];
			var kills = new int[count];
			var deaths = new int[count];
			var scores = new int[count];
			for (int i = 0; i < count; ++i)
			{
				characters[i] = tallies[i].characterId;
				kills[i] = tallies[i].kills;
				deaths[i] = tallies[i].deaths;
				scores[i] = tallies[i].score;
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				string memberTable = dbContext.GetTableName<ArenaMatchMemberEntity>();
				var sql = $@"UPDATE {memberTable} AS m
					SET kills = v.kills, deaths = v.deaths, score = v.score
					FROM UNNEST({{1}}, {{2}}, {{3}}, {{4}}) AS v(character_id, kills, deaths, score)
					WHERE m.match_id = {{0}} AND m.character_id = v.character_id";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { matchId, characters, kills, deaths, scores },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<long>>> FetchCharactersInLiveMatchesAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default)
		{
			long[] ids = Distinct(characterIds);
			if (ids.Length == 0)
			{
				return DatabaseResult<IReadOnlyList<long>>.Success(Array.Empty<long>());
			}

			var result = await ExecuteReadAsync<IReadOnlyList<long>>(async dbContext =>
			{
				string memberTable = dbContext.GetTableName<ArenaMatchMemberEntity>();
				var sql = $@"SELECT DISTINCT m.character_id AS value
					FROM {memberTable} m
					JOIN {TableName} am ON am.id = m.match_id
					WHERE m.character_id = ANY({{0}}) AND am.status < {{1}}";

				var rows = await dbContext.SqlLongValues
					.FromSqlRaw(sql, ids, (int)ArenaMatchStatus.Ended)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return rows.Select(r => r.Value).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> CancelAbandonedAsync(DateTime createdBeforeUtc, int maxRows = 64, CancellationToken cancellationToken = default)
		{
			if (maxRows < 1)
			{
				maxRows = 1;
			}

			var now = DateTime.UtcNow;

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				string sceneTable = dbContext.GetTableName<SceneEntity>();

				/* Not ended long after it formed, with no usable instance row left to play in: the
				 * load failed, or the host died and its rows were reaped. Nothing will ever finish
				 * it, and every seat is a character locked out of both finders. */
				var sql = $@"UPDATE {TableName}
					SET status = {{1}}, time_ended = {{2}}
					WHERE id IN (
						SELECT am.id FROM {TableName} am
						WHERE am.status < {{0}}
							AND am.time_created < {{3}}
							AND NOT EXISTS (SELECT 1 FROM {sceneTable} s WHERE s.id = am.instance_id AND s.scene_status IN ({{4}}, {{5}}, {{6}}))
						ORDER BY am.time_created
						LIMIT {{7}}
					)";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[]
					{
						(int)ArenaMatchStatus.Ended,
						(int)ArenaMatchStatus.Cancelled,
						now,
						createdBeforeUtc,
						(int)SceneStatus.Pending,
						(int)SceneStatus.Loading,
						(int)SceneStatus.Ready,
						maxRows,
					},
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		private static long[] Distinct(IReadOnlyList<long> ids)
		{
			if (ids == null || ids.Count == 0)
			{
				return Array.Empty<long>();
			}

			var seen = new HashSet<long>();
			var result = new List<long>(Math.Min(ids.Count, MaxBatchIds));
			for (int i = 0; i < ids.Count && result.Count < MaxBatchIds; ++i)
			{
				if (ids[i] > 0 && seen.Add(ids[i]))
				{
					result.Add(ids[i]);
				}
			}
			return result.ToArray();
		}

		private static ArenaMatchData MapMatch(ArenaMatchEntity e)
		{
			return new ArenaMatchData(e.ID, e.WorldServerID, e.InstanceID, e.SceneName, e.TemplateID, e.Format, e.TeamCount, e.TeamSize, e.Status, e.WinnerTeam, e.TimeCreated, e.TimeStarted, e.TimeEnded);
		}

		private static ArenaMatchMemberData MapMember(ArenaMatchMemberEntity e)
		{
			return new ArenaMatchMemberData(e.ID, e.MatchID, e.CharacterID, e.Team, e.Kills, e.Deaths, e.Score);
		}
	}
}
