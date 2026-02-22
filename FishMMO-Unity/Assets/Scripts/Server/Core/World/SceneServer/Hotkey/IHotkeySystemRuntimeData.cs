namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for hotkey ingress guards.
	/// </summary>
	public interface IHotkeySystemRuntimeData : IRuntimeDataContainer
	{
		IngressGuard IngressGuard { get; }
	}
}