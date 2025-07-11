﻿using FishNet.Connection;
using FishNet.Transporting;
using System;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Logging;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FishMMO.Server
{
	/// <summary>
	/// Server Character Creation system.
	/// </summary>
	public class CharacterCreateSystem : ServerBehaviour
	{
		public WorldSceneDetailsCache WorldSceneDetailsCache;
		public int MaxCharacters = 8;
		public List<AbilityTemplate> StartingAbilities = new List<AbilityTemplate>();
		public List<BaseItemTemplate> StartingInventoryItems = new List<BaseItemTemplate>();
		public List<EquippableItemTemplate> StartingEquipment = new List<EquippableItemTemplate>();

		public override void InitializeOnce()
		{
			if (Server != null)
			{
				Server.RegisterBroadcast<CharacterCreateBroadcast>(OnServerCharacterCreateBroadcastReceived, true);
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			if (Server != null)
			{
				Server.UnregisterBroadcast<CharacterCreateBroadcast>(OnServerCharacterCreateBroadcastReceived);
			}
		}

		private void OnServerCharacterCreateBroadcastReceived(NetworkConnection conn, CharacterCreateBroadcast msg, Channel channel)
		{
			if (conn.IsActive)
			{
				// Validate character creation data
				if (!Constants.Authentication.IsAllowedCharacterName(msg.CharacterName))
				{
					//Log.Debug("CharacterCreateSystem", "Invalid Character Name.");

					// Invalid character name
					Server.Broadcast(conn, new CharacterCreateResultBroadcast()
					{
						Result = CharacterCreateResult.InvalidCharacterName,
					}, true, Channel.Reliable);
					return;
				}

				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				if (!AccountManager.GetAccountNameByConnection(conn, out string accountName))
				{
					//Log.Debug("CharacterCreateSystem", "Account not found.");

					// Account not found??
					conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
					return;
				}
				int characterCount = CharacterService.GetCount(dbContext, accountName);
				if (characterCount >= MaxCharacters)
				{
					//Log.Debug("CharacterCreateSystem", "Too many characters.");

					// Too many characters
					Server.Broadcast(conn, new CharacterCreateResultBroadcast()
					{
						Result = CharacterCreateResult.TooMany,
					}, true, Channel.Reliable);
					return;
				}
				var character = CharacterService.GetByName(dbContext, msg.CharacterName);
				if (character != null)
				{
					//Log.Debug("CharacterCreateSystem", "Character name is taken.");

					// Character name already taken
					Server.Broadcast(conn, new CharacterCreateResultBroadcast()
					{
						Result = CharacterCreateResult.CharacterNameTaken,
					}, true, Channel.Reliable);
					return;
				}

				if (WorldSceneDetailsCache == null ||
					WorldSceneDetailsCache.Scenes == null ||
					WorldSceneDetailsCache.Scenes.Count < 1)
				{
					//Log.Debug("CharacterCreateSystem", "Spawn positions invalid.");

					// Failed to find spawn positions to validate with
					Server.Broadcast(conn, new CharacterCreateResultBroadcast()
					{
						Result = CharacterCreateResult.InvalidSpawn,
					}, true, Channel.Reliable);
					return;
				}
				// Validate spawn details
				if (WorldSceneDetailsCache.Scenes.TryGetValue(msg.SceneName, out WorldSceneDetails details))
				{
					// Validate spawner
					if (details.InitialSpawnPositions.TryGetValue(msg.SpawnerName, out CharacterInitialSpawnPositionDetails initialSpawnPosition))
					{
						//Log.Debug("CharacterCreateSystem", $"RaceTemplate ID: {msg.RaceTemplateID}");

						// Validate race
						RaceTemplate raceTemplate = RaceTemplate.Get<RaceTemplate>(msg.RaceTemplateID);
						if (raceTemplate == null)
						{
							//Log.Debug("CharacterCreateSystem", "RaceTemplate is invalid.");

							conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
							return;
						}
						if (raceTemplate.Prefab == null)
						{
							//Log.Debug("CharacterCreateSystem", "RaceTemplate Prefab is invalid.");

							conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
							return;
						}
						if (raceTemplate.GetModelReference(msg.ModelIndex) == null)
						{
							//Log.Debug("CharacterCreateSystem", "ModelIndex is invalid.");

							conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
							return;
						}

						bool validateAllowedRace = false;
						foreach (RaceTemplate t in initialSpawnPosition.AllowedRaces)
						{
							if (t.Name == raceTemplate.Name)
							{
								validateAllowedRace = true;
								break;
							}
						}
						if (!validateAllowedRace)
						{
							//Log.Debug("CharacterCreateSystem", "Race not allowed.");

							conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
							return;
						}

						// Validate spawnable prefab
						IPlayerCharacter characterPrefab = raceTemplate.Prefab.GetComponent<IPlayerCharacter>();
						if (characterPrefab == null ||
							Server.NetworkManager.SpawnablePrefabs.GetObject(true, characterPrefab.NetworkObject.PrefabId) == null)
						{
							//Log.Debug("CharacterCreateSystem", "Character prefab is broken or not loaded.");

							conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
							return;
						}

						// Create the new character
						var newCharacter = new CharacterEntity()
						{
							Account = accountName,
							Name = msg.CharacterName,
							NameLowercase = msg.CharacterName?.ToLower(),
							RaceID = msg.RaceTemplateID,
							ModelIndex = msg.ModelIndex,
							BindScene = msg.SceneName,
							BindX = initialSpawnPosition.Position.x,
							BindY = initialSpawnPosition.Position.y,
							BindZ = initialSpawnPosition.Position.z,
							SceneName = initialSpawnPosition.SceneName,
							X = initialSpawnPosition.Position.x,
							Y = initialSpawnPosition.Position.y,
							Z = initialSpawnPosition.Position.z,
							RotX = initialSpawnPosition.Rotation.x,
							RotY = initialSpawnPosition.Rotation.y,
							RotZ = initialSpawnPosition.Rotation.z,
							RotW = initialSpawnPosition.Rotation.w,
							AccessLevel = (byte)AccessLevel.Player,
							TimeCreated = DateTime.UtcNow,
						};
						dbContext.Characters.Add(newCharacter);
						dbContext.SaveChanges();

						Dictionary<int, CharacterAttributeEntity> initialAttributes = new Dictionary<int, CharacterAttributeEntity>();

						// Create the initial character attributes set
						if (raceTemplate.InitialAttributes != null &&
							raceTemplate.InitialAttributes.Attributes.Count > 0)
						{
							foreach (CharacterAttributeTemplate template in raceTemplate.InitialAttributes.Attributes)
							{
								initialAttributes.Add(template.ID, new CharacterAttributeEntity()
								{
									CharacterID = newCharacter.ID,
									TemplateID = template.ID,
									Value = template.InitialValue,
									CurrentValue = template.IsResourceAttribute ? template.InitialValue : 0.0f,
								});

								//Log.Debug("CharacterCreateSystem", $"{template.Name} : Initial {template.InitialValue}");
							}
						}

						// Add character factions
						if (raceTemplate.InitialFaction != null)
						{
							foreach (FactionTemplate faction in raceTemplate.InitialFaction.DefaultAllied)
							{
								dbContext.CharacterFactions.Add(new CharacterFactionEntity()
								{
									CharacterID = newCharacter.ID,
									TemplateID = faction.ID,
									Value = FactionTemplate.Maximum,
								});
							}
							foreach (FactionTemplate faction in raceTemplate.InitialFaction.DefaultNeutral)
							{
								dbContext.CharacterFactions.Add(new CharacterFactionEntity()
								{
									CharacterID = newCharacter.ID,
									TemplateID = faction.ID,
									Value = 0,
								});
							}
							foreach (FactionTemplate faction in raceTemplate.InitialFaction.DefaultHostile)
							{
								dbContext.CharacterFactions.Add(new CharacterFactionEntity()
								{
									CharacterID = newCharacter.ID,
									TemplateID = faction.ID,
									Value = FactionTemplate.Minimum,
								});
							}
							dbContext.SaveChanges();
						}

						// Add starting abilities
						AddStartingAbilities(dbContext, newCharacter.ID, StartingAbilities);
						AddStartingAbilities(dbContext, newCharacter.ID, raceTemplate.StartingAbilities);

						// Add inventory items
						AddStartingItems(dbContext, newCharacter.ID, StartingInventoryItems);
						AddStartingItems(dbContext, newCharacter.ID, raceTemplate.StartingInventoryItems);

						// Add equipped items
						AddStartingEquipment(dbContext, newCharacter.ID, StartingEquipment, initialAttributes);
						AddStartingEquipment(dbContext, newCharacter.ID, raceTemplate.StartingEquipment, initialAttributes);

						// Save the initial character attributes to the database
						if (initialAttributes != null &&
							initialAttributes.Count > 0)
						{
							foreach (KeyValuePair<int, CharacterAttributeEntity> pair in initialAttributes)
							{
								dbContext.CharacterAttributes.Add(pair.Value);
							}
							dbContext.SaveChanges();
						}

						// Send success to the client
						Server.Broadcast(conn, new CharacterCreateResultBroadcast()
						{
							Result = CharacterCreateResult.Success,
						}, true, Channel.Reliable);

						// Send the create broadcast back to the client
						Server.Broadcast(conn, msg, true, Channel.Reliable);
					}
					else
					{
						Log.Debug("CharacterCreateSystem", "Unable to get find initial spawn position for Spawner.");
					}
				}
				else
				{
					Log.Debug("CharacterCreateSystem", "Unable to get World Scene Details.");
				}
			}
		}

		private void AddStartingAbilities(NpgsqlDbContext dbContext, long characterID, List<AbilityTemplate> startingAbilities)
		{
			if (startingAbilities != null)
			{
				foreach (AbilityTemplate startingAbility in startingAbilities)
				{
					var dbAbility = new CharacterAbilityEntity()
					{
						CharacterID = characterID,
						TemplateID = startingAbility.ID,
						AbilityEvents = startingAbility.Events.Select(a => a.ID).ToList(),
					};
					dbContext.CharacterAbilities.Add(dbAbility);
				}
				dbContext.SaveChanges();
			}
		}

		private void AddStartingItems(NpgsqlDbContext dbContext, long characterID, List<BaseItemTemplate> startingItems)
		{
			if (startingItems != null)
			{
				for (int i = 0; i < startingItems.Count; ++i)
				{
					BaseItemTemplate itemTemplate = startingItems[i];
					var dbItem = new CharacterInventoryEntity()
					{
						CharacterID = characterID,
						TemplateID = itemTemplate.ID,
						Slot = i,
						Seed = 0,
						Amount = 1,
					};
					dbContext.CharacterInventoryItems.Add(dbItem);
				}
				dbContext.SaveChanges();
			}
		}

		private void AddStartingEquipment(NpgsqlDbContext dbContext, long characterID, List<EquippableItemTemplate> startingEquipment, Dictionary<int, CharacterAttributeEntity> initialAttributes)
		{
			if (startingEquipment != null)
			{
				for (int i = 0; i < startingEquipment.Count; ++i)
				{
					EquippableItemTemplate itemTemplate = startingEquipment[i];

					// Generate the item attributes so we can add them to the initial character attributes
					ItemGenerator itemGenerator = new ItemGenerator();
					itemGenerator.Generate(1, itemTemplate);

					// Update our initial attribute values to include equipped item attributes
					foreach (ItemAttribute itemAttribute in itemGenerator.Attributes.Values)
					{
						if (initialAttributes.TryGetValue(itemAttribute.Template.CharacterAttribute.ID, out CharacterAttributeEntity attributeEntity))
						{
							//Log.Debug("CharacterCreateSystem", $"{itemTemplate.Name} - {itemAttribute.Template.CharacterAttribute.Name} adding {itemAttribute.value}");
							attributeEntity.Value += itemAttribute.value;
						}
					}

					// Add the equipped item to the database
					var dbItem = new CharacterEquipmentEntity()
					{
						CharacterID = characterID,
						TemplateID = itemTemplate.ID,
						Slot = (int)itemTemplate.Slot,
						Seed = itemGenerator.Seed,
						Amount = 0,
					};
					dbContext.CharacterEquippedItems.Add(dbItem);
				}
				dbContext.SaveChanges();
			}
		}
	}
}