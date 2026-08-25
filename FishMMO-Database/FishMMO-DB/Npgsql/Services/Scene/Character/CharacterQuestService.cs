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
	/// Service for managing character quests in the database.
	/// Provides async operations for CRUD operations on character quest data.
	/// Uses the BaseService execution strategy for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	public sealed class CharacterQuestService : BaseService<CharacterQuestEntity>, ICharacterQuestService
	{
		/// <summary>
		/// Compiled query for retrieving character quests.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, IAsyncEnumerable<CharacterQuestEntity>> getQuestsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId) =>
				context.CharacterQuests
					.AsNoTracking()
					.Where(q => q.CharacterID == characterId && !q.Deleted));

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterQuestService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterQuestService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<BulkWriteResult>> PersistAsync(IEnumerable<CharacterQuestData> quests, CancellationToken cancellationToken = default)
		{
			if (quests == null || !quests.Any())
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Quests collection must not be null or empty.");
			}

			var questList = quests.ToList();
			if (questList.Any(q => q.Version <= 0))
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more quests had an invalid Version. Version must be greater than 0.");
			}

			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (questList.Count > 1)
			{
				var deduped = new Dictionary<(long CharacterID, int TemplateID), CharacterQuestData>();
				foreach (var quest in questList)
				{
					deduped[(quest.CharacterID, quest.TemplateID)] = quest;
				}

				if (deduped.Count != questList.Count)
				{
					questList = deduped.Values.ToList();
				}
			}

			int suppliedRows = questList.Count;

			return await ExecuteTransactionAsync<BulkWriteResult>(async dbContext =>
			{
				var characterIds = questList.Select(q => q.CharacterID).Distinct().ToArray();
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

				var activeQuests = questList.Where(q => activeCharacterIdSet.Contains(q.CharacterID)).ToList();
				if (activeQuests.Count == 0)
				{
					return new BulkWriteResult(suppliedRows, 0, 0);
				}

				var now = DateTime.UtcNow;
				var characterIdArray = activeQuests.Select(q => q.CharacterID).ToArray();
				var templateIdArray = activeQuests.Select(q => q.TemplateID).ToArray();
				var versionArray = activeQuests.Select(q => q.Version).ToArray();
				var statusArray = activeQuests.Select(q => (short)q.Status).ToArray();
				var objectiveValuesArray = activeQuests.Select(q => q.ObjectiveValues ?? "").ToArray();

				var sql = $@"
					INSERT INTO {TableName}
						(character_id, template_id, version, status, objective_values, time_created, deleted, time_deleted)
					SELECT
						u.character_id,
						u.template_id,
						u.version,
						u.status,
						u.objective_values,
						{{5}},
						FALSE,
						NULL
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::integer[],
						{{2}}::bigint[],
						{{3}}::smallint[],
						{{4}}::text[]
					) AS u(character_id, template_id, version, status, objective_values)
					ON CONFLICT (character_id, template_id)
					DO UPDATE SET
						status = EXCLUDED.status,
						objective_values = EXCLUDED.objective_values,
						deleted = FALSE,
						time_deleted = NULL,
						version = EXCLUDED.version
					WHERE
						EXCLUDED.version > {TableName}.version;";

				int appliedRows = await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeQuests.Count,
					new object[] { characterIdArray, templateIdArray, versionArray, statusArray, objectiveValuesArray, now },
					"One or more quests were rejected due to a stale Version.",
					cancellationToken,
					BulkVersionConflictPolicy.SkipStaleRows).ConfigureAwait(false);

				return new BulkWriteResult(suppliedRows, activeQuests.Count, appliedRows);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteQuestAsync(long characterId, int templateId, long incomingVersion, CancellationToken cancellationToken = default)
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
					WHERE character_id = {{2}} AND template_id = {{3}} AND deleted = FALSE AND version < {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, incomingVersion, characterId, templateId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var anyActive = await dbContext.CharacterQuests
						.AsNoTracking()
						.AnyAsync(q => q.CharacterID == characterId && q.TemplateID == templateId && !q.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Quest delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterQuestData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterQuestData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getQuestsQuery(dbContext, characterId).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				var quests = entities.Select(q => new CharacterQuestData(
					id: q.ID,
					version: q.Version,
					characterID: q.CharacterID,
					templateID: q.TemplateID,
					status: q.Status,
					objectiveValues: q.ObjectiveValues
				)).ToList();

				return (IReadOnlyList<CharacterQuestData>)quests;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}