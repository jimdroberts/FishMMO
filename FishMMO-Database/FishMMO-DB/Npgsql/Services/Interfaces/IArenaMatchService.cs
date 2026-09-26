using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for arena matches: reading the match a hosting scene server has just
	/// loaded, recording its progress and its result.
	/// </summary>
	/// <remarks>
	/// Matches are <em>created</em> by <see cref="IGroupFinderQueueService.TryFormArenaMatchAsync"/>,
	/// inside the transaction that takes the players out of the queue; nothing here creates one.
	/// The hosting scene server is not necessarily the one that formed the match, so everything it
	/// needs to run it is read back from these rows.
	/// </remarks>
	public interface IArenaMatchService
	{
		/// <summary>Reads the match that runs in one instance.</summary>
		/// <param name="instanceId">The <c>scenes</c> row of the instance.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The match, or null when the instance is not an arena match.</returns>
		Task<DatabaseResult<ArenaMatchData?>> FetchByInstanceAsync(long instanceId, CancellationToken cancellationToken = default);

		/// <summary>Reads one match by id.</summary>
		Task<DatabaseResult<ArenaMatchData?>> FetchAsync(long matchId, CancellationToken cancellationToken = default);

		/// <summary>Reads every seat of a match.</summary>
		Task<DatabaseResult<IReadOnlyList<ArenaMatchMemberData>>> FetchMembersAsync(long matchId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Advances a match's status, stamping the start or end time as appropriate.
		/// </summary>
		/// <remarks>
		/// Only ever moves forward: the <c>WHERE</c> refuses a status lower than the current one,
		/// so a late write from a server that lost the instance cannot reopen an ended match.
		/// </remarks>
		/// <param name="matchId">Match to update.</param>
		/// <param name="status">New status.</param>
		/// <param name="winnerTeam">Winning team for <see cref="ArenaMatchStatus.Ended"/>, or -1.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>True when the row moved.</returns>
		Task<DatabaseResult<bool>> UpdateStatusAsync(long matchId, ArenaMatchStatus status, int winnerTeam = -1, CancellationToken cancellationToken = default);

		/// <summary>
		/// Writes the final tallies for a match's seats in one statement.
		/// </summary>
		/// <param name="matchId">Match the seats belong to.</param>
		/// <param name="tallies">Per character: kills, deaths and score.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Rows updated.</returns>
		Task<DatabaseResult<int>> UpdateMemberTalliesAsync(long matchId, IReadOnlyList<(long characterId, int kills, int deaths, int score)> tallies, CancellationToken cancellationToken = default);

		/// <summary>
		/// Which of the given characters hold a seat in a match that has not ended.
		/// </summary>
		/// <remarks>
		/// The arena half of the one-instance-per-party rule, asked at queue time about a whole
		/// party so that a party with a member in a live arena cannot open a dungeon, and one with a
		/// dungeon open cannot enter an arena. The forming transaction re-asserts it per seat.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<long>>> FetchCharactersInLiveMatchesAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default);

		/// <summary>
		/// Cancels matches older than <paramref name="olderThan"/> that have not ended and whose
		/// instance no longer exists.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A match whose instance failed to load, or whose hosting server died at any point, would
		/// otherwise hold every seat's character out of both finders forever. Called from every
		/// scene server's stale sweep, so it does not depend on the server that lost the match.
		/// </para>
		/// <para>
		/// Aged by the database's clock, which also stamped the match's creation: the sweeping
		/// server is rarely the one that formed the match. Served by the partial index on unfinished
		/// matches, so a sweep reads the handful of matches still open, not the whole history.
		/// </para>
		/// </remarks>
		/// <param name="olderThan">How long a match must have existed before it can be judged abandoned.</param>
		/// <param name="maxRows">Upper bound on matches cancelled in one call.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Matches cancelled.</returns>
		Task<DatabaseResult<int>> CancelAbandonedAsync(TimeSpan olderThan, int maxRows = 64, CancellationToken cancellationToken = default);

		/// <summary>A character's most recent matches, newest first, each with their own seat.</summary>
		Task<DatabaseResult<IReadOnlyList<ArenaHistoryData>>> FetchRecentForCharacterAsync(long characterId, int limit, CancellationToken cancellationToken = default);

		/// <summary>
		/// Marks a seat vacated so the backfill may fill it, keeping the leaver's row for history.
		/// </summary>
		/// <returns>True when the seat was seated and is now vacated.</returns>
		Task<DatabaseResult<bool>> MarkSeatVacatedAsync(long matchId, long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Re-seats a vacated character who came back inside the reconnect grace, if their seat was not
		/// filled meanwhile.
		/// </summary>
		/// <remarks>
		/// "Not filled" is counted: the team must have fewer seated players than the match's team size,
		/// under the match row's lock, which the backfill takes too — so a backfill and a reconnect
		/// racing for the last seat cannot both have it.
		/// </remarks>
		/// <returns>True when the seat was vacated, the team had room, and the seat is theirs again.</returns>
		Task<DatabaseResult<bool>> ReseatAsync(long matchId, long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Opens, for <paramref name="window"/> from now, the window during which vacated seats may
		/// be backfilled; or closes it, with null.
		/// </summary>
		/// <remarks>
		/// The end is stamped by the database's clock because it is read by the backfill
		/// transactions of every other scene server, which compare it with the database's clock; a
		/// moment computed by the hosting server would shift the window by the difference between
		/// the two machines' clocks.
		/// </remarks>
		Task<DatabaseResult<bool>> SetBackfillWindowAsync(long matchId, TimeSpan? window, CancellationToken cancellationToken = default);

		/// <summary>Writes the rating change each seat earned in a ranked match.</summary>
		Task<DatabaseResult<int>> UpdateMemberRatingDeltasAsync(long matchId, IReadOnlyList<(long characterId, int ratingDelta)> deltas, CancellationToken cancellationToken = default);

		/// <summary>Stamps a match as ranked in a season. Called once, right after it forms, before it goes live.</summary>
		Task<DatabaseResult<bool>> SetRankedAsync(long matchId, long seasonId, CancellationToken cancellationToken = default);
	}
}
