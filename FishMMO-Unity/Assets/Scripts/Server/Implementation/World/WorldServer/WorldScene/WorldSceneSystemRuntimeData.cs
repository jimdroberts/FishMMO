using System;
using System.Collections.Generic;
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
		public int IsProcessingQueue { get; set; }
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
			IsProcessingQueue = 0;
			WaitQueueRateSeconds = 2.0f;
			NextWaitingQueueSweep = 0.0f;
			NextDebounceCleanup = 0.0f;
			LoginAuthenticator = null;
			NextWaitQueueUpdate = 0.0f;
			InstanceLookupDebounce = new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);
			WaitingQueueEnteredUtcByClientId = new Dictionary<int, DateTime>();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the runtime data. Called when resetting state between sessions.
		/// </summary>
		public override void Clear()
		{
			IsProcessingQueue = 0;
			WaitQueueRateSeconds = 2.0f;
			NextWaitingQueueSweep = 0.0f;
			NextDebounceCleanup = 0.0f;
			LoginAuthenticator = null;
			NextWaitQueueUpdate = 0.0f;
			InstanceLookupDebounce?.Clear();
			WaitingQueueEnteredUtcByClientId?.Clear();
		}

		/// <summary>
		/// Deinitializes the runtime data. Called when shutting down the server.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
			InstanceLookupDebounce = null;
			WaitingQueueEnteredUtcByClientId = null;
		}
	}
}