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
	/// - OperationCanceledException → DatabaseTimeoutException
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
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterFactionEntity>>> GetFactionsQuery =
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
					"Empty or null factions collection.");
			}

			return await ExecuteWithStrategyAsync(async (dbContext, strategy) =>
			{
				// Extract arrays for bulk UPSERT
				var characterIds = factionList.Select(f => f.CharacterID).ToArray();
				var templateIds = factionList.Select(f => f.TemplateID).ToArray();
				var values = factionList.Select(f => f.Value).ToArray();

				// Single bulk UPSERT using UNNEST - atomic operation, no transaction needed
				await strategy.ExecuteAsync(async () =>
				{
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {TableName} (character_id, template_id, value)
					SELECT * FROM UNNEST(
						{characterIds}::bigint[],
						{templateIds}::int[],
						{values}::int[]
					)
					ON CONFLICT (character_id, template_id) DO UPDATE SET
						value = EXCLUDED.value",
						cancellationToken);
				});
			}, "SaveFactions", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteFactionsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteWithStrategyAsync(async (dbContext, strategy) =>
			{
				// Use atomic DELETE for thread safety
				await strategy.ExecuteAsync(async () =>
				{
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {TableName} WHERE character_id = {characterId}",
						cancellationToken);
				});
			}, "DeleteFactions", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterFactionData>>> GetFactionsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterFactionData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteWithStrategyAsync<IReadOnlyList<CharacterFactionData>>(async dbContext =>
			{
				var entities = await GetFactionsQuery(dbContext, characterId, cancellationToken);
				var factions = entities.Select(f => new CharacterFactionData(
					id: f.ID,
					characterID: f.CharacterID,
					templateID: f.TemplateID,
					value: f.Value
				)).ToList();

				return (IReadOnlyList<CharacterFactionData>)factions;
			}, "GetFactions", cancellationToken);
		}
	}
}