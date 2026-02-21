using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Concrete main-thread queue data container for InteractableSystem.
	/// Inherits thread-safe Queue + lock infrastructure from MainThreadQueueData.
	/// </summary>
	public class InteractableSystemMainThreadQueueData : MainThreadQueueData, IInteractableSystemMainThreadQueueData
	{
	}
}