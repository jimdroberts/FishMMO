using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Managing;
using FishNet.Object;
using FishMMO_DB;
using FishMMO_DB.Entities;
using UnityEngine;

namespace FishMMO.Server.Services
{
	/// <summary>
	/// Handles all Database<->Server Character interactions.
	/// </summary>
	public class CharacterService
	{
		public static CharacterEntity GetById(ServerDbContext dbContext, long id)
		{
			var character = dbContext.Characters.FirstOrDefault(c => c.Id == id);

			if (character == null)
			{
				//throw new Exception($"Couldn't find character with id {id}");
			}
			return character;
		}

		public static CharacterEntity GetByName(ServerDbContext dbContext, string name)
		{
			var character = dbContext.Characters
				.FirstOrDefault(c => c.NameLowercase == name.ToLower());

			if (character == null)
			{
				// Log: $"Couldn't find character with name {name}"
			}
			return character;
		}

		public static List<CharacterDetails> GetDetails(ServerDbContext dbContext, string account)
		{
			return dbContext.Characters
				.Where(c => c.Account == account && !c.Deleted)
				.ToList()
				.Select(c => new CharacterDetails()
				{
					CharacterName = c.Name
				})
				.ToList();
		}

		public static void Save(ServerDbContext dbContext, List<Character> characters, bool online = true)
		{
			// get characters by their names
			var characterNames = characters.Select((c) => c.CharacterName.ToLower()).ToList();
			var dbCharacters = dbContext.Characters.Where((c) => characterNames.Contains(c.NameLowercase)).ToList();
			
			//
			foreach (Character character in characters)
			{
				Save(dbContext, character, online);
			}
		}
		
		/// <summary>
		/// Save a character to the database. Only Scene Servers should be saving characters. A character can only be in one scene at a time.
		/// </summary>
		public static void Save(ServerDbContext dbContext, Character character, bool online = true, 
			CharacterEntity existingCharacter = null)
		{
			if (existingCharacter == null)
			{
				existingCharacter = dbContext.Characters.FirstOrDefault((c) => c.NameLowercase == character.CharacterName.ToLower());
			}
			
			// if it's still null, throw exception
			if (existingCharacter == null)
			{
				//throw new Exception($"Unable to fetch character with name {character.CharacterName}");
				return;
			}

			// store these into vars so we don't have to access them a bunch of times
			var charPosition = character.Transform.position;
			var rotation = character.Transform.rotation;

			// copy over the new values into the existing entity
			existingCharacter.Name = character.CharacterName;
			existingCharacter.NameLowercase = character.CharacterName.ToLower();
			existingCharacter.Account = character.Account;
			existingCharacter.IsGameMaster = character.IsGameMaster;
			existingCharacter.RaceID = character.RaceID;
			existingCharacter.SceneName = character.SceneName;
			existingCharacter.X = charPosition.x;
			existingCharacter.Y = charPosition.y;
			existingCharacter.Z = charPosition.z;
			existingCharacter.RotX = rotation.x;
			existingCharacter.RotY = rotation.y;
			existingCharacter.RotZ = rotation.z;
			existingCharacter.RotW = rotation.w;
			existingCharacter.Online = online;
			existingCharacter.LastSaved = DateTime.UtcNow;

			CharacterAttributeService.Save(dbContext, character);
			CharacterAchievementService.Save(dbContext, character);
			CharacterBuffService.Save(dbContext, character);
		}

		/// <summary>
		/// KeepData is automatically true... This means we don't actually delete anything. Deleted is simply set to true just incase we need to reinstate a character..
		/// </summary>
		public static void Delete(ServerDbContext dbContext, string account, string characterName, bool keepData = false)
		{
			var character = dbContext.Characters
							.FirstOrDefault(c => c.Account == account &&
												 c.NameLowercase == characterName.ToLower());

			if (character == null) return;

			if (keepData)
			{
				character.TimeDeleted = DateTime.UtcNow;
				character.Deleted = true;
			}
			else
			{
				CharacterAttributeService.Delete(dbContext, character.Id, keepData);
				CharacterAchievementService.Delete(dbContext, character.Id, keepData);
				CharacterBuffService.Delete(dbContext, character.Id, keepData);
				dbContext.Characters.Remove(character);
			}
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
		
		public static bool TrySetSelected(ServerDbContext dbContext, string account, string characterName)
		{
			// get all characters for account
			var characters = dbContext.Characters
				.Where((c) => c.Account == account && !c.Deleted)
				.ToList();
			
			// deselect all characters
			foreach (var characterEntity in characters)
			{
				characterEntity.Selected = false;
			}

			var selectedCharacter = characters.FirstOrDefault((c) => c.NameLowercase == characterName.ToLower()); 
			if (selectedCharacter != null)
			{
				selectedCharacter.Selected = true;
				return true;
			}
			return false;
		}
		
		/// <summary>
		/// Returns true if we successfully get our selected character for the connections account, otherwise returns false.
		/// </summary>
		public static bool TryGetSelectedDetails(ServerDbContext dbContext, string account, out long characterId)
		{
			var character = dbContext.Characters
				.FirstOrDefault((c) => c.Account == account && c.Selected && !c.Deleted);
			if (character != null)
			{
				characterId = character.Id;
				return true;
			}
			characterId = 0;
			return false;
		}
		
		/// <summary>
		/// Returns true if we successfully set our selected character for the connections account, otherwise returns false.
		/// </summary>
		public static bool TrySetOnline(ServerDbContext dbContext, string account, string characterName)
		{
			var characters = dbContext.Characters
				.Where((c) => c.Account == account && !c.Deleted)
				.ToList();
			if (characters.Any((c) => c.Online))
			{
				// a character on this account is already online, we should disconnect them FIXME
				return false;
			}

			var selectedCharacter = characters.FirstOrDefault((c) => c.NameLowercase == characterName.ToLower());
			if (selectedCharacter != null)
			{
				selectedCharacter.Online = true;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Returns true if any of the accounts characters are currently online.
		/// </summary>
		public static bool TryGetOnline(ServerDbContext dbContext, string account)
		{
			var characters = dbContext.Characters
				.Where((c) => c.Account == account && !c.Deleted)
				.ToList();

			foreach (var characterEntity in characters)
			{
				if (characterEntity.Online)
				{
					if (characterEntity.LastSaved.AddMinutes(10) < DateTime.UtcNow)
					{
						characterEntity.Online = false;
					}
					else
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Returns true if we successfully get our selected characters scene for the connections account, otherwise returns false.
		/// </summary>
		public static bool TryGetSelectedSceneName(ServerDbContext dbContext, string account, out string sceneName)
		{
			var character = dbContext.Characters
				.FirstOrDefault((c) => c.Account == account && c.Selected && !c.Deleted);
			if (character != null)
			{
				sceneName = character.SceneName;
				return true;
			}
			sceneName = "";
			return false;
		}
		
		/// <summary>
		/// Attempts to load a character from the database. The character is loaded to the last known position/rotation and set inactive.
		/// </summary>
		public static bool TryGet(ServerDbContext dbContext, long characterId, NetworkManager networkManager, out Character character)
		{
			var dbCharacter =
				dbContext.Characters.FirstOrDefault((c) => c.Id == characterId && !c.Deleted);
			if (dbCharacter != null)
			{
				// find prefab
				NetworkObject prefab = networkManager.SpawnablePrefabs.GetObject(true, dbCharacter.RaceID);
				if (prefab != null)
				{
					// instantiate the character object
					NetworkObject nob = networkManager.GetPooledInstantiated(prefab, prefab.SpawnableCollectionId, true);

					character = nob.GetComponent<Character>();
					if (character != null)
					{
						character.Motor.SetPositionAndRotationAndVelocity(new Vector3(dbCharacter.X, dbCharacter.Y, dbCharacter.Z),
																		  new Quaternion(dbCharacter.RotX, dbCharacter.RotY, dbCharacter.RotZ, dbCharacter.RotW),
																		  Vector3.zero);
						character.ID = dbCharacter.Id;
						character.CharacterName = dbCharacter.Name;
						character.Account = dbCharacter.Account;
						character.IsGameMaster = dbCharacter.IsGameMaster;
						character.RaceID = dbCharacter.RaceID;
						character.RaceName = prefab.name;
						character.SceneName = dbCharacter.SceneName;
						character.IsTeleporting = false;

						CharacterAttributeService.Load(dbContext, character);
						CharacterAchievementService.Load(dbContext, character);
						CharacterBuffService.Load(dbContext, character);

						return true;
					}

					Debug.Log(dbCharacter.Name + " has been instantiated at Pos:" +
							  nob.transform.position.ToString() + " Rot:" + nob.transform.rotation.ToString());
				}
			}
			character = null;
			return false;
		}
	}
}