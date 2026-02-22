namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for character inventory ingress guards.
	/// </summary>
	public interface ICharacterInventorySystemRuntimeData : IRuntimeDataContainer
	{
		IngressGuard IngressGuard { get; }
	}
}