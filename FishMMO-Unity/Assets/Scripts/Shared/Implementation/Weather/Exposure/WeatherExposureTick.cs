using System.Collections.Generic;
using FishNet.Managing.Client;
using FishNet.Managing.Predicting;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// Converts a prediction replicate's tick into the tick the weather timeline is evaluated at.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Two clocks, and they are not the same one.</b> The weather timeline is anchored in the
	/// SYNCHRONISED tick (<c>TimeManager.Tick</c>): <see cref="WeatherTimeline.WorldSecondsAt"/>
	/// measures from <c>WorldSecondsTick</c>, which the scene server stamps from its own
	/// <c>TimeManager.Tick</c> and broadcasts. A replicate's <c>input.GetTick()</c>, however, is the
	/// OWNING CLIENT's unsynchronised <c>LocalTick</c> — a counter that restarts at zero on every
	/// connect — so on the server the two differ by an arbitrary per-connection constant. Feeding an
	/// input tick straight to the weather would read the forecast for some other hour entirely, and
	/// would read a DIFFERENT wrong hour on the client than on the server, so the reconcile would
	/// fight the prediction for ever.
	/// </para>
	/// <para>
	/// <b>The mapping.</b> FishNet hands us the pairing it reconciled on: <c>ClientStateTick</c> is
	/// the local tick of the last reconcile and <c>ServerStateTick</c> is the server tick of that
	/// same reconcile. The difference between them is the constant, so
	/// </para>
	/// <para>
	/// FishNet 4.7 publishes that pairing only WHILE it reconciles — <c>PredictionManager</c> resets
	/// both to <c>UNSET_TICK</c> after every reconcile, where 4.6 left them standing until the next.
	/// Read raw, live prediction would then fall back to the client's synchronised estimate while
	/// replay used the pairing, and the two would disagree by however far the estimate is off. So
	/// <see cref="ReconcilePairing"/> remembers the pairing of the last reconcile per
	/// PredictionManager (from <c>OnPreReconcile</c>), and live prediction and replay read the same
	/// numbers again. It is forgotten when the client connection stops, because LocalTick restarts.
	/// </para>
	/// <code>weatherTick = ServerStateTick + (inputTick − ClientStateTick)</code>
	/// <para>
	/// and this one line is correct both live and in replay. In replay it is provably so: FishNet
	/// walks <c>ClientReplayTick</c> and <c>ServerReplayTick</c> forward together from
	/// <c>ClientStateTick + 1</c> and <c>ServerStateTick + 1</c> (PredictionManager, the
	/// <c>while (ClientReplayTick &lt; localTick)</c> loop), so for the replicate being replayed at
	/// client tick <c>t</c> the server's tick is exactly what the formula returns. That identity is
	/// what <see cref="Resolve"/> asserts against when a replay tick is supplied, and what the
	/// closed-loop tests pin.
	/// </para>
	/// <para>
	/// <b>On the server the input tick is not used at all.</b> The server IS the authority on when
	/// "now" is; its own <c>TimeManager.Tick</c> is the weather's tick, and the client's counter is
	/// only a label for matching inputs to reconciles.
	/// </para>
	/// <para>
	/// Pure arithmetic on purpose: everything here takes plain numbers, so the whole mapping can be
	/// tested without a NetworkManager, a connection or a running scene.
	/// </para>
	/// </remarks>
	public static class WeatherExposureTick
	{
		/// <summary>FishNet's "no tick" sentinel, repeated so callers need not reference TimeManager.</summary>
		public const uint Unset = TimeManager.UNSET_TICK;

		/// <summary>
		/// The synchronised tick to evaluate the weather at for one replicate step.
		/// </summary>
		/// <param name="isServer">True when this peer is the server, which owns the clock.</param>
		/// <param name="serverTick">The server's own <c>TimeManager.Tick</c>. Used only when <paramref name="isServer"/>.</param>
		/// <param name="clientSyncTick">
		/// The client's estimate of the synchronised tick (<c>TimeManager.Tick</c>), used only before
		/// a first reconcile has established the pairing.
		/// </param>
		/// <param name="clientStateTick">The last reconcile's client tick (<see cref="ReconcilePairing"/>), or <see cref="Unset"/>.</param>
		/// <param name="serverStateTick">The last reconcile's server tick (<see cref="ReconcilePairing"/>), or <see cref="Unset"/>.</param>
		/// <param name="inputTick">The replicate's own tick, in the owning client's domain.</param>
		/// <returns>
		/// A tick in the weather timeline's (synchronised) domain. Zero is a real answer — it is the
		/// first tick of the timeline, which is where a mapping that would fall below it is clamped —
		/// and is also what <see cref="Unset"/> happens to be, since FishNet's sentinel is literally
		/// zero. Nothing downstream needs to tell the two apart: both mean "read the oldest weather".
		/// </returns>
		public static uint Resolve(
			bool isServer,
			uint serverTick,
			uint clientSyncTick,
			uint clientStateTick,
			uint serverStateTick,
			uint inputTick)
		{
			if (isServer)
			{
				// The server's replicate runs during its own tick; the queued input's label is the
				// owner's counter and means nothing here.
				return serverTick == Unset ? 0u : serverTick;
			}

			// No reconcile yet (the first seconds after spawn), or a replicate with no real input:
			// the client's own synchronised estimate is the best available answer, and it is the
			// same quantity the server will be using.
			if (clientStateTick == Unset || serverStateTick == Unset || inputTick == Unset)
			{
				return clientSyncTick == Unset ? 0u : clientSyncTick;
			}

			/* The pairing, extrapolated.
			 *
			 * The difference is taken in WRAPPED 32-bit arithmetic and then read as signed, which is
			 * the standard way to difference sequence numbers and is not the same as subtracting two
			 * longs. It has to be, for two reasons. An input tick may be either side of the
			 * reconciled one — behind it while replaying, ahead of it while predicting forward — so
			 * the result must be able to go negative. And a counter that has wrapped past
			 * uint.MaxValue is only a few ticks after one that has not, yet a plain subtraction of
			 * the two calls it four billion ticks BEFORE: at thirty ticks a second that is a reading
			 * of the weather four years out, or, once clamped, of the world's first moment. Wrapped,
			 * the same subtraction gives the few ticks it really is. */
			int elapsed = unchecked((int)(inputTick - clientStateTick));
			long mapped = (long)serverStateTick + elapsed;
			return mapped <= 0L ? 0u : (uint)mapped;
		}

		/// <summary>
		/// <see cref="Resolve"/> for a behaviour on a predicted object, reading the clocks off its own
		/// managers. Every predicted thing that reads the weather goes through this, so there is one
		/// place the two tick domains are reconciled and one place to get it wrong.
		/// </summary>
		/// <param name="behaviour">The behaviour being replicated.</param>
		/// <param name="inputTick">The replicate's own tick, in the owning client's domain.</param>
		public static uint Resolve(NetworkBehaviour behaviour, uint inputTick)
		{
			if (behaviour == null)
			{
				return 0u;
			}

			TimeManager time = behaviour.TimeManager;
			bool isServer = behaviour.IsServerStarted;

			uint serverTick = time != null ? time.Tick : Unset;
			// The client's own estimate of the synchronised tick is the same quantity, and is the
			// only thing available before a first reconcile has established the pairing.
			uint clientSyncTick = serverTick;
			uint clientStateTick = Unset;
			uint serverStateTick = Unset;

			if (!isServer && behaviour.PredictionManager != null)
			{
				ReconcilePairing.Get(behaviour.PredictionManager, behaviour.ClientManager, out clientStateTick, out serverStateTick);
			}

			return Resolve(isServer, serverTick, clientSyncTick, clientStateTick, serverStateTick, inputTick);
		}

		/// <summary>
		/// What <see cref="Resolve"/> must return while FishNet is replaying, from FishNet's own
		/// replay counters. Equal to the formula by construction — see the remarks on this class —
		/// so it exists to be asserted against rather than to be used in its place.
		/// </summary>
		/// <remarks>
		/// Not used by the controller. Taking the replay counter directly would work, but it is only
		/// valid inside a replay, so the controller would need two code paths where one will do —
		/// and a second path is a second thing to get wrong.
		/// </remarks>
		public static uint FromReplayCounters(uint serverReplayTick) => serverReplayTick;

		/// <summary>
		/// True when the formula and FishNet's replay counter agree for one replayed step. The
		/// closed-loop check, in one place, for tests and for a debug assertion.
		/// </summary>
		public static bool ReplayAgrees(
			uint clientStateTick,
			uint serverStateTick,
			uint clientReplayTick,
			uint serverReplayTick)
		{
			if (clientStateTick == Unset || serverStateTick == Unset ||
				clientReplayTick == Unset || serverReplayTick == Unset)
			{
				return true;
			}

			uint predicted = Resolve(false, Unset, Unset, clientStateTick, serverStateTick, clientReplayTick);
			return predicted == serverReplayTick;
		}

		/// <summary>
		/// The (client, server) tick pairing of the last reconcile, per <see cref="PredictionManager"/>.
		/// </summary>
		/// <remarks>
		/// FishNet 4.7 clears <c>ClientStateTick</c>/<c>ServerStateTick</c> once a reconcile ends; this
		/// keeps the last pair so the live tick maps through the same offset the replay used. Inside a
		/// reconcile the PredictionManager's own values are read directly (they are the pairing being
		/// replayed), which also covers the first reconcile after a lazy subscription. Client only: the
		/// server never asks.
		/// </remarks>
		internal static class ReconcilePairing
		{
			private sealed class Entry
			{
				public PredictionManager PredictionManager;
				public ClientManager ClientManager;
				public uint ClientTick = Unset;
				public uint ServerTick = Unset;

				public void OnPreReconcile(uint clientTick, uint serverTick)
				{
					ClientTick = clientTick;
					ServerTick = serverTick;
				}

				public void OnClientConnectionState(ClientConnectionStateArgs args)
				{
					// LocalTick restarts at zero on the next connection; an old pairing would map it
					// into the wrong part of the timeline until the first reconcile.
					if (args.ConnectionState != LocalConnectionState.Started)
					{
						ClientTick = Unset;
						ServerTick = Unset;
					}
				}
			}

			private static readonly Dictionary<PredictionManager, Entry> entries = new Dictionary<PredictionManager, Entry>();
			private static readonly List<PredictionManager> stale = new List<PredictionManager>();

			/// <summary>The pairing to map a client input tick through, or <see cref="Unset"/> twice.</summary>
			public static void Get(PredictionManager predictionManager, ClientManager clientManager, out uint clientStateTick, out uint serverStateTick)
			{
				Entry entry = Track(predictionManager, clientManager);
				if (predictionManager.IsReconciling)
				{
					clientStateTick = predictionManager.ClientStateTick;
					serverStateTick = predictionManager.ServerStateTick;
					if (entry != null && clientStateTick != Unset && serverStateTick != Unset)
					{
						entry.ClientTick = clientStateTick;
						entry.ServerTick = serverStateTick;
					}
					return;
				}

				clientStateTick = entry != null ? entry.ClientTick : Unset;
				serverStateTick = entry != null ? entry.ServerTick : Unset;
			}

			/// <summary>Forgets every pairing and unsubscribes. For tests and domain reload.</summary>
			[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
			internal static void Clear()
			{
				foreach (Entry entry in entries.Values)
				{
					Unsubscribe(entry);
				}
				entries.Clear();
			}

			private static Entry Track(PredictionManager predictionManager, ClientManager clientManager)
			{
				if (predictionManager == null)
				{
					return null;
				}
				if (entries.TryGetValue(predictionManager, out Entry entry))
				{
					if (entry.ClientManager == null && clientManager != null)
					{
						entry.ClientManager = clientManager;
						clientManager.OnClientConnectionState += entry.OnClientConnectionState;
					}
					return entry;
				}

				// A new manager: drop any that Unity has destroyed (scene change, reconnect with a
				// fresh NetworkManager) so the table cannot grow without bound.
				foreach (KeyValuePair<PredictionManager, Entry> kvp in entries)
				{
					if (kvp.Key == null)
					{
						stale.Add(kvp.Key);
					}
				}
				for (int i = 0; i < stale.Count; ++i)
				{
					entries.Remove(stale[i]);
				}
				stale.Clear();

				entry = new Entry { PredictionManager = predictionManager };
				predictionManager.OnPreReconcile += entry.OnPreReconcile;
				if (clientManager != null)
				{
					entry.ClientManager = clientManager;
					clientManager.OnClientConnectionState += entry.OnClientConnectionState;
				}
				entries.Add(predictionManager, entry);
				return entry;
			}

			private static void Unsubscribe(Entry entry)
			{
				// Unity-null after destroy; the C# events are plain delegates, safe either way.
				if (!ReferenceEquals(entry.PredictionManager, null))
				{
					entry.PredictionManager.OnPreReconcile -= entry.OnPreReconcile;
				}
				if (!ReferenceEquals(entry.ClientManager, null))
				{
					entry.ClientManager.OnClientConnectionState -= entry.OnClientConnectionState;
				}
			}
		}
	}
}
