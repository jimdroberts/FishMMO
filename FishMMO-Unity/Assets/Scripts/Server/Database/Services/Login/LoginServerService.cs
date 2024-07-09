using System;
using System.Diagnostics;
using System.Linq;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.DatabaseServices
{
	public class LoginServerService
	{
		/// <summary>
		/// Adds a new server to the server list. The Login server will fetch this list for new clients.
		/// </summary>
		public static LoginServerEntity Add(
			NpgsqlDbContext dbContext,
			string address,
			ushort port,
			out long id
		)
		{
			if (string.IsNullOrWhiteSpace(address))
				throw new Exception("Address is invalid");

			var loginServer = dbContext.LoginServers.FirstOrDefault(c => c.Address == address && c.Port == port);
			if (loginServer != null) throw new Exception($"Login Server at {address}:{port} has already been added to the database."); ;

			var server = new LoginServerEntity()
			{
				LastPulse = DateTime.UtcNow,
				Address = address,
				Port = port,
			};
			dbContext.LoginServers.Add(server);
			dbContext.SaveChanges();

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