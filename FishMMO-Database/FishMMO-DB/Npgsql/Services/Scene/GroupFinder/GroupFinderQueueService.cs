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
	/// <para>
	/// Every state change here is a single statement, or a single transaction, whose
	/// <c>WHERE</c> re-asserts the state it expects. The queue is worked by every scene server on
	/// the world at once, and that is what makes two of them touching the same row produce one
	/// winner and one no-op.
	/// </para>
	/// <para>
	/// Every queue time is the DATABASE's (<see cref="DbNow"/>), and every staleness test is made
	/// against it in SQL. A heartbeat is written by one scene server and judged by all the others;
	/// stamped and judged by each server's own clock, a server running behind had its waiters
	/// left out of everybody's counts and, far enough behind, swept away every sweep.
	/// </para>
	/// </remarks>
	public sealed class GroupFinderQueueService : BaseService<GroupFinderQueueEntity>, IGroupFinderQueueService
	{
		/// <summary>
		/// The database's clock, as the naive-UTC timestamp these columns hold.
		/// </summary>
		/// <remarks>
		/// <c>now()</c>, the transaction's start, rather than the <c>clock_timestamp()</c> the plot
		/// and guild update markers use. Those are watermarks that readers sweep past, so the stamp
		/// must be as late as possible; these are compared against a window many seconds wide, where
		/// a transaction's few milliseconds do not matter, and one value per transaction is what
		/// makes a pre-made group's rows share a queue time and a formed match's rows share a
		/// matched time. <c>AT TIME ZONE 'UTC'</c> because the columns hold UTC without a zone and
		/// the server's own zone is whatever it was installed with.
		/// </remarks>
		private const string DbNow = "(now() AT TIME ZONE 'UTC')";

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
		public async Task<DatabaseResult<long>> EnqueueAsync(long worldServerId, long characterId, SceneType sceneType, string sceneName, int difficulty, TimeSpan staleAfter, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || characterId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID, character ID and scene name are required.");
			}

			if (difficulty < 0)
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Difficulty index cannot be negative.");
			}

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
				return await UpsertRowAsync(dbContext, worldServerId, characterId, sceneType, sceneName, difficulty, 0, staleAfter, cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <summary>
		/// The single-row upsert both enqueue paths use. Returns the row id, or 0 when a live
		/// matched row refused the re-point.
		/// </summary>
		private async Task<long> UpsertRowAsync(NpgsqlDbContext dbContext, long worldServerId, long characterId, SceneType sceneType, string sceneName, int difficulty, long groupId, TimeSpan staleAfter, CancellationToken cancellationToken)
		{
			var sql = $@"INSERT INTO {TableName}
					(world_server_id, character_id, scene_type, group_id, scene_name, difficulty, status, party_id, instance_id, time_created, last_pulse, time_matched)
				VALUES ({{0}}, {{1}}, {{6}}, {{7}}, {{2}}, {{3}}, {{4}}, 0, 0, {DbNow}, {DbNow}, NULL)
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
				WHERE {TableName}.status = {{4}} OR {TableName}.last_pulse < {DbNow} - {{5}}
				RETURNING id";

			return await ExecuteReturningOrDefaultAsync(
				dbContext,
				sql,
				new object[] { worldServerId, characterId, sceneName, difficulty, (int)GroupFinderQueueStatus.Waiting, staleAfter, (int)sceneType, groupId },
				reader => reader.GetInt64(0),
				cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> EnqueueGroupAsync(long worldServerId, SceneType sceneType, string sceneName, int difficulty, long groupId, IReadOnlyList<long> characterIds, TimeSpan staleAfter, CancellationToken cancellationToken = default)
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

			/* One transaction, so DbNow is one value: the members queued together, and every one of
			 * them gets the same queue time. */
			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				int written = 0;
				foreach (long characterId in ids)
				{
					long rowId = await UpsertRowAsync(dbContext, worldServerId, characterId, sceneType, sceneName, difficulty, groupId, staleAfter, cancellationToken).ConfigureAwait(false);
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
		public async Task<DatabaseResult<IReadOnlyList<GroupFinderPulseData>>> PulseAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default)
		{
			long[] ids = Distinct(characterIds);
			if (ids.Length == 0)
			{
				return DatabaseResult<IReadOnlyList<GroupFinderPulseData>>.Success(Array.Empty<GroupFinderPulseData>());
			}

			var result = await ExecuteWriteAsync<IReadOnlyList<GroupFinderPulseData>>(async dbContext =>
			{
				string membershipTable = dbContext.GetTableName<CharacterPartyEntity>();

				/* The heartbeat, the rows it touched, and — for a row matched into a party — the
				 * character's membership, in one statement. The membership is read from the same
				 * snapshot as the row: the forming transaction commits the match and the memberships
				 * together, so a matched row is never seen without the membership it created. Safe
				 * to repeat when the reply is lost; a heartbeat is idempotent. */
				var sql = $@"WITH pulsed AS (
						UPDATE {TableName}
						SET last_pulse = {DbNow}
						WHERE character_id = ANY({{0}})
						RETURNING id, world_server_id, character_id, scene_type, group_id, scene_name, difficulty, status, party_id, instance_id, time_created, last_pulse, time_matched
					)
					SELECT p.id, p.world_server_id, p.character_id, p.scene_type, p.group_id, p.scene_name, p.difficulty, p.status, p.party_id, p.instance_id, p.time_created, p.last_pulse, p.time_matched,
						COALESCE(cp.party_id, 0) AS member_party_id,
						COALESCE(cp.rank, 0) AS member_rank
					FROM pulsed p
					LEFT JOIN {membershipTable} cp ON cp.character_id = p.character_id AND p.status = {{1}} AND p.party_id <> 0";

				List<GroupFinderPulseData> rows = await ReadRowsAsync(
					dbContext,
					sql,
					new object[] { ids, (int)GroupFinderQueueStatus.Matched },
					reader => new GroupFinderPulseData(
						new GroupFinderQueueData(
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
						reader.GetInt64(13),
						Convert.ToByte(reader.GetValue(14))),
					cancellationToken).ConfigureAwait(false);

				return rows;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> CountWaitingAsync(long worldServerId, SceneType sceneType, string sceneName, int difficulty, TimeSpan staleAfter, CancellationToken cancellationToken = default)
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
						AND last_pulse >= {DbNow} - {{4}}";

				return await ExecuteScalarIntAsync(
					dbContext,
					sql,
					new object[] { worldServerId, sceneName, difficulty, (int)GroupFinderQueueStatus.Waiting, staleAfter, (int)sceneType },
					cancellationToken).ConfigureAwait(false);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyDictionary<GroupFinderQueueKey, int>>> CountWaitingAsync(long worldServerId, IReadOnlyList<GroupFinderQueueKey> keys, TimeSpan staleAfter, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<IReadOnlyDictionary<GroupFinderQueueKey, int>>.Failure(DatabaseErrorCodes.ValidationError, "World server ID must be greater than zero.");
			}

			// Distinct and bounded, like the id batches; a key with no scene cannot match a row.
			var distinct = new List<GroupFinderQueueKey>();
			var seen = new HashSet<GroupFinderQueueKey>();
			if (keys != null)
			{
				for (int i = 0; i < keys.Count && distinct.Count < MaxBatchIds; ++i)
				{
					if (!string.IsNullOrWhiteSpace(keys[i].SceneName) && seen.Add(keys[i]))
					{
						distinct.Add(keys[i]);
					}
				}
			}

			var counts = new Dictionary<GroupFinderQueueKey, int>(distinct.Count);
			foreach (GroupFinderQueueKey key in distinct)
			{
				counts[key] = 0;
			}
			if (distinct.Count == 0)
			{
				return DatabaseResult<IReadOnlyDictionary<GroupFinderQueueKey, int>>.Success(counts);
			}

			var sceneTypes = new int[distinct.Count];
			var sceneNames = new string[distinct.Count];
			var difficulties = new int[distinct.Count];
			for (int i = 0; i < distinct.Count; ++i)
			{
				sceneTypes[i] = distinct[i].SceneType;
				sceneNames[i] = distinct[i].SceneName;
				difficulties[i] = distinct[i].Difficulty;
			}

			var result = await ExecuteReadAsync<IReadOnlyDictionary<GroupFinderQueueKey, int>>(async dbContext =>
			{
				/* One probe of the matcher's index per key, grouped in one statement. Only keys with
				 * waiters come back; the dictionary above already holds a zero for the rest. */
				var sql = $@"SELECT k.scene_type, k.scene_name, k.difficulty, COUNT(*)::int
					FROM UNNEST({{1}}::int[], {{2}}::text[], {{3}}::int[]) AS k(scene_type, scene_name, difficulty)
					JOIN {TableName} q
						ON q.world_server_id = {{0}}
						AND q.scene_type = k.scene_type
						AND q.scene_name = k.scene_name
						AND q.difficulty = k.difficulty
						AND q.status = {{4}}
						AND q.last_pulse >= {DbNow} - {{5}}
					GROUP BY k.scene_type, k.scene_name, k.difficulty";

				List<(GroupFinderQueueKey Key, int Count)> rows = await ReadRowsAsync(
					dbContext,
					sql,
					new object[] { worldServerId, sceneTypes, sceneNames, difficulties, (int)GroupFinderQueueStatus.Waiting, staleAfter },
					reader => (new GroupFinderQueueKey(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)), reader.GetInt32(3)),
					cancellationToken).ConfigureAwait(false);

				foreach ((GroupFinderQueueKey key, int count) in rows)
				{
					counts[key] = count;
				}
				return counts;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<GroupFinderQueueKey>>> FetchBackfillOpeningsAsync(long worldServerId, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<IReadOnlyList<GroupFinderQueueKey>>.Failure(DatabaseErrorCodes.ValidationError, "World server ID must be greater than zero.");
			}

			var result = await ExecuteReadAsync<IReadOnlyList<GroupFinderQueueKey>>(async dbContext =>
			{
				string sceneTable = dbContext.GetTableName<SceneEntity>();
				string matchTable = dbContext.GetTableName<ArenaMatchEntity>();
				string memberTable = dbContext.GetTableName<ArenaMatchMemberEntity>();

				/* The same opening TryBackfillArenaSeatAsync's first statement looks for — live, window
				 * open, instance ready, some team short — across every arena at once and without a
				 * lock. Served by the (world_server_id, status) index; live matches are few. */
				var sql = $@"SELECT DISTINCT am.scene_name, am.format
					FROM {matchTable} am
					JOIN {sceneTable} s ON s.id = am.instance_id AND s.scene_status = {{2}}
					WHERE am.world_server_id = {{0}}
						AND am.status = {{1}}
						AND am.backfill_until_utc IS NOT NULL AND am.backfill_until_utc > {DbNow}
						AND EXISTS (
							SELECT 1 FROM generate_series(0, am.team_count - 1) AS g(team)
							WHERE (SELECT COUNT(*) FROM {memberTable} m WHERE m.match_id = am.id AND m.team = g.team AND m.status = {{3}}) < am.team_size)";

				List<GroupFinderQueueKey> rows = await ReadRowsAsync(
					dbContext,
					sql,
					new object[] { worldServerId, (int)ArenaMatchStatus.Live, (int)SceneStatus.Ready, (int)ArenaSeatStatus.Seated },
					reader => new GroupFinderQueueKey(PvPSceneType, reader.GetString(0), reader.GetInt32(1)),
					cancellationToken).ConfigureAwait(false);

				return rows;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<GroupFinderMatchData>> TryFormGroupAsync(
			long worldServerId,
			string sceneName,
			int difficulty,
			int groupSize,
			TimeSpan staleAfter,
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
						AND q.last_pulse >= {DbNow} - {{4}}
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
					new object[] { worldServerId, sceneName, difficulty, waiting, staleAfter, (int)sceneType, pending, loading, ready, groupSize, GroupSceneType },
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
					SET status = {{0}}, party_id = {{1}}, instance_id = {{2}}, time_matched = {DbNow}
					WHERE id = ANY({{3}})";

				int claimed = await dbContext.Database.ExecuteSqlRawAsync(
					claimSql,
					new object[] { matched, partyId, instanceId.Value, rowIds },
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
			TimeSpan staleAfter,
			int maxCandidates = 128,
			ArenaRatingSource ratingSource = default,
			ArenaComposeOptions composeOptions = default,
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
				string ratingTable = dbContext.GetTableName<ArenaRatingEntity>();
				string attributeTable = dbContext.GetTableName<CharacterAttributeEntity>();

				int waiting = (int)GroupFinderQueueStatus.Waiting;
				int matched = (int)GroupFinderQueueStatus.Matched;
				int pending = (int)SceneStatus.Pending;
				int loading = (int)SceneStatus.Loading;
				int ready = (int)SceneStatus.Ready;

				/* The rating column is a scalar subquery so the row lock stays on q alone; a JOIN
				 * would lock rating rows too under FOR UPDATE. */
				string ratingExpr = "0";
				if (ratingSource.SeasonID > 0)
				{
					ratingExpr = $"COALESCE((SELECT r.rating FROM {ratingTable} r WHERE r.season_id = {{12}} AND r.character_id = q.character_id), {{13}})";
				}
				// != 0: template ids are signed hashes, and 0 is ArenaRatingSource's "none".
				else if (ratingSource.AttributeTemplateID != 0)
				{
					ratingExpr = $"COALESCE((SELECT a.value FROM {attributeTable} a WHERE a.character_id = q.character_id AND a.template_id = {{12}} AND a.deleted = FALSE LIMIT 1), {{13}})";
				}

				/* 1. Lock the eligible waiters, oldest first. Same locking rationale as the
				 * dungeon former. Eligibility is the arena's: no seat in a live match, no usable
				 * dungeon or arena instance held. Party membership is fine here. */
				var selectSql = $@"SELECT q.id, q.character_id, q.group_id, {ratingExpr} AS rating
					FROM {TableName} q
					WHERE q.world_server_id = {{0}}
						AND q.scene_type = {{1}}
						AND q.scene_name = {{2}}
						AND q.difficulty = {{3}}
						AND q.status = {{4}}
						AND q.last_pulse >= {DbNow} - {{5}}
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
					new object[]
					{
						worldServerId, PvPSceneType, sceneName, format, waiting, staleAfter, GroupSceneType, pending, loading, ready, (int)ArenaMatchStatus.Ended, candidateLimit,
						ratingSource.SeasonID > 0 ? ratingSource.SeasonID : (object)(long)ratingSource.AttributeTemplateID,
						ratingSource.DefaultRating,
					},
					reader => new ArenaCandidate(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), Convert.ToInt32(reader.GetValue(3))),
					cancellationToken).ConfigureAwait(false);

				if (!ArenaMatchComposer.TryCompose(candidates, teamCount, teamSize, composeOptions, out List<ArenaSeat> seats))
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

				/* 3. The match. Its creation time is the database's, because the abandoned-match
				 * sweep that ages it runs on every other scene server and compares it with the
				 * database's clock (ArenaMatchService.CancelAbandonedAsync). */
				var matchSql = $@"INSERT INTO {matchTable}
						(world_server_id, instance_id, scene_name, template_id, format, team_count, team_size, status, winner_team, time_created)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, {{7}}, -1, {DbNow})
					RETURNING id";

				long matchId = await ExecuteReturningAsync(
					dbContext,
					matchSql,
					new object[] { worldServerId, instanceId.Value, sceneName, templateId, format, teamCount, teamSize, (int)ArenaMatchStatus.Gathering },
					reader => reader.GetInt64(0),
					cancellationToken).ConfigureAwait(false);

				// 4. The seats.
				var memberSql = $@"INSERT INTO {memberTable} (match_id, character_id, team, kills, deaths, score, status, rating_delta)
					SELECT {{0}}, x.character_id, x.team, 0, 0, 0, 0, 0
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
					SET status = {{0}}, party_id = 0, instance_id = {{1}}, time_matched = {DbNow}
					WHERE id = ANY({{2}})";

				int claimed = await dbContext.Database.ExecuteSqlRawAsync(
					claimSql,
					new object[] { matched, instanceId.Value, rowIds },
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
		public async Task<DatabaseResult<ArenaBackfillData>> TryBackfillArenaSeatAsync(
			long worldServerId,
			string sceneName,
			int format,
			TimeSpan staleAfter,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName) || format < 0)
			{
				return DatabaseResult<ArenaBackfillData>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID, scene name and format are required.");
			}

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
				int seated = (int)ArenaSeatStatus.Seated;

				/* 1. The neediest live match of this arena and format with an open window: the team
				 * with the fewest seated players. Locked, so two servers cannot fill the same seat. */
				var matchSql = $@"SELECT am.id, am.instance_id, t.team
					FROM {matchTable} am
					JOIN LATERAL (
						SELECT g.team, COUNT(m.id) FILTER (WHERE m.status = {{4}}) AS seated
						FROM generate_series(0, am.team_count - 1) AS g(team)
						LEFT JOIN {memberTable} m ON m.match_id = am.id AND m.team = g.team
						GROUP BY g.team
						ORDER BY seated ASC, g.team ASC
						LIMIT 1
					) t ON TRUE
					JOIN {sceneTable} s ON s.id = am.instance_id AND s.scene_status = {{5}}
					WHERE am.world_server_id = {{0}}
						AND am.scene_name = {{1}}
						AND am.format = {{2}}
						AND am.status = {{3}}
						AND am.backfill_until_utc IS NOT NULL AND am.backfill_until_utc > {DbNow}
						AND t.seated < am.team_size
					ORDER BY am.time_created
					LIMIT 1
					FOR UPDATE OF am";

				var openings = await ReadRowsAsync(
					dbContext,
					matchSql,
					new object[] { worldServerId, sceneName, format, (int)ArenaMatchStatus.Live, seated, ready },
					reader => (matchId: reader.GetInt64(0), instanceId: reader.GetInt64(1), team: reader.GetInt32(2)),
					cancellationToken).ConfigureAwait(false);

				if (openings.Count == 0)
				{
					return ArenaBackfillData.None;
				}

				var opening = openings[0];

				/* 2. The longest-waiting solo who is eligible and was never in this match (a
				 * deserter does not backfill their own seat; the reconnect path reseats them). Solo
				 * only: a group must play together and a single vacated seat cannot hold it. */
				var waiterSql = $@"SELECT q.id, q.character_id
					FROM {TableName} q
					WHERE q.world_server_id = {{0}}
						AND q.scene_type = {{1}}
						AND q.scene_name = {{2}}
						AND q.difficulty = {{3}}
						AND q.status = {{4}}
						AND q.group_id = 0
						AND q.last_pulse >= {DbNow} - {{5}}
						AND NOT EXISTS (
							SELECT 1 FROM {sceneTable} s
							WHERE s.character_id = q.character_id
								AND s.world_server_id = {{0}}
								AND s.scene_type IN ({{6}}, {{1}})
								AND s.scene_status IN ({{7}}, {{8}}, {{9}}))
						AND NOT EXISTS (
							SELECT 1 FROM {memberTable} m
							JOIN {matchTable} am ON am.id = m.match_id
							WHERE m.character_id = q.character_id AND (am.status < {{10}} OR am.id = {{11}}))
					ORDER BY q.time_created, q.id
					LIMIT 1
					FOR UPDATE OF q";

				var waiters = await ReadRowsAsync(
					dbContext,
					waiterSql,
					new object[] { worldServerId, PvPSceneType, sceneName, format, waiting, staleAfter, GroupSceneType, pending, loading, ready, (int)ArenaMatchStatus.Ended, opening.matchId },
					reader => (rowId: reader.GetInt64(0), characterId: reader.GetInt64(1)),
					cancellationToken).ConfigureAwait(false);

				if (waiters.Count == 0)
				{
					return ArenaBackfillData.None;
				}

				var waiter = waiters[0];

				/* 3. The seat, then the queue row bound to the instance.
				 *
				 * The seat is only taken while the team still has room, counted HERE and not
				 * trusted from step 1. Step 1 locks the match and counts in the same statement, and
				 * under READ COMMITTED a statement's snapshot is taken when it starts: a second
				 * backfill of the same match waits on the lock and then still counts from before the
				 * first one's seat committed, so both saw one open seat and both took it (issue #267;
				 * the same defect over-filled parties and guilds). This INSERT runs after the lock is
				 * held, so it sees every earlier backfill. Zero rows means the team filled first, and
				 * the check below rolls back as a lost race. */
				int inserted = await dbContext.Database.ExecuteSqlRawAsync(
					$@"INSERT INTO {memberTable} (match_id, character_id, team, kills, deaths, score, status, rating_delta)
					SELECT {{0}}, {{1}}, {{2}}, 0, 0, 0, {{3}}, 0
					WHERE (SELECT COUNT(*) FROM {memberTable} WHERE match_id = {{0}} AND team = {{2}} AND status = {{3}})
						< (SELECT team_size FROM {matchTable} WHERE id = {{0}})",
					new object[] { opening.matchId, waiter.characterId, opening.team, seated },
					cancellationToken).ConfigureAwait(false);

				int claimed = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {TableName} SET status = {{0}}, party_id = 0, instance_id = {{1}}, time_matched = {DbNow} WHERE id = {{2}} AND status = {{3}}",
					new object[] { matched, opening.instanceId, waiter.rowId, waiting },
					cancellationToken).ConfigureAwait(false);

				if (inserted != 1 || claimed != 1)
				{
					throw new DatabaseException(
						$"Arena backfill of match {opening.matchId} seated {inserted} and claimed {claimed} rows. Rolling back.",
						errorCode: DatabaseErrorCodes.StaleState);
				}

				return new ArenaBackfillData(true, opening.matchId, opening.instanceId, waiter.characterId, opening.team);
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

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET status = {{1}}, party_id = {{2}}, instance_id = {{3}}, time_matched = {DbNow}
					WHERE character_id = {{0}} AND status = {{4}}";

				int affected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { characterId, (int)GroupFinderQueueStatus.Matched, partyId, instanceId, (int)GroupFinderQueueStatus.Waiting },
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
		public async Task<DatabaseResult<int>> DeleteStaleAsync(TimeSpan staleAfter, int maxRows = 256, CancellationToken cancellationToken = default)
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
						WHERE last_pulse < {DbNow} - {{0}}
						ORDER BY last_pulse
						LIMIT {{1}}
					)";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { staleAfter, maxRows },
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
	}
}
