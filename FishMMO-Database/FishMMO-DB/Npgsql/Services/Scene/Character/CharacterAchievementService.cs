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
	/// Service for managing character achievements in the database.
	/// Provides async operations for CRUD operations on character achievement data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	public sealed class CharacterAchievementService : BaseService<CharacterAchievementEntity>, ICharacterAchievementService
	{
		/// <summary>
		/// Compiled query for retrieving character achievements.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterAchievementEntity>>> GetAchievementsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterAchievements
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId)
					.ToList());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterAchievementService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterAchievementService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveAchievementsAsync(IEnumerable<CharacterAchievementData> achievements, CancellationToken cancellationToken = default)
		{
			if (achievements == null || !achievements.Any())
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Achievements collection must not be null or empty.");
			}

			var achievementList = achievements.ToList();

			// Extract arrays for bulk UPSERT
			var characterIds = achievementList.Select(a => a.CharacterID).ToArray();
			var templateIds = achievementList.Select(a => a.TemplateID).ToArray();
			var tiers = achievementList.Select(a => a.Tier).ToArray();
			var values = achievementList.Select(a => a.Value).ToArray();

			var result = await ExecuteSqlAsync(
				$@"INSERT INTO {TableName} 
					(character_id, template_id, tier, value)
					SELECT * FROM UNNEST(
						{characterIds}::bigint[],
						{templateIds}::int[],
						{tiers}::smallint[],
						{values}::int[]
					)
					ON CONFLICT (character_id, template_id) 
					DO UPDATE SET 
						tier = EXCLUDED.tier,
						value = EXCLUDED.value",
				"SaveCharacterAchievements",
				entityName: "CharacterAchievement",
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAchievementsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.");
			}

			var result = await ExecuteSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {characterId}",
				"DeleteCharacterAchievements",
				entityName: "CharacterAchievement",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterAchievementData>>> GetAchievementsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterAchievementData>>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.");
			}

			return await ExecuteSqlAsync(async dbContext =>
			{
				var entities = await GetAchievementsQuery(dbContext, characterId, cancellationToken);
				var achievements = entities.Select(a => new CharacterAchievementData(
					id: a.ID,
					characterID: a.CharacterID,
					templateID: a.TemplateID,
					tier: a.Tier,
					value: a.Value
				)).ToList();

				return (IReadOnlyList<CharacterAchievementData>)achievements;
			}, "GetCharacterAchievements", cancellationToken);
		}
	}
}