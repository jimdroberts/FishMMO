using FishNet.Connection;
using System.Collections.Generic;
using FishNet.Transporting;
using FishMMO.Server.Services;
using FishMMO.Shared;

namespace FishMMO.Server
{
	/// <summary>
	/// Server Select system.
	/// </summary>
	public class ServerSelectSystem : ServerBehaviour
	{
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
				ServerManager.RegisterBroadcast<RequestServerListBroadcast>(OnServerRequestServerListBroadcastReceived, true);
			}
			else if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<RequestServerListBroadcast>(OnServerRequestServerListBroadcastReceived);
			}
		}

		private void OnServerRequestServerListBroadcastReceived(NetworkConnection conn, RequestServerListBroadcast msg)
		{
			using var dbContext = Server.DbContextFactory.CreateDbContext();
			
			if (conn.IsActive)
			{
				List<WorldServerDetails> worldServerList = WorldServerService.GetServerList(dbContext);

				ServerListBroadcast serverListMsg = new ServerListBroadcast()
				{
					servers = worldServerList
				};

				conn.Broadcast(serverListMsg);
			}
		}
	}
}