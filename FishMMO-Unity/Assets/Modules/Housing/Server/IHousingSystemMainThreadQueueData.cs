namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Marker interface for the HousingSystem main-thread action queue.
	/// Isolates housing queued actions from other subsystem queues.
	/// </summary>
	public interface IHousingSystemMainThreadQueueData : IMainThreadQueueData
	{
	}
}
