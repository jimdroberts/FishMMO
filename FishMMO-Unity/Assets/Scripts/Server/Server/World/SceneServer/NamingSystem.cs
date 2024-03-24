using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Npgsql.Entities;
//using UnityEngine;

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
				ServerManager.RegisterBroadcast<ReverseNamingBroadcast>(OnServerReverseNamingBroadcastReceived, true);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<NamingBroadcast>(OnServerNamingBroadcastReceived);
				ServerManager.UnregisterBroadcast<ReverseNamingBroadcast>(OnServerReverseNamingBroadcastReceived);
			}
		}

		/// <summary>
		/// Naming request broadcast received from a character.
		/// </summary>
		private void OnServerNamingBroadcastReceived(NetworkConnection conn, NamingBroadcast msg, Channel channel)
		{
			switch (msg.type)
			{
				case NamingSystemType.CharacterName:
					//Debug.Log("NamingSystem: Searching by Character ID: " + msg.id);
					// check our local scene server first
					if (ServerBehaviour.TryGet(out CharacterSystem characterSystem) &&
						characterSystem.CharactersByID.TryGetValue(msg.id, out Character character))
					{
						//Debug.Log("NamingSystem: Character Local Result " + character.CharacterName);
						SendNamingBroadcast(conn, NamingSystemType.CharacterName, msg.id, character.CharacterName);
					}
					// then check the database
					else if (Server.NpgsqlDbContextFactory != null)
					{
						using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
						string name = CharacterService.GetNameByID(dbContext, msg.id);
						if (!string.IsNullOrWhiteSpace(name))
						{
							//Debug.Log("NamingSystem: Character Database Result " + name);
							SendNamingBroadcast(conn, NamingSystemType.CharacterName, msg.id, name);
						}
					}
					break;
				case NamingSystemType.GuildName:
					//Debug.Log("NamingSystem: Searching by Guild ID: " + msg.id);
					// get the name from the database
					if (Server.NpgsqlDbContextFactory != null)
					{
						using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
						string name = GuildService.GetNameByID(dbContext, msg.id);
						if (!string.IsNullOrWhiteSpace(name))
						{
							//Debug.Log("NamingSystem: Guild Database Result " + name);
							SendNamingBroadcast(conn, NamingSystemType.GuildName, msg.id, name);
						}
					}
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// Allows the server to send naming requests to the connection
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

			Server.Broadcast(conn, msg, true, Channel.Reliable);
		}

		/// <summary>
		/// Reverse naming request broadcast received from a character.
		/// </summary>
		private void OnServerReverseNamingBroadcastReceived(NetworkConnection conn, ReverseNamingBroadcast msg, Channel channel)
		{
			var nameLowerCase = msg.nameLowerCase.ToLower();
			switch (msg.type)
			{
				case NamingSystemType.CharacterName:
					//Debug.Log("NamingSystem: Searching by Character ID: " + msg.id);
					// check our local scene server first
					if (ServerBehaviour.TryGet(out CharacterSystem characterSystem) &&
						characterSystem.CharactersByLowerCaseName.TryGetValue(nameLowerCase, out Character character))
					{
						//Debug.Log("NamingSystem: Character Local Result " + character.CharacterName);
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, character.ID, character.CharacterName);
						break;
					}
					// then check the database
					if (Server.NpgsqlDbContextFactory != null)
					{
						using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
						CharacterEntity entity = CharacterService.GetByName(dbContext, msg.nameLowerCase);
						if (entity != null)
						{
							//Debug.Log("NamingSystem: Character Database Result " + name);
							SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, entity.ID, entity.Name);
							break;
						}
					}
					// let the client know it wasn't found
					SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, 0, "");
					break;
				case NamingSystemType.GuildName:
					// Currently not supported, implement this if/when needed
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// Allows the server to send reverse naming requests to the connection
		/// </summary>
		public void SendReverseNamingBroadcast(NetworkConnection conn, NamingSystemType type, string nameLowerCase, long id, string name)
		{
			if (conn == null)
				return;

			ReverseNamingBroadcast msg = new ReverseNamingBroadcast()
			{
				type = type,
				nameLowerCase = nameLowerCase,
				id = id,
				name = name
			};

			Server.Broadcast(conn, msg, true, Channel.Reliable);
		}
	}
}