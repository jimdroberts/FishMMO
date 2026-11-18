namespace FishMMO.Server.Core.World.WorldServer
{
	/// <summary>
	/// Runtime data container interface for WorldSceneSystem state.
	/// Stores mutable state separate from the system logic.
	/// </summary>
	public interface IWorldSceneSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Atomically attempts to begin queue processing.
		/// </summary>
		/// <returns><c>true</c> if processing was started; otherwise <c>false</c>.</returns>
		bool TryBeginProcessing();

		/// <summary>
		/// Atomically ends queue processing.
		/// </summary>
		void EndProcessing();

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