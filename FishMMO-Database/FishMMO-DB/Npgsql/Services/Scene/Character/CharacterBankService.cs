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
	/// Character bank service with async operations, atomic SQL, and DTO pattern.
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
	public sealed class CharacterBankService : BaseService<CharacterBankEntity>, ICharacterBankService
	{
		/// <summary>
		/// Compiled query for retrieving character bank items (hot path for bank access).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterBankEntity>>> GetBankItemsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterBankItems
					.AsNoTracking()
					.Where(i => i.CharacterID == characterId)
					.ToList());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterBankService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterBankService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> SaveBankItemAsync(CharacterBankData item, CancellationToken cancellationToken = default)
		{
			if (item.CharacterID == 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteAsync<long>(async (dbContext, ct) =>
			{
				var charactersTableName = dbContext.GetTableName<CharacterEntity>();
				// Use PostgreSQL UPSERT for atomic insert-or-update
				var result = await dbContext.CharacterBankItems
					.FromSqlRaw($@"
					WITH active_character AS (
						SELECT id
						FROM {charactersTableName}
						WHERE id = {{0}} AND deleted = FALSE
						FOR KEY SHARE
					)
					INSERT INTO {TableName}
						(character_id, template_id, slot, seed, amount, time_created)
					SELECT
						{{0}}, {{1}}, {{2}}, {{3}}, {{4}}, CURRENT_TIMESTAMP
					FROM active_character
					ON CONFLICT (character_id, slot) 
					DO UPDATE SET 
						template_id = EXCLUDED.template_id,
						seed = EXCLUDED.seed,
						amount = EXCLUDED.amount
					RETURNING id, character_id, template_id, slot, seed, amount, time_created",
					item.CharacterID,
					item.TemplateID,
					item.Slot,
					item.Seed,
					item.Amount)
						.AsNoTracking()
						.FirstOrDefaultAsync(ct);

				if (result == null)
				{
					throw new DatabaseEntityNotFoundException("Character", item.CharacterID.ToString());
				}

				return result.ID;
			}, "SaveBankItem", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveBankItemsAsync(IEnumerable<CharacterBankData> items, CancellationToken cancellationToken = default)
		{
			var itemList = items?.ToList();
			if (itemList == null || itemList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"No items to save. Items collection must not be null or empty.");
			}

			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (itemList.Count > 1)
			{
				var deduped = new Dictionary<(long CharacterID, int Slot), CharacterBankData>();
				foreach (var item in itemList)
				{
					deduped[(item.CharacterID, item.Slot)] = item;
				}

				if (deduped.Count != itemList.Count)
				{
					itemList = deduped.Values.ToList();
				}
			}

			// Extract arrays for bulk UPSERT
			var characterIds = itemList.Select(i => i.CharacterID).ToArray();
			var templateIds = itemList.Select(i => i.TemplateID).ToArray();
			var slots = itemList.Select(i => i.Slot).ToArray();
			var seeds = itemList.Select(i => i.Seed).ToArray();
			var amounts = itemList.Select(i => (int)i.Amount).ToArray();

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
					INSERT INTO {TableName} (character_id, template_id, slot, seed, amount, time_created)
					SELECT u.character_id, u.template_id, u.slot, u.seed, u.amount, CURRENT_TIMESTAMP
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::int[],
						{{2}}::int[],
						{{3}}::int[],
						{{4}}::int[]
					) AS u(character_id, template_id, slot, seed, amount)
					JOIN active_characters ac ON ac.id = u.character_id
					ON CONFLICT (character_id, slot) DO UPDATE SET
						template_id = EXCLUDED.template_id,
						seed = EXCLUDED.seed,
						amount = EXCLUDED.amount",
					new object[] { characterIds, templateIds, slots, seeds, amounts },
					ct);
			}, "SaveBankItems", cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteBankItemsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			var result = await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {{0}}",
				"DeleteBankItems",
				new object[] { characterId },
				entityName: "CharacterBankItem",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteBankSlotAsync(long characterId, int slot, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			var result = await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {{0}} AND slot = {{1}}",
				"DeleteBankSlot",
				new object[] { characterId, slot },
				entityName: "CharacterBankItem",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterBankData>>> GetBankItemsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterBankData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				var entities = await GetBankItemsQuery(dbContext, characterId, ct);
				var items = entities.Select(i => new CharacterBankData(
					id: i.ID,
					characterID: i.CharacterID,
					templateID: i.TemplateID,
					slot: i.Slot,
					seed: i.Seed,
					amount: i.Amount
				)).ToList();

				return (IReadOnlyList<CharacterBankData>)items;
			}, "GetBankItems", cancellationToken);
		}
	}
}