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
        public static WorldServerEntity AddWorldServer(
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

        public static void WorldServerPulse(ServerDbContext dbContext, string name)
        {
            var worldServer = dbContext.WorldServers
                .FirstOrDefault(server => server.Name.ToLower() == name.ToLower());
            if (worldServer == null) throw new Exception($"Couldn't find world with name {name}");
            
            worldServer.LastPulse = DateTime.UtcNow;
        }
        
        public static void DeleteWorldServer(ServerDbContext dbContext, string name) 
        {
            var worldServer = dbContext.WorldServers
                .FirstOrDefault(server => server.Name.ToLower() == name.ToLower());
            if (worldServer == null) throw new Exception($"Couldn't find world with name {name}");

            dbContext.WorldServers.Remove(worldServer);
        }
        
        public static List<WorldServerDetails> GetWorldServerList(ServerDbContext dbContext)
        {
            return dbContext.WorldServers
                .ToList()
                .Select(server => new WorldServerDetails()
                {
                    name = server.Name,
                    lastPulse = server.LastPulse,
                    address = server.Address,
                    port = server.Port,
                    characterCount = server.CharacterCount,
                    locked = server.Locked,
                })
                .ToList();
        }
    }
}