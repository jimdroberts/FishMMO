using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
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
		/// Compiled query for retrieving a tracked pet by ID.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterPetEntity?>> getByIdTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long id, CancellationToken ct) =>
				(CharacterPetEntity?)context.CharacterPets
					.FirstOrDefault(p => p.ID == id));

		/// <summary>
		/// Compiled query for retrieving a tracked pet by character ID.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterPetEntity?>> getByCharacterIdTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				(CharacterPetEntity?)context.CharacterPets
					.FirstOrDefault(p => p.CharacterID == characterId));

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterPetService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterPetService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SavePetAsync(CharacterPetData petData, CancellationToken cancellationToken = default)
		{
			if (petData.CharacterID <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			if (petData.TemplateID <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Template ID must be greater than 0.",
					isTransient: false);
			}

			var saveResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, petData.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", petData.CharacterID.ToString());
				}

				CharacterPetEntity? entity;
				var isNew = false;
				if (petData.ID > 0)
				{
					entity = await getByIdTrackingQuery(dbContext, petData.ID, cancellationToken).ConfigureAwait(false);
					if (entity == null)
					{
						throw new DatabaseEntityNotFoundException("CharacterPet", petData.ID.ToString());
					}
				}
				else
				{
					entity = await getByCharacterIdTrackingQuery(dbContext, petData.CharacterID, cancellationToken).ConfigureAwait(false);
					if (entity == null)
					{
						entity = new CharacterPetEntity();
						await dbContext.CharacterPets.AddAsync(entity, cancellationToken).ConfigureAwait(false);
						isNew = true;
					}
				}

				ValidateVersion(entity, petData.Version);

				if (entity.Deleted)
				{
					entity.Deleted = false;
					entity.TimeDeleted = null;
				}

				entity.CharacterID = petData.CharacterID;
				entity.TemplateID = petData.TemplateID;
				entity.Abilities = petData.Abilities?.ToList() ?? new List<int>();
				entity.Spawned = petData.Spawned;
				if (isNew)
				{
					entity.TimeCreated = DateTime.UtcNow;
				}
			}).ConfigureAwait(false);

			if (saveResult.IsSuccess)
			{
				return DatabaseResult.Success();
			}

			if (saveResult.ErrorCode != "UNIQUE_VIOLATION")
			{
				return DatabaseResult.Failure(saveResult.ErrorCode, saveResult.ErrorMessage, saveResult.IsTransient);
			}

			// Retry as update on unique violations.
			var updateResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, petData.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", petData.CharacterID.ToString());
				}

				CharacterPetEntity? entity = petData.ID > 0
					? await getByIdTrackingQuery(dbContext, petData.ID, cancellationToken).ConfigureAwait(false)
					: await getByCharacterIdTrackingQuery(dbContext, petData.CharacterID, cancellationToken).ConfigureAwait(false);

				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("CharacterPet", $"(CharacterID: {petData.CharacterID}, ID: {petData.ID})");
				}

				ValidateVersion(entity, petData.Version);

				if (entity.Deleted)
				{
					entity.Deleted = false;
					entity.TimeDeleted = null;
				}

				entity.TemplateID = petData.TemplateID;
				entity.Abilities = petData.Abilities?.ToList() ?? new List<int>();
				entity.Spawned = petData.Spawned;
			}).ConfigureAwait(false);

			return updateResult.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(updateResult.ErrorCode, updateResult.ErrorMessage, updateResult.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SavePetsAsync(IEnumerable<CharacterPetData> pets, CancellationToken cancellationToken = default)
		{
			var petList = pets?.Where(p => p.CharacterID > 0).ToList();
			if (petList == null || petList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Pets collection must not be null or empty.",
					isTransient: false);
			}

			// Separate pets into updates and inserts based on ID
			var petsToUpdate = petList.Where(p => p.ID > 0).ToList();
			var petsToInsert = petList.Where(p => p.ID == 0).ToList();

			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (petsToInsert.Count > 1)
			{
				var dedupedInsert = new Dictionary<long, CharacterPetData>();
				foreach (var pet in petsToInsert)
				{
					dedupedInsert[pet.CharacterID] = pet;
				}
				if (dedupedInsert.Count != petsToInsert.Count)
				{
					petsToInsert = dedupedInsert.Values.ToList();
				}
			}

			// Avoid ambiguous multi-match UPDATE ... FROM when duplicate IDs are present.
			if (petsToUpdate.Count > 1)
			{
				var dedupedUpdate = new Dictionary<long, CharacterPetData>();
				foreach (var pet in petsToUpdate)
				{
					dedupedUpdate[pet.ID] = pet;
				}
				if (dedupedUpdate.Count != petsToUpdate.Count)
				{
					petsToUpdate = dedupedUpdate.Values.ToList();
				}
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var characterIds = petList.Select(p => p.CharacterID).Distinct().ToArray();
				var activeCharacterIds = await dbContext.Characters
					.AsNoTracking()
					.Where(c => characterIds.Contains(c.ID) && !c.Deleted)
					.Select(c => c.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);
				var now = DateTime.UtcNow;

				var activeUpdates = petsToUpdate
					.Where(p => activeCharacterIdSet.Contains(p.CharacterID) && p.TemplateID > 0)
					.ToList();
				var activeInserts = petsToInsert
					.Where(p => activeCharacterIdSet.Contains(p.CharacterID) && p.TemplateID > 0)
					.ToList();

				if (activeUpdates.Count > 0)
				{
					var ids = activeUpdates.Select(p => p.ID).Distinct().ToArray();
					var existingIds = await dbContext.CharacterPets
						.AsNoTracking()
						.Where(p => ids.Contains(p.ID) && activeCharacterIdSet.Contains(p.CharacterID))
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
					var abilitiesArray = activeUpdates
						.Select(p => (p.Abilities ?? new List<int>()).ToArray())
						.ToArray();
					var spawnedArray = activeUpdates.Select(p => p.Spawned).ToArray();

					var sql = $@"
						UPDATE {TableName} AS t
						SET
							character_id = u.character_id,
							template_id = u.template_id,
							abilities = u.abilities,
							spawned = u.spawned,
							deleted = FALSE,
							time_deleted = NULL,
							version = CASE
								WHEN u.version > 0 THEN u.version
								ELSE t.version
							END
						FROM UNNEST(
							{{0}}::bigint[],
							{{1}}::bigint[],
							{{2}}::integer[],
							{{3}}::bigint[],
							{{4}}::integer[][],
							{{5}}::boolean[]
						) AS u(id, character_id, template_id, version, abilities, spawned)
						WHERE t.id = u.id
							AND (u.version <= 0 OR u.version > t.version);";

					await ExecuteBulkUpsertAsync(
						dbContext,
						sql,
						activeUpdates.Count,
						new object[] { idArray, characterIdArray, templateIdArray, versionArray, abilitiesArray, spawnedArray },
						"One or more pets were rejected due to a stale Version.",
						cancellationToken).ConfigureAwait(false);
				}

				if (activeInserts.Count > 0)
				{
					var characterIdArray = activeInserts.Select(p => p.CharacterID).ToArray();
					var templateIdArray = activeInserts.Select(p => p.TemplateID).ToArray();
					var versionArray = activeInserts.Select(p => p.Version).ToArray();
					var abilitiesArray = activeInserts
						.Select(p => (p.Abilities ?? new List<int>()).ToArray())
						.ToArray();
					var spawnedArray = activeInserts.Select(p => p.Spawned).ToArray();

					var sql = $@"
						INSERT INTO {TableName}
							(character_id, template_id, version, abilities, spawned, time_created, deleted, time_deleted)
						SELECT
							u.character_id,
							u.template_id,
							CASE
								WHEN u.version > 0 THEN u.version
								ELSE 0
							END,
							u.abilities,
							u.spawned,
							{{5}},
							FALSE,
							NULL
						FROM UNNEST(
							{{0}}::bigint[],
							{{1}}::integer[],
							{{2}}::bigint[],
							{{3}}::integer[][],
							{{4}}::boolean[]
						) AS u(character_id, template_id, version, abilities, spawned)
						ON CONFLICT (character_id)
						DO UPDATE SET
							template_id = EXCLUDED.template_id,
							abilities = EXCLUDED.abilities,
							spawned = EXCLUDED.spawned,
							deleted = FALSE,
							time_deleted = NULL,
							version = CASE
								WHEN EXCLUDED.version > 0 THEN EXCLUDED.version
								ELSE {TableName}.version
							END
						WHERE EXCLUDED.version <= 0 OR EXCLUDED.version > {TableName}.version;";

					await ExecuteBulkUpsertAsync(
						dbContext,
						sql,
						activeInserts.Count,
						new object[] { characterIdArray, templateIdArray, versionArray, abilitiesArray, spawnedArray, now },
						"One or more pets were rejected due to a stale Version.",
						cancellationToken).ConfigureAwait(false);
				}
			}).ConfigureAwait(false);
		}

		public async Task<DatabaseResult> DeletePetAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}
					WHERE character_id = {{1}} AND deleted = FALSE";
				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { now, characterId }, cancellationToken)
					.ConfigureAwait(false);
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterPetData?>> GetPetAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterPetData?>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
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
		public async Task<DatabaseResult<CharacterPetData?>> GetSpawnedPetAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterPetData?>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
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