using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Concrete main-thread queue data container for CharacterInventorySystem.
	/// Inherits thread-safe Queue + lock infrastructure from SystemMainThreadQueueData.
	/// </summary>
	public class CharacterInventorySystemMainThreadQueueData : SystemMainThreadQueueData, ICharacterInventorySystemMainThreadQueueData
	{
	}
}
