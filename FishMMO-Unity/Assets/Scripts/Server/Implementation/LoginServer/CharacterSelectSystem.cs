using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Server Character Select system.
	/// </summary>
	public class CharacterSelectSystem : ServerBehaviour
	{
		/// <summary>
		/// If true, keeps deleted character data in the database for recovery or auditing.
		/// </summary>
		public bool KeepDeleteData = true;

		/// <summary>
		/// Initializes the character select system, registering broadcast handlers for character list, delete, and select requests.
		/// </summary>
		public override void InitializeOnce()
		{
			if (Server != null)
			{
				Server.NetworkWrapper.RegisterBroadcast<CharacterRequestListBroadcast>(OnServerCharacterRequestListBroadcastReceived, true);
				Server.NetworkWrapper.RegisterBroadcast<CharacterDeleteBroadcast>(OnServerCharacterDeleteBroadcastReceived, true);
				Server.NetworkWrapper.RegisterBroadcast<CharacterSelectBroadcast>(OnServerCharacterSelectBroadcastReceived, true);
			}
			else
			{
				enabled = false;
			}
		}

		/// <summary>
		/// Cleans up the character select system, unregistering broadcast handlers for character list, delete, and select requests.
		/// </summary>
		public override void Destroying()
		{
			if (Server != null)
			{
				Server.NetworkWrapper.UnregisterBroadcast<CharacterRequestListBroadcast>(OnServerCharacterRequestListBroadcastReceived);
				Server.NetworkWrapper.UnregisterBroadcast<CharacterDeleteBroadcast>(OnServerCharacterDeleteBroadcastReceived);
				Server.NetworkWrapper.UnregisterBroadcast<CharacterSelectBroadcast>(OnServerCharacterSelectBroadcastReceived);
			}
		}

		/// <summary>
		/// Handles broadcast to request the list of available characters for the account, queries the database and sends the list to the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">CharacterRequestListBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerCharacterRequestListBroadcastReceived(NetworkConnection conn, CharacterRequestListBroadcast msg, Channel channel)
		{
			if (!Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				// character is requesting character list before authentication completes, disconnect them...
				conn.Disconnect(true);
			}
			else if (conn.IsActive)
			{
				using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
				// load all character details for the account from database
				List<CharacterDetails> characterList = CharacterService.GetDetails(dbContext, accountName);

				// append the characters to the broadcast message
				CharacterListBroadcast characterListMsg = new CharacterListBroadcast()
				{
					Characters = characterList
				};

				Server.NetworkWrapper.Broadcast(conn, characterListMsg, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Handles broadcast to delete a character for the account, updates the database and notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">CharacterDeleteBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerCharacterDeleteBroadcastReceived(NetworkConnection conn, CharacterDeleteBroadcast msg, Channel channel)
		{
			if (conn.IsActive && Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
				if (CharacterService.TryDelete(dbContext, accountName, msg.CharacterName, KeepDeleteData))
				{
					CharacterDeleteBroadcast charDeleteMsg = new CharacterDeleteBroadcast()
					{
						CharacterName = msg.CharacterName,
					};

					Server.NetworkWrapper.Broadcast(conn, charDeleteMsg, true, Channel.Reliable);
				}
			}
		}

		/// <summary>
		/// Handles broadcast to select a character for the account, updates the database and sends the world server list to the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">CharacterSelectBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerCharacterSelectBroadcastReceived(NetworkConnection conn, CharacterSelectBroadcast msg, Channel channel)
		{
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			if (conn.IsActive && Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				if (!CharacterService.Exists(dbContext, accountName, msg.CharacterName))
				{
					// character doesn't exist for account
					conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				}
				else
				{
					if (CharacterService.TrySetSelected(dbContext, accountName, msg.CharacterName))
					{
						// send the client the world server list
						List<WorldServerDetails> worldServerList = WorldServerService.GetServerList(dbContext);
						Server.NetworkWrapper.Broadcast(conn, new ServerListBroadcast()
						{
							Servers = worldServerList
						}, true, Channel.Reliable);
					}
				}
			}
		}
	}
}