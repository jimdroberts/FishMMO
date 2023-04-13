using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using SQLite;
using UnityEngine;

namespace Server
{
	public partial class Database
	{
		class characters
		{
			[PrimaryKey] // important for performance: O(log n) instead of O(n)
			[Collation("NOCASE")] // [COLLATE NOCASE for case insensitive compare. this way we can't both create 'Archer' and 'archer' as characters]
			public string name { get; set; }
			[Indexed] // add index on account to avoid full scans when loading characters
			public string account { get; set; }
			public string raceName { get; set; }
			public string sceneName { get; set; }
			public float x { get; set; }
			public float y { get; set; }
			public float z { get; set; }
			public float rotX { get; set; }
			public float rotY { get; set; }
			public float rotZ { get; set; }
			public float rotW { get; set; }
			public bool isGameMaster { get; set; }
			public bool selected { get; set; }
			// online status can be checked from external programs with either just
			// just 'online', or 'online && (DateTime.UtcNow - lastsaved) <= 1min)
			// which is robust to server crashes too.
			public bool online { get; set; }
			public DateTime lastSaved { get; set; }
			public DateTime timeDeleted { get; set; }
			public bool deleted { get; set; }
		}

		// character data //////////////////////////////////////////////////////////
		public bool CharacterExists(string characterName)
		{
			// checks deleted ones too so we don't end up with duplicates if we un-delete one
			return connection.FindWithQuery<characters>("SELECT * FROM characters WHERE name=?", characterName) != null;
		}

		public void CharacterDelete(string characterName)
		{
			// soft delete the character so it can always be restored later
			connection.Execute("UPDATE characters SET deleted=1 WHERE name=?", characterName);
		}

		/// <summary>
		/// Returns a very basic character list. Details include character name and equipped items. Use this for character selection.
		/// </summary>
		public List<global::CharacterDetails> GetCharacterList(string account)
		{
            List<global::CharacterDetails> result = new List<global::CharacterDetails>();
			foreach (characters character in connection.Query<characters>("SELECT * FROM characters WHERE account=? AND deleted=0", account))
			{
				result.Add(new global::CharacterDetails
				{
					characterName = character.name,
				});
			}
			return result;
		}

		/// <summary>
		/// Returns true if we successfully set our selected character for the connections account, otherwise returns false.
		/// </summary>
		public bool TrySetCharacterSelected(string account, string characterName)
		{
			// deselect all characters
			connection.Execute("UPDATE characters SET selected=0 WHERE account=?", account);

			if (connection.FindWithQuery<characters>("SELECT * FROM characters WHERE name=? AND account=? AND deleted=0", characterName, account) != null)
			{
				connection.Execute("UPDATE characters SET selected=1 WHERE name=? AND account=?", characterName, account);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Returns true if we successfully get our selected character for the connections account, otherwise returns false.
		/// </summary>
		public bool TryGetSelectedCharacterDetails(string account, out string characterName)
		{
			characters characterRow = connection.FindWithQuery<characters>("SELECT * FROM characters WHERE account=? AND selected=1 AND deleted=0", account);
			if (characterRow != null)
			{
				characterName = characterRow.name;
				return true;
			}
			characterName = "";
			return false;
		}

		/// <summary>
		/// Save a new character to the database. Only the Login Server should be calling this function. A character can only be in one scene at a time.
		/// </summary>
		public void NewCharacter(string accountName, string characterName, string raceName, CharacterInitialSpawnPosition initialSpawnPosition)
		{
			connection.BeginTransaction();
			connection.InsertOrReplace(new characters
			{
				name = characterName,
				account = accountName,
				isGameMaster = false,
				raceName = raceName == null || raceName.Length < 1 ? "Player" : raceName,
				sceneName = initialSpawnPosition.sceneName,
				x = initialSpawnPosition.position.x,
				y = initialSpawnPosition.position.y,
				z = initialSpawnPosition.position.z,
				rotX = initialSpawnPosition.rotation.x,
				rotY = initialSpawnPosition.rotation.y,
				rotZ = initialSpawnPosition.rotation.z,
				rotW = initialSpawnPosition.rotation.w,
				selected = false,
				online = false,
				lastSaved = DateTime.UtcNow,
			});

			//SaveAttributes(character.AttributeController);
			//SaveCooldowns(character.CooldownController);
			//SaveInventory(character.InventoryController);
			//SaveEquipment(character.EquipmentController);
			//SaveAbilities(character.AbilityController);
			//SaveAchievements(character.AchievementController);
			//SaveBuffs(character.BuffController);
			//SaveQuests(character.QuestController);
			//SavePosition(character.CharacterMovementController);
			//SaveGuild(character.GuildController);
			//SaveParty(character.PartyController);

			connection.Commit();
		}

		
		public void DeleteCharacter(string account, string characterName)
		{
			connection.BeginTransaction(); // transaction for performance
			connection.Execute("UPDATE characters SET timeDeleted=? WHERE account=? AND name=?", DateTime.UtcNow, account, characterName);
			connection.Execute("UPDATE characters SET deleted=? WHERE account=? AND name=?", true, account, characterName);
			connection.Commit(); // end transaction
		}

		/// <summary>
		/// Attempts to load a character from the database. The character is loaded to the last known position/rotation and set inactive.
		/// </summary>
		public bool TryLoadCharacter(string characterName, List<NetworkObject> prefabs, NetworkManager networkManager, out Character character)
		{
			characters row = connection.FindWithQuery<characters>("SELECT * FROM characters WHERE name=? AND deleted=0", characterName);
			if (row != null)
			{
				NetworkObject prefab = prefabs.Find(p => p.name == row.raceName);
				if (prefab != null)
				{
					Vector3 spawnPos = new Vector3(row.x, row.y, row.z);
					Quaternion spawnRot = new Quaternion(row.rotX, row.rotY, row.rotZ, row.rotW);

					// set the prefab position to our spawn position so the player spawns in the right spot
					prefab.transform.position = spawnPos;

					// set the prefab rotation so our player spawns with the proper orientation
					prefab.transform.rotation = spawnRot;

					// instantiate the character object
					NetworkObject nob = networkManager.GetPooledInstantiated(prefab, true);

					// immediately deactive the game object.. we are not ready yet
					nob.gameObject.SetActive(false);

					// set position and rotation just incase..
					nob.transform.SetPositionAndRotation(spawnPos, spawnRot);

					Debug.Log("[" + DateTime.UtcNow + "] " + row.name + " has been instantiated at Pos:" + nob.transform.position.ToString() + " Rot:" + nob.transform.rotation.ToString());

					character = nob.GetComponent<Character>();
					if (character != null)
					{
						character.characterName = row.name;
						character.account = row.account;
						character.isGameMaster = row.isGameMaster;
						character.raceName = row.raceName;
						character.sceneName = row.sceneName;
						return true;
					}
				}
			}
			character = null;
			return false;
		}
	}
}