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
					.Where(a => a.CharacterID == characterId)
					.ToList());

		/// <summary>
		/// Compiled query for counting character abilities.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> getCountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterAbilities
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId)
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

			return await ExecuteMirrorAsync(async dbContext =>
				await getCountQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
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

			var abilityResult = await ExecuteMirrorAsync(async dbContext =>
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

			return await ExecuteMirrorAsync(async dbContext =>
			{
				var previousAutoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
				dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
				try
				{
					var allCharacterIds = list.Select(a => a.CharacterID).Distinct().ToArray();
					var activeCharacterIds = await dbContext.Characters
						.AsNoTracking()
						.Where(c => allCharacterIds.Contains(c.ID) && !c.Deleted)
						.Select(c => c.ID)
						.ToListAsync(cancellationToken)
						.ConfigureAwait(false);
					var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);

					if (newItems.Any())
					{
						var templateIds = newItems.Select(a => a.TemplateID).Distinct().ToArray();
						var existingForCompositeKeys = await dbContext.CharacterAbilities
							.Where(a => activeCharacterIdSet.Contains(a.CharacterID) && templateIds.Contains(a.TemplateID))
							.ToListAsync(cancellationToken)
							.ConfigureAwait(false);

						var existingByKey = new Dictionary<(long CharacterID, int TemplateID), CharacterAbilityEntity>();
						foreach (var existing in existingForCompositeKeys)
						{
							existingByKey[(existing.CharacterID, existing.TemplateID)] = existing;
						}

						foreach (var ability in newItems)
						{
							if (!activeCharacterIdSet.Contains(ability.CharacterID))	continue;

							var key = (ability.CharacterID, ability.TemplateID);
							if (!existingByKey.TryGetValue(key, out var entity))
							{
								entity = new CharacterAbilityEntity
								{
									CharacterID = ability.CharacterID,
									TemplateID = ability.TemplateID,
									Version = ability.Version,
									TimeCreated = DateTime.UtcNow
								};
								await dbContext.CharacterAbilities.AddAsync(entity, cancellationToken).ConfigureAwait(false);
								existingByKey[key] = entity;
							}

							ValidateVersion(entity, ability.Version);

							entity.AbilityEvents = ability.AbilityEvents == null ? new List<int>() : new List<int>(ability.AbilityEvents);
							entity.Cooldown = ability.Cooldown;
						}
					}

					if (existingItems.Any())
					{
						var ids = existingItems.Select(a => a.ID).Distinct().ToArray();
						var entities = await dbContext.CharacterAbilities
							.Where(a => ids.Contains(a.ID))
							.ToListAsync(cancellationToken)
							.ConfigureAwait(false);
						var entitiesById = entities.ToDictionary(a => a.ID);

						foreach (var ability in existingItems)
						{
							if (!activeCharacterIdSet.Contains(ability.CharacterID)) continue;
							if (!entitiesById.TryGetValue(ability.ID, out var entity)) continue;

							ValidateVersion(entity, ability.Version);

							entity.CharacterID = ability.CharacterID;
							entity.TemplateID = ability.TemplateID;
							entity.AbilityEvents = ability.AbilityEvents == null ? new List<int>() : new List<int>(ability.AbilityEvents);
							entity.Cooldown = ability.Cooldown;
						}
					}
				}
				finally
				{
					dbContext.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetectChanges;
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

			return await ExecuteMirrorAsync(async dbContext =>
			{
				var abilityIds = await dbContext.CharacterAbilities
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId)
					.Select(a => a.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				foreach (var abilityId in abilityIds)
				{
					var entity = new CharacterAbilityEntity { ID = abilityId };
					dbContext.CharacterAbilities.Remove(entity);
				}
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

			return await ExecuteMirrorAsync(async dbContext =>
			{
				var ability = await dbContext.CharacterAbilities
					.FirstOrDefaultAsync(a => a.ID == abilityId && a.CharacterID == characterId, cancellationToken)
					.ConfigureAwait(false);
				if (ability == null)
				{
					return;
				}

				dbContext.CharacterAbilities.Remove(ability);
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

			return await ExecuteMirrorAsync(async dbContext =>
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
			}).ConfigureAwait(false);
		}
	}
}