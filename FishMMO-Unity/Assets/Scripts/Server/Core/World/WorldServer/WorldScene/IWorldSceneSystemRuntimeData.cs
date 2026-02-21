namespace FishMMO.Server.Core.World.WorldServer
{
	/// <summary>
	/// Runtime data container interface for WorldSceneSystem state.
	/// Stores mutable state separate from the system logic.
	/// </summary>
	public interface IWorldSceneSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Prevents overlapping async queue processing cycles.
		/// </summary>
		int IsProcessingQueue { get; set; }

		/// <summary>
		/// Interval (in seconds) between wait queue updates.
		/// </summary>
		float WaitQueueRateSeconds { get; set; }

		/// <summary>
		/// Time remaining until the next waiting-queue sweep.
		/// </summary>
		float NextWaitingQueueSweep { get; set; }

		/// <summary>
		/// Time remaining until the next debounce cleanup sweep.
		/// </summary>
		float NextDebounceCleanup { get; set; }

		/// <summary>
		/// Time remaining until the next wait queue update.
		/// </summary>
		float NextWaitQueueUpdate { get; set; }
	}
}