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
	/// Service for ranked arena seasons and ratings. See <see cref="IArenaRatingService"/>.
	/// </summary>
	public sealed class ArenaRatingService : BaseService<ArenaRatingEntity>, IArenaRatingService
	{
		private const int MaxBatchIds = 1024;
		private const int DefaultRating = 1500;

		/// <summary>
		/// Initializes a new instance of ArenaRatingService.
		/// </summary>
		public ArenaRatingService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ArenaSeasonData>> GetOrCreateActiveSeasonAsync(CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				string seasonTable = dbContext.GetTableName<ArenaSeasonEntity>();

				/* The partial unique index on active makes two servers racing to create the first
				 * season safe: the loser's insert is a no-op (ON CONFLICT on the index) and the
				 * following read returns the winner's row. */
				var insertSql = $@"INSERT INTO {seasonTable} (name, starts_utc, ends_utc, active, time_created)
					SELECT 'Season 1', {{0}}, NULL, TRUE, {{0}}
					WHERE NOT EXISTS (SELECT 1 FROM {seasonTable} WHERE active = TRUE)
					ON CONFLICT DO NOTHING";

				await dbContext.Database.ExecuteSqlRawAsync(insertSql, new object[] { now }, cancellationToken).ConfigureAwait(false);

				var rows = await dbContext.ArenaSeasons
					.FromSqlRaw($@"SELECT * FROM {seasonTable} WHERE active = TRUE LIMIT 1")
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				if (rows.Count == 0)
				{
					throw new DatabaseException("No active arena season after creating one.", errorCode: DatabaseErrorCodes.StaleState);
				}

				return MapSeason(rows[0]);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ArenaSeasonData>> StartNewSeasonAsync(string name, DateTime? endsUtc, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return DatabaseResult<ArenaSeasonData>.Failure(DatabaseErrorCodes.ValidationError, "Season name is required.");
			}

			var now = DateTime.UtcNow;

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				string seasonTable = dbContext.GetTableName<ArenaSeasonEntity>();

				await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {seasonTable} SET active = FALSE, ends_utc = COALESCE(ends_utc, {{0}}) WHERE active = TRUE",
					new object[] { now },
					cancellationToken).ConfigureAwait(false);

				var rows = await dbContext.ArenaSeasons
					.FromSqlRaw(
						$@"INSERT INTO {seasonTable} (name, starts_utc, ends_utc, active, time_created)
						VALUES ({{0}}, {{1}}, {{2}}, TRUE, {{1}})
						RETURNING *",
						name.Trim(), now, (object)endsUtc ?? DBNull.Value)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapSeason(rows[0]);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<ArenaRatingData>>> FetchRatingsAsync(long seasonId, IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default)
		{
			if (seasonId <= 0)
			{
				return DatabaseResult<IReadOnlyList<ArenaRatingData>>.Failure(DatabaseErrorCodes.ValidationError, "Season ID must be greater than zero.");
			}

			long[] ids = Distinct(characterIds);
			if (ids.Length == 0)
			{
				return DatabaseResult<IReadOnlyList<ArenaRatingData>>.Success(Array.Empty<ArenaRatingData>());
			}

			var result = await ExecuteReadAsync<IReadOnlyList<ArenaRatingData>>(async dbContext =>
			{
				var rows = await dbContext.ArenaRatings
					.FromSqlRaw($@"SELECT * FROM {TableName} WHERE season_id = {{0}} AND character_id = ANY({{1}})", seasonId, ids)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return rows.Select(MapRating).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> UpsertRatingsAsync(long seasonId, IReadOnlyList<(long characterId, int newRating, bool won)> results, CancellationToken cancellationToken = default)
		{
			if (seasonId <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Season ID must be greater than zero.");
			}

			if (results == null || results.Count == 0)
			{
				return DatabaseResult<int>.Success(0);
			}

			int count = Math.Min(results.Count, MaxBatchIds);
			var characters = new long[count];
			var ratings = new int[count];
			var wins = new int[count];
			var losses = new int[count];
			for (int i = 0; i < count; ++i)
			{
				characters[i] = results[i].characterId;
				ratings[i] = results[i].newRating;
				wins[i] = results[i].won ? 1 : 0;
				losses[i] = results[i].won ? 0 : 1;
			}

			var now = DateTime.UtcNow;

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"INSERT INTO {TableName} (season_id, character_id, rating, peak_rating, games, wins, losses, last_updated)
					SELECT {{0}}, v.character_id, v.rating, GREATEST(v.rating, {{5}}), 1, v.wins, v.losses, {{6}}
					FROM UNNEST({{1}}, {{2}}, {{3}}, {{4}}) AS v(character_id, rating, wins, losses)
					ON CONFLICT (season_id, character_id) DO UPDATE
					SET rating = EXCLUDED.rating,
						peak_rating = GREATEST({TableName}.peak_rating, EXCLUDED.rating),
						games = {TableName}.games + 1,
						wins = {TableName}.wins + EXCLUDED.wins,
						losses = {TableName}.losses + EXCLUDED.losses,
						last_updated = EXCLUDED.last_updated";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { seasonId, characters, ratings, wins, losses, DefaultRating, now },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<ArenaRatingData>>> FetchTopAsync(long seasonId, int limit, CancellationToken cancellationToken = default)
		{
			if (seasonId <= 0)
			{
				return DatabaseResult<IReadOnlyList<ArenaRatingData>>.Failure(DatabaseErrorCodes.ValidationError, "Season ID must be greater than zero.");
			}

			limit = Math.Clamp(limit, 1, 200);

			var result = await ExecuteReadAsync<IReadOnlyList<ArenaRatingData>>(async dbContext =>
			{
				// Games > 0 keeps a freshly-inserted default row out of the board; ties break by fewer games.
				var rows = await dbContext.ArenaRatings
					.FromSqlRaw($@"SELECT * FROM {TableName} WHERE season_id = {{0}} AND games > 0 ORDER BY rating DESC, games ASC, id ASC LIMIT {{1}}", seasonId, limit)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return rows.Select(MapRating).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

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

		private static ArenaSeasonData MapSeason(ArenaSeasonEntity e)
		{
			return new ArenaSeasonData(e.ID, e.Name, e.StartsUtc, e.EndsUtc, e.Active);
		}

		private static ArenaRatingData MapRating(ArenaRatingEntity e)
		{
			return new ArenaRatingData(e.SeasonID, e.CharacterID, e.Rating, e.PeakRating, e.Games, e.Wins, e.Losses);
		}
	}
}
