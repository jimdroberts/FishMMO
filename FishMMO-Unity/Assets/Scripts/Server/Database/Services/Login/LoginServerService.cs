using System;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;

namespace FishMMO.Server.DatabaseServices
{
	public class LoginServerService
	{
		/// <summary>
		/// Adds a new server to the server list. The Login server will fetch this list for new clients.
		/// </summary>
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
				Log.Debug($"LoginServerService: Login Server[{loginServer.ID}] with name \"{name}\" already exists. Updating information.");

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

			Log.Debug($"LoginServerService: Added Login Server to Database: [{server.ID}] {name}:{address}:{port}");

			id = server.ID;
			return server;
		}

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