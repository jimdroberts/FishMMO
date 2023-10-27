using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;

public enum NamingSystemType : byte
{
	CharacterName,
	GuildName,
}

namespace FishMMO.Server
{
	/// <summary>
	/// This is a simple naming service that provides clients with names of objects based on their ID.
	/// </summary>
	public class NamingSystem : ServerBehaviour
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

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Started)
			{
				ServerManager.RegisterBroadcast<NamingBroadcast>(OnServerNamingBroadcastReceived, true);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<NamingBroadcast>(OnServerNamingBroadcastReceived);
			}
		}

		/// <summary>
		/// Chat message received from a character.
		/// </summary>
		private void OnServerNamingBroadcastReceived(NetworkConnection conn, NamingBroadcast msg)
		{
			switch (msg.type)
			{
				case NamingSystemType.CharacterName:
					// check our local scene server first
					if (Server.CharacterSystem != null &&
						Server.CharacterSystem.CharactersByID.TryGetValue(msg.id, out Character character))
					{
						SendNamingBroadcast(conn, NamingSystemType.CharacterName, msg.id, character.CharacterName);
					}
					// then check the database
					else if (Server.DbContextFactory != null)
					{
						using var dbContext = Server.DbContextFactory.CreateDbContext();
						string name = CharacterService.GetNameByID(dbContext, msg.id);
						if (!string.IsNullOrWhiteSpace(name))
						{
							SendNamingBroadcast(conn, NamingSystemType.CharacterName, msg.id, name);
						}
					}
					break;
				case NamingSystemType.GuildName:
					// get the name from the database
					if (Server.DbContextFactory != null)
					{
						using var dbContext = Server.DbContextFactory.CreateDbContext();
						string name = GuildService.GetNameByID(dbContext, msg.id);
						if (!string.IsNullOrWhiteSpace(name))
						{
							SendNamingBroadcast(conn, NamingSystemType.GuildName, msg.id, name);
						}
					}
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// Allows the server to send system messages to the connection
		/// </summary>
		public void SendNamingBroadcast(NetworkConnection conn, NamingSystemType type, long id, string name)
		{
			if (conn == null)
				return;

			NamingBroadcast msg = new NamingBroadcast()
			{
				type = type,
				id = id,
				name = name,
			};

			conn.Broadcast(msg);
		}
	}
}