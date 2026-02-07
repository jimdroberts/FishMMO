using FishMMO.Server.Core;
using FishMMO.Server.Core.World.WorldServer;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// Concrete main-thread queue data container for WorldSceneSystem.
	/// Provides a thread-safe queue for marshalling async DB results back to the main Unity thread.
	/// </summary>
	public class WorldSceneSystemMainThreadQueueData : MainThreadQueueData, IWorldSceneSystemMainThreadQueueData
	{
	}
}
