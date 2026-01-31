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
		/// Compiled query for retrieving character bank items (hot path for bank access).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterBankEntity>>> getBankItemsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterBankItems
					.AsNoTracking()
					.Where(i => i.CharacterID == characterId && !i.Deleted)
					.ToList());

		/// <summary>
		/// Compiled query for retrieving a tracked bank item by character ID and slot.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, int, CancellationToken, Task<CharacterBankEntity?>> getByCharacterAndSlotTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, int slot, CancellationToken ct) =>
				(CharacterBankEntity?)context.CharacterBankItems
					.FirstOrDefault(i => i.CharacterID == characterId && i.Slot == slot));

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
			if (item.CharacterID <= 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			var insertResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, item.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", item.CharacterID.ToString());
				}

				var entity = new CharacterBankEntity
				{
					CharacterID = item.CharacterID,
					Version = item.Version,
					TemplateID = item.TemplateID,
					Slot = item.Slot,
					Seed = item.Seed,
					Amount = item.Amount,
					TimeCreated = DateTime.UtcNow
				};
				await dbContext.CharacterBankItems.AddAsync(entity, cancellationToken).ConfigureAwait(false);
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

			var updateResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, item.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", item.CharacterID.ToString());
				}

				var entity = await getByCharacterAndSlotTrackingQuery(dbContext, item.CharacterID, item.Slot, cancellationToken).ConfigureAwait(false);
				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("CharacterBankItem", $"(CharacterID: {item.CharacterID}, Slot: {item.Slot})");
				}

				ValidateVersion(entity, item.Version);
				if (entity.Deleted)
				{
					entity.Deleted = false;
					entity.TimeDeleted = null;
				}

				entity.TemplateID = item.TemplateID;
				entity.Seed = item.Seed;
				entity.Amount = item.Amount;
				return entity;
			}).ConfigureAwait(false);

			return updateResult.IsSuccess
				? DatabaseResult<long>.Success(updateResult.Data.ID)
				: DatabaseResult<long>.Failure(updateResult.ErrorCode, updateResult.ErrorMessage, updateResult.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveBankItemsAsync(IEnumerable<CharacterBankData> items, CancellationToken cancellationToken = default)
		{
			var itemList = items?.ToList();
			if (itemList == null || itemList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"No items to save. Items collection must not be null or empty.",
					isTransient: false);
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

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var previousAutoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
				try
				{
					dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

					var characterIds = itemList.Select(i => i.CharacterID).Distinct().ToArray();
					var activeCharacterIds = await dbContext.Characters
						.AsNoTracking()
						.Where(c => characterIds.Contains(c.ID) && !c.Deleted)
						.Select(c => c.ID)
						.ToListAsync(cancellationToken)
						.ConfigureAwait(false);
					var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);

					var slotList = itemList.Select(i => i.Slot).Distinct().ToArray();
					var desiredKeys = new HashSet<(long CharacterID, int Slot)>(itemList.Select(i => (i.CharacterID, i.Slot)));
					var existing = await dbContext.CharacterBankItems
						.Where(i => activeCharacterIdSet.Contains(i.CharacterID) && slotList.Contains(i.Slot))
						.ToListAsync(cancellationToken)
						.ConfigureAwait(false);

					var existingByKey = new Dictionary<(long CharacterID, int Slot), CharacterBankEntity>();
					foreach (var entity in existing)
					{
						var key = (entity.CharacterID, entity.Slot);
						if (!desiredKeys.Contains(key)) continue;
						existingByKey[key] = entity;
					}

					foreach (var item in itemList)
					{
						if (!activeCharacterIdSet.Contains(item.CharacterID)) continue;

						var key = (item.CharacterID, item.Slot);
						if (!existingByKey.TryGetValue(key, out var entity))
						{
							entity = new CharacterBankEntity
							{
								CharacterID = item.CharacterID,
								Version = item.Version,
								Slot = item.Slot,
								TimeCreated = DateTime.UtcNow
							};
							await dbContext.CharacterBankItems.AddAsync(entity, cancellationToken).ConfigureAwait(false);
							existingByKey[key] = entity;
						}

						ValidateVersion(entity, item.Version);
						if (entity.Deleted)
						{
							entity.Deleted = false;
							entity.TimeDeleted = null;
						}

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
		public async Task<DatabaseResult> DeleteBankItemsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var itemIds = await dbContext.CharacterBankItems
					.AsNoTracking()
					.Where(i => i.CharacterID == characterId && !i.Deleted)
					.Select(i => i.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				foreach (var itemId in itemIds)
				{
					var entity = new CharacterBankEntity { ID = itemId, Deleted = true, TimeDeleted = now };
					dbContext.Attach(entity);
					dbContext.Entry(entity).Property(e => e.Deleted).IsModified = true;
					dbContext.Entry(entity).Property(e => e.TimeDeleted).IsModified = true;
				}
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteBankSlotAsync(long characterId, int slot, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var itemIds = await dbContext.CharacterBankItems
					.AsNoTracking()
					.Where(i => i.CharacterID == characterId && i.Slot == slot && !i.Deleted)
					.Select(i => i.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				foreach (var itemId in itemIds)
				{
					var entity = new CharacterBankEntity { ID = itemId, Deleted = true, TimeDeleted = now };
					dbContext.Attach(entity);
					dbContext.Entry(entity).Property(e => e.Deleted).IsModified = true;
					dbContext.Entry(entity).Property(e => e.TimeDeleted).IsModified = true;
				}
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterBankData>>> GetBankItemsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterBankData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getBankItemsQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				var items = entities.Select(i => new CharacterBankData(
					id: i.ID,
					version: i.Version,
					characterID: i.CharacterID,
					templateID: i.TemplateID,
					slot: i.Slot,
					seed: i.Seed,
					amount: i.Amount
				)).ToList();

				return (IReadOnlyList<CharacterBankData>)items;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}