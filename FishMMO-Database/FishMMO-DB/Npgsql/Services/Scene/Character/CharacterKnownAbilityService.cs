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
	/// Service for managing character known abilities in the database.
	/// Provides async operations for CRUD operations on character known ability data.
	/// Uses the BaseService execution strategy for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	public sealed class CharacterKnownAbilityService : BaseService<CharacterKnownAbilityEntity>, ICharacterKnownAbilityService
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
		/// Compiled query for retrieving character known abilities.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterKnownAbilityEntity>>> getKnownAbilitiesQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterKnownAbilities
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId)
					.ToList());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterKnownAbilityService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterKnownAbilityService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveKnownAbilityAsync(long characterId, int templateId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			if (templateId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Template ID must be greater than 0.",
					isTransient: false);
			}

			var insertResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					// Preserve previous behavior: no-op if character is missing/deleted.
					return;
				}

				var exists = await dbContext.CharacterKnownAbilities
					.AsNoTracking()
					.AnyAsync(a => a.CharacterID == characterId && a.TemplateID == templateId, cancellationToken)
					.ConfigureAwait(false);
				if (exists)
				{
					return;
				}

				var entity = new CharacterKnownAbilityEntity
				{
					CharacterID = characterId,
					TemplateID = templateId,
					TimeCreated = DateTime.UtcNow
				};
				await dbContext.CharacterKnownAbilities.AddAsync(entity, cancellationToken).ConfigureAwait(false);
			}).ConfigureAwait(false);

			if (insertResult.IsSuccess || insertResult.ErrorCode == "UNIQUE_VIOLATION")
			{
				return DatabaseResult.Success();
			}

			return DatabaseResult.Failure(insertResult.ErrorCode, insertResult.ErrorMessage, insertResult.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveKnownAbilitiesAsync(IEnumerable<CharacterKnownAbilityData> knownAbilities, CancellationToken cancellationToken = default)
		{
			var abilityList = knownAbilities?.ToList();
			if (abilityList == null || abilityList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Abilities collection must not be null or empty.",
					isTransient: false);
			}

			// Prevent duplicates within the same batch from causing tracking issues.
			if (abilityList.Count > 1)
			{
				var deduped = new Dictionary<(long CharacterID, int TemplateID), CharacterKnownAbilityData>();
				foreach (var ability in abilityList)
				{
					deduped[(ability.CharacterID, ability.TemplateID)] = ability;
				}
				abilityList = deduped.Values.ToList();
			}

			var saveResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var previousAutoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
				try
				{
					dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

					var characterIds = abilityList.Select(a => a.CharacterID).Distinct().ToArray();
					var activeCharacterIds = await dbContext.Characters
						.AsNoTracking()
						.Where(c => characterIds.Contains(c.ID) && !c.Deleted)
						.Select(c => c.ID)
						.ToListAsync(cancellationToken)
						.ConfigureAwait(false);
					var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);

					var templateIds = abilityList.Select(a => a.TemplateID).Distinct().ToArray();
					var existingKeys = await dbContext.CharacterKnownAbilities
						.AsNoTracking()
						.Where(a => activeCharacterIdSet.Contains(a.CharacterID) && templateIds.Contains(a.TemplateID))
						.Select(a => new { a.CharacterID, a.TemplateID })
						.ToListAsync(cancellationToken)
						.ConfigureAwait(false);

					var existingKeySet = new HashSet<(long CharacterID, int TemplateID)>(existingKeys.Select(k => (k.CharacterID, k.TemplateID)));

					foreach (var ability in abilityList)
					{
						if (!activeCharacterIdSet.Contains(ability.CharacterID)) continue;
						if (ability.TemplateID <= 0) continue;

						var key = (ability.CharacterID, ability.TemplateID);
						if (existingKeySet.Contains(key)) continue;

						var entity = new CharacterKnownAbilityEntity
						{
							CharacterID = ability.CharacterID,
							TemplateID = ability.TemplateID,
							Version = ability.Version,
							TimeCreated = DateTime.UtcNow
						};
						await dbContext.CharacterKnownAbilities.AddAsync(entity, cancellationToken).ConfigureAwait(false);
						existingKeySet.Add(key);
					}
				}
				finally
				{
					dbContext.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetectChanges;
				}
			}).ConfigureAwait(false);

			if (saveResult.IsSuccess || saveResult.ErrorCode == "UNIQUE_VIOLATION")
			{
				return DatabaseResult.Success();
			}

			return DatabaseResult.Failure(saveResult.ErrorCode, saveResult.ErrorMessage, saveResult.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteKnownAbilityAsync(long characterId, int templateId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			if (templateId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Template ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var abilityId = await dbContext.CharacterKnownAbilities
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId && a.TemplateID == templateId)
					.Select(a => a.ID)
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				if (abilityId <= 0)
				{
					return;
				}

				var entity = new CharacterKnownAbilityEntity { ID = abilityId };
				dbContext.CharacterKnownAbilities.Remove(entity);
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAllKnownAbilitiesAsync(long characterId, CancellationToken cancellationToken = default)
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
				var abilityIds = await dbContext.CharacterKnownAbilities
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId)
					.Select(a => a.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				foreach (var abilityId in abilityIds)
				{
					var entity = new CharacterKnownAbilityEntity { ID = abilityId };
					dbContext.CharacterKnownAbilities.Remove(entity);
				}
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>> GetKnownAbilitiesAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getKnownAbilitiesQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				var abilities = entities.Select(a => new CharacterKnownAbilityData(
					id: a.ID,
					version: a.Version,
					characterID: a.CharacterID,
					templateID: a.TemplateID
				)).ToList();

				return (IReadOnlyList<CharacterKnownAbilityData>)abilities;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}