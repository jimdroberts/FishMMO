using FishNet.Connection;
using System.Collections.Generic;
using FishNet.Transporting;
using FishMMO.Server.Core;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Manages server selection for clients, providing the list of available world servers from the database.
	/// </summary>
	[CreateAssetMenu(fileName = "ServerSelectSystem", menuName = "FishMMO/Server/LoginServer/Server Select System", order = 1)]
	public class ServerSelectSystem : ServerBehaviour
	{
		/// <summary>
		/// Idle timeout in seconds for world servers to be considered active.
		/// </summary>
		public float IdleTimeout = 60;

		/// <summary>
		/// Initializes the server select system, registering broadcast handlers for server list requests.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			Server.NetworkWrapper.RegisterBroadcast<RequestServerListBroadcast>(OnServerRequestServerListBroadcastReceived, true);

			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the server select system, unregistering broadcast handlers for server list requests.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server != null)
			{
				Server.NetworkWrapper.UnregisterBroadcast<RequestServerListBroadcast>(OnServerRequestServerListBroadcastReceived);
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
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();

			if (conn.IsActive)
			{
				List<WorldServerDetails> worldServerList = WorldServerService.GetServerList(dbContext, IdleTimeout);

				ServerListBroadcast serverListMsg = new ServerListBroadcast()
				{
					Servers = worldServerList
				};

				Server.NetworkWrapper.Broadcast(conn, serverListMsg, true, Channel.Reliable);
			}
		}
	}
}