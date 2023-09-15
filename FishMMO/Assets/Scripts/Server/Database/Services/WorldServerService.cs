using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using FishMMO_DB;
using FishMMO_DB.Entities;

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
            bool locked
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

            return server;
        }

        public static void Pulse(ServerDbContext dbContext, string name, int characterCount)
        {
            var worldServer = dbContext.WorldServers
                .FirstOrDefault(server => server.Name.ToLower() == name.ToLower());
            if (worldServer == null) throw new Exception($"Couldn't find world with name {name}");
            
            worldServer.LastPulse = DateTime.UtcNow;
            worldServer.CharacterCount = characterCount;
        }
        
        public static void Delete(ServerDbContext dbContext, string name) 
        {
            var worldServer = dbContext.WorldServers
                .FirstOrDefault(server => server.Name.ToLower() == name.ToLower());
            if (worldServer == null) throw new Exception($"Couldn't find world with name {name}");

            dbContext.WorldServers.Remove(worldServer);
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