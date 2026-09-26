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
	/// Service for managing character pet data in the database.
	/// Provides async operations for CRUD operations on character pet data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character pet persistence including:
	/// - Pet save/update with atomic UPSERT operations
	/// - Pet spawn state management
	/// - Pet deletion (soft delete)
	/// - Pet retrieval (including spawned pet queries)
	/// - Pet attribute and buff data management
	/// 
	/// Database operations are executed via the BaseService execution wrappers for:
	/// - Automatic transient failure retry
	/// - Centralized exception handling and mapping
	/// - Consistent DatabaseResult pattern
	/// 
	/// When a write requires multiple database statements, it should be wrapped in
	/// <see cref="BaseService{TEntity}.ExecuteTransactionAsync"/>. Single-statement SQL
	/// operations (including CTE-based UPSERT/DELETE/UPDATE) are executed atomically without
	/// requiring an explicit transaction wrapper.
	/// </remarks>
	public sealed class CharacterPetService : BaseService<CharacterPetEntity>, ICharacterPetService
	{
		/// <summary>
		/// Compiled query for checking whether a character exists and is not deleted.
		/// Returns the character ID if active, otherwise 0.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<long>> getActiveCharacterIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.Where(c => c.ID == characterId && !c.Deleted)
					.Select(c => c.ID)
					.FirstOrDefault());

		/// <summary>
		/// Compiled query for retrieving character pet.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterPetEntity?>> getPetQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterPets
					.AsNoTracking()
					.FirstOrDefault(p => p.CharacterID == characterId && !p.Deleted));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving spawned character pet.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterPetEntity?>> getSpawnedPetQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterPets
					.AsNoTracking()
					.FirstOrDefault(p => p.CharacterID == characterId && p.Spawned && !p.Deleted));
#pragma warning restore CS8619

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterPetService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterPetService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(CharacterPetData petData, CancellationToken cancellationToken = default)
		{
			if (petData.CharacterID <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			/* Zero is the only "no template" value. Template ids are the signed deterministic hash of
			 * the template's type and asset name (CachedScriptableObject.AddToCache), so about half of
			 * all templates have a NEGATIVE id; rejecting <= 0 here silently refused to save them. */
			if (petData.TemplateID == 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Template ID must not be 0.");
			}

			if (petData.Version <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid version. Version must be greater than 0.");
			}

			/* A transaction now, not a lone statement: the pet row and the pruning of the rows it
			 * leaves unrestorable commit together. See PruneUnrestorableRowsAsync. */
			var result = await ExecuteTransactionAsync<int>(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var abilities = petData.Abilities?.ToArray() ?? Array.Empty<int>();
				var characterTableName = dbContext.GetTableName<CharacterEntity>();
				var sql = $@"
					WITH active_character AS (
						SELECT id FROM {characterTableName} WHERE id = {{0}} AND deleted = FALSE
					),
					id_ok AS (
						SELECT CASE
							WHEN {{6}}::bigint <= 0 THEN TRUE
							WHEN EXISTS (SELECT 1 FROM {TableName} WHERE id = {{6}} AND character_id = {{0}}) THEN TRUE
							ELSE FALSE
						END AS ok
					),
					upserted AS (
						INSERT INTO {TableName}
							(character_id, template_id, version, abilities, spawned, time_created, deleted, time_deleted)
						SELECT
							{{0}},
							{{1}},
							{{2}},
							{{3}},
							{{4}},
							{{5}},
							FALSE,
							NULL
						WHERE EXISTS (SELECT 1 FROM active_character)
							AND (SELECT ok FROM id_ok)
						ON CONFLICT (character_id)
						DO UPDATE SET
							template_id = EXCLUDED.template_id,
							abilities = EXCLUDED.abilities,
							spawned = EXCLUDED.spawned,
							deleted = FALSE,
							time_deleted = NULL,
							version = EXCLUDED.version
						WHERE
							EXCLUDED.version > {TableName}.version
						RETURNING 1
					)
					SELECT CASE
						WHEN NOT EXISTS (SELECT 1 FROM active_character) THEN 1
						WHEN NOT (SELECT ok FROM id_ok) THEN 3
						WHEN EXISTS (SELECT 1 FROM upserted) THEN 0
						ELSE 2
					END AS value";

				int written = await ExecuteScalarIntAsync(
					dbContext,
					sql,
					new object[] { petData.CharacterID, petData.TemplateID, petData.Version, abilities, petData.Spawned, now, petData.ID },
					cancellationToken).ConfigureAwait(false);

				// Written, or superseded by a newer pet row: either way the character has one to prune against.
				if (written == 0 || written == 2)
				{
					await PruneUnrestorableRowsAsync(dbContext, new[] { petData.CharacterID }, cancellationToken).ConfigureAwait(false);
				}
				return written;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!result.IsSuccess)
			{
				return DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			switch (result.Data)
			{
				case 0:
					return DatabaseResult.Success();
				case 1:
					return DatabaseResult.Failure(DatabaseErrorCodes.NotFound, "Character not found or deleted.");
				case 2:
					return DatabaseResult.Failure(DatabaseErrorCodes.StaleState, "Pet update rejected due to a stale Version.");
				case 3:
					return DatabaseResult.Failure(DatabaseErrorCodes.NotFound, $"CharacterPet with ID {petData.ID} was not found for CharacterID {petData.CharacterID}.");
				default:
					return DatabaseResult.Failure(DatabaseErrorCodes.DatabaseError, "A database error occurred.", isTransient: true);
			}
		}

		/// <inheritdoc/>
		public Task<DatabaseResult<BulkWriteResult>> PersistAsync(IEnumerable<CharacterPetData> pets, CancellationToken cancellationToken = default)
			=> PersistBatchAsync(pets, null, cancellationToken);

		/// <inheritdoc/>
		public Task<DatabaseResult<BulkWriteResult>> PersistOwnedAsync(IEnumerable<CharacterPetData> pets, IReadOnlyCollection<CharacterSessionLeaseData> claims, CancellationToken cancellationToken = default)
		{
			string? invalid = CharacterWriteGate.ValidateClaims(claims);
			return invalid != null
				? Task.FromResult(DatabaseResult<BulkWriteResult>.Failure(DatabaseErrorCodes.ValidationError, invalid))
				: PersistBatchAsync(pets, claims, cancellationToken);
		}

		/// <summary>
		/// The batch write behind <see cref="PersistAsync(IEnumerable{CharacterPetData}, CancellationToken)"/> and
		/// <see cref="PersistOwnedAsync"/>. Prunes the dead attribute and buff rows of every pet it
		/// writes; see <see cref="PruneUnrestorableRowsAsync"/>.
		/// </summary>
		/// <param name="pets">Rows to write.</param>
		/// <param name="claims">The writer's claims, or null for the ungated write. See <see cref="CharacterWriteGate"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		private async Task<DatabaseResult<BulkWriteResult>> PersistBatchAsync(IEnumerable<CharacterPetData> pets, IReadOnlyCollection<CharacterSessionLeaseData>? claims, CancellationToken cancellationToken)
		{
			var petList = pets?.Where(p => p.CharacterID > 0).ToList();
			if (petList == null || petList.Count == 0)
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Pets collection must not be null or empty.");
			}

			if (petList.Any(p => p.Version <= 0))
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more pets had an invalid Version. Version must be greater than 0.");
			}

			// Separate pets into updates and inserts based on ID
			var petsToUpdate = petList.Where(p => p.ID > 0).ToList();
			var petsToInsert = petList.Where(p => p.ID == 0).ToList();

			// Deduplicate across update and insert groups to prevent the same CharacterID
			// from appearing in both, which would cause a double-update conflict.
			// When a duplicate is found, keep the one with the higher Version.
			if (petsToInsert.Count > 0 && petsToUpdate.Count > 0)
			{
				var insertLookup = petsToInsert.ToDictionary(p => p.CharacterID);
				var updateLookup = petsToUpdate.ToDictionary(p => p.CharacterID);

				var crossKeys = new HashSet<long>(insertLookup.Keys);
				crossKeys.IntersectWith(updateLookup.Keys);

				foreach (var characterId in crossKeys)
				{
					if (updateLookup[characterId].Version >= insertLookup[characterId].Version)
					{
						petsToInsert.Remove(insertLookup[characterId]);
					}
					else
					{
						petsToUpdate.Remove(updateLookup[characterId]);
					}
				}
			}

			// One row per key, the newest version: see BulkBatch.KeepNewest. An upsert cannot touch one
			// row twice, and an UPDATE ... FROM matching one row twice is ambiguous.
			petsToInsert = BulkBatch.KeepNewest(petsToInsert, pet => pet.CharacterID, pet => pet.Version);

			// One row per key, the newest version: see BulkBatch.KeepNewest. An upsert cannot touch one
			// row twice, and an UPDATE ... FROM matching one row twice is ambiguous.
			petsToUpdate = BulkBatch.KeepNewest(petsToUpdate, pet => pet.ID, pet => pet.Version);

			int suppliedRows = petList.Count;

			return await ExecuteTransactionAsync<BulkWriteResult>(async dbContext =>
			{
				/* Both branches contribute. The batch is split by whether a row already has a
				 * primary key, so neither statement alone describes what the caller asked for. */
				var characterIds = petList.Select(p => p.CharacterID).Distinct().ToArray();
				/* The ownership gate, and the per-character existence check it subsumes: the rows of a
				 * missing or deleted character, or — for an owned write — of one whose claim the writer
				 * no longer holds, are left out and reported as Filtered rather than failing every other
				 * character's rows with them. See CharacterWriteGate. */
				CharacterWriteAdmission admission = await CharacterWriteGate.AdmitAsync(dbContext, characterIds, claims, cancellationToken).ConfigureAwait(false);
				int unownedRows = admission.CountUnowned(petsToUpdate, row => row.CharacterID) + admission.CountUnowned(petsToInsert, row => row.CharacterID);
				BulkWriteResult outcome = new BulkWriteResult(suppliedRows, 0, 0, unownedRows);
				var admittedCharacterIds = admission.Writable.ToArray();

				var now = DateTime.UtcNow;

				// Template ids are signed hashes; only 0 means "no template". See PersistAsync.
				var activeUpdates = petsToUpdate
					.Where(p => admission.Admits(p.CharacterID) && p.TemplateID != 0)
					.ToList();
				var activeInserts = petsToInsert
					.Where(p => admission.Admits(p.CharacterID) && p.TemplateID != 0)
					.ToList();

				if (activeUpdates.Count > 0)
				{
					var ids = activeUpdates.Select(p => p.ID).Distinct().ToArray();
					var existingIds = await dbContext.CharacterPets
						.AsNoTracking()
						.Where(p => ids.Contains(p.ID) && admittedCharacterIds.Contains(p.CharacterID))
						.Select(p => p.ID)
						.ToListAsync(cancellationToken)
						.ConfigureAwait(false);
					var existingIdSet = new HashSet<long>(existingIds);
					activeUpdates = activeUpdates.Where(p => existingIdSet.Contains(p.ID)).ToList();
				}

				if (activeUpdates.Count > 0)
				{
					var idArray = activeUpdates.Select(p => p.ID).ToArray();
					var characterIdArray = activeUpdates.Select(p => p.CharacterID).ToArray();
					var templateIdArray = activeUpdates.Select(p => p.TemplateID).ToArray();
					var versionArray = activeUpdates.Select(p => p.Version).ToArray();
					var abilitiesJson = ToJaggedIntArrayJson(activeUpdates
						.Select(p => (p.Abilities ?? new List<int>()).ToArray())
						.ToArray());
					var spawnedArray = activeUpdates.Select(p => p.Spawned).ToArray();

					// abilities is a ragged (non-rectangular) set of per-row integer[] values, which
					// Npgsql/EF Core 5 cannot reliably bind as a native integer[][] parameter. It is sent as
					// jsonb instead and decoded server-side, joined back to the other UNNESTed columns by
					// ordinal position. See BaseService.ToJaggedIntArrayJson for details.
					var sql = $@"
						UPDATE {TableName} AS t
						SET
							character_id = u.character_id,
							template_id = u.template_id,
							abilities = u.abilities,
							spawned = u.spawned,
							deleted = FALSE,
							time_deleted = NULL,
							version = u.version
						FROM (
							SELECT
								ids.id,
								ids.character_id,
								ids.template_id,
								ids.version,
								events.abilities,
								ids.spawned
							FROM UNNEST(
								{{0}}::bigint[],
								{{1}}::bigint[],
								{{2}}::integer[],
								{{3}}::bigint[],
								{{5}}::boolean[]
							) WITH ORDINALITY AS ids(id, character_id, template_id, version, spawned, ord)
							JOIN (
								SELECT
									arr.ord,
									ARRAY(SELECT elem::integer FROM jsonb_array_elements_text(arr.value) AS elem) AS abilities
								FROM jsonb_array_elements({{4}}::jsonb) WITH ORDINALITY AS arr(value, ord)
							) AS events ON events.ord = ids.ord
						) AS u
						WHERE t.id = u.id
							AND u.version > t.version;";

					int appliedUpdates = await ExecuteBulkUpsertAsync(
						dbContext,
						sql,
						activeUpdates.Count,
						new object[] { idArray, characterIdArray, templateIdArray, versionArray, abilitiesJson, spawnedArray },
						"One or more pets were rejected due to a stale Version.",
						cancellationToken,
						BulkVersionConflictPolicy.SkipStaleRows).ConfigureAwait(false);

					outcome += new BulkWriteResult(0, activeUpdates.Count, appliedUpdates);
				}

				if (activeInserts.Count > 0)
				{
					var characterIdArray = activeInserts.Select(p => p.CharacterID).ToArray();
					var templateIdArray = activeInserts.Select(p => p.TemplateID).ToArray();
					var versionArray = activeInserts.Select(p => p.Version).ToArray();
					var abilitiesJson = ToJaggedIntArrayJson(activeInserts
						.Select(p => (p.Abilities ?? new List<int>()).ToArray())
						.ToArray());
					var spawnedArray = activeInserts.Select(p => p.Spawned).ToArray();

					// See the UPDATE branch above: abilities is sent as jsonb and decoded server-side
					// instead of as a native integer[][] parameter.
					var sql = $@"
						INSERT INTO {TableName}
							(character_id, template_id, version, abilities, spawned, time_created, deleted, time_deleted)
						SELECT
							u.character_id,
							u.template_id,
							u.version,
							events.abilities,
							u.spawned,
							{{5}},
							FALSE,
							NULL
						FROM UNNEST(
							{{0}}::bigint[],
							{{1}}::integer[],
							{{2}}::bigint[],
							{{4}}::boolean[]
						) WITH ORDINALITY AS u(character_id, template_id, version, spawned, ord)
						JOIN (
							SELECT
								arr.ord,
								ARRAY(SELECT elem::integer FROM jsonb_array_elements_text(arr.value) AS elem) AS abilities
							FROM jsonb_array_elements({{3}}::jsonb) WITH ORDINALITY AS arr(value, ord)
						) AS events ON events.ord = u.ord
						ON CONFLICT (character_id)
						DO UPDATE SET
							template_id = EXCLUDED.template_id,
							abilities = EXCLUDED.abilities,
							spawned = EXCLUDED.spawned,
							deleted = FALSE,
							time_deleted = NULL,
							version = EXCLUDED.version
						WHERE
							EXCLUDED.version > {TableName}.version;";

					int appliedInserts = await ExecuteBulkUpsertAsync(
						dbContext,
						sql,
						activeInserts.Count,
						new object[] { characterIdArray, templateIdArray, versionArray, abilitiesJson, spawnedArray, now },
						"One or more pets were rejected due to a stale Version.",
						cancellationToken,
						BulkVersionConflictPolicy.SkipStaleRows).ConfigureAwait(false);

					outcome += new BulkWriteResult(0, activeInserts.Count, appliedInserts);
				}

				// After the pet rows, in the same transaction: prune against what is now stored.
				await PruneUnrestorableRowsAsync(dbContext, admittedCharacterIds, cancellationToken).ConfigureAwait(false);

				return outcome;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Deletes every pet attribute and pet buff row of these characters that no longer matches
		/// the version of the pet row stored for them, inside the caller's transaction.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Those rows can never be restored.</b> All three pet tables are stamped with the
		/// character's version on each pass, and the restore (<c>PetSystem.LoadAndSpawnPetAsync</c>)
		/// keeps only the attribute and buff rows whose version equals the pet row's. Every pass
		/// therefore leaves the previous pass's rows behind for any template the current pet no
		/// longer has — a buff that ended, an attribute a previous pet knew — and nothing ever
		/// removed them, so they piled up for the life of the character.
		/// </para>
		/// <para>
		/// <b>Why here, in every pet-row write, and not in a sweep.</b> Deciding "cannot be restored"
		/// needs the stored pet row, and the pet-row write is the one moment that row changes: this
		/// runs in the same transaction, after the upsert, so it compares against exactly what the
		/// transaction leaves behind — the newer row if this write was superseded, which keeps that
		/// row's attributes and buffs and deletes this write's stale ones. The comparison is equality,
		/// not order, so a newer snapshot's rows written ahead of a pet row that has not committed yet
		/// are deleted only if they could not be restored anyway; the retry that writes that pet row
		/// writes its rows again. A sweep would have to make the same comparison, on a timer, over
		/// every character, and would need a home that runs it; the write already has the rows it
		/// needs locked. The pet-row write precedes the attribute and buff writes of the same pass
		/// (<c>CharacterSystem.SavePetsAsync</c>), so a pass never deletes the rows it is about to
		/// write, and at most one pass's worth of rows written late behind a newer pet row survives
		/// until the next pet-row write.
		/// </para>
		/// <para>
		/// Unconditional on ownership, deliberately: it deletes only rows no reader can use, whoever
		/// holds the character. The caller passes only the characters whose pet row it just wrote.
		/// </para>
		/// </remarks>
		/// <param name="dbContext">The pet-row write's context, with its transaction open.</param>
		/// <param name="characterIds">The characters whose pet row the caller just wrote.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		private static async Task PruneUnrestorableRowsAsync(NpgsqlDbContext dbContext, long[] characterIds, CancellationToken cancellationToken)
		{
			if (characterIds == null || characterIds.Length == 0)
			{
				return;
			}

			string pets = dbContext.GetTableName<CharacterPetEntity>();
			foreach (string dependent in new[]
			{
				dbContext.GetTableName<CharacterPetAttributeEntity>(),
				dbContext.GetTableName<CharacterPetBuffEntity>(),
			})
			{
				string sql = $@"
					DELETE FROM {dependent} AS d
					WHERE d.character_id = ANY({{0}}::bigint[])
						AND NOT EXISTS (
							SELECT 1 FROM {pets} AS p
							WHERE p.character_id = d.character_id
								AND p.deleted = FALSE
								AND p.version = d.version)";
				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { characterIds }, cancellationToken).ConfigureAwait(false);
			}
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
					var pet = await getPetQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
					if (pet != null)
					{
						throw new StaleStateException("Pet delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterPetData?>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterPetData?>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync<CharacterPetData?>(async dbContext =>
			{
				var entity = await getPetQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				if (entity == null)
					return null;

				return new CharacterPetData(
					id: entity.ID,
					version: entity.Version,
					characterID: entity.CharacterID,
					templateID: entity.TemplateID,
					abilities: entity.Abilities ?? new List<int>(),
					spawned: entity.Spawned
				);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterPetData?>> FetchSpawnedAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterPetData?>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync<CharacterPetData?>(async dbContext =>
			{
				var entity = await getSpawnedPetQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				if (entity == null)
					return null;

				return new CharacterPetData(
					id: entity.ID,
					version: entity.Version,
					characterID: entity.CharacterID,
					templateID: entity.TemplateID,
					abilities: entity.Abilities ?? new List<int>(),
					spawned: entity.Spawned
				);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}