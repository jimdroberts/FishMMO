using FishNet.Connection;
using FishNet.Transporting;
using System;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using System.Collections.Generic;
using System.Linq;

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
					// Account not found??
					conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
					return;
				}
				int characterCount = CharacterService.GetCount(dbContext, accountName);
				if (characterCount >= MaxCharacters)
				{
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
						// Validate race
						RaceTemplate raceTemplate = RaceTemplate.Get<RaceTemplate>(msg.RaceTemplateID);
						if (raceTemplate == null ||
							raceTemplate.Prefab == null)
						{
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
							conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
							return;
						}

						// Validate spawnable prefab
						IPlayerCharacter characterPrefab = raceTemplate.Prefab.GetComponent<IPlayerCharacter>();
						if (characterPrefab == null ||
							Server.NetworkManager.SpawnablePrefabs.GetObject(true, characterPrefab.NetworkObject.PrefabId) == null)
						{
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

						// Add character attributes
						if (raceTemplate.InitialAttributes != null)
						{
							foreach (CharacterAttributeTemplate template in raceTemplate.InitialAttributes.Attributes.Values)
							{
								dbContext.CharacterAttributes.Add(new CharacterAttributeEntity()
								{
									CharacterID = newCharacter.ID,
									TemplateID = template.ID,
									Value = template.InitialValue,
									CurrentValue = template.IsResourceAttribute ? template.InitialValue : 0.0f,
								});
							}
							dbContext.SaveChanges();
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
						if (StartingAbilities != null)
						{
							foreach (AbilityTemplate startingAbility in StartingAbilities)
							{
								var dbAbility = new CharacterAbilityEntity()
								{
									CharacterID = newCharacter.ID,
									TemplateID = startingAbility.ID,
									AbilityEvents = startingAbility.Events.Select(a => a.ID).ToList(),
								};
								dbContext.CharacterAbilities.Add(dbAbility);
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
				}
			}
		}
	}
}