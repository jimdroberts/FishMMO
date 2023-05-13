using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Server
{
	public struct ScenePulseBroadcast : IBroadcast
	{
		public string name;
		public List<SceneInstanceDetails> sceneInstanceDetails;
	}

	public struct SceneServerDetailsBroadcast : IBroadcast
	{
		public string address;
		public ushort port;
		public List<SceneInstanceDetails> sceneInstanceDetails;
	}

	public struct SceneListBroadcast : IBroadcast
	{
		public List<SceneInstanceDetails> sceneInstanceDetails;
	}

	public struct SceneLoadBroadcast : IBroadcast
	{
		public string sceneName;
		public int handle;
	}

	public struct SceneUnloadBroadcast : IBroadcast
	{
		public string sceneName;
		public int handle;
	}

	public struct SceneCharacterConnectedBroadcast : IBroadcast
	{
		public long characterId;
		public string sceneName;
	}

	public struct SceneCharacterDisconnectedBroadcast : IBroadcast
	{
		public long characterId;
	}

	public struct WorldChatBroadcast : IBroadcast
	{
		public ChatBroadcast chatMsg;
	}

	public struct WorldChatTellBroadcast : IBroadcast
	{
		public long targetId;
		public ChatBroadcast chatMsg;
	}
}