using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service for managing character achievements in the database.
	/// Provides async operations for CRUD operations on character achievement data.
	/// Uses the BaseService execution strategy for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	public sealed class CharacterAchievementService : BaseService<CharacterAchievementEntity>, ICharacterAchievementService
	{
		/// <summary>
		/// Compiled query for retrieving character achievements.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterAchievementEntity>>> getAchievementsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterAchievements
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId && !a.Deleted)
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
					"Achievements collection must not be null or empty.",
					isTransient: false);
			}

			var achievementList = achievements.ToList();
			if (achievementList.Any(a => a.Version <= 0))
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"One or more achievements had an invalid Version. Version must be greater than 0.",
					isTransient: false);
			}

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

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var characterIds = achievementList.Select(a => a.CharacterID).Distinct().ToArray();
				var activeCharacterIds = await dbContext.Characters
					.AsNoTracking()
					.Where(c => characterIds.Contains(c.ID) && !c.Deleted)
					.Select(c => c.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);

				var activeAchievements = achievementList.Where(a => activeCharacterIdSet.Contains(a.CharacterID)).ToList();
				if (activeAchievements.Count == 0)
				{
					return;
				}

				var now = DateTime.UtcNow;
				var characterIdArray = activeAchievements.Select(a => a.CharacterID).ToArray();
				var templateIdArray = activeAchievements.Select(a => a.TemplateID).ToArray();
				var versionArray = activeAchievements.Select(a => a.Version).ToArray();
				var tierArray = activeAchievements.Select(a => (short)a.Tier).ToArray();
				var valueArray = activeAchievements.Select(a => (long)a.Value).ToArray();

				var sql = $@"
					INSERT INTO {TableName}
						(character_id, template_id, version, tier, value, time_created, deleted, time_deleted)
					SELECT
						u.character_id,
						u.template_id,
						u.version,
						u.tier,
						u.value,
						{{5}},
						FALSE,
						NULL
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::integer[],
						{{2}}::bigint[],
						{{3}}::smallint[],
						{{4}}::bigint[]
					) AS u(character_id, template_id, version, tier, value)
					ON CONFLICT (character_id, template_id)
					DO UPDATE SET
						tier = EXCLUDED.tier,
						value = EXCLUDED.value,
						deleted = FALSE,
						time_deleted = NULL,
						version = EXCLUDED.version
					WHERE
						EXCLUDED.version > {TableName}.version
						OR (
							EXCLUDED.version = {TableName}.version
							AND {TableName}.tier = EXCLUDED.tier
							AND {TableName}.value = EXCLUDED.value
							AND {TableName}.deleted = FALSE
							AND {TableName}.time_deleted IS NULL
						);";

				await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeAchievements.Count,
					new object[] { characterIdArray, templateIdArray, versionArray, tierArray, valueArray, now },
					"One or more achievements were rejected due to a stale Version.",
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAchievementsAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid Version. Version must be greater than 0.",
					isTransient: false);
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
					var anyActive = await dbContext.CharacterAchievements
						.AsNoTracking()
						.AnyAsync(a => a.CharacterID == characterId && !a.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Achievement delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterAchievementData>>> GetAchievementsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterAchievementData>>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getAchievementsQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				var achievements = entities.Select(a => new CharacterAchievementData(
					id: a.ID,
					version: a.Version,
					characterID: a.CharacterID,
					templateID: a.TemplateID,
					tier: a.Tier,
					value: a.Value
				)).ToList();

				return (IReadOnlyList<CharacterAchievementData>)achievements;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}