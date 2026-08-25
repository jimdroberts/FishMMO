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

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> EnqueueAsync(
			long worldServerId,
			string sceneName,
			SceneType sceneType,
			long characterId = 0,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID and scene name are required.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var entity = new SceneEntity
				{
					WorldServerID = worldServerId,
					SceneName = sceneName,
					SceneType = (int)sceneType,
					SceneStatus = (int)SceneStatus.Pending,
					CharacterID = characterId,
					TimeCreated = DateTime.UtcNow
				};
				await dbContext.Scenes.AddAsync(entity, cancellationToken).ConfigureAwait(false);
				return entity;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!result.IsSuccess)
			{
				return DatabaseResult<long>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			if (result.Data.ID <= 0)
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.DatabaseError, "Failed to enqueue scene.", isTransient: true);
			}

			return DatabaseResult<long>.Success(result.Data.ID);
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

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* One statement, so the "how many are already coming?" count and the insert
				 * cannot be interleaved by a second caller. scene_server_id and scene_handle are
				 * written as 0 because no scene server owns the row yet — DequeueAsync hands it
				 * to one, and SetReadyAsync stamps both. */
				var sql = $@"INSERT INTO {TableName}
						(world_server_id, scene_server_id, scene_name, scene_handle, scene_status, scene_type, character_id, character_count, time_created)
					SELECT {{0}}, 0, {{1}}, 0, {{2}}, {{3}}, 0, 0, {{4}}
					WHERE (
						SELECT COUNT(*) FROM {TableName}
						WHERE world_server_id = {{0}}
							AND scene_name = {{1}}
							AND scene_type = {{3}}
							AND scene_status IN ({{2}}, {{5}})
					) < {{6}}
					RETURNING id";

				return await ExecuteReturningOrDefaultAsync(
					dbContext,
					sql,
					new object[]
					{
						worldServerId,
						sceneName,
						(int)SceneStatus.Pending,
						(int)sceneType,
						DateTime.UtcNow,
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

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* One statement, so no other member of the party can insert between the existence
				 * check and this insert. scene_server_id and scene_handle are written as 0 because
				 * no scene server owns the row yet — DequeueAsync hands it to one, and SetReadyAsync
				 * stamps both. */

				/* Fixed parameter offsets. {0}..{10} are the row values and the blocking statuses;
				 * the variable-length member id list starts at {11}. Held as named locals rather
				 * than interpolated arithmetic because `{{expr}}` inside an interpolated verbatim
				 * string is an escaped literal brace, not a value — a mistake this method has
				 * already made once. */
				const int FirstBlockingIndex = 11;

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

				string sql;
				if (heldClauses.Count == 0)
				{
					// Nothing to guard against — an ungrouped insert with no requester id. The
					// unconditional insert is the same statement without the NOT EXISTS.
					sql = $@"INSERT INTO {TableName}
							(world_server_id, scene_server_id, scene_name, scene_handle, scene_status, scene_type, character_id, character_count, time_created, party_id, difficulty, is_private)
						VALUES ({{0}}, 0, {{1}}, 0, {{2}}, {{3}}, {{4}}, 0, {{5}}, {{6}}, {{7}}, {{8}})
						RETURNING id";
				}
				else
				{
					sql = $@"INSERT INTO {TableName}
							(world_server_id, scene_server_id, scene_name, scene_handle, scene_status, scene_type, character_id, character_count, time_created, party_id, difficulty, is_private)
						SELECT {{0}}, 0, {{1}}, 0, {{2}}, {{3}}, {{4}}, 0, {{5}}, {{6}}, {{7}}, {{8}}
						WHERE NOT EXISTS (
							SELECT 1 FROM {TableName}
							WHERE world_server_id = {{0}}
								AND scene_type = {{3}}
								AND scene_status IN ({{2}}, {{9}}, {{10}})
								AND ({string.Join(" OR ", heldClauses)})
						)
						RETURNING id";
				}

				var parameters = new object[FirstBlockingIndex + blocking.Count];
				parameters[0] = worldServerId;
				parameters[1] = sceneName;
				parameters[2] = (int)SceneStatus.Pending;
				parameters[3] = (int)sceneType;
				parameters[4] = characterId;
				parameters[5] = DateTime.UtcNow;
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

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneData>> DequeueAsync(CancellationToken cancellationToken = default)
		{
			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"WITH scene_to_update AS (
						SELECT id FROM {TableName}
						WHERE scene_status = {{0}}
						ORDER BY time_created, id
						FOR UPDATE SKIP LOCKED
						LIMIT 1
						)
						UPDATE {TableName}
						SET scene_status = {{1}}
						FROM scene_to_update
						WHERE {TableName}.id = scene_to_update.id
						RETURNING {TableName}.id, {TableName}.world_server_id, {TableName}.scene_server_id, {TableName}.scene_name, {TableName}.scene_handle, {TableName}.scene_status, {TableName}.scene_type, {TableName}.character_id, {TableName}.character_count, {TableName}.time_created, {TableName}.party_id, {TableName}.difficulty, {TableName}.is_private";

				var pendingStatus = (int)SceneStatus.Pending;
				var loadingStatus = (int)SceneStatus.Loading;

				var entity = await ExecuteReturningOrDefaultAsync(
					dbContext,
					sql,
					new object[] { pendingStatus, loadingStatus },
					reader => new SceneEntity
					{
						ID = reader.GetInt64(0),
						WorldServerID = reader.GetInt64(1),
						SceneServerID = reader.GetInt64(2),
						SceneName = reader.GetString(3),
						SceneHandle = reader.GetInt32(4),
						SceneStatus = reader.GetInt32(5),
						SceneType = reader.GetInt32(6),
						CharacterID = reader.GetInt64(7),
						CharacterCount = reader.GetInt32(8),
						TimeCreated = reader.GetDateTime(9),
						/* Carried through the dequeue, not looked up afterwards. The scene server
						 * that dequeues a row is the one that will host the instance, and the
						 * difficulty is what tells it which ruleset to apply — a second round trip
						 * to fetch it would leave a window in which the scene exists with no rules. */
						PartyID = reader.GetInt64(10),
						Difficulty = reader.GetInt32(11),
						IsPrivate = reader.GetBoolean(12),
					},
					cancellationToken).ConfigureAwait(false);

				return entity != null ? (SceneData?)MapEntityToDto(entity) : null;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			// Convert null result to business logic failure (not an exception case)
			if (result.IsSuccess && result.Data == null)
			{
				return DatabaseResult<SceneData>.Failure(DatabaseErrorCodes.NotFound, "No pending scenes available.");
			}

			// If failed, propagate the failure
			if (!result.IsSuccess)
			{
				return DatabaseResult<SceneData>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			// Success with data (checked for null above)
			return DatabaseResult<SceneData>.Success(result.Data!.Value);
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
			DateTime olderThanUtc,
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

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				// SKIP LOCKED so a row a scene server is concurrently dequeuing is left to it
				// rather than deleted out from under an in-flight load.
				var sql = $@"WITH stale AS (
						SELECT id FROM {TableName}
						WHERE world_server_id = {{0}}
							AND scene_status <> {{1}}
							AND time_created < {{2}}
						ORDER BY time_created, id
						FOR UPDATE SKIP LOCKED
						LIMIT {{3}}
					)
					DELETE FROM {TableName}
					USING stale
					WHERE {TableName}.id = stale.id";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { worldServerId, (int)SceneStatus.Ready, olderThanUtc, maxRows },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteByStaleSceneServersAsync(
			long worldServerId,
			DateTime pulseOlderThanUtc,
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

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* NOT EXISTS covers both halves of "the host is gone": a scene server that
				 * deregistered (no row) and one that crashed (row present, pulse stopped). A
				 * plain join against scene_servers would silently keep the first case. */
				var sql = $@"WITH orphaned AS (
						SELECT s.id FROM {TableName} AS s
						WHERE s.world_server_id = {{0}}
							AND s.scene_server_id <> 0
							AND NOT EXISTS (
								SELECT 1 FROM scene_servers AS ss
								WHERE ss.id = s.scene_server_id
									AND ss.last_pulse >= {{1}}
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
					new object[] { worldServerId, pulseOlderThanUtc, maxRows },
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