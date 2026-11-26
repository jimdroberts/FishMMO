using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Duel records and their history. See <see cref="IDuelRecordService"/>.
	/// </summary>
	public sealed class DuelRecordService : BaseService<DuelRecordEntity>, IDuelRecordService
	{
		/// <summary>Initializes a new instance of DuelRecordService.</summary>
		public DuelRecordService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<DuelResultData>> RecordResultAsync(Guid requestKey, long winnerId, long loserId, bool draw, short reason, int kFactor, CancellationToken cancellationToken = default)
		{
			if (requestKey == Guid.Empty)
			{
				return DatabaseResult<DuelResultData>.Failure(DatabaseErrorCodes.ValidationError, "A duel result needs a request key.");
			}
			if (winnerId <= 0 || loserId <= 0 || winnerId == loserId)
			{
				return DatabaseResult<DuelResultData>.Failure(DatabaseErrorCodes.ValidationError, "A duel result needs two different characters.");
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				string results = dbContext.GetTableName<DuelResultEntity>();
				DateTime now = DateTime.UtcNow;

				/* Both rows exist before either is locked: a first duel inserts at the default rating. ON CONFLICT DO
				 * NOTHING, so two servers inserting one character's first row both go on to the lock below. */
				await dbContext.Database.ExecuteSqlRawAsync(
					$@"INSERT INTO {TableName} (character_id, rating, peak_rating, wins, losses, draws, last_updated)
						VALUES ({{0}}, {{2}}, {{2}}, 0, 0, 0, {{3}}), ({{1}}, {{2}}, {{2}}, 0, 0, 0, {{3}})
						ON CONFLICT (character_id) DO NOTHING",
					new object[] { winnerId, loserId, DuelRating.DefaultRating, now },
					cancellationToken).ConfigureAwait(false);

				// Locked in id order, so two results that share a character cannot deadlock.
				List<DuelRecordData> locked = await ReadRowsAsync(dbContext,
					$@"SELECT character_id, rating, wins, losses, draws FROM {TableName}
						WHERE character_id = ANY({{0}}) ORDER BY character_id FOR UPDATE",
					new object[] { new[] { winnerId, loserId } },
					reader => new DuelRecordData(reader.GetInt64(0), reader.GetInt32(1), 0, reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)),
					cancellationToken).ConfigureAwait(false);

				DuelRecordData winner = Find(locked, winnerId);
				DuelRecordData loser = Find(locked, loserId);

				// A retry of a result that already committed: report the records as they stand.
				List<(int winnerChange, int loserChange)> earlier = await ReadRowsAsync(dbContext,
					$"SELECT winner_rating_change, loser_rating_change FROM {results} WHERE request_key = {{0}}",
					new object[] { requestKey },
					reader => (reader.GetInt32(0), reader.GetInt32(1)),
					cancellationToken).ConfigureAwait(false);
				if (earlier.Count > 0)
				{
					return new DuelResultData(
						With(winner, winner.Rating, earlier[0].winnerChange),
						With(loser, loser.Rating, earlier[0].loserChange));
				}

				(int winnerChange, int loserChange) = DuelRating.Changes(winner.Rating, loser.Rating, draw, kFactor);
				int winnerRating = winner.Rating + winnerChange;
				int loserRating = loser.Rating + loserChange;

				await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {TableName} SET
							rating = {{1}},
							peak_rating = GREATEST(peak_rating, {{1}}),
							wins = wins + {{2}},
							draws = draws + {{3}},
							last_updated = {{4}}
						WHERE character_id = {{0}}",
					new object[] { winnerId, winnerRating, draw ? 0 : 1, draw ? 1 : 0, now },
					cancellationToken).ConfigureAwait(false);
				await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {TableName} SET
							rating = {{1}},
							peak_rating = GREATEST(peak_rating, {{1}}),
							losses = losses + {{2}},
							draws = draws + {{3}},
							last_updated = {{4}}
						WHERE character_id = {{0}}",
					new object[] { loserId, loserRating, draw ? 0 : 1, draw ? 1 : 0, now },
					cancellationToken).ConfigureAwait(false);

				await dbContext.Database.ExecuteSqlRawAsync(
					$@"INSERT INTO {results} (request_key, winner_id, loser_id, draw, reason, winner_rating_change, loser_rating_change, created_utc)
						VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, {{7}})",
					new object[] { requestKey, winnerId, loserId, draw, reason, winnerChange, loserChange, now },
					cancellationToken).ConfigureAwait(false);

				return new DuelResultData(
					new DuelRecordData(winnerId, winnerRating, winnerChange, winner.Wins + (draw ? 0 : 1), winner.Losses, winner.Draws + (draw ? 1 : 0)),
					new DuelRecordData(loserId, loserRating, loserChange, loser.Wins, loser.Losses + (draw ? 0 : 1), loser.Draws + (draw ? 1 : 0)));
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<DuelRecordData>> FetchRecordAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<DuelRecordData>.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than zero.");
			}

			return await ExecuteReadAsync<DuelRecordData>(async dbContext =>
			{
				List<DuelRecordData> rows = await ReadRowsAsync(dbContext,
					$"SELECT character_id, rating, wins, losses, draws FROM {TableName} WHERE character_id = {{0}}",
					new object[] { characterId },
					reader => new DuelRecordData(reader.GetInt64(0), reader.GetInt32(1), 0, reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)),
					cancellationToken).ConfigureAwait(false);
				return rows.Count > 0 ? rows[0] : new DuelRecordData(characterId, DuelRating.DefaultRating, 0, 0, 0, 0);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static DuelRecordData Find(List<DuelRecordData> rows, long characterId)
		{
			for (int i = 0; i < rows.Count; ++i)
			{
				if (rows[i].CharacterID == characterId)
				{
					return rows[i];
				}
			}
			// Unreachable after the insert above (the character rows exist: the foreign key would have refused it).
			return new DuelRecordData(characterId, DuelRating.DefaultRating, 0, 0, 0, 0);
		}

		private static DuelRecordData With(DuelRecordData record, int rating, int change)
		{
			return new DuelRecordData(record.CharacterID, rating, change, record.Wins, record.Losses, record.Draws);
		}
	}
}
