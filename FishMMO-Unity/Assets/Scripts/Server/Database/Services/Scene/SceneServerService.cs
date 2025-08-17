using System;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing scene servers, including adding, updating, deleting, and retrieving server data from the database.
		/// </summary>
		public class SceneServerService
	{
		/// <summary>
		/// Adds a new scene server to the server list or updates an existing one. The login server will fetch this list for new clients.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="name">The name of the scene server.</param>
		/// <param name="address">The address of the scene server.</param>
		/// <param name="port">The port of the scene server.</param>
		/// <param name="characterCount">The number of characters on the server.</param>
		/// <param name="locked">Whether the server is locked.</param>
		/// <param name="id">The ID of the added or updated server.</param>
		/// <returns>The added or updated scene server entity.</returns>
		public static SceneServerEntity Add(
			NpgsqlDbContext dbContext,
			string name,
			string address,
			ushort port,
			int characterCount,
			bool locked,
			out long id
		)
		{
			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
			{
				throw new Exception("Name or Address is invalid");
			}

			var sceneServer = dbContext.SceneServers.FirstOrDefault(c => c.Name == name);
			if (sceneServer != null)
			{
				Log.Debug("ServerServerService", $"Scene Server[{sceneServer.ID}] with name \"{name}\" already exists. Updating information.");

				sceneServer.LastPulse = DateTime.UtcNow;
				sceneServer.Address = address;
				sceneServer.Port = port;
				sceneServer.CharacterCount = characterCount;
				sceneServer.Locked = locked;

				dbContext.SaveChanges();

				id = sceneServer.ID;
				return sceneServer;
			}

			var server = new SceneServerEntity()
			{
				Name = name,
				LastPulse = DateTime.UtcNow,
				Address = address,
				Port = port,
				CharacterCount = characterCount,
				Locked = locked
			};
			dbContext.SceneServers.Add(server);
			dbContext.SaveChanges();

			Log.Debug("ServerServerService", $"Added Scene Server to Database: [{server.ID}] {name}:{address}:{port}");

			id = server.ID;
			return server;
		}

		/// <summary>
		/// Updates the last pulse, character count, and lock state for a scene server.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="id">The ID of the scene server to update.</param>
		/// <param name="characterCount">The number of characters on the server.</param>
		/// <param name="locked">Whether the server is locked.</param>
		public static void Pulse(NpgsqlDbContext dbContext, long id, int characterCount, bool locked)
		{
			if (id == 0)
			{
				return;
			}

			var sceneServer = dbContext.SceneServers.FirstOrDefault(c => c.ID == id);
			if (sceneServer == null) throw new Exception($"Couldn't find Scene Server with ID: {id}");

			sceneServer.LastPulse = DateTime.UtcNow;
			sceneServer.CharacterCount = characterCount;
			sceneServer.Locked = locked;
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Deletes a scene server from the database by its ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="id">The ID of the scene server to delete.</param>
		public static void Delete(NpgsqlDbContext dbContext, long id)
		{
			if (id == 0)
			{
				return;
			}

			var sceneServer = dbContext.SceneServers.FirstOrDefault(c => c.ID == id);
			if (sceneServer != null)
			{
				dbContext.SceneServers.Remove(sceneServer);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Retrieves a scene server from the database by its ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="id">The ID of the scene server to retrieve.</param>
		/// <returns>The scene server entity if found; otherwise, null.</returns>
		public static SceneServerEntity GetServer(NpgsqlDbContext dbContext, long id)
		{
			if (id == 0)
			{
				return null;
			}

			var sceneServer = dbContext.SceneServers.FirstOrDefault(c => c.ID == id);
			if (sceneServer == null) throw new Exception($"Couldn't find Scene Server with ID: {id}");

			return sceneServer;
		}
	}
}