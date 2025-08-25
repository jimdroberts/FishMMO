namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for the social "friends" subsystem on a scene server.
	/// The implementation accepts friend add/remove requests, enforces limits, and
	/// notifies clients about friend list changes. Keeping this interface free of
	/// engine-specific types allows core code to interact with friend features
	/// without depending on the networking or game engine.
	/// </summary>
	public interface IFriendSystem : IServerBehaviour
	{
		/// <summary>
		/// Maximum number of friends allowed per character. Implementations should
		/// enforce this limit when processing friend add requests.
		/// </summary>
		int MaxFriends { get; }
	}
}