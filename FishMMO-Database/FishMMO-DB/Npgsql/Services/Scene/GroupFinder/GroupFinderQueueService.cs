using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service for the dungeon group finder's queue. See <see cref="IGroupFinderQueueService"/>.
	/// </summary>
	/// <remarks>
	/// Every state change here is a single statement, or a single transaction, whose
	/// <c>WHERE</c> re-asserts the state it expects. The queue is worked by every scene server on
	/// the world at once, and that is what makes two of them touching the same row produce one
	/// winner and one no-op.
	/// </remarks>
	public sealed class GroupFinderQueueService : BaseService<GroupFinderQueueEntity>, IGroupFinderQueueService
	{
		/// <summary>
		/// Ceiling on ids accepted by the batched methods, so a caller cannot build a statement
		/// with an unbounded parameter list.
		/// </summary>
		private const int MaxBatchIds = 1024;

		/// <summary>
		/// Ceiling on the group size one call may form. Well above any party cap; this bounds the
		/// row lock and the statement, not the game.
		/// </summary>
		private const int MaxGroupSize = 64;

		/// <summary>
		/// The shared <c>FishMMO.Shared.SceneType.Group</c> value: a dungeon instance.
		/// </summary>
		/// <remarks>
		/// Numeric on purpose. Callers pass the shared enum cast to <c>int</c>, and the
		/// database-side <see cref="SceneType"/> enum's member names do not correspond to those
		/// values, so naming them here would read as the wrong thing.
		/// </remarks>
		private const int GroupSceneType = 2;

		/// <summary>The shared <c>FishMMO.Shared.SceneType.PvP</c> value: an arena instance.</summary>
		private const int PvPSceneType = 3;

		/// <summary>
		/// Initializes a new instance of GroupFinderQueueService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		public GroupFinderQueueService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> EnqueueAsync(long worldServerId, long characterId, SceneType sceneType, string sceneName, int difficulty, DateTime stalePulsedBeforeUtc, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || characterId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID, character ID and scene name are required.");
			}

			if (difficulty < 0)
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Difficulty index cannot be negative.");
			}

			var now = DateTime.UtcNow;

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* An upsert keyed by character. The conflict branch re-points an existing WAITING
				 * row — and restarts its clock, because it is a new request for a new thing — but
				 * leaves a live matched row untouched: the WHERE makes the update a no-op,
				 * RETURNING then yields nothing, and the caller sees 0. A matched row whose
				 * heartbeat has stopped is not live — its server died before the transfer — and is
				 * re-pointed like a waiting one, so that character is not locked out of the finder
				 * until the stale sweep happens to reach them.
				 *
				 * The table qualifier in the WHERE is required, not stylistic: inside ON CONFLICT DO
				 * UPDATE a bare column name is ambiguous between the existing and proposed rows,
				 * and Postgres refuses it. */
				return await UpsertRowAsync(dbContext, worldServerId, characterId, sceneType, sceneName, difficulty, 0, now, stalePulsedBeforeUtc, cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <summary>
		/// The single-row upsert both enqueue paths use. Returns the row id, or 0 when a live
		/// matched row refused the re-point.
		/// </summary>
		private async Task<long> UpsertRowAsync(NpgsqlDbContext dbContext, long worldServerId, long characterId, SceneType sceneType, string sceneName, int difficulty, long groupId, DateTime now, DateTime stalePulsedBeforeUtc, CancellationToken cancellationToken)
		{
			var sql = $@"INSERT INTO {TableName}
					(world_server_id, character_id, scene_type, group_id, scene_name, difficulty, status, party_id, instance_id, time_created, last_pulse, time_matched)
				VALUES ({{0}}, {{1}}, {{7}}, {{8}}, {{2}}, {{3}}, {{4}}, 0, 0, {{5}}, {{5}}, NULL)
				ON CONFLICT (character_id) DO UPDATE
				SET world_server_id = EXCLUDED.world_server_id,
					scene_type = EXCLUDED.scene_type,
					group_id = EXCLUDED.group_id,
					scene_name = EXCLUDED.scene_name,
					difficulty = EXCLUDED.difficulty,
					time_created = EXCLUDED.time_created,
					last_pulse = EXCLUDED.last_pulse,
					status = {{4}},
					party_id = 0,
					instance_id = 0,
					time_matched = NULL
				WHERE {TableName}.status = {{4}} OR {TableName}.last_pulse < {{6}}
				RETURNING id";

			return await ExecuteReturningOrDefaultAsync(
				dbContext,
				sql,
				new object[] { worldServerId, characterId, sceneName, difficulty, (int)GroupFinderQueueStatus.Waiting, now, stalePulsedBeforeUtc, (int)sceneType, groupId },
				reader => reader.GetInt64(0),
				cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> EnqueueGroupAsync(long worldServerId, SceneType sceneType, string sceneName, int difficulty, long groupId, IReadOnlyList<long> characterIds, DateTime stalePulsedBeforeUtc, CancellationToken cancellationToken = default)
		{
			long[] ids = Distinct(characterIds);
			if (worldServerId <= 0 || groupId <= 0 || ids.Length == 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID, group ID, members and scene name are required.");
			}

			if (difficulty < 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Format index cannot be negative.");
			}

			var now = DateTime.UtcNow;

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				int written = 0;
				foreach (long characterId in ids)
				{
					long rowId = await UpsertRowAsync(dbContext, worldServerId, characterId, sceneType, sceneName, difficulty, groupId, now, stalePulsedBeforeUtc, cancellationToken).ConfigureAwait(false);
					if (rowId <= 0)
					{
						/* A member is live-matched elsewhere. The group queues together or not at
						 * all; throwing rolls back the members already written. */
						throw new DatabaseException(
							$"Group finder could not queue group {groupId}: character {characterId} is already matched.",
							errorCode: DatabaseErrorCodes.StaleState);
					}
					++written;
				}
				return written;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> DeleteAsync(long characterId, bool onlyIfWaiting, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than zero.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = onlyIfWaiting
					? $@"DELETE FROM {TableName} WHERE character_id = {{0}} AND status = {{1}}"
					: $@"DELETE FROM {TableName} WHERE character_id = {{0}}";

				int affected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { characterId, (int)GroupFinderQueueStatus.Waiting },
					cancellationToken).ConfigureAwait(false);

				return affected > 0;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<GroupFinderQueueData?>> DeleteReturningAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<GroupFinderQueueData?>.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than zero.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE character_id = {{0}}
					RETURNING id, world_server_id, character_id, scene_type, group_id, scene_name, difficulty, status, party_id, instance_id, time_created, last_pulse, time_matched";

				return await ExecuteReturningOrDefaultAsync(
					dbContext,
					sql,
					new object[] { characterId },
					reader => (GroupFinderQueueData?)new GroupFinderQueueData(
						reader.GetInt64(0),
						reader.GetInt64(1),
						reader.GetInt64(2),
						reader.GetInt32(3),
						reader.GetInt64(4),
						reader.GetString(5),
						reader.GetInt32(6),
						reader.GetInt32(7),
						reader.GetInt64(8),
						reader.GetInt64(9),
						reader.GetDateTime(10),
						reader.GetDateTime(11),
						reader.IsDBNull(12) ? (DateTime?)null : reader.GetDateTime(12)),
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> PulseAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default)
		{
			long[] ids = Distinct(characterIds);
			if (ids.Length == 0)
			{
				return DatabaseResult<int>.Success(0);
			}

			var now = DateTime.UtcNow;

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET last_pulse = {{0}} WHERE character_id = ANY({{1}})";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { now, ids },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<GroupFinderQueueData>>> FetchByCharactersAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default)
		{
			long[] ids = Distinct(characterIds);
			if (ids.Length == 0)
			{
				return DatabaseResult<IReadOnlyList<GroupFinderQueueData>>.Success(Array.Empty<GroupFinderQueueData>());
			}

			var result = await ExecuteReadAsync<IReadOnlyList<GroupFinderQueueData>>(async dbContext =>
			{
				var sql = $@"SELECT * FROM {TableName} WHERE character_id = ANY({{0}})";

				var rows = await dbContext.GroupFinderQueue
					.FromSqlRaw(sql, ids)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return rows.Select(MapEntityToDto).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> CountWaitingAsync(long worldServerId, SceneType sceneType, string sceneName, int difficulty, DateTime pulsedSinceUtc, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID and scene name are required.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var sql = $@"SELECT COUNT(*)::int FROM {TableName}
					WHERE world_server_id = {{0}}
						AND scene_type = {{5}}
						AND scene_name = {{1}}
						AND difficulty = {{2}}
						AND status = {{3}}
						AND last_pulse >= {{4}}";

				return await ExecuteScalarIntAsync(
					dbContext,
					sql,
					new object[] { worldServerId, sceneName, difficulty, (int)GroupFinderQueueStatus.Waiting, pulsedSinceUtc, (int)sceneType },
					cancellationToken).ConfigureAwait(false);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<GroupFinderMatchData>> TryFormGroupAsync(
			long worldServerId,
			string sceneName,
			int difficulty,
			int groupSize,
			DateTime pulsedSinceUtc,
			SceneType sceneType,
			byte leaderRank,
			byte memberRank,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<GroupFinderMatchData>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID and scene name are required.");
			}

			if (difficulty < 0)
			{
				return DatabaseResult<GroupFinderMatchData>.Failure(DatabaseErrorCodes.ValidationError, "Difficulty index cannot be negative.");
			}

			if (groupSize < 1 || groupSize > MaxGroupSize)
			{
				return DatabaseResult<GroupFinderMatchData>.Failure(DatabaseErrorCodes.ValidationError, $"Group size must be between 1 and {MaxGroupSize}.");
			}

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				string partyTable = dbContext.GetTableName<PartyEntity>();
				string membershipTable = dbContext.GetTableName<CharacterPartyEntity>();
				string characterTable = dbContext.GetTableName<CharacterEntity>();
				string sceneTable = dbContext.GetTableName<SceneEntity>();

				int waiting = (int)GroupFinderQueueStatus.Waiting;
				int matched = (int)GroupFinderQueueStatus.Matched;
				int pending = (int)SceneStatus.Pending;
				int loading = (int)SceneStatus.Loading;
				int ready = (int)SceneStatus.Ready;

				/* 1. Take the longest-waiting eligible players, and lock their rows.
				 *
				 * Eligibility is decided here, inside the transaction, not at queue time. A waiter
				 * who has since accepted a party invitation is skipped rather than pulled out of
				 * that party; one whose character already holds a usable instance is skipped
				 * rather than having the one-instance rule refuse the whole group a moment later.
				 *
				 * FOR UPDATE without SKIP LOCKED, deliberately. Two scene servers forming the same
				 * group at once must not each take half of it and both fail — with SKIP LOCKED
				 * they could, forever, on every pump. Plain FOR UPDATE makes the second wait for
				 * the first to commit, after which its predicate is re-evaluated against the new
				 * row versions, finds them matched, and it takes nothing. The lock is held for a
				 * few statements; nothing else waits on these rows. */
				var selectSql = $@"SELECT q.id, q.character_id
					FROM {TableName} q
					WHERE q.world_server_id = {{0}}
						AND q.scene_type = {{10}}
						AND q.scene_name = {{1}}
						AND q.difficulty = {{2}}
						AND q.status = {{3}}
						AND q.last_pulse >= {{4}}
						AND NOT EXISTS (SELECT 1 FROM {membershipTable} cp WHERE cp.character_id = q.character_id)
						AND NOT EXISTS (
							SELECT 1 FROM {sceneTable} s
							WHERE s.character_id = q.character_id
								AND s.world_server_id = {{0}}
								AND s.scene_type = {{5}}
								AND s.scene_status IN ({{6}}, {{7}}, {{8}}))
					ORDER BY q.time_created, q.id
					LIMIT {{9}}
					FOR UPDATE OF q";

				List<(long RowID, long CharacterID)> candidates = await ReadRowsAsync(
					dbContext,
					selectSql,
					new object[] { worldServerId, sceneName, difficulty, waiting, pulsedSinceUtc, (int)sceneType, pending, loading, ready, groupSize, GroupSceneType },
					reader => (reader.GetInt64(0), reader.GetInt64(1)),
					cancellationToken).ConfigureAwait(false);

				if (candidates.Count < groupSize)
				{
					// The ordinary outcome: not enough people yet. Nothing was changed.
					return GroupFinderMatchData.None;
				}

				long[] rowIds = new long[candidates.Count];
				long[] memberIds = new long[candidates.Count];
				for (int i = 0; i < candidates.Count; ++i)
				{
					rowIds[i] = candidates[i].RowID;
					memberIds[i] = candidates[i].CharacterID;
				}
				long leaderId = memberIds[0];
				var now = DateTime.UtcNow;

				// 2. The party. Version and time_created take their column defaults.
				var partySql = $@"INSERT INTO {partyTable} (world_server_id, time_created) VALUES ({{0}}, {{1}}) RETURNING id";
				long partyId = await ExecuteReturningAsync(
					dbContext,
					partySql,
					new object[] { worldServerId, now },
					reader => reader.GetInt64(0),
					cancellationToken).ConfigureAwait(false);

				/* 3. Everybody's membership, leader first in rank.
				 *
				 * ON CONFLICT DO NOTHING with the count checked, rather than the upsert the
				 * invitation path uses. The select above already excluded characters with a
				 * membership row, but those rows were not locked, and an invitation accepted in
				 * the gap between that select and this insert would otherwise be silently
				 * overwritten — moving the character out of a party they just joined and into one
				 * they never saw. Coming up short here throws, and the throw rolls back everything
				 * above, including the party row. */
				var membershipSql = $@"INSERT INTO {membershipTable} (character_id, party_id, rank, health_pct, version, time_created)
					SELECT c.character_id, {{0}}, CASE WHEN c.character_id = {{1}} THEN {{2}} ELSE {{3}} END, 1.0, 1, {{4}}
					FROM UNNEST({{5}}) AS c(character_id)
					JOIN {characterTable} ch ON ch.id = c.character_id AND ch.deleted = FALSE
					ON CONFLICT (character_id) DO NOTHING";

				int inserted = await dbContext.Database.ExecuteSqlRawAsync(
					membershipSql,
					new object[] { partyId, leaderId, (int)leaderRank, (int)memberRank, now, memberIds },
					cancellationToken).ConfigureAwait(false);

				if (inserted != memberIds.Length)
				{
					throw new DatabaseException(
						$"Group finder could not seat every member of a group for '{sceneName}': {inserted} of {memberIds.Length} memberships were written. Rolling the group back.",
						errorCode: DatabaseErrorCodes.StaleState);
				}

				/* 4. The instance, under the same guard as the dungeon finder's open path: no
				 * member, and not this party, may already hold a usable instance of anything. The
				 * select excluded members with one, so this is belt and braces against a member
				 * who opened something in the gap. No row means blocked; blocked means roll back. */
				var sceneSql = $@"INSERT INTO {sceneTable}
						(world_server_id, scene_server_id, scene_name, scene_handle, scene_status, scene_type, character_id, character_count, time_created, party_id, difficulty, is_private)
					SELECT {{0}}, 0, {{1}}, 0, {{2}}, {{3}}, {{4}}, 0, {{5}}, {{6}}, {{7}}, FALSE
					WHERE NOT EXISTS (
						SELECT 1 FROM {sceneTable}
						WHERE world_server_id = {{0}}
							AND scene_type = {{3}}
							AND scene_status IN ({{2}}, {{8}}, {{9}})
							AND (character_id = ANY({{10}}) OR (party_id <> 0 AND party_id = {{6}}))
					)
					RETURNING id";

				long? instanceId = await ExecuteReturningOrDefaultAsync(
					dbContext,
					sceneSql,
					new object[] { worldServerId, sceneName, pending, (int)sceneType, leaderId, now, partyId, difficulty, loading, ready, memberIds },
					reader => (long?)reader.GetInt64(0),
					cancellationToken).ConfigureAwait(false);

				if (!instanceId.HasValue || instanceId.Value <= 0)
				{
					throw new DatabaseException(
						$"Group finder could not open an instance of '{sceneName}' for a newly formed group: a member already holds one. Rolling the group back.",
						errorCode: DatabaseErrorCodes.StaleState);
				}

				// 5. Bind the queue rows to what was just built.
				var claimSql = $@"UPDATE {TableName}
					SET status = {{0}}, party_id = {{1}}, instance_id = {{2}}, time_matched = {{3}}
					WHERE id = ANY({{4}})";

				int claimed = await dbContext.Database.ExecuteSqlRawAsync(
					claimSql,
					new object[] { matched, partyId, instanceId.Value, now, rowIds },
					cancellationToken).ConfigureAwait(false);

				if (claimed != rowIds.Length)
				{
					// Cannot happen while the rows are locked; if it does, nothing above may stand.
					throw new DatabaseException(
						$"Group finder marked {claimed} of {rowIds.Length} locked queue rows as matched. Rolling the group back.",
						errorCode: DatabaseErrorCodes.StaleState);
				}

				return new GroupFinderMatchData(true, partyId, instanceId.Value, leaderId, memberIds);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ArenaMatchFormedData>> TryFormArenaMatchAsync(
			long worldServerId,
			string sceneName,
			int format,
			int templateId,
			int teamCount,
			int teamSize,
			DateTime pulsedSinceUtc,
			int maxCandidates = 128,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<ArenaMatchFormedData>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID and scene name are required.");
			}

			if (format < 0 || teamCount < 2 || teamSize < 1 || teamCount * teamSize > MaxGroupSize)
			{
				return DatabaseResult<ArenaMatchFormedData>.Failure(DatabaseErrorCodes.ValidationError, $"Team count must be at least 2, team size at least 1, and the match at most {MaxGroupSize} players.");
			}

			int seatsNeeded = teamCount * teamSize;
			int candidateLimit = Math.Max(seatsNeeded, Math.Min(maxCandidates, 512));

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				string sceneTable = dbContext.GetTableName<SceneEntity>();
				string matchTable = dbContext.GetTableName<ArenaMatchEntity>();
				string memberTable = dbContext.GetTableName<ArenaMatchMemberEntity>();

				int waiting = (int)GroupFinderQueueStatus.Waiting;
				int matched = (int)GroupFinderQueueStatus.Matched;
				int pending = (int)SceneStatus.Pending;
				int loading = (int)SceneStatus.Loading;
				int ready = (int)SceneStatus.Ready;

				/* 1. Lock the eligible waiters, oldest first. Same locking rationale as the
				 * dungeon former. Eligibility is the arena's: no seat in a live match, no usable
				 * dungeon or arena instance held. Party membership is fine here. */
				var selectSql = $@"SELECT q.id, q.character_id, q.group_id
					FROM {TableName} q
					WHERE q.world_server_id = {{0}}
						AND q.scene_type = {{1}}
						AND q.scene_name = {{2}}
						AND q.difficulty = {{3}}
						AND q.status = {{4}}
						AND q.last_pulse >= {{5}}
						AND NOT EXISTS (
							SELECT 1 FROM {sceneTable} s
							WHERE s.character_id = q.character_id
								AND s.world_server_id = {{0}}
								AND s.scene_type IN ({{6}}, {{1}})
								AND s.scene_status IN ({{7}}, {{8}}, {{9}}))
						AND NOT EXISTS (
							SELECT 1 FROM {memberTable} m
							JOIN {matchTable} am ON am.id = m.match_id
							WHERE m.character_id = q.character_id AND am.status < {{10}})
					ORDER BY q.time_created, q.id
					LIMIT {{11}}
					FOR UPDATE OF q";

				List<ArenaCandidate> candidates = await ReadRowsAsync(
					dbContext,
					selectSql,
					new object[] { worldServerId, PvPSceneType, sceneName, format, waiting, pulsedSinceUtc, GroupSceneType, pending, loading, ready, (int)ArenaMatchStatus.Ended, candidateLimit },
					reader => new ArenaCandidate(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)),
					cancellationToken).ConfigureAwait(false);

				if (!ArenaMatchComposer.TryCompose(candidates, teamCount, teamSize, out List<ArenaSeat> seats))
				{
					return ArenaMatchFormedData.None;
				}

				var rowIds = new long[seats.Count];
				var memberIds = new long[seats.Count];
				var teams = new int[seats.Count];
				for (int i = 0; i < seats.Count; ++i)
				{
					rowIds[i] = seats[i].RowID;
					memberIds[i] = seats[i].CharacterID;
					teams[i] = seats[i].Team;
				}
				var now = DateTime.UtcNow;

				/* 2. The instance: private, unowned by any party — the match row is who is in it —
				 * and under the one-instance guard against every seat, across both instance kinds. */
				var sceneSql = $@"INSERT INTO {sceneTable}
						(world_server_id, scene_server_id, scene_name, scene_handle, scene_status, scene_type, character_id, character_count, time_created, party_id, difficulty, is_private)
					SELECT {{0}}, 0, {{1}}, 0, {{2}}, {{3}}, {{4}}, 0, {{5}}, 0, {{6}}, TRUE
					WHERE NOT EXISTS (
						SELECT 1 FROM {sceneTable}
						WHERE world_server_id = {{0}}
							AND scene_type IN ({{3}}, {{9}})
							AND scene_status IN ({{2}}, {{7}}, {{8}})
							AND character_id = ANY({{10}})
					)
					RETURNING id";

				long? instanceId = await ExecuteReturningOrDefaultAsync(
					dbContext,
					sceneSql,
					new object[] { worldServerId, sceneName, pending, PvPSceneType, memberIds[0], now, format, loading, ready, GroupSceneType, memberIds },
					reader => (long?)reader.GetInt64(0),
					cancellationToken).ConfigureAwait(false);

				if (!instanceId.HasValue || instanceId.Value <= 0)
				{
					throw new DatabaseException(
						$"Arena match for '{sceneName}' could not open its instance: a seated character already holds one. Rolling the match back.",
						errorCode: DatabaseErrorCodes.StaleState);
				}

				// 3. The match.
				var matchSql = $@"INSERT INTO {matchTable}
						(world_server_id, instance_id, scene_name, template_id, format, team_count, team_size, status, winner_team, time_created)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, {{7}}, -1, {{8}})
					RETURNING id";

				long matchId = await ExecuteReturningAsync(
					dbContext,
					matchSql,
					new object[] { worldServerId, instanceId.Value, sceneName, templateId, format, teamCount, teamSize, (int)ArenaMatchStatus.Gathering, now },
					reader => reader.GetInt64(0),
					cancellationToken).ConfigureAwait(false);

				// 4. The seats.
				var memberSql = $@"INSERT INTO {memberTable} (match_id, character_id, team, kills, deaths, score)
					SELECT {{0}}, x.character_id, x.team, 0, 0, 0
					FROM UNNEST({{1}}, {{2}}) AS x(character_id, team)";

				int seated = await dbContext.Database.ExecuteSqlRawAsync(
					memberSql,
					new object[] { matchId, memberIds, teams },
					cancellationToken).ConfigureAwait(false);

				if (seated != memberIds.Length)
				{
					throw new DatabaseException(
						$"Arena match {matchId} seated {seated} of {memberIds.Length} players. Rolling the match back.",
						errorCode: DatabaseErrorCodes.StaleState);
				}

				// 5. Bind the queue rows to the instance. No party: arenas do not form one.
				var claimSql = $@"UPDATE {TableName}
					SET status = {{0}}, party_id = 0, instance_id = {{1}}, time_matched = {{2}}
					WHERE id = ANY({{3}})";

				int claimed = await dbContext.Database.ExecuteSqlRawAsync(
					claimSql,
					new object[] { matched, instanceId.Value, now, rowIds },
					cancellationToken).ConfigureAwait(false);

				if (claimed != rowIds.Length)
				{
					throw new DatabaseException(
						$"Arena match {matchId} marked {claimed} of {rowIds.Length} locked queue rows as matched. Rolling the match back.",
						errorCode: DatabaseErrorCodes.StaleState);
				}

				return new ArenaMatchFormedData(true, matchId, instanceId.Value, seats);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> TryClaimForInstanceAsync(long characterId, long partyId, long instanceId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || partyId <= 0 || instanceId <= 0)
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "Character, party and instance IDs must be greater than zero.");
			}

			var now = DateTime.UtcNow;

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET status = {{1}}, party_id = {{2}}, instance_id = {{3}}, time_matched = {{4}}
					WHERE character_id = {{0}} AND status = {{5}}";

				int affected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { characterId, (int)GroupFinderQueueStatus.Matched, partyId, instanceId, now, (int)GroupFinderQueueStatus.Waiting },
					cancellationToken).ConfigureAwait(false);

				return affected > 0;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> ReleaseClaimAsync(long characterId, long instanceId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || instanceId <= 0)
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "Character and instance IDs must be greater than zero.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET status = {{1}}, party_id = 0, instance_id = 0, time_matched = NULL
					WHERE character_id = {{0}} AND status = {{2}} AND instance_id = {{3}}";

				int affected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { characterId, (int)GroupFinderQueueStatus.Waiting, (int)GroupFinderQueueStatus.Matched, instanceId },
					cancellationToken).ConfigureAwait(false);

				return affected > 0;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteStaleAsync(DateTime pulsedBeforeUtc, int maxRows = 256, CancellationToken cancellationToken = default)
		{
			if (maxRows < 1)
			{
				maxRows = 1;
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName}
					WHERE id IN (
						SELECT id FROM {TableName}
						WHERE last_pulse < {{0}}
						ORDER BY last_pulse
						LIMIT {{1}}
					)";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { pulsedBeforeUtc, maxRows },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <summary>
		/// Deduplicates a caller's id list, dropping non-positive ids and capping its length.
		/// </summary>
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
				long id = ids[i];
				if (id > 0 && seen.Add(id))
				{
					result.Add(id);
				}
			}
			return result.ToArray();
		}

		/// <summary>
		/// Runs a query on the context's connection and transaction and maps every row.
		/// </summary>
		/// <remarks>
		/// The base class offers single-row readers only; the group former needs the whole
		/// candidate set, locked, on the transaction it is inside.
		/// </remarks>
		private static async Task<List<TRow>> ReadRowsAsync<TRow>(
			NpgsqlDbContext dbContext,
			string sql,
			object[] parameters,
			Func<DbDataReader, TRow> map,
			CancellationToken cancellationToken)
		{
			var connection = dbContext.Database.GetDbConnection();
			if (connection.State != ConnectionState.Open)
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
			}

			using var command = connection.CreateCommand();
			command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
			command.CommandText = ParameterPlaceholderRegex.Replace(sql, "@p$1");
			for (int i = 0; i < parameters.Length; i++)
			{
				var param = command.CreateParameter();
				param.ParameterName = "@p" + i;
				param.Value = parameters[i] ?? DBNull.Value;
				command.Parameters.Add(param);
			}

			var rows = new List<TRow>();
			using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
			while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
			{
				rows.Add(map(reader));
			}
			return rows;
		}

		/// <summary>
		/// Maps a queue entity to its DTO.
		/// </summary>
		private static GroupFinderQueueData MapEntityToDto(GroupFinderQueueEntity entity)
		{
			return new GroupFinderQueueData(
				entity.ID,
				entity.WorldServerID,
				entity.CharacterID,
				entity.SceneType,
				entity.GroupID,
				entity.SceneName,
				entity.Difficulty,
				entity.Status,
				entity.PartyID,
				entity.InstanceID,
				entity.TimeCreated,
				entity.LastPulse,
				entity.TimeMatched);
		}
	}
}
