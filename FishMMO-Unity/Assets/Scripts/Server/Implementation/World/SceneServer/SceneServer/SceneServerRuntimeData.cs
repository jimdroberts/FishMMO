using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using System;
using System.Collections.Generic;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for scene server identification and state.
	/// Manages scene server operational state separately from SceneServerSystem logic.
	/// </summary>
	public class SceneServerRuntimeData : RuntimeDataContainer, ISceneServerRuntimeData
	{
		/// <summary>
		/// Database ID for this scene server instance.
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// Indicates whether the scene server is locked (not accepting new connections).
		/// </summary>
		public bool IsLocked { get; set; }

		/// <inheritdoc/>
		public int PulseInFlight { get; set; }

		/// <inheritdoc/>
		public Dictionary<long, DateTime> PendingSceneEnqueueUtcBySceneId { get; private set; }

		/// <inheritdoc/>
		public DateTime NextPendingSceneSweepUtc { get; set; }

		/// <summary>
		/// Initializes the scene server runtime data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			PendingSceneEnqueueUtcBySceneId = new Dictionary<long, DateTime>();
			NextPendingSceneSweepUtc = DateTime.UtcNow;
			PulseInFlight = 0;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all scene server runtime data.
		/// </summary>
		public override void Clear()
		{
			ID = 0;
			IsLocked = false;
			PulseInFlight = 0;
			PendingSceneEnqueueUtcBySceneId?.Clear();
			NextPendingSceneSweepUtc = DateTime.UtcNow;
		}

		/// <summary>
		/// Deinitializes the scene server runtime data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
			PendingSceneEnqueueUtcBySceneId = null;
		}
	}
}