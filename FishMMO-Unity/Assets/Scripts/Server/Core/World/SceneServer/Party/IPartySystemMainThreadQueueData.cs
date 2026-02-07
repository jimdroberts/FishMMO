namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Per-system main-thread queue interface for PartySystem.
	/// Ensures this system gets its own slot in the DataContainerRegistry
	/// without colliding with other systems that also use IMainThreadQueueData.
	/// </summary>
	public interface IPartySystemMainThreadQueueData : IMainThreadQueueData
	{
	}
}
