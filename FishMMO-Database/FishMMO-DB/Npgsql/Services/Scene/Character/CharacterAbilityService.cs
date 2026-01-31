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
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterAbilityEntity>>> getAbilitiesQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterAbilities
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId && !a.Deleted)
					.ToList());

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
		/// Compiled query for retrieving an existing ability by composite key (character ID + template ID).
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, int, CancellationToken, Task<CharacterAbilityEntity?>> getByCharacterAndTemplateQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, int templateId, CancellationToken ct) =>
				context.CharacterAbilities
					.FirstOrDefault(a => a.CharacterID == characterId && a.TemplateID == templateId));
#pragma warning restore CS8619

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
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
				await getCountQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> SaveAbilityAsync(CharacterAbilityData abilityData, CancellationToken cancellationToken = default)
		{
			if (abilityData.CharacterID <= 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			var abilityResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var isCharacterActive = await dbContext.Characters
					.AsNoTracking()
					.AnyAsync(c => c.ID == abilityData.CharacterID && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (!isCharacterActive)
				{
					throw new DatabaseEntityNotFoundException("Character", abilityData.CharacterID.ToString());
				}

				var ability = await getByCharacterAndTemplateQuery(dbContext, abilityData.CharacterID, abilityData.TemplateID, cancellationToken)
					.ConfigureAwait(false);

				if (ability == null)
				{
					ability = new CharacterAbilityEntity
					{
						CharacterID = abilityData.CharacterID,
						TemplateID = abilityData.TemplateID,
						Version = abilityData.Version,
						TimeCreated = DateTime.UtcNow
					};

					await dbContext.CharacterAbilities.AddAsync(ability, cancellationToken).ConfigureAwait(false);
				}

				ValidateVersion(ability, abilityData.Version);
				if (ability.Deleted)
				{
					ability.Deleted = false;
					ability.TimeDeleted = null;
				}

				ability.AbilityEvents = abilityData.AbilityEvents == null
					? new List<int>()
					: new List<int>(abilityData.AbilityEvents);
				ability.Cooldown = abilityData.Cooldown;

				return ability;
			}).ConfigureAwait(false);

			if (!abilityResult.IsSuccess)
			{
				return DatabaseResult<long>.Failure(abilityResult.ErrorCode, abilityResult.ErrorMessage, abilityResult.IsTransient);
			}

			return DatabaseResult<long>.Success(abilityResult.Data.ID);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveAbilitiesAsync(IEnumerable<CharacterAbilityData> abilities, CancellationToken cancellationToken = default)
		{
			if (abilities == null || !abilities.Any())
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Abilities collection must not be null or empty.",
					isTransient: false);
			}

			var list = abilities.ToList();
			var newItems = list.Where(a => a.ID <= 0).ToList();
			var existingItems = list.Where(a => a.ID > 0).ToList();

			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (newItems.Count > 1)
			{
				var dedupedNew = new Dictionary<(long CharacterID, int TemplateID), CharacterAbilityData>();
				foreach (var ability in newItems)
				{
					dedupedNew[(ability.CharacterID, ability.TemplateID)] = ability;
				}
				if (dedupedNew.Count != newItems.Count)
				{
					newItems = dedupedNew.Values.ToList();
				}
			}

			// Avoid ambiguous multi-match UPDATE ... FROM when duplicate IDs are present.
			if (existingItems.Count > 1)
			{
				var dedupedExisting = new Dictionary<long, CharacterAbilityData>();
				foreach (var ability in existingItems)
				{
					dedupedExisting[ability.ID] = ability;
				}
				if (dedupedExisting.Count != existingItems.Count)
				{
					existingItems = dedupedExisting.Values.ToList();
				}
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var allCharacterIds = list.Select(a => a.CharacterID).Distinct().ToArray();
				var activeCharacterIds = await dbContext.Characters
					.AsNoTracking()
					.Where(c => allCharacterIds.Contains(c.ID) && !c.Deleted)
					.Select(c => c.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);

				var activeNewItems = newItems
					.Where(a => activeCharacterIdSet.Contains(a.CharacterID))
					.ToList();
				var activeExistingItems = existingItems
					.Where(a => activeCharacterIdSet.Contains(a.CharacterID))
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
					var abilityEventsArray = activeExistingItems
						.Select(a => (a.AbilityEvents ?? new List<int>()).ToArray())
						.ToArray();
					var cooldownArray = activeExistingItems.Select(a => a.Cooldown).ToArray();

					var sql = $@"
						UPDATE {TableName} AS t
						SET
							character_id = u.character_id,
							template_id = u.template_id,
							ability_events = u.ability_events,
							cooldown = u.cooldown,
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
							{{5}}::real[]
						) AS u(id, character_id, template_id, version, ability_events, cooldown)
						WHERE t.id = u.id
							AND (u.version <= 0 OR u.version > t.version);";

					await ExecuteBulkUpsertAsync(
						dbContext,
						sql,
						activeExistingItems.Count,
						new object[] { idArray, characterIdArray, templateIdArray, versionArray, abilityEventsArray, cooldownArray },
						"One or more abilities were rejected due to a stale Version.",
						cancellationToken).ConfigureAwait(false);
				}

				if (activeNewItems.Count > 0)
				{
					var characterIdArray = activeNewItems.Select(a => a.CharacterID).ToArray();
					var templateIdArray = activeNewItems.Select(a => a.TemplateID).ToArray();
					var versionArray = activeNewItems.Select(a => a.Version).ToArray();
					var abilityEventsArray = activeNewItems
						.Select(a => (a.AbilityEvents ?? new List<int>()).ToArray())
						.ToArray();
					var cooldownArray = activeNewItems.Select(a => a.Cooldown).ToArray();

					var sql = $@"
						INSERT INTO {TableName}
							(character_id, template_id, version, ability_events, cooldown, time_created, deleted, time_deleted)
						SELECT
							u.character_id,
							u.template_id,
							CASE
								WHEN u.version > 0 THEN u.version
								ELSE 0
							END,
							u.ability_events,
							u.cooldown,
							{{5}},
							FALSE,
							NULL
						FROM UNNEST(
							{{0}}::bigint[],
							{{1}}::integer[],
							{{2}}::bigint[],
							{{3}}::integer[][],
							{{4}}::real[]
						) AS u(character_id, template_id, version, ability_events, cooldown)
						ON CONFLICT (character_id, template_id)
						DO UPDATE SET
							ability_events = EXCLUDED.ability_events,
							cooldown = EXCLUDED.cooldown,
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
						activeNewItems.Count,
						new object[] { characterIdArray, templateIdArray, versionArray, abilityEventsArray, cooldownArray, now },
						"One or more abilities were rejected due to a stale Version.",
						cancellationToken).ConfigureAwait(false);
				}
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAbilitiesAsync(long characterId, CancellationToken cancellationToken = default)
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
		public async Task<DatabaseResult> DeleteAbilityAsync(long characterId, long abilityId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || abilityId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID and ability ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}
					WHERE id = {{1}} AND character_id = {{2}} AND deleted = FALSE";
				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { now, abilityId, characterId }, cancellationToken)
					.ConfigureAwait(false);
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterAbilityData>>> GetAbilitiesAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterAbilityData>>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getAbilitiesQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
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