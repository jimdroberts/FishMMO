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
	/// Service for managing character item cooldowns in the database.
	/// Provides async operations for CRUD operations on character item cooldown data.
	/// Uses the BaseService execution strategy for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	public sealed class CharacterItemCooldownService : BaseService<CharacterItemCooldownEntity>, ICharacterItemCooldownService
	{
		/// <summary>
		/// Compiled query for retrieving character item cooldowns (hot path for character load).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, IAsyncEnumerable<CharacterItemCooldownEntity>> getCooldownsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId) =>
				context.CharacterItemCooldowns
					.AsNoTracking()
					.Where(c => c.CharacterID == characterId && !c.Deleted));

		/// <summary>
		/// Compiled query for counting character item cooldowns.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> getCountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterItemCooldowns
					.AsNoTracking()
					.Where(c => c.CharacterID == characterId && !c.Deleted)
					.Count());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterItemCooldownService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterItemCooldownService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
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
		public async Task<DatabaseResult<long>> PersistAsync(CharacterItemCooldownData cooldownData, CancellationToken cancellationToken = default)
		{
			if (cooldownData.CharacterID <= 0)
			{
				return DatabaseResult<long>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			if (cooldownData.Version <= 0)
			{
				return DatabaseResult<long>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid version. Version must be greater than 0.");
			}

			var result = await ExecuteTransactionAsync<long>(async dbContext =>
			{
				var isCharacterActive = await dbContext.Characters
					.AsNoTracking()
					.AnyAsync(c => c.ID == cooldownData.CharacterID && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (!isCharacterActive)
				{
					throw new DatabaseEntityNotFoundException("Character", cooldownData.CharacterID.ToString());
				}

				var now = DateTime.UtcNow;
				var sql = $@"
					WITH upserted AS (
						INSERT INTO {TableName}
							(character_id, category, version, cooldown_end, time_created, deleted, time_deleted)
						VALUES
							({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, FALSE, NULL)
						ON CONFLICT (character_id, category)
						DO UPDATE SET
							cooldown_end = EXCLUDED.cooldown_end,
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
					new object[] { cooldownData.CharacterID, cooldownData.Category, cooldownData.Version, cooldownData.CooldownEnd, now },
					cancellationToken).ConfigureAwait(false);

				if (id <= 0)
				{
					throw new StaleStateException("Item cooldown persist rejected due to a stale Version.");
				}

				return id;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(IEnumerable<CharacterItemCooldownData> cooldowns, CancellationToken cancellationToken = default)
		{
			if (cooldowns == null || !cooldowns.Any())
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Item cooldowns collection must not be null or empty.");
			}

			var list = cooldowns.ToList();
			if (list.Any(c => c.Version <= 0))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more item cooldowns had an invalid Version. Version must be greater than 0.");
			}

			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (list.Count > 1)
			{
				var deduped = new Dictionary<(long CharacterID, int Category), CharacterItemCooldownData>();
				foreach (var cooldown in list)
				{
					deduped[(cooldown.CharacterID, cooldown.Category)] = cooldown;
				}

				if (deduped.Count != list.Count)
				{
					list = deduped.Values.ToList();
				}
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var allCharacterIds = list.Select(c => c.CharacterID).Distinct().ToArray();
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

				var activeCooldowns = list.Where(c => activeCharacterIdSet.Contains(c.CharacterID)).ToList();
				if (activeCooldowns.Count == 0)
				{
					return;
				}

				var now = DateTime.UtcNow;
				var characterIdArray = activeCooldowns.Select(c => c.CharacterID).ToArray();
				var categoryArray = activeCooldowns.Select(c => c.Category).ToArray();
				var versionArray = activeCooldowns.Select(c => c.Version).ToArray();
				var cooldownEndArray = activeCooldowns.Select(c => c.CooldownEnd).ToArray();

				var sql = $@"
					INSERT INTO {TableName}
						(character_id, category, version, cooldown_end, time_created, deleted, time_deleted)
					SELECT
						u.character_id,
						u.category,
						u.version,
						u.cooldown_end,
						{{4}},
						FALSE,
						NULL
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::integer[],
						{{2}}::bigint[],
						{{3}}::double precision[]
					) AS u(character_id, category, version, cooldown_end)
					ON CONFLICT (character_id, category)
					DO UPDATE SET
						cooldown_end = EXCLUDED.cooldown_end,
						deleted = FALSE,
						time_deleted = NULL,
						version = EXCLUDED.version
					WHERE
						EXCLUDED.version > {TableName}.version;";

				await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeCooldowns.Count,
					new object[] { characterIdArray, categoryArray, versionArray, cooldownEndArray, now },
					"One or more item cooldowns were rejected due to a stale Version.",
					cancellationToken).ConfigureAwait(false);
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
					var anyActive = await dbContext.CharacterItemCooldowns
						.AsNoTracking()
						.AnyAsync(c => c.CharacterID == characterId && !c.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Item cooldown delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterItemCooldownData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterItemCooldownData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getCooldownsQuery(dbContext, characterId).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				var cooldowns = entities.Select(c => new CharacterItemCooldownData(
					id: c.ID,
					version: c.Version,
					characterID: c.CharacterID,
					category: c.Category,
					cooldownEnd: c.CooldownEnd
				)).ToList();

				return (IReadOnlyList<CharacterItemCooldownData>)cooldowns;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
