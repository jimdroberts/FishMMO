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

	/// <summary>
	/// Broadcast sent by the LoginServer to a queued client with their current
	/// position in the login queue.  Sent periodically at a server-configured rate.
	///
	/// <para><b>Position semantics:</b></para>
	/// <list type="bullet">
	///   <item><b>&gt; 0</b> — Waiting in queue.  Display the position to the user.</item>
	///   <item><b>0</b> — Admitted.  The client should re-initiate the handshake now.</item>
	///   <item><b>-1</b> — Cancelled.  The queue entry was purged (timeout or shutdown).</item>
	/// </list>
	///
	/// <para><b>Server-authoritative update rate:</b> The server controls how often
	/// this broadcast is sent via the <c>LoginQueueUpdateRateSeconds</c> config key.
	/// Clients are passive receivers only — there is no request path for faster updates.</para>
	/// </summary>
	public struct LoginQueuePositionBroadcast : IBroadcast
	{
		/// <summary>
		/// Current 1-based queue position.  0 = admitted, -1 = cancelled.
		/// </summary>
		public int QueuePosition;

		/// <summary>
		/// Rough estimated wait time in seconds based on the server's admission rate.
		/// 0 if unknown or if the client has been admitted.
		/// </summary>
		public int EstimatedWaitSeconds;

		/// <summary>
		/// Total number of clients currently in the queue.
		/// </summary>
		public int TotalQueued;
	}
}