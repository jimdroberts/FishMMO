using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Concrete main-thread queue data container for SceneServerSystem.
	/// Provides a thread-safe queue for marshalling async DB results back to the main Unity thread.
	/// </summary>
	public class SceneServerSystemMainThreadQueueData : SystemMainThreadQueueData, ISceneServerSystemMainThreadQueueData
	{
	}
}