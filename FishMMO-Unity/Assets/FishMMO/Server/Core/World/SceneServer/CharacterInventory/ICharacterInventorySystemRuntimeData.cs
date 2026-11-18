namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for character inventory ingress guards.
	/// </summary>
	public interface ICharacterInventorySystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Shared ingress guard for per-connection per-operation debounce and in-flight tracking.
		/// </summary>
		IngressGuard IngressGuard { get; }
	}
}