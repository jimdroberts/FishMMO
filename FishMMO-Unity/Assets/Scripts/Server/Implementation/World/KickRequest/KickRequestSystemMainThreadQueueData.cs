using FishMMO.Server.Core.World;

namespace FishMMO.Server.Implementation.World
{
	/// <summary>
	/// Main-thread queue data container for KickRequestSystem.
	/// Separate concrete type ensures the DataContainerRegistry creates
	/// an independent instance for this system.
	/// </summary>
	public class KickRequestSystemMainThreadQueueData : MainThreadQueueData, IKickRequestSystemMainThreadQueueData
	{
	}
}