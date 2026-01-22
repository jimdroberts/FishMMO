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
	/// Character equipment service with async operations, atomic SQL, and DTO pattern.
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

			return await ExecuteSqlAsync<long>(async (dbContext, ct) =>
			{
				// Use PostgreSQL UPSERT for atomic insert-or-update
				var result = await dbContext.CharacterEquippedItems
					.FromSqlInterpolated($@"
					INSERT INTO {TableName}
						(character_id, template_id, slot, seed, amount)
					VALUES 
						({equipment.CharacterID}, {equipment.TemplateID}, {equipment.Slot}, {equipment.Seed}, {equipment.Amount})
					ON CONFLICT (character_id, slot) 
					DO UPDATE SET 
						template_id = EXCLUDED.template_id,
						seed = EXCLUDED.seed,
						amount = EXCLUDED.amount
					RETURNING id, character_id, template_id, slot, seed, amount")
					.AsNoTracking()
					.FirstOrDefaultAsync(ct);
				var existingEquipment = RequireEntityExists(result, "CharacterEquipment", $"{equipment.CharacterID}:{equipment.Slot}");
				return existingEquipment.ID;
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

			// Extract arrays for bulk UPSERT
			var characterIds = equipmentList.Select(e => e.CharacterID).ToArray();
			var templateIds = equipmentList.Select(e => e.TemplateID).ToArray();
			var slots = equipmentList.Select(e => e.Slot).ToArray();
			var seeds = equipmentList.Select(e => e.Seed).ToArray();
			var amounts = equipmentList.Select(e => (int)e.Amount).ToArray();

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
				"SaveEquipmentMultiple",
				entityName: "CharacterEquipment",
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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

			var result = await ExecuteSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {characterId}",
				"DeleteEquipment",
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

			var result = await ExecuteSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {characterId} AND slot = {slot}",
				"DeleteEquipmentSlot",
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

			return await ExecuteSqlAsync(async (dbContext, ct) =>
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