using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Managing;
using FishNet.Object;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server.DatabaseServices
{
	/// <summary>
	/// Handles all Database<->Server Character interactions.
	/// </summary>
	public class CharacterService
	{
		public static int GetCount(NpgsqlDbContext dbContext, string account)
		{
			return dbContext.Characters.Where((c) => c.Account == account && !c.Deleted).Count();
		}

		public static bool ExistsAndOnline(NpgsqlDbContext dbContext, long id)
		{
			if (id == 0)
			{
				return false;
			}
			return dbContext.Characters.FirstOrDefault((c) => c.ID == id &&
															  c.Online) != null;
		}

		public static bool ExistsAndOnline(NpgsqlDbContext dbContext, string characterName)
		{
			return dbContext.Characters.FirstOrDefault((c) => c.NameLowercase == characterName.ToLower() &&
															  c.Online) != null;
		}

		public static bool Exists(NpgsqlDbContext dbContext, string account, string characterName)
		{
			return dbContext.Characters.FirstOrDefault((c) => c.Account == account &&
															  c.NameLowercase == characterName.ToLower()) != null;
		}

		public static CharacterEntity GetByID(NpgsqlDbContext dbContext, long id)
		{
			if (id == 0)
			{
				return null;
			}
			var character = dbContext.Characters.FirstOrDefault(c => c.ID == id);
			if (character == null)
			{
				//throw new Exception($"Couldn't find character with id {id}");
			}
			return character;
		}

		public static long GetIdByName(NpgsqlDbContext dbContext, string name)
		{
			var character = dbContext.Characters.FirstOrDefault(c => c.NameLowercase == name.ToLower());
			if (character == null)
			{
				return 0;
			}
			return character.ID;
		}

		public static string GetNameByID(NpgsqlDbContext dbContext, long id)
		{
			if (id == 0)
			{
				return "";
			}
			var character = dbContext.Characters.FirstOrDefault(c => c.ID == id);
			if (character == null)
			{
				return "";
			}
			return character.Name;
		}

		public static CharacterEntity GetByName(NpgsqlDbContext dbContext, string name, bool checkDeleted = false)
		{
			var character = dbContext.Characters.FirstOrDefault(c => c.NameLowercase == name.ToLower());
			if (character == null ||
				checkDeleted && character.Deleted)
			{
				// Log: $"Couldn't find character with name {name}"
				return null;
			}
			return character;
		}

		public static List<CharacterDetails> GetDetails(NpgsqlDbContext dbContext, string account)
		{
			return dbContext.Characters.Where(c => c.Account == account && !c.Deleted)
										.Select(c => new CharacterDetails()
										{
											CharacterName = c.Name
										})
										.ToList();
		}

		/// <summary>
		/// Selects a character in the database. This is used for validation purposes.
		/// </summary>
		public static bool TrySetSelected(NpgsqlDbContext dbContext, string account, string characterName)
		{
			// get all characters for account
			var characters = dbContext.Characters.Where((c) => c.Account == account && !c.Deleted);

			// deselect all characters
			foreach (var characterEntity in characters)
			{
				characterEntity.Selected = false;
			}
			dbContext.SaveChanges();
			var selectedCharacter = characters.FirstOrDefault((c) => c.Account == account && !c.Deleted && c.NameLowercase == characterName.ToLower());
			if (selectedCharacter != null)
			{
				selectedCharacter.Selected = true;
				dbContext.SaveChanges();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Selects a character in the database. This is used for validation purposes.
		/// </summary>
		public static bool GetSelected(NpgsqlDbContext dbContext, string account)
		{
			return dbContext.Characters.Where((c) => c.Account == account && c.Selected && !c.Deleted) != null;
		}

		/// <summary>
		/// Returns true if we successfully get our selected characters scene for the connections account, otherwise returns false.
		/// </summary>
		public static bool TryGetSelectedSceneName(NpgsqlDbContext dbContext, string account, out string sceneName)
		{
			var character = dbContext.Characters.FirstOrDefault((c) => c.Account == account &&
																	   c.Selected &&
																	   !c.Deleted);

			if (character != null)
			{
				sceneName = character.SceneName;
				return true;
			}
			sceneName = "";
			return false;
		}

		/// <summary>
		/// Returns true if we successfully get our selected character for the connections account, otherwise returns false.
		/// </summary>
		public static bool TryGetSelectedDetails(NpgsqlDbContext dbContext, string account, out long characterID)
		{
			var character = dbContext.Characters.FirstOrDefault((c) => c.Account == account && c.Selected && !c.Deleted);
			if (character != null)
			{
				characterID = character.ID;
				return true;
			}
			characterID = 0;
			return false;
		}
		
		/// <summary>
		/// Returns true if we successfully set our selected character for the connections account, otherwise returns false.
		/// </summary>
		public static void SetOnline(NpgsqlDbContext dbContext, string account, string characterName)
		{
			var selectedCharacter = dbContext.Characters.FirstOrDefault((c) => c.Account == account &&
																			   c.NameLowercase == characterName.ToLower());
			if (selectedCharacter != null)
			{
				selectedCharacter.Online = true;
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Returns true if any of the accounts characters are currently online.
		/// </summary>
		public static bool TryGetOnline(NpgsqlDbContext dbContext, string account)
		{
			var characters = dbContext.Characters.Where((c) => c.Account == account &&
															   c.Online == true &&
															   !c.Deleted).ToList();
			return characters != null && characters.Count > 0;
		}

		/// <summary>
		/// Set the selected characters world server id for the connections account.
		/// </summary>
		public static void SetWorld(NpgsqlDbContext dbContext, string account, long worldServerID)
		{
			if (worldServerID == 0)
			{
				return;
			}
			// get all characters for account
			var character = dbContext.Characters.FirstOrDefault((c) => c.Account == account && c.Selected && !c.Deleted);
			if (character != null)
			{
				character.WorldServerID = worldServerID;
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Set the selected characters scene handle for the connections account.
		/// </summary>
		public static void SetSceneHandle(NpgsqlDbContext dbContext, string account, int sceneHandle)
		{
			// get all characters for account
			var character = dbContext.Characters.FirstOrDefault((c) => c.Account == account && c.Selected && !c.Deleted);
			if (character != null)
			{
				character.SceneHandle = sceneHandle;
				dbContext.SaveChanges();
			}
		}

		public static void Save(NpgsqlDbContext dbContext, List<Character> characters, bool online = true)
		{
			using var dbTransaction = dbContext.Database.BeginTransaction();

			if (characters == null ||
				characters.Count < 1)
			{
				return;
			}
			foreach (Character character in characters)
			{
				Save(dbContext, character, online);
			}

			dbTransaction.Commit();
		}

		/// <summary>
		/// Save a character to the database. Only Scene Servers should be saving characters. A character can only be in one scene at a time.
		/// </summary>
		public static void Save(NpgsqlDbContext dbContext, Character character, bool online = true, CharacterEntity existingCharacter = null)
		{
			if (character == null)
			{
				return;
			}
			if (existingCharacter == null)
			{
				existingCharacter = dbContext.Characters.FirstOrDefault((c) => c.NameLowercase == character.CharacterName.ToLower());

				// if it's still null, throw exception
				if (existingCharacter == null)
				{
					//throw new Exception($"Unable to fetch character with name {character.CharacterName}");
					return;
				}
			}

			// store these into vars so we don't have to access them a bunch of times
			var charPosition = character.Transform.position;
			var rotation = character.Transform.rotation;

			// copy over the new values into the existing entity
			existingCharacter.Name = character.CharacterName;
			existingCharacter.NameLowercase = character.CharacterName.ToLower();
			existingCharacter.Account = character.Account;
			existingCharacter.AccessLevel = (byte)character.AccessLevel;
			existingCharacter.RaceID = character.RaceID.Value;
			existingCharacter.Currency = character.Currency.Value;
			existingCharacter.SceneName = character.SceneName.Value;
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

			dbContext.SaveChanges();

			/*Debug.Log(character.CharacterName + " has been saved at Pos: " +
					  character.Transform.position.ToString() +
					  " Rot: " + rotation);*/
		}

		/// <summary>
		/// KeepData is automatically true... This means we don't actually delete anything. Deleted is simply set to true just incase we need to reinstate a character..
		/// </summary>
		public static void Delete(NpgsqlDbContext dbContext, string account, string characterName, bool keepData = true)
		{
			using var dbTransaction = dbContext.Database.BeginTransaction();

			if (dbTransaction == null)
			{
				return;
			}

			var character = dbContext.Characters.FirstOrDefault(c => c.Account == account &&
																	 c.NameLowercase == characterName.ToLower());

			if (character == null)
			{
				return;
			}

			// possible preserved data
			CharacterAttributeService.Delete(dbContext, character.ID, keepData);
			CharacterAchievementService.Delete(dbContext, character.ID, keepData);
			CharacterBuffService.Delete(dbContext, character.ID, keepData);
			CharacterFriendService.Delete(dbContext, character.ID, keepData);
			CharacterInventoryService.Delete(dbContext, character.ID, keepData);
			CharacterEquipmentService.Delete(dbContext, character.ID, keepData);
			CharacterBankService.Delete(dbContext, character.ID, keepData);
			CharacterAbilityService.Delete(dbContext, character.ID, keepData);
			CharacterKnownAbilityService.Delete(dbContext, character.ID, keepData);

			// complete deletions
			CharacterGuildService.Delete(dbContext, character.ID);
			CharacterPartyService.Delete(dbContext, character.ID);

			if (keepData)
			{
				character.TimeDeleted = DateTime.UtcNow;
				character.Deleted = true;
				dbContext.SaveChanges();
			}
			else
			{
				dbContext.Characters.Remove(character);
				dbContext.SaveChanges();
			}

			dbTransaction.Commit();
		}

		/// <summary>
		/// Attempts to load a character from the database. The character is loaded to the last known position/rotation and set inactive.
		/// </summary>
		public static bool TryGet(NpgsqlDbContext dbContext, long characterID, NetworkManager networkManager, out Character character)
		{
			character = null;

			using var dbTransaction = dbContext.Database.BeginTransaction();

			var dbCharacter = dbContext.Characters.FirstOrDefault((c) => c.ID == characterID &&
																		 !c.Deleted);
			if (dbCharacter != null &&
				dbTransaction != null)
			{
				// validate race
				RaceTemplate raceTemplate = RaceTemplate.Get<RaceTemplate>(dbCharacter.RaceID);
				if (raceTemplate == null ||
					raceTemplate.Prefab == null)
				{
					return false;
				}

				// validate spawnable prefab
				Character characterPrefab = raceTemplate.Prefab.GetComponent<Character>();
				if (characterPrefab == null ||
					networkManager.SpawnablePrefabs.GetObject(true, characterPrefab.NetworkObject.PrefabId) == null)
				{
					return false;
				}

				Vector3 dbPosition = new Vector3(dbCharacter.X, dbCharacter.Y, dbCharacter.Z);
				Quaternion dbRotation = new Quaternion(dbCharacter.RotX, dbCharacter.RotY, dbCharacter.RotZ, dbCharacter.RotW);

				// instantiate the character object
				NetworkObject nob = networkManager.GetPooledInstantiated(characterPrefab.NetworkObject, dbPosition, dbRotation, true);
				character = nob.GetComponent<Character>();
				if (character == null)
				{
					return false;
				}

				character.Motor.SetPositionAndRotationAndVelocity(dbPosition, dbRotation, Vector3.zero);
				character.ID = dbCharacter.ID;
				character.CharacterName = dbCharacter.Name;
				character.CharacterNameLower = dbCharacter.NameLowercase;
				character.Account = dbCharacter.Account;
				character.WorldServerID = dbCharacter.WorldServerID;
				character.AccessLevel = (AccessLevel)dbCharacter.AccessLevel;
				character.SetSyncVarDatabaseValue(character.RaceID, dbCharacter.RaceID);
				character.SetSyncVarDatabaseValue(character.Currency, dbCharacter.Currency);
				character.SetSyncVarDatabaseValue(character.RaceName, raceTemplate.Name);
				character.SceneHandle = dbCharacter.SceneHandle;
				character.SetSyncVarDatabaseValue(character.SceneName, dbCharacter.SceneName);

				// character becomes immortal when loading.. just in case..
				if (character.TryGet(out CharacterDamageController damageController))
				{
					damageController.Immortal = true;
				}

				CharacterAttributeService.Load(dbContext, character);
				CharacterAchievementService.Load(dbContext, character);
				CharacterBuffService.Load(dbContext, character);
				CharacterGuildService.Load(dbContext, character);
				CharacterPartyService.Load(dbContext, character);
				CharacterFriendService.Load(dbContext, character);
				CharacterInventoryService.Load(dbContext, character);
				CharacterEquipmentService.Load(dbContext, character);
				CharacterBankService.Load(dbContext, character);
				CharacterAbilityService.Load(dbContext, character);
				CharacterKnownAbilityService.Load(dbContext, character);

				/*Debug.Log(dbCharacter.Name + " has been loaded at Pos:" +
					  nob.transform.position.ToString() +
					  " Rot:" + nob.transform.rotation.ToString());*/

				dbTransaction.Commit();
			}
			return true;
		}
	}
}