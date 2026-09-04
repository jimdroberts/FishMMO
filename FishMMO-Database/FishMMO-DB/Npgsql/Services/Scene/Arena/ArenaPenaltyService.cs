using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service for arena queue locks. See <see cref="IArenaPenaltyService"/>.
	/// </summary>
	public sealed class ArenaPenaltyService : BaseService<ArenaPenaltyEntity>, IArenaPenaltyService
	{
		private const int MaxBatchIds = 1024;

		/// <summary>
		/// Initializes a new instance of ArenaPenaltyService.
		/// </summary>
		public ArenaPenaltyService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<ArenaPenaltyData>>> FetchActiveAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default)
		{
			long[] ids = Distinct(characterIds);
			if (ids.Length == 0)
			{
				return DatabaseResult<IReadOnlyList<ArenaPenaltyData>>.Success(Array.Empty<ArenaPenaltyData>());
			}

			var now = DateTime.UtcNow;

			var result = await ExecuteReadAsync<IReadOnlyList<ArenaPenaltyData>>(async dbContext =>
			{
				var rows = await dbContext.ArenaPenalties
					.FromSqlRaw($@"SELECT * FROM {TableName} WHERE character_id = ANY({{0}}) AND locked_until_utc > {{1}}", ids, now)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return rows.Select(e => new ArenaPenaltyData(e.CharacterID, e.LockedUntilUtc, e.Reason)).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> SetAsync(long characterId, DateTime lockedUntilUtc, string reason, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than zero.");
			}

			reason = string.IsNullOrWhiteSpace(reason) ? "Deserter" : reason.Trim();
			if (reason.Length > 128)
			{
				reason = reason.Substring(0, 128);
			}

			var now = DateTime.UtcNow;

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				// The later of the two locks stands, so a second desertion cannot shorten the first.
				var sql = $@"INSERT INTO {TableName} (character_id, locked_until_utc, reason, time_created)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}})
					ON CONFLICT (character_id) DO UPDATE
					SET locked_until_utc = GREATEST({TableName}.locked_until_utc, EXCLUDED.locked_until_utc),
						reason = EXCLUDED.reason";

				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { characterId, lockedUntilUtc, reason, now }, cancellationToken).ConfigureAwait(false);
				return true;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> ClearAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than zero.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				int affected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"DELETE FROM {TableName} WHERE character_id = {{0}}",
					new object[] { characterId },
					cancellationToken).ConfigureAwait(false);
				return affected > 0;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		private static long[] Distinct(IReadOnlyList<long> ids)
		{
			if (ids == null || ids.Count == 0)
			{
				return Array.Empty<long>();
			}

			var seen = new HashSet<long>();
			var result = new List<long>(Math.Min(ids.Count, MaxBatchIds));
			for (int i = 0; i < ids.Count && result.Count < MaxBatchIds; ++i)
			{
				if (ids[i] > 0 && seen.Add(ids[i]))
				{
					result.Add(ids[i]);
				}
			}
			return result.ToArray();
		}
	}
}
