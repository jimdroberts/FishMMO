using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct RequestInitialSceneBroadcast : IBroadcast
	{
	}

	public struct SceneLoadBroadcast : IBroadcast
	{
		public string SceneName;
	}

	public struct SceneUnloadBroadcast : IBroadcast
	{
		public string SceneName;
	}

	public struct CharacterSceneChangeRequestBroadcast : IBroadcast
	{
		public string FromTeleporter;
		public string TeleporterName;
	}
}