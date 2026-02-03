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
	/// Character equipment service with async operations, atomic SQL, and DTO pattern.
	/// Uses repository pattern with EF Core and raw SQL for race-condition-prone operations.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// Follows SOLID principles: SRP, OCP, LSP, ISP, DIP.
	/// </summary>
	/// <remarks>
	/// This service manages character equipped-item storage including:
	/// - Single-item save/update via atomic UPSERT (INSERT ... ON CONFLICT DO UPDATE)
	/// - Batch save/update via UNNEST + UPSERT
	/// - Soft-delete operations (bulk and slot-specific)
	/// - Retrieval of current equipment
	/// 
	/// Write operations are executed inside the BaseService execution wrappers for retry and exception mapping.
	/// Version/authority semantics are enforced in UPSERT via <see cref="BaseService{TEntity}.ExecuteBulkUpsertAsync"/>.
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
					.Where(e => e.CharacterID == characterId && !e.Deleted)
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
		public async Task<DatabaseResult<long>> PersistAsync(CharacterEquipmentData equipment, CancellationToken cancellationToken = default)
		{
			if (equipment.CharacterID <= 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			if (equipment.Version <= 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Invalid Version. Version must be greater than 0.",
					isTransient: false);
			}

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, equipment.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", equipment.CharacterID.ToString());
				}

				var now = DateTime.UtcNow;
				await ExecuteBulkUpsertAsync(
					dbContext,
					GetUpsertSql(),
					expectedRowsAffected: 1,
					new object[]
					{
						new[] { equipment.CharacterID },
						new[] { equipment.Slot },
						new[] { equipment.Version },
						new[] { equipment.TemplateID },
						new[] { equipment.Seed },
						new[] { equipment.Amount },
						now,
					},
					"Equipment item was rejected due to a stale Version.",
					cancellationToken).ConfigureAwait(false);

				var id = await dbContext.CharacterEquippedItems
					.AsNoTracking()
					.Where(e => e.CharacterID == equipment.CharacterID && e.Slot == equipment.Slot && !e.Deleted)
					.Select(e => e.ID)
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				if (id <= 0)
				{
					throw new DatabaseEntityNotFoundException("CharacterEquipment", $"(CharacterID: {equipment.CharacterID}, Slot: {equipment.Slot})");
				}

				return id;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<long>.Success(result.Data)
				: DatabaseResult<long>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(IEnumerable<CharacterEquipmentData> equipment, CancellationToken cancellationToken = default)
		{
			var equipmentList = equipment?.ToList();
			if (equipmentList == null || equipmentList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Empty or null equipment collection.",
					isTransient: false);
			}

			if (equipmentList.Any(e => e.Version <= 0))
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"One or more equipment items had an invalid Version. Version must be greater than 0.",
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

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var characterIds = equipmentList.Select(e => e.CharacterID).Distinct().ToArray();
				var activeCharacterIds = await dbContext.Characters
					.AsNoTracking()
					.Where(c => characterIds.Contains(c.ID) && !c.Deleted)
					.Select(c => c.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);

				var activeEquipment = equipmentList.Where(e => activeCharacterIdSet.Contains(e.CharacterID)).ToList();
				if (activeEquipment.Count == 0)
				{
					return;
				}

				var now = DateTime.UtcNow;
				var characterIdArray = activeEquipment.Select(e => e.CharacterID).ToArray();
				var slotArray = activeEquipment.Select(e => e.Slot).ToArray();
				var versionArray = activeEquipment.Select(e => e.Version).ToArray();
				var templateIdArray = activeEquipment.Select(e => e.TemplateID).ToArray();
				var seedArray = activeEquipment.Select(e => e.Seed).ToArray();
				var amountArray = activeEquipment.Select(e => e.Amount).ToArray();

				var sql = GetUpsertSql();

				await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeEquipment.Count,
					new object[] { characterIdArray, slotArray, versionArray, templateIdArray, seedArray, amountArray, now },
					"One or more equipment items were rejected due to a stale Version.",
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private string GetUpsertSql()
		{
			return $@"
				INSERT INTO {TableName}
					(character_id, slot, version, template_id, seed, amount, time_created, deleted, time_deleted)
				SELECT
					u.character_id,
					u.slot,
					u.version,
					u.template_id,
					u.seed,
					u.amount,
					{{6}},
					FALSE,
					NULL
				FROM UNNEST(
					{{0}}::bigint[],
					{{1}}::integer[],
					{{2}}::bigint[],
					{{3}}::integer[],
					{{4}}::integer[],
					{{5}}::integer[]
				) AS u(character_id, slot, version, template_id, seed, amount)
				ON CONFLICT (character_id, slot)
				DO UPDATE SET
					template_id = EXCLUDED.template_id,
					seed = EXCLUDED.seed,
					amount = EXCLUDED.amount,
					deleted = FALSE,
					time_deleted = NULL,
					version = EXCLUDED.version
				WHERE
					EXCLUDED.version > {TableName}.version;";
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
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
					var anyActive = await dbContext.CharacterEquippedItems
						.AsNoTracking()
						.AnyAsync(e => e.CharacterID == characterId && !e.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Equipment delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, int slot, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
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
					WHERE character_id = {{2}} AND slot = {{3}} AND deleted = FALSE AND version < {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, incomingVersion, characterId, slot }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var stillActive = await dbContext.CharacterEquippedItems
						.AsNoTracking()
						.AnyAsync(e => e.CharacterID == characterId && e.Slot == slot && !e.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (stillActive)
					{
						throw new StaleStateException("Equipment slot delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterEquipmentData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterEquipmentData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
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
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}