namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for hotkey ingress guards.
	/// </summary>
	public interface IHotkeySystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Shared ingress guard for per-connection per-operation debounce and in-flight tracking.
		/// </summary>
		IngressGuard IngressGuard { get; }
	}
}