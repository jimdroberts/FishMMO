namespace FishMMO.Server.Core.World.SceneServer
{
	using System;
	using System.Collections.Generic;

	/// <summary>
	/// Runtime data container for scene server identification and state.
	/// Provides access to scene server operational state.
	/// </summary>
	public interface ISceneServerRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Database ID for this scene server instance.
		/// </summary>
		long ID { get; set; }

		/// <summary>
		/// Indicates whether the scene server is locked (not accepting new connections).
		/// </summary>
		bool IsLocked { get; set; }

		/// <summary>
		/// Atomic in-flight gate for periodic pulse work.
		/// </summary>
		int PulseInFlight { get; set; }

		/// <summary>
		/// Tracks UTC enqueue timestamps for pending scene load requests.
		/// </summary>
		Dictionary<long, DateTime> PendingSceneEnqueueUtcBySceneId { get; }

		/// <summary>
		/// Next UTC timestamp when pending-scene cleanup is allowed.
		/// </summary>
		DateTime NextPendingSceneSweepUtc { get; set; }
	}
}
