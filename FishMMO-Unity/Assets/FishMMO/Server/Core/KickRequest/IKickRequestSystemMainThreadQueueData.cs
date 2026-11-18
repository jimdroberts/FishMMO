namespace FishMMO.Server.Core
{
	/// <summary>
	/// Main-thread queue data interface for KickRequestSystem.
	/// Separate interface ensures the DataContainerRegistry maps this
	/// independently from other systems' main-thread queues.
	/// </summary>
	public interface IKickRequestSystemMainThreadQueueData : IMainThreadQueueData
	{
	}
}