namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for interactable ingress guards and debounce tracker cleanup.
	/// </summary>
	public interface IInteractableSystemRuntimeData : IRuntimeDataContainer
	{
		IngressGuard IngressGuard { get; }
	}
}