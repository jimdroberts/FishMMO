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
	/// Service for managing character pet buffs in the database.
	/// Provides async operations for CRUD operations on character pet buff data.
	/// Uses the BaseService execution strategy for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// Every DTO field maps to the column of the same name, and every column is written. This
	/// used to be a renaming layer: <c>Level</c> into <c>stacks</c>, <c>BuffTimeEnd</c> into
	/// <c>remaining_time</c>, with <c>tick_time</c> and <c>tick_count</c> hard-coded to zero as
	/// "reserved for future use". They were never reserved. They are the tick schedule and the
	/// accumulated tick count of a periodic effect, the player's own buff service has always
	/// written them, and zeroing them restored a damage-over-time effect on a pet with its
	/// cumulative progress erased and its next tick due immediately.
	/// </remarks>
	public sealed class CharacterPetBuffService : BaseService<CharacterPetBuffEntity>, ICharacterPetBuffService
	{
		/// <summary>
		/// Compiled query for retrieving character pet buffs (hot path for character load).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, IAsyncEnumerable<CharacterPetBuffEntity>> getBuffsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId) =>
				context.CharacterPetBuffs
					.AsNoTracking()
					.Where(b => b.CharacterID == characterId && !b.Deleted));

		/// <summary>
		/// Compiled query for counting character pet buffs.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> getCountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterPetBuffs
					.AsNoTracking()
					.Where(b => b.CharacterID == characterId && !b.Deleted)
					.Count());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterPetBuffService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterPetBuffService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> CountAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<int>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
				await getCountQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> PersistAsync(CharacterPetBuffData buffData, CancellationToken cancellationToken = default)
		{
			if (buffData.CharacterID <= 0)
			{
				return DatabaseResult<long>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			if (buffData.Version <= 0)
			{
				return DatabaseResult<long>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid version. Version must be greater than 0.");
			}

			var result = await ExecuteTransactionAsync<long>(async dbContext =>
			{
				var isCharacterActive = await dbContext.Characters
					.AsNoTracking()
					.AnyAsync(c => c.ID == buffData.CharacterID && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (!isCharacterActive)
				{
					throw new DatabaseEntityNotFoundException("Character", buffData.CharacterID.ToString());
				}

				var now = DateTime.UtcNow;
				var sql = $@"
					WITH upserted AS (
						INSERT INTO {TableName}
							(character_id, template_id, version, remaining_time, tick_time, stacks, tick_count, time_created, deleted, time_deleted)
						VALUES
							({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, {{7}}, FALSE, NULL)
						ON CONFLICT (character_id, template_id)
						DO UPDATE SET
							remaining_time = EXCLUDED.remaining_time,
							tick_time = EXCLUDED.tick_time,
							stacks = EXCLUDED.stacks,
							tick_count = EXCLUDED.tick_count,
							deleted = FALSE,
							time_deleted = NULL,
							version = EXCLUDED.version
						WHERE
							EXCLUDED.version > {TableName}.version
						RETURNING id
					)
					SELECT COALESCE((SELECT id FROM upserted LIMIT 1), 0)::bigint AS value";

				var id = await ExecuteScalarLongAsync(
					dbContext,
					sql,
					new object[] { buffData.CharacterID, buffData.TemplateID, buffData.Version, buffData.RemainingTime, buffData.TickTime, buffData.Stacks, buffData.TickCount, now },
					cancellationToken).ConfigureAwait(false);

				if (id <= 0)
				{
					throw new StaleStateException("Pet buff persist rejected due to a stale Version.");
				}

				return id;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<BulkWriteResult>> PersistAsync(IEnumerable<CharacterPetBuffData> buffs, CancellationToken cancellationToken = default)
		{
			if (buffs == null || !buffs.Any())
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Pet buffs collection must not be null or empty.");
			}

			var list = buffs.ToList();
			if (list.Any(b => b.Version <= 0))
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more pet buffs had an invalid Version. Version must be greater than 0.");
			}

			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (list.Count > 1)
			{
				var deduped = new Dictionary<(long CharacterID, int TemplateID), CharacterPetBuffData>();
				foreach (var buff in list)
				{
					deduped[(buff.CharacterID, buff.TemplateID)] = buff;
				}

				if (deduped.Count != list.Count)
				{
					list = deduped.Values.ToList();
				}
			}

			int suppliedRows = list.Count;

			return await ExecuteTransactionAsync<BulkWriteResult>(async dbContext =>
			{
				var allCharacterIds = list.Select(b => b.CharacterID).Distinct().ToArray();
				var activeCharacterIds = await dbContext.Characters
					.AsNoTracking()
					.Where(c => allCharacterIds.Contains(c.ID) && !c.Deleted)
					.Select(c => c.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);

				if (activeCharacterIdSet.Count != allCharacterIds.Length)
				{
					var missingCharacterId = allCharacterIds.First(id => !activeCharacterIdSet.Contains(id));
					throw new DatabaseEntityNotFoundException("Character", missingCharacterId.ToString(), "Character not found or deleted.");
				}

				var activeBuffs = list.Where(b => activeCharacterIdSet.Contains(b.CharacterID)).ToList();
				if (activeBuffs.Count == 0)
				{
					return new BulkWriteResult(suppliedRows, 0, 0);
				}

				var now = DateTime.UtcNow;
				var characterIdArray = activeBuffs.Select(b => b.CharacterID).ToArray();
				var templateIdArray = activeBuffs.Select(b => b.TemplateID).ToArray();
				var versionArray = activeBuffs.Select(b => b.Version).ToArray();
				var remainingTimeArray = activeBuffs.Select(b => b.RemainingTime).ToArray();
				var tickTimeArray = activeBuffs.Select(b => b.TickTime).ToArray();
				var stacksArray = activeBuffs.Select(b => b.Stacks).ToArray();
				var tickCountArray = activeBuffs.Select(b => b.TickCount).ToArray();

				var sql = $@"
					INSERT INTO {TableName}
						(character_id, template_id, version, remaining_time, tick_time, stacks, tick_count, time_created, deleted, time_deleted)
					SELECT
						u.character_id,
						u.template_id,
						u.version,
						u.remaining_time,
						u.tick_time,
						u.stacks,
						u.tick_count,
						{{7}},
						FALSE,
						NULL
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::integer[],
						{{2}}::bigint[],
						{{3}}::double precision[],
						{{4}}::double precision[],
						{{5}}::integer[],
						{{6}}::integer[]
					) AS u(character_id, template_id, version, remaining_time, tick_time, stacks, tick_count)
					ON CONFLICT (character_id, template_id)
					DO UPDATE SET
						remaining_time = EXCLUDED.remaining_time,
						tick_time = EXCLUDED.tick_time,
						stacks = EXCLUDED.stacks,
						tick_count = EXCLUDED.tick_count,
						deleted = FALSE,
						time_deleted = NULL,
						version = EXCLUDED.version
					WHERE
						EXCLUDED.version > {TableName}.version;";

				int appliedRows = await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeBuffs.Count,
					new object[] { characterIdArray, templateIdArray, versionArray, remainingTimeArray, tickTimeArray, stacksArray, tickCountArray, now },
					"One or more pet buffs were rejected due to a stale Version.",
					cancellationToken,
					BulkVersionConflictPolicy.SkipStaleRows).ConfigureAwait(false);

				return new BulkWriteResult(suppliedRows, activeBuffs.Count, appliedRows);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid version. Version must be greater than 0.");
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
					var anyActive = await dbContext.CharacterPetBuffs
						.AsNoTracking()
						.AnyAsync(b => b.CharacterID == characterId && !b.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Pet buff delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterPetBuffData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterPetBuffData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getBuffsQuery(dbContext, characterId).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				var buffs = entities.Select(b => new CharacterPetBuffData(
					id: b.ID,
					version: b.Version,
					characterID: b.CharacterID,
					templateID: b.TemplateID,
					remainingTime: b.RemainingTime,
					tickTime: b.TickTime,
					stacks: b.Stacks,
					tickCount: b.TickCount
				)).ToList();

				return (IReadOnlyList<CharacterPetBuffData>)buffs;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
