namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for the faction subsystem on a scene server.
	/// Provides a minimal surface so other core systems can obtain or interact
	/// with faction-related services without depending on the implementation.
	/// </summary>
	public interface IFactionSystem : IServerBehaviour
	{
	}
}