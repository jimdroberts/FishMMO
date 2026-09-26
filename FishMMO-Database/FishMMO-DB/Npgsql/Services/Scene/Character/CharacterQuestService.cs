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
		public Task<DatabaseResult<BulkWriteResult>> PersistAsync(IEnumerable<CharacterQuestData> quests, CancellationToken cancellationToken = default)
			=> PersistBatchAsync(quests, null, cancellationToken);

		/// <inheritdoc/>
		public Task<DatabaseResult<BulkWriteResult>> PersistOwnedAsync(IEnumerable<CharacterQuestData> quests, IReadOnlyCollection<CharacterSessionLeaseData> claims, CancellationToken cancellationToken = default)
		{
			string? invalid = CharacterWriteGate.ValidateClaims(claims);
			return invalid != null
				? Task.FromResult(DatabaseResult<BulkWriteResult>.Failure(DatabaseErrorCodes.ValidationError, invalid))
				: PersistBatchAsync(quests, claims, cancellationToken);
		}

		/// <summary>
		/// The batch write behind <see cref="PersistAsync(IEnumerable{CharacterQuestData}, CancellationToken)"/> and
		/// <see cref="PersistOwnedAsync"/>.
		/// </summary>
		/// <param name="quests">Rows to write.</param>
		/// <param name="claims">The writer's claims, or null for the ungated write. See <see cref="CharacterWriteGate"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		private async Task<DatabaseResult<BulkWriteResult>> PersistBatchAsync(IEnumerable<CharacterQuestData> quests, IReadOnlyCollection<CharacterSessionLeaseData>? claims, CancellationToken cancellationToken)
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

			// Counted before collapsing, so a key the batch names twice shows as Filtered. See BulkBatch.
			int suppliedRows = questList.Count;
			// One row per key, the newest version: see BulkBatch.KeepNewest. An upsert cannot touch one
			// row twice, and an UPDATE ... FROM matching one row twice is ambiguous.
			questList = BulkBatch.KeepNewest(questList, quest => (quest.CharacterID, quest.TemplateID), quest => quest.Version);


			return await ExecuteTransactionAsync<BulkWriteResult>(async dbContext =>
			{
				var characterIds = questList.Select(q => q.CharacterID).Distinct().ToArray();
				/* The ownership gate, and the per-character existence check it subsumes: the rows of a
				 * missing or deleted character, or — for an owned write — of one whose claim the writer
				 * no longer holds, are left out and reported as Filtered rather than failing every other
				 * character's rows with them. See CharacterWriteGate. */
				CharacterWriteAdmission admission = await CharacterWriteGate.AdmitAsync(dbContext, characterIds, claims, cancellationToken).ConfigureAwait(false);
				int unownedRows = admission.CountUnowned(questList, row => row.CharacterID);

				var activeQuests = questList.Where(q => admission.Admits(q.CharacterID)).ToList();
				if (activeQuests.Count == 0)
				{
					return new BulkWriteResult(suppliedRows, 0, 0, unownedRows);
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

				return new BulkWriteResult(suppliedRows, activeQuests.Count, appliedRows, unownedRows);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public Task<DatabaseResult> DeleteQuestAsync(long characterId, int templateId, long incomingVersion, CancellationToken cancellationToken = default)
			=> DeleteQuestCoreAsync(characterId, templateId, incomingVersion, null, cancellationToken);

		/// <inheritdoc/>
		public Task<DatabaseResult> DeleteQuestOwnedAsync(long characterId, int templateId, long incomingVersion, CharacterSessionLeaseData claim, CancellationToken cancellationToken = default)
		{
			string? invalid = CharacterWriteGate.ValidateClaim(claim, characterId);
			return invalid != null
				? Task.FromResult(DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, invalid))
				: DeleteQuestCoreAsync(characterId, templateId, incomingVersion, claim, cancellationToken);
		}

		/// <summary>
		/// The delete behind <see cref="DeleteQuestAsync"/> and <see cref="DeleteQuestOwnedAsync"/>.
		/// </summary>
		/// <param name="characterId">Character who owns the quest.</param>
		/// <param name="templateId">Quest template ID to delete.</param>
		/// <param name="incomingVersion">Only deletes if this version exceeds the stored version.</param>
		/// <param name="claim">The writer's claim, or null for the ungated delete. See <see cref="CharacterWriteGate"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		private async Task<DatabaseResult> DeleteQuestCoreAsync(long characterId, int templateId, long incomingVersion, CharacterSessionLeaseData? claim, CancellationToken cancellationToken)
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

			Func<NpgsqlDbContext, Task> delete = async dbContext =>
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
			};

			if (!claim.HasValue)
			{
				return await ExecuteWriteAsync(delete, saveChanges: false, operationName: nameof(DeleteQuestAsync), cancellationToken: cancellationToken).ConfigureAwait(false);
			}

			/* Owned: the gate and the soft delete in one transaction, so the claim it admits is held —
			 * under the character's share lock — until the tombstone is written. Retrying after a lost
			 * commit reply is safe: the tombstone already carries this version, so the retry matches
			 * nothing and finds no live row. */
			CharacterSessionLeaseData held = claim.Value;
			return await ExecuteTransactionAsync(async dbContext =>
			{
				await CharacterWriteGate.AdmitOneAsync(dbContext, characterId, held, cancellationToken).ConfigureAwait(false);
				await delete(dbContext).ConfigureAwait(false);
			}, saveChanges: false, operationName: nameof(DeleteQuestOwnedAsync), cancellationToken: cancellationToken).ConfigureAwait(false);
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