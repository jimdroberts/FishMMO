namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Per-system main-thread queue interface for AbilitySystem.
	/// Ensures this system gets its own slot in the DataContainerRegistry
	/// without colliding with other systems that also use IMainThreadQueueData.
	/// </summary>
	/// <remarks>
	/// Added for one job: carrying the granting half of a crafted ability back onto the main thread.
	/// The identity is minted on an async worker — the database assigns it inside the upsert's
	/// RETURNING clause — and every structure that has to receive it (the <c>Ability</c> itself, the
	/// ability controller, the broadcast) is main-thread only.
	/// </remarks>
	public interface IAbilitySystemMainThreadQueueData : IMainThreadQueueData
	{
	}
}
