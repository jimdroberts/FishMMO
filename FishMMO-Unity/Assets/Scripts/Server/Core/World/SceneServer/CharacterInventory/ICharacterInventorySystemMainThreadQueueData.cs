namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Per-system main-thread queue interface for CharacterInventorySystem.
	/// Ensures this system gets its own slot in the DataContainerRegistry
	/// without colliding with other systems that also use IMainThreadQueueData.
	/// </summary>
	/// <remarks>
	/// Added for one job: carrying database-assigned item identities back onto the live
	/// <c>Item</c> objects. The write happens on an async worker, and every container in this
	/// system is main-thread only — see the memory/database invariant on
	/// <c>CharacterInventorySystem</c>.
	/// </remarks>
	public interface ICharacterInventorySystemMainThreadQueueData : IMainThreadQueueData
	{
	}
}
