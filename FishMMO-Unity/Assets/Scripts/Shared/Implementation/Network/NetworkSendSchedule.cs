using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Managing.Predicting;

namespace FishMMO.Shared
{
	/// <summary>
	/// How often each side flushes its outgoing network data, while the simulation keeps ticking at the full tick rate:
	/// with an interval of 2, every packet carries two ticks of messages, so there are half as many packets and half the
	/// per-packet transport cost (IP, UDP and QUIC headers, which at ~58 B a packet were over half of a player's traffic).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Nothing is dropped or merged, only delayed.</b> Every message a tick writes is queued exactly as before and leaves
	/// with the next flush, in order, so every peer simulates the same data as at an interval of 1. What changes is arrival
	/// timing, and each place that depends on timing is covered:
	/// </para>
	/// <list type="bullet">
	/// <item><b>The server's input queue.</b> At a client interval of 2, inputs arrive two at a time. An empty replicate
	/// queue runs DEFAULT input (a misprediction the reconcile must undo), so the queue is kept one tick deeper per extra tick
	/// of interval: <c>PredictionManager.StateInterpolation</c> = base + (client interval − 1). Lag compensation already adds
	/// that depth (<c>LagCompensationTick.ResolveReplicateQueueTicks</c> reads it live), so hits stay exact.</item>
	/// <item><b>Observed characters.</b> Their transform updates arrive two ticks' worth at a time: the same pattern as the
	/// observer streaming's 2-tick interval, which the interpolation buffer was sized for. The interval is therefore capped
	/// at <see cref="MaximumInterval"/>, never above what has been tested.</item>
	/// <item><b>Lag compensation's view offset.</b> The client adds the measured round trip; pings are batched like
	/// everything else, so the measured round trip carries the batching delay on average.</item>
	/// <item><b>Agreement.</b> The server decides both intervals (its configuration) and tells each client on entry
	/// (<see cref="NetworkScheduleBroadcast"/>), so a client and the server can never run different depths. A client that
	/// has not been told (the login server, an older server) runs at 1.</item>
	/// </list>
	/// </remarks>
	public static class NetworkSendSchedule
	{
		/// <summary>The largest interval allowed: the observer interpolation buffer is sized for 2-tick delivery.</summary>
		public const byte MaximumInterval = 2;

		/// <summary>
		/// The largest idle interval (a player away from any fight). Observed characters are drawn on the playback clock,
		/// whose adaptive delay absorbs the extra tick (NetworkTransform timed playback), and lag compensation uses the exact
		/// view tick the client claims, so one more tick of delay costs no accuracy; it only arrives 33 ms later.
		/// </summary>
		public const byte MaximumIdleInterval = 3;

		/// <summary>How long a player stays at the full rate after its last sign of a fight, in ticks (3 s at 30 Hz).</summary>
		public const uint EngagedHoldTicks = 90;

		/// <summary>The idle interval: 0 (off) unless it is above the server's own interval, at most <see cref="MaximumIdleInterval"/>.</summary>
		public static byte ClampIdle(int idleInterval, byte serverInterval)
		{
			if (idleInterval <= serverInterval)
			{
				return 0;
			}
			return (byte)(idleInterval > MaximumIdleInterval ? MaximumIdleInterval : idleInterval);
		}

		/// <summary>
		/// A player is engaged while it is in combat (it dealt or took damage: set on that very tick) or the observer streaming
		/// found it near a fight (a target selected, someone fighting it, a character in combat within its engagement range).
		/// </summary>
		public static bool IsEngaged(bool inCombat, bool nearCombat) => inCombat || nearCombat;

		/// <summary>
		/// The connection's own interval: 0 (the server's) while engaged or within <see cref="EngagedHoldTicks"/> of it, else
		/// <paramref name="idleInterval"/>.
		/// </summary>
		public static byte ResolveConnectionInterval(byte idleInterval, uint tick, uint lastEngagedTick)
		{
			if (idleInterval == 0)
			{
				return 0;
			}
			return tick - lastEngagedTick < EngagedHoldTicks ? (byte)0 : idleInterval;
		}

		private static byte baseStateInterpolation;
		private static bool baseCaptured;

		/// <summary>An interval within 1..<see cref="MaximumInterval"/>.</summary>
		public static byte Clamp(int interval)
		{
			return (byte)(interval < 1 ? 1 : interval > MaximumInterval ? MaximumInterval : interval);
		}

		/// <summary>The replicate queue depth for a client send interval: one more tick per extra tick of interval.</summary>
		public static byte StateInterpolationFor(byte baseInterpolation, byte clientSendInterval)
		{
			int depth = baseInterpolation + (Clamp(clientSendInterval) - 1);
			return (byte)(depth > byte.MaxValue ? byte.MaxValue : depth);
		}

		/// <summary>
		/// Applies a schedule to this process: the server's or the client's own flush interval, and the matching replicate
		/// queue depth. Idempotent; the authored depth is captured once, so applying again never compounds.
		/// </summary>
		public static void Apply(NetworkManager networkManager, byte serverSendInterval, byte clientSendInterval, bool asServer)
		{
			PredictionManager prediction = PredictionOf(networkManager);
			if (prediction == null || networkManager.TransportManager == null)
			{
				return;
			}
			if (!baseCaptured)
			{
				baseStateInterpolation = prediction.StateInterpolation;
				baseCaptured = true;
			}
			networkManager.TransportManager.SetSendInterval(asServer, Clamp(asServer ? serverSendInterval : clientSendInterval));
			if (asServer)
			{
				/* The server never flushes as a client, but it reads its clients' interval from here: a client flushing every N
				 * ticks writes RedundancyCount + N - 1 inputs per replicate (FishNet, FISHMMO EDIT: batched replicates), and the
				 * server's "too many past replicates" check and its held-input wait both allow for that. */
				networkManager.TransportManager.SetSendInterval(asServer: false, Clamp(clientSendInterval));
			}
			prediction.SetStateInterpolation(StateInterpolationFor(baseStateInterpolation, clientSendInterval));
		}

		/// <summary>Back to FishNet's own behaviour (every tick, the authored depth), e.g. when the client leaves a scene server.</summary>
		public static void Reset(NetworkManager networkManager, bool asServer)
		{
			if (networkManager != null && networkManager.TransportManager != null)
			{
				networkManager.TransportManager.SetSendInterval(asServer, 1);
			}
			PredictionManager prediction = PredictionOf(networkManager);
			if (baseCaptured && prediction != null)
			{
				prediction.SetStateInterpolation(baseStateInterpolation);
			}
		}

		/// <summary>The replicate queue depth in force (for logs).</summary>
		public static int CurrentStateInterpolation(NetworkManager networkManager)
		{
			PredictionManager prediction = PredictionOf(networkManager);
			return prediction != null ? prediction.StateInterpolation : -1;
		}

		/// <summary>The prediction manager on the network manager's object (FishNet keeps its own reference internal).</summary>
		private static PredictionManager PredictionOf(NetworkManager networkManager)
		{
			return networkManager != null ? networkManager.GetComponent<PredictionManager>() : null;
		}
	}

	/// <summary>
	/// Server to client, on entering a scene server: the send intervals it runs, so the client flushes at the interval the
	/// server buffers for and keeps the same replicate queue depth (<see cref="NetworkSendSchedule"/>).
	/// </summary>
	public struct NetworkScheduleBroadcast : IBroadcast
	{
		/// <summary>The server's flush interval, in ticks.</summary>
		public byte ServerSendInterval;
		/// <summary>The interval the client must flush at, in ticks.</summary>
		public byte ClientSendInterval;
	}
}
