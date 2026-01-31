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

				var templateIds = achievementList.Select(a => a.TemplateID).Distinct().ToArray();
				var existing = await dbContext.CharacterAchievements
					.Where(a => activeCharacterIdSet.Contains(a.CharacterID) && templateIds.Contains(a.TemplateID))
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var existingByKey = new Dictionary<(long CharacterID, int TemplateID), CharacterAchievementEntity>();
				foreach (var entity in existing)
				{
					existingByKey[(entity.CharacterID, entity.TemplateID)] = entity;
				}

				foreach (var achievement in achievementList)
				{
					if (!activeCharacterIdSet.Contains(achievement.CharacterID)) continue;

					var key = (achievement.CharacterID, achievement.TemplateID);
					if (!existingByKey.TryGetValue(key, out var entity))
					{
						entity = new CharacterAchievementEntity
						{
							CharacterID = achievement.CharacterID,
							TemplateID = achievement.TemplateID,
							Version = achievement.Version,
							TimeCreated = DateTime.UtcNow
						};
						await dbContext.CharacterAchievements.AddAsync(entity, cancellationToken).ConfigureAwait(false);
						existingByKey[key] = entity;
					}

					ValidateVersion(entity, achievement.Version);
					if (entity.Deleted)
					{
						entity.Deleted = false;
						entity.TimeDeleted = null;
					}

					entity.Tier = achievement.Tier;
					entity.Value = achievement.Value;
				}
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAchievementsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var achievementIds = await dbContext.CharacterAchievements
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId && !a.Deleted)
					.Select(a => a.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				foreach (var achievementId in achievementIds)
				{
					var entity = new CharacterAchievementEntity { ID = achievementId, Deleted = true, TimeDeleted = now };
					dbContext.Attach(entity);
					dbContext.Entry(entity).Property(e => e.Deleted).IsModified = true;
					dbContext.Entry(entity).Property(e => e.TimeDeleted).IsModified = true;
				}
			}).ConfigureAwait(false);
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