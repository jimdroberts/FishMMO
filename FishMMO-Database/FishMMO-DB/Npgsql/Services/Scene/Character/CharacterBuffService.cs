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
	/// Character buff service with async operations, atomic SQL, and DTO pattern.
	/// Uses repository pattern with EF Core and raw SQL for race-condition-prone operations.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// Follows SOLID principles: SRP, OCP, LSP, ISP, DIP.
	/// </summary>
	/// <remarks>
	/// All methods that use ExecuteSqlRawAsync are wrapped in execution strategies
	/// to provide automatic retry logic (up to 3 attempts) for transient database failures
	/// such as connection timeouts, deadlocks, or network interruptions.
	/// 
	/// Exception Handling Strategy:
	/// - Catches specific exceptions (NpgsqlException, DbUpdateException, TimeoutException)
	/// - Converts to custom DatabaseException hierarchy with sanitized messages
	/// - Returns DatabaseResult for safe, typed error handling
	/// - Preserves detailed error information for logging while exposing safe messages to clients
	/// </remarks>
	public sealed class CharacterBuffService : BaseService<CharacterBuffEntity>, ICharacterBuffService
	{
		/// <summary>
		/// Compiled query for retrieving character buffs (hot path for character state).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterBuffEntity>>> getBuffsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterBuffs
					.AsNoTracking()
					.Where(b => b.CharacterID == characterId && !b.Deleted)
					.ToList());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterBuffService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterBuffService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveBuffsAsync(IEnumerable<CharacterBuffData> buffs, CancellationToken cancellationToken = default)
		{
			var buffList = buffs?.ToList();
			if (buffList == null || buffList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"No buffs to save. Buffs collection must not be null or empty.",
					isTransient: false);
			}

			if (buffList.Any(b => b.Version <= 0))
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"One or more buffs had an invalid Version. Version must be greater than 0.",
					isTransient: false);
			}

			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (buffList.Count > 1)
			{
				var deduped = new Dictionary<(long CharacterID, int TemplateID), CharacterBuffData>();
				foreach (var buff in buffList)
				{
					deduped[(buff.CharacterID, buff.TemplateID)] = buff;
				}

				if (deduped.Count != buffList.Count)
				{
					buffList = deduped.Values.ToList();
				}
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var characterIds = buffList.Select(b => b.CharacterID).Distinct().ToArray();
				var activeCharacterIds = await dbContext.Characters
					.AsNoTracking()
					.Where(c => characterIds.Contains(c.ID) && !c.Deleted)
					.Select(c => c.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);

				var activeBuffs = buffList.Where(b => activeCharacterIdSet.Contains(b.CharacterID)).ToList();
				if (activeBuffs.Count == 0)
				{
					return;
				}

				var now = DateTime.UtcNow;
				var characterIdArray = activeBuffs.Select(b => b.CharacterID).ToArray();
				var templateIdArray = activeBuffs.Select(b => b.TemplateID).ToArray();
				var versionArray = activeBuffs.Select(b => b.Version).ToArray();
				var remainingTimeArray = activeBuffs.Select(b => b.RemainingTime).ToArray();
				var tickTimeArray = activeBuffs.Select(b => b.TickTime).ToArray();
				var stacksArray = activeBuffs.Select(b => b.Stacks).ToArray();

				var sql = $@"
					INSERT INTO {TableName}
						(character_id, template_id, version, remaining_time, tick_time, stacks, time_created, deleted, time_deleted)
					SELECT
						u.character_id,
						u.template_id,
						u.version,
						u.remaining_time,
						u.tick_time,
						u.stacks,
						{{6}},
						FALSE,
						NULL
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::integer[],
						{{2}}::bigint[],
						{{3}}::real[],
						{{4}}::real[],
						{{5}}::integer[]
					) AS u(character_id, template_id, version, remaining_time, tick_time, stacks)
					ON CONFLICT (character_id, template_id)
					DO UPDATE SET
						remaining_time = EXCLUDED.remaining_time,
						tick_time = EXCLUDED.tick_time,
						stacks = EXCLUDED.stacks,
						deleted = FALSE,
						time_deleted = NULL,
						version = EXCLUDED.version
					WHERE
						EXCLUDED.version > {TableName}.version
						OR (
							EXCLUDED.version = {TableName}.version
							AND {TableName}.remaining_time = EXCLUDED.remaining_time
							AND {TableName}.tick_time = EXCLUDED.tick_time
							AND {TableName}.stacks = EXCLUDED.stacks
							AND {TableName}.deleted = FALSE
							AND {TableName}.time_deleted IS NULL
						);";

				await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeBuffs.Count,
					new object[] { characterIdArray, templateIdArray, versionArray, remainingTimeArray, tickTimeArray, stacksArray, now },
					"One or more buffs were rejected due to a stale Version.",
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteBuffsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}
					WHERE character_id = {{1}} AND deleted = FALSE";
				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { now, characterId }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterBuffData>>> GetBuffsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterBuffData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getBuffsQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				var buffs = entities.Select(b => new CharacterBuffData(
					id: b.ID,
					version: b.Version,
					characterID: b.CharacterID,
					templateID: b.TemplateID,
					remainingTime: b.RemainingTime,
					tickTime: b.TickTime,
					stacks: b.Stacks
				)).ToList();

				return (IReadOnlyList<CharacterBuffData>)buffs;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}