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
		public async Task<DatabaseResult<long>> PersistAsync(CharacterAbilityData abilityData, CancellationToken cancellationToken = default)
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
				var isCharacterActive = await dbContext.Characters
					.AsNoTracking()
					.AnyAsync(c => c.ID == abilityData.CharacterID && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (!isCharacterActive)
				{
					throw new DatabaseEntityNotFoundException("Character", abilityData.CharacterID.ToString());
				}

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

				var idRow = await dbContext.Set<SqlLongValue>()
					.FromSqlRaw(sql, abilityData.CharacterID, abilityData.TemplateID, abilityData.Version, abilityEvents, abilityData.Cooldown, now)
					.AsNoTracking()
					.SingleAsync(cancellationToken)
					.ConfigureAwait(false);

				if (idRow.Value <= 0)
				{
					throw new StaleStateException("Ability persist rejected due to a stale Version.");
				}

				return idRow.Value;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(IEnumerable<CharacterAbilityData> abilities, CancellationToken cancellationToken = default)
		{
			if (abilities == null || !abilities.Any())
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Abilities collection must not be null or empty.");
			}

			var list = abilities.ToList();
			if (list.Any(a => a.Version <= 0))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more abilities had an invalid Version. Version must be greater than 0.");
			}
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

				if (activeCharacterIdSet.Count != allCharacterIds.Length)
				{
					var missingCharacterId = allCharacterIds.First(id => !activeCharacterIdSet.Contains(id));
					throw new DatabaseEntityNotFoundException("Character", missingCharacterId.ToString(), "Character not found or deleted.");
				}

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
							version = u.version
						FROM UNNEST(
							{{0}}::bigint[],
							{{1}}::bigint[],
							{{2}}::integer[],
							{{3}}::bigint[],
							{{4}}::integer[][],
							{{5}}::real[]
						) AS u(id, character_id, template_id, version, ability_events, cooldown)
						WHERE t.id = u.id
							AND u.version > t.version;";

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
							u.version,
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
							version = EXCLUDED.version
						WHERE
							EXCLUDED.version > {TableName}.version;";

					await ExecuteBulkUpsertAsync(
						dbContext,
						sql,
						activeNewItems.Count,
						new object[] { characterIdArray, templateIdArray, versionArray, abilityEventsArray, cooldownArray, now },
						"One or more abilities were rejected due to a stale Version.",
						cancellationToken).ConfigureAwait(false);
				}
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