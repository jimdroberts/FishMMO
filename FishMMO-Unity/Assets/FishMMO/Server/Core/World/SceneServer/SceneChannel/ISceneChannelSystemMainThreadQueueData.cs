namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Marker interface for the SceneChannelSystem main-thread action queue.
	/// Isolates channel system queued actions from other subsystem queues.
	/// </summary>
	public interface ISceneChannelSystemMainThreadQueueData : IMainThreadQueueData
	{
	}
}