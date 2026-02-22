namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for friend ingress guards.
	/// </summary>
	public interface IFriendSystemRuntimeData : IRuntimeDataContainer
	{
		IngressGuard IngressGuard { get; }
	}
}