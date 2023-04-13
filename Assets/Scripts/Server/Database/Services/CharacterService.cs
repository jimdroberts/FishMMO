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

        public static void SaveCharacters(ServerDbContext dbContext, List<Character> characters, bool online = true)
        {
            // get characters by their names
            var characterNames = characters.Select((c) => c.characterName.ToLower()).ToList();
            var dbCharacters = dbContext.Characters.Where((c) => characterNames.Contains(c.NameLowercase)).ToList();
            
            //
            foreach (Character character in characters)
            {
                SaveCharacter(dbContext, character, online);
            }
        }
        
        /// <summary>
        /// Save a character to the database. Only Scene Servers should be saving characters. A character can only be in one scene at a time.
        /// </summary>
        public static void SaveCharacter(ServerDbContext dbContext, Character character, bool online = true, 
            CharacterEntity existingCharacter = null)
        {
            if (existingCharacter == null)
            {
                existingCharacter = dbContext.Characters.FirstOrDefault((c) => c.NameLowercase == character.characterName.ToLower());
            }
            
            // if it's still null, throw exception
            if (existingCharacter == null)
            {
                throw new Exception($"Unable to fetch character with name {character.characterName}");
            }

            // store these into vars so we don't have to access them a bunch of times
            var charTransform = character.transform;
            var charPosition = charTransform.position;
            var rotation = charTransform.rotation;

            // copy over the new values into the existing entity
            existingCharacter.Name = character.characterName;
            existingCharacter.NameLowercase = character.characterName.ToLower();
            existingCharacter.Account = character.account;
            existingCharacter.IsGameMaster = character.isGameMaster;
            existingCharacter.RaceName = character.raceName;
            existingCharacter.SceneName = character.sceneName;
            existingCharacter.X = charPosition.x;
            existingCharacter.Y = charPosition.y;
            existingCharacter.Z = charPosition.z;
            existingCharacter.RotX = rotation.x;
            existingCharacter.RotY = rotation.y;
            existingCharacter.RotZ = rotation.z;
            existingCharacter.RotW = rotation.w;
            existingCharacter.Online = online;
            existingCharacter.LastSaved = DateTime.UtcNow;
        }

        /// <summary>
        /// Don't actually delete anything. Deleted is simply set to true just incase we need to reinstate a character..
        /// </summary>
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
        
        
    }
}