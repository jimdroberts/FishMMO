using System;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.DatabaseServices
{
	public class SceneServerService
	{
		/// <summary>
		/// Adds a new server to the server list. The Login server will fetch this list for new clients.
		/// </summary>
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
				UnityEngine.Debug.Log($"ServerServerService: Scene Server[{sceneServer.ID}] with name \"{name}\" already exists. Updating information.");

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

			UnityEngine.Debug.Log($"ServerServerService: Added Scene Server to Database: [{server.ID}] {name}:{address}:{port}");

			id = server.ID;
			return server;
		}

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