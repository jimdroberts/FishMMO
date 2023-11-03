using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;

namespace FishMMO.Server
{
	/// <summary>
	/// Server Character Select system.
	/// </summary>
	public class CharacterSelectSystem : ServerBehaviour
	{
		public bool KeepDeleteData = true;

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
				ServerManager.RegisterBroadcast<CharacterRequestListBroadcast>(OnServerCharacterRequestListBroadcastReceived, true);
				ServerManager.RegisterBroadcast<CharacterDeleteBroadcast>(OnServerCharacterDeleteBroadcastReceived, true);
				ServerManager.RegisterBroadcast<CharacterSelectBroadcast>(OnServerCharacterSelectBroadcastReceived, true);
			}
			else if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<CharacterRequestListBroadcast>(OnServerCharacterRequestListBroadcastReceived);
				ServerManager.UnregisterBroadcast<CharacterDeleteBroadcast>(OnServerCharacterDeleteBroadcastReceived);
				ServerManager.UnregisterBroadcast<CharacterSelectBroadcast>(OnServerCharacterSelectBroadcastReceived);
			}
		}

		private void OnServerCharacterRequestListBroadcastReceived(NetworkConnection conn, CharacterRequestListBroadcast msg)
		{
			if (!AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				// character is requesting character list before authentication completes, disconnect them...
				conn.Disconnect(true);
			}
			else if (conn.IsActive)
			{
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
                // load all character details for the account from database
                List<CharacterDetails> characterList = CharacterService.GetDetails(dbContext, accountName);

				// append the characters to the broadcast message
				CharacterListBroadcast characterListMsg = new CharacterListBroadcast()
				{
					characters = characterList
				};

				conn.Broadcast(characterListMsg, true, Channel.Reliable);
			}
		}

		private void OnServerCharacterDeleteBroadcastReceived(NetworkConnection conn, CharacterDeleteBroadcast msg)
		{
			if (conn.IsActive && AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				CharacterService.Delete(dbContext, accountName, msg.characterName, KeepDeleteData);
				dbContext.SaveChanges();

				CharacterDeleteBroadcast charDeleteMsg = new CharacterDeleteBroadcast()
				{
					characterName = msg.characterName,
				};

				conn.Broadcast(charDeleteMsg, true, Channel.Reliable);
			}
		}

		private void OnServerCharacterSelectBroadcastReceived(NetworkConnection conn, CharacterSelectBroadcast msg)
		{
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (conn.IsActive && AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				if (!CharacterService.Exists(dbContext, accountName, msg.characterName))
				{
					// character doesn't exist for account
					conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				}
				else
				{
					if (CharacterService.TrySetSelected(dbContext, accountName, msg.characterName))
					{
						dbContext.SaveChanges();

						// send the client the world server list
						List<WorldServerDetails> worldServerList = WorldServerService.GetServerList(dbContext);
						conn.Broadcast(new ServerListBroadcast()
						{
							servers = worldServerList
						}, true, Channel.Reliable);
					}
				}
			}
		}
	}
}