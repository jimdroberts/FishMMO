using System;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.DatabaseServices
{
		/// <summary>
		/// Provides methods for managing login servers, including adding, updating, deleting, and retrieving server data from the database.
		/// </summary>
		public class LoginServerService
	{
		/// <summary>
		/// Adds a new login server to the server list or updates an existing one. The login server will fetch this list for new clients.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="name">The name of the login server.</param>
		/// <param name="address">The address of the login server.</param>
		/// <param name="port">The port of the login server.</param>
		/// <param name="id">The ID of the added or updated server.</param>
		/// <returns>The added or updated login server entity.</returns>
		public static LoginServerEntity Add(
			NpgsqlDbContext dbContext,
			string name,
			string address,
			ushort port,
			out long id
		)
		{
			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
			{
				throw new Exception("Name or Address is invalid");
			}

			var loginServer = dbContext.LoginServers.FirstOrDefault(c => c.Name == name);
			if (loginServer != null)
			{
				Log.Debug("LoginServerService", $"Login Server[{loginServer.ID}] with name \"{name}\" already exists. Updating information.");

				loginServer.LastPulse = DateTime.UtcNow;
				loginServer.Address = address;
				loginServer.Port = port;

				dbContext.SaveChanges();

				id = loginServer.ID;
				return loginServer;
			}

			var server = new LoginServerEntity()
			{
				Name = name,
				LastPulse = DateTime.UtcNow,
				Address = address,
				Port = port,
			};
			dbContext.LoginServers.Add(server);
			dbContext.SaveChanges();

			Log.Debug("LoginServerService", $"Added Login Server to Database: [{server.ID}] {name}:{address}:{port}");

			id = server.ID;
			return server;
		}

		/// <summary>
		/// Updates the last pulse for a login server.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="id">The ID of the login server to update.</param>
		public static void Pulse(NpgsqlDbContext dbContext, long id)
		{
			if (id == 0)
			{
				return;
			}
			var loginServer = dbContext.LoginServers.FirstOrDefault(c => c.ID == id);
			if (loginServer == null) throw new Exception($"Couldn't find Login Server with ID: {id}");

			loginServer.LastPulse = DateTime.UtcNow;
			dbContext.SaveChanges();
		}

		/// <summary>
		/// Deletes a login server from the database by its ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="id">The ID of the login server to delete.</param>
		public static void Delete(NpgsqlDbContext dbContext, long id)
		{
			if (id == 0)
			{
				return;
			}
			var loginServer = dbContext.LoginServers.FirstOrDefault(c => c.ID == id);
			if (loginServer != null)
			{
				dbContext.LoginServers.Remove(loginServer);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Retrieves a login server from the database by its ID.
		/// </summary>
		/// <param name="dbContext">The database context.</param>
		/// <param name="loginServerID">The ID of the login server to retrieve.</param>
		/// <returns>The login server entity if found; otherwise, null.</returns>
		public static LoginServerEntity GetServer(NpgsqlDbContext dbContext, long loginServerID)
		{
			if (loginServerID == 0)
			{
				return null;
			}
			var loginServer = dbContext.LoginServers.FirstOrDefault(c => c.ID == loginServerID);
			if (loginServer == null) throw new Exception($"Couldn't find Login Server with ID: {loginServerID}");

			return loginServer;
		}
	}
}