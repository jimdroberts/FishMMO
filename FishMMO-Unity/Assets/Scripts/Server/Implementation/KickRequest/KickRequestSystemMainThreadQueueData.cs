using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Main-thread queue data container for KickRequestSystem.
	/// Separate concrete type ensures the DataContainerRegistry creates
	/// an independent instance for this system.
	/// </summary>
	public class KickRequestSystemMainThreadQueueData : SystemMainThreadQueueData, IKickRequestSystemMainThreadQueueData
	{
	}
}