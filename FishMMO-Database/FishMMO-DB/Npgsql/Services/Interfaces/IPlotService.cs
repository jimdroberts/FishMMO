using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Reads and writes ownership of authored plots of land.
	/// </summary>
	/// <remarks>
	/// Every method that changes ownership reports how many rows it affected, and the caller is
	/// expected to check it. The counts are the only way to tell a claim that succeeded from one
	/// that lost a race, and treating zero as success would sell one plot to two players.
	/// </remarks>
	public interface IPlotService
	{
		/// <summary>
		/// Ensures a row exists for every foundation authored in a scene.
		/// </summary>
		/// <param name="worldServerID">The world server whose land this is.</param>
		/// <param name="sceneName">The scene the foundations belong to.</param>
		/// <param name="plotKeys">Canonicalised keys of the foundations found in the scene.</param>
		/// <returns>The number of plots that did not previously exist.</returns>
		/// <remarks>
		/// Idempotent, because every scene server hosting a channel of the scene runs it on load and
		/// they all describe the same land. Plots that already exist keep the ownership they have —
		/// registration must never disturb it, or a restart would evict whoever lives there.
		///
		/// <para>Scoped to one world server. The same scene runs on every world, and registering it
		/// unscoped would give them all a single shared row per plot — one player's house appearing
		/// as already-owned land to everybody on every other world.</para>
		/// </remarks>
		Task<DatabaseResult<int>> RegisterAsync(long worldServerID, string sceneName, IReadOnlyList<string> plotKeys, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches every plot in one world server's copy of a scene, owned or not.
		/// </summary>
		Task<DatabaseResult<List<PlotData>>> FetchBySceneAsync(long worldServerID, string sceneName, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches every plot a character owns, across all scenes.
		/// </summary>
		Task<DatabaseResult<List<PlotData>>> FetchByOwnerCharacterAsync(long characterID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches every plot a guild owns, across all scenes.
		/// </summary>
		Task<DatabaseResult<List<PlotData>>> FetchByOwnerGuildAsync(long guildID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Claims an unowned plot.
		/// </summary>
		/// <param name="plotID">The plot to claim.</param>
		/// <param name="ownerCharacterID">The claiming character, or zero for a guild claim.</param>
		/// <param name="ownerGuildID">The claiming guild, or zero for a character claim.</param>
		/// <returns>
		/// 1 when this call took the plot; 0 when it was already owned, does not exist, or the
		/// claiming character already owns one.
		/// </returns>
		/// <param name="taxDueUtc">When the first tax payment falls due, or null for untaxed land.</param>
		/// <param name="claimedState">
		/// The <c>FishMMO.Shared.PlotState</c> the plot enters, as its underlying integer. Defaults
		/// to <c>Building</c>.
		/// </param>
		/// <remarks>
		/// Exactly one of the two identifiers may be non-zero.
		///
		/// <para>The first tax date and the new state are set by the same statement that takes the
		/// plot, so there is no moment where land is owned but untaxed, or owned but still showing
		/// as an empty lot. Set separately, a crash in the gap would leave a plot nobody ever has to
		/// pay for, or one that every channel draws as available.</para>
		///
		/// <para>A character may hold only one plot. That is asked here <em>and</em> enforced by a
		/// partial unique index, because this clause alone cannot stop two simultaneous claims on
		/// two scene servers. A caller that gets <c>UNIQUE_VIOLATION</c> back rather than a row
		/// count lost that race and should report it the same way it reports zero.</para>
		/// </remarks>
		Task<DatabaseResult<int>> TryClaimAsync(long plotID, long ownerCharacterID, long ownerGuildID, DateTime? taxDueUtc, int claimedState = 1, CancellationToken cancellationToken = default);

		/// <summary>
		/// Releases a plot back to the unowned pool, if the expected owner still holds it.
		/// </summary>
		/// <param name="plotID">The plot to release.</param>
		/// <param name="expectedOwnerCharacterID">The character believed to own it, or zero.</param>
		/// <param name="expectedOwnerGuildID">The guild believed to own it, or zero.</param>
		/// <param name="releasedState">
		/// The <c>FishMMO.Shared.PlotState</c> the plot is left in, as its underlying integer.
		/// Defaults to <c>Empty</c>; reclamation passes <c>Abandoned</c>.
		/// </param>
		/// <returns>1 when released, 0 when somebody else owns it or it was already unowned.</returns>
		/// <remarks>
		/// The state the plot lands in is the caller's to choose, because releasing is two different
		/// events wearing one name. An owner handing the deed back leaves a bare lot; a sweep taking
		/// land off somebody who stopped paying leaves a house standing empty, which renders
		/// differently and tells a passer-by a different story.
		/// </remarks>
		Task<DatabaseResult<int>> ReleaseAsync(long plotID, long expectedOwnerCharacterID, long expectedOwnerGuildID, int releasedState = 0, CancellationToken cancellationToken = default);

		/// <summary>
		/// Moves a plot from one lifecycle state to another, if it is still where and whose the
		/// caller thinks it is.
		/// </summary>
		/// <param name="plotID">The plot to move.</param>
		/// <param name="expectedState">The state it must currently be in.</param>
		/// <param name="newState">The state to move it to. Must differ from the expected one.</param>
		/// <param name="expectedOwnerCharacterID">The character believed to own it, or zero.</param>
		/// <param name="expectedOwnerGuildID">The guild believed to own it, or zero.</param>
		/// <returns>1 when the plot moved, 0 when it had already moved or changed hands.</returns>
		Task<DatabaseResult<int>> TrySetStateAsync(long plotID, int expectedState, int newState, long expectedOwnerCharacterID, long expectedOwnerGuildID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gives a first due date to character-owned plots that have none.
		/// </summary>
		/// <param name="worldServerID">The world whose land to backfill.</param>
		/// <param name="firstDueUtc">When those plots should first fall due.</param>
		/// <returns>How many plots were given a due date.</returns>
		/// <remarks>
		/// Land claimed while tax was switched off is stored with no due date, and the sweep only
		/// looks at plots that have one. Turning tax on afterwards would therefore never touch any
		/// of it: those plots would be free forever, and the difference between a founding player
		/// and a later one would be permanent.
		///
		/// <para>Only rows with no date are touched, so this cannot reset a plot that is already
		/// being billed — running it repeatedly is safe, which matters because it runs on every
		/// scene resolve.</para>
		///
		/// <para>Character-owned only. Guild land is deferred rather than charged until a treasury
		/// exists, so giving it a date would only make every sweep pick it up and push it along
		/// again.</para>
		/// </remarks>
		Task<DatabaseResult<int>> BackfillTaxDueAsync(long worldServerID, DateTime firstDueUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches owned plots whose tax has fallen due.
		/// </summary>
		/// <param name="worldServerID">The world whose land to sweep.</param>
		/// <param name="asOfUtc">The moment to judge against.</param>
		/// <param name="limit">Most rows to return.</param>
		/// <remarks>
		/// Bounded, because a server that has been down over a billing period comes back to every
		/// plot at once and an unbounded sweep would try to charge all of them in one pass.
		/// </remarks>
		Task<DatabaseResult<List<PlotData>>> FetchTaxDueAsync(long worldServerID, DateTime asOfUtc, int limit, CancellationToken cancellationToken = default);

		/// <summary>
		/// Moves a plot's tax date forward, if it still holds the date the caller charged against.
		/// </summary>
		/// <param name="plotID">The plot being charged.</param>
		/// <param name="expectedDueUtc">The due date the caller read.</param>
		/// <param name="nextDueUtc">The date the next payment falls due.</param>
		/// <returns>1 when this call advanced it, 0 when somebody else already did.</returns>
		/// <remarks>
		/// The pin is what makes the tax safe to sweep from every scene server at once. Several may
		/// host the same world's scenes, they all see the same plot come due, and only the one whose
		/// expected date still matches may charge it — so a period produces one payment rather than
		/// one per server.
		///
		/// <para>Called <em>before</em> the money is taken, for the same reason a plot is claimed
		/// before it is paid for: winning the right to charge is the contended step, and a caller
		/// that loses it must not have taken anything.</para>
		/// </remarks>
		Task<DatabaseResult<int>> TryAdvanceTaxAsync(long plotID, DateTime expectedDueUtc, DateTime nextDueUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Records that a tax payment was missed, without disturbing an earlier miss.
		/// </summary>
		/// <param name="plotID">The plot that went unpaid.</param>
		/// <param name="delinquentSinceUtc">When the first missed payment fell due.</param>
		/// <remarks>
		/// Only sets the mark when there is not one already. The grace period is measured from the
		/// <em>first</em> miss, so overwriting it on every later failure would restart the clock
		/// each period and the plot could never be reclaimed.
		/// </remarks>
		Task<DatabaseResult<int>> MarkTaxDelinquentAsync(long plotID, DateTime delinquentSinceUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Clears a plot's delinquency after a successful payment.
		/// </summary>
		Task<DatabaseResult<int>> ClearTaxDelinquencyAsync(long plotID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Releases every plot a guild owns.
		/// </summary>
		/// <returns>The number of plots released.</returns>
		/// <remarks>
		/// Guilds are hard-deleted, and the owner column carries no foreign key to clean up behind
		/// them, so land held by a disbanded guild would otherwise stay claimed by an identifier
		/// that no longer resolves to anything — unclaimable, and unbuildable, permanently.
		/// </remarks>
		Task<DatabaseResult<int>> ReleaseAllForGuildAsync(long guildID, CancellationToken cancellationToken = default);
	}
}
