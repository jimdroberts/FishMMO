using System;
using System.Collections.Generic;
using System.Threading;
using FishMMO.Database.Data;
using FishMMO.Server.Core;
using FishMMO.Server.Core.Collections;
using FishMMO.Server.Core.World.WorldServer;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// Runtime data container for WorldSceneSystem state.
	/// Stores mutable state separate from the system logic.
	/// </summary>
	public class WorldSceneSystemRuntimeData : RuntimeDataContainer, IWorldSceneSystemRuntimeData
	{
		private int isProcessingQueue;

		/// <summary>
		/// Atomically attempts to begin queue processing.
		/// </summary>
		/// <returns><c>true</c> if processing was started; otherwise <c>false</c>.</returns>
		public bool TryBeginProcessing()
		{
			return Interlocked.CompareExchange(ref isProcessingQueue, 1, 0) == 0;
		}

		/// <summary>
		/// Atomically ends queue processing.
		/// </summary>
		public void EndProcessing()
		{
			Interlocked.Exchange(ref isProcessingQueue, 0);
		}
		public float WaitQueueRateSeconds { get; set; }
		public float NextWaitingQueueSweep { get; set; }
		public float NextDebounceCleanup { get; set; }

		/// <summary>
		/// Per-account debounce tracker for world-scene instance lookups.
		/// </summary>
		public ExpiringKeyTracker<string> InstanceLookupDebounce { get; set; }

		/// <summary>
		/// Tracks when each client entered a world-scene waiting queue.
		/// </summary>
		public Dictionary<int, DateTime> WaitingQueueEnteredUtcByClientId { get; set; }

		/// <summary>
		/// Cache of <c>FetchAvailableAsync</c> results keyed by scene name.
		/// Entries expire after a configurable TTL to reduce database polling.
		/// </summary>
		public TimedCache<string, IReadOnlyList<SceneData>> AvailableSceneCache { get; set; }

		/// <summary>
		/// Cache of scene server addresses keyed by scene server ID.
		/// Addresses change infrequently, so a longer TTL is appropriate.
		/// </summary>
		public TimedCache<long, (string Address, ushort Port)> SceneServerAddressCache { get; set; }

		/// <summary>
		/// Cached total character count across all scenes for this world server.
		/// Used by <c>UpdateConnectionCountAsync</c> to avoid re-fetching all scene rows every cycle.
		/// </summary>
		public int CachedSceneCharacterCount { get; set; }

		/// <summary>
		/// UTC timestamp of the last <see cref="CachedSceneCharacterCount"/> update.
		/// </summary>
		public DateTime CachedSceneCharacterCountUtc { get; set; }

		/// <summary>
		/// Reference to the world server authenticator for login/authentication events.
		/// </summary>
		public WorldServerAuthenticator LoginAuthenticator { get; set; }

		/// <summary>
		/// Time remaining until the next wait queue update.
		/// </summary>
		public float NextWaitQueueUpdate { get; set; }

		/// <summary>
		/// Initializes the runtime data once. Called when the data container is first set up.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			EndProcessing();
			WaitQueueRateSeconds = 2.0f;
			NextWaitingQueueSweep = 0.0f;
			NextDebounceCleanup = 0.0f;
			LoginAuthenticator = null;
			NextWaitQueueUpdate = 0.0f;
			InstanceLookupDebounce = new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);
			WaitingQueueEnteredUtcByClientId = new Dictionary<int, DateTime>();
			AvailableSceneCache = new TimedCache<string, IReadOnlyList<SceneData>>(StringComparer.OrdinalIgnoreCase);
			SceneServerAddressCache = new TimedCache<long, (string, ushort)>();
			CachedSceneCharacterCount = 0;
			CachedSceneCharacterCountUtc = DateTime.MinValue;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the runtime data. Called when resetting state between sessions.
		/// </summary>
		public override void Clear()
		{
			EndProcessing();
			WaitQueueRateSeconds = 2.0f;
			NextWaitingQueueSweep = 0.0f;
			NextDebounceCleanup = 0.0f;
			LoginAuthenticator = null;
			NextWaitQueueUpdate = 0.0f;
			InstanceLookupDebounce?.Clear();
			WaitingQueueEnteredUtcByClientId?.Clear();
			AvailableSceneCache?.Clear();
			SceneServerAddressCache?.Clear();
			CachedSceneCharacterCount = 0;
			CachedSceneCharacterCountUtc = DateTime.MinValue;
		}

		/// <summary>
		/// Deinitializes the runtime data. Called when shutting down the server.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
			InstanceLookupDebounce = null;
			WaitingQueueEnteredUtcByClientId = null;
			AvailableSceneCache?.Clear();
			AvailableSceneCache = null;
			SceneServerAddressCache?.Clear();
			SceneServerAddressCache = null;
		}
	}
}