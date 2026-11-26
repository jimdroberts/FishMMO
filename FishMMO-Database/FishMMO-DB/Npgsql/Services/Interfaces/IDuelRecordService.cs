using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Duel records (the Duels module): every character's rating, wins, losses and draws, and the history of finished
	/// duels. The duel boards read <c>duel_record</c> through <see cref="ILeaderboardService"/>.
	/// </summary>
	public interface IDuelRecordService
	{
		/// <summary>
		/// Records one finished duel in one transaction: both records (inserted at the default rating by a first duel),
		/// the rating change from both sides' ratings as they stand, and the history row.
		/// </summary>
		/// <remarks>
		/// Idempotent by <paramref name="requestKey"/>: a retry after a lost commit reply returns the records as they now
		/// stand and changes nothing. Both rows are locked in id order, so two results involving one character serialise.
		/// </remarks>
		/// <param name="requestKey">Chosen once per duel by the caller.</param>
		/// <param name="winnerId">The winner, or for a draw the first side.</param>
		/// <param name="loserId">The loser, or for a draw the second side.</param>
		/// <param name="draw">True when neither won.</param>
		/// <param name="reason">How it ended (the game's own code), kept in the history.</param>
		/// <param name="kFactor">The most one duel moves a rating.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<DuelResultData>> RecordResultAsync(Guid requestKey, long winnerId, long loserId, bool draw, short reason, int kFactor, CancellationToken cancellationToken = default);

		/// <summary>One character's record, or a default (unrated) one when they have never duelled.</summary>
		Task<DatabaseResult<DuelRecordData>> FetchRecordAsync(long characterId, CancellationToken cancellationToken = default);
	}
}
