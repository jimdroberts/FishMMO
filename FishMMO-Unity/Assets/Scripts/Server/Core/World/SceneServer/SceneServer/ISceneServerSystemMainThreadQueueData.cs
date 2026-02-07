using FishMMO.Server.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Per-system main-thread queue data interface for SceneServerSystem.
	/// Extends IMainThreadQueueData to provide a dedicated queue for marshalling
	/// async DB results back to the main Unity thread.
	/// </summary>
	public interface ISceneServerSystemMainThreadQueueData : IMainThreadQueueData
	{
	}
}
