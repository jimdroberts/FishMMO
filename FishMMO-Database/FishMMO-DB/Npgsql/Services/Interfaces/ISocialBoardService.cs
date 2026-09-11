using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// The operator's read of the social systems: guilds, their ladders, their logs, and parties.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A read-only service spanning six tables, separate from the per-table services the game
	/// itself uses. Those exist to serve one guild or one membership row being mutated by the
	/// player who owns it — <see cref="IGuildService"/> fetches the guild a character is in,
	/// <see cref="ICharacterGuildService"/> writes one membership. This exists to answer "what
	/// does this guild look like from outside", which is a different question with a different
	/// shape, and folding it into them would hand the game server methods for enumerating every
	/// guild on the shard that it has no business calling.
	/// </para>
	/// <para>
	/// <b>Nothing here writes.</b> Guild and party membership is game state, owned by the scene
	/// server holding the characters: it is changed by players in the world, replicated through
	/// the update tables, and a row edited underneath a running server would be overwritten by
	/// its next save or, worse, leave the in-memory roster and the stored one disagreeing. The
	/// panel looks and does not touch, and there is no method here that could be called by
	/// mistake.
	/// </para>
	/// <para>
	/// <b>Member counts are counted in the same statement as the rows they belong to.</b> A page
	/// of twenty-five guilds is not twenty-six round trips; the count is a correlated subquery
	/// in the page's own SELECT, and the rosters for a page of parties are one further query
	/// keyed by the IDs on that page. An empty guild and an empty party are ordinary rows and
	/// come back as rows, not as gaps.
	/// </para>
	/// </remarks>
	public interface ISocialBoardService
	{
		/// <summary>
		/// Guilds whose name starts with the given text, or every guild when it is empty.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A prefix match, against the lower-cased computed name column, so the unique index on
		/// it does the work and nothing runs <c>LOWER()</c> per row. A contains-match would read
		/// the whole table on every keystroke of a debounced search box.
		/// </para>
		/// <para>
		/// Each row carries the member count and the leader. Neither is a column: the count is
		/// counted, and the leader is the member whose rank reaches the top rung of the guild's
		/// ladder — which is the test the game server itself applies, rather than a second
		/// definition of leadership that only the panel believes.
		/// </para>
		/// </remarks>
		/// <param name="query">Name prefix, or null/empty for every guild.</param>
		/// <param name="page">1-based page number.</param>
		/// <param name="pageSize">Rows per page. Clamped by the service.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<GuildAdminPage>> SearchGuildsAsync(
			string query,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// One guild in full: the row, its rank ladder, its roster and its pending applications.
		/// </summary>
		/// <remarks>
		/// Fails with <see cref="DatabaseErrorCodes.NotFound"/> when no such guild exists, which
		/// the caller must tell apart from a guild that exists and has nobody in it. The roster
		/// is unpaged: a guild's membership is bounded by the game's own cap, and a roster split
		/// across pages could not be ordered by rank without re-reading it.
		/// </remarks>
		/// <param name="guildId">The guild's primary key.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<GuildAdminDetail>> FetchGuildAsync(
			long guildId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// One guild's activity log, newest first.
		/// </summary>
		/// <remarks>
		/// The log is append-only and stores character IDs; the actor and target names are
		/// resolved at read time in one further query for the IDs on the page. A guild with no
		/// log is an ordinary answer: the table only started recording when the feature landed,
		/// so an older guild can legitimately have nothing in it.
		/// </remarks>
		/// <param name="guildId">The guild's primary key.</param>
		/// <param name="page">1-based page number.</param>
		/// <param name="pageSize">Rows per page. Clamped by the service.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<GuildLogPage>> FetchGuildLogAsync(
			long guildId,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Live parties, newest first, each with its member count and roster.
		/// </summary>
		/// <remarks>
		/// A party is a transient row: it exists while people are grouped and is deleted when the
		/// last member leaves, so this list is a snapshot of who is grouped with whom right now
		/// rather than a history. Rosters are small enough to carry inline — a party is capped at
		/// a handful of members — and come back in one query for the whole page.
		/// </remarks>
		/// <param name="worldServerId">Filter to one world server, or null for all of them.</param>
		/// <param name="page">1-based page number.</param>
		/// <param name="pageSize">Rows per page. Clamped by the service.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<PartyAdminPage>> FetchPartiesAsync(
			long? worldServerId,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default);
	}
}
