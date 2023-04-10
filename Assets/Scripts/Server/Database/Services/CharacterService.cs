using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Server.Entities;

namespace Server.Services
{
    public class CharacterService
    {
        public static CharacterEntity GetCharacterById(ServerDbContext dbContext, long id)
        {
            var character = dbContext.Characters.FirstOrDefault(c => c.Id == id);
            
            if (character == null) throw new Exception($"Couldn't find character with id {id}");
            return character;
        }
        
        public static CharacterEntity GetCharacterByName(ServerDbContext dbContext, string name)
        {
            var character = dbContext.Characters
                .FirstOrDefault(c => c.NameLowercase == name.ToLower());
            
            if (character == null) throw new Exception($"Couldn't find character with name {name}");
            return character;
        }

        public static List<CharacterDetails> GetCharacterList(ServerDbContext dbContext, string accountName)
        {
            return dbContext.Characters
                .Where(c => c.Account == accountName.ToLower())
                .ToList()
                .Select(c => new CharacterDetails()
                {
                    characterName = c.Name
                })
                .ToList();
        }

        public static void DeleteCharacter(ServerDbContext dbContext, string account, string characterName)
        {
            var character = dbContext.Characters
                .FirstOrDefault(c => c.Account == account.ToLower() && 
                                                 c.NameLowercase == characterName.ToLower());

            if (character == null) throw new Exception($"Can't find character with name {characterName}");
            character.TimeDeleted = DateTime.UtcNow;
            character.Deleted = true;
        }
        
        /*public static async Task<CharacterEntity> AddCharacter(ServerDbContext dbContext, CharacterEntity character)
        {
            if (character.Id > 0)
            {
                var existing = await dbContext.Characters
                    .FirstOrDefaultAsync(c => c.Id == character.Id);

                if (existing == null) throw new Exception($"Couldn't find character with id {character.Id}");

                existing = character;
                return existing;
            }
        }*/
        
        public static void ExistingCharacter(ServerDbContext dbContext)
        {
        }
        
        public static void SaveCharacter(ServerDbContext dbContext)
        {
        }
    }
}