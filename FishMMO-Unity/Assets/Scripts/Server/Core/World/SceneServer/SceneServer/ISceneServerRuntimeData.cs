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
		/// Atomically transitions the pulse gate from idle to in-flight.
		/// Returns true if this call won the race; false if a pulse is already in flight.
		/// </summary>
		bool TryBeginPulse();

		/// <summary>
		/// Atomically transitions the pulse gate from in-flight back to idle.
		/// </summary>
		void EndPulse();

		/// <summary>
		/// Next UTC timestamp when pending-scene cleanup is allowed.
		/// </summary>
		DateTime NextPendingSceneSweepUtc { get; set; }

		/// <summary>
		/// Reusable buffer for scene pulse payload data (Handle, CharacterCount).
		/// Only used from the main thread. Snapshotted before passing to async.
		/// </summary>
		List<(int Handle, int CharacterCount)> ScenePulseDataBuffer { get; }

		/// <summary>
		/// Reusable buffer for scene handles queued for unloading. Only used from the main thread.
		/// </summary>
		List<int> ScenesToUnloadBuffer { get; }

		/// <summary>
		/// Reusable buffer for iterating scene group value collections. Only used from the main thread.
		/// </summary>
		List<Dictionary<int, ISceneInstanceDetails>> SceneGroupValuesBuffer { get; }

		/// <summary>
		/// Reusable buffer for iterating scene instance details. Only used from the main thread.
		/// </summary>
		List<ISceneInstanceDetails> SceneDetailsValuesBuffer { get; }

		/// <summary>
		/// Reusable buffer for expired pending scene IDs during cleanup sweeps. Only used from the main thread.
		/// </summary>
		List<long> ExpiredSceneIdsBuffer { get; }

		/// <summary>
		/// Reusable buffer for scene handles during unload iteration. Only used from the main thread.
		/// </summary>
		List<int> UnloadedHandlesBuffer { get; }
	}
}