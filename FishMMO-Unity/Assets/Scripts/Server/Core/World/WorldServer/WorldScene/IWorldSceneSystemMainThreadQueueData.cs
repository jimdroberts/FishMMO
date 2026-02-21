namespace FishMMO.Server.Core.World.WorldServer
{
	/// <summary>
	/// Per-system main-thread queue data interface for WorldSceneSystem.
	/// Extends IMainThreadQueueData to provide a dedicated queue for marshalling
	/// async DB results back to the main Unity thread.
	/// </summary>
	public interface IWorldSceneSystemMainThreadQueueData : IMainThreadQueueData
	{
	}
}