using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	public sealed class SceneService : BaseService<SceneEntity>, ISceneService
	{
		/// <summary>
		/// Compiled query for retrieving character instance scene (hot path for scene loading).
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, int, long, string, CancellationToken, Task<SceneEntity?>> getCharacterInstanceQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, int sceneType, long worldServerId, string sceneName, CancellationToken ct) =>
				context.Scenes
					.AsNoTracking()
					.Where(s => s.CharacterID == characterId &&
								s.SceneType == sceneType &&
								s.WorldServerID == worldServerId &&
								s.SceneName == sceneName &&
								/* Enterable rows only.
								 *
								 * Both callers already discard a row they cannot enter, so this
								 * changes nothing for them except which row wins the ordering below
								 * — and that mattered: a Failed row that happened to be newer than
								 * a live one masked an instance the character actually owns. Since
								 * EnqueueForPartyAsync blocks on that live row, the caller could
								 * then neither reach it nor replace it until it unloaded of its own
								 * accord. Filtering here makes what this returns agree with what
								 * that insert guard considers to exist. */
								(s.SceneStatus == (int)SceneStatus.Ready ||
								 s.SceneStatus == (int)SceneStatus.Pending ||
								 s.SceneStatus == (int)SceneStatus.Loading))
					// Newest first: duplicate rows for the same (character, scene) can already
					// exist from before this query filtered on the scene, and an unordered
					// FirstOrDefault made which one came back a property of physical row order.
					.OrderByDescending(s => s.ID)
					.FirstOrDefault());
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving scene by ID (hot path for scene loading).
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<SceneEntity?>> fetchByIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long sceneId, CancellationToken ct) =>
				context.Scenes
					.AsNoTracking()
					.FirstOrDefault(s => s.ID == sceneId));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving available scenes (hot path for scene matchmaking).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, string, int, int, IAsyncEnumerable<SceneEntity>> fetchAvailableQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long worldServerId, string sceneName, int maxClients, int readyStatus) =>
				context.Scenes
						.AsNoTracking()
						.Where(s =>
							s.WorldServerID == worldServerId &&
							s.SceneName == sceneName &&
							s.CharacterCount < maxClients &&
							s.SceneStatus == readyStatus));

		/// <summary>
		/// Compiled query for retrieving ready scenes (hot path for scene server queries).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, int, IAsyncEnumerable<SceneEntity>> fetchReadyQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long worldServerId, int readyStatus) =>
				context.Scenes
					.AsNoTracking()
					.Where(s => s.WorldServerID == worldServerId && s.SceneStatus == readyStatus));

		/// <summary>
		/// Initializes a new instance of SceneService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public SceneService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <summary>
		/// The database server's clock as a UTC <c>timestamp without time zone</c>, the type of
		/// every scene time column.
		/// </summary>
		/// <remarks>
		/// A scene row is written by one process and aged by others. The dungeon finder on one
		/// scene server queues it, a different scene server loads it and bounds its lifetime, and
		/// the world server reaps it if it never becomes ready. Each enqueue used to stamp
		/// <c>time_created</c> with its own host's <c>DateTime.UtcNow</c>, so every age taken from
		/// the row was really the difference between two hosts' clocks, and a host running
		/// minutes fast closed instances minutes early. The database is the one clock every one
		/// of those processes already shares. Rows are stamped with it here and aged against it
		/// in SQL (<see cref="SceneAgeSecondsSql"/>), so no host clock takes part at all.
		/// <para>
		/// <c>clock_timestamp()</c> rather than <c>CURRENT_TIMESTAMP</c>: the latter is the start
		/// of the enclosing transaction, not the moment of the statement.
		/// </para>
		/// </remarks>
		private const string DatabaseUtcNowSql = "(clock_timestamp() AT TIME ZONE 'UTC')";

		/// <summary>
		/// A scene row's age in seconds by the database clock, as <c>double precision</c>. Expects
		/// the scene table to be reachable as <c>s</c>.
		/// </summary>
		private const string SceneAgeSecondsSql = "EXTRACT(EPOCH FROM (" + DatabaseUtcNowSql + " - s.time_created))::double precision";

		/// <summary>
		/// The scene columns <see cref="ReadSceneRow"/> maps, in its order, qualified by the alias
		/// <c>s</c>.
		/// </summary>
		private const string SceneRowColumnsSql =
			"s.id, s.world_server_id, s.scene_server_id, s.scene_name, s.scene_handle, s.scene_status, s.scene_type, " +
			"s.character_id, s.character_count, s.time_created, s.party_id, s.difficulty, s.is_private";

		/// <summary>Number of columns in <see cref="SceneRowColumnsSql"/>.</summary>
		private const int SceneRowColumnCount = 13;

		/// <summary>
		/// Maps one row laid out as <see cref="SceneRowColumnsSql"/>, starting at ordinal 0.
		/// </summary>
		private static SceneData ReadSceneRow(System.Data.Common.DbDataReader reader)
		{
			return new SceneData(
				id: reader.GetInt64(0),
				worldServerID: reader.GetInt64(1),
				sceneServerID: reader.GetInt64(2),
				sceneName: reader.GetString(3),
				sceneHandle: reader.GetInt32(4),
				sceneStatus: reader.GetInt32(5),
				sceneType: reader.GetInt32(6),
				characterID: reader.GetInt64(7),
				characterCount: reader.GetInt32(8),
				timeCreated: DateTime.SpecifyKind(reader.GetDateTime(9), DateTimeKind.Utc),
				partyID: reader.GetInt64(10),
				difficulty: reader.GetInt32(11),
				isPrivate: reader.GetBoolean(12));
		}

		/// <summary>
		/// Reads the age column that follows <see cref="SceneRowColumnsSql"/>, clamped at zero.
		/// </summary>
		/// <remarks>
		/// Negative only for a row stamped by a host clock ahead of the database, which rows
		/// written before <see cref="DatabaseUtcNowSql"/> may be. "Created in the future" is not an
		/// age anything downstream can use; zero is the honest reading of it.
		/// </remarks>
		private static double ReadSceneAge(System.Data.Common.DbDataReader reader)
		{
			double age = reader.IsDBNull(SceneRowColumnCount) ? 0.0 : reader.GetDouble(SceneRowColumnCount);
			return age > 0.0 ? age : 0.0;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> EnqueueIfUnderOutstandingLimitAsync(
			long worldServerId,
			string sceneName,
			SceneType sceneType,
			int maxOutstanding = 1,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID and scene name are required.");
			}

			if (maxOutstanding < 1)
			{
				maxOutstanding = 1;
			}

			/* Taken once, outside the retried delegate. A retry after a reply lost past the commit
			 * answers with the load its first attempt queued; it used to either queue a second (still
			 * under the cap) or report 0, "enough already coming", for a load it had queued itself
			 * (issue #267). */
			Guid requestKey = Guid.NewGuid();

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* One statement, so the "how many are already coming?" count and the insert
				 * cannot be interleaved by a second caller. scene_server_id and scene_handle are
				 * written as 0 because no scene server owns the row yet — DequeueAsync hands it
				 * to one and stamps the claim, and SetReadyAsync writes the real handle.
				 * time_created is the database's clock; see DatabaseUtcNowSql. */
				var sql = $@"WITH mine AS (
						SELECT id FROM {TableName} WHERE request_key = {{4}}
					),
					ins AS (
						INSERT INTO {TableName}
							(world_server_id, scene_server_id, scene_name, scene_handle, scene_status, scene_type, character_id, character_count, time_created, request_key)
						SELECT {{0}}, 0, {{1}}, 0, {{2}}, {{3}}, 0, 0, {DatabaseUtcNowSql}, {{4}}
						WHERE NOT EXISTS (SELECT 1 FROM mine)
						AND (
							SELECT COUNT(*) FROM {TableName}
							WHERE world_server_id = {{0}}
								AND scene_name = {{1}}
								AND scene_type = {{3}}
								AND scene_status IN ({{2}}, {{5}})
						) < {{6}}
						RETURNING id
					)
					SELECT id FROM mine UNION ALL SELECT id FROM ins";

				return await ExecuteReturningOrDefaultAsync(
					dbContext,
					sql,
					new object[]
					{
						worldServerId,
						sceneName,
						(int)SceneStatus.Pending,
						(int)sceneType,
						requestKey,
						(int)SceneStatus.Loading,
						maxOutstanding,
					},
					reader => reader.GetInt64(0),
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			// Zero means the insert was skipped because enough loads are already outstanding,
			// which is the whole point of this method and is reported as success.
			return result;
		}

		/// <summary>
		/// Ceiling on how many party members the blocking check below considers.
		/// </summary>
		/// <remarks>
		/// The predicate is built as an inline list of parameters rather than an array bind, so it
		/// has to be bounded. Far above any real party size; a list longer than this is a caller
		/// bug rather than a party, and truncating is safer than emitting unbounded SQL.
		/// </remarks>
		private const int MaxPartyBlockingIds = 64;

		/// <inheritdoc/>
		/// <summary>
		/// The shared <c>FishMMO.Shared.SceneType.PvP</c> value. Numeric because the database-side
		/// <see cref="SceneType"/> enum's names do not correspond to the shared values callers cast in.
		/// </summary>
		private const int ArenaSceneType = 3;

		public async Task<DatabaseResult<long>> EnqueueForPartyAsync(
			long worldServerId,
			string sceneName,
			SceneType sceneType,
			long characterId,
			long partyId,
			int difficulty,
			bool isPrivate,
			IReadOnlyList<long> partyCharacterIds,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID and scene name are required.");
			}

			if (difficulty < 0)
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Difficulty index cannot be negative.");
			}

			// Deduplicated, and the requester is always included: a row created for them between
			// the caller's own lookup and this insert must block it too.
			var blocking = new List<long>(MaxPartyBlockingIds);
			var seen = new HashSet<long>();
			if (characterId > 0 && seen.Add(characterId))
			{
				blocking.Add(characterId);
			}
			if (partyCharacterIds != null)
			{
				for (int i = 0; i < partyCharacterIds.Count && blocking.Count < MaxPartyBlockingIds; ++i)
				{
					long memberId = partyCharacterIds[i];
					if (memberId > 0 && seen.Add(memberId))
					{
						blocking.Add(memberId);
					}
				}
			}

			long owningPartyId = partyId > 0 ? partyId : 0L;

			/* Taken once, outside the retried delegate. A retry after a reply lost past the commit
			 * answers with the instance its first attempt queued. The guard below used to find that
			 * instance and report 0, "the party already holds one", for the party's own new instance;
			 * and the unguarded branch inserted a second (issue #267). */
			Guid requestKey = Guid.NewGuid();

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* One statement, so no other member of the party can insert between the existence
				 * check and this insert. scene_server_id and scene_handle are written as 0 because
				 * no scene server owns the row yet — DequeueAsync hands it to one and stamps the
				 * claim, and SetReadyAsync writes the real handle. time_created is the database's
				 * clock; see DatabaseUtcNowSql. */

				/* Fixed parameter offsets. {0}..{10} are the row values, the request key ({5}) and
				 * the blocking statuses; the variable-length member id list starts at {11}. Held as
				 * named locals rather than interpolated arithmetic because `{{expr}}` inside an
				 * interpolated verbatim string is an escaped literal brace, not a value — a mistake
				 * this method has already made once. */
				const int FirstBlockingIndex = 11;
				const int KeyIndex = 5;

				var ids = new System.Text.StringBuilder();
				for (int i = 0; i < blocking.Count; ++i)
				{
					if (i > 0)
					{
						ids.Append(", ");
					}
					ids.Append('{').Append(FirstBlockingIndex + i).Append('}');
				}

				/* Two ways to already hold an instance, and both have to block.
				 *
				 * By member id, which catches an instance opened by somebody who is in the party
				 * right now; and by party id, which catches one opened by somebody who no longer
				 * is. The second is what stops a party whose original opener has left or logged
				 * out from opening a second copy of a dungeon they are still standing in — and,
				 * read the other way, it is what lets the remaining members still find it.
				 *
				 * The guard deliberately does NOT match on scene_name. One instance per party,
				 * not one per party per dungeon: scoped to the name, a party could hold a live
				 * copy of every dungeon on the shard at once — open one, walk out, open the next —
				 * each holding a full physics scene and a scene row until its own idle timeout
				 * expired. The scene_name in the inserted row is which dungeon this request is
				 * for; the NOT EXISTS below is "does this party already have one open at all".
				 *
				 * It does not match on difficulty either, for the same reason. Opening the same
				 * dungeon again on Hard is still a second instance. */
				var heldClauses = new List<string>(2);
				if (blocking.Count > 0)
				{
					heldClauses.Add($"character_id IN ({ids})");
				}
				if (owningPartyId > 0)
				{
					heldClauses.Add("(party_id <> 0 AND party_id = {6})");
				}

				/* Arenas count. An arena instance row names only its first seat, so a member sitting
				 * in a live arena match is found through the match's seats rather than through
				 * scenes.character_id. One instance per party means one of either kind.
				 *
				 * AND NOT EXISTS, appended to the held-instance NOT EXISTS below. This was written
				 * `OR EXISTS (...)`, which inverted it: the predicate read "holds nothing OR sits in
				 * a live arena", so a member in a live match made the insert run unconditionally —
				 * opening a dungeon the arena seat was meant to block, and a second instance for a
				 * party that already held one (issue #267 audit). */
				string arenaClause = null;
				if (blocking.Count > 0)
				{
					string matchTable = dbContext.GetTableName<Entities.ArenaMatchEntity>();
					string memberTable = dbContext.GetTableName<Entities.ArenaMatchMemberEntity>();
					arenaClause = $@"AND NOT EXISTS (
							SELECT 1 FROM {memberTable} m
							JOIN {matchTable} am ON am.id = m.match_id
							WHERE m.character_id IN ({ids}) AND am.status < {{{FirstBlockingIndex + blocking.Count}}}
						)";
				}

				string sql;
				if (heldClauses.Count == 0)
				{
					// Nothing to guard against — an ungrouped insert with no requester id. The
					// same statement without the held-instance NOT EXISTS.
					sql = $@"WITH mine AS (
							SELECT id FROM {TableName} WHERE request_key = {{{KeyIndex}}}
						),
						ins AS (
							INSERT INTO {TableName}
								(world_server_id, scene_server_id, scene_name, scene_handle, scene_status, scene_type, character_id, character_count, time_created, party_id, difficulty, is_private, request_key)
							SELECT {{0}}, 0, {{1}}, 0, {{2}}, {{3}}, {{4}}, 0, {DatabaseUtcNowSql}, {{6}}, {{7}}, {{8}}, {{{KeyIndex}}}
							WHERE NOT EXISTS (SELECT 1 FROM mine)
							RETURNING id
						)
						SELECT id FROM mine UNION ALL SELECT id FROM ins";
				}
				else
				{
					sql = $@"WITH mine AS (
							SELECT id FROM {TableName} WHERE request_key = {{{KeyIndex}}}
						),
						ins AS (
							INSERT INTO {TableName}
								(world_server_id, scene_server_id, scene_name, scene_handle, scene_status, scene_type, character_id, character_count, time_created, party_id, difficulty, is_private, request_key)
							SELECT {{0}}, 0, {{1}}, 0, {{2}}, {{3}}, {{4}}, 0, {DatabaseUtcNowSql}, {{6}}, {{7}}, {{8}}, {{{KeyIndex}}}
							WHERE NOT EXISTS (SELECT 1 FROM mine)
							AND NOT EXISTS (
								SELECT 1 FROM {TableName}
								WHERE world_server_id = {{0}}
									AND scene_type IN ({{3}}, {{{FirstBlockingIndex + blocking.Count + 1}}})
									AND scene_status IN ({{2}}, {{9}}, {{10}})
									AND ({string.Join(" OR ", heldClauses)})
							) {arenaClause}
							RETURNING id
						)
						SELECT id FROM mine UNION ALL SELECT id FROM ins";
				}

				// Two trailing parameters after the member ids: the live-match status ceiling, and
				// the arena scene type, so an arena instance held by a member blocks a dungeon too.
				var parameters = new object[FirstBlockingIndex + blocking.Count + 2];
				parameters[FirstBlockingIndex + blocking.Count] = (int)ArenaMatchStatus.Ended;
				parameters[FirstBlockingIndex + blocking.Count + 1] = ArenaSceneType;
				parameters[0] = worldServerId;
				parameters[1] = sceneName;
				parameters[2] = (int)SceneStatus.Pending;
				parameters[3] = (int)sceneType;
				parameters[4] = characterId;
				parameters[KeyIndex] = requestKey;
				parameters[6] = owningPartyId;
				parameters[7] = difficulty;
				parameters[8] = isPrivate;
				parameters[9] = (int)SceneStatus.Loading;
				parameters[10] = (int)SceneStatus.Ready;
				for (int i = 0; i < blocking.Count; ++i)
				{
					parameters[FirstBlockingIndex + i] = blocking[i];
				}

				return await ExecuteReturningOrDefaultAsync(
					dbContext,
					sql,
					parameters,
					reader => reader.GetInt64(0),
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			// Zero means a party member already holds a usable instance, which is the whole point
			// of this method and is reported as success.
			return result;
		}

		/// <summary>
		/// Last value handed out by <see cref="NextClaimToken"/>. Starts at a random point so two
		/// incarnations of one scene server do not issue the same run of tokens.
		/// </summary>
		private static int claimTokenSequence = new Random().Next();

		/// <summary>
		/// A dequeue claim token: negative, and distinct from every other token this process has
		/// issued for the next 2^31 claims.
		/// </summary>
		/// <remarks>
		/// Written into <c>scene_handle</c> by <see cref="DequeueAsync"/>, where it identifies one call
		/// among the loads the same scene server has in flight. Negative so it can never be read as a
		/// real handle; distinct rather than random so two in-flight claims of one server cannot
		/// share one. Tokens from a previous incarnation of the server do not matter: that server
		/// deletes its own rows when it starts (<see cref="DeleteBySceneServerAsync"/>).
		/// </remarks>
		private static int NextClaimToken()
		{
			int n = Interlocked.Increment(ref claimTokenSequence) & int.MaxValue;
			return -1 - n;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<(SceneData Scene, double AgeSeconds)>> DequeueAsync(long sceneServerId, CancellationToken cancellationToken = default)
		{
			if (sceneServerId <= 0)
			{
				return DatabaseResult<(SceneData Scene, double AgeSeconds)>.Failure(DatabaseErrorCodes.ValidationError, "Invalid scene server ID.");
			}

			/* Taken once, outside the retried delegate: it is what lets a retry recognise the row its
			 * first attempt claimed. See ISceneService.DequeueAsync. */
			int claimToken = NextClaimToken();

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* The age rides back with the row, measured by the database clock that stamped it.
				 * The scene server that takes this row bounds the instance's lifetime from its
				 * creation, and it can only do that honestly from an age neither host clock took
				 * part in. See DatabaseUtcNowSql.
				 *
				 * The party, difficulty and privacy are carried through the dequeue, not looked up
				 * afterwards. The scene server that dequeues a row is the one that will host the
				 * instance, and the difficulty is what tells it which ruleset to apply — a second
				 * round trip to fetch it would leave a window in which the scene exists with no
				 * rules. */
				/* The claim is recorded on the row, and a retry looks for it first.
				 *
				 * A dequeue used to be a bare status flip. A retry after a reply lost past the commit
				 * could not tell its own committed claim from anybody else's Loading row, so it took
				 * the next pending row as well, and the first sat in Loading — owned by nobody, since
				 * scene_server_id was still 0 — until the world server's age sweep reaped it five
				 * minutes later, with every player waiting on it waiting that long.
				 *
				 * "mine" is the row this call already claimed, found by claimant and token; only when
				 * there is none is a pending row taken. One statement, so the look and the claim
				 * cannot be separated by another caller. "mine" reads the committed row as it is;
				 * the claim's own values come back through RETURNING, because a statement's outer
				 * SELECT does not see what its own UPDATE wrote. */
				var sql = $@"WITH mine AS (
						SELECT {SceneRowColumnsSql}, {SceneAgeSecondsSql}
						FROM {TableName} AS s
						WHERE s.scene_server_id = {{2}}
							AND s.scene_handle = {{3}}
							AND s.scene_status = {{1}}
						LIMIT 1
					),
					scene_to_update AS (
						SELECT id FROM {TableName}
						WHERE scene_status = {{0}}
							AND NOT EXISTS (SELECT 1 FROM mine)
						ORDER BY time_created, id
						FOR UPDATE SKIP LOCKED
						LIMIT 1
					),
					claimed AS (
						UPDATE {TableName} AS s
						SET scene_status = {{1}},
							scene_server_id = {{2}},
							scene_handle = {{3}}
						FROM scene_to_update
						WHERE s.id = scene_to_update.id
						RETURNING {SceneRowColumnsSql}, {SceneAgeSecondsSql}
					)
					SELECT * FROM mine
					UNION ALL
					SELECT * FROM claimed";

				var pendingStatus = (int)SceneStatus.Pending;
				var loadingStatus = (int)SceneStatus.Loading;

				return await ExecuteReturningOrDefaultAsync<(SceneData Scene, double AgeSeconds)?>(
					dbContext,
					sql,
					new object[] { pendingStatus, loadingStatus, sceneServerId, claimToken },
					reader => (ReadSceneRow(reader), ReadSceneAge(reader)),
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			// Convert null result to business logic failure (not an exception case)
			if (result.IsSuccess && result.Data == null)
			{
				return DatabaseResult<(SceneData Scene, double AgeSeconds)>.Failure(DatabaseErrorCodes.NotFound, "No pending scenes available.");
			}

			// If failed, propagate the failure
			if (!result.IsSuccess)
			{
				return DatabaseResult<(SceneData Scene, double AgeSeconds)>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			// Success with data (checked for null above)
			return DatabaseResult<(SceneData Scene, double AgeSeconds)>.Success(result.Data!.Value);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateStatusAsync(long sceneId, SceneStatus status, CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid scene ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET scene_status = {{0}}
					WHERE id = {{1}}";

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { (int)status, sceneId },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("Scene", sceneId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <summary>
		/// Upper bound on the scene ids one batched statement names.
		/// </summary>
		/// <remarks>
		/// Every caller already passes a bounded set (a pulse's closures, one sweep's expiries, one
		/// routing batch's instances); this is the second bound, so a future caller that forgets the
		/// first cannot turn one call into an unbounded statement.
		/// </remarks>
		private const int MaxSceneIdsPerStatement = 4096;

		/// <summary>
		/// The distinct positive ids in <paramref name="sceneIds"/>, ascending, capped at
		/// <see cref="MaxSceneIdsPerStatement"/>.
		/// </summary>
		/// <remarks>
		/// Ascending so that every batched statement over scene rows takes its row locks in the same
		/// order, which is what keeps two of them from deadlocking on an overlapping set.
		/// </remarks>
		private static long[] ToSceneIdArray(IEnumerable<long> sceneIds)
		{
			if (sceneIds == null)
			{
				return Array.Empty<long>();
			}

			var ids = new SortedSet<long>();
			foreach (long id in sceneIds)
			{
				if (id > 0)
				{
					ids.Add(id);
					if (ids.Count >= MaxSceneIdsPerStatement)
					{
						break;
					}
				}
			}

			var array = new long[ids.Count];
			ids.CopyTo(array);
			return array;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> UpdateStatusManyAsync(IReadOnlyCollection<long> sceneIds, SceneStatus status, CancellationToken cancellationToken = default)
		{
			long[] ids = ToSceneIdArray(sceneIds);
			if (ids.Length == 0)
			{
				return DatabaseResult<int>.Success(0);
			}

			// Absolute, so a retry after a reply lost past the commit writes the same thing again.
			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET scene_status = {{0}}
					WHERE id = ANY({{1}}::bigint[])";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { (int)status, ids },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SetReadyAsync(
			long sceneId,
			long sceneServerId,
			long worldServerId,
			string sceneName,
			int sceneHandle,
			CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0 || sceneServerId <= 0 || worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: scene ID, scene server ID, world server ID, and scene name are required.");
			}

			// Still performs a best-effort "already ready" check to be robust to in-call retries.
			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				/* Addressed by ID: this is the row the caller dequeued and loaded, and only that
				 * row may be told where the resulting scene instance lives. Selecting "the oldest
				 * loading row with this name" instead meant two concurrent loads of the same
				 * scene could each stamp their server and handle onto the other's row — which,
				 * for an instanced scene, hands a character the instance created for somebody
				 * else, because character_id stays with the row.
				 *
				 * scene_name is still matched as a consistency check so a caller that passes a
				 * mismatched ID fails rather than silently rewriting an unrelated row. */
				var claimSql = $@"WITH claimable_scene AS (
						SELECT id FROM {TableName}
						WHERE id = {{0}}
							AND world_server_id = {{1}}
							AND scene_name = {{2}}
							AND scene_status = {{3}}
						FOR UPDATE
					)
					UPDATE {TableName}
					SET scene_status = {{4}},
						scene_server_id = {{5}},
						scene_handle = {{6}}
					FROM claimable_scene
					WHERE {TableName}.id = claimable_scene.id
					RETURNING {TableName}.id";

				var claimedId = await ExecuteReturningOrDefaultAsync(
					dbContext,
					claimSql,
					new object[] { sceneId, worldServerId, sceneName, (int)SceneStatus.Loading, (int)SceneStatus.Ready, sceneServerId, sceneHandle },
					reader => reader.GetInt64(0),
					cancellationToken).ConfigureAwait(false);

				if (claimedId > 0)
				{
					return (long?)claimedId;
				}

				// Fallback: check if already ready (idempotency on retry)
				var alreadyReadyId = await dbContext.Scenes
					.AsNoTracking()
					.Where(s =>
						s.ID == sceneId
						&& s.WorldServerID == worldServerId
						&& s.SceneName == sceneName
						&& s.SceneStatus == (int)SceneStatus.Ready
						&& s.SceneServerID == sceneServerId
						&& s.SceneHandle == sceneHandle)
					.Select(s => (long?)s.ID)
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				return alreadyReadyId;
			}).ConfigureAwait(false);

			return result.IsSuccess
				? (result.Data.HasValue ? DatabaseResult.Success() : DatabaseResult.Failure(DatabaseErrorCodes.NotFound, $"Scene {sceneId} could not be claimed as ready."))
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long sceneId, int characterCount, CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid scene ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				// Addressed by row id: a scene handle is process-local. See ISceneService.PulseAsync.
				var sql = $@"UPDATE {TableName}
					SET character_count = {{0}}
					WHERE id = {{1}}";

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { characterCount, sceneId },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("Scene", sceneId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long sceneId, CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid scene ID.");
			}

			// Deliberately not throwing on zero rows: both callers are deleting a scene they have
			// already stopped serving, and a row someone else reaped first is the same outcome.
			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE id = {{0}}";
				await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { sceneId },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteManyAsync(IReadOnlyCollection<long> sceneIds, CancellationToken cancellationToken = default)
		{
			long[] ids = ToSceneIdArray(sceneIds);
			if (ids.Length == 0)
			{
				return DatabaseResult<int>.Success(0);
			}

			// Idempotent for the same reason as DeleteAsync: a row already gone is the outcome asked for.
			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE id = ANY({{0}}::bigint[])";
				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { ids },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteBySceneServerAsync(long sceneServerId, CancellationToken cancellationToken = default)
		{
			if (sceneServerId <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Invalid scene server ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE scene_server_id = {{0}}";
				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { sceneServerId },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteByWorldServerAsync(long worldServerId, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Invalid world server ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE world_server_id = {{0}}";
				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { worldServerId },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneData>> FetchCharacterInstanceAsync(
			long characterId,
			SceneType sceneType,
			long worldServerId,
			string sceneName,
			CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<SceneData>.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			if (worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<SceneData>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID and scene name are required.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var type = (int)sceneType;
				var scene = await getCharacterInstanceQuery(dbContext, characterId, type, worldServerId, sceneName, cancellationToken).ConfigureAwait(false);

				if (scene == null)
				{
					throw new DatabaseEntityNotFoundException("Scene", $"character {characterId}, type {sceneType}, world {worldServerId}, scene {sceneName}");
				}

				return MapEntityToDto(scene);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<SceneData>>> FetchCharacterInstancesAsync(
			IReadOnlyList<long> characterIds,
			SceneType sceneType,
			long worldServerId,
			long partyId = 0,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<IReadOnlyList<SceneData>>.Failure(DatabaseErrorCodes.ValidationError, "Invalid world server ID.");
			}

			var ids = new List<long>();
			var seen = new HashSet<long>();
			if (characterIds != null)
			{
				for (int i = 0; i < characterIds.Count && ids.Count < MaxPartyBlockingIds; ++i)
				{
					long characterId = characterIds[i];
					if (characterId > 0 && seen.Add(characterId))
					{
						ids.Add(characterId);
					}
				}
			}

			long owningPartyId = partyId > 0 ? partyId : 0L;

			if (ids.Count == 0 && owningPartyId == 0)
			{
				return DatabaseResult<IReadOnlyList<SceneData>>.Success(Array.Empty<SceneData>());
			}

			int type = (int)sceneType;
			int pending = (int)SceneStatus.Pending;
			int loading = (int)SceneStatus.Loading;
			int ready = (int)SceneStatus.Ready;

			return await ExecuteReadAsync<IReadOnlyList<SceneData>>(async dbContext =>
			{
				/* Matched by party as well as by member id, and this is what closes the re-entry
				 * lockout. An instance is recorded against the character who opened it, so a party
				 * whose opener has since left it — or logged out and been dropped from it — could
				 * no longer resolve the dungeon its members were still standing in: the finder saw
				 * nothing, opened a second instance, and split the group. Matching on party_id
				 * finds it regardless of who created it, which is also what lets a member walk out
				 * to the entrance and walk straight back in. */
				var scenes = await dbContext.Scenes
					.AsNoTracking()
					.Where(s => (ids.Contains(s.CharacterID) || (owningPartyId != 0 && s.PartyID == owningPartyId)) &&
								s.SceneType == type &&
								s.WorldServerID == worldServerId &&
								(s.SceneStatus == pending || s.SceneStatus == loading || s.SceneStatus == ready))
					// Newest first, so a caller taking the first match of a given scene name gets
					// the most recently opened one — the same rule FetchCharacterInstanceAsync uses.
					.OrderByDescending(s => s.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				IReadOnlyList<SceneData> data = scenes.Select(MapEntityToDto).ToList();
				return data;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<SceneData>>> FetchJoinableInstancesAsync(
			long worldServerId,
			string sceneName,
			int difficulty,
			SceneType sceneType,
			int maxClients,
			int maxRows = 32,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<IReadOnlyList<SceneData>>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID and scene name are required.");
			}

			/* Bounded here as well as by the caller. This answers a request a client can repeat,
			 * and the reply is serialised into a broadcast — an unbounded row count would let a
			 * shard with many open instances produce a message large enough to be a problem in
			 * itself, independently of how often it is asked for. */
			int rowLimit = maxRows < 1 ? 1 : (maxRows > 128 ? 128 : maxRows);
			int capacity = maxClients < 1 ? 1 : maxClients;
			int type = (int)sceneType;
			int pending = (int)SceneStatus.Pending;
			int loading = (int)SceneStatus.Loading;
			int ready = (int)SceneStatus.Ready;

			return await ExecuteReadAsync<IReadOnlyList<SceneData>>(async dbContext =>
			{
				/* Instances that are still loading are listed deliberately.
				 *
				 * A party that has just opened a dungeon spends several seconds in Pending and
				 * Loading, and that is exactly the window in which a straggler is most likely to
				 * be looking for them. Hiding it would show an empty list to somebody whose group
				 * is right there, and they would open a second copy — the split-party failure the
				 * one-instance rule exists to prevent, arrived at through the finder instead of
				 * around it.
				 *
				 * Private instances are excluded, and full ones are excluded, because neither can
				 * be joined; offering a row whose Join button is guaranteed to be refused is
				 * worse than not offering it. */
				var scenes = await dbContext.Scenes
					.AsNoTracking()
					.Where(s => s.WorldServerID == worldServerId &&
								s.SceneName == sceneName &&
								s.SceneType == type &&
								s.Difficulty == difficulty &&
								!s.IsPrivate &&
								s.CharacterCount < capacity &&
								(s.SceneStatus == pending || s.SceneStatus == loading || s.SceneStatus == ready))
					// Oldest first: a run that has been going longest is the one closest to
					// needing a replacement, and a stable order stops rows jumping under the
					// player's cursor between refreshes.
					.OrderBy(s => s.ID)
					.Take(rowLimit)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				IReadOnlyList<SceneData> data = scenes.Select(MapEntityToDto).ToList();
				return data;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> SetInstancePrivacyAsync(
			long sceneId,
			long requiredPartyId,
			long requiredCharacterId,
			bool isPrivate,
			CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0)
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "Invalid scene ID.");
			}

			if (requiredPartyId <= 0 && requiredCharacterId <= 0)
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "An owning party or character is required.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* Ownership is re-asserted in the UPDATE itself rather than checked first.
				 *
				 * The caller has already authorised the request against the party roster it holds
				 * in memory, but that roster is a cache: a leader can be demoted, or the instance
				 * handed to another party's row id, between the check and the write. Folding the
				 * ownership test into the statement means a stale authorisation updates zero rows
				 * instead of flipping somebody else's dungeon private.
				 *
				 * A party-owned instance is matched on the party; an ungrouped one has party_id 0
				 * and is matched on the character who opened it. */
				var sql = $@"UPDATE {TableName}
					SET is_private = {{0}}
					WHERE id = {{1}}
						AND (({{2}} <> 0 AND party_id = {{2}}) OR (party_id = 0 AND character_id = {{3}}))";

				int affected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { isPrivate, sceneId, requiredPartyId, requiredCharacterId },
					cancellationToken).ConfigureAwait(false);

				return affected > 0;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}
		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneData>> FetchAsync(long sceneId, CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0)
			{
				return DatabaseResult<SceneData>.Failure(DatabaseErrorCodes.ValidationError, "Invalid scene ID.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var scene = await fetchByIdQuery(dbContext, sceneId, cancellationToken).ConfigureAwait(false);

				if (scene == null)
				{
					throw new DatabaseEntityNotFoundException("Scene", sceneId.ToString());
				}

				return MapEntityToDto(scene);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<(SceneData Scene, double AgeSeconds)>>> FetchWithAgesAsync(
			IReadOnlyCollection<long> sceneIds,
			CancellationToken cancellationToken = default)
		{
			long[] ids = ToSceneIdArray(sceneIds);
			if (ids.Length == 0)
			{
				return DatabaseResult<IReadOnlyList<(SceneData Scene, double AgeSeconds)>>.Success(Array.Empty<(SceneData, double)>());
			}

			return await ExecuteReadAsync<IReadOnlyList<(SceneData Scene, double AgeSeconds)>>(async dbContext =>
			{
				var sql = $@"SELECT {SceneRowColumnsSql}, {SceneAgeSecondsSql}
					FROM {TableName} AS s
					WHERE s.id = ANY({{0}}::bigint[])";

				return await ReadRowsAsync(
					dbContext,
					sql,
					new object[] { ids },
					reader => (ReadSceneRow(reader), ReadSceneAge(reader)),
					cancellationToken).ConfigureAwait(false);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<SceneData>>> FetchAvailableAsync(
			long worldServerId,
			string sceneName,
			int maxClients,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<IReadOnlyList<SceneData>>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID and scene name are required.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var readyStatus = (int)SceneStatus.Ready;
				var scenes = await fetchAvailableQuery(dbContext, worldServerId, sceneName, maxClients, readyStatus).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				IReadOnlyList<SceneData> data = scenes.Select(MapEntityToDto).ToList();
				return data;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<SceneData>>> FetchManyAsync(long worldServerId, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<IReadOnlyList<SceneData>>.Failure(DatabaseErrorCodes.ValidationError, "Invalid world server ID.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var readyStatus = (int)SceneStatus.Ready;
				var scenes = await fetchReadyQuery(dbContext, worldServerId, readyStatus).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				IReadOnlyList<SceneData> data = scenes.Select(MapEntityToDto).ToList();
				return data;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> SumCharacterCountAsync(long worldServerId, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Invalid world server ID.");
			}

			// The same rows FetchManyAsync returns, summed where they are rather than shipped here.
			return await ExecuteReadAsync(async dbContext =>
			{
				var sql = $@"SELECT COALESCE(SUM(character_count), 0)::bigint
					FROM {TableName}
					WHERE world_server_id = {{0}} AND scene_status = {{1}}";

				long total = await ExecuteScalarLongAsync(
					dbContext,
					sql,
					new object[] { worldServerId, (int)SceneStatus.Ready },
					cancellationToken).ConfigureAwait(false);

				return total > int.MaxValue ? int.MaxValue : (int)Math.Max(0L, total);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> PulseBatchAsync(
			List<(long sceneId, int characterCount)> pulses,
			int maxBatchSize = 1000,
			CancellationToken cancellationToken = default)
		{
			if (pulses == null || pulses.Count == 0)
			{
				return DatabaseResult<int>.Success(0);
			}

			if (maxBatchSize < 500) maxBatchSize = 500;
			else if (maxBatchSize > 2500) maxBatchSize = 2500;

			int totalRowsAffected = 0;

			for (int offset = 0; offset < pulses.Count; offset += maxBatchSize)
			{
				var batchCount = Math.Min(maxBatchSize, pulses.Count - offset);

				// Build parallel arrays for PostgreSQL unnest.
				var sceneIds = new long[batchCount];
				var counts = new int[batchCount];
				for (int i = 0; i < batchCount; i++)
				{
					var (sceneId, count) = pulses[offset + i];
					sceneIds[i] = sceneId;
					counts[i] = count;
				}

				var result = await ExecuteWriteAsync(async dbContext =>
				{
					// Use unnest to efficiently join an array of values into an UPDATE.
					// Addressed by row id: a scene handle is process-local.
					var sql = $@"UPDATE {TableName} AS t
						SET character_count = batch.new_count
						FROM unnest({{0}}::bigint[], {{1}}::int[]) AS batch(scene_id, new_count)
						WHERE t.id = batch.scene_id";

					return await dbContext.Database.ExecuteSqlRawAsync(
						sql,
						new object[] { sceneIds, counts },
						cancellationToken).ConfigureAwait(false);
				}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

				if (!result.IsSuccess)
				{
					return DatabaseResult<int>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
				}

				totalRowsAffected += result.Data;
			}

			return DatabaseResult<int>.Success(totalRowsAffected);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteStaleUnreadyAsync(
			long worldServerId,
			double minAgeSeconds,
			int maxRows = 256,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Invalid world server ID.");
			}

			if (maxRows < 1)
			{
				maxRows = 1;
			}
			else if (maxRows > 4096)
			{
				maxRows = 4096;
			}

			if (!(minAgeSeconds > 0.0))
			{
				minAgeSeconds = 0.0;
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* SKIP LOCKED so a row a scene server is concurrently dequeuing is left to it
				 * rather than deleted out from under an in-flight load.
				 *
				 * The cutoff is taken from the database clock that stamped time_created, not passed
				 * in from the caller's host. See DatabaseUtcNowSql. */
				var sql = $@"WITH stale AS (
						SELECT id FROM {TableName}
						WHERE world_server_id = {{0}}
							AND scene_status <> {{1}}
							AND time_created < {DatabaseUtcNowSql} - make_interval(secs => {{2}})
						ORDER BY time_created, id
						FOR UPDATE SKIP LOCKED
						LIMIT {{3}}
					)
					DELETE FROM {TableName}
					USING stale
					WHERE {TableName}.id = stale.id";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { worldServerId, (int)SceneStatus.Ready, minAgeSeconds, maxRows },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteByStaleSceneServersAsync(
			long worldServerId,
			double pulseStaleSeconds,
			int maxRows = 256,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Invalid world server ID.");
			}

			/* Refused rather than clamped. A window of zero (or a NaN, which compares false with
			 * everything) calls every scene server dead, and this statement would then delete every
			 * scene row the world owns, live instances and their players included. */
			if (!(pulseStaleSeconds > 0.0))
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "The pulse staleness window must be positive.");
			}

			if (maxRows < 1)
			{
				maxRows = 1;
			}
			else if (maxRows > 4096)
			{
				maxRows = 4096;
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* NOT EXISTS covers both halves of "the host is gone": a scene server that
				 * deregistered (no row) and one that crashed (row present, pulse stopped). A
				 * plain join against scene_servers would silently keep the first case.
				 *
				 * The cutoff is the database clock minus the window, the same clock that stamped
				 * last_pulse. It used to be an instant computed on the world server's host, so the
				 * sweep's idea of "stale" moved with that host's clock. */
				var sql = $@"WITH orphaned AS (
						SELECT s.id FROM {TableName} AS s
						WHERE s.world_server_id = {{0}}
							AND s.scene_server_id <> 0
							AND NOT EXISTS (
								SELECT 1 FROM scene_servers AS ss
								WHERE ss.id = s.scene_server_id
									AND ss.last_pulse >= {DatabaseUtcNowSql} - make_interval(secs => {{1}})
							)
						ORDER BY s.id
						FOR UPDATE SKIP LOCKED
						LIMIT {{2}}
					)
					DELETE FROM {TableName}
					USING orphaned
					WHERE {TableName}.id = orphaned.id";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { worldServerId, pulseStaleSeconds, maxRows },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <summary>
		/// Maps SceneEntity to SceneData DTO.
		/// </summary>
		/// <param name="entity">Scene entity from database.</param>
		/// <returns>Scene data DTO.</returns>
		private SceneData MapEntityToDto(SceneEntity entity)
		{
			return new SceneData(
				id: entity.ID,
				sceneServerID: entity.SceneServerID,
				worldServerID: entity.WorldServerID,
				sceneName: entity.SceneName,
				sceneHandle: entity.SceneHandle,
				sceneStatus: entity.SceneStatus,
				sceneType: entity.SceneType,
				characterID: entity.CharacterID,
				characterCount: entity.CharacterCount,
				timeCreated: entity.TimeCreated,
				partyID: entity.PartyID,
				difficulty: entity.Difficulty,
				isPrivate: entity.IsPrivate
			);
		}
	}
}