namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for pet ingress guards.
	/// </summary>
	public interface IPetSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Shared ingress guard for per-connection per-operation debounce and in-flight tracking.
		/// </summary>
		IngressGuard IngressGuard { get; }
	}
}