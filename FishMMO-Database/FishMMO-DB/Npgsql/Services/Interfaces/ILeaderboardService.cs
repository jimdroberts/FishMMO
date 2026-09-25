using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Read-only ranking of characters by a number the game already stores.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Nothing here writes.</b> Every source is owned by the system that writes it — the arena
	/// match coordinator for ratings, the character save for attributes and achievements — and a
	/// leaderboard is only a way of reading it across characters.
	/// </para>
	/// <para>
	/// <b>Eligibility is decided in the SQL, not by the caller.</b> A deleted character (a soft
	/// delete: the row stays with a renamed name) and a character on a banned account are never
	/// ranked, and neither is anyone on a staff account unless the query asks for them. Ranks are
	/// computed over the eligible rows only, so removing somebody closes the gap they leave rather
	/// than leaving a hole in the numbering.
	/// </para>
	/// <para>
	/// <b>One rank definition, used twice.</b> A page ranks its rows with <c>RANK()</c> ordered by
	/// score, and a standing is one plus the eligible characters with a strictly higher score —
	/// the same number by construction, so a character looked up on its own is given the rank it
	/// would carry on the page. Rows with equal scores share a rank; the order among them is fixed
	/// (fewer games first on the arena board, then character id) so pages never overlap or skip.
	/// </para>
	/// <para>
	/// Callers are expected to cache: these are ordered reads over whole tables' worth of rows,
	/// served cheaply by an index but not free. The scene server holds the results for a short
	/// time and shares one read between every player who asks in that window.
	/// </para>
	/// </remarks>
	public interface ILeaderboardService
	{
		/// <summary>
		/// A slice of a board, best first, with the board's size.
		/// </summary>
		/// <param name="query">Which board.</param>
		/// <param name="offset">Zero-based position of the first row.</param>
		/// <param name="limit">Rows to return, clamped to 1-200.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<LeaderboardPageData>> FetchPageAsync(LeaderboardQuery query, int offset, int limit, CancellationToken cancellationToken = default);

		/// <summary>
		/// One character's rank and score on a board, wherever it falls.
		/// </summary>
		/// <remarks>
		/// Succeeds with <see cref="LeaderboardStandingData.Ranked"/> false when the character is
		/// not on the board, which is an ordinary answer and not a failure.
		/// </remarks>
		/// <param name="query">Which board.</param>
		/// <param name="characterId">The character.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<LeaderboardStandingData>> FetchStandingAsync(LeaderboardQuery query, long characterId, CancellationToken cancellationToken = default);
	}
}
