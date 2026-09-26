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
	/// Service for managing character abilities in the database.
	/// Provides async operations for CRUD operations on character ability data.
	/// Uses the BaseService execution strategy for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	public sealed class CharacterAbilityService : BaseService<CharacterAbilityEntity>, ICharacterAbilityService
	{
		/// <summary>
		/// Compiled query for retrieving character abilities (hot path for character load).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, IAsyncEnumerable<CharacterAbilityEntity>> getAbilitiesQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId) =>
				context.CharacterAbilities
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId && !a.Deleted));

		/// <summary>
		/// Compiled query for counting character abilities.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> getCountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterAbilities
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId && !a.Deleted)
					.Count());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterAbilityService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterAbilityService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> CountAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<int>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
				await getCountQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public Task<DatabaseResult<long>> PersistAsync(CharacterAbilityData abilityData, CancellationToken cancellationToken = default)
			=> PersistOneAsync(abilityData, null, cancellationToken);

		/// <inheritdoc/>
		public Task<DatabaseResult<long>> PersistOwnedAsync(CharacterAbilityData abilityData, CharacterSessionLeaseData claim, CancellationToken cancellationToken = default)
		{
			string? invalid = CharacterWriteGate.ValidateClaim(claim, abilityData.CharacterID);
			return invalid != null
				? Task.FromResult(DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, invalid))
				: PersistOneAsync(abilityData, claim, cancellationToken);
		}

		/// <summary>
		/// The single-row write behind <see cref="PersistAsync(CharacterAbilityData, CancellationToken)"/> and
		/// <see cref="PersistOwnedAsync(CharacterAbilityData, CharacterSessionLeaseData, CancellationToken)"/>.
		/// </summary>
		/// <param name="abilityData">The row.</param>
		/// <param name="claim">The writer's claim, or null for the ungated write. See <see cref="CharacterWriteGate"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		private async Task<DatabaseResult<long>> PersistOneAsync(CharacterAbilityData abilityData, CharacterSessionLeaseData? claim, CancellationToken cancellationToken)
		{
			if (abilityData.CharacterID <= 0)
			{
				return DatabaseResult<long>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			if (abilityData.Version <= 0)
			{
				return DatabaseResult<long>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid version. Version must be greater than 0.");
			}

			var result = await ExecuteTransactionAsync<long>(async dbContext =>
			{
				/* The ownership gate, and the existence check it subsumes (the only check the ungated
				 * write makes). An owned write holds the character's share lock from here to the
				 * commit, so the release it would otherwise race waits behind it. See
				 * CharacterWriteGate. */
				await CharacterWriteGate.AdmitOneAsync(dbContext, abilityData.CharacterID, claim, cancellationToken).ConfigureAwait(false);

				var now = DateTime.UtcNow;
				var abilityEvents = abilityData.AbilityEvents?.ToArray() ?? Array.Empty<int>();
				var sql = $@"
					WITH upserted AS (
						INSERT INTO {TableName}
							(character_id, template_id, version, ability_events, cooldown, time_created, deleted, time_deleted)
						VALUES
							({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, FALSE, NULL)
						ON CONFLICT (character_id, template_id)
						DO UPDATE SET
							ability_events = EXCLUDED.ability_events,
							cooldown = EXCLUDED.cooldown,
							deleted = FALSE,
							time_deleted = NULL,
							version = EXCLUDED.version
						WHERE
							EXCLUDED.version > {TableName}.version
						RETURNING id
					)
					SELECT COALESCE((SELECT id FROM upserted LIMIT 1), 0)::bigint AS value";

				var id = await ExecuteScalarLongAsync(
					dbContext,
					sql,
					new object[] { abilityData.CharacterID, abilityData.TemplateID, abilityData.Version, abilityEvents, abilityData.Cooldown, now },
					cancellationToken).ConfigureAwait(false);

				if (id <= 0)
				{
					throw new StaleStateException("Ability persist rejected due to a stale Version.");
				}

				return id;
			}, saveChanges: false, operationName: claim.HasValue ? nameof(PersistOwnedAsync) : nameof(PersistAsync), cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public Task<DatabaseResult<BulkWriteResult>> PersistAsync(IEnumerable<CharacterAbilityData> abilities, CancellationToken cancellationToken = default)
			=> PersistBatchAsync(abilities, null, cancellationToken);

		/// <inheritdoc/>
		public Task<DatabaseResult<BulkWriteResult>> PersistOwnedAsync(IEnumerable<CharacterAbilityData> abilities, IReadOnlyCollection<CharacterSessionLeaseData> claims, CancellationToken cancellationToken = default)
		{
			string? invalid = CharacterWriteGate.ValidateClaims(claims);
			return invalid != null
				? Task.FromResult(DatabaseResult<BulkWriteResult>.Failure(DatabaseErrorCodes.ValidationError, invalid))
				: PersistBatchAsync(abilities, claims, cancellationToken);
		}

		/// <summary>
		/// The batch write behind <see cref="PersistAsync(IEnumerable{CharacterAbilityData}, CancellationToken)"/> and
		/// <see cref="PersistOwnedAsync"/>.
		/// </summary>
		/// <param name="abilities">Rows to write.</param>
		/// <param name="claims">The writer's claims, or null for the ungated write. See <see cref="CharacterWriteGate"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		private async Task<DatabaseResult<BulkWriteResult>> PersistBatchAsync(IEnumerable<CharacterAbilityData> abilities, IReadOnlyCollection<CharacterSessionLeaseData>? claims, CancellationToken cancellationToken)
		{
			if (abilities == null || !abilities.Any())
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Abilities collection must not be null or empty.");
			}

			var list = abilities.ToList();
			if (list.Any(a => a.Version <= 0))
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more abilities had an invalid Version. Version must be greater than 0.");
			}
			var newItems = list.Where(a => a.ID <= 0).ToList();
			var existingItems = list.Where(a => a.ID > 0).ToList();

			// Deduplicate across new and existing groups to prevent the same (CharacterID, TemplateID)
			// pair from appearing in both, which would cause a double-update conflict.
			// When a duplicate is found, keep the one with the higher Version.
			if (newItems.Count > 0 && existingItems.Count > 0)
			{
				var newItemLookup = newItems.ToDictionary(a => (a.CharacterID, a.TemplateID));
				var existingItemLookup = existingItems.ToDictionary(a => (a.CharacterID, a.TemplateID));

				var crossKeys = new HashSet<(long CharacterID, int TemplateID)>(newItemLookup.Keys);
				crossKeys.IntersectWith(existingItemLookup.Keys);

				foreach (var key in crossKeys)
				{
					if (existingItemLookup[key].Version >= newItemLookup[key].Version)
					{
						newItems.Remove(newItemLookup[key]);
					}
					else
					{
						existingItems.Remove(existingItemLookup[key]);
					}
				}
			}

			// One row per key, the newest version: see BulkBatch.KeepNewest. An upsert cannot touch one
			// row twice, and an UPDATE ... FROM matching one row twice is ambiguous.
			newItems = BulkBatch.KeepNewest(newItems, ability => (ability.CharacterID, ability.TemplateID), ability => ability.Version);

			// One row per key, the newest version: see BulkBatch.KeepNewest. An upsert cannot touch one
			// row twice, and an UPDATE ... FROM matching one row twice is ambiguous.
			existingItems = BulkBatch.KeepNewest(existingItems, ability => ability.ID, ability => ability.Version);

			int suppliedRows = list.Count;

			return await ExecuteTransactionAsync<BulkWriteResult>(async dbContext =>
			{
				/* Both branches contribute. The batch is split by whether a row already has a
				 * primary key, so neither statement alone describes what the caller asked for. */
				var allCharacterIds = list.Select(a => a.CharacterID).Distinct().ToArray();
				/* The ownership gate, and the per-character existence check it subsumes: the rows of a
				 * missing or deleted character, or — for an owned write — of one whose claim the writer
				 * no longer holds, are left out and reported as Filtered rather than failing every other
				 * character's rows with them. See CharacterWriteGate. */
				CharacterWriteAdmission admission = await CharacterWriteGate.AdmitAsync(dbContext, allCharacterIds, claims, cancellationToken).ConfigureAwait(false);
				int unownedRows = admission.CountUnowned(newItems, row => row.CharacterID) + admission.CountUnowned(existingItems, row => row.CharacterID);
				BulkWriteResult outcome = new BulkWriteResult(suppliedRows, 0, 0, unownedRows);

				var activeNewItems = newItems
					.Where(a => admission.Admits(a.CharacterID))
					.ToList();
				var activeExistingItems = existingItems
					.Where(a => admission.Admits(a.CharacterID))
					.ToList();

				if (activeExistingItems.Count > 0)
				{
					var ids = activeExistingItems.Select(a => a.ID).Distinct().ToArray();
					var existingIds = await dbContext.CharacterAbilities
						.AsNoTracking()
						.Where(a => ids.Contains(a.ID))
						.Select(a => a.ID)
						.ToListAsync(cancellationToken)
						.ConfigureAwait(false);
					var existingIdSet = new HashSet<long>(existingIds);
					activeExistingItems = activeExistingItems.Where(a => existingIdSet.Contains(a.ID)).ToList();
				}

				var now = DateTime.UtcNow;

				if (activeExistingItems.Count > 0)
				{
					var idArray = activeExistingItems.Select(a => a.ID).ToArray();
					var characterIdArray = activeExistingItems.Select(a => a.CharacterID).ToArray();
					var templateIdArray = activeExistingItems.Select(a => a.TemplateID).ToArray();
					var versionArray = activeExistingItems.Select(a => a.Version).ToArray();
					var abilityEventsJson = ToJaggedIntArrayJson(activeExistingItems
						.Select(a => (a.AbilityEvents ?? new List<int>()).ToArray())
						.ToArray());
					var cooldownArray = activeExistingItems.Select(a => a.Cooldown).ToArray();

					// ability_events is a ragged (non-rectangular) set of per-row integer[] values, which
					// Npgsql/EF Core 5 cannot reliably bind as a native integer[][] parameter. It is sent as
					// jsonb instead and decoded server-side, joined back to the other UNNESTed columns by
					// ordinal position. See BaseService.ToJaggedIntArrayJson for details.
					var sql = $@"
						UPDATE {TableName} AS t
						SET
							character_id = u.character_id,
							template_id = u.template_id,
							ability_events = u.ability_events,
							cooldown = u.cooldown,
							deleted = FALSE,
							time_deleted = NULL,
							version = u.version
						FROM (
							SELECT
								ids.id,
								ids.character_id,
								ids.template_id,
								ids.version,
								events.ability_events,
								ids.cooldown
							FROM UNNEST(
								{{0}}::bigint[],
								{{1}}::bigint[],
								{{2}}::integer[],
								{{3}}::bigint[],
								{{5}}::real[]
							) WITH ORDINALITY AS ids(id, character_id, template_id, version, cooldown, ord)
							JOIN (
								SELECT
									arr.ord,
									ARRAY(SELECT elem::integer FROM jsonb_array_elements_text(arr.value) AS elem) AS ability_events
								FROM jsonb_array_elements({{4}}::jsonb) WITH ORDINALITY AS arr(value, ord)
							) AS events ON events.ord = ids.ord
						) AS u
						WHERE t.id = u.id
							AND u.version > t.version;";

					int appliedExisting = await ExecuteBulkUpsertAsync(
						dbContext,
						sql,
						activeExistingItems.Count,
						new object[] { idArray, characterIdArray, templateIdArray, versionArray, abilityEventsJson, cooldownArray },
						"One or more abilities were rejected due to a stale Version.",
						cancellationToken,
						BulkVersionConflictPolicy.SkipStaleRows).ConfigureAwait(false);

					outcome += new BulkWriteResult(0, activeExistingItems.Count, appliedExisting);
				}

				if (activeNewItems.Count > 0)
				{
					var characterIdArray = activeNewItems.Select(a => a.CharacterID).ToArray();
					var templateIdArray = activeNewItems.Select(a => a.TemplateID).ToArray();
					var versionArray = activeNewItems.Select(a => a.Version).ToArray();
					var abilityEventsJson = ToJaggedIntArrayJson(activeNewItems
						.Select(a => (a.AbilityEvents ?? new List<int>()).ToArray())
						.ToArray());
					var cooldownArray = activeNewItems.Select(a => a.Cooldown).ToArray();

					// See the UPDATE branch above: ability_events is sent as jsonb and decoded server-side
					// instead of as a native integer[][] parameter.
					var sql = $@"
						INSERT INTO {TableName}
							(character_id, template_id, version, ability_events, cooldown, time_created, deleted, time_deleted)
						SELECT
							u.character_id,
							u.template_id,
							u.version,
							events.ability_events,
							u.cooldown,
							{{5}},
							FALSE,
							NULL
						FROM UNNEST(
							{{0}}::bigint[],
							{{1}}::integer[],
							{{2}}::bigint[],
							{{4}}::real[]
						) WITH ORDINALITY AS u(character_id, template_id, version, cooldown, ord)
						JOIN (
							SELECT
								arr.ord,
								ARRAY(SELECT elem::integer FROM jsonb_array_elements_text(arr.value) AS elem) AS ability_events
							FROM jsonb_array_elements({{3}}::jsonb) WITH ORDINALITY AS arr(value, ord)
						) AS events ON events.ord = u.ord
						ON CONFLICT (character_id, template_id)
						DO UPDATE SET
							ability_events = EXCLUDED.ability_events,
							cooldown = EXCLUDED.cooldown,
							deleted = FALSE,
							time_deleted = NULL,
							version = EXCLUDED.version
						WHERE
							EXCLUDED.version > {TableName}.version;";

					int appliedNew = await ExecuteBulkUpsertAsync(
						dbContext,
						sql,
						activeNewItems.Count,
						new object[] { characterIdArray, templateIdArray, versionArray, abilityEventsJson, cooldownArray, now },
						"One or more abilities were rejected due to a stale Version.",
						cancellationToken,
						BulkVersionConflictPolicy.SkipStaleRows).ConfigureAwait(false);

					outcome += new BulkWriteResult(0, activeNewItems.Count, appliedNew);
				}

				return outcome;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid version. Version must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}, version = {{1}}
					WHERE character_id = {{2}} AND deleted = FALSE AND version < {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, incomingVersion, characterId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var anyActive = await dbContext.CharacterAbilities
						.AsNoTracking()
						.AnyAsync(a => a.CharacterID == characterId && !a.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Ability delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, long abilityId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || abilityId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID and ability ID must be greater than 0.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid version. Version must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}, version = {{1}}
					WHERE id = {{2}} AND character_id = {{3}} AND deleted = FALSE AND version < {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, incomingVersion, abilityId, characterId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var stillActive = await dbContext.CharacterAbilities
						.AsNoTracking()
						.AnyAsync(a => a.ID == abilityId && a.CharacterID == characterId && !a.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (stillActive)
					{
						throw new StaleStateException("Ability delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public Task<DatabaseResult> DeleteAbilityAsync(long characterId, long abilityId, long incomingVersion, CancellationToken cancellationToken = default)
			=> DeleteAbilityCoreAsync(characterId, abilityId, incomingVersion, null, admitReleased: false, cancellationToken);

		/// <inheritdoc/>
		public Task<DatabaseResult> DeleteAbilityOwnedAsync(long characterId, long abilityId, long incomingVersion, CharacterSessionLeaseData claim, bool admitReleased = false, CancellationToken cancellationToken = default)
		{
			string? invalid = CharacterWriteGate.ValidateClaim(claim, characterId);
			return invalid != null
				? Task.FromResult(DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, invalid))
				: DeleteAbilityCoreAsync(characterId, abilityId, incomingVersion, claim, admitReleased, cancellationToken);
		}

		/// <summary>
		/// The delete behind <see cref="DeleteAbilityAsync"/> and <see cref="DeleteAbilityOwnedAsync"/>.
		/// </summary>
		/// <param name="characterId">The owning character.</param>
		/// <param name="abilityId">The row identity.</param>
		/// <param name="incomingVersion">The version ceiling the row must be at or below.</param>
		/// <param name="claim">The writer's claim, or null for the ungated delete. See <see cref="CharacterWriteGate"/>.</param>
		/// <param name="admitReleased">
		/// With a claim, also admit a character that holds no claim at all — for a delete that undoes
		/// the writer's own row. See <see cref="CharacterWriteGate.AdmitOwnOrReleasedAsync"/>.
		/// </param>
		/// <param name="cancellationToken">Cancellation token.</param>
		private async Task<DatabaseResult> DeleteAbilityCoreAsync(long characterId, long abilityId, long incomingVersion, CharacterSessionLeaseData? claim, bool admitReleased, CancellationToken cancellationToken)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID");
			}

			if (abilityId <= 0)
			{
				// An ability that was never written has nothing to delete, and asking to remove id 0
				// would match every unassigned row if the guard below were ever relaxed. Report it
				// rather than issuing the statement. This is also the shape a crafted ability arrives
				// in before its identity has been written back, so the refusal is load-bearing: a
				// silent success here would tell the caller an ability was forgotten that never was.
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid ability ID. The ability has no database identity to delete.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid version. Version must be greater than 0.");
			}

			Func<NpgsqlDbContext, Task> delete = async dbContext =>
			{
				// A forgotten ability is removed outright rather than left as a tombstone. The upsert
				// keys on (character_id, template_id) and only writes when the incoming version beats
				// the surviving row's, so a soft delete — which stamps the caller's version into that
				// row — would leave it holding a version no fresh craft could outrank. The re-craft
				// would then come back from the upsert with id 0 and be rejected as stale, making
				// forget-then-craft, the flow the craft panel advertises, permanently impossible.
				// CharacterItemService removes a vacated row for the same reason.
				// The character is part of the predicate so one character's delete can never reach
				// another's row, even if a caller passes an id it does not own.
				var sql = $@"DELETE FROM {TableName}
					WHERE id = {{1}} AND character_id = {{2}} AND version <= {{0}}";

				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { incomingVersion, abilityId, characterId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					// Nothing was removed. Either the row is already gone — a second forget, or a
					// tombstone left by the character-wide delete — which is a success, or it is
					// still here and outranks the version the caller quoted, which is not.
					var row = await dbContext.CharacterAbilities
						.AsNoTracking()
						.FirstOrDefaultAsync(a => a.ID == abilityId && a.CharacterID == characterId, cancellationToken)
						.ConfigureAwait(false);

					if (row != null && !row.Deleted)
					{
						throw new StaleStateException("Ability delete rejected due to a stale Version.");
					}
				}
			};

			if (!claim.HasValue)
			{
				return await ExecuteWriteAsync(delete, saveChanges: false, operationName: nameof(DeleteAbilityAsync), cancellationToken: cancellationToken).ConfigureAwait(false);
			}

			/* Owned: the gate and the delete in one transaction, so the claim it admits is held — under
			 * the character's share lock — until the row is gone. A delete is naturally idempotent, so
			 * the transaction's retry after a lost commit reply finds the row already gone and succeeds. */
			CharacterSessionLeaseData held = claim.Value;
			return await ExecuteTransactionAsync(async dbContext =>
			{
				if (admitReleased)
				{
					await CharacterWriteGate.AdmitOwnOrReleasedAsync(dbContext, held, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					await CharacterWriteGate.AdmitOneAsync(dbContext, characterId, held, cancellationToken).ConfigureAwait(false);
				}
				await delete(dbContext).ConfigureAwait(false);
			}, saveChanges: false, operationName: nameof(DeleteAbilityOwnedAsync), cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterAbilityData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterAbilityData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getAbilitiesQuery(dbContext, characterId).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				var abilities = entities.Select(a => new CharacterAbilityData(
					id: a.ID,
					version: a.Version,
					characterID: a.CharacterID,
					templateID: a.TemplateID,
					abilityEvents: a.AbilityEvents,
					cooldown: a.Cooldown
				)).ToList();

				return (IReadOnlyList<CharacterAbilityData>)abilities;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}