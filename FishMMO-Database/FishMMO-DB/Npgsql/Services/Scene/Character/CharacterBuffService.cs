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
					"No buffs to save. Buffs collection must not be null or empty.",
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

			return await ExecuteMirrorAsync(async dbContext =>
			{
				var characterIds = buffList.Select(b => b.CharacterID).Distinct().ToArray();
				var activeCharacterIds = await dbContext.Characters
					.AsNoTracking()
					.Where(c => characterIds.Contains(c.ID) && !c.Deleted)
					.Select(c => c.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);

				var templateIds = buffList.Select(b => b.TemplateID).Distinct().ToArray();
				var existing = await dbContext.CharacterBuffs
					.Where(b => activeCharacterIdSet.Contains(b.CharacterID) && templateIds.Contains(b.TemplateID))
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var existingByKey = new Dictionary<(long CharacterID, int TemplateID), CharacterBuffEntity>();
				foreach (var entity in existing)
				{
					existingByKey[(entity.CharacterID, entity.TemplateID)] = entity;
				}

				foreach (var buff in buffList)
				{
					if (!activeCharacterIdSet.Contains(buff.CharacterID)) continue;

					var key = (buff.CharacterID, buff.TemplateID);
					if (!existingByKey.TryGetValue(key, out var entity))
					{
						entity = new CharacterBuffEntity
						{
							CharacterID = buff.CharacterID,
							TemplateID = buff.TemplateID,
							Version = buff.Version,
							TimeCreated = DateTime.UtcNow
						};
						await dbContext.CharacterBuffs.AddAsync(entity, cancellationToken).ConfigureAwait(false);
						existingByKey[key] = entity;
					}

					ValidateVersion(entity, buff.Version);
					if (buff.Version > 0) entity.Version = buff.Version;
					entity.RemainingTime = buff.RemainingTime;
					entity.TickTime = buff.TickTime;
					entity.Stacks = buff.Stacks;
				}
			}).ConfigureAwait(false);
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

			return await ExecuteMirrorAsync(async dbContext =>
			{
				var buffIds = await dbContext.CharacterBuffs
					.AsNoTracking()
					.Where(b => b.CharacterID == characterId)
					.Select(b => b.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				foreach (var buffId in buffIds)
				{
					var entity = new CharacterBuffEntity { ID = buffId };
					dbContext.CharacterBuffs.Remove(entity);
				}
			}).ConfigureAwait(false);
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

			return await ExecuteMirrorAsync(async dbContext =>
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
			}).ConfigureAwait(false);
		}
	}
}