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
		/// Cancels matches that had not ended by <paramref name="createdBeforeUtc"/> and whose
		/// instance no longer exists.
		/// </summary>
		/// <remarks>
		/// A match whose instance failed to load, or whose hosting server died at any point, would
		/// otherwise hold every seat's character out of both finders forever. Called from every
		/// scene server's stale sweep, so it does not depend on the server that lost the match.
		/// </remarks>
		Task<DatabaseResult<int>> CancelAbandonedAsync(DateTime createdBeforeUtc, int maxRows = 64, CancellationToken cancellationToken = default);
	}
}
