using FishNet.Connection;
using System.Collections.Generic;
using FishNet.Transporting;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;

namespace FishMMO.Server
{
	/// <summary>
	/// Server Select system.
	/// </summary>
	public class ServerSelectSystem : ServerBehaviour
	{
		public float IdleTimeout = 60;

		public override void InitializeOnce()
		{
			if (Server != null)
			{
				Server.RegisterBroadcast<RequestServerListBroadcast>(OnServerRequestServerListBroadcastReceived, true);
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			if (Server != null)
			{
				Server.UnregisterBroadcast<RequestServerListBroadcast>(OnServerRequestServerListBroadcastReceived);
			}
		}

		private void OnServerRequestServerListBroadcastReceived(NetworkConnection conn, RequestServerListBroadcast msg, Channel channel)
		{
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			
			if (conn.IsActive)
			{
				List<WorldServerDetails> worldServerList = WorldServerService.GetServerList(dbContext, IdleTimeout);

				ServerListBroadcast serverListMsg = new ServerListBroadcast()
				{
					Servers = worldServerList
				};

				Server.Broadcast(conn, serverListMsg, true, Channel.Reliable);
			}
		}
	}
}