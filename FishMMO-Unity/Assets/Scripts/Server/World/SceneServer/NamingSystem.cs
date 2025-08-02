using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server
{
	/// <summary>
	/// This is a simple naming service that provides clients with names of objects based on their ID.
	/// </summary>
	public class NamingSystem : ServerBehaviour
	{
		/// <summary>
		/// Initializes the naming system, registering broadcast handlers for naming and reverse naming requests.
		/// </summary>
		public override void InitializeOnce()
		{
			if (Server != null)
			{
				Server.RegisterBroadcast<NamingBroadcast>(OnServerNamingBroadcastReceived, true);
				Server.RegisterBroadcast<ReverseNamingBroadcast>(OnServerReverseNamingBroadcastReceived, true);
			}
			else
			{
				enabled = false;
			}
		}

		/// <summary>
		/// Cleans up the naming system, unregistering broadcast handlers.
		/// </summary>
		public override void Destroying()
		{
			if (Server != null)
			{
				Server.UnregisterBroadcast<NamingBroadcast>(OnServerNamingBroadcastReceived);
				Server.UnregisterBroadcast<ReverseNamingBroadcast>(OnServerReverseNamingBroadcastReceived);
			}
		}

		/// <summary>
		/// Handles incoming naming requests from clients, resolves names by ID for characters and guilds.
		/// Checks local cache first, then falls back to database lookup.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">NamingBroadcast message containing the type and ID to resolve.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerNamingBroadcastReceived(NetworkConnection conn, NamingBroadcast msg, Channel channel)
		{
			switch (msg.Type)
			{
				case NamingSystemType.CharacterName:
					//Log.Debug("NamingSystem: Searching by Character ID: " + msg.id);
					// check our local scene server first
					if (ServerBehaviour.TryGet(out CharacterSystem characterSystem) &&
						characterSystem.CharactersByID.TryGetValue(msg.ID, out IPlayerCharacter character))
					{
						//Log.Debug("NamingSystem: Character Local Result " + character.CharacterName);
						SendNamingBroadcast(conn, NamingSystemType.CharacterName, msg.ID, character.CharacterName);
					}
					// then check the database
					else if (Server.NpgsqlDbContextFactory != null)
					{
						using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
						string name = CharacterService.GetNameByID(dbContext, msg.ID);
						if (!string.IsNullOrWhiteSpace(name))
						{
							//Log.Debug("NamingSystem: Character Database Result " + name);
							SendNamingBroadcast(conn, NamingSystemType.CharacterName, msg.ID, name);
						}
					}
					break;
				case NamingSystemType.GuildName:
					//Log.Debug("NamingSystem: Searching by Guild ID: " + msg.id);
					// get the name from the database
					if (Server.NpgsqlDbContextFactory != null)
					{
						using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
						string name = GuildService.GetNameByID(dbContext, msg.ID);
						if (!string.IsNullOrWhiteSpace(name))
						{
							//Log.Debug("NamingSystem: Guild Database Result " + name);
							SendNamingBroadcast(conn, NamingSystemType.GuildName, msg.ID, name);
						}
					}
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// Sends a naming broadcast to the specified connection, providing the resolved name for the given ID and type.
		/// </summary>
		/// <param name="conn">Network connection to send the broadcast to.</param>
		/// <param name="type">Type of naming system (character, guild, etc.).</param>
		/// <param name="id">ID of the object to resolve.</param>
		/// <param name="name">Resolved name to send.</param>
		public void SendNamingBroadcast(NetworkConnection conn, NamingSystemType type, long id, string name)
		{
			if (conn == null)
				return;

			NamingBroadcast msg = new NamingBroadcast()
			{
				Type = type,
				ID = id,
				Name = name,
			};

			Server.Broadcast(conn, msg, true, Channel.Reliable);
		}

		/// <summary>
		/// Handles incoming reverse naming requests from clients, resolves IDs by name for characters.
		/// Checks local cache first, then falls back to database lookup. Notifies client if not found.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">ReverseNamingBroadcast message containing the type and name to resolve.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerReverseNamingBroadcastReceived(NetworkConnection conn, ReverseNamingBroadcast msg, Channel channel)
		{
			var nameLowerCase = msg.NameLowerCase.ToLower();
			switch (msg.Type)
			{
				case NamingSystemType.CharacterName:
					//Log.Debug("NamingSystem: Searching by Character ID: " + msg.id);
					// check our local scene server first
					if (ServerBehaviour.TryGet(out CharacterSystem characterSystem) &&
						characterSystem.CharactersByLowerCaseName.TryGetValue(nameLowerCase, out IPlayerCharacter character))
					{
						//Log.Debug("NamingSystem: Character Local Result " + character.CharacterName);
						SendReverseNamingBroadcast(conn, NamingSystemType.CharacterName, nameLowerCase, character.ID, character.CharacterName);
						break;
					}
					// then check the database
					if (Server.NpgsqlDbContextFactory != null)
					{
						using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
						CharacterEntity entity = CharacterService.GetByName(dbContext, msg.NameLowerCase);
						if (entity != null)
						{
							//Log.Debug("NamingSystem: Character Database Result " + name);
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
		/// Sends a reverse naming broadcast to the specified connection, providing the resolved ID and name for the given type and name.
		/// </summary>
		/// <param name="conn">Network connection to send the broadcast to.</param>
		/// <param name="type">Type of naming system (character, guild, etc.).</param>
		/// <param name="nameLowerCase">Lowercase name to resolve.</param>
		/// <param name="id">Resolved ID to send.</param>
		/// <param name="name">Resolved name to send.</param>
		public void SendReverseNamingBroadcast(NetworkConnection conn, NamingSystemType type, string nameLowerCase, long id, string name)
		{
			if (conn == null)
				return;

			ReverseNamingBroadcast msg = new ReverseNamingBroadcast()
			{
				Type = type,
				NameLowerCase = nameLowerCase,
				ID = id,
				Name = name
			};

			Server.Broadcast(conn, msg, true, Channel.Reliable);
		}
	}
}