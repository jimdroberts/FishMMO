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
		private static readonly Func<NpgsqlDbContext, long, IAsyncEnumerable<CharacterKnownAbilityEntity>> getKnownAbilitiesQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId) =>
				context.CharacterKnownAbilities
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId && !a.Deleted));

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterKnownAbilityService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterKnownAbilityService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(long characterId, int templateId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			if (templateId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Template ID must be greater than 0.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid Version. Version must be greater than 0.");
			}

			var insertResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString(), "Character not found or deleted.");
				}

				var now = DateTime.UtcNow;
				var sql = $@"INSERT INTO {TableName}
					(character_id, template_id, version, time_created, deleted, time_deleted)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, FALSE, NULL)
					ON CONFLICT (character_id, template_id)
					DO UPDATE SET
						deleted = FALSE,
						time_deleted = NULL,
						version = EXCLUDED.version
					WHERE
						EXCLUDED.version > {TableName}.version";

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { characterId, templateId, incomingVersion, now },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var existing = await dbContext.CharacterKnownAbilities
						.AsNoTracking()
						.Where(a => a.CharacterID == characterId && a.TemplateID == templateId)
						.Select(a => new { a.Version })
						.FirstOrDefaultAsync(cancellationToken)
						.ConfigureAwait(false);

					if (existing != null)
					{
						if (existing.Version == incomingVersion)
						{
							throw new DuplicateReplayException("Known ability persist rejected because the Version was a duplicate replay.");
						}

						throw new StaleStateException("Known ability persist rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return insertResult;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(IEnumerable<CharacterKnownAbilityData> knownAbilities, CancellationToken cancellationToken = default)
		{
			var abilityList = knownAbilities?.ToList();
			if (abilityList == null || abilityList.Count == 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Abilities collection must not be null or empty.");
			}

			if (abilityList.Any(a => a.Version <= 0))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more known abilities had an invalid Version. Version must be greater than 0.");
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
				var characterIds = abilityList.Select(a => a.CharacterID).Distinct().ToArray();
				var activeCharacterIds = await dbContext.Characters
					.AsNoTracking()
					.Where(c => characterIds.Contains(c.ID) && !c.Deleted)
					.Select(c => c.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);

				if (activeCharacterIdSet.Count != characterIds.Length)
				{
					var missingCharacterId = characterIds.First(id => !activeCharacterIdSet.Contains(id));
					throw new DatabaseEntityNotFoundException("Character", missingCharacterId.ToString(), "Character not found or deleted.");
				}

				var activeAbilities = abilityList
					.Where(a => a.TemplateID > 0 && activeCharacterIdSet.Contains(a.CharacterID))
					.ToList();
				if (activeAbilities.Count == 0)
				{
					return;
				}

				var now = DateTime.UtcNow;
				var characterIdArray = activeAbilities.Select(a => a.CharacterID).ToArray();
				var templateIdArray = activeAbilities.Select(a => a.TemplateID).ToArray();
				var versionArray = activeAbilities.Select(a => a.Version).ToArray();

				var sql = $@"
					INSERT INTO {TableName}
						(character_id, template_id, version, time_created, deleted, time_deleted)
					SELECT
						u.character_id,
						u.template_id,
						u.version,
						{{3}},
						FALSE,
						NULL
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::integer[],
						{{2}}::bigint[]
					) AS u(character_id, template_id, version)
					ON CONFLICT (character_id, template_id)
					DO UPDATE SET
						deleted = FALSE,
						time_deleted = NULL,
						version = EXCLUDED.version
					WHERE
						EXCLUDED.version > {TableName}.version;";

				await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeAbilities.Count,
					new object[] { characterIdArray, templateIdArray, versionArray, now },
					"One or more known abilities were rejected due to a stale Version.",
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return saveResult;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, int templateId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			if (templateId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Template ID must be greater than 0.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid Version. Version must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}, version = {{1}}
					WHERE character_id = {{2}} AND template_id = {{3}} AND deleted = FALSE AND version < {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, incomingVersion, characterId, templateId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var stillActive = await dbContext.CharacterKnownAbilities
						.AsNoTracking()
						.AnyAsync(a => a.CharacterID == characterId && a.TemplateID == templateId && !a.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (stillActive)
					{
						throw new StaleStateException("Known ability delete rejected due to a stale Version.");
					}
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
					"Invalid Version. Version must be greater than 0.");
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
					var anyActive = await dbContext.CharacterKnownAbilities
						.AsNoTracking()
						.AnyAsync(a => a.CharacterID == characterId && !a.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Known ability delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getKnownAbilitiesQuery(dbContext, characterId).MaterializeAsync(cancellationToken).ConfigureAwait(false);
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