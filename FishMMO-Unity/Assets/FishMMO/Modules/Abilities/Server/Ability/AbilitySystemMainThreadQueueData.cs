using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Concrete main-thread queue data container for AbilitySystem.
	/// Inherits thread-safe Queue + lock infrastructure from SystemMainThreadQueueData.
	/// </summary>
	public class AbilitySystemMainThreadQueueData : SystemMainThreadQueueData, IAbilitySystemMainThreadQueueData
	{
	}
}
