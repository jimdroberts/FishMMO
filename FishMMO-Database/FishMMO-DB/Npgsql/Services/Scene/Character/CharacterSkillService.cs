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
	/// Service for managing character skills in the database.
	/// Provides async operations for CRUD operations on character skill data.
	/// Uses the BaseService execution strategy for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	public sealed class CharacterSkillService : BaseService<CharacterSkillEntity>, ICharacterSkillService
	{
		/// <summary>
		/// Compiled query for retrieving character skills (hot path for character load).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterSkillEntity>>> getSkillsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterSkills
					.AsNoTracking()
					.Where(s => s.CharacterID == characterId && !s.Deleted)
					.ToList());

		/// <summary>
		/// Compiled query for counting character skills.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> getCountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterSkills
					.AsNoTracking()
					.Where(s => s.CharacterID == characterId && !s.Deleted)
					.Count());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterSkillService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterSkillService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> CountAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<int>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
				await getCountQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> PersistAsync(CharacterSkillData skillData, CancellationToken cancellationToken = default)
		{
			if (skillData.CharacterID <= 0)
			{
				return DatabaseResult<long>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			if (skillData.Version <= 0)
			{
				return DatabaseResult<long>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid version. Version must be greater than 0.");
			}

			var result = await ExecuteTransactionAsync<long>(async dbContext =>
			{
				var isCharacterActive = await dbContext.Characters
					.AsNoTracking()
					.AnyAsync(c => c.ID == skillData.CharacterID && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (!isCharacterActive)
				{
					throw new DatabaseEntityNotFoundException("Character", skillData.CharacterID.ToString());
				}

				var now = DateTime.UtcNow;
				var sql = $@"
					WITH upserted AS (
						INSERT INTO {TableName}
							(character_id, template_id, version, level, experience, cast_time_end, cooldown_end, time_created, deleted, time_deleted)
						VALUES
							({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, {{7}}, FALSE, NULL)
						ON CONFLICT (character_id, template_id)
						DO UPDATE SET
							level = EXCLUDED.level,
							experience = EXCLUDED.experience,
							cast_time_end = EXCLUDED.cast_time_end,
							cooldown_end = EXCLUDED.cooldown_end,
							deleted = FALSE,
							time_deleted = NULL,
							version = EXCLUDED.version
						WHERE
							EXCLUDED.version > {TableName}.version
						RETURNING id
					)
					SELECT COALESCE((SELECT id FROM upserted LIMIT 1), 0)::bigint AS value";

				var id = await ExecuteScalarLongAsync(
					dbContext,
					sql,
					new object[] { skillData.CharacterID, skillData.TemplateID, skillData.Version, skillData.Level, skillData.Experience, skillData.CastTimeEnd, skillData.CooldownEnd, now },
					cancellationToken).ConfigureAwait(false);

				if (id <= 0)
				{
					throw new StaleStateException("Skill persist rejected due to a stale Version.");
				}

				return id;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(IEnumerable<CharacterSkillData> skills, CancellationToken cancellationToken = default)
		{
			if (skills == null || !skills.Any())
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Skills collection must not be null or empty.");
			}

			var list = skills.ToList();
			if (list.Any(s => s.Version <= 0))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more skills had an invalid Version. Version must be greater than 0.");
			}

			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (list.Count > 1)
			{
				var deduped = new Dictionary<(long CharacterID, int TemplateID), CharacterSkillData>();
				foreach (var skill in list)
				{
					deduped[(skill.CharacterID, skill.TemplateID)] = skill;
				}

				if (deduped.Count != list.Count)
				{
					list = deduped.Values.ToList();
				}
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var allCharacterIds = list.Select(s => s.CharacterID).Distinct().ToArray();
				var activeCharacterIds = await dbContext.Characters
					.AsNoTracking()
					.Where(c => allCharacterIds.Contains(c.ID) && !c.Deleted)
					.Select(c => c.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);

				if (activeCharacterIdSet.Count != allCharacterIds.Length)
				{
					var missingCharacterId = allCharacterIds.First(id => !activeCharacterIdSet.Contains(id));
					throw new DatabaseEntityNotFoundException("Character", missingCharacterId.ToString(), "Character not found or deleted.");
				}

				var activeSkills = list.Where(s => activeCharacterIdSet.Contains(s.CharacterID)).ToList();
				if (activeSkills.Count == 0)
				{
					return;
				}

				var now = DateTime.UtcNow;
				var characterIdArray = activeSkills.Select(s => s.CharacterID).ToArray();
				var templateIdArray = activeSkills.Select(s => s.TemplateID).ToArray();
				var versionArray = activeSkills.Select(s => s.Version).ToArray();
				var levelArray = activeSkills.Select(s => s.Level).ToArray();
				var experienceArray = activeSkills.Select(s => s.Experience).ToArray();
				var castTimeEndArray = activeSkills.Select(s => s.CastTimeEnd).ToArray();
				var cooldownEndArray = activeSkills.Select(s => s.CooldownEnd).ToArray();

				var sql = $@"
					INSERT INTO {TableName}
						(character_id, template_id, version, level, experience, cast_time_end, cooldown_end, time_created, deleted, time_deleted)
					SELECT
						u.character_id,
						u.template_id,
						u.version,
						u.level,
						u.experience,
						u.cast_time_end,
						u.cooldown_end,
						{{7}},
						FALSE,
						NULL
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::integer[],
						{{2}}::bigint[],
						{{3}}::integer[],
						{{4}}::integer[],
						{{5}}::double precision[],
						{{6}}::double precision[]
					) AS u(character_id, template_id, version, level, experience, cast_time_end, cooldown_end)
					ON CONFLICT (character_id, template_id)
					DO UPDATE SET
						level = EXCLUDED.level,
						experience = EXCLUDED.experience,
						cast_time_end = EXCLUDED.cast_time_end,
						cooldown_end = EXCLUDED.cooldown_end,
						deleted = FALSE,
						time_deleted = NULL,
						version = EXCLUDED.version
					WHERE
						EXCLUDED.version > {TableName}.version;";

				await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeSkills.Count,
					new object[] { characterIdArray, templateIdArray, versionArray, levelArray, experienceArray, castTimeEndArray, cooldownEndArray, now },
					"One or more skills were rejected due to a stale Version.",
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
					"Character ID must be greater than 0.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid version. Version must be greater than 0.");
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
					var anyActive = await dbContext.CharacterSkills
						.AsNoTracking()
						.AnyAsync(s => s.CharacterID == characterId && !s.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Skill delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterSkillData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterSkillData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getSkillsQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				var skills = entities.Select(s => new CharacterSkillData(
					id: s.ID,
					version: s.Version,
					characterID: s.CharacterID,
					templateID: s.TemplateID,
					level: s.Level,
					experience: s.Experience,
					castTimeEnd: s.CastTimeEnd,
					cooldownEnd: s.CooldownEnd
				)).ToList();

				return (IReadOnlyList<CharacterSkillData>)skills;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
