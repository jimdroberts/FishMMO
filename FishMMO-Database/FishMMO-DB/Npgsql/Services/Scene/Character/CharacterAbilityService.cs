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
	/// <summary>
	/// Service for managing character abilities in the database.
	/// Provides async operations for CRUD operations on character ability data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	public sealed class CharacterAbilityService : BaseService<CharacterAbilityEntity>, ICharacterAbilityService
	{
		/// <summary>
		/// Compiled query for retrieving character abilities (hot path for character load).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterAbilityEntity>>> GetAbilitiesQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterAbilities
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId)
					.ToList());

		/// <summary>
		/// Compiled query for counting character abilities.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> GetCountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterAbilities
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId)
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
		public async Task<DatabaseResult<int>> GetCountAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<int>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				return await GetCountQuery(dbContext, characterId, cancellationToken);
			}, "GetCharacterAbilityCount", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> SaveAbilityAsync(CharacterAbilityData abilityData, CancellationToken cancellationToken = default)
		{
			if (abilityData.CharacterID <= 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Use atomic UPSERT with RETURNING for thread safety and proper retry strategy support
				var events = abilityData.AbilityEvents ?? new List<int>();

				var result = await dbContext.CharacterAbilities
					.FromSqlInterpolated($@"
						INSERT INTO {TableName} (character_id, template_id, ability_events, cooldown)
						VALUES ({abilityData.CharacterID}, {abilityData.TemplateID}, {events}, {abilityData.Cooldown})
						ON CONFLICT (character_id, template_id)
						DO UPDATE SET
							ability_events = EXCLUDED.ability_events,
							cooldown = EXCLUDED.cooldown
						RETURNING id, character_id, template_id, ability_events, cooldown")
					.AsNoTracking()
					.FirstOrDefaultAsync(cancellationToken);

				return result?.ID ?? 0;
			}, "SaveCharacterAbility", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveAbilitiesAsync(IEnumerable<CharacterAbilityData> abilities, CancellationToken cancellationToken = default)
		{
			if (abilities == null || !abilities.Any())
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Abilities collection must not be null or empty.");
			}

			var list = abilities.ToList();
			var newItems = list.Where(a => a.ID <= 0).ToList();
			var existingItems = list.Where(a => a.ID > 0).ToList();

			// Wrap both operations in transaction for atomicity
			var transactionResult = await ExecuteInTransactionAsync(async (dbContext, transaction) =>
			{
				// Handle new abilities with atomic INSERT using ON CONFLICT
				if (newItems.Any())
				{
					var newCharacterIds = newItems.Select(a => a.CharacterID).ToArray();
					var newTemplateIds = newItems.Select(a => a.TemplateID).ToArray();
					var newEventArrays = newItems.Select(a => a.AbilityEvents?.ToArray() ?? Array.Empty<int>()).ToArray();
					var newCooldowns = newItems.Select(a => a.Cooldown).ToArray();

					// Atomic UPSERT for new items - uses unique constraint (character_id, template_id)
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {TableName} (character_id, template_id, ability_events, cooldown)
						SELECT * FROM UNNEST(
							{newCharacterIds}::bigint[],
							{newTemplateIds}::int[],
							{newEventArrays}::int[][],
							{newCooldowns}::float4[]
						)
						ON CONFLICT (character_id, template_id)
						DO UPDATE SET
						ability_events = EXCLUDED.ability_events,
						cooldown = EXCLUDED.cooldown",
						cancellationToken);
				}

				// Handle existing abilities with atomic UPDATE by ID
				if (existingItems.Any())
				{
					var ids = existingItems.Select(a => a.ID).ToArray();
					var characterIds = existingItems.Select(a => a.CharacterID).ToArray();
					var templateIds = existingItems.Select(a => a.TemplateID).ToArray();
					var eventArrays = existingItems.Select(a => a.AbilityEvents?.ToArray() ?? Array.Empty<int>()).ToArray();
					var cooldowns = existingItems.Select(a => a.Cooldown).ToArray();

					// Atomic bulk UPDATE by ID - preserves ID-based update semantics
					// Allows changing template_id if needed
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {TableName} AS target
						SET character_id = source.char_id,
							template_id = source.t_id,
							ability_events = source.evs,
							cooldown = source.cd
						FROM UNNEST(
							{ids}::bigint[],
							{characterIds}::bigint[],
							{templateIds}::int[],
							{eventArrays}::int[][],
							{cooldowns}::float4[]
						) AS source(id, char_id, t_id, evs, cd)
						WHERE target.id = source.id",
						cancellationToken);
				}

				return true;
			}, "SaveCharacterAbilities", cancellationToken);

			if (transactionResult.IsSuccess)
			{
				return DatabaseResult.Success();
			}
			else
			{
				return DatabaseResult.Failure(transactionResult.ErrorCode, transactionResult.ErrorMessage, transactionResult.IsTransient);
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAbilitiesAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.");
			}

			var result = await ExecuteSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {characterId}",
				"DeleteCharacterAbilities",
				entityName: "CharacterAbility",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAbilityAsync(long characterId, long abilityId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || abilityId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID and ability ID must be greater than 0.");
			}

			var result = await ExecuteSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {characterId} AND id = {abilityId}",
				"DeleteCharacterAbility",
				entityName: "CharacterAbility",
				entityId: abilityId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterAbilityData>>> GetAbilitiesAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterAbilityData>>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				var entities = await GetAbilitiesQuery(dbContext, characterId, cancellationToken);
				var abilities = entities.Select(a => new CharacterAbilityData(
					id: a.ID,
					characterID: a.CharacterID,
					templateID: a.TemplateID,
					abilityEvents: a.AbilityEvents,
					cooldown: a.Cooldown
				)).ToList();

				return (IReadOnlyList<CharacterAbilityData>)abilities;
			}, "GetCharacterAbilities", cancellationToken);
		}
	}
}