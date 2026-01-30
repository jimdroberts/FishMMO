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
		/// Compiled query for checking whether a character exists and is not deleted.
		/// Returns the character ID if active, otherwise 0.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<long>> getActiveCharacterIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.Where(c => c.ID == characterId && !c.Deleted)
					.Select(c => c.ID)
					.FirstOrDefault());

		/// <summary>
		/// Compiled query for retrieving character equipment (hot path for character rendering).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterEquipmentEntity>>> getEquipmentQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterEquippedItems
					.AsNoTracking()
					.Where(e => e.CharacterID == characterId)
					.ToList());

		/// <summary>
		/// Compiled query for retrieving a tracked equipment item by character ID and slot.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, int, CancellationToken, Task<CharacterEquipmentEntity?>> getByCharacterAndSlotTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, int slot, CancellationToken ct) =>
				(CharacterEquipmentEntity?)context.CharacterEquippedItems
					.FirstOrDefault(e => e.CharacterID == characterId && e.Slot == slot));

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
			if (equipment.CharacterID <= 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			var insertResult = await ExecuteMirrorAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, equipment.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", equipment.CharacterID.ToString());
				}

				var entity = new CharacterEquipmentEntity
				{
					CharacterID = equipment.CharacterID,
					Version = equipment.Version,
					TemplateID = equipment.TemplateID,
					Slot = equipment.Slot,
					Seed = equipment.Seed,
					Amount = equipment.Amount,
					TimeCreated = DateTime.UtcNow
				};
				await dbContext.CharacterEquippedItems.AddAsync(entity, cancellationToken).ConfigureAwait(false);
				return entity;
			}).ConfigureAwait(false);

			if (insertResult.IsSuccess)
			{
				return DatabaseResult<long>.Success(insertResult.Data.ID);
			}

			if (insertResult.ErrorCode != "UNIQUE_VIOLATION")
			{
				return DatabaseResult<long>.Failure(insertResult.ErrorCode, insertResult.ErrorMessage, insertResult.IsTransient);
			}

			var updateResult = await ExecuteMirrorAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, equipment.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", equipment.CharacterID.ToString());
				}

				var entity = await getByCharacterAndSlotTrackingQuery(dbContext, equipment.CharacterID, equipment.Slot, cancellationToken).ConfigureAwait(false);
				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("CharacterEquipment", $"(CharacterID: {equipment.CharacterID}, Slot: {equipment.Slot})");
				}

				ValidateVersion(entity, equipment.Version);
				if (equipment.Version > 0) entity.Version = equipment.Version;

				entity.TemplateID = equipment.TemplateID;
				entity.Seed = equipment.Seed;
				entity.Amount = equipment.Amount;
				return entity;
			}).ConfigureAwait(false);

			return updateResult.IsSuccess
				? DatabaseResult<long>.Success(updateResult.Data.ID)
				: DatabaseResult<long>.Failure(updateResult.ErrorCode, updateResult.ErrorMessage, updateResult.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveEquipmentMultipleAsync(IEnumerable<CharacterEquipmentData> equipment, CancellationToken cancellationToken = default)
		{
			var equipmentList = equipment?.ToList();
			if (equipmentList == null || equipmentList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Empty or null equipment collection.",
					isTransient: false);
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

			return await ExecuteMirrorAsync(async dbContext =>
			{
				var previousAutoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
				try
				{
					dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

					var characterIds = equipmentList.Select(e => e.CharacterID).Distinct().ToArray();
					var activeCharacterIds = await dbContext.Characters
						.AsNoTracking()
						.Where(c => characterIds.Contains(c.ID) && !c.Deleted)
						.Select(c => c.ID)
						.ToListAsync(cancellationToken)
						.ConfigureAwait(false);
					var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);

					var slotList = equipmentList.Select(e => e.Slot).Distinct().ToArray();
					var desiredKeys = new HashSet<(long CharacterID, int Slot)>(equipmentList.Select(e => (e.CharacterID, e.Slot)));
					var existing = await dbContext.CharacterEquippedItems
						.Where(e => activeCharacterIdSet.Contains(e.CharacterID) && slotList.Contains(e.Slot))
						.ToListAsync(cancellationToken)
						.ConfigureAwait(false);

					var existingByKey = new Dictionary<(long CharacterID, int Slot), CharacterEquipmentEntity>();
					foreach (var entity in existing)
					{
						var key = (entity.CharacterID, entity.Slot);
						if (!desiredKeys.Contains(key)) continue;
						existingByKey[key] = entity;
					}

					foreach (var item in equipmentList)
					{
						if (!activeCharacterIdSet.Contains(item.CharacterID)) continue;

						var key = (item.CharacterID, item.Slot);
						if (!existingByKey.TryGetValue(key, out var entity))
						{
							entity = new CharacterEquipmentEntity
							{
								CharacterID = item.CharacterID,
								Version = item.Version,
								Slot = item.Slot,
								TimeCreated = DateTime.UtcNow
							};
							await dbContext.CharacterEquippedItems.AddAsync(entity, cancellationToken).ConfigureAwait(false);
							existingByKey[key] = entity;
						}

						ValidateVersion(entity, item.Version);
						if (item.Version > 0) entity.Version = item.Version;

						entity.TemplateID = item.TemplateID;
						entity.Seed = item.Seed;
						entity.Amount = item.Amount;
					}
				}
				finally
				{
					dbContext.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetectChanges;
				}
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteEquipmentAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteMirrorAsync(async dbContext =>
			{
				var equipmentIds = await dbContext.CharacterEquippedItems
					.AsNoTracking()
					.Where(e => e.CharacterID == characterId)
					.Select(e => e.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				foreach (var equipmentId in equipmentIds)
				{
					var entity = new CharacterEquipmentEntity { ID = equipmentId };
					dbContext.CharacterEquippedItems.Remove(entity);
				}
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteEquipmentSlotAsync(long characterId, int slot, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteMirrorAsync(async dbContext =>
			{
				var equipmentIds = await dbContext.CharacterEquippedItems
					.AsNoTracking()
					.Where(e => e.CharacterID == characterId && e.Slot == slot)
					.Select(e => e.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				foreach (var equipmentId in equipmentIds)
				{
					var entity = new CharacterEquipmentEntity { ID = equipmentId };
					dbContext.CharacterEquippedItems.Remove(entity);
				}
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterEquipmentData>>> GetEquipmentAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterEquipmentData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteMirrorAsync(async dbContext =>
			{
				var entities = await getEquipmentQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				var equipment = entities.Select(e => new CharacterEquipmentData(
					id: e.ID,
					version: e.Version,
					characterID: e.CharacterID,
					templateID: e.TemplateID,
					slot: e.Slot,
					seed: e.Seed,
					amount: e.Amount
				)).ToList();

				return (IReadOnlyList<CharacterEquipmentData>)equipment;
			}).ConfigureAwait(false);
		}
	}
}