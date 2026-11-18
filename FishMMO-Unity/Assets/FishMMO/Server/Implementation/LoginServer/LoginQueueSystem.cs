using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Auth.Core.Collections;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Manages a FIFO login queue for clients arriving when the server is at
	/// authentication capacity.  Queued clients stay connected at the QUIC layer
	/// and receive periodic position updates via <see cref="LoginQueuePositionBroadcast"/>.
	/// When admitted (position reaches 0), the client re-initiates the handshake
	/// and proceeds through normal authentication.
	///
	/// <para><b>Update rate:</b> The <c>LoginQueueUpdateRateSeconds</c> config key
	/// controls how often position updates are broadcast.  This is server-authoritative
	/// only — clients cannot request faster updates.</para>
	///
	/// <para><b>Admission rate:</b> Clients are admitted from the queue at a
	/// server-configured rate (<c>LoginQueueAdmissionRatePerSecond</c>) to prevent
	/// the newly-admitted clients from immediately re-saturating auth capacity.</para>
	///
	/// <para><b>When it engages:</b> only when a handshake would push the number of connections
	/// mid-authentication past the authenticator's pending cap (<c>AuthMaxPendingConnections</c>,
	/// default 1,000 on the login server: what its SRP verify and proof channels hold between them,
	/// see <c>PendingAuthRules.DefaultLoginPendingCap</c>). The authenticator finds this system in the behaviour registry when it
	/// needs it, so the system has to be listed in the LoginServer scene's behaviours for the
	/// queue to exist at all.</para>
	///
	/// <para><b>Threading:</b> main thread only. Every entry point is reached from a FishNet
	/// callback, the authenticator's handshake path or its per-frame sweep, all of which run on
	/// the main thread.</para>
	/// </summary>
	[UnityEngine.CreateAssetMenu(fileName = "LoginQueueSystem", menuName = "FishMMO/Server/LoginServer/Login Queue System", order = 1)]
	public class LoginQueueSystem : ServerBehaviour
	{
		/// <summary>
		/// Ordered FIFO queue of connections waiting for an auth slot.
		/// Uses <see cref="ArrivalOrderTracker{TKey}"/> for O(1) add/remove by connection.
		/// </summary>
		private ArrivalOrderTracker<NetworkConnection> queue;

		/// <summary>
		/// Interval in seconds between position-update broadcast sweeps.
		/// Server-controlled — read from .cfg at startup.
		/// </summary>
		private float queueUpdateRateSeconds = 2.0f;

		/// <summary>
		/// Maximum number of clients allowed in the login queue.
		/// Clients beyond this cap receive a <see cref="ClientAuthenticationResult.ServerBusy"/>
		/// rejection instead of being queued.
		/// </summary>
		private int maxQueueSize = 500;

		/// <summary>
		/// Maximum rate at which clients are admitted from the queue (per second).
		/// Smooths re-admission to prevent auth-capacity re-saturation.
		/// </summary>
		private float admissionRatePerSecond = DefaultAdmissionRatePerSecond;

		/// <summary>
		/// Admissions per second when <c>LoginQueueAdmissionRatePerSecond</c> is not configured.
		/// </summary>
		/// <remarks>
		/// Fifty: under what the SRP pipeline completes, so the queue drains into it rather than
		/// refilling the pending cap it is waiting on. The bottleneck is the proof stage, two workers
		/// by default, each proof several database round trips (the lockout reads, the online check,
		/// the token write) plus the SRP arithmetic — tens of milliseconds, so somewhere near a hundred
		/// a second between them. The old five a second took a hundred seconds to drain a full queue
		/// of 500 while the pipeline sat mostly idle. An admitted client that finds the cap still full
		/// is re-admitted at once rather than sent to the back (<see cref="recentlyAdmitted"/>), so a
		/// rate slightly above the pipeline costs a retry, not a place.
		/// </remarks>
		public const float DefaultAdmissionRatePerSecond = 50f;

		/// <summary>
		/// Maximum seconds a client can remain in the queue before being timed out.
		/// Timed-out clients receive position -1 and are disconnected.
		/// </summary>
		private float queueTimeoutSeconds = 300f;

		/// <summary>
		/// Admissions paid for and not yet spent. Accrues at <see cref="admissionRatePerSecond"/>
		/// and each admission spends one; see <see cref="AccrueAdmissionCredit"/>.
		/// </summary>
		/// <remarks>
		/// A credit rather than a countdown to the next single admission: the countdown admitted at
		/// most one client per frame, so any configured rate above the frame rate was silently cut
		/// to the frame rate.
		/// </remarks>
		private float admissionCredit;

		/// <summary>
		/// Most admission credit that can build up while clients are queued, in seconds of the
		/// admission rate — so a frame hitch can make up at most this much lost time.
		/// </summary>
		private const float MaxAdmissionCreditSeconds = 1f;

		/// <summary>
		/// Accumulator for the next position-broadcast sweep.
		/// </summary>
		private float nextQueueUpdate;

		/// <summary>
		/// Accumulator for periodic purge of disconnected/timed-out entries.
		/// </summary>
		private float nextPurgeSweep;

		/// <summary>
		/// Interval between purge sweeps in seconds.
		/// </summary>
		private const float PurgeSweepIntervalSeconds = 10f;

		/// <summary>
		/// Client IDs recently admitted from the queue, in admission order.
		/// If a recently-admitted client's re-handshake fails (auth cap full),
		/// they get immediate re-admission instead of being re-queued at the tail.
		/// Entries expire after <see cref="RecentAdmitTtlSeconds"/>.
		/// </summary>
		/// <remarks>
		/// A fixed window per client from its admission, on the monotonic clock: every entry lives
		/// the same TTL and a re-admission restarts it at the back, so the oldest entry is always the
		/// next to expire and <see cref="SweepRecentlyAdmitted"/> reads only the head — nothing at
		/// all while the set is empty — and a lapsed entry reads as absent even before the sweep
		/// reaches it. It was a ConcurrentDictionary swept on a timer that never reset while the set
		/// was empty, so from ten seconds after startup its Count, which takes every one of the
		/// dictionary's locks, ran every frame.
		/// </remarks>
		private readonly FixedWindowCounter<int> recentlyAdmitted =
			new FixedWindowCounter<int>(TimeSpan.FromSeconds(RecentAdmitTtlSeconds));

		/// <summary>
		/// Monotonic time (seconds) each connection first entered the queue, preserved across
		/// re-queues. <see cref="PurgeStaleEntries"/> times the queue timeout from it.
		/// </summary>
		/// <remarks>
		/// A client that is admitted and then deferred again (auth cap still full once its
		/// fast-pass window has lapsed) re-enters at the tail with a fresh arrival stamp.
		/// Without remembering the original, <see cref="queueTimeoutSeconds"/> restarts on
		/// every cycle and a client can be recycled indefinitely while later arrivals get
		/// in ahead of it. Keyed by ClientId so the entry survives the connection object
		/// leaving and re-entering the queue.
		/// <para>
		/// Monotonic because the timeout is a local duration: on the wall clock, a step forward
		/// timed out — and disconnected — everyone in the queue at the next purge.
		/// </para>
		/// </remarks>
		private readonly ConcurrentDictionary<int, double> firstQueuedSeconds = new ConcurrentDictionary<int, double>();
		private const float RecentAdmitTtlSeconds = 15f;

		/// <summary>Reused by <see cref="PurgeStaleEntries"/> so a purge pass allocates nothing.</summary>
		private readonly List<NetworkConnection> purgeBuffer = new List<NetworkConnection>();

		/// <summary>
		/// Returns the current number of clients in the queue.
		/// </summary>
		public int QueuedCount => queue?.Count ?? 0;

		/// <summary>
		/// Returns whether <paramref name="conn"/> is currently waiting in the queue.
		/// </summary>
		/// <remarks>
		/// Used by the authenticator's handshake-timeout sweep: a queued connection is
		/// intentionally left unauthenticated for as long as the wait lasts, which would
		/// otherwise look identical to a client that connected and never handshook.
        /// </remarks>
		public bool IsQueued(NetworkConnection conn) => conn != null && queue != null && queue.Contains(conn);

		/// <summary>
		/// Returns whether <paramref name="conn"/> is waiting in the queue <em>or</em> has
		/// just been admitted and is expected to re-handshake.
		/// </summary>
		/// <remarks>
		/// The admitted-but-not-yet-re-handshaked window matters as much as the queue
		/// itself: an admitted client is popped off the queue, so it is no longer "queued",
		/// yet it is still unauthenticated and still waiting on us. Treating only the queue
		/// as exempt from the handshake timeout would let that window be disconnected out
		/// from under a client the server itself just invited back.
		/// </remarks>
		public bool IsAwaitingAdmission(NetworkConnection conn) =>
			conn != null && (IsQueued(conn) || recentlyAdmitted.GetCount(conn.ClientId, MonotonicClock.NowSeconds) > 0);

		#region ServerBehaviour Lifecycle

		/// <inheritdoc/>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			queue = new ArrivalOrderTracker<NetworkConnection>();
			ReadConfiguration();
			// One admission ready, as the countdown this replaced started at zero: the first
			// client queued after idle is admitted at once, the rest at the configured rate.
			admissionCredit = 1f;
			nextQueueUpdate = 0f;
			nextPurgeSweep = PurgeSweepIntervalSeconds;
			recentlyAdmitted.Clear();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc/>
		protected override void OnUpdate(float deltaTime)
		{
			if (queue == null)
				return;

			SweepRecentlyAdmitted(MonotonicClock.NowSeconds);
			TryAdmitFromQueue(deltaTime);
			BroadcastPositionUpdates(deltaTime);
			PurgeStaleEntries(deltaTime);
		}

		/// <inheritdoc/>
		public override void OnDeinitialize()
		{
			// Disconnect any clients still in the queue on shutdown.
			if (queue != null)
			{
				queue.ForEachInOrder((conn, _, _) =>
				{
					try
					{
						if (conn != null && conn.IsActive)
						{
							SendPositionUpdate(conn, -1, 0, 0);
							// Not immediately: the cancellation notice above still has to reach
							// the client. See PurgeStaleEntries.
							conn.Disconnect(false);
						}
					}
					catch { }
				});
				queue.Clear();
				queue = null;
			}
			recentlyAdmitted.Clear();
			firstQueuedSeconds.Clear();
		}

		#endregion

		#region Configuration

		/// <summary>
		/// Reads queue configuration from the server .cfg file.
		/// </summary>
		private void ReadConfiguration()
		{
			var cfg = Server?.Configuration;
			if (cfg == null) return;

			queueUpdateRateSeconds = ReadFloatConfig(cfg, "LoginQueueUpdateRateSeconds", 2.0f, 0.5f, 60f);
			maxQueueSize = cfg.GetInt("LoginQueueMaxSize", 500);
			if (maxQueueSize < 1) maxQueueSize = 1;
			if (maxQueueSize > 10000) maxQueueSize = 10000;
			admissionRatePerSecond = ReadFloatConfig(cfg, "LoginQueueAdmissionRatePerSecond", DefaultAdmissionRatePerSecond, 0.1f, 100f);
			queueTimeoutSeconds = ReadFloatConfig(cfg, "LoginQueueTimeoutSeconds", 300f, 30f, 3600f);

			Log.Debug("LoginQueueSystem",
				$"Configured: updateRate={queueUpdateRateSeconds}s, maxSize={maxQueueSize}, " +
				$"admissionRate={admissionRatePerSecond}/s, timeout={queueTimeoutSeconds}s");
		}

		/// <summary>
		/// Reads a float configuration value via string parsing, with bounds clamping.
		/// </summary>
		private static float ReadFloatConfig(IServerConfiguration cfg, string key,
			float defaultValue, float min, float max)
		{
			string raw = cfg.GetString(key, null);
			if (string.IsNullOrEmpty(raw) || !float.TryParse(raw,
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out float value))
			{
				value = defaultValue;
			}
			if (value < min) value = min;
			if (value > max) value = max;
			return value;
		}

		#endregion

		#region Public API (called from ServerAuthenticatorCore)

		/// <summary>
		/// Attempts to enqueue a connection that was deferred by the auth cap.
		/// Returns <c>true</c> if the client was queued; <c>false</c> if the queue
		/// is full and the client should be rejected outright.
		/// </summary>
		/// <param name="conn">The network connection to queue.</param>
		/// <returns><c>true</c> if queued; <c>false</c> if the queue is at capacity.</returns>
		public bool TryEnqueue(NetworkConnection conn)
		{
			if (queue == null) return false;
			if (conn == null || !conn.IsActive) return false;

			// Fast-pass: if this client was recently admitted from the queue
			// but their re-handshake failed (auth cap still full), admit them
			// immediately instead of re-queuing at the tail.
			if (recentlyAdmitted.GetCount(conn.ClientId, MonotonicClock.NowSeconds) > 0)
			{
				recentlyAdmitted.Remove(conn.ClientId);
				int pos = 0; // immediate re-admission
				SendPositionUpdate(conn, pos, 0, queue.Count);
				Log.Debug("LoginQueueSystem",
					$"Connection {conn.ClientId} fast-pass re-admitted (recently admitted).");
				return true;
			}

			if (queue.Count >= maxQueueSize)
			{
				Log.Warning("LoginQueueSystem",
					$"Login queue full ({maxQueueSize}); rejecting connection {conn.ClientId}.");
				return false;
			}

			// Guard: don't double-queue
			if (queue.Contains(conn))
				return true;

			// Preserve the original arrival time across re-queues so queueTimeoutSeconds
			// bounds the client's total wait rather than restarting each cycle.
			// The tracker's own timestamp is unused: the timeout is timed from firstQueuedSeconds.
			firstQueuedSeconds.GetOrAdd(conn.ClientId, MonotonicClock.NowSeconds);
			queue.TrackIfMissing(conn, default);

			// Send an immediate position update so the client knows they're queued.
			// The connection was not queued (checked above) and was appended at the tail, so
			// its 1-based position is the queue length — no need for GetPosition, which walks
			// the queue from the head and made every enqueue O(N).
			int position = queue.Count;
			int estimate = EstimateWaitSeconds(position);
			SendPositionUpdate(conn, position, estimate, position);

			Log.Debug("LoginQueueSystem",
				$"Connection {conn.ClientId} queued at position {position}/{position}.");

			return true;
		}

		#endregion

		#region Queue Processing

		/// <summary>
		/// Admits clients from the front of the queue at the configured admission rate —
		/// as many per frame as the accrued credit pays for.
		/// Admitted clients receive position=0, signalling them to re-initiate the handshake.
		/// </summary>
		private void TryAdmitFromQueue(float deltaTime)
		{
			bool queueEmpty = queue.Count == 0;
			admissionCredit = AccrueAdmissionCredit(admissionCredit, deltaTime, admissionRatePerSecond, queueEmpty);
			if (queueEmpty) return;

			// Discard dead entries without spending admission credit on them.
			// Popping a disconnected client used to consume the tick anyway, so a run of
			// stale entries throttled real admissions to the queue's drain rate (5/s by
			// default) — the queue appeared to stall for clients who were still waiting.
			// Bounded so a fully-dead queue cannot spin the whole frame.
			const int maxSkipPerTick = 64;
			int skipped = 0;
			double nowSeconds = MonotonicClock.NowSeconds;
			while (admissionCredit >= 1f && skipped < maxSkipPerTick && queue.PopOldest(out NetworkConnection conn, out _))
			{
				if (conn == null || !conn.IsActive)
				{
					skipped++;
					continue;
				}

				// Track as recently admitted so that if their re-handshake
				// fails (auth cap still full), they get a fast-pass through
				// TryEnqueue instead of being re-queued at the tail. Removed first so a
				// re-admission moves to the back and the set stays in admission order.
				recentlyAdmitted.Remove(conn.ClientId);
				recentlyAdmitted.Increment(conn.ClientId, nowSeconds);

				// Position 0 = "you are being processed now — retry your handshake"
				SendPositionUpdate(conn, 0, 0, 0);
				Log.Debug("LoginQueueSystem",
					$"Connection {conn.ClientId} admitted from queue. " +
					$"{queue.Count} remaining.");

				admissionCredit -= 1f;
			}

			if (skipped > 0)
			{
				Log.Debug("LoginQueueSystem", $"Skipped {skipped} dead queue entries while admitting.");
			}
		}

		/// <summary>
		/// Advances the admission credit by one frame.
		/// </summary>
		/// <remarks>
		/// While clients are queued the credit grows at <paramref name="ratePerSecond"/>, capped at
		/// <see cref="MaxAdmissionCreditSeconds"/> of it (never below one admission), and each
		/// admission spends one — so the rate holds whatever the frame rate, and a hitch makes up
		/// at most that much lost time. With the queue empty it refills to one admission and no
		/// further: the first client queued after idle goes straight through and the rate applies
		/// from there, rather than an idle spell banking a burst.
		/// </remarks>
		/// <param name="credit">Credit before this frame.</param>
		/// <param name="deltaTime">Seconds since the last frame.</param>
		/// <param name="ratePerSecond">Configured admission rate.</param>
		/// <param name="queueEmpty">Whether no client is waiting.</param>
		/// <returns>Credit after this frame.</returns>
		internal static float AccrueAdmissionCredit(float credit, float deltaTime, float ratePerSecond, bool queueEmpty)
		{
			float rate = Math.Max(0f, ratePerSecond);
			float cap = queueEmpty ? 1f : Math.Max(1f, rate * MaxAdmissionCreditSeconds);
			float accrued = credit + Math.Max(0f, deltaTime) * rate;
			return Math.Min(accrued, cap);
		}

		/// <summary>
		/// Sends <see cref="LoginQueuePositionBroadcast"/> to every queued client
		/// at the configured update rate.  Each client receives only their own position.
		/// </summary>
		private void BroadcastPositionUpdates(float deltaTime)
		{
			nextQueueUpdate -= deltaTime;
			if (nextQueueUpdate > 0f) return;

			nextQueueUpdate = queueUpdateRateSeconds;

			int total = queue.Count;
			queue.ForEachInOrder((conn, _, pos) =>
			{
				if (conn == null || !conn.IsActive) return;
				int estimate = EstimateWaitSeconds(pos);
				SendPositionUpdate(conn, pos, estimate, total);
			});
		}

		/// <summary>
		/// Removes entries past their TTL from <see cref="recentlyAdmitted"/>, oldest first.
		/// </summary>
		/// <remarks>
		/// Every frame, reading only the head: an entry leaves exactly when its TTL runs out, so
		/// the fast-pass window is the same for every client (clearing the whole set, as this once
		/// did, made it anywhere from 0 to ~25 seconds), and a frame with nothing due — including
		/// every frame while the set is empty — costs one peek.
		/// </remarks>
		private void SweepRecentlyAdmitted(double nowSeconds)
		{
			recentlyAdmitted.SweepExpired(nowSeconds, int.MaxValue);
		}

		/// <summary>
		/// Immediately removes a disconnected client from both the queue and
		/// the recently-admitted set. Call from connection-stopped handlers
		/// to prevent wasted admission ticks on dead connections.
		/// </summary>
		/// <param name="clientId">The FishNet client ID that disconnected.</param>
		public void OnClientDisconnected(int clientId)
		{
			recentlyAdmitted.Remove(clientId);
			firstQueuedSeconds.TryRemove(clientId, out _);
			// We can't look up the NetworkConnection from just the clientId
			// without access to the ServerManager, but the next purge sweep
			// will catch it. The recentlyAdmitted cleanup is the critical path
			// to prevent fast-pass abuse after disconnect.
		}

		/// <summary>
		/// Removes a disconnected client from the queue and the recently-admitted set.
		/// </summary>
		/// <remarks>
		/// Preferred over the ClientId-only overload when the caller still holds the
		/// connection: the entry goes immediately rather than lingering until the next
		/// purge sweep, which keeps reported positions and the queue count honest and
		/// stops the admission loop from having to step over it.
		/// <para>
		/// Must be called on the main thread — the queue itself is not thread-safe, which
		/// is why the ClientId-only overload deliberately touches nothing but the
		/// concurrent recently-admitted set.
		/// </para>
		/// </remarks>
		public void OnClientDisconnected(NetworkConnection conn)
		{
			if (conn == null) return;
			recentlyAdmitted.Remove(conn.ClientId);
			firstQueuedSeconds.TryRemove(conn.ClientId, out _);
			queue?.Remove(conn);
		}

		/// <summary>
		/// Removes connections that have disconnected while queued or exceeded the timeout.
		/// </summary>
		private void PurgeStaleEntries(float deltaTime)
		{
			nextPurgeSweep -= deltaTime;
			if (nextPurgeSweep > 0f) return;

			nextPurgeSweep = PurgeSweepIntervalSeconds;

			double now = MonotonicClock.NowSeconds;

			List<NetworkConnection> toRemove = purgeBuffer;
			toRemove.Clear();

			queue.ForEachInOrder((conn, _, _) =>
			{
				if (conn == null || !conn.IsActive ||
					(firstQueuedSeconds.TryGetValue(conn.ClientId, out double arrived) && now - arrived > queueTimeoutSeconds))
					toRemove.Add(conn);
			});

			foreach (var conn in toRemove)
			{
				if (queue.Remove(conn))
				{
					try
					{
						if (conn != null && conn.IsActive)
						{
							// Position -1 = queue cancelled
							SendPositionUpdate(conn, -1, 0, 0);
							/* Not immediately. Disconnect(true) stops the transport connection
							 * outright, discarding anything still queued for send — including the
							 * cancellation notice on the line above, which is the only thing that
							 * tells the client its queue wait ended and closes the
							 * "Queue position: N" dialog. Passing false marks the connection dirty
							 * so the server flushes pending sends first, which is what every other
							 * disconnect in the character and scene systems does. */
							conn.Disconnect(false);
						}
					}
					catch (Exception ex)
					{
						Log.Warning("LoginQueueSystem", $"Error disconnecting purged client {conn?.ClientId}: {ex}");
					}
				}
			}

			if (toRemove.Count > 0)
			{
				Log.Debug("LoginQueueSystem",
					$"Purged {toRemove.Count} stale entries from queue. " +
					$"{queue.Count} remaining.");
			}
			toRemove.Clear();
		}

		#endregion

		#region Helpers

		/// <summary>
		/// Sends a queue position update to a single client.
		/// The client does not need to be authenticated — the broadcast is sent with
		/// <c>requireAuthentication: false</c>.
		/// </summary>
		/// <remarks>
		/// Channel selection is by meaning, not by convenience. A positive position is a
		/// periodic progress report: it is re-sent every <c>queueUpdateRateSeconds</c> and any
		/// individual loss is corrected by the next sweep, so Unreliable is correct and keeps a
		/// large queue off the reliable channel.
		/// <para>
		/// Position 0 (admitted — re-send your handshake) and position -1 (queue cancelled) are
		/// one-shot state transitions with nothing behind them. Sending those unreliably meant a
		/// single dropped datagram stranded the client: it had already been popped off the
		/// queue, so no later sweep would mention it again, and it sat connected and silent
		/// until the authenticator's 15s handshake timeout disconnected it with no explanation.
		/// On a lossy link that turns into "logging in randomly does nothing".
		/// </para>
		/// </remarks>
		private void SendPositionUpdate(NetworkConnection conn, int position,
			int estimatedWaitSeconds, int totalQueued)
		{
			if (conn == null || !conn.IsActive) return;

			Server?.NetworkWrapper?.Broadcast(conn,
				new LoginQueuePositionBroadcast
				{
					QueuePosition = position,
					EstimatedWaitSeconds = estimatedWaitSeconds,
					TotalQueued = totalQueued,
				},
				requireAuthentication: false,
				position > 0 ? Channel.Unreliable : Channel.Reliable);
		}

		/// <summary>
		/// Returns a rough estimated wait time in seconds for a given queue position.
		/// Based on the configured admission rate.
		/// </summary>
		private int EstimateWaitSeconds(int position)
		{
			if (position <= 0) return 0;
			if (admissionRatePerSecond <= 0f) return position * 2; // fallback
			return (int)Math.Ceiling(position / admissionRatePerSecond);
		}

		#endregion
	}
}
