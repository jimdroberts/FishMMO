using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Database.Exceptions;
using Microsoft.EntityFrameworkCore;
using FishMMO.Shared;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	public sealed class CharacterService : BaseService<CharacterEntity>, ICharacterService
	{
		private static readonly TimeSpan DefaultSessionLeaseDuration = TimeSpan.FromMinutes(2);

		private enum SaveCharacterWriteOutcome
		{
			Success = 0,
			NotFound = 1,
			AuthorityLost = 2,
			/// <summary>The caller no longer holds the character's session claim.</summary>
			OwnershipLost = 3,
		}

		/// <summary>
		/// Compiled query for FetchAsync (by id) hot path.
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
		/* No (CharacterEntity?) cast on the body, and the nullability warning silenced instead, as
		 * on the queries below. The cast wrapped FirstOrDefault in a Convert node; CompileAsyncQuery
		 * turns FirstOrDefault into its async form and the Convert was left converting a
		 * Task<CharacterEntity> to a CharacterEntity, so every call threw INVALID_OPERATION ("No
		 * coercion operator is defined") — FetchAsync(long) had never returned a character, which
		 * broke adding a friend, the friend list's status at login and naming by id (issue #267). */
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterEntity?>> fetchByIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.FirstOrDefault(c => c.ID == characterId && !c.Deleted));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving character by name (hot path for login/character selection).
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<CharacterEntity?>> fetchByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string nameLower, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.FirstOrDefault(c => c.NameLowercase == nameLower && !c.Deleted));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving character by name with a selected filter.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, bool, CancellationToken, Task<CharacterEntity?>> fetchByNameSelectedQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string nameLower, bool selected, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.FirstOrDefault(c => c.NameLowercase == nameLower && !c.Deleted && c.Selected == selected));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving a single character by account.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<CharacterEntity?>> fetchByAccountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string account, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.FirstOrDefault(c => c.Account == account && !c.Deleted));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving a single character by account with a selected filter.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, bool, CancellationToken, Task<CharacterEntity?>> fetchByAccountSelectedQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string account, bool selected, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.FirstOrDefault(c => c.Account == account && !c.Deleted && c.Selected == selected));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for counting characters by account (hot path for character creation validation).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<int>> countByAccountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string account, CancellationToken ct) =>
				context.Characters
						.AsNoTracking()
						.Where(c => c.Account == account && !c.Deleted)
						.Count());

		/// <summary>
		/// Retrieves all non-deleted characters for an account.
		/// Uses a regular async query — EF Core 5+ caches query plans automatically,
		/// and the EF.CompileAsyncQuery API had a known translation failure with
		/// negated boolean conditions (!c.Deleted) on the Npgsql provider.
		/// </summary>
		private static async Task<List<CharacterEntity>> FetchManyByAccountQueryAsync(
			NpgsqlDbContext context, string account, CancellationToken ct)
		{
			return await context.Characters
				.AsNoTracking()
				.Where(c => c.Account == account && c.Deleted == false)
				.ToListAsync(ct);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">The database context factory.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> CountAsync(string account, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(account))
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			var result = await ExecuteReadAsync(async dbContext =>
				await countByAccountQuery(dbContext, account, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> CreateCharacterAsync(CharacterData characterData, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedCharacterName(characterData.Name))
			{
				return DatabaseResult<long>.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidCharacterNameError);
			}

			if (!Authentication.IsAllowedUsername(characterData.Account))
			{
				return DatabaseResult<long>.Failure(
					DatabaseErrorCodes.ValidationError,
					Authentication.InvalidUsernameError);
			}

			var nameLower = characterData.Name.Trim().ToLowerInvariant();
			var result = await ExecuteWriteAsync<long>(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sceneName = characterData.SceneName ?? string.Empty;
				var bindScene = characterData.BindScene ?? string.Empty;

				var sql = $@"
					WITH inserted AS (
						INSERT INTO {TableName} (
							name,
							account,
							selected,
							world_server_id,
							scene_name,
							scene_handle,
							bind_scene,
							bind_x,
							bind_y,
							bind_z,
							instance_id,
							instance_x,
							instance_y,
							instance_z,
							instance_rot_x,
							instance_rot_y,
							instance_rot_z,
							instance_rot_w,
							race_id,
							model_index,
							x,
							y,
							z,
							rot_x,
							rot_y,
							rot_z,
							rot_w,
							access_level,
							session_state,
							session_owner_server_id,
							session_owner_token,
							session_lease_expires_utc,
							flags,
							version,
							time_created,
							last_saved,
							deleted,
							time_deleted
						)
						VALUES (
							{{0}},
							{{1}},
							{{2}},
							{{3}},
							{{4}},
							{{5}},
							{{6}},
							{{7}},
							{{8}},
							{{9}},
							{{10}},
							{{11}},
							{{12}},
							{{13}},
							{{14}},
							{{15}},
							{{16}},
							{{17}},
							{{18}},
							{{19}},
							{{20}},
							{{21}},
							{{22}},
							{{23}},
							{{24}},
							{{25}},
							{{26}},
							{{27}},
							0,
							0,
							{{28}},
							{{29}},
							{{30}},
							1,
							{{31}},
							{{32}},
							FALSE,
							NULL
						)
						ON CONFLICT (name_lowercase)
						DO NOTHING
						RETURNING id
					)
					SELECT COALESCE((SELECT id FROM inserted LIMIT 1), -1)::bigint AS value";

				var id = await ExecuteScalarLongAsync(
					dbContext,
					sql,
					new object[]
					{
						characterData.Name,
						characterData.Account,
						characterData.Selected,
						characterData.WorldServerID,
						sceneName,
						characterData.SceneHandle,
						bindScene,
						characterData.BindX,
						characterData.BindY,
						characterData.BindZ,
						characterData.InstanceID,
						characterData.InstanceX,
						characterData.InstanceY,
						characterData.InstanceZ,
						characterData.InstanceRotX,
						characterData.InstanceRotY,
						characterData.InstanceRotZ,
						characterData.InstanceRotW,
						characterData.RaceID,
						characterData.ModelIndex,
						characterData.X,
						characterData.Y,
						characterData.Z,
						characterData.RotX,
						characterData.RotY,
						characterData.RotZ,
						characterData.RotW,
						characterData.AccessLevel,
						Guid.Empty,
						DateTime.UnixEpoch,
						characterData.Flags,
						now,
						now
					},
					cancellationToken).ConfigureAwait(false);

				if (id <= 0)
				{
					throw new DatabaseException(
						"Character name already exists.",
						errorCode: DatabaseErrorCodes.AlreadyExists);
				}

				return id;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Persists character state only — it deliberately does not touch the session lease.
		/// It used to extend the lease for any row whose session was Online, without checking
		/// which server was writing, so a stale save from a server that had already released
		/// the character extended the *new* owner's lease. Lease liveness belongs to the
		/// ownership operations (<see cref="TryClaimAsync"/>, <see cref="ReleaseAsync"/>,
		/// <see cref="RefreshSessionLeaseAsync"/>, <see cref="RefreshSessionLeasesAsync"/>),
		/// all of which verify ownership before writing.
		/// <para>
		/// It also does not write <c>selected</c>. That column belongs to the login flow —
		/// character creation sets it, <see cref="SetSelectedAsync"/> moves it — and a gameplay
		/// save has no business asserting it. It used to write <c>true</c> unconditionally, so a
		/// save replayed after the player had already picked a different character (the retry
		/// queue does exactly this after a database hiccup) left two rows marked selected, and
		/// the account would enter the world as whichever one the next lookup returned first.
		/// </para>
		/// </remarks>
		public Task<DatabaseResult> PersistAsync(CharacterData characterData, CancellationToken cancellationToken = default)
			=> PersistInternalAsync(characterData, null, cancellationToken);

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistOwnedAsync(CharacterData characterData, CharacterSessionLeaseData ownership, CancellationToken cancellationToken = default)
		{
			if (!ownership.IsValid || ownership.CharacterID != characterData.ID)
			{
				return Task.FromResult(DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Ownership triple is missing, malformed, or refers to a different character."));
			}
			return PersistInternalAsync(characterData, ownership, cancellationToken);
		}

		/// <summary>
		/// Shared implementation of the character-row write.
		/// </summary>
		/// <param name="characterData">Snapshot to persist.</param>
		/// <param name="ownership">
		/// When supplied, the write additionally requires that the row is still claimed by this
		/// server under this token. See <see cref="PersistOwnedAsync"/>.
		/// </param>
		/// <param name="cancellationToken">Cancellation token.</param>
		private async Task<DatabaseResult> PersistInternalAsync(CharacterData characterData, CharacterSessionLeaseData? ownership, CancellationToken cancellationToken = default)
		{
			if (characterData.ID <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			// Version is required and authoritative.
			// LastSaved is treated as analytics only and is not used for concurrency.
			if (characterData.Version <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid Version. Version must be greater than zero.");
			}

			var saveResult = await ExecuteTransactionAsync<SaveCharacterWriteOutcome>(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sceneName = characterData.SceneName ?? string.Empty;
				var bindScene = characterData.BindScene ?? string.Empty;

				var sql = $@"UPDATE {TableName}
					SET name = {{0}},
						account = {{1}},
						world_server_id = {{2}},
						scene_name = {{3}},
						scene_handle = {{4}},
						bind_scene = {{5}},
						bind_x = {{6}},
						bind_y = {{7}},
						bind_z = {{8}},
						instance_id = {{9}},
						instance_x = {{10}},
						instance_y = {{11}},
						instance_z = {{12}},
						instance_rot_x = {{13}},
						instance_rot_y = {{14}},
						instance_rot_z = {{15}},
						instance_rot_w = {{16}},
						race_id = {{17}},
						model_index = {{18}},
						x = {{19}},
						y = {{20}},
						z = {{21}},
						rot_x = {{22}},
						rot_y = {{23}},
						rot_z = {{24}},
						rot_w = {{25}},
						access_level = {{26}},
						flags = {{27}},
						version = {{28}},
						last_saved = {{29}}
					WHERE id = {{30}} AND deleted = FALSE AND version < {{28}}"
					+ (ownership.HasValue
						? $" AND session_state = {{31}} AND session_owner_server_id = {{32}} AND session_owner_token = {{33}}"
						: string.Empty);

				var parameters = new object[]
					{
						characterData.Name,
						characterData.Account,
						characterData.WorldServerID,
						sceneName,
						characterData.SceneHandle,
						bindScene,
						characterData.BindX,
						characterData.BindY,
						characterData.BindZ,
						characterData.InstanceID,
						characterData.InstanceX,
						characterData.InstanceY,
						characterData.InstanceZ,
						characterData.InstanceRotX,
						characterData.InstanceRotY,
						characterData.InstanceRotZ,
						characterData.InstanceRotW,
						characterData.RaceID,
						characterData.ModelIndex,
						characterData.X,
						characterData.Y,
						characterData.Z,
						characterData.RotX,
						characterData.RotY,
						characterData.RotZ,
						characterData.RotW,
						characterData.AccessLevel,
						characterData.Flags,
						characterData.Version,
						now,
						characterData.ID,
					};

				if (ownership.HasValue)
				{
					// Appended rather than interleaved so the existing parameter indices above
					// stay stable.
					var owned = new object[parameters.Length + 3];
					Array.Copy(parameters, owned, parameters.Length);
					owned[parameters.Length] = (short)CharacterSessionState.Online;
					owned[parameters.Length + 1] = ownership.Value.OwnerServerID;
					owned[parameters.Length + 2] = ownership.Value.OwnerToken;
					parameters = owned;
				}

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					parameters,
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected > 0)
				{
					/* The buff set rides the row, inside this transaction and behind the checks the
					 * UPDATE just passed. See CharacterBuffService.ReplaceSetsAsync. */
					if (characterData.Buffs != null)
					{
						await CharacterBuffService.ReplaceSetsAsync(
							dbContext,
							CharacterBuffService.FlattenSets(new[] { (characterData.ID, characterData.Version, characterData.Buffs) }),
							cancellationToken).ConfigureAwait(false);
					}
					return SaveCharacterWriteOutcome.Success;
				}

				// No row updated: either missing, deleted, or authority lost.
				// Load current state (no tracking) only for the conflict path to preserve idempotency.
				var current = await dbContext.Characters
					.AsNoTracking()
					.FirstOrDefaultAsync(c => c.ID == characterData.ID && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);

				if (current == null)
				{
					return SaveCharacterWriteOutcome.NotFound;
				}

				// Checked before the version comparison: losing the claim is the more specific
				// and more serious condition, and a server that has lost it will usually ALSO
				// look version-stale once the new owner has saved once. Reporting that as a
				// benign stale write is what let a displaced server keep trying forever.
				if (ownership.HasValue &&
					(current.SessionState != CharacterSessionState.Online ||
					 current.SessionOwnerServerId != ownership.Value.OwnerServerID ||
					 current.SessionOwnerToken != ownership.Value.OwnerToken))
				{
					return SaveCharacterWriteOutcome.OwnershipLost;
				}

				if (current.Version > characterData.Version)
				{
					return SaveCharacterWriteOutcome.AuthorityLost;
				}

				throw new DuplicateReplayException();
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!saveResult.IsSuccess)
			{
				return DatabaseResult.Failure(saveResult.ErrorCode, saveResult.ErrorMessage, saveResult.IsTransient);
			}

			switch (saveResult.Data)
			{
				case SaveCharacterWriteOutcome.Success:
					return DatabaseResult.Success();
				case SaveCharacterWriteOutcome.NotFound:
					return DatabaseResult.Failure(
						DatabaseErrorCodes.NotFound,
						"Character not found.");
				case SaveCharacterWriteOutcome.AuthorityLost:
					return DatabaseResult.Failure(
						DatabaseErrorCodes.StaleState,
						"A newer server process has already saved this character. Refusing to overwrite progress.");
				case SaveCharacterWriteOutcome.OwnershipLost:
					return DatabaseResult.Failure(
						DatabaseErrorCodes.Forbidden,
						"This server no longer holds the character's session claim. Refusing to overwrite the current owner's state.");
				default:
					return DatabaseResult.Failure(DatabaseErrorCodes.DatabaseError, "Unexpected save outcome.");
			}
		}

		/// <summary>
		/// Takes the row lock on every named character, in ascending id order, inside the caller's
		/// transaction.
		/// </summary>
		/// <remarks>
		/// <b>The order is the point.</b> A statement that updates many character rows locks them in
		/// whatever order its plan visits them, and two such statements over overlapping rows can each
		/// hold a row the other is waiting for. The two-character exchange already locks ascending
		/// (<c>CharacterInventorySystem.RunExchangeAsync</c>), so every multi-row writer here locks
		/// ascending too and no cycle can form between them. <c>FOR NO KEY UPDATE</c> is the mode an
		/// ordinary UPDATE takes, so this adds no conflict the write would not have had, and it still
		/// admits the <c>FOR KEY SHARE</c> locks every character-owned table's foreign key takes.
		/// With <c>ORDER BY</c>, PostgreSQL applies the locks as the sorted rows are returned.
		/// </remarks>
		private async Task LockCharacterRowsAscendingAsync(NpgsqlDbContext dbContext, long[] characterIds, CancellationToken cancellationToken)
		{
			await ReadRowsAsync(
				dbContext,
				$"SELECT id FROM {TableName} WHERE id = ANY({{0}}::bigint[]) ORDER BY id FOR NO KEY UPDATE",
				new object[] { characterIds },
				reader => reader.GetInt64(0),
				cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para>
		/// <b>Why this exists.</b> The periodic save used to await <see cref="PersistOwnedAsync"/> once
		/// per resident character, each in its own context and transaction: three round trips a
		/// character, in series. At 500 residents that was about 1,500 round trips a pass, and at a
		/// few thousand the pass outlasted its own interval, so the next was skipped and the effective
		/// save interval doubled. This is one transaction of three statements for the whole list — four
		/// when any row carries a buff set, which is written for exactly the rows the UPDATE wrote (see
		/// <see cref="CharacterBuffService.ReplaceSetsAsync"/>).
		/// </para>
		/// <para>
		/// <b>Exactly the checks the single-row write makes, per row.</b> A row is written only when it
		/// is not deleted, its incoming version is newer than the stored one, and — when the request
		/// carries a claim — the row is still Online under that server and token. Rows the UPDATE did
		/// not return are then read back, still under the locks this transaction holds, and classified
		/// by <see cref="ClassifyUnwritten"/>; nothing about one row decides another's outcome.
		/// </para>
		/// <para>
		/// One transaction for the whole list, so callers keep lists to a few hundred rows: the row
		/// locks are held until the commit.
		/// </para>
		/// </remarks>
		public async Task<DatabaseResult<IReadOnlyList<CharacterPersistResult>>> PersistManyAsync(IReadOnlyList<CharacterPersistRequest> requests, CancellationToken cancellationToken = default)
		{
			if (requests == null || requests.Count == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterPersistResult>>.Success(Array.Empty<CharacterPersistResult>());
			}

			var results = new List<CharacterPersistResult>(requests.Count);

			/* Malformed rows are answered here and never sent. A character named twice keeps its
			 * newest snapshot, as BulkBatch.KeepNewest would, and the older one is Stale: the batch
			 * itself holds something newer, which is exactly what Stale means. An UPDATE ... FROM
			 * that matched one row twice would apply an arbitrary one of the two. */
			var positionOf = new Dictionary<long, int>(requests.Count);
			var valid = new List<CharacterPersistRequest>(requests.Count);
			foreach (CharacterPersistRequest request in requests)
			{
				CharacterData data = request.Data;
				if (data.ID <= 0 ||
					data.Version <= 0 ||
					data.Name == null ||
					data.Account == null ||
					(request.Ownership.HasValue &&
						(!request.Ownership.Value.IsValid || request.Ownership.Value.CharacterID != data.ID)))
				{
					results.Add(new CharacterPersistResult(data.ID, CharacterPersistOutcome.Invalid));
					continue;
				}

				if (positionOf.TryGetValue(data.ID, out int at))
				{
					if (data.Version > valid[at].Data.Version)
					{
						results.Add(new CharacterPersistResult(data.ID, CharacterPersistOutcome.Stale));
						valid[at] = request;
					}
					else
					{
						results.Add(new CharacterPersistResult(data.ID, CharacterPersistOutcome.Stale));
					}
					continue;
				}

				positionOf[data.ID] = valid.Count;
				valid.Add(request);
			}

			if (valid.Count == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterPersistResult>>.Success(results);
			}

			valid.Sort((a, b) => a.Data.ID.CompareTo(b.Data.ID));

			DatabaseResult<List<CharacterPersistResult>> written = await ExecuteTransactionAsync<List<CharacterPersistResult>>(
				dbContext => PersistManyInTransactionAsync(dbContext, valid, cancellationToken),
				saveChanges: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!written.IsSuccess)
			{
				return DatabaseResult<IReadOnlyList<CharacterPersistResult>>.Failure(written.ErrorCode, written.ErrorMessage, written.IsTransient);
			}

			results.AddRange(written.Data);
			return DatabaseResult<IReadOnlyList<CharacterPersistResult>>.Success(results);
		}

		/// <summary>
		/// The statements of <see cref="PersistManyAsync"/>, inside its transaction.
		/// </summary>
		/// <param name="dbContext">The transaction's context.</param>
		/// <param name="rows">Validated, de-duplicated rows in ascending id order.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>One outcome per row.</returns>
		private async Task<List<CharacterPersistResult>> PersistManyInTransactionAsync(
			NpgsqlDbContext dbContext,
			List<CharacterPersistRequest> rows,
			CancellationToken cancellationToken)
		{
			int n = rows.Count;
			var ids = new long[n];
			var names = new string[n];
			var accounts = new string[n];
			var worldServerIds = new long[n];
			var sceneNames = new string[n];
			var sceneHandles = new long[n];
			var bindScenes = new string[n];
			var bindX = new float[n];
			var bindY = new float[n];
			var bindZ = new float[n];
			var instanceIds = new long[n];
			var instanceX = new float[n];
			var instanceY = new float[n];
			var instanceZ = new float[n];
			var instanceRotX = new float[n];
			var instanceRotY = new float[n];
			var instanceRotZ = new float[n];
			var instanceRotW = new float[n];
			var raceIds = new int[n];
			var modelIndexes = new int[n];
			var x = new float[n];
			var y = new float[n];
			var z = new float[n];
			var rotX = new float[n];
			var rotY = new float[n];
			var rotZ = new float[n];
			var rotW = new float[n];
			// smallint, not byte[]: Npgsql binds a byte[] as bytea.
			var accessLevels = new short[n];
			var flags = new int[n];
			var versions = new long[n];
			var gated = new bool[n];
			var ownerServerIds = new long[n];
			var ownerTokens = new Guid[n];

			for (int i = 0; i < n; ++i)
			{
				CharacterData d = rows[i].Data;
				ids[i] = d.ID;
				names[i] = d.Name;
				accounts[i] = d.Account;
				worldServerIds[i] = d.WorldServerID;
				sceneNames[i] = d.SceneName ?? string.Empty;
				sceneHandles[i] = d.SceneHandle;
				bindScenes[i] = d.BindScene ?? string.Empty;
				bindX[i] = d.BindX;
				bindY[i] = d.BindY;
				bindZ[i] = d.BindZ;
				instanceIds[i] = d.InstanceID;
				instanceX[i] = d.InstanceX;
				instanceY[i] = d.InstanceY;
				instanceZ[i] = d.InstanceZ;
				instanceRotX[i] = d.InstanceRotX;
				instanceRotY[i] = d.InstanceRotY;
				instanceRotZ[i] = d.InstanceRotZ;
				instanceRotW[i] = d.InstanceRotW;
				raceIds[i] = d.RaceID;
				modelIndexes[i] = d.ModelIndex;
				x[i] = d.X;
				y[i] = d.Y;
				z[i] = d.Z;
				rotX[i] = d.RotX;
				rotY[i] = d.RotY;
				rotZ[i] = d.RotZ;
				rotW[i] = d.RotW;
				accessLevels[i] = d.AccessLevel;
				flags[i] = d.Flags;
				versions[i] = d.Version;
				gated[i] = rows[i].Ownership.HasValue;
				ownerServerIds[i] = rows[i].Ownership?.OwnerServerID ?? 0L;
				ownerTokens[i] = rows[i].Ownership?.OwnerToken ?? Guid.Empty;
			}

			await LockCharacterRowsAscendingAsync(dbContext, ids, cancellationToken).ConfigureAwait(false);

			/* The columns PersistInternalAsync writes, and only those: not `selected`, which belongs to
			 * the login flow, and nothing of the session, which belongs to the claim operations. */
			string sql = $@"
				UPDATE {TableName} AS c
				SET name = u.name,
					account = u.account,
					world_server_id = u.world_server_id,
					scene_name = u.scene_name,
					scene_handle = u.scene_handle,
					bind_scene = u.bind_scene,
					bind_x = u.bind_x,
					bind_y = u.bind_y,
					bind_z = u.bind_z,
					instance_id = u.instance_id,
					instance_x = u.instance_x,
					instance_y = u.instance_y,
					instance_z = u.instance_z,
					instance_rot_x = u.instance_rot_x,
					instance_rot_y = u.instance_rot_y,
					instance_rot_z = u.instance_rot_z,
					instance_rot_w = u.instance_rot_w,
					race_id = u.race_id,
					model_index = u.model_index,
					x = u.x,
					y = u.y,
					z = u.z,
					rot_x = u.rot_x,
					rot_y = u.rot_y,
					rot_z = u.rot_z,
					rot_w = u.rot_w,
					access_level = u.access_level,
					flags = u.flags,
					version = u.version,
					last_saved = {{33}}
				FROM UNNEST(
					{{0}}::bigint[], {{1}}::text[], {{2}}::text[], {{3}}::bigint[], {{4}}::text[], {{5}}::bigint[],
					{{6}}::text[], {{7}}::real[], {{8}}::real[], {{9}}::real[],
					{{10}}::bigint[], {{11}}::real[], {{12}}::real[], {{13}}::real[],
					{{14}}::real[], {{15}}::real[], {{16}}::real[], {{17}}::real[],
					{{18}}::integer[], {{19}}::integer[],
					{{20}}::real[], {{21}}::real[], {{22}}::real[], {{23}}::real[], {{24}}::real[], {{25}}::real[], {{26}}::real[],
					{{27}}::smallint[], {{28}}::integer[], {{29}}::bigint[],
					{{30}}::boolean[], {{31}}::bigint[], {{32}}::uuid[]
				) AS u(id, name, account, world_server_id, scene_name, scene_handle,
					bind_scene, bind_x, bind_y, bind_z,
					instance_id, instance_x, instance_y, instance_z,
					instance_rot_x, instance_rot_y, instance_rot_z, instance_rot_w,
					race_id, model_index,
					x, y, z, rot_x, rot_y, rot_z, rot_w,
					access_level, flags, version,
					gated, owner_server_id, owner_token)
				WHERE c.id = u.id
					AND c.deleted = FALSE
					AND c.version < u.version
					AND (NOT u.gated
						OR (c.session_state = {{34}}
							AND c.session_owner_server_id = u.owner_server_id
							AND c.session_owner_token = u.owner_token))
				RETURNING c.id";

			List<long> updated = await ReadRowsAsync(
				dbContext,
				sql,
				new object[]
				{
					ids, names, accounts, worldServerIds, sceneNames, sceneHandles,
					bindScenes, bindX, bindY, bindZ,
					instanceIds, instanceX, instanceY, instanceZ,
					instanceRotX, instanceRotY, instanceRotZ, instanceRotW,
					raceIds, modelIndexes,
					x, y, z, rotX, rotY, rotZ, rotW,
					accessLevels, flags, versions,
					gated, ownerServerIds, ownerTokens,
					DateTime.UtcNow,
					(short)CharacterSessionState.Online,
				},
				reader => reader.GetInt64(0),
				cancellationToken).ConfigureAwait(false);

			var updatedSet = new HashSet<long>(updated);
			var results = new List<CharacterPersistResult>(n);
			var unwritten = new List<CharacterPersistRequest>();
			List<(long CharacterID, long Version, IReadOnlyList<CharacterBuffData> Buffs)> buffSets = null;
			foreach (CharacterPersistRequest row in rows)
			{
				if (updatedSet.Contains(row.Data.ID))
				{
					results.Add(new CharacterPersistResult(row.Data.ID, CharacterPersistOutcome.Saved));
					if (row.Data.Buffs != null)
					{
						(buffSets ??= new List<(long, long, IReadOnlyList<CharacterBuffData>)>()).Add((row.Data.ID, row.Data.Version, row.Data.Buffs));
					}
				}
				else
				{
					unwritten.Add(row);
				}
			}

			/* Only the rows the UPDATE wrote carry their buff set, and in the same transaction: a row
			 * refused as stale, unowned or deleted writes no set, which is what keeps an older save
			 * from deleting or re-adding a buff a newer one decided. See
			 * CharacterBuffService.ReplaceSetsAsync. */
			if (buffSets != null)
			{
				await CharacterBuffService.ReplaceSetsAsync(dbContext, CharacterBuffService.FlattenSets(buffSets), cancellationToken).ConfigureAwait(false);
			}

			if (unwritten.Count == 0)
			{
				return results;
			}

			// Read under the locks taken above, so what is read is what the UPDATE saw.
			long[] unwrittenIds = unwritten.Select(r => r.Data.ID).ToArray();
			var stored = await ReadRowsAsync(
				dbContext,
				$@"SELECT id, deleted, version, session_state, session_owner_server_id, session_owner_token
					FROM {TableName} WHERE id = ANY({{0}}::bigint[])",
				new object[] { unwrittenIds },
				reader => new
				{
					ID = reader.GetInt64(0),
					Deleted = reader.GetBoolean(1),
					Version = reader.GetInt64(2),
					State = (CharacterSessionState)reader.GetInt16(3),
					OwnerServerId = reader.GetInt64(4),
					OwnerToken = reader.GetGuid(5),
				},
				cancellationToken).ConfigureAwait(false);

			var storedById = stored.ToDictionary(s => s.ID);
			foreach (CharacterPersistRequest row in unwritten)
			{
				CharacterPersistOutcome outcome = storedById.TryGetValue(row.Data.ID, out var s)
					? ClassifyUnwritten(row, true, s.Deleted, s.Version, s.State, s.OwnerServerId, s.OwnerToken)
					: ClassifyUnwritten(row, false, false, 0L, CharacterSessionState.Offline, 0L, Guid.Empty);
				results.Add(new CharacterPersistResult(row.Data.ID, outcome));
			}

			return results;
		}

		/// <summary>
		/// Says why a row of a batched save was not written, from the stored row as it stands under
		/// the batch's lock.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The single-row save's order, kept: a missing or deleted row first; then a lost claim, which
		/// is checked before the version because a server that has lost its claim usually ALSO looks
		/// version-stale once the new owner has saved, and reporting that as a benign stale write is
		/// what let a displaced server keep trying forever; then the version.
		/// </para>
		/// <para>
		/// An equal version under a claim still held is this caller's own write, replayed — a
		/// transaction retried after its commit reply was lost. The single-row path reports that as a
		/// DUPLICATE_REPLAY error, which callers then mistook for a failure worth retrying. Without a
		/// claim an equal version proves nothing about who wrote it, so it is Stale.
		/// </para>
		/// </remarks>
		/// <param name="request">The row that was sent.</param>
		/// <param name="exists">Whether a row with that id exists at all.</param>
		/// <param name="deleted">Whether it is soft-deleted.</param>
		/// <param name="storedVersion">Its stored version.</param>
		/// <param name="sessionState">Its session state.</param>
		/// <param name="ownerServerId">The server its session names.</param>
		/// <param name="ownerToken">The token its session names.</param>
		/// <returns>Why the row was not written.</returns>
		public static CharacterPersistOutcome ClassifyUnwritten(
			CharacterPersistRequest request,
			bool exists,
			bool deleted,
			long storedVersion,
			CharacterSessionState sessionState,
			long ownerServerId,
			Guid ownerToken)
		{
			if (!exists || deleted)
			{
				return CharacterPersistOutcome.NotFound;
			}

			if (request.Ownership.HasValue)
			{
				CharacterSessionLeaseData lease = request.Ownership.Value;
				if (sessionState != CharacterSessionState.Online ||
					ownerServerId != lease.OwnerServerID ||
					ownerToken != lease.OwnerToken)
				{
					return CharacterPersistOutcome.OwnershipLost;
				}
			}

			if (storedVersion == request.Data.Version && request.Ownership.HasValue)
			{
				return CharacterPersistOutcome.Replayed;
			}

			/* storedVersion > incoming is the ordinary stale write. An equal version without a claim
			 * is stale for the reason above. A LOWER stored version on a row that is owned and live
			 * cannot be here at all — the UPDATE would have written it under the lock this read shares
			 * — so reporting it as Stale rather than Saved is the answer that loses nothing: the
			 * caller's next pass writes a newer version anyway. */
			return CharacterPersistOutcome.Stale;
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <see cref="ReleaseAsync"/> for many characters in one transaction, with the same per-row
		/// ownership check: a triple that no longer matches (already released, or claimed away after
		/// a lease lapse) releases nothing and is simply absent from the result. Rows are locked in
		/// ascending id order first; see <see cref="LockCharacterRowsAscendingAsync"/>.
		/// </remarks>
		public async Task<DatabaseResult<IReadOnlyList<long>>> ReleaseManyAsync(IReadOnlyList<CharacterSessionLeaseData> leases, CancellationToken cancellationToken = default)
		{
			if (leases == null || leases.Count == 0)
			{
				return DatabaseResult<IReadOnlyList<long>>.Success(Array.Empty<long>());
			}

			// Exact triples only once; a malformed one can never match a claim.
			var distinct = new HashSet<(long, long, Guid)>();
			var valid = new List<CharacterSessionLeaseData>(leases.Count);
			foreach (CharacterSessionLeaseData lease in leases)
			{
				if (lease.IsValid && distinct.Add((lease.CharacterID, lease.OwnerServerID, lease.OwnerToken)))
				{
					valid.Add(lease);
				}
			}
			if (valid.Count == 0)
			{
				return DatabaseResult<IReadOnlyList<long>>.Success(Array.Empty<long>());
			}
			valid.Sort((a, b) => a.CharacterID.CompareTo(b.CharacterID));

			long[] ids = valid.Select(l => l.CharacterID).ToArray();
			long[] serverIds = valid.Select(l => l.OwnerServerID).ToArray();
			Guid[] tokens = valid.Select(l => l.OwnerToken).ToArray();

			DatabaseResult<List<long>> released = await ExecuteTransactionAsync<List<long>>(async dbContext =>
			{
				await LockCharacterRowsAscendingAsync(dbContext, ids, cancellationToken).ConfigureAwait(false);

				return await ReadRowsAsync(
					dbContext,
					$@"UPDATE {TableName} AS c
						SET session_state = {{3}},
							session_owner_server_id = 0,
							session_owner_token = {{4}},
							session_lease_expires_utc = {{5}},
							last_saved = {{6}}
						FROM UNNEST({{0}}::bigint[], {{1}}::bigint[], {{2}}::uuid[]) AS u(id, owner_server_id, owner_token)
						WHERE c.id = u.id
							AND c.deleted = FALSE
							AND c.session_state = {{7}}
							AND c.session_owner_server_id = u.owner_server_id
							AND c.session_owner_token = u.owner_token
						RETURNING c.id",
					new object[]
					{
						ids,
						serverIds,
						tokens,
						(short)CharacterSessionState.Offline,
						Guid.Empty,
						DateTime.UnixEpoch,
						DateTime.UtcNow,
						(short)CharacterSessionState.Online,
					},
					reader => reader.GetInt64(0),
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!released.IsSuccess)
			{
				return DatabaseResult<IReadOnlyList<long>>.Failure(released.ErrorCode, released.ErrorMessage, released.IsTransient);
			}
			return DatabaseResult<IReadOnlyList<long>>.Success(released.Data);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para><b>Soft Delete:</b></para>
		/// This performs an atomic soft delete rather than removing data.
		/// It renames the character (appending <c>_DELETED_{GUID}</c>) to free up the original name,
		/// sets <c>deleted=true</c>, and stamps the character <c>Version</c> to the incoming authoritative value.
		/// Character guild/party memberships are hard-deleted (temporary state).
		/// This method does not soft-delete character-owned sub-entities; those entities have independent Version streams.
		/// </remarks>
		public async Task<DatabaseResult> DeleteAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid incoming version.");
			}

			var transactionResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var tableName = dbContext.GetTableName<CharacterEntity>();
				var guid = Guid.NewGuid().ToString("D");
				var suffix = $"{DeletedNameMarker}{guid}";

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
						SET name = (COALESCE(name, '') || {{1}}),
							deleted = TRUE,
							time_deleted = timezone('UTC', CURRENT_TIMESTAMP),
							version = {{2}}
						WHERE id = {{0}} AND deleted = FALSE AND version < {{2}}",
					new object[] { characterId, suffix, incomingVersion },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					var currentState = await dbContext.Set<CharacterEntity>()
						.AsNoTracking()
						.IgnoreQueryFilters()
						.Where(x => x.ID == characterId)
						.Select(x => new { x.Deleted, x.Version })
						.SingleOrDefaultAsync(cancellationToken)
						.ConfigureAwait(false);

					if (currentState == null || currentState.Deleted)
					{
						return;
					}

					if (currentState.Version == incomingVersion)
					{
						throw new DuplicateReplayException();
					}

					throw new StaleStateException("Stale character delete (incoming version is not newer than persisted state).");
				}

				// Hard-delete temporary membership state (intentional).
				var guildTableName = dbContext.GetTableName<CharacterGuildEntity>();
				await dbContext.Database.ExecuteSqlRawAsync(
					$"DELETE FROM {guildTableName} WHERE character_id = {{0}}",
					new object[] { characterId },
					cancellationToken).ConfigureAwait(false);

				var partyTableName = dbContext.GetTableName<CharacterPartyEntity>();
				await dbContext.Database.ExecuteSqlRawAsync(
					$"DELETE FROM {partyTableName} WHERE character_id = {{0}}",
					new object[] { characterId },
					cancellationToken).ConfigureAwait(false);
			}).ConfigureAwait(false);

			return transactionResult;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterData?>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterData?>.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			var result = await ExecuteReadAsync<CharacterData?>(async dbContext =>
			{
				var entity = await fetchByIdQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				return entity == null ? null : MapEntityToData(entity);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> RestoreAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than 0.");
			}

			/* A transaction rather than a single write: the character row and the purge of its
			 * deletion tombstones below must land together, or a restore that half-applied would
			 * leave a live character whose saves are refused. */
			return await ExecuteTransactionAsync(async dbContext =>
			{
				var entity = await dbContext.Characters
					.FirstOrDefaultAsync(c => c.ID == characterId, cancellationToken)
					.ConfigureAwait(false);

				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}
				if (!entity.Deleted)
				{
					// Already live. Succeeding silently would tell the caller a restore happened.
					throw new DatabaseException("That character is not deleted.", errorCode: DatabaseErrorCodes.ValidationError);
				}

				/* Deletion appended a marker and a GUID to the name so that the unique index
				 * would release it. Restoring without undoing that brings the character back
				 * called "Bob_DELETED_2f3a…", which is not a name a player may even be given.
				 * If somebody has taken "Bob" in the meantime the index refuses this, which is
				 * the collision the caller has to report. */
				string original = StripDeletedSuffix(entity.Name);
				if (!string.IsNullOrEmpty(original) && original != entity.Name)
				{
					// Lowered here, not inside the expression: the comparison must be a plain
					// parameter so it uses the index on name_lowercase.
					string originalLowered = original.ToLowerInvariant();
					bool taken = await dbContext.Characters
						.AsNoTracking()
						.AnyAsync(c => c.ID != characterId && c.NameLowercase == originalLowered, cancellationToken)
						.ConfigureAwait(false);

					if (taken)
					{
						throw new DatabaseException(
							$"The name '{original}' was taken while this character was deleted.",
							errorCode: DatabaseErrorCodes.UniqueViolation);
					}
					entity.Name = original;
				}

				entity.Deleted = false;
				entity.TimeDeleted = null;
				// Never restore straight into the selected slot: the account may have picked
				// another character since, and two selected rows is a state the login server
				// does not expect.
				entity.Selected = false;
				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

				/* Remove the tombstones the deletion wrote, or the restored character can never save
				 * these tables again.
				 *
				 * When the login server is configured not to keep a deleted character's data
				 * (CharacterSelectSystem.KeepDeleteData = false), deletion soft-deletes every
				 * sub-entity row at version long.MaxValue — the value that is guaranteed to pass each
				 * table's "incoming version wins" guard. The same guard then refuses every later
				 * write to those keys: no version a save can carry exceeds long.MaxValue. So a
				 * restored character's attributes, buffs, factions, achievements, abilities and pets
				 * were reported SUPERSEDED on every save and silently never written, and its hotkeys
				 * failed STALE_STATE outright (issue #267 audit).
				 *
				 * Removed, not un-deleted: in that mode deletion hard-deletes the character's items,
				 * so the data was not kept and bringing half of it back would be a character nobody
				 * ever had. Only rows at exactly long.MaxValue are touched — a row tombstoned during
				 * play (a quest turned in, say) carries its own small version and must keep refusing
				 * the stale save it exists to refuse. With KeepDeleteData on (the shipped setting)
				 * deletion writes no tombstones and this removes nothing. */
				const long DeletionTombstoneVersion = long.MaxValue;
				string[] tombstonedTables =
				{
					dbContext.GetTableName<CharacterAbilityEntity>(),
					dbContext.GetTableName<CharacterAchievementEntity>(),
					dbContext.GetTableName<CharacterAttributeEntity>(),
					dbContext.GetTableName<CharacterBuffEntity>(),
					dbContext.GetTableName<CharacterFactionEntity>(),
					dbContext.GetTableName<CharacterFriendEntity>(),
					dbContext.GetTableName<CharacterHotkeyEntity>(),
					dbContext.GetTableName<CharacterKnownAbilityEntity>(),
					dbContext.GetTableName<CharacterPetEntity>(),
					dbContext.GetTableName<CharacterPetAttributeEntity>(),
					dbContext.GetTableName<CharacterPetBuffEntity>(),
					dbContext.GetTableName<CharacterArchetypeEntity>(),
				};
				foreach (string table in tombstonedTables)
				{
					await dbContext.Database.ExecuteSqlRawAsync(
						$"DELETE FROM {table} WHERE character_id = {{0}} AND deleted = TRUE AND version = {{1}}",
						new object[] { characterId, DeletionTombstoneVersion },
						cancellationToken).ConfigureAwait(false);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterAdminData>> FetchAdminAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterAdminData>.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entity = await dbContext.Characters
					.AsNoTracking()
					.FirstOrDefaultAsync(c => c.ID == characterId, cancellationToken)
					.ConfigureAwait(false);

				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}
				return MapEntityToAdminData(entity);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterAdminData>>> FetchAdminByAccountAsync(
			string accountName,
			bool includeDeleted,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<IReadOnlyList<CharacterAdminData>>.Failure(
					DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				// characters.account is a foreign key to accounts.name, so it holds that exact
				// string and the index on it serves an exact predicate. No paging: an account
				// holds a handful of characters, and a detail page wants all of them.
				IQueryable<CharacterEntity> q = dbContext.Characters
					.AsNoTracking()
					.Where(c => c.Account == accountName);

				if (!includeDeleted)
				{
					q = q.Where(c => !c.Deleted);
				}

				var rows = await q
					.OrderBy(c => c.Name)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return (IReadOnlyList<CharacterAdminData>)rows.Select(MapEntityToAdminData).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterAdminPage>> SearchAdminAsync(
			string query,
			bool includeDeleted,
			bool? online,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default)
		{
			// A caller that asks for page 0 or ten thousand rows is a bug or a probe; neither
			// gets to choose how much of the table this reads.
			if (page < 1)
			{
				page = 1;
			}
			if (pageSize < 1)
			{
				pageSize = 25;
			}
			if (pageSize > MaxAdminPageSize)
			{
				pageSize = MaxAdminPageSize;
			}

			string prefix = query?.Trim().ToLowerInvariant();

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<CharacterEntity> q = dbContext.Characters.AsNoTracking();

				if (!includeDeleted)
				{
					q = q.Where(c => !c.Deleted);
				}
				if (!string.IsNullOrEmpty(prefix))
				{
					/* A prefix, not a contains. A leading wildcard forecloses any index
					 * unconditionally, so this at least leaves the door open.
					 *
					 * It is not an index seek as deployed, though, and the comment that used to
					 * claim it was wrong: the database collates en_US.UTF-8 and these indexes
					 * use the default opclass, which PostgreSQL will not use for LIKE 'x%'.
					 * Making it one needs text_pattern_ops indexes, which is a migration and
					 * not urgent at this table size — but do not read this as already fast. */
					q = q.Where(c => c.NameLowercase.StartsWith(prefix) || c.Account.ToLower().StartsWith(prefix));
				}
				if (online.HasValue)
				{
					var now = DateTime.UtcNow;
					q = online.Value
						? q.Where(c => c.SessionState == CharacterSessionState.Online && c.SessionLeaseExpiresUtc > now)
						: q.Where(c => c.SessionState != CharacterSessionState.Online || c.SessionLeaseExpiresUtc <= now);
				}

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				var rows = await q
					.OrderByDescending(c => c.LastSaved)
					.ThenBy(c => c.ID)
					.Skip((page - 1) * pageSize)
					.Take(pageSize)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return new CharacterAdminPage
				{
					Items = rows.Select(MapEntityToAdminData).ToList(),
					Page = page,
					PageSize = pageSize,
					TotalCount = total,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateAdminAsync(long characterId, CharacterAdminEdit edit, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than 0.");
			}
			if (edit == null || edit.IsEmpty)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Nothing to change.");
			}
			return await ExecuteWriteAsync(async dbContext =>
			{
				var entity = await dbContext.Characters
					.FirstOrDefaultAsync(c => c.ID == characterId, cancellationToken)
					.ConfigureAwait(false);

				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}
				if (entity.Deleted)
				{
					throw new DatabaseException("That character is deleted. Restore it first.", errorCode: DatabaseErrorCodes.ValidationError);
				}

				// The lease is re-checked here, inside the write, so a server that claimed the
				// character after the operator loaded the page wins instead of being written
				// over. The persistence pass would overwrite anything set below anyway.
				if (entity.SessionState == CharacterSessionState.Online &&
					entity.SessionLeaseExpiresUtc > DateTime.UtcNow)
				{
					throw new DatabaseException(
						"That character holds a live session lease and cannot be edited.",
						errorCode: DatabaseErrorCodes.StaleState);
				}

				if (edit.X.HasValue)
				{
					entity.X = edit.X.Value;
				}
				if (edit.Y.HasValue)
				{
					entity.Y = edit.Y.Value;
				}
				if (edit.Z.HasValue)
				{
					entity.Z = edit.Z.Value;
				}
				if (edit.SceneName != null)
				{
					entity.SceneName = edit.SceneName;
				}
				if (edit.BindScene != null)
				{
					entity.BindScene = edit.BindScene;
				}
				if (edit.AccessLevel.HasValue)
				{
					entity.AccessLevel = edit.AccessLevel.Value;
				}

				// A save whose version is not greater than this one is refused by the
				// persistence pass's own guard, so bumping it is what makes this write stick.
				entity.Version += 1;
				entity.LastSaved = DateTime.UtcNow;
				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// The marker deletion appends to a character's name, followed by a GUID.
		/// </summary>
		/// <remarks>
		/// Deletion renames rather than only flagging, because <c>name_lowercase</c> carries a
		/// unique index with no partial predicate: a deleted row would otherwise hold its name
		/// forever and nobody could ever reuse it. Restore has to undo exactly this, which is
		/// why both sides read the marker from here instead of spelling it out twice.
		/// </remarks>
		private const string DeletedNameMarker = "_DELETED_";

		/// <summary>
		/// Strips the deletion suffix from a name, giving back what the character was called.
		/// </summary>
		/// <remarks>
		/// The last occurrence, not the first: a player is allowed no underscores in a name, so
		/// the marker cannot appear in one legitimately, but a row deleted, restored and deleted
		/// again through some older path should still come back to its real name.
		/// </remarks>
		public static string StripDeletedSuffix(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return name;
			}
			int at = name.LastIndexOf(DeletedNameMarker, StringComparison.Ordinal);
			return at <= 0 ? name : name.Substring(0, at);
		}

		/// <summary>The most rows one operator search will read, whatever it asks for.</summary>
		private const int MaxAdminPageSize = 100;

		private static CharacterAdminData MapEntityToAdminData(CharacterEntity entity) => new CharacterAdminData
		{
			ID = entity.ID,
			// A deleted row carries its mangled name; an operator needs the real one.
			Name = entity.Deleted ? StripDeletedSuffix(entity.Name) : entity.Name,
			StoredName = entity.Name,
			Account = entity.Account,
			RaceID = entity.RaceID,
			AccessLevel = entity.AccessLevel,
			Selected = entity.Selected,
			Deleted = entity.Deleted,
			TimeDeleted = entity.TimeDeleted,
			SessionState = (int)entity.SessionState,
			SessionOwnerServerID = entity.SessionOwnerServerId,
			SessionLeaseExpiresUtc = entity.SessionLeaseExpiresUtc,
			WorldServerID = entity.WorldServerID,
			SceneName = entity.SceneName,
			BindScene = entity.BindScene,
			X = entity.X,
			Y = entity.Y,
			Z = entity.Z,
			Version = entity.Version,
			TimeCreated = entity.TimeCreated,
			LastSaved = entity.LastSaved,
			LockedUntil = entity.LockedUntil,
			LockedAt = entity.LockedAt,
			LockedBy = entity.LockedBy,
			LockReason = entity.LockReason,
		};

		/// <inheritdoc/>
		public async Task<DatabaseResult> RenameAsync(long characterId, string newName, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than 0.");
			}
			if (!Authentication.IsAllowedCharacterName(newName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidCharacterNameError);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				// Deleted characters are renameable on purpose. A soft-deleted row still holds
				// its name in the unique index, so moving it aside is how an operator frees a
				// name — and it is the only way to restore a character whose name was taken
				// while it was gone.
				var entity = await dbContext.Characters
					.FirstOrDefaultAsync(c => c.ID == characterId, cancellationToken)
					.ConfigureAwait(false);

				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}

				// The unique index on name_lowercase is what actually prevents a collision, and
				// it stays the guard: this check only exists so the common case answers with
				// "that name is taken" instead of a raw constraint violation. A name claimed
				// between this read and the save still loses, to the index.
				string lowered = newName.ToLowerInvariant();
				bool taken = await dbContext.Characters
					.AsNoTracking()
					.AnyAsync(c => c.ID != characterId && c.NameLowercase == lowered, cancellationToken)
					.ConfigureAwait(false);

				if (taken)
				{
					throw new DatabaseException($"The name '{newName}' is already taken.", errorCode: DatabaseErrorCodes.UniqueViolation);
				}

				entity.Name = newName;
				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterData>>> FetchManyAsync(string account, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(account))
			{
				return DatabaseResult<IReadOnlyList<CharacterData>>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var entities = await FetchManyByAccountQueryAsync(dbContext, account, cancellationToken).ConfigureAwait(false);
				return (IReadOnlyList<CharacterData>)entities.Select(MapEntityToData).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterData?>> FetchAsync(string characterName, CancellationToken cancellationToken = default)
		{
			return await FetchAsync(characterName, selected: null, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterNameData>>> FetchNamesAsync(
			IReadOnlyList<long> characterIds,
			CancellationToken cancellationToken = default)
		{
			var ids = new List<long>();
			var seen = new HashSet<long>();
			if (characterIds != null)
			{
				for (int i = 0; i < characterIds.Count && ids.Count < MaxNameLookupIds; ++i)
				{
					long characterId = characterIds[i];
					if (characterId > 0 && seen.Add(characterId))
					{
						ids.Add(characterId);
					}
				}
			}

			if (ids.Count == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterNameData>>.Success(Array.Empty<CharacterNameData>());
			}

			return await ExecuteReadAsync<IReadOnlyList<CharacterNameData>>(async dbContext =>
			{
				/* Projected to the two columns rather than materialising characters.
				 *
				 * The callers are display paths — labelling rows in a browsable list, and the scene
				 * servers' batched name lookups — and a character row is wide. Pulling whole
				 * entities to read one string each would put the cost of a full character load on a
				 * query a client can ask for repeatedly.
				 *
				 * A deleted character does not resolve, as the documented contract says and as the
				 * single lookup (fetchByIdQuery) has always had it: a name lookup must not answer
				 * differently depending on whether it arrived alone or in a batch. */
				var rows = await dbContext.Characters
					.AsNoTracking()
					.Where(c => ids.Contains(c.ID) && !c.Deleted)
					.Select(c => new { c.ID, c.Name })
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				IReadOnlyList<CharacterNameData> data = rows
					.Select(r => new CharacterNameData(r.ID, r.Name))
					.ToList();
				return data;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Ceiling on how many names one lookup will resolve.
		/// </summary>
		/// <remarks>
		/// The caller is answering a client-triggered request whose row count it has already
		/// bounded; this is the second bound, so a future caller that forgets the first cannot
		/// turn a name lookup into an unbounded query.
		/// </remarks>
		private const int MaxNameLookupIds = 128;

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterData?>> FetchAsync(string characterName, bool? selected, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedCharacterName(characterName))
			{
				return DatabaseResult<CharacterData?>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidCharacterNameError);
			}

			var result = await ExecuteReadAsync<CharacterData?>(async dbContext =>
			{
				var nameLower = characterName.ToLowerInvariant();
				var entity = selected.HasValue
					? await fetchByNameSelectedQuery(dbContext, nameLower, selected.Value, cancellationToken).ConfigureAwait(false)
					: await fetchByNameQuery(dbContext, nameLower, cancellationToken).ConfigureAwait(false);
				return entity == null ? null : MapEntityToData(entity);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterData?>> FetchByAccountAsync(string accountName, CancellationToken cancellationToken = default)
		{
			return await FetchByAccountAsync(accountName, selected: null, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterData?>> FetchByAccountAsync(string accountName, bool? selected, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<CharacterData?>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			var result = await ExecuteReadAsync<CharacterData?>(async dbContext =>
			{
				var entity = selected.HasValue
					? await fetchByAccountSelectedQuery(dbContext, accountName, selected.Value, cancellationToken).ConfigureAwait(false)
					: await fetchByAccountQuery(dbContext, accountName, cancellationToken).ConfigureAwait(false);
				return entity == null ? null : MapEntityToData(entity);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para><b>Concurrency Safety:</b></para>
		/// Uses a single atomic statement with SELECT FOR UPDATE to prevent race conditions
		/// when multiple requests attempt to select different characters concurrently.
		/// Row-level locks are acquired for all characters belonging to the account,
		/// ensuring only one selection operation can proceed at a time per account.
		/// </remarks>
		public async Task<DatabaseResult> SetSelectedAsync(string account, long characterId, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(account) || characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid account or character ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var tableName = dbContext.GetTableName<CharacterEntity>();
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"WITH locked_chars AS (
					SELECT id FROM {tableName} 
					WHERE account = {{0}} AND deleted = FALSE
					ORDER BY id
					FOR UPDATE
					)
					UPDATE {tableName} 
					SET selected = (id = {{1}})
					WHERE account = {{0}} AND deleted = FALSE
					AND id IN (SELECT id FROM locked_chars)
					AND EXISTS (SELECT 1 FROM locked_chars WHERE id = {{1}})",
					new object[] { account, characterId },
					cancellationToken).ConfigureAwait(false);

				/* The EXISTS is what makes a character that is not one of this account's live
				 * characters NotFound. Without it, a foreign or deleted id still matched every row
				 * of the account, set `selected` false on all of them and reported success,
				 * leaving the account with nothing selected (issue #267). */
				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<Guid>> TryClaimAsync(long characterId, long ownerServerId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || ownerServerId <= 0)
			{
				return DatabaseResult<Guid>.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID or owner server ID.");
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var newToken = Guid.NewGuid();
				var newLeaseExpiresUtc = now + DefaultSessionLeaseDuration;
				var tableName = dbContext.GetTableName<CharacterEntity>();

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
					SET
						session_state = {{0}},
						session_owner_server_id = {{1}},
						session_owner_token = {{2}},
						session_lease_expires_utc = {{3}},
						last_saved = {{4}}
					WHERE id = {{5}} AND deleted = false
						AND (session_state = {{6}} OR session_lease_expires_utc <= {{4}})",
					(short)CharacterSessionState.Online,
					ownerServerId,
					newToken,
					newLeaseExpiresUtc,
					now,
					characterId,
					(short)CharacterSessionState.Offline
				).ConfigureAwait(false);

				if (rowsAffected > 0)
				{
					return newToken;
				}

				var exists = await dbContext.Characters
					.AnyAsync(c => c.ID == characterId && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (!exists)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}

				throw new DatabaseException(
					"Character is already owned by another server.",
					errorCode: DatabaseErrorCodes.InvalidOperation);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> ReleaseAsync(long characterId, long ownerServerId, Guid ownerToken, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || ownerServerId <= 0 || ownerToken == Guid.Empty)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID, owner server ID, or owner token.");
			}

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var tableName = dbContext.GetTableName<CharacterEntity>();

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
					SET
						session_state = {{0}},
						session_owner_server_id = {{1}},
						session_owner_token = {{2}},
						session_lease_expires_utc = {{3}},
						last_saved = {{4}}
					WHERE id = {{5}} AND deleted = false
						AND session_state = {{6}}
						AND session_owner_server_id = {{7}}
						AND session_owner_token = {{8}}",
					(short)CharacterSessionState.Offline,
					0L,
					Guid.Empty,
					DateTime.UnixEpoch,
					now,
					characterId,
					(short)CharacterSessionState.Online,
					ownerServerId,
					ownerToken
				).ConfigureAwait(false);

				if (rowsAffected == 1)
				{
					return;
				}

				var exists = await dbContext.Characters
					.AnyAsync(c => c.ID == characterId && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (!exists)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}

				throw new DatabaseException(
					"Character is not online under this server.",
					errorCode: DatabaseErrorCodes.InvalidOperation);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> RefreshSessionLeaseAsync(long characterId, long ownerServerId, Guid ownerToken, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || ownerServerId <= 0 || ownerToken == Guid.Empty)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID, owner server ID, or owner token.");
			}

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var newLeaseExpiresUtc = now + DefaultSessionLeaseDuration;
				var tableName = dbContext.GetTableName<CharacterEntity>();

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
					SET
						session_lease_expires_utc = {{0}},
						last_saved = {{1}}
					WHERE id = {{2}} AND deleted = false
						AND session_state = {{3}}
						AND session_owner_server_id = {{4}}
						AND session_owner_token = {{5}}",
					newLeaseExpiresUtc,
					now,
					characterId,
					(short)CharacterSessionState.Online,
					ownerServerId,
					ownerToken
				).ConfigureAwait(false);

				if (rowsAffected == 1)
				{
					return;
				}

				var exists = await dbContext.Characters
					.AnyAsync(c => c.ID == characterId && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (!exists)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}

				throw new DatabaseException(
					"Character is not owned by this server.",
					errorCode: DatabaseErrorCodes.InvalidOperation);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <summary>
		/// Maximum leases sent in one statement. Each lease contributes three parameters, so
		/// this stays far below PostgreSQL's 65535 parameter ceiling while keeping the number
		/// of round trips proportional to population/500 rather than to population.
		/// </summary>
		private const int MaxLeaseRefreshBatchSize = 500;

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> RefreshSessionLeasesAsync(IReadOnlyList<CharacterSessionLeaseData> leases, CancellationToken cancellationToken = default)
		{
			if (leases == null || leases.Count == 0)
			{
				return DatabaseResult<int>.Success(0);
			}

			// Drop malformed entries up front so a single bad row cannot fail the whole batch.
			var valid = new List<CharacterSessionLeaseData>(leases.Count);
			foreach (var lease in leases)
			{
				if (lease.IsValid)
				{
					valid.Add(lease);
				}
			}

			if (valid.Count == 0)
			{
				return DatabaseResult<int>.Success(0);
			}

			int totalRefreshed = 0;

			for (int offset = 0; offset < valid.Count; offset += MaxLeaseRefreshBatchSize)
			{
				int batchSize = Math.Min(MaxLeaseRefreshBatchSize, valid.Count - offset);
				int batchStart = offset;

				var batchResult = await ExecuteTransactionAsync<int>(async dbContext =>
				{
					var now = DateTime.UtcNow;
					var newLeaseExpiresUtc = now + DefaultSessionLeaseDuration;
					var tableName = dbContext.GetTableName<CharacterEntity>();

					// Parameters: {0} new lease, {1} now, {2} Online, then (id, serverId, token) per lease.
					var parameters = new object[3 + (batchSize * 3)];
					parameters[0] = newLeaseExpiresUtc;
					parameters[1] = now;
					parameters[2] = (short)CharacterSessionState.Online;

					var values = new System.Text.StringBuilder();
					for (int i = 0; i < batchSize; i++)
					{
						var lease = valid[batchStart + i];
						int p = 3 + (i * 3);
						parameters[p] = lease.CharacterID;
						parameters[p + 1] = lease.OwnerServerID;
						parameters[p + 2] = lease.OwnerToken;

						if (i > 0)
						{
							values.Append(", ");
						}
						// Explicit casts: a bare parameter inside VALUES gives PostgreSQL nothing
						// to infer the column type from, which fails to resolve the comparison
						// operators in the join predicate below.
						values.Append("(CAST(").Append('{').Append(p).Append("} AS bigint), ")
							  .Append("CAST(").Append('{').Append(p + 1).Append("} AS bigint), ")
							  .Append("CAST(").Append('{').Append(p + 2).Append("} AS uuid))");
					}

					/* Ascending row locks first. This statement and the batched save and release
					 * (PersistManyAsync, ReleaseManyAsync) update overlapping sets of character rows,
					 * and each would otherwise lock them in its own plan order — so two of them could
					 * each hold a row the other waits on. See LockCharacterRowsAscendingAsync. */
					var lockIds = new long[batchSize];
					for (int i = 0; i < batchSize; i++)
					{
						lockIds[i] = valid[batchStart + i].CharacterID;
					}
					await LockCharacterRowsAscendingAsync(dbContext, lockIds, cancellationToken).ConfigureAwait(false);

					// Ownership is verified per row: a server that released the session, or had it
					// claimed away after its lease expired, matches nothing here and so cannot
					// extend the current owner's lease.
					var sql = $@"UPDATE {tableName} AS c
						SET session_lease_expires_utc = {{0}},
							last_saved = {{1}}
						FROM (VALUES {values}) AS v(character_id, owner_server_id, owner_token)
						WHERE c.id = v.character_id
							AND c.deleted = false
							AND c.session_state = {{2}}
							AND c.session_owner_server_id = v.owner_server_id
							AND c.session_owner_token = v.owner_token";

					return await dbContext.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
				}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

				if (!batchResult.IsSuccess)
				{
					return batchResult;
				}

				totalRefreshed += batchResult.Data;
			}

			return DatabaseResult<int>.Success(totalRefreshed);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<long>>> FetchUnownedSessionsAsync(IReadOnlyList<CharacterSessionLeaseData> leases, CancellationToken cancellationToken = default)
		{
			if (leases == null || leases.Count == 0)
			{
				return DatabaseResult<IReadOnlyList<long>>.Success(Array.Empty<long>());
			}

			// Deduplicate and index by character so the comparison below is a dictionary hit
			// rather than a scan, and a caller that passes the same character twice cannot
			// report it twice.
			var expected = new Dictionary<long, CharacterSessionLeaseData>(leases.Count);
			foreach (var lease in leases)
			{
				if (lease.IsValid)
				{
					expected[lease.CharacterID] = lease;
				}
			}

			if (expected.Count == 0)
			{
				return DatabaseResult<IReadOnlyList<long>>.Success(Array.Empty<long>());
			}

			var ids = new List<long>(expected.Keys);

			return await ExecuteReadAsync<IReadOnlyList<long>>(async dbContext =>
			{
				var rows = await dbContext.Characters
					.AsNoTracking()
					.Where(c => ids.Contains(c.ID))
					.Select(c => new
					{
						c.ID,
						c.Deleted,
						c.SessionState,
						c.SessionOwnerServerId,
						c.SessionOwnerToken,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var lost = new List<long>();
				var seen = new HashSet<long>(rows.Count);

				foreach (var row in rows)
				{
					seen.Add(row.ID);

					if (!expected.TryGetValue(row.ID, out CharacterSessionLeaseData lease))
					{
						continue;
					}

					if (row.Deleted ||
						row.SessionState != CharacterSessionState.Online ||
						row.SessionOwnerServerId != lease.OwnerServerID ||
						row.SessionOwnerToken != lease.OwnerToken)
					{
						lost.Add(row.ID);
					}
				}

				// A row that no longer exists cannot be owned either. Reporting it keeps the
				// caller's eviction path total: every ID it asked about is either still owned
				// or returned here.
				foreach (long id in ids)
				{
					if (!seen.Contains(id))
					{
						lost.Add(id);
					}
				}

				return (IReadOnlyList<long>)lost;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para><b>Last-writer-wins (intentional — no version gating):</b></para>
		/// This update intentionally does NOT use version gating (no <c>AND version &lt; {version_param}</c>)
		/// because position data is considered "last writer wins" and is not critical for consistency.
		/// Frequent position updates would cause excessive version conflicts under optimistic concurrency.
		/// <para/>
		/// <para><b>Split-brain scenario:</b></para>
		/// If two server processes concurrently claim ownership of the same character (e.g., due to a
		/// lease expiry race), their position updates will silently overwrite each other without
		/// detection. The character&#39;s persisted position will reflect whichever server wrote last
		/// (approximately), potentially causing brief jitter on the client if it reconnects to the
		/// second server. This is acceptable because:
		/// <list type="bullet">
		///   <item><description>Position is transient state re-synced from the authoritative server on reconnect.</description></item>
		///   <item><description>The session lease system (TryClaim/Release/RefreshSessionLease) bounds the split-brain window to at most one lease duration (~2 minutes).</description></item>
		///   <item><description>Authoritative state (inventory, quests, skills) uses strict version gating and is not affected.</description></item>
		/// </list>
		/// <para/>
		/// <para>If this behavior needs to change, add <c>AND version &lt; {version_param}</c> to the WHERE clause
		/// and pass the incoming version as a parameter.</para>
		/// </remarks>
		public async Task<DatabaseResult> UpdatePositionAsync(long characterId, float x, float y, float z, float rotX, float rotY, float rotZ, float rotW, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var tableName = dbContext.GetTableName<CharacterEntity>();
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
					SET
						x = {{0}},
						y = {{1}},
						z = {{2}},
						rot_x = {{3}},
						rot_y = {{4}},
						rot_z = {{5}},
						rot_w = {{6}},
						last_saved = {{7}}
					WHERE id = {{8}} AND deleted = FALSE",
					x,
					y,
					z,
					rotX,
					rotY,
					rotZ,
					rotW,
					now,
					characterId).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para><b>Last-writer-wins (intentional — no version gating):</b></para>
		/// This update intentionally does NOT use version gating (no <c>AND version &lt; {version_param}</c>)
		/// because scene/position data is considered "last writer wins" and is not critical for consistency.
		/// Frequent scene updates would cause excessive version conflicts under optimistic concurrency.
		/// <para/>
		/// <para><b>Split-brain scenario:</b></para>
		/// If two server processes concurrently claim ownership of the same character (e.g., due to a
		/// lease expiry race), their scene updates will silently overwrite each other without
		/// detection. The character&#39;s persisted scene will reflect whichever server wrote last
		/// (approximately), potentially causing the client to briefly land in the wrong scene on
		/// reconnect. This is acceptable because:
		/// <list type="bullet">
		///   <item><description>Scene is transient state re-synced from the authoritative server on reconnect.</description></item>
		///   <item><description>The session lease system (TryClaim/Release/RefreshSessionLease) bounds the split-brain window to at most one lease duration (~2 minutes).</description></item>
		///   <item><description>Authoritative state (inventory, quests, skills) uses strict version gating and is not affected.</description></item>
		/// </list>
		/// <para/>
		/// <para>If this behavior needs to change, add <c>AND version &lt; {version_param}</c> to the WHERE clause
		/// and pass the incoming version as a parameter.</para>
		/// </remarks>
		public async Task<DatabaseResult> UpdateSceneAsync(long characterId, long worldServerId, string sceneName, long sceneHandle, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var tableName = dbContext.GetTableName<CharacterEntity>();
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
					SET
						world_server_id = {{0}},
						scene_name = {{1}},
						scene_handle = {{2}},
						last_saved = {{3}}
					WHERE id = {{4}} AND deleted = FALSE",
					worldServerId,
					sceneName ?? string.Empty,
					sceneHandle,
					now,
					characterId).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <summary>
		/// Upper bound on the characters one <see cref="UpdateSceneBatchAsync"/> statement names.
		/// Larger batches are written in several statements.
		/// </summary>
		private const int MaxSceneBindsPerStatement = 1000;

		/// <inheritdoc/>
		/// <remarks>
		/// The write <see cref="UpdateSceneAsync"/> makes, for many characters in one statement, with
		/// the same guard (a row that exists and is not deleted) and the same last-writer-wins
		/// semantics, for the reasons given there.
		/// <para>
		/// Rows are locked in id order before they are written, in the mode the UPDATE takes anyway
		/// (<c>FOR NO KEY UPDATE</c>, which leaves foreign-key checks on the row unblocked). Two
		/// multi-row writers that lock overlapping rows in different orders can deadlock; a fixed
		/// order is what prevents it.
		/// </para>
		/// </remarks>
		public async Task<DatabaseResult<IReadOnlyList<long>>> UpdateSceneBatchAsync(
			long worldServerId,
			IReadOnlyList<(long CharacterId, string SceneName, long SceneHandle)> binds,
			CancellationToken cancellationToken = default)
		{
			if (binds == null || binds.Count == 0)
			{
				return DatabaseResult<IReadOnlyList<long>>.Success(Array.Empty<long>());
			}

			// One entry per character. A later entry wins, as it would have had the single-row
			// writes run in order.
			var byId = new SortedDictionary<long, (string SceneName, long SceneHandle)>();
			for (int i = 0; i < binds.Count; ++i)
			{
				var bind = binds[i];
				if (bind.CharacterId > 0)
				{
					byId[bind.CharacterId] = (bind.SceneName ?? string.Empty, bind.SceneHandle);
				}
			}
			if (byId.Count == 0)
			{
				return DatabaseResult<IReadOnlyList<long>>.Success(Array.Empty<long>());
			}

			var entries = byId.ToList();
			var written = new List<long>(entries.Count);
			for (int offset = 0; offset < entries.Count; offset += MaxSceneBindsPerStatement)
			{
				int count = Math.Min(MaxSceneBindsPerStatement, entries.Count - offset);
				var ids = new long[count];
				var sceneNames = new string[count];
				var sceneHandles = new long[count];
				for (int i = 0; i < count; ++i)
				{
					var entry = entries[offset + i];
					ids[i] = entry.Key;
					sceneNames[i] = entry.Value.SceneName;
					sceneHandles[i] = entry.Value.SceneHandle;
				}

				// Absolute values, so a retry after a reply lost past the commit writes the same
				// thing again and returns the same ids.
				var result = await ExecuteWriteAsync(async dbContext =>
				{
					var tableName = dbContext.GetTableName<CharacterEntity>();
					var sql = $@"WITH batch AS (
							SELECT * FROM unnest({{1}}::bigint[], {{2}}::text[], {{3}}::bigint[]) AS b(id, scene_name, scene_handle)
						),
						locked AS (
							SELECT c.id, batch.scene_name, batch.scene_handle FROM {tableName} AS c
							JOIN batch ON batch.id = c.id
							WHERE c.deleted = FALSE
							ORDER BY c.id
							FOR NO KEY UPDATE OF c
						)
						UPDATE {tableName} AS c
						SET world_server_id = {{0}},
							scene_name = locked.scene_name,
							scene_handle = locked.scene_handle,
							last_saved = {{4}}
						FROM locked
						WHERE c.id = locked.id
						RETURNING c.id";

					return await ReadRowsAsync(
						dbContext,
						sql,
						new object[] { worldServerId, ids, sceneNames, sceneHandles, DateTime.UtcNow },
						reader => reader.GetInt64(0),
						cancellationToken).ConfigureAwait(false);
				}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

				if (!result.IsSuccess)
				{
					return DatabaseResult<IReadOnlyList<long>>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
				}
				written.AddRange(result.Data);
			}

			return DatabaseResult<IReadOnlyList<long>>.Success(written);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<(CharacterData Character, CharacterLockState Lock)?>> FetchSelectedWithLockAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<(CharacterData Character, CharacterLockState Lock)?>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			// The row FetchByAccountAsync(selected: true) reads already carries the lock columns;
			// FetchLockAsync read the same row a second time just to get them.
			return await ExecuteReadAsync<(CharacterData Character, CharacterLockState Lock)?>(async dbContext =>
			{
				var entity = await fetchByAccountSelectedQuery(dbContext, accountName, true, cancellationToken).ConfigureAwait(false);
				if (entity == null)
				{
					return null;
				}
				return (MapEntityToData(entity), new CharacterLockState(entity.LockedUntil, entity.LockedBy, entity.LockReason));
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterData>>> FetchSelectedCharactersByAccountsAsync(
			List<string> accounts,
			int maxBatchSize = 1000,
			CancellationToken cancellationToken = default)
		{
			if (accounts == null || accounts.Count == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterData>>.Success(Array.Empty<CharacterData>());
			}

			if (maxBatchSize < 500) maxBatchSize = 500;
			else if (maxBatchSize > 2500) maxBatchSize = 2500;

			var allResults = new List<CharacterData>(accounts.Count);

			for (int offset = 0; offset < accounts.Count; offset += maxBatchSize)
			{
				var batchCount = Math.Min(maxBatchSize, accounts.Count - offset);
				var batch = accounts.GetRange(offset, batchCount);

				var result = await ExecuteReadAsync(async dbContext =>
				{
					var batchArray = batch.ToArray();
					return await dbContext.Characters
						.AsNoTracking()
						.Where(c => batchArray.Contains(c.Account) && c.Selected && !c.Deleted)
						.ToListAsync(cancellationToken)
						.ConfigureAwait(false);
				}, cancellationToken: cancellationToken).ConfigureAwait(false);

				if (!result.IsSuccess)
				{
					return DatabaseResult<IReadOnlyList<CharacterData>>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
				}

				foreach (var entity in result.Data)
				{
					allResults.Add(MapEntityToData(entity));
				}
			}

			return DatabaseResult<IReadOnlyList<CharacterData>>.Success(allResults);
		}

		/// <summary>
		/// Bit mask for <c>CharacterFlags.IsCombatLogged</c> (bit 13).
		/// </summary>
		/// <remarks>
		/// Written as a literal rather than referencing the enum so the expression stays
		/// translatable to SQL — <c>IsFlagged</c> is a generic extension method EF cannot
		/// convert. Kept in sync with <c>FishMMO.Shared.CharacterFlags.IsCombatLogged</c>.
		/// </remarks>
		private const int CombatLoggedFlagMask = 1 << 13;

		/// <inheritdoc/>
		public async Task<DatabaseResult> ClearCombatLoggedAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var tableName = dbContext.GetTableName<CharacterEntity>();

				// Deliberately not version-gated: this clears a single derived bit rather than
				// competing with gameplay state, and the caller resorts to it precisely when the
				// server that would have bumped the version is gone.
				await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
					SET flags = flags & {~CombatLoggedFlagMask}
					WHERE id = {{0}} AND deleted = false",
					characterId).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>Longest operator account name the moderation columns store.</summary>
		private const int ModerationActorMaxLength = 50;

		/// <summary>Longest reason the moderation columns store.</summary>
		private const int ModerationReasonMaxLength = 256;

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistMuteAsync(long characterId, DateTime? mutedUntilUtc, string? mutedBy, string? reason, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				// Not version-gated: the save path never names these columns, so there is no
				// gameplay write for a mute to race.
				int rows = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {TableName}
					SET muted = TRUE,
						muted_until = {{1}},
						muted_by = {{2}},
						mute_reason = {{3}}
					WHERE id = {{0}} AND deleted = false",
					new object[]
					{
						characterId,
						(object?)mutedUntilUtc,
						(object?)ClipModerationText(mutedBy, ModerationActorMaxLength),
						(object?)ClipModerationText(reason, ModerationReasonMaxLength),
					}, cancellationToken).ConfigureAwait(false);
				if (rows == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", $"{characterId}");
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> LockAsync(long characterId, DateTime lockedUntilUtc, string lockedBy, string reason, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}
			if (lockedUntilUtc <= DateTime.UtcNow)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "A lock must end in the future.");
			}
			if (string.IsNullOrWhiteSpace(lockedBy) || string.IsNullOrWhiteSpace(reason))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "A lock needs the staff account and a reason.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Not version-gated, like the mute: the save path never names these columns, so no
				 * gameplay write can race it. Placing a lock over an existing one replaces it — an
				 * operator extending or shortening a lock is the normal case, and the audit log holds
				 * the history the row does not. */
				int rows = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {TableName}
					SET locked_until = {{1}},
						locked_at = timezone('UTC', CURRENT_TIMESTAMP),
						locked_by = {{2}},
						lock_reason = {{3}}
					WHERE id = {{0}} AND deleted = false",
					new object[]
					{
						characterId,
						lockedUntilUtc,
						ClipModerationText(lockedBy, ModerationActorMaxLength),
						ClipModerationText(reason, ModerationReasonMaxLength),
					}, cancellationToken).ConfigureAwait(false);
				if (rows == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", $"{characterId}");
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> UnlockAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				int rows = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {TableName}
					SET locked_until = NULL,
						locked_at = NULL,
						locked_by = NULL,
						lock_reason = NULL
					WHERE id = {{0}} AND locked_until IS NOT NULL",
					new object[] { characterId }, cancellationToken).ConfigureAwait(false);
				return rows > 0;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterLockState>> FetchLockAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterLockState>.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var row = await dbContext.Characters
					.AsNoTracking()
					.Where(c => c.ID == characterId)
					.Select(c => new { c.LockedUntil, c.LockedBy, c.LockReason })
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);
				if (row == null)
				{
					throw new DatabaseEntityNotFoundException("Character", $"{characterId}");
				}
				return new CharacterLockState(row.LockedUntil, row.LockedBy, row.LockReason);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> ClearMuteAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				int rows = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {TableName}
					SET muted = FALSE,
						muted_until = NULL,
						muted_by = NULL,
						mute_reason = NULL
					WHERE id = {{0}} AND deleted = false",
					new object[] { characterId }, cancellationToken).ConfigureAwait(false);
				if (rows == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", $"{characterId}");
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterChatMuteState>> FetchChatMuteAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterChatMuteState>.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var row = await (
					from c in dbContext.Characters.AsNoTracking()
					join a in dbContext.Accounts.AsNoTracking() on c.Account equals a.Name
					where c.ID == characterId && !c.Deleted
					select new
					{
						c.Muted,
						c.MutedUntil,
						c.MutedBy,
						c.MuteReason,
						AccountMuted = a.Muted,
						AccountMutedUntil = a.MutedUntil,
						AccountMutedBy = a.MutedBy,
						AccountMuteReason = a.MuteReason,
					})
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				if (row == null)
				{
					throw new DatabaseEntityNotFoundException("Character", $"{characterId}");
				}

				return new CharacterChatMuteState(
					new ChatMuteData(row.AccountMuted, row.AccountMutedUntil, row.AccountMutedBy, row.AccountMuteReason),
					new ChatMuteData(row.Muted, row.MutedUntil, row.MutedBy, row.MuteReason));
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>Cuts operator-supplied text to a column's length rather than failing the write on it.</summary>
		private static string? ClipModerationText(string? text, int maxLength)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}
			text = text.Trim();
			return text.Length <= maxLength ? text : text.Substring(0, maxLength);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<DateTime?>> TryBeginChannelSwitchAsync(long characterId, TimeSpan cooldown, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<DateTime?>.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			if (cooldown < TimeSpan.Zero)
			{
				cooldown = TimeSpan.Zero;
			}

			DateTime nowUtc = DateTime.UtcNow;
			DateTime eligibleBeforeUtc = nowUtc - cooldown;

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var tableName = dbContext.GetTableName<CharacterEntity>();

				/* Check and stamp in one statement.
				 *
				 * Reading the timestamp and writing it back separately would let two scene
				 * servers each read an elapsed cooldown and each allow a switch — which is
				 * precisely the case that matters, because the whole point of persisting this is
				 * that consecutive switches land on different servers. The row is not
				 * version-gated: this is a rate limit, not gameplay state, and it must neither
				 * lose to a concurrent save nor bump the version a save is guarding on.
				 *
				 * The pre-update value comes back through a CTE, because RETURNING reports the row
				 * as written. The caller needs it to undo the claim when the transfer it was taken
				 * for does not happen — see RollbackChannelSwitchAsync. */
				/* One row always comes back, saying whether the character exists as well as what the
				 * claim replaced: a missing or deleted character used to return no row, which reads
				 * as "on cooldown", so the player was told they were travelling too often
				 * (issue #267). It is NotFound now. */
				var sql = $@"WITH previous AS (
						SELECT id, last_channel_switch_utc
						FROM {tableName}
						WHERE id = {{1}} AND deleted = false
					),
					stamped AS (
						UPDATE {tableName} AS c
						SET last_channel_switch_utc = {{0}}
						FROM previous
						WHERE c.id = previous.id
							AND previous.last_channel_switch_utc <= {{2}}
						RETURNING previous.last_channel_switch_utc AS was
					)
					SELECT (SELECT COUNT(*) FROM previous), (SELECT was FROM stamped)";

				var outcome = await ExecuteReturningOrDefaultAsync(
					dbContext,
					sql,
					new object[] { nowUtc, characterId, eligibleBeforeUtc },
					reader => (Found: reader.GetInt64(0) > 0, Was: reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1)),
					cancellationToken).ConfigureAwait(false);

				if (!outcome.Found)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}
				return outcome.Was;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> RollbackChannelSwitchAsync(long characterId, DateTime previousUtc, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var tableName = dbContext.GetTableName<CharacterEntity>();

				/* Guarded on the column still being ahead of what we are restoring, so this is a
				 * no-op if the claim has already been superseded. Nothing else writes this column
				 * and a character has one session at a time, so the only thing that can be ahead
				 * of previousUtc is the claim being undone. */
				await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
					SET last_channel_switch_utc = {{0}}
					WHERE id = {{1}}
						AND deleted = false
						AND last_channel_switch_utc > {{0}}",
					new object[] { previousUtc, characterId },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterData?>> FetchInWorldCharacterAsync(string account, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(account))
			{
				return DatabaseResult<CharacterData?>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			var nowUtc = DateTime.UtcNow;

			return await ExecuteReadAsync<CharacterData?>(async dbContext =>
			{
				// Same expired-lease rule as AnyOnlineAsync: a claim whose lease has lapsed
				// belongs to a server that is no longer there, so it must not block the player
				// from selecting a different character.
				var entity = await dbContext.Characters
					.AsNoTracking()
					.FirstOrDefaultAsync(c => c.Account == account &&
											  !c.Deleted &&
											  c.SessionState != CharacterSessionState.Offline &&
											  c.SessionLeaseExpiresUtc > nowUtc, cancellationToken)
					.ConfigureAwait(false);

				return entity == null ? (CharacterData?)null : MapEntityToData(entity);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// A character whose body is running out a combat-logout timer is deliberately NOT
		/// counted as online. Its session is still claimed — that is what keeps the body
		/// authoritative and stops another server taking it — but the player who owns it must
		/// be able to log back in and rejoin it. Counting it here would lock them out of their
		/// own character for the whole linger window, which is the opposite of the intent.
		/// A genuine second session still blocks, because a live session never carries this flag.
		/// <para>
		/// A claim whose lease has expired does not count either. <see cref="TryClaimAsync"/>
		/// treats an expired lease as free — that is the whole recovery path for a scene server
		/// that died holding characters — but this check did not, so the row a crashed server
		/// left behind reported its owner as permanently online. The account was then refused at
		/// login forever with "already online", against a session that no longer existed and a
		/// character any server was free to claim. Matching the claim predicate here bounds that
		/// to one lease duration.
		/// </para>
		/// </remarks>
		public async Task<DatabaseResult<bool>> AnyOnlineAsync(string account, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(account))
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			var nowUtc = DateTime.UtcNow;

			var result = await ExecuteReadAsync(async dbContext =>
			{
				return await dbContext.Characters
					.AsNoTracking()
					.AnyAsync(c => c.Account == account &&
								   !c.Deleted &&
								   c.SessionState != CharacterSessionState.Offline &&
								   c.SessionLeaseExpiresUtc > nowUtc &&
								   (c.Flags & CombatLoggedFlagMask) == 0, cancellationToken)
					.ConfigureAwait(false);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <summary>
		/// Maps a CharacterEntity to CharacterData DTO.
		/// </summary>
		/// <param name="entity">The character entity.</param>
		/// <returns>The character data DTO.</returns>
		private static CharacterData MapEntityToData(CharacterEntity entity)
		{
			/* The claim predicate every other reader here uses (AnyOnlineAsync, the online filter,
			 * FetchInWorldCharacterAsync): a claim whose lease has lapsed belongs to a server that is
			 * no longer there. Without the lease check, the row a crashed scene server left behind
			 * reported its character online to the friend list, to whispers and to the Control Panel
			 * until something claimed it again (issue #267). */
			var online = entity.SessionState == CharacterSessionState.Online &&
						 entity.SessionLeaseExpiresUtc > DateTime.UtcNow;

			return new CharacterData(
				entity.ID,
				entity.Name,
				entity.NameLowercase,
				entity.Account,
				entity.Selected,
				entity.WorldServerID,
				entity.SceneName,
				entity.SceneHandle,
				entity.BindScene,
				entity.BindX,
				entity.BindY,
				entity.BindZ,
				entity.InstanceID,
				entity.InstanceX,
				entity.InstanceY,
				entity.InstanceZ,
				entity.InstanceRotX,
				entity.InstanceRotY,
				entity.InstanceRotZ,
				entity.InstanceRotW,
				entity.RaceID,
				entity.ModelIndex,
				entity.X,
				entity.Y,
				entity.Z,
				entity.RotX,
				entity.RotY,
				entity.RotZ,
				entity.RotW,
				entity.AccessLevel,
				online,
				entity.Flags,
				entity.Version,
				entity.TimeCreated,
				entity.LastSaved
			);
		}
	}
}