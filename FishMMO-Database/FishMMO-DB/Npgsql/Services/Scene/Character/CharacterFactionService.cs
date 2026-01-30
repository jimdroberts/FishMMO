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
	/// - PostgresException (23505) → DatabaseConstraintException (Unique violation)
	/// - PostgresException (23503) → DatabaseConstraintException (Foreign key violation)
	/// - NpgsqlException → DatabaseConnectionException
	/// - DbUpdateException → DatabaseQueryException
	/// - Exception → DatabaseQueryException
	/// 
	/// Methods return DatabaseResult to provide structured error handling
	/// without throwing exceptions to calling code.
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
					.Where(f => f.CharacterID == characterId)
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
		public async Task<DatabaseResult> SaveFactionsAsync(IEnumerable<CharacterFactionData> factions, CancellationToken cancellationToken = default)
		{
			var factionList = factions?.ToList();
			if (factionList == null || factionList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Empty or null factions collection.",
					isTransient: false);
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

				var templateIds = factionList.Select(f => f.TemplateID).Distinct().ToArray();
				var existing = await dbContext.CharacterFactions
					.Where(f => activeCharacterIdSet.Contains(f.CharacterID) && templateIds.Contains(f.TemplateID))
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var existingByKey = new Dictionary<(long CharacterID, int TemplateID), CharacterFactionEntity>();
				foreach (var entity in existing)
				{
					existingByKey[(entity.CharacterID, entity.TemplateID)] = entity;
				}

				foreach (var faction in factionList)
				{
					if (!activeCharacterIdSet.Contains(faction.CharacterID)) continue;

					var key = (faction.CharacterID, faction.TemplateID);
					if (!existingByKey.TryGetValue(key, out var entity))
					{
						entity = new CharacterFactionEntity
						{
							CharacterID = faction.CharacterID,
							TemplateID = faction.TemplateID,
							Version = faction.Version,
							TimeCreated = DateTime.UtcNow
						};
						await dbContext.CharacterFactions.AddAsync(entity, cancellationToken).ConfigureAwait(false);
						existingByKey[key] = entity;
					}

					ValidateVersion(entity, faction.Version);

					entity.Value = faction.Value;
				}
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteFactionsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var factionIds = await dbContext.CharacterFactions
					.AsNoTracking()
					.Where(f => f.CharacterID == characterId)
					.Select(f => f.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				foreach (var factionId in factionIds)
				{
					var entity = new CharacterFactionEntity { ID = factionId };
					dbContext.CharacterFactions.Remove(entity);
				}
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterFactionData>>> GetFactionsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterFactionData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
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