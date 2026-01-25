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
	/// Character equipment service with async operations, atomic SQL, and DTO pattern.
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
	public sealed class CharacterEquipmentService : BaseService<CharacterEquipmentEntity>, ICharacterEquipmentService
	{
		/// <summary>
		/// Compiled query for retrieving character equipment (hot path for character rendering).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterEquipmentEntity>>> GetEquipmentQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterEquippedItems
					.AsNoTracking()
					.Where(e => e.CharacterID == characterId)
					.ToList());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterEquipmentService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterEquipmentService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> SaveEquipmentAsync(CharacterEquipmentData equipment, CancellationToken cancellationToken = default)
		{
			if (equipment.CharacterID == 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteAsync<long>(async (dbContext, ct) =>
			{
				var charactersTableName = dbContext.GetTableName<CharacterEntity>();
				// Use PostgreSQL UPSERT for atomic insert-or-update
				var result = await dbContext.CharacterEquippedItems
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
					equipment.CharacterID,
					equipment.TemplateID,
					equipment.Slot,
					equipment.Seed,
					equipment.Amount)
					.AsNoTracking()
					.FirstOrDefaultAsync(ct);

				if (result == null)
				{
					throw new DatabaseEntityNotFoundException("Character", equipment.CharacterID.ToString());
				}

				return result.ID;
			}, "SaveEquipment", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveEquipmentMultipleAsync(IEnumerable<CharacterEquipmentData> equipment, CancellationToken cancellationToken = default)
		{
			var equipmentList = equipment?.ToList();
			if (equipmentList == null || equipmentList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Empty or null equipment collection.");
			}

			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (equipmentList.Count > 1)
			{
				var deduped = new Dictionary<(long CharacterID, int Slot), CharacterEquipmentData>();
				foreach (var item in equipmentList)
				{
					deduped[(item.CharacterID, item.Slot)] = item;
				}

				if (deduped.Count != equipmentList.Count)
				{
					equipmentList = deduped.Values.ToList();
				}
			}

			// Extract arrays for bulk UPSERT
			var characterIds = equipmentList.Select(e => e.CharacterID).ToArray();
			var templateIds = equipmentList.Select(e => e.TemplateID).ToArray();
			var slots = equipmentList.Select(e => e.Slot).ToArray();
			var seeds = equipmentList.Select(e => e.Seed).ToArray();
			var amounts = equipmentList.Select(e => (int)e.Amount).ToArray();

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
			}, "SaveEquipmentMultiple", cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteEquipmentAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			var result = await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {{0}}",
				"DeleteEquipment",
				new object[] { characterId },
				entityName: "CharacterEquipment",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteEquipmentSlotAsync(long characterId, int slot, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			var result = await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {{0}} AND slot = {{1}}",
				"DeleteEquipmentSlot",
				new object[] { characterId, slot },
				entityName: "CharacterEquipment",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterEquipmentData>>> GetEquipmentAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterEquipmentData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				var entities = await GetEquipmentQuery(dbContext, characterId, ct);
				var equipment = entities.Select(e => new CharacterEquipmentData(
					id: e.ID,
					characterID: e.CharacterID,
					templateID: e.TemplateID,
					slot: e.Slot,
					seed: e.Seed,
					amount: e.Amount
				)).ToList();

				return (IReadOnlyList<CharacterEquipmentData>)equipment;
			}, "GetEquipment", cancellationToken);
		}
	}
}