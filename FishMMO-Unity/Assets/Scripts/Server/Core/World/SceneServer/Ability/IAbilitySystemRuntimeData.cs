namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for ability request ingress guards.
	/// </summary>
	public interface IAbilitySystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Shared ingress guard for per-connection per-operation debounce and in-flight tracking.
		/// </summary>
		/// <remarks>
		/// The forget path holds its guard until the delete lands rather than until the handler
		/// returns, so this guard is doing more here than the usual request debounce: a second
		/// forget for the same ability, arriving while the first is still in flight, is refused
		/// outright instead of racing it.
		/// </remarks>
		IngressGuard IngressGuard { get; }
	}
}
