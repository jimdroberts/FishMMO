using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Concrete main-thread queue data container for FriendSystem.
	/// Inherits thread-safe Queue + lock infrastructure from SystemMainThreadQueueData.
	/// </summary>
	public class FriendSystemMainThreadQueueData : SystemMainThreadQueueData, IFriendSystemMainThreadQueueData
	{
	}
}