using System;
using System.Collections.Generic;
using System.Threading;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for hotkey ingress protection and pending hotkey persistence.
	/// </summary>
	public class HotkeySystemRuntimeData : RuntimeDataContainer, IHotkeySystemRuntimeData
	{
		/// <summary>
		/// Ingress guard for debouncing and rate-limiting hotkey operation requests.
		/// </summary>
		public IngressGuard IngressGuard { get; private set; }

		/// <summary>
		/// Newest un-written hotkey snapshot per character.
		/// </summary>
		/// <remarks>
		/// Main-thread only: every writer is a broadcast handler or the periodic pump, both of
		/// which run on the main thread. The DRAINED copies are what cross onto the persistence
		/// worker, so no shared mutable state leaves this object.
		/// </remarks>
		private readonly Dictionary<long, HotkeyData[]> pendingWrites = new Dictionary<long, HotkeyData[]>();

		/// <summary>
		/// Running maximum of every version handed out, so versions stay strictly increasing.
		/// </summary>
		private long lastVersion;

		/// <summary>
		/// Initializes the hotkey system runtime data, creating the ingress guard.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			IngressGuard = new IngressGuard();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public void StageHotkeyWrite(long characterID, IReadOnlyList<HotkeyData> hotkeys)
		{
			if (characterID <= 0 || hotkeys == null || hotkeys.Count == 0)
			{
				return;
			}

			HotkeyData[] snapshot = new HotkeyData[hotkeys.Count];
			for (int i = 0; i < hotkeys.Count; ++i)
			{
				snapshot[i] = hotkeys[i];
			}

			pendingWrites[characterID] = snapshot;
		}

		/// <inheritdoc />
		public bool DrainHotkeyWrites(List<KeyValuePair<long, HotkeyData[]>> destination)
		{
			if (destination == null || pendingWrites.Count == 0)
			{
				return false;
			}

			foreach (KeyValuePair<long, HotkeyData[]> pair in pendingWrites)
			{
				destination.Add(pair);
			}
			pendingWrites.Clear();
			return true;
		}

		/// <inheritdoc />
		public bool TryDrainHotkeyWrite(long characterID, out HotkeyData[] hotkeys)
		{
			if (pendingWrites.TryGetValue(characterID, out hotkeys))
			{
				pendingWrites.Remove(characterID);
				return true;
			}
			hotkeys = null;
			return false;
		}

		/// <inheritdoc />
		public long NextHotkeyVersion()
		{
			long candidate = DateTime.UtcNow.Ticks;

			// Interlocked because a persistence continuation could in principle ask for a version
			// off the worker thread; the loop is the standard monotonic-max CAS.
			while (true)
			{
				long current = Interlocked.Read(ref lastVersion);
				long next = candidate > current ? candidate : current + 1;
				if (Interlocked.CompareExchange(ref lastVersion, next, current) == current)
				{
					return next;
				}
			}
		}

		/// <summary>
		/// Clears the ingress guard and any staged hotkey writes.
		/// </summary>
		public override void Clear()
		{
			IngressGuard?.Clear();
			pendingWrites.Clear();
		}

		/// <summary>
		/// Deinitializes the runtime data, clearing ingress guard state.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
		}
	}
}
