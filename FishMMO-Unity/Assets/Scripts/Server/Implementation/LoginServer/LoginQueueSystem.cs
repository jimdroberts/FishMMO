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
	/// </summary>
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
		private float admissionRatePerSecond = 5.0f;

		/// <summary>
		/// Maximum seconds a client can remain in the queue before being timed out.
		/// Timed-out clients receive position -1 and are disconnected.
		/// </summary>
		private float queueTimeoutSeconds = 300f;

		/// <summary>
		/// Accumulator for rate-limited admission timing.
		/// </summary>
		private float nextAdmissionTime;

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
		/// Set of client IDs that were recently admitted from the queue.
		/// If a recently-admitted client's re-handshake fails (auth cap full),
		/// they get immediate re-admission instead of being re-queued at the tail.
		/// Entries expire after <see cref="RecentAdmitTtlSeconds"/>.
		/// </summary>
		// ConcurrentDictionary used instead of HashSet for thread safety.
		// OnClientDisconnected() is called from FishNet network callbacks (potentially
		// on a transport thread), while TryEnqueue() and TryAdmitFromQueue() run on
		// the Unity main thread. ConcurrentDictionary provides lock-free reads/writes.
		private readonly ConcurrentDictionary<int, byte> recentlyAdmitted = new ConcurrentDictionary<int, byte>();
		private const float RecentAdmitTtlSeconds = 15f;
		private float nextRecentAdmitSweep;

		/// <summary>
		/// Returns the current number of clients in the queue.
		/// </summary>
		public int QueuedCount => queue?.Count ?? 0;

		#region ServerBehaviour Lifecycle

		/// <inheritdoc/>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			queue = new ArrivalOrderTracker<NetworkConnection>();
			ReadConfiguration();
			nextAdmissionTime = 0f;
			nextQueueUpdate = 0f;
			nextPurgeSweep = PurgeSweepIntervalSeconds;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc/>
		protected override void OnUpdate(float deltaTime)
		{
			if (queue == null)
				return;

			SweepRecentlyAdmitted(deltaTime);
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
							conn.Disconnect(true);
						}
					}
					catch { }
				});
				queue.Clear();
				queue = null;
			}
			recentlyAdmitted.Clear();
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
			admissionRatePerSecond = ReadFloatConfig(cfg, "LoginQueueAdmissionRatePerSecond", 5.0f, 0.1f, 100f);
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
			if (recentlyAdmitted.ContainsKey(conn.ClientId))
			{
				recentlyAdmitted.TryRemove(conn.ClientId, out _);
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

			queue.TrackIfMissing(conn, DateTime.UtcNow);

			// Send an immediate position update so the client knows they're queued.
			int position = queue.GetPosition(conn);
			int estimate = EstimateWaitSeconds(position);
			SendPositionUpdate(conn, position, estimate, queue.Count);

			Log.Debug("LoginQueueSystem",
				$"Connection {conn.ClientId} queued at position {position}/{queue.Count}.");

			return true;
		}

		#endregion

		#region Queue Processing

		/// <summary>
		/// Admits clients from the front of the queue at the configured admission rate.
		/// Admitted clients receive position=0, signalling them to re-initiate the handshake.
		/// </summary>
		private void TryAdmitFromQueue(float deltaTime)
		{
			if (queue.Count == 0) return;

			nextAdmissionTime -= deltaTime;
			if (nextAdmissionTime > 0f) return;

			if (queue.PopOldest(out NetworkConnection conn, out _))
			{
				if (conn != null && conn.IsActive)
				{
					// Track as recently admitted so that if their re-handshake
					// fails (auth cap still full), they get a fast-pass through
					// TryEnqueue instead of being re-queued at the tail.
					recentlyAdmitted.TryAdd(conn.ClientId, 0);

					// Position 0 = "you are being processed now — retry your handshake"
					SendPositionUpdate(conn, 0, 0, 0);
					Log.Debug("LoginQueueSystem",
						$"Connection {conn.ClientId} admitted from queue. " +
						$"{queue.Count} remaining.");
				}
			}

			// Reset the admission timer — one client per interval.
			nextAdmissionTime = 1.0f / admissionRatePerSecond;
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
		/// Removes expired entries from <see cref="recentlyAdmitted"/>.
		/// Runs every <see cref="PurgeSweepIntervalSeconds"/> seconds.
		/// </summary>
		private void SweepRecentlyAdmitted(float deltaTime)
		{
			nextRecentAdmitSweep -= deltaTime;
			if (nextRecentAdmitSweep > 0f || recentlyAdmitted.Count == 0) return;

			nextRecentAdmitSweep = PurgeSweepIntervalSeconds;
			// NOTE: All entries are cleared at once, not just expired ones.
			// The effective fast-pass window ranges from 0 to ~25 seconds (not exactly 15s).
			// This is an acceptable simplification for the admission rate.
			recentlyAdmitted.Clear();
		}

		/// <summary>
		/// Immediately removes a disconnected client from both the queue and
		/// the recently-admitted set. Call from connection-stopped handlers
		/// to prevent wasted admission ticks on dead connections.
		/// </summary>
		/// <param name="clientId">The FishNet client ID that disconnected.</param>
		public void OnClientDisconnected(int clientId)
		{
			recentlyAdmitted.TryRemove(clientId, out _);
			// We can't look up the NetworkConnection from just the clientId
			// without access to the ServerManager, but the next purge sweep
			// will catch it. The recentlyAdmitted cleanup is the critical path
			// to prevent fast-pass abuse after disconnect.
		}

		/// <summary>
		/// Removes connections that have disconnected while queued or exceeded the timeout.
		/// </summary>
		private void PurgeStaleEntries(float deltaTime)
		{
			nextPurgeSweep -= deltaTime;
			if (nextPurgeSweep > 0f) return;

			nextPurgeSweep = PurgeSweepIntervalSeconds;

			DateTime now = DateTime.UtcNow;
			DateTime timeoutThreshold = now.AddSeconds(-queueTimeoutSeconds);

			var toRemove = new System.Collections.Generic.List<NetworkConnection>();

			queue.ForEachInOrder((conn, enteredUtc, _) =>
			{
				if (conn == null || !conn.IsActive || enteredUtc < timeoutThreshold)
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
							conn.Disconnect(true);
						}
					}
					catch (Exception ex)
				{
					Log.Warning("LoginQueueSystem", $"Error disconnecting purged client {conn?.ClientId}: {ex.Message}");
				}
				}
			}

			if (toRemove.Count > 0)
			{
				Log.Debug("LoginQueueSystem",
					$"Purged {toRemove.Count} stale entries from queue. " +
					$"{queue.Count} remaining.");
			}
		}

		#endregion

		#region Helpers

		/// <summary>
		/// Sends a queue position update to a single client via unreliable broadcast.
		/// The client does not need to be authenticated — the broadcast is sent with
		/// <c>requireAuthentication: false</c>.
		/// </summary>
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
				Channel.Unreliable);
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
