using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for chat handling on a scene server.
	/// Provides channel-specific handlers that validate, parse, rate-limit, and
	/// broadcast or route incoming chat messages. Implementations should keep
	/// message validation and spam protections here; the boolean return value for
	/// each handler indicates whether the message should be persisted/forwarded
	/// to upstream systems (for example written to the chat database) or suppressed.
	/// </summary>
	public interface IChatSystem : IServerBehaviour, IChatHelper
	{
	}
}