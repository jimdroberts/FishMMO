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
	/// Service for managing character archetypes in the database.
	/// Provides async operations for CRUD operations on character archetype data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	public sealed class CharacterArchetypeService : BaseService<CharacterArchetypeEntity>, ICharacterArchetypeService
	{
		/// <summary>
		/// Compiled query for retrieving character archetypes.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, IAsyncEnumerable<CharacterArchetypeEntity>> getArchetypesQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId) =>
				context.CharacterArchetypes
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId && !a.Deleted));

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterArchetypeService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterArchetypeService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public Task<DatabaseResult<BulkWriteResult>> PersistAsync(IEnumerable<CharacterArchetypeData> archetypes, CancellationToken cancellationToken = default)
			=> PersistBatchAsync(archetypes, null, cancellationToken);

		/// <inheritdoc/>
		public Task<DatabaseResult<BulkWriteResult>> PersistOwnedAsync(IEnumerable<CharacterArchetypeData> archetypes, IReadOnlyCollection<CharacterSessionLeaseData> claims, CancellationToken cancellationToken = default)
		{
			string? invalid = CharacterWriteGate.ValidateClaims(claims);
			return invalid != null
				? Task.FromResult(DatabaseResult<BulkWriteResult>.Failure(DatabaseErrorCodes.ValidationError, invalid))
				: PersistBatchAsync(archetypes, claims, cancellationToken);
		}

		/// <summary>
		/// The batch write behind <see cref="PersistAsync(IEnumerable{CharacterArchetypeData}, CancellationToken)"/> and
		/// <see cref="PersistOwnedAsync"/>.
		/// </summary>
		/// <param name="archetypes">Rows to write.</param>
		/// <param name="claims">The writer's claims, or null for the ungated write. See <see cref="CharacterWriteGate"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		private async Task<DatabaseResult<BulkWriteResult>> PersistBatchAsync(IEnumerable<CharacterArchetypeData> archetypes, IReadOnlyCollection<CharacterSessionLeaseData>? claims, CancellationToken cancellationToken)
		{
			if (archetypes == null)
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Archetypes collection must not be null.");
			}

			var archetypeList = archetypes.ToList();
			if (archetypeList.Count == 0)
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Archetypes collection must not be empty.");
			}

			if (archetypeList.Any(a => a.Version <= 0))
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more archetypes had an invalid Version. Version must be greater than 0.");
			}

			// Counted before collapsing, so a key the batch names twice shows as Filtered. See BulkBatch.
			int suppliedRows = archetypeList.Count;
			// One row per key, the newest version: see BulkBatch.KeepNewest. An upsert cannot touch one
			// row twice, and an UPDATE ... FROM matching one row twice is ambiguous.
			archetypeList = BulkBatch.KeepNewest(archetypeList, archetype => (archetype.CharacterID, archetype.TemplateID), archetype => archetype.Version);


			return await ExecuteTransactionAsync<BulkWriteResult>(async dbContext =>
			{
				var characterIds = archetypeList.Select(a => a.CharacterID).Distinct().ToArray();
				/* The ownership gate, and the per-character existence check it subsumes: the rows of a
				 * missing or deleted character, or — for an owned write — of one whose claim the writer
				 * no longer holds, are left out and reported as Filtered rather than failing every other
				 * character's rows with them. See CharacterWriteGate. */
				CharacterWriteAdmission admission = await CharacterWriteGate.AdmitAsync(dbContext, characterIds, claims, cancellationToken).ConfigureAwait(false);
				int unownedRows = admission.CountUnowned(archetypeList, row => row.CharacterID);

				var activeArchetypes = archetypeList.Where(a => admission.Admits(a.CharacterID)).ToList();
				if (activeArchetypes.Count == 0)
				{
					return new BulkWriteResult(suppliedRows, 0, 0, unownedRows);
				}

				var now = DateTime.UtcNow;
				var characterIdArray = activeArchetypes.Select(a => a.CharacterID).ToArray();
				var templateIdArray = activeArchetypes.Select(a => a.TemplateID).ToArray();
				var versionArray = activeArchetypes.Select(a => a.Version).ToArray();

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

				int appliedRows = await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeArchetypes.Count,
					new object[] { characterIdArray, templateIdArray, versionArray, now },
					"One or more archetypes were rejected due to a stale Version.",
					cancellationToken,
					BulkVersionConflictPolicy.SkipStaleRows).ConfigureAwait(false);

				return new BulkWriteResult(suppliedRows, activeArchetypes.Count, appliedRows, unownedRows);
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
					var anyActive = await dbContext.CharacterArchetypes
						.AsNoTracking()
						.AnyAsync(a => a.CharacterID == characterId && !a.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Archetype delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterArchetypeData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterArchetypeData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getArchetypesQuery(dbContext, characterId).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				var archetypes = entities.Select(a => new CharacterArchetypeData(
					id: a.ID,
					version: a.Version,
					characterID: a.CharacterID,
					templateID: a.TemplateID
				)).ToList();

				return (IReadOnlyList<CharacterArchetypeData>)archetypes;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}