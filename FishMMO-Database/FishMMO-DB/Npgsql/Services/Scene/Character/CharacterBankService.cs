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
	/// Character bank service with async operations, atomic SQL, and DTO pattern.
	/// Uses repository pattern with EF Core and raw SQL for race-condition-prone operations.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// Follows SOLID principles: SRP, OCP, LSP, ISP, DIP.
	/// </summary>
	/// <remarks>
	/// All methods that use ExecuteSqlInterpolatedAsync are wrapped in execution strategies
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

			return await ExecuteWithStrategyAsync<long>(async dbContext =>
			{
				// Use PostgreSQL UPSERT for atomic insert-or-update
				var result = await dbContext.CharacterBankItems
					.FromSqlInterpolated($@"
					INSERT INTO {TableName}
						(character_id, template_id, slot, seed, amount)
					VALUES 
						({item.CharacterID}, {item.TemplateID}, {item.Slot}, {item.Seed}, {item.Amount})
					ON CONFLICT (character_id, slot) 
					DO UPDATE SET 
						template_id = EXCLUDED.template_id,
						seed = EXCLUDED.seed,
						amount = EXCLUDED.amount
					RETURNING id, character_id, template_id, slot, seed, amount")
						.AsNoTracking()
						.FirstOrDefaultAsync(cancellationToken);

				ValidateEntityExists(result, "CharacterBankItem", $"{item.CharacterID}:{item.Slot}");

				return result!.ID;
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

			// Extract arrays for bulk UPSERT
			var characterIds = itemList.Select(i => i.CharacterID).ToArray();
			var templateIds = itemList.Select(i => i.TemplateID).ToArray();
			var slots = itemList.Select(i => i.Slot).ToArray();
			var seeds = itemList.Select(i => i.Seed).ToArray();
			var amounts = itemList.Select(i => (int)i.Amount).ToArray();

			// Single bulk UPSERT using UNNEST - atomic operation, no transaction needed
			var result = await ExecuteSqlAsync(
				$@"INSERT INTO {TableName} (character_id, template_id, slot, seed, amount)
				SELECT * FROM UNNEST(
					{characterIds}::bigint[],
					{templateIds}::int[],
					{slots}::int[],
					{seeds}::int[],
					{amounts}::int[]
				)
				ON CONFLICT (character_id, slot) DO UPDATE SET
					template_id = EXCLUDED.template_id,
					seed = EXCLUDED.seed,
					amount = EXCLUDED.amount",
				"SaveBankItems",
				entityName: "CharacterBankItem",
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

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

			var result = await ExecuteSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {characterId}",
				"DeleteBankItems",
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

			var result = await ExecuteSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {characterId} AND slot = {slot}",
				"DeleteBankSlot",
				entityName: "CharacterBankItem",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterBankData>>> GetBankItemsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				var entities = await GetBankItemsQuery(dbContext, characterId, cancellationToken);
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