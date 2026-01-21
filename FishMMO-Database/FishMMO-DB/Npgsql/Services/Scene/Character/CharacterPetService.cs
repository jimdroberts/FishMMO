using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	public sealed class CharacterPetService : BaseService<CharacterPetEntity>, ICharacterPetService
	{
		/// <summary>
		/// Compiled query for retrieving character pet.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterPetEntity?>> GetPetQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterPets
					.AsNoTracking()
					.FirstOrDefault(p => p.CharacterID == characterId));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving spawned character pet.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterPetEntity?>> GetSpawnedPetQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterPets
					.AsNoTracking()
					.FirstOrDefault(p => p.CharacterID == characterId && p.Spawned));
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
		public async Task<DatabaseResult> SavePetAsync(CharacterPetData petData, CancellationToken cancellationToken = default)
		{
			if (petData.CharacterID == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var result = await ExecuteSqlAsync(
				$@"UPDATE {TableName} 
				   SET character_id = {petData.CharacterID},
				       template_id = {petData.TemplateID},
				       abilities = {petData.Abilities},
				       spawned = {petData.Spawned}
				   WHERE id = {petData.ID}",
				"SavePet",
				entityName: "CharacterPet",
				entityId: petData.ID,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SavePetsAsync(IEnumerable<CharacterPetData> pets, CancellationToken cancellationToken = default)
		{
			var petList = pets?.Where(p => p.CharacterID > 0).ToList();
			if (petList == null || petList.Count == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Empty or null pets collection");
			}

			// Separate pets into updates and inserts based on ID
			var petsToUpdate = petList.Where(p => p.ID > 0).ToList();
			var petsToInsert = petList.Where(p => p.ID == 0).ToList();

			// Wrap both operations in transaction for atomicity
			var transactionResult = await ExecuteSqlAsync(async (dbContext, transaction) =>
			{
				// Handle existing pets with atomic UPDATE by ID
				if (petsToUpdate.Count > 0)
				{
					var updateIds = petsToUpdate.Select(p => p.ID).ToArray();
					var updateCharacterIds = petsToUpdate.Select(p => p.CharacterID).ToArray();
					var updateTemplateIds = petsToUpdate.Select(p => p.TemplateID).ToArray();
					var updateAbilities = petsToUpdate.Select(p => p.Abilities.ToArray()).ToArray();
					var updateSpawned = petsToUpdate.Select(p => p.Spawned).ToArray();

					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {TableName} AS t SET
						character_id = u.character_id,
						template_id = u.template_id,
						abilities = u.abilities,
						spawned = u.spawned
					FROM (SELECT * FROM UNNEST(
						{updateIds}::bigint[],
						{updateCharacterIds}::bigint[],
						{updateTemplateIds}::int[],
						{updateAbilities}::int[][],
						{updateSpawned}::boolean[]
					) AS u(id, character_id, template_id, abilities, spawned)) AS u
					WHERE t.id = u.id",
						cancellationToken);
				}

				// Handle new pets with atomic UPSERT using character_id unique constraint
				if (petsToInsert.Count > 0)
				{
					var insertCharacterIds = petsToInsert.Select(p => p.CharacterID).ToArray();
					var insertTemplateIds = petsToInsert.Select(p => p.TemplateID).ToArray();
					var insertAbilities = petsToInsert.Select(p => p.Abilities.ToArray()).ToArray();
					var insertSpawned = petsToInsert.Select(p => p.Spawned).ToArray();

					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {TableName} (character_id, template_id, abilities, spawned)
					SELECT * FROM UNNEST(
						{insertCharacterIds}::bigint[],
						{insertTemplateIds}::int[],
						{insertAbilities}::int[][],
						{insertSpawned}::boolean[]
					)
					ON CONFLICT (character_id)
					DO UPDATE SET
						template_id = EXCLUDED.template_id,
						abilities = EXCLUDED.abilities,
						spawned = EXCLUDED.spawned",
						cancellationToken);
				}

				return true;
			}, "SavePets", cancellationToken);

			if (transactionResult.IsSuccess)
			{
				return DatabaseResult.Success();
			}
			else
			{
				return DatabaseResult.Failure(transactionResult.ErrorCode, transactionResult.ErrorMessage, transactionResult.IsTransient);
			}
		}

		public async Task<DatabaseResult> DeletePetAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var result = await ExecuteSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {characterId}",
				"DeletePet",
				entityName: "CharacterPet",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterPetData?>> GetPetAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<CharacterPetData?>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteSqlAsync<CharacterPetData?>(async dbContext =>
			{
				var entity = await GetPetQuery(dbContext, characterId, cancellationToken);
				if (entity == null)
					return null;

				return new CharacterPetData(
					id: entity.ID,
					characterID: entity.CharacterID,
					templateID: entity.TemplateID,
					abilities: entity.Abilities,
					spawned: entity.Spawned
				);
			}, "GetPet", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterPetData?>> GetSpawnedPetAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<CharacterPetData?>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteSqlAsync<CharacterPetData?>(async dbContext =>
			{
				var entity = await GetSpawnedPetQuery(dbContext, characterId, cancellationToken);
				if (entity == null)
					return null;

				return new CharacterPetData(
					id: entity.ID,
					characterID: entity.CharacterID,
					templateID: entity.TemplateID,
					abilities: entity.Abilities,
					spawned: entity.Spawned
				);
			}, "GetSpawnedPet", cancellationToken);
		}
	}
}