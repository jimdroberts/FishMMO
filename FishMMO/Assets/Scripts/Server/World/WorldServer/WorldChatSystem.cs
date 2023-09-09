using FishNet.Connection;
using FishNet.Transporting;
using FishNet.Managing.Server;

namespace FishMMO.Server
{
	/// <summary>
	/// World server chat system.
	/// </summary>
	public class WorldChatSystem : ServerBehaviour
	{
		public override void InitializeOnce()
		{
			if (ServerManager != null &&
				Server.WorldSceneSystem != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Started)
			{
				ServerManager.RegisterBroadcast<WorldChatBroadcast>(OnServerWorldChatBroadcastReceived, true);
				ServerManager.RegisterBroadcast<WorldChatTellBroadcast>(OnServerWorldChatTellBroadcastReceived, true);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
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
			Server.WorldSceneSystem.BroadcastToAllScenes(msg);
		}


		/// <summary>
		/// World chat acts as a relay and broadcasts incoming chat messages to all scene servers.
		/// </summary>
		private void OnServerWorldChatTellBroadcastReceived(NetworkConnection conn, WorldChatTellBroadcast msg)
		{
			Server.WorldSceneSystem.BroadcastToCharacter(msg.targetId, msg);
		}
	}
}