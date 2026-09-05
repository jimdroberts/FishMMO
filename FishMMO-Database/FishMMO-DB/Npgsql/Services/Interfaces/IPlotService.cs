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
		/// 1 when this call took the plot, 0 when it was already owned or does not exist.
		/// </returns>
		/// <remarks>
		/// Exactly one of the two identifiers may be non-zero.
		/// </remarks>
		Task<DatabaseResult<int>> TryClaimAsync(long plotID, long ownerCharacterID, long ownerGuildID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Releases a plot back to the unowned pool, if the expected owner still holds it.
		/// </summary>
		/// <param name="plotID">The plot to release.</param>
		/// <param name="expectedOwnerCharacterID">The character believed to own it, or zero.</param>
		/// <param name="expectedOwnerGuildID">The guild believed to own it, or zero.</param>
		/// <returns>1 when released, 0 when somebody else owns it or it was already unowned.</returns>
		Task<DatabaseResult<int>> ReleaseAsync(long plotID, long expectedOwnerCharacterID, long expectedOwnerGuildID, CancellationToken cancellationToken = default);

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
