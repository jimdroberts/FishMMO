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
		/// <summary>
		/// Idle timeout in seconds for world servers to be considered active.
		/// </summary>
		public float IdleTimeout = 60;

		/// <summary>
		/// Initializes the server select system, registering broadcast handlers for server list requests.
		/// </summary>
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

		/// <summary>
		/// Cleans up the server select system, unregistering broadcast handlers for server list requests.
		/// </summary>
		public override void Destroying()
		{
			if (Server != null)
			{
				Server.UnregisterBroadcast<RequestServerListBroadcast>(OnServerRequestServerListBroadcastReceived);
			}
		}

		/// <summary>
		/// Handles broadcast to request the list of available world servers, queries the database and sends the list to the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">RequestServerListBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
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