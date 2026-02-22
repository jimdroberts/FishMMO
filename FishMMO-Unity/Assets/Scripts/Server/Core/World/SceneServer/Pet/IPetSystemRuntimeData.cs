namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for pet ingress guards.
	/// </summary>
	public interface IPetSystemRuntimeData : IRuntimeDataContainer
	{
		IngressGuard IngressGuard { get; }
	}
}