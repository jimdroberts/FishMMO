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
	/// Service for managing character factions in the database.
	/// Provides async operations for CRUD operations on character faction reputation data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character faction reputation including:
	/// - Batch faction save/update with atomic UPSERT operations
	/// - Faction deletion (bulk operations)
	/// - Faction retrieval
	/// 
	/// All database exceptions are caught and wrapped in appropriate DatabaseException types:
	/// - OperationCanceledException → DatabaseOperationCanceledException
	/// - PostgresException (23505) → DatabaseConstraintException (Unique constraint conflict; non-transient failure)
	/// - PostgresException (23503) → DatabaseConstraintException (Foreign key violation)
	/// - NpgsqlException → DatabaseConnectionException
	/// - DbUpdateException → DatabaseQueryException
	/// - Exception → DatabaseQueryException
	/// 
	/// Methods return DatabaseResult to provide structured error handling
	/// without throwing exceptions to calling code.
	/// Unique constraint violations are not used as normal control flow; write paths prefer deterministic SQL (e.g. UPSERT) where appropriate.
	/// </remarks>
	public sealed class CharacterFactionService : BaseService<CharacterFactionEntity>, ICharacterFactionService
	{
		/// <summary>
		/// Compiled query for retrieving character factions.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterFactionEntity>>> getFactionsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterFactions
					.AsNoTracking()
					.Where(f => f.CharacterID == characterId && !f.Deleted)
					.ToList());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterFactionService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterFactionService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(IEnumerable<CharacterFactionData> factions, CancellationToken cancellationToken = default)
		{
			var factionList = factions?.ToList();
			if (factionList == null || factionList.Count == 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Empty or null factions collection.");
			}

			if (factionList.Any(f => f.Version <= 0))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more factions had an invalid Version. Version must be greater than 0.");
			}

			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (factionList.Count > 1)
			{
				var deduped = new Dictionary<(long CharacterID, int TemplateID), CharacterFactionData>();
				foreach (var faction in factionList)
				{
					deduped[(faction.CharacterID, faction.TemplateID)] = faction;
				}

				if (deduped.Count != factionList.Count)
				{
					factionList = deduped.Values.ToList();
				}
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var characterIds = factionList.Select(f => f.CharacterID).Distinct().ToArray();
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

				var activeFactions = factionList.Where(f => activeCharacterIdSet.Contains(f.CharacterID)).ToList();
				if (activeFactions.Count == 0)
				{
					return;
				}

				var now = DateTime.UtcNow;
				var characterIdArray = activeFactions.Select(f => f.CharacterID).ToArray();
				var templateIdArray = activeFactions.Select(f => f.TemplateID).ToArray();
				var versionArray = activeFactions.Select(f => f.Version).ToArray();
				var valueArray = activeFactions.Select(f => f.Value).ToArray();

				var sql = $@"
					INSERT INTO {TableName}
						(character_id, template_id, version, value, time_created, deleted, time_deleted)
					SELECT
						u.character_id,
						u.template_id,
						u.version,
						u.value,
						{{4}},
						FALSE,
						NULL
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::integer[],
						{{2}}::bigint[],
						{{3}}::integer[]
					) AS u(character_id, template_id, version, value)
					ON CONFLICT (character_id, template_id)
					DO UPDATE SET
						value = EXCLUDED.value,
						deleted = FALSE,
						time_deleted = NULL,
						version = EXCLUDED.version
					WHERE
						EXCLUDED.version > {TableName}.version;";

				await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeFactions.Count,
					new object[] { characterIdArray, templateIdArray, versionArray, valueArray, now },
					"One or more factions were rejected due to a stale Version.",
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID. Character ID must be greater than 0.");
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
					var anyActive = await dbContext.CharacterFactions
						.AsNoTracking()
						.AnyAsync(f => f.CharacterID == characterId && !f.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Faction delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterFactionData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterFactionData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getFactionsQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				var factions = entities.Select(f => new CharacterFactionData(
					id: f.ID,
					version: f.Version,
					characterID: f.CharacterID,
					templateID: f.TemplateID,
					value: f.Value
				)).ToList();

				return (IReadOnlyList<CharacterFactionData>)factions;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}