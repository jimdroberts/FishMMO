using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// The daemon control plane: how AppHealthMonitor reports in, and how it is told what to do.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The daemons poll and the panel writes, exactly as the world and scene servers already
	/// work. Nothing calls a daemon; there is no inbound port on a game host, no certificate to
	/// manage there and no firewall rule to keep right, and a host behind NAT is reachable on
	/// the same terms as one that is not.
	/// </para>
	/// <para>
	/// <b>Why this can do what a server lock cannot.</b> A stopped game server polls nothing,
	/// so no row can reach it — but its supervisor is a different process and is still running.
	/// That is what makes start, stop and restart possible here and impossible in
	/// <c>IWorldServerService</c>.
	/// </para>
	/// <para>
	/// <b>The security model, in one line: a command names a verb and an application the daemon
	/// already supervises, and carries nothing the daemon executes.</b> Someone who can write
	/// to this table can restart a FishMMO process. They cannot make a game host run something
	/// new, because the path and arguments live only in that host's configuration file. Every
	/// method here is written to keep that true, and a change that lets a row describe what to
	/// run rather than which known thing to act on would turn a database password into remote
	/// code execution.
	/// </para>
	/// </remarks>
	public interface IDaemonService
	{
		/// <summary>
		/// Registers a daemon and refreshes its heartbeat, replacing what it supervises.
		/// </summary>
		/// <remarks>
		/// The reported list is authoritative for that host: an application removed from the
		/// daemon's configuration disappears here on the next beat, rather than lingering as a
		/// row an operator can still aim a command at.
		/// </remarks>
		/// <param name="hostName">The machine's name.</param>
		/// <param name="daemonVersion">The daemon build.</param>
		/// <param name="osDescription">What the operating system calls itself.</param>
		/// <param name="processorCount">Logical processors.</param>
		/// <param name="startedUtc">When the daemon process started.</param>
		/// <param name="apps">What it supervises, as observed this cycle.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult> HeartbeatAsync(
			string hostName,
			string daemonVersion,
			string osDescription,
			int processorCount,
			System.DateTime startedUtc,
			IReadOnlyList<DaemonAppReport> apps,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Claims the commands waiting for one host.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Atomic: the rows are marked as this instance's in the same statement that selects
		/// them, so two daemons sharing a host name cannot both execute one command, and a
		/// daemon that dies mid-command leaves a claimed row somebody can see rather than one
		/// that is quietly done twice.
		/// </para>
		/// <para>
		/// Expired commands are never returned. A restart carried out twenty minutes after it
		/// was asked for — once the operator has moved on and players are back on the server —
		/// is worse than one that never happened.
		/// </para>
		/// </remarks>
		/// <param name="hostName">The claiming daemon's own host name. It gets nothing else.</param>
		/// <param name="instanceId">This daemon process's identity, stamped on what it takes.</param>
		/// <param name="maxCommands">How many to take at once.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<IReadOnlyList<DaemonCommandData>>> ClaimCommandsAsync(
			string hostName,
			string instanceId,
			int maxCommands,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Records how a claimed command ended.
		/// </summary>
		/// <remarks>
		/// Only by the instance that claimed it, so a second daemon cannot overwrite the
		/// outcome of work it did not do. A refusal — the application is not in this daemon's
		/// configuration — is reported through here as a failure, not swallowed: an operator
		/// who asked for something impossible must be told.
		/// </remarks>
		Task<DatabaseResult> CompleteCommandAsync(
			long commandId,
			string instanceId,
			bool succeeded,
			string outcome,
			CancellationToken cancellationToken = default);

		/// <summary>Every host and what it supervises, for the panel.</summary>
		Task<DatabaseResult<IReadOnlyList<DaemonHostData>>> FetchHostsAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Queues a command.
		/// </summary>
		/// <remarks>
		/// Refuses a host or application the daemons have not reported. That is not a
		/// convenience check: it keeps the set of things a command can name to what some daemon
		/// already supervises, so the queue cannot be used to invent a target.
		/// </remarks>
		/// <param name="hostName">The host to act on.</param>
		/// <param name="appName">The supervised application, by name.</param>
		/// <param name="verb">Start, stop or restart.</param>
		/// <param name="requestedBy">The operator account.</param>
		/// <param name="reason">Why. Recorded here and in the audit log.</param>
		/// <param name="lifetime">How long it stays executable before being abandoned.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<long>> EnqueueCommandAsync(
			string hostName,
			string appName,
			DaemonCommandVerb verb,
			string requestedBy,
			string reason,
			System.TimeSpan lifetime,
			CancellationToken cancellationToken = default);

		/// <summary>The command history, newest first.</summary>
		Task<DatabaseResult<DaemonCommandPage>> FetchCommandsAsync(
			string hostName,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// The supervision history: what the daemons observed changing, newest first.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Not what the command history is.</b> That records what operators asked for through
		/// this panel. This records what the supervisors saw happen on their own — a process that
		/// died at three in the morning, the restarts that followed, and whether it came back —
		/// none of which anybody asked for and none of which is recorded anywhere else. The
		/// current-state row in <c>daemon_apps</c> is overwritten every beat, so a fault that has
		/// already been recovered from leaves no trace at all without this.
		/// </para>
		/// <para>
		/// <b>And it is not process output.</b> No log line, no exception text and nothing else a
		/// supervised process wrote reaches here; the rows are derived server-side from two enum
		/// values and two integers. Log contents routinely carry connection strings and tokens
		/// inside exception messages, and a panel route that relayed them would turn a read-only
		/// operator view into a way of reading them out of the machine.
		/// </para>
		/// </remarks>
		/// <param name="hostName">Restrict to one host, or null for all.</param>
		/// <param name="appName">Restrict to one application by name, or null for all.</param>
		/// <param name="status">Restrict to a resulting status, or null for all.</param>
		/// <param name="fromUtc">Only observations at or after this instant, when given.</param>
		/// <param name="toUtc">Only observations at or before this instant, when given.</param>
		/// <param name="page">1-based page number.</param>
		/// <param name="pageSize">Rows per page, clamped by the service.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<DaemonAppEventPage>> FetchAppEventsAsync(
			string hostName,
			string appName,
			DaemonAppStatus? status,
			System.DateTime? fromUtc,
			System.DateTime? toUtc,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default);
	}
}
