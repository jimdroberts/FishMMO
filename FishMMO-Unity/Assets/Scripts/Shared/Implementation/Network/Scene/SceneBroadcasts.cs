using FishNet.Broadcast;
using FishNet.Managing.Scened;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast indicating that the client has validated the current scene.
	/// No additional data required.
	/// </summary>
	public struct ClientValidatedSceneBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast indicating that the client has unloaded one or more scenes.
	/// Contains a list of unloaded scenes.
	/// </summary>
	public struct ClientScenesUnloadedBroadcast : IBroadcast
	{
		/// <summary>List of scenes that have been unloaded by the client.</summary>
		public UnloadedScene[] UnloadedScenes;
	}

	/// <summary>
	/// Broadcast requesting the initial scene to be loaded for the client.
	/// No additional data required.
	/// </summary>
	public struct RequestInitialSceneBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for loading a specific scene.
	/// Contains the name of the scene to load.
	/// </summary>
	public struct SceneLoadBroadcast : IBroadcast
	{
		/// <summary>Name of the scene to load.</summary>
		public string SceneName;
	}

	/// <summary>
	/// Broadcast for unloading a specific scene.
	/// Contains the name of the scene to unload.
	/// </summary>
	public struct SceneUnloadBroadcast : IBroadcast
	{
		/// <summary>Name of the scene to unload.</summary>
		public string SceneName;
	}

	/// <summary>
	/// Broadcast for requesting a character scene change via a teleporter.
	/// Contains the source teleporter and target teleporter names.
	/// </summary>
	public struct CharacterSceneChangeRequestBroadcast : IBroadcast
	{
		/// <summary>Name of the teleporter the character is coming from.</summary>
		public string FromTeleporter;
		/// <summary>Name of the teleporter the character is going to.</summary>
		public string TeleporterName;
	}

	/// <summary>
	/// Broadcast for sending a list of available scene channels to the client.
	/// Contains a list of channel addresses.
	/// </summary>
	public struct SceneChannelListBroadcast : IBroadcast
	{
		/// <summary>List of available channel addresses for scene selection.</summary>
		public ChannelAddress[] Addresses;
	}

	/// <summary>
	/// Broadcast for selecting a specific scene channel.
	/// Contains the selected channel address.
	/// </summary>
	/// <remarks>
	/// <b>Bandwidth note:</b> This broadcast sends the full <see cref="ChannelAddress"/> struct
	/// (port, scene name, character count, etc.) when only <c>SceneHandle</c> (int) is needed
	/// to identify the target. If bandwidth becomes a concern, consider replacing with a
	/// leaner struct containing only <c>int SceneHandle</c>.
	/// </remarks>
	public struct SceneChannelSelectBroadcast : IBroadcast
	{
		/// <summary>Selected channel address for the scene.</summary>
		public ChannelAddress Channel;
	}

	/// <summary>
	/// Broadcast requesting the list of available scene channels from the server.
	/// Sent by the client to request an updated channel list for the current scene.
	/// </summary>
	public struct RequestSceneChannelListBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast sent by the server when it cannot process a gameplay request because the
	/// async work queue is full. The client should display a transient "Server Busy" notification.
	/// </summary>
	/// <remarks>
	/// <b>Enhancement note:</b> This broadcast currently carries no metadata (empty payload).
	/// Consider adding <c>RetryAfterSeconds</c> (int) and <c>QueuePosition</c> (int) fields
	/// so the client can show a meaningful countdown and queue status to the user.
	/// </remarks>
	public struct ServerBusyBroadcast : IBroadcast
	{
	}
}