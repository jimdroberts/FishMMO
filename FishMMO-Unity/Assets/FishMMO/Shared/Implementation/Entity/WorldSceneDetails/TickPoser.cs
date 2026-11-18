using System.Collections.Generic;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Predicting;
using FishNet.Managing.Timing;
using FishMMO.Shared.Weather;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>Something a character collides with whose pose or solidity is a function of the synced tick.</summary>
	public interface ITickPosed
	{
		/// <summary>
		/// Puts this object where (and as solid as) it is at server tick <paramref name="syncTick"/>. True when a transform or
		/// collider changed, so the caller syncs transforms once for all.
		/// </summary>
		bool PoseForTick(uint syncTick, double tickDelta);
	}

	/// <summary>
	/// Poses every <see cref="ITickPosed"/> before each tick a character is simulated at: the live tick
	/// (TimeManager.OnPreTick), each tick a client replays (PredictionManager.OnPreReplicateReplay) and the live tick again
	/// after a reconcile (OnPostReconcile), each mapped to the server tick it runs at exactly as the ships are
	/// (<see cref="FloatingBoats"/>, <see cref="WeatherExposureTick"/>), then Physics.SyncTransforms once.
	/// </summary>
	/// <remarks>
	/// Anything a predicted character collides with has to be where it was on the tick the character is simulated at, on
	/// the server, on the owner's live tick and on every tick the owner replays; otherwise the owner predicts against a
	/// world the server did not have and is corrected. Posing from a frame (Update, LateUpdate) does neither: a frame's
	/// idea of the tick is not the owner's tick, and a replay never rewinds it. One subscription per process, however
	/// many scenes and objects (a scene server hosts several scenes).
	/// </remarks>
	public static class TickPoser
	{
		private static readonly List<ITickPosed> all = new List<ITickPosed>();
		private static NetworkManager network;
		private static TimeManager time;
		private static PredictionManager prediction;
		private static bool attached;

		/// <summary>Registers an object (OnEnable).</summary>
		public static void Add(ITickPosed posed)
		{
			if (posed != null && !all.Contains(posed))
			{
				all.Add(posed);
			}
			Ensure();
		}

		/// <summary>Unregisters an object (OnDisable).</summary>
		public static void Remove(ITickPosed posed)
		{
			all.Remove(posed);
			if (all.Count == 0)
			{
				Detach();
			}
		}

		/// <summary>Subscribes to the network's tick if it has not yet (the network may start after the objects).</summary>
		public static void Ensure()
		{
			if (attached && network != null)
			{
				return;
			}
			Detach();
			if (all.Count == 0 || NetworkManager.Instances.Count == 0)
			{
				return;
			}
			NetworkManager nm = InstanceFinder.NetworkManager;
			PredictionManager pm = nm != null ? InstanceFinder.PredictionManager : null;
			if (nm == null || nm.TimeManager == null || pm == null)
			{
				return;
			}
			network = nm;
			time = nm.TimeManager;
			prediction = pm;
			time.OnPreTick += OnPreTick;
			prediction.OnPreReplicateReplay += OnPreReplicateReplay;
			prediction.OnPostReconcile += OnPostReconcile;
			attached = true;
		}

		private static void Detach()
		{
			// Plain C# events: safe to unsubscribe from a destroyed manager.
			if (!ReferenceEquals(time, null))
			{
				time.OnPreTick -= OnPreTick;
			}
			if (!ReferenceEquals(prediction, null))
			{
				prediction.OnPreReplicateReplay -= OnPreReplicateReplay;
				prediction.OnPostReconcile -= OnPostReconcile;
			}
			network = null;
			time = null;
			prediction = null;
			attached = false;
		}

		/// <summary>The synced (server) tick this peer simulates <paramref name="localTick"/> at.</summary>
		private static uint SyncTickOf(uint localTick)
		{
			if (network.IsServerStarted)
			{
				return time.Tick;
			}
			WeatherExposureTick.ReconcilePairing.Get(prediction, network.ClientManager, out uint clientStateTick, out uint serverStateTick);
			return WeatherExposureTick.Resolve(false, WeatherExposureTick.Unset, time.Tick, clientStateTick, serverStateTick, localTick);
		}

		private static void OnPreTick() => PoseAll(SyncTickOf(time.LocalTick));

		private static void OnPreReplicateReplay(uint clientTick, uint serverTick) => PoseAll(SyncTickOf(clientTick));

		private static void OnPostReconcile(uint clientTick, uint serverTick) => PoseAll(SyncTickOf(time.LocalTick));

		private static void PoseAll(uint tick)
		{
			if (network == null)
			{
				attached = false;
				return;
			}
			double tickDelta = time.TickDelta;
			bool changed = false;
			for (int i = all.Count - 1; i >= 0; i--)
			{
				ITickPosed posed = all[i];
				if (posed == null || (posed is Object unityObject && unityObject == null))
				{
					all.RemoveAt(i);
					continue;
				}
				changed |= posed.PoseForTick(tick, tickDelta);
			}
			// Automatic transform sync is off: without this the character controller would test last tick's poses.
			if (changed)
			{
				Physics.SyncTransforms();
			}
		}
	}
}
