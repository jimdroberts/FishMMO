using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Concrete main-thread queue data container for ChatSystem.
	/// Inherits thread-safe Queue + lock infrastructure from MainThreadQueueData.
	/// </summary>
	public class ChatSystemMainThreadQueueData : MainThreadQueueData, IChatSystemMainThreadQueueData
	{
	}
}