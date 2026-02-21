namespace FishMMO.Server.Core.LoginServer
{
	/// <summary>
	/// Main-thread queue data interface for AccountCreationSystem.
	/// Separate interface ensures the DataContainerRegistry maps this
	/// independently from other systems' main-thread queues.
	/// </summary>
	public interface IAccountCreationSystemMainThreadQueueData : IMainThreadQueueData
	{
	}
}