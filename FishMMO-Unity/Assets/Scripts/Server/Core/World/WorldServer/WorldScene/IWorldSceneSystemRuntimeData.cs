namespace FishMMO.Server.Core.World.WorldServer
{
	/// <summary>
	/// Runtime data container interface for WorldSceneSystem state.
	/// Stores mutable state separate from the system logic.
	/// </summary>
	public interface IWorldSceneSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Time remaining until the next wait queue update.
		/// </summary>
		float NextWaitQueueUpdate { get; set; }
	}
}