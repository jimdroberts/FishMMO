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