using FishNet.Broadcast;

public struct RequestInitialSceneBroadcast : IBroadcast
{
}

public struct SceneLoadBroadcast : IBroadcast
{
	public string sceneName;
}

public struct SceneUnloadBroadcast : IBroadcast
{
	public string sceneName;
}

public struct CharacterSceneChangeRequestBroadcast : IBroadcast
{
	public string fromTeleporter;
	public string teleporterName;
}

/// <summary>
/// This broadcast tells the client to reconnect to the specific world address and port. SceneName and TeleporterName are for loading screens.
/// </summary>
public struct SceneWorldReconnectBroadcast : IBroadcast
{
	public string address;
	public ushort port;
	public string sceneName;
	public string teleporterName;
}