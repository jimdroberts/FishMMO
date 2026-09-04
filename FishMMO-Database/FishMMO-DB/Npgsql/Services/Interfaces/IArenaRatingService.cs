using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for ranked arena seasons and per-season ratings.
	/// </summary>
	/// <remarks>
	/// Ratings are keyed by season; a new season starts everyone over. The rating maths lives in
	/// the shared <c>ArenaRating</c> rules, not here — this service only stores the results and
	/// answers the leaderboard.
	/// </remarks>
	public interface IArenaRatingService
	{
		/// <summary>
		/// The active season, creating a first one when none exists.
		/// </summary>
		/// <remarks>
		/// Called once per ranked match formed and once per board opened, so the answer is cheap:
		/// one indexed read, and an insert only on a fresh database.
		/// </remarks>
		Task<DatabaseResult<ArenaSeasonData>> GetOrCreateActiveSeasonAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Ends the active season and starts a new one. Ratings of the old season are kept.
		/// </summary>
		/// <param name="name">Display name of the new season.</param>
		/// <param name="endsUtc">When the new season ends, or null for open-ended.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<ArenaSeasonData>> StartNewSeasonAsync(string name, DateTime? endsUtc, CancellationToken cancellationToken = default);

		/// <summary>Reads the given characters' ratings in a season. Characters with none are absent.</summary>
		Task<DatabaseResult<IReadOnlyList<ArenaRatingData>>> FetchRatingsAsync(long seasonId, IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default);

		/// <summary>
		/// Writes ratings after a ranked match, in one statement.
		/// </summary>
		/// <remarks>
		/// An upsert: a character's first ranked game inserts their row. Peak rating is kept at the
		/// higher of the old and new values by the statement itself.
		/// </remarks>
		/// <param name="seasonId">Season the match counted towards.</param>
		/// <param name="results">Per character: the new rating and whether they won.</param>
		Task<DatabaseResult<int>> UpsertRatingsAsync(long seasonId, IReadOnlyList<(long characterId, int newRating, bool won)> results, CancellationToken cancellationToken = default);

		/// <summary>The highest rated characters of a season, with their names resolved by the caller.</summary>
		Task<DatabaseResult<IReadOnlyList<ArenaRatingData>>> FetchTopAsync(long seasonId, int limit, CancellationToken cancellationToken = default);
	}
}
