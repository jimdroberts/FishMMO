using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.Character
{
	/// <summary>
	/// Concrete main-thread queue data container for CharacterSystem.
	/// Inherits thread-safe Queue + lock infrastructure from MainThreadQueueData.
	/// </summary>
	public class CharacterSystemMainThreadQueueData : MainThreadQueueData, ICharacterSystemMainThreadQueueData
	{
	}
}