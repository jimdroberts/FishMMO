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
			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (achievementList.Count > 1)
			{
				var deduped = new Dictionary<(long CharacterID, int TemplateID), CharacterAchievementData>();
				foreach (var achievement in achievementList)
				{
					deduped[(achievement.CharacterID, achievement.TemplateID)] = achievement;
				}

				if (deduped.Count != achievementList.Count)
				{
					achievementList = deduped.Values.ToList();
				}
			}

			// Extract arrays for bulk UPSERT
			var characterIds = achievementList.Select(a => a.CharacterID).ToArray();
			var templateIds = achievementList.Select(a => a.TemplateID).ToArray();
			var tiers = achievementList.Select(a => a.Tier).ToArray();
			var values = achievementList.Select(a => a.Value).ToArray();

			var result = await ExecuteAsync(async (dbContext, ct) =>
			{
				var charactersTableName = dbContext.GetTableName<CharacterEntity>();
				return await dbContext.Database.ExecuteSqlRawAsync(
					$@"WITH active_characters AS (
						SELECT id
						FROM {charactersTableName}
						WHERE id = ANY({{0}}::bigint[]) AND deleted = FALSE
						ORDER BY id
						FOR KEY SHARE
					)
					INSERT INTO {TableName} (character_id, template_id, tier, value, time_created)
					SELECT u.character_id, u.template_id, u.tier, u.value, CURRENT_TIMESTAMP
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::int[],
						{{2}}::smallint[],
						{{3}}::int[]
					) AS u(character_id, template_id, tier, value)
					JOIN active_characters ac ON ac.id = u.character_id
					ON CONFLICT (character_id, template_id) DO UPDATE SET
						tier = EXCLUDED.tier,
						value = EXCLUDED.value",
					new object[] { characterIds, templateIds, tiers, values },
					ct);
			}, "SaveCharacterAchievements", cancellationToken).ConfigureAwait(false);

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

			var result = await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {{0}}",
				"DeleteCharacterAchievements",
				new object[] { characterId },
				entityName: "CharacterAchievement",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);

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

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				var entities = await GetAchievementsQuery(dbContext, characterId, ct).ConfigureAwait(false);
				var achievements = entities.Select(a => new CharacterAchievementData(
					id: a.ID,
					characterID: a.CharacterID,
					templateID: a.TemplateID,
					tier: a.Tier,
					value: a.Value
				)).ToList();

				return (IReadOnlyList<CharacterAchievementData>)achievements;
			}, "GetCharacterAchievements", cancellationToken).ConfigureAwait(false);
		}
	}
}