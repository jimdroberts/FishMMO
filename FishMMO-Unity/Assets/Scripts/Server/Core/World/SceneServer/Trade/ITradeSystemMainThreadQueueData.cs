namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Marker for the trade system's main-thread queue, so it does not share a registry slot
	/// with another system's queue.
	/// </summary>
	public interface ITradeSystemMainThreadQueueData : IMainThreadQueueData
	{
	}
}
