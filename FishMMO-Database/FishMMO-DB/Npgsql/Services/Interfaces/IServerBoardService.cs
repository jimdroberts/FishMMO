using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// The operator's read of every server process and live scene instance.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A read-only service spanning four tables, separate from the three per-tier services that
	/// the servers themselves use to register and pulse. Those exist to serve one process
	/// talking about itself; this exists to answer "what is the shard doing", which is a
	/// different question with a different shape, and folding it into them would give a login
	/// server a method for enumerating scene servers it has no business calling.
	/// </para>
	/// <para>
	/// <b>Nothing here filters by pulse age.</b> Every per-tier service has a "fetch active"
	/// that hides a process which has stopped pulsing, which is right for the game — a dead
	/// server should not receive work. It is exactly wrong for an operator: the server that has
	/// stopped pulsing is the one they opened the page to find.
	/// </para>
	/// </remarks>
	public interface IServerBoardService
	{
		/// <summary>
		/// When one registered server last pulsed, for a health check.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The pulse is a far better liveness signal than any socket probe: it proves the server
		/// is running its loop, doing work, and can still reach the database. A port probe
		/// proves a socket is bound. It is also the only signal available now that the transport
		/// is WebTransport over QUIC, which has no "is the port open" answer — a QUIC server
		/// replies only to a valid Initial packet carrying a TLS ClientHello, so a plain
		/// datagram gets nothing back.
		/// </para>
		/// <para>
		/// Returns null when no server is registered under that name, which the caller must
		/// treat differently from a stale pulse: never registered and stopped talking are
		/// different faults.
		/// </para>
		/// </remarks>
		/// <param name="kind">Which tier: <c>login</c>, <c>world</c> or <c>scene</c>.</param>
		/// <param name="serverName">The name the server registered under, from its own configuration.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<System.DateTime?>> FetchLastPulseAsync(
			string kind,
			string serverName,
			CancellationToken cancellationToken = default);

		/// <summary>Every login, world and scene server, plus the live instance count.</summary>
		Task<DatabaseResult<ServerBoardData>> FetchBoardAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Live scene instances.
		/// </summary>
		/// <param name="status">Filter by <c>SceneStatus</c>, or null for any.</param>
		/// <param name="sceneServerId">Filter to one scene server, or null for all.</param>
		/// <param name="page">1-based page number.</param>
		/// <param name="pageSize">Rows per page. Clamped by the service.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<SceneInstancePage>> FetchScenesAsync(
			int? status,
			long? sceneServerId,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default);
	}
}
