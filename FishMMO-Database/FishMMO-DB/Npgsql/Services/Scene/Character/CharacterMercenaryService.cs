using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Persists a character's hired mercenary (<c>character_mercenaries</c>, the Mercenaries module): one row, replaced on
	/// every save, deleted when the mercenary dies.
	/// </summary>
	public sealed class CharacterMercenaryService : BaseService<CharacterMercenaryEntity>, ICharacterMercenaryService
	{
		/// <summary>The most buffs or resources one row keeps.</summary>
		public const int MaxEntries = 64;

		public CharacterMercenaryService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveAsync(CharacterMercenaryData mercenary, CancellationToken cancellationToken = default)
		{
			if (mercenary == null || mercenary.CharacterID <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than 0.");
			}
			if (mercenary.KitID == 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "A mercenary needs a kit.");
			}
			if (string.IsNullOrWhiteSpace(mercenary.Name) || mercenary.Name.Length > 32)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "A mercenary's name must be 1 to 32 characters.");
			}

			int[] resourceIDs = mercenary.ResourceTemplateIDs ?? Array.Empty<int>();
			float[] resourceValues = mercenary.ResourceValues ?? Array.Empty<float>();
			int[] buffIDs = mercenary.BuffTemplateIDs ?? Array.Empty<int>();
			float[] buffRemaining = mercenary.BuffRemaining ?? Array.Empty<float>();
			float[] buffTickTime = mercenary.BuffTickTime ?? Array.Empty<float>();
			int[] buffStacks = mercenary.BuffStacks ?? Array.Empty<int>();
			int[] buffTickCount = mercenary.BuffTickCount ?? Array.Empty<int>();
			if (resourceIDs.Length != resourceValues.Length ||
				buffIDs.Length != buffRemaining.Length || buffIDs.Length != buffTickTime.Length ||
				buffIDs.Length != buffStacks.Length || buffIDs.Length != buffTickCount.Length)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "A mercenary's resource and buff arrays must be the same lengths.");
			}
			if (resourceIDs.Length > MaxEntries || buffIDs.Length > MaxEntries)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, $"A mercenary keeps at most {MaxEntries} resources and buffs.");
			}
			for (int i = 0; i < resourceValues.Length; ++i)
			{
				if (!IsFinite(resourceValues[i]))
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "A mercenary's resource values must be finite.");
				}
			}
			for (int i = 0; i < buffRemaining.Length; ++i)
			{
				if (!IsFinite(buffRemaining[i]) || !IsFinite(buffTickTime[i]))
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "A mercenary's buff times must be finite.");
				}
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"
					INSERT INTO {TableName}
						(character_id, kit_id, name, resting, stance, appearance_seed, name_seed, name_gender, name_biome_id,
						 name_variant, resource_template_ids, resource_values, buff_template_ids, buff_remaining, buff_tick_time,
						 buff_stacks, buff_tick_count, time_hired, time_updated, race_template_id)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, {{7}}, {{8}}, {{9}}, {{10}}, {{11}}, {{12}}, {{13}}, {{14}},
						{{15}}, {{16}}, {{17}}, {{17}}, {{18}})
					ON CONFLICT (character_id)
					DO UPDATE SET
						-- A different kit is a new hire: it is hired now. The same kit keeps its hiring time.
						time_hired = CASE WHEN {TableName}.kit_id = EXCLUDED.kit_id AND {TableName}.name = EXCLUDED.name THEN {TableName}.time_hired ELSE EXCLUDED.time_hired END,
						kit_id = EXCLUDED.kit_id,
						name = EXCLUDED.name,
						resting = EXCLUDED.resting,
						stance = EXCLUDED.stance,
						appearance_seed = EXCLUDED.appearance_seed,
						name_seed = EXCLUDED.name_seed,
						name_gender = EXCLUDED.name_gender,
						name_biome_id = EXCLUDED.name_biome_id,
						name_variant = EXCLUDED.name_variant,
						race_template_id = EXCLUDED.race_template_id,
						resource_template_ids = EXCLUDED.resource_template_ids,
						resource_values = EXCLUDED.resource_values,
						buff_template_ids = EXCLUDED.buff_template_ids,
						buff_remaining = EXCLUDED.buff_remaining,
						buff_tick_time = EXCLUDED.buff_tick_time,
						buff_stacks = EXCLUDED.buff_stacks,
						buff_tick_count = EXCLUDED.buff_tick_count,
						time_updated = EXCLUDED.time_updated;";

				await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[]
					{
						mercenary.CharacterID, mercenary.KitID, mercenary.Name, mercenary.Resting, (short)mercenary.Stance,
						mercenary.AppearanceSeed, mercenary.NameSeed, (short)mercenary.NameGender, mercenary.NameBiomeID,
						(short)mercenary.NameVariant, resourceIDs, resourceValues, buffIDs, buffRemaining, buffTickTime,
						buffStacks, buffTickCount, DateTime.UtcNow, mercenary.RaceTemplateID,
					}, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		/// <remarks>Null when the character has no mercenary.</remarks>
		public async Task<DatabaseResult<CharacterMercenaryData>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterMercenaryData>.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				CharacterMercenaryEntity entity = await dbContext.Set<CharacterMercenaryEntity>()
					.AsNoTracking()
					.FirstOrDefaultAsync(m => m.CharacterID == characterId, cancellationToken)
					.ConfigureAwait(false);
				if (entity == null)
				{
					return null;
				}
				return new CharacterMercenaryData()
				{
					CharacterID = entity.CharacterID,
					KitID = entity.KitID,
					Name = entity.Name,
					Resting = entity.Resting,
					Stance = entity.Stance,
					AppearanceSeed = entity.AppearanceSeed,
					NameSeed = entity.NameSeed,
					NameGender = entity.NameGender,
					NameBiomeID = entity.NameBiomeID,
					NameVariant = entity.NameVariant,
					RaceTemplateID = entity.RaceTemplateID,
					ResourceTemplateIDs = entity.ResourceTemplateIDs ?? Array.Empty<int>(),
					ResourceValues = entity.ResourceValues ?? Array.Empty<float>(),
					BuffTemplateIDs = entity.BuffTemplateIDs ?? Array.Empty<int>(),
					BuffRemaining = entity.BuffRemaining ?? Array.Empty<float>(),
					BuffTickTime = entity.BuffTickTime ?? Array.Empty<float>(),
					BuffStacks = entity.BuffStacks ?? Array.Empty<int>(),
					BuffTickCount = entity.BuffTickCount ?? Array.Empty<int>(),
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		/// <remarks>The mercenary's death (it is released), and character deletion.</remarks>
		public async Task<DatabaseResult> DeleteAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE character_id = {{0}}";
				await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { characterId }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
