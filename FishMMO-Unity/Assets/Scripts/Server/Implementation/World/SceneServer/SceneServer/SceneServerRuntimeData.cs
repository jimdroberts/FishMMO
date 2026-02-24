using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using System;
using System.Collections.Generic;
using System.Threading;

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

		private int pulseInFlight;

		/// <inheritdoc/>
		public bool TryBeginPulse()
		{
			return Interlocked.CompareExchange(ref pulseInFlight, 1, 0) == 0;
		}

		/// <inheritdoc/>
		public void EndPulse()
		{
			Interlocked.Exchange(ref pulseInFlight, 0);
		}

		/// <inheritdoc/>
		public Dictionary<long, DateTime> PendingSceneEnqueueUtcBySceneId { get; private set; }

		/// <inheritdoc/>
		public DateTime NextPendingSceneSweepUtc { get; set; }

		/// <inheritdoc/>
		public List<(int Handle, int CharacterCount, bool StalePulse, double TimeSinceLastExit)> ScenePulseDataBuffer { get; private set; }

		/// <inheritdoc/>
		public List<int> ScenesToUnloadBuffer { get; private set; }

		/// <inheritdoc/>
		public List<Dictionary<int, ISceneInstanceDetails>> SceneGroupValuesBuffer { get; private set; }

		/// <inheritdoc/>
		public List<ISceneInstanceDetails> SceneDetailsValuesBuffer { get; private set; }

		/// <inheritdoc/>
		public List<long> ExpiredSceneIdsBuffer { get; private set; }

		/// <summary>
		/// Initializes the scene server runtime data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			PendingSceneEnqueueUtcBySceneId = new Dictionary<long, DateTime>();
			NextPendingSceneSweepUtc = DateTime.UtcNow;
			ScenePulseDataBuffer = new List<(int, int, bool, double)>();
			ScenesToUnloadBuffer = new List<int>();
			SceneGroupValuesBuffer = new List<Dictionary<int, ISceneInstanceDetails>>();
			SceneDetailsValuesBuffer = new List<ISceneInstanceDetails>();
			ExpiredSceneIdsBuffer = new List<long>();
			Interlocked.Exchange(ref pulseInFlight, 0);
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all scene server runtime data.
		/// </summary>
		public override void Clear()
		{
			ID = 0;
			IsLocked = false;
			Interlocked.Exchange(ref pulseInFlight, 0);
			PendingSceneEnqueueUtcBySceneId?.Clear();
			NextPendingSceneSweepUtc = DateTime.UtcNow;
			ScenePulseDataBuffer?.Clear();
			ScenesToUnloadBuffer?.Clear();
			SceneGroupValuesBuffer?.Clear();
			SceneDetailsValuesBuffer?.Clear();
			ExpiredSceneIdsBuffer?.Clear();
		}

		/// <summary>
		/// Deinitializes the scene server runtime data container.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
			PendingSceneEnqueueUtcBySceneId = null;
			ScenePulseDataBuffer = null;
			ScenesToUnloadBuffer = null;
			SceneGroupValuesBuffer = null;
			SceneDetailsValuesBuffer = null;
			ExpiredSceneIdsBuffer = null;
		}
	}
}