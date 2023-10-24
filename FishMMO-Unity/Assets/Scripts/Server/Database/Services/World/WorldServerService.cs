using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database;
using FishMMO.Database.Entities;

namespace FishMMO.Server.Services
{
    public class WorldServerService
    {
        /// <summary>
        /// Adds a new server to the server list. The Login server will fetch this list for new clients.
        /// </summary>
        public static WorldServerEntity Add(
            ServerDbContext dbContext,
            string name,
            string address,
            ushort port,
            int characterCount,
            bool locked,
            out long id
        )
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
                throw new Exception("Name or address is invalid");
            
            if (dbContext.WorldServers
                .Any(server => EF.Functions.Like(server.Name, name.ToLower()))) 
                throw new Exception($"World with name \"{name}\" already exists");

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

            id = server.ID;
			return server;
        }

        public static void Pulse(ServerDbContext dbContext, long id, int characterCount)
        {
            var worldServer = dbContext.WorldServers.FirstOrDefault(c => c.ID == id);
			if (worldServer == null) throw new Exception($"Couldn't find World Server with ID: {id}");
            
            worldServer.LastPulse = DateTime.UtcNow;
            worldServer.CharacterCount = characterCount;
        }
        
        public static void Delete(ServerDbContext dbContext, long id) 
        {
            var worldServer = dbContext.WorldServers.FirstOrDefault(c => c.ID == id);
			if (worldServer == null) throw new Exception($"Couldn't find World Server with ID: {id}");

            dbContext.WorldServers.Remove(worldServer);
        }

		public static WorldServerEntity GetServer(ServerDbContext dbContext, long worldServerID)
		{
			var worldServer = dbContext.WorldServers.FirstOrDefault(c => c.ID == worldServerID);
			if (worldServer == null) throw new Exception($"Couldn't find World Server with ID: {worldServerID}");

            return worldServer;
		}

		public static List<WorldServerDetails> GetServerList(ServerDbContext dbContext)
        {
            return dbContext.WorldServers
                .ToList()
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