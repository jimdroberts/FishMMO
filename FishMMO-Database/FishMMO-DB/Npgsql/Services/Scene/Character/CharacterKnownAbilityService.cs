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
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character known abilities including:
	/// - Single known ability save with atomic INSERT ON CONFLICT operations
	/// - Batch known ability save with transactions
	/// - Known ability deletion (single and bulk operations)
	/// - Known ability retrieval
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
	public sealed class CharacterKnownAbilityService : BaseService<CharacterKnownAbilityEntity>, ICharacterKnownAbilityService
	{
		/// <summary>
		/// Compiled query for retrieving character known abilities.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterKnownAbilityEntity>>> GetKnownAbilitiesQuery =
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
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Use atomic INSERT with ON CONFLICT DO NOTHING for thread safety
				await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"INSERT INTO {TableName} (character_id, template_id)
								VALUES ({characterId}, {templateId})
								ON CONFLICT (character_id, template_id) DO NOTHING",
					cancellationToken);
			}, "SaveKnownAbility", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveKnownAbilitiesAsync(IEnumerable<CharacterKnownAbilityData> knownAbilities, CancellationToken cancellationToken = default)
		{
			var abilityList = knownAbilities?.ToList();
			if (abilityList == null || abilityList.Count == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Empty or null abilities collection");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Extract arrays for bulk UPSERT
				var characterIds = abilityList.Select(a => a.CharacterID).ToArray();
				var templateIds = abilityList.Select(a => a.TemplateID).ToArray();

				// Single bulk INSERT using UNNEST with ON CONFLICT - atomic operation, no transaction needed
				await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"INSERT INTO {TableName} (character_id, template_id)
						SELECT * FROM UNNEST(
							{characterIds}::bigint[],
							{templateIds}::int[]
						)
						ON CONFLICT (character_id, template_id) DO NOTHING",
					cancellationToken);
			}, "SaveKnownAbilities", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteKnownAbilityAsync(long characterId, int templateId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Use atomic DELETE for thread safety
				await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"DELETE FROM {TableName} 
							WHERE character_id = {characterId} AND template_id = {templateId}",
					cancellationToken);
			}, "DeleteKnownAbility", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAllKnownAbilitiesAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Use atomic DELETE for thread safety
				await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"DELETE FROM {TableName} WHERE character_id = {characterId}",
					cancellationToken);
			}, "DeleteAllKnownAbilities", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>> GetKnownAbilitiesAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteWithStrategyAsync<IReadOnlyList<CharacterKnownAbilityData>>(
				async (dbContext) =>
				{
					var entities = await GetKnownAbilitiesQuery(dbContext, characterId, cancellationToken);
					var abilities = entities.Select(a => new CharacterKnownAbilityData(
						id: a.ID,
						characterID: a.CharacterID,
						templateID: a.TemplateID
					)).ToList();

					return (IReadOnlyList<CharacterKnownAbilityData>)abilities;
				},
				"GetKnownAbilities",
				cancellationToken);
		}
	}
}