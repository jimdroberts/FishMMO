namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for hotkey handling on scene servers.
	/// Implementations should process hotkey set requests coming from clients and
	/// update the player's hotkey state accordingly. Connection and channel types
	/// are intentionally typed as <c>object</c> to avoid tying this contract to a
	/// specific networking library.
	/// </summary>
	public interface IHotkeySystem : IServerBehaviour
	{
	}
}