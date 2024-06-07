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
			if (Server != null)
			{
				Server.RegisterBroadcast<CharacterRequestListBroadcast>(OnServerCharacterRequestListBroadcastReceived, true);
				Server.RegisterBroadcast<CharacterDeleteBroadcast>(OnServerCharacterDeleteBroadcastReceived, true);
				Server.RegisterBroadcast<CharacterSelectBroadcast>(OnServerCharacterSelectBroadcastReceived, true);
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
				Server.UnregisterBroadcast<CharacterRequestListBroadcast>(OnServerCharacterRequestListBroadcastReceived);
				Server.UnregisterBroadcast<CharacterDeleteBroadcast>(OnServerCharacterDeleteBroadcastReceived);
				Server.UnregisterBroadcast<CharacterSelectBroadcast>(OnServerCharacterSelectBroadcastReceived);
			}
		}

		private void OnServerCharacterRequestListBroadcastReceived(NetworkConnection conn, CharacterRequestListBroadcast msg, Channel channel)
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

				Server.Broadcast(conn, characterListMsg, true, Channel.Reliable);
			}
		}

		private void OnServerCharacterDeleteBroadcastReceived(NetworkConnection conn, CharacterDeleteBroadcast msg, Channel channel)
		{
			if (conn.IsActive && AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				CharacterService.Delete(dbContext, accountName, msg.characterName, KeepDeleteData);

				CharacterDeleteBroadcast charDeleteMsg = new CharacterDeleteBroadcast()
				{
					characterName = msg.characterName,
				};

				Server.Broadcast(conn, charDeleteMsg, true, Channel.Reliable);
			}
		}

		private void OnServerCharacterSelectBroadcastReceived(NetworkConnection conn, CharacterSelectBroadcast msg, Channel channel)
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
						// send the client the world server list
						List<WorldServerDetails> worldServerList = WorldServerService.GetServerList(dbContext);
						Server.Broadcast(conn, new ServerListBroadcast()
						{
							servers = worldServerList
						}, true, Channel.Reliable);
					}
				}
			}
		}
	}
}