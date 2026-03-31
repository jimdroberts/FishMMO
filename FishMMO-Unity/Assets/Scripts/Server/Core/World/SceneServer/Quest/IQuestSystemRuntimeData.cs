namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for quest system ingress guards.
	/// </summary>
	public interface IQuestSystemRuntimeData : IRuntimeDataContainer
	{
		IngressGuard IngressGuard { get; }
	}
}
