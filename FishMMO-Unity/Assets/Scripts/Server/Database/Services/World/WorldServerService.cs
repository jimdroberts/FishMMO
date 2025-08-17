using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing world servers, including adding, updating, deleting, and retrieving server data from the database.
		/// </summary>
		public class WorldServerService
	{
		/// <summary>
		/// Adds a new world server to the server list or updates an existing one. The login server will fetch this list for new clients.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="name">The name of the world server.</param>
		/// <param name="address">The address of the world server.</param>
		/// <param name="port">The port of the world server.</param>
		/// <param name="characterCount">The number of characters on the server.</param>
		/// <param name="locked">Whether the server is locked.</param>
		/// <param name="id">The ID of the added or updated server.</param>
		/// <returns>The added or updated world server entity.</returns>
		public static WorldServerEntity Add(
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
				throw new Exception("Name or address is invalid");
			}

			// See if a World Server exists with this name already.
			var worldServer = dbContext.WorldServers.FirstOrDefault(c => c.Name.Equals(name));
			if (worldServer != null)
			{
				Log.Debug("WorldServerService", $"World Server[{worldServer.ID}] with name \"{name}\" already exists. Updating information.");

				worldServer.LastPulse = DateTime.UtcNow;
				worldServer.Address = address;
				worldServer.Port = port;
				worldServer.CharacterCount = characterCount;
				worldServer.Locked = locked;

				dbContext.SaveChanges();

				id = worldServer.ID;
				return worldServer;
			}

			var server = new WorldServerEntity()
			{
				Name = name,
				LastPulse = DateTime.UtcNow,
				Address = address,
				Port = port,
				CharacterCount = characterCount,
				Locked = locked
			};
			dbContext.WorldServers.Add(server);
			dbContext.SaveChanges();

			Log.Debug("WorldServerService", $"Added World Server to Database: [{server.ID}] {name}:{address}:{port}");

			id = server.ID;
			return server;
		}

		/// <summary>
		/// Updates the last pulse and character count for a world server.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="id">The ID of the world server to update.</param>
		/// <param name="characterCount">The number of characters on the server.</param>
		public static void Pulse(NpgsqlDbContext dbContext, long id, int characterCount)
		{
			if (id == 0)
			{
				return;
			}
			var worldServer = dbContext.WorldServers.FirstOrDefault(c => c.ID == id);
			if (worldServer == null) throw new Exception($"Couldn't find World Server with ID: {id}");

			worldServer.LastPulse = DateTime.UtcNow;
			worldServer.CharacterCount = characterCount;
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Deletes a world server from the database by its ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="id">The ID of the world server to delete.</param>
		public static void Delete(NpgsqlDbContext dbContext, long id)
		{
			if (id == 0)
			{
				return;
			}
			var worldServer = dbContext.WorldServers.FirstOrDefault(c => c.ID == id);
			if (worldServer != null)
			{
				dbContext.WorldServers.Remove(worldServer);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Retrieves a world server from the database by its ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="worldServerID">The ID of the world server to retrieve.</param>
		/// <returns>The world server entity if found; otherwise, null.</returns>
		public static WorldServerEntity GetServer(NpgsqlDbContext dbContext, long worldServerID)
		{
			if (worldServerID == 0)
			{
				return null;
			}
			var worldServer = dbContext.WorldServers.FirstOrDefault(c => c.ID == worldServerID);
			if (worldServer == null) throw new Exception($"Couldn't find World Server with ID: {worldServerID}");

			return worldServer;
		}

		/// <summary>
		/// Retrieves a list of world server details for servers that have not timed out.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="idleTimeout">The idle timeout in seconds.</param>
		/// <returns>A list of world server details for active servers.</returns>
		public static List<WorldServerDetails> GetServerList(NpgsqlDbContext dbContext, float idleTimeout = 60.0f /*60 second timeout*/)
		{
			return dbContext.WorldServers.Where((s) => s.LastPulse.AddSeconds(idleTimeout) >= DateTime.UtcNow)
			    .Select(server => new WorldServerDetails()
			    {
				    Name = server.Name,
				    LastPulse = server.LastPulse,
				    Address = server.Address,
				    Port = server.Port,
				    CharacterCount = server.CharacterCount,
				    Locked = server.Locked,
			    })
			    .ToList();
		}
	}
}