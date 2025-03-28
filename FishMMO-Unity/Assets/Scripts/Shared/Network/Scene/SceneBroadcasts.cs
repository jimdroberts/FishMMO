using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Managing.Scened;

namespace FishMMO.Shared
{
	public struct ClientValidatedSceneBroadcast : IBroadcast
	{
	}

	public struct ClientScenesUnloadedBroadcast : IBroadcast
	{
		public List<UnloadedScene> UnloadedScenes;
	}

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

	public struct SceneChannelListBroadcast : IBroadcast
	{
		public List<ChannelAddress> Addresses;
	}

	public struct SceneChannelSelectBroadcast : IBroadcast
	{
		public ChannelAddress Channel;
	}
}