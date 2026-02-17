using FishMMO.Server.Core.LoginServer;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Main-thread queue data container for AccountCreationSystem.
	/// Separate concrete type ensures the DataContainerRegistry creates
	/// an independent instance for this system.
	/// </summary>
	public class AccountCreationSystemMainThreadQueueData : SystemMainThreadQueueData, IAccountCreationSystemMainThreadQueueData
	{
	}
}