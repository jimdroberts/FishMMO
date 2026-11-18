using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishNet.Connection;
using FishNet.Transporting.WebTransport;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Writes one row a minute of what this server process sent and received to
	/// <c>server_bandwidth_minute</c>, for the Control Panel's bandwidth page.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One per process, owned by the tier's system.</b> <c>LoginServerSystem</c>,
	/// <c>WorldServerSystem</c> and <c>SceneServerSystem</c> each start one when they finish
	/// initialising and stop it when they deinitialise. It is a plain helper rather than a
	/// <see cref="ServerBehaviour"/> so that no server scene needs another asset in its behaviour
	/// list, and so the three tiers cannot drift apart.
	/// </para>
	/// <para>
	/// <b>The source is the transport's own counters</b> (<see cref="TransportTraffic.Capture"/>):
	/// FishNet bytes counted by the WebTransport socket, and msquic's process-wide UDP payload and
	/// datagram counts, which include the handshakes and refused connections that never became a
	/// session — exactly what an operator pays egress for. FishNet's <c>StatisticsManager</c> is
	/// disabled on servers and is not used.
	/// </para>
	/// <para>
	/// <b>Nothing runs on the main thread but the timer.</b> Every 60 seconds the periodic system
	/// hands one unit of work to the async worker; that work reads the database clock, captures the
	/// counters (allocation-free, any thread), files the interval in the
	/// <see cref="ServerBandwidthLedger"/> and writes every pending minute in one SET-upsert. A
	/// unit is single-flight: while one is still running the next tick is skipped, and because the
	/// counters are cumulative the following sample simply covers the longer interval.
	/// </para>
	/// <para>
	/// <b>A failure never double-counts, never blocks, and is never silent.</b> A failed clock read
	/// defers the sample (nothing is lost). A failed write keeps its minutes pending and they are
	/// re-sent with the next sample — safe, because the write is a SET of each minute's running
	/// total, keyed before the retried delegate runs. Only after
	/// <see cref="ServerBandwidthMath.MaxPendingMinutes"/> minutes of failures are the oldest
	/// dropped, and the drop is logged with its count. Faults go through one
	/// <see cref="RepeatingFaultLog"/>, so a database that is down for an hour logs its first error
	/// in full, a summary every fifteen minutes and one recovery line.
	/// </para>
	/// </remarks>
	public sealed class ServerBandwidthRecorder
	{
		/// <summary>Seconds between samples. One row per minute is the table's grain.</summary>
		public const float SampleIntervalSeconds = 60f;

		/// <summary>The longest the final sample at shutdown may block the main thread.</summary>
		public const int FinalSampleTimeoutMs = 1_500;

		/// <summary>
		/// Shutdown budget the final sample leaves for everything else. The budget
		/// (<see cref="UnitySyncOverAsync.BeginShutdownBudget"/>) is shared by every behaviour's
		/// teardown, and character saves matter more than the last partial minute of bandwidth, so
		/// the final sample is skipped rather than allowed to eat into this.
		/// </summary>
		public const int ShutdownReserveMs = 4_000;

		/// <summary>Consecutive deferred samples before the recorder says it is recording nothing.</summary>
		private const int DeferralsBeforeWarning = 5;

		private const string LogCategory = "ServerBandwidth";

		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;
		private readonly IPeriodicUpdateSystem periodic;
		private readonly int kind;
		private readonly string serverName;
		private readonly Guid instanceId;
		private readonly ServerBandwidthLedger ledger;
		private readonly RepeatingFaultLog faults;

		/// <summary>Set while a unit of work is running; the single-flight gate.</summary>
		private int inFlight;

		/// <summary>Signalled whenever no unit of work is running, so shutdown can wait for one.</summary>
		private readonly ManualResetEventSlim idle = new ManualResetEventSlim(true);

		private int consecutiveDeferrals;
		private long droppedReported;
		private bool stopped;

		private ServerBandwidthRecorder(
			IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server,
			IPeriodicUpdateSystem periodic,
			int kind,
			string serverName)
		{
			this.server = server;
			this.periodic = periodic;
			this.kind = kind;
			this.serverName = serverName;

			/* Taken once, here, and never again: every row this process writes carries it, which
			 * is what keeps a restart's counters (from zero, in a new process) apart from the old
			 * process's in the same minute. */
			instanceId = Guid.NewGuid();

			TransportTraffic.Capture(out TransportTrafficSnapshot start);
			ledger = new ServerBandwidthLedger(in start);
			faults = new RepeatingFaultLog(LogCategory, $"Bandwidth recording for {ServerBandwidthKind.Name(kind)} server '{serverName}'", summaryIntervalSeconds: 900.0);
		}

		/// <summary>The id every row this process writes carries.</summary>
		public Guid InstanceId => instanceId;

		/// <summary>
		/// Starts recording for this process, or returns null (having logged why) when it cannot:
		/// no configured <c>ServerName</c> to key the rows by, no bandwidth service, or no
		/// periodic system.
		/// </summary>
		/// <param name="server">The server this process runs.</param>
		/// <param name="type">Its tier.</param>
		public static ServerBandwidthRecorder TryStart(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server, ServerType type)
		{
			int kind = KindOf(type);
			if (server == null || kind == 0)
			{
				_ = Log.Error(LogCategory, $"Bandwidth is not recorded: {(server == null ? "no server" : $"'{type}' is not a login, world or scene server")}.");
				return null;
			}

			/* The configured name, on all three tiers, because it is what an operator knows the
			 * server as and what the supervising daemon checks its pulse by. */
			if (server.Configuration == null || !server.Configuration.TryGetString("ServerName", out string name) || string.IsNullOrWhiteSpace(name))
			{
				_ = Log.Error(LogCategory, "Bandwidth is not recorded: ServerName is not configured, so there is nothing to file the rows under.");
				return null;
			}
			if (name.Length > FishMMO.Database.Npgsql.Services.ServerBandwidthService.MaxServerNameLength)
			{
				_ = Log.Error(LogCategory, $"Bandwidth is not recorded: ServerName '{name}' is longer than {FishMMO.Database.Npgsql.Services.ServerBandwidthService.MaxServerNameLength} characters.");
				return null;
			}
			if (server.Database?.ServiceRegistry == null || !server.Database.ServiceRegistry.TryGet<IServerBandwidthService>(out _))
			{
				_ = Log.Error(LogCategory, "Bandwidth is not recorded: IServerBandwidthService is not registered.");
				return null;
			}
			if (!(server is IPeriodicUpdateSystem periodic))
			{
				_ = Log.Error(LogCategory, "Bandwidth is not recorded: the server has no periodic update system.");
				return null;
			}

			var recorder = new ServerBandwidthRecorder(server, periodic, kind, name);
			periodic.RegisterPeriodicCallback(SampleIntervalSeconds, recorder.OnPeriodicSample);
			_ = Log.Debug(LogCategory, $"Recording bandwidth as {ServerBandwidthKind.Name(kind)} server '{name}' (instance {recorder.instanceId}).");
			return recorder;
		}

		/// <summary>The stored tier number for a server type; 0 for anything else.</summary>
		public static int KindOf(ServerType type)
		{
			switch (type)
			{
				case ServerType.Login: return ServerBandwidthKind.Login;
				case ServerType.World: return ServerBandwidthKind.World;
				case ServerType.Scene: return ServerBandwidthKind.Scene;
				default: return 0;
			}
		}

		/// <summary>
		/// Stops the timer and takes one last sample, bounded, so the minute the process stopped
		/// in is not lost. Main thread, from the owning system's <c>OnDeinitialize</c>.
		/// </summary>
		/// <remarks>
		/// Called after the transport has stopped, so the sample includes the disconnects; the
		/// native counters survive the library's deinitialisation (they are carried), so the
		/// capture is still measured. Skipped, with a line in the log, when a write is still running
		/// past the timeout or the shutdown budget is too low to spare — the budget belongs to the
		/// character saves first.
		/// </remarks>
		public void Stop()
		{
			if (stopped)
			{
				return;
			}
			stopped = true;
			periodic.UnregisterPeriodicCallback(OnPeriodicSample);

			int available = UnitySyncOverAsync.ClampToShutdownBudget(ShutdownReserveMs + FinalSampleTimeoutMs);
			if (available < ShutdownReserveMs + FinalSampleTimeoutMs)
			{
				_ = Log.Warning(LogCategory, $"Shutdown budget is low ({available} ms left); the final partial minute of bandwidth is not recorded.");
				return;
			}

			/* One deadline for the wait and the sample together, so the main thread is held for
			 * FinalSampleTimeoutMs at most, not twice that. */
			var waited = System.Diagnostics.Stopwatch.StartNew();
			if (!idle.Wait(FinalSampleTimeoutMs) || Interlocked.CompareExchange(ref inFlight, 1, 0) != 0)
			{
				_ = Log.Warning(LogCategory, "A bandwidth write was still running at shutdown; the final partial minute is not recorded.");
				return;
			}
			idle.Reset();

			try
			{
				int left = FinalSampleTimeoutMs - (int)waited.ElapsedMilliseconds;
				if (left <= 0 || !UnitySyncOverAsync.TryRun(SampleAndWriteAsync, left))
				{
					_ = Log.Warning(LogCategory, $"The final bandwidth sample did not finish within {FinalSampleTimeoutMs} ms; its minute may not be recorded.");
				}
			}
			catch (Exception ex)
			{
				_ = Log.Warning(LogCategory, $"The final bandwidth sample failed; its minute is not recorded: {ex}");
			}
			finally
			{
				Interlocked.Exchange(ref inFlight, 0);
				idle.Set();
			}
		}

		/// <summary>The periodic tick: hand one unit of work to the async worker, unless one is running.</summary>
		private void OnPeriodicSample(float elapsedSeconds)
		{
			if (stopped || Interlocked.CompareExchange(ref inFlight, 1, 0) != 0)
			{
				// Still writing the last one. The next sample covers this interval; nothing is lost.
				return;
			}
			idle.Reset();

			/* A refusal (the worker saturated or not running) loses nothing — the next sample covers
			 * this interval — but a worker that keeps refusing means nothing is being recorded, so it
			 * goes through the fault log. The gate is still held here, so no unit of work can be
			 * touching the fault log at the same time. */
			Exception refused = null;
			try
			{
				bool queued = server.DataContainerRegistry != null &&
					server.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var worker) &&
					worker.Enqueue(RunAsync, nameof(ServerBandwidthRecorder));
				if (!queued)
				{
					refused = new InvalidOperationException("the async worker did not accept the bandwidth sample");
				}
			}
			catch (Exception ex)
			{
				refused = ex;
			}

			if (refused != null)
			{
				faults.Report(refused, MonotonicClock.NowSeconds);
				Interlocked.Exchange(ref inFlight, 0);
				idle.Set();
			}
		}

		/// <summary>One unit of work on the async worker, releasing the gate however it ends.</summary>
		/// <remarks>
		/// The ledger and the fault log are not thread-safe and are only touched with the gate
		/// held. Successive units may run on different pool threads, but never at once, and the
		/// interlocked exchange that releases the gate orders one unit's writes before the next's
		/// reads.
		/// </remarks>
		private async Task RunAsync()
		{
			try
			{
				await SampleAndWriteAsync(CancellationToken.None).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				faults.Report(ex, MonotonicClock.NowSeconds);
			}
			finally
			{
				Interlocked.Exchange(ref inFlight, 0);
				idle.Set();
			}
		}

		/// <summary>
		/// Reads the database clock, captures the counters, files the interval and writes every
		/// pending minute. Only ever runs with the gate held.
		/// </summary>
		private async Task SampleAndWriteAsync(CancellationToken cancellationToken)
		{
			if (server.Database?.ServiceRegistry == null || !server.Database.ServiceRegistry.TryGet<IServerBandwidthService>(out var service))
			{
				throw new InvalidOperationException("IServerBandwidthService is no longer registered.");
			}

			/* The key comes from the database clock and is fixed HERE, before the write's retry loop
			 * ever runs: a retry that re-read the clock could cross into the next minute and file the
			 * same traffic twice under two keys. The clock is read first and the counters captured
			 * the moment it answers, so the minute is the database's minute at the capture, to within
			 * the reply's one-way latency. */
			DatabaseResult<DateTime> clock = await service.FetchDatabaseUtcNowAsync(cancellationToken).ConfigureAwait(false);
			double answeredAt = MonotonicClock.NowSeconds;
			if (!clock.IsSuccess)
			{
				// Deferred, not lost: the counters keep accumulating and the next sample carries them.
				throw new DatabaseFaultException("reading the database clock", clock.ErrorCode, clock.ErrorMessage);
			}

			TransportTraffic.Capture(out TransportTrafficSnapshot snapshot);
			DateTime databaseAtSnapshot = clock.Data.AddSeconds(snapshot.TimestampSeconds - answeredAt);

			switch (ledger.Record(in snapshot, databaseAtSnapshot))
			{
				case ServerBandwidthSampleOutcome.Deferred:
					if (++consecutiveDeferrals == DeferralsBeforeWarning)
					{
						_ = Log.Warning(LogCategory, $"{DeferralsBeforeWarning} bandwidth samples in a row could not be measured " +
							"(the transport's QUIC counters are unavailable); nothing is being recorded until they are.");
					}
					break;
				case ServerBandwidthSampleOutcome.Rebaselined:
					consecutiveDeferrals = 0;
					_ = Log.Warning(LogCategory, "The transport's counters could not be differenced against the previous sample " +
						"(they went backwards, or were measured differently at the two ends). Recording restarts from now; " +
						"the traffic since the previous sample is not recorded.");
					break;
				default:
					consecutiveDeferrals = 0;
					break;
			}

			long dropped = ledger.DroppedMinutes;
			if (dropped != droppedReported)
			{
				_ = Log.Warning(LogCategory, $"{dropped - droppedReported} minute(s) of bandwidth were dropped unwritten " +
					$"({ServerBandwidthMath.MaxPendingMinutes} were already waiting on the database); {dropped} dropped since this process started.");
				droppedReported = dropped;
			}

			if (ledger.PendingCount == 0)
			{
				faults.ReportSuccess();
				return;
			}

			var batch = ledger.Pending();
			DatabaseResult<int> written = await service.RecordAsync(kind, serverName, instanceId, batch, cancellationToken).ConfigureAwait(false);
			if (!written.IsSuccess)
			{
				if (written.ErrorCode == DatabaseErrorCodes.ValidationError)
				{
					/* The rows themselves were refused, so re-sending them can never succeed and would
					 * block every later minute behind them. Dropped, loudly. */
					ledger.Acknowledge(batch);
				}
				throw new DatabaseFaultException($"writing {batch.Count} minute(s)", written.ErrorCode, written.ErrorMessage);
			}

			ledger.Acknowledge(batch);
			faults.ReportSuccess();
		}

		/// <summary>A failed <see cref="DatabaseResult"/>, as an exception the fault log can summarise.</summary>
		private sealed class DatabaseFaultException : Exception
		{
			public DatabaseFaultException(string doing, string code, string message)
				: base($"Bandwidth recording failed {doing}: [{code}] {message}")
			{
			}
		}
	}
}
