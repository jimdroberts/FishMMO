using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>Main-thread queue for <see cref="MatchmakingSystem"/>'s async replies.</summary>
	public class MatchmakingSystemMainThreadQueueData : SystemMainThreadQueueData, IMatchmakingSystemMainThreadQueueData
	{
	}
}
