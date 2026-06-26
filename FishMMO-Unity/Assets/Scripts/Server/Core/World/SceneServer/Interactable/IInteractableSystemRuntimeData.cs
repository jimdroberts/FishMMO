namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for interactable ingress guards and debounce tracker cleanup.
	/// </summary>
	public interface IInteractableSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Shared ingress guard for per-connection per-operation debounce and in-flight tracking.
		/// </summary>
		IngressGuard IngressGuard { get; }
	}
}