using FishNet.Connection;
using FishNet.Transporting;
using FishNet.Managing.Server;

namespace Server
{
	/// <summary>
	/// World server chat system.
	/// </summary>
	public class WorldChatSystem : ServerBehaviour
	{
		public WorldSceneSystem WorldSceneSystem;

		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Started)
			{
				ServerManager.RegisterBroadcast<WorldChatBroadcast>(OnServerWorldChatBroadcastReceived, true);
				ServerManager.RegisterBroadcast<WorldChatTellBroadcast>(OnServerWorldChatTellBroadcastReceived, true);
			}
			else if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<WorldChatBroadcast>(OnServerWorldChatBroadcastReceived);
				ServerManager.UnregisterBroadcast<WorldChatTellBroadcast>(OnServerWorldChatTellBroadcastReceived);
			}
		}

		/// <summary>
		/// World chat acts as a relay and broadcasts incoming chat messages to all scene servers.
		/// </summary>
		private void OnServerWorldChatBroadcastReceived(NetworkConnection conn, WorldChatBroadcast msg)
		{
			if (WorldSceneSystem != null)
			{
				WorldSceneSystem.BroadcastToAllScenes(msg);
			}
		}


		/// <summary>
		/// World chat acts as a relay and broadcasts incoming chat messages to all scene servers.
		/// </summary>
		private void OnServerWorldChatTellBroadcastReceived(NetworkConnection conn, WorldChatTellBroadcast msg)
		{
			if (WorldSceneSystem != null)
			{
				WorldSceneSystem.BroadcastToCharacter(msg.targetName, msg);
			}
		}
	}
}