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
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterBuffEntity>>> GetBuffsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterBuffs
					.AsNoTracking()
					.Where(b => b.CharacterID == characterId)
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
					"No buffs to save. Buffs collection must not be null or empty.");
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

			// Extract arrays for bulk UPSERT
			var characterIds = buffList.Select(b => b.CharacterID).ToArray();
			var templateIds = buffList.Select(b => b.TemplateID).ToArray();
			var remainingTimes = buffList.Select(b => b.RemainingTime).ToArray();
			var tickTimes = buffList.Select(b => b.TickTime).ToArray();
			var stacks = buffList.Select(b => b.Stacks).ToArray();

			var result = await ExecuteAsync(async (dbContext, ct) =>
			{
				var charactersTableName = dbContext.GetTableName<CharacterEntity>();
				return await dbContext.Database.ExecuteSqlRawAsync(
					$@"WITH active_characters AS (
						SELECT id
						FROM {charactersTableName}
						WHERE id = ANY({{0}}::bigint[]) AND deleted = FALSE
						FOR KEY SHARE
					)
					INSERT INTO {TableName} (character_id, template_id, remaining_time, tick_time, stacks, time_created)
					SELECT u.character_id, u.template_id, u.remaining_time, u.tick_time, u.stacks, CURRENT_TIMESTAMP
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::int[],
						{{2}}::float4[],
						{{3}}::float4[],
						{{4}}::int[]
					) AS u(character_id, template_id, remaining_time, tick_time, stacks)
					JOIN active_characters ac ON ac.id = u.character_id
					ON CONFLICT (character_id, template_id) DO UPDATE SET
						remaining_time = EXCLUDED.remaining_time,
						tick_time = EXCLUDED.tick_time,
						stacks = EXCLUDED.stacks",
					new object[] { characterIds, templateIds, remainingTimes, tickTimes, stacks },
					ct);
			}, "SaveBuffs", cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteBuffsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			var result = await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {{0}}",
				"DeleteBuffs",
				new object[] { characterId },
				entityName: "CharacterBuff",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterBuffData>>> GetBuffsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterBuffData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				var entities = await GetBuffsQuery(dbContext, characterId, ct);
				var buffs = entities.Select(b => new CharacterBuffData(
					id: b.ID,
					characterID: b.CharacterID,
					templateID: b.TemplateID,
					remainingTime: b.RemainingTime,
					tickTime: b.TickTime,
					stacks: b.Stacks
				)).ToList();

				return (IReadOnlyList<CharacterBuffData>)buffs;
			}, "GetBuffs", cancellationToken);
		}
	}
}