using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Server.Services;

namespace FishMMO.Server
{
	/// <summary>
	/// Server Character Select system.
	/// </summary>
	public class CharacterSelectSystem : ServerBehaviour
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
				using var dbContext = Server.DbContextFactory.CreateDbContext();
                // load all character details for the account from database
                List<global::CharacterDetails> characterList = CharacterService.GetCharacterList(dbContext, accountName);

				// append the characters to the broadcast message
				CharacterListBroadcast characterListMsg = new CharacterListBroadcast()
				{
					characters = characterList
				};

				conn.Broadcast(characterListMsg);
			}
		}

		private void OnServerCharacterDeleteBroadcastReceived(NetworkConnection conn, CharacterDeleteBroadcast msg)
		{
			if (conn.IsActive && AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				using var dbContext = Server.DbContextFactory.CreateDbContext();
				CharacterService.Delete(dbContext, accountName, msg.characterName);
				dbContext.SaveChanges();

				CharacterDeleteBroadcast charDeleteMsg = new CharacterDeleteBroadcast()
				{
					characterName = msg.characterName,
				};

				conn.Broadcast(charDeleteMsg);
			}
		}

		private void OnServerCharacterSelectBroadcastReceived(NetworkConnection conn, CharacterSelectBroadcast msg)
		{
			using var dbContext = Server.DbContextFactory.CreateDbContext();
			if (conn.IsActive && AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				var selectedCharacter = CharacterService.TrySetCharacterSelected(dbContext, accountName, msg.characterName);
				dbContext.SaveChanges();
				
				if (selectedCharacter)
				{
					List<WorldServerDetails> worldServerList = WorldServerService.GetWorldServerList(dbContext);
					conn.Broadcast(new ServerListBroadcast()
					{
						servers = worldServerList
					});
				}
				else
				{
					//disconnect? failed to select character
					conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				}
			}
		}
	}
}