using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Object;
using SceneManager = FishNet.Managing.Scened.SceneManager;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Npgsql.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Server
{
	// Character manager handles the players character
	public class CharacterSystem : ServerBehaviour
	{
		private SceneServerAuthenticator loginAuthenticator;
		private LocalConnectionState serverState;

		public float SaveRate = 60.0f;
		private float nextSave = 0.0f;

		public float OutOfBoundsCheckRate = 2.5f;
		private float nextOutOfBoundsCheck = 0.0f;

		/// <summary>
		/// Triggered before a character is loaded from the database. <conn, CharacterID>
		/// </summary>
		public event Action<NetworkConnection, long> OnBeforeLoadCharacter;
		/// <summary>
		/// Triggered after a character is loaded from the database. <conn, Character>
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnAfterLoadCharacter;
		/// <summary>
		/// Triggered immediately after a character is added to their respective cache.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnConnect;
		/// <summary>
		/// Triggered immediately after a character is removed from their respective cache.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnDisconnect;
		/// <summary>
		/// Triggered immediately after a character is spawned in the scene.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter, Scene> OnSpawnCharacter;
		/// <summary>
		/// Triggered immediately after a character is despawned from the scene.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnDespawnCharacter;
		/// <summary>
		/// Triggered immediately after a pet is killed.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnPetKilled;

		public Dictionary<long, IPlayerCharacter> CharactersByID = new Dictionary<long, IPlayerCharacter>();
		public Dictionary<string, IPlayerCharacter> CharactersByLowerCaseName = new Dictionary<string, IPlayerCharacter>();
		public Dictionary<long, Dictionary<long, IPlayerCharacter>> CharactersByWorld = new Dictionary<long, Dictionary<long, IPlayerCharacter>>();
		public Dictionary<NetworkConnection, IPlayerCharacter> ConnectionCharacters = new Dictionary<NetworkConnection, IPlayerCharacter>();
		public Dictionary<NetworkConnection, IPlayerCharacter> WaitingSceneLoadCharacters = new Dictionary<NetworkConnection, IPlayerCharacter>();

		public override void InitializeOnce()
		{
			nextSave = SaveRate;

			if (ServerManager != null &&
				ServerBehaviour.TryGet(out SceneServerSystem sceneServerSystem))
			{
				loginAuthenticator = FindFirstObjectByType<SceneServerAuthenticator>();
				if (loginAuthenticator == null)
					throw new UnityException("SceneServerAuthenticator not found!");

				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
				ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

				loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;

				Server.RegisterBroadcast<ClientValidatedSceneBroadcast>(OnClientValidatedSceneBroadcastReceived, true);
				Server.RegisterBroadcast<ClientScenesUnloadedBroadcast>(OnClientScenesUnloadedBroadcastReceived, true);
				Server.NetworkManager.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;

				IPlayerCharacter.OnTeleport += IPlayerCharacter_OnTeleport;
				ICharacterDamageController.OnKilled += CharacterDamageController_OnKilled;
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			if (ServerManager != null)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;

				Server.UnregisterBroadcast<ClientValidatedSceneBroadcast>(OnClientValidatedSceneBroadcastReceived, true);
				Server.UnregisterBroadcast<ClientScenesUnloadedBroadcast>(OnClientScenesUnloadedBroadcastReceived, true);
				Server.NetworkManager.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;

				IPlayerCharacter.OnTeleport -= IPlayerCharacter_OnTeleport;
				ICharacterDamageController.OnKilled -= CharacterDamageController_OnKilled;
			}

			if (Server != null &&
				Server.NpgsqlDbContextFactory != null)
			{
				// save all the characters before we quit
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				CharacterService.Save(dbContext, new List<IPlayerCharacter>(CharactersByID.Values), false);
			}
		}

		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started &&
				ServerBehaviour.TryGet(out SceneServerSystem sceneServerSystem))
			{
				if (nextOutOfBoundsCheck < 0)
				{
					nextOutOfBoundsCheck = OutOfBoundsCheckRate;

					if (sceneServerSystem.WorldSceneDetailsCache != null &&
						ConnectionCharacters != null)
					{
						// TODO: Should the character be doing this and more often?
						// They'd need a cached world boundaries to check themselves against
						// which would prevent the need to do all of this lookup stuff.
						foreach (IPlayerCharacter character in ConnectionCharacters.Values)
						{
							if (character == null ||
								string.IsNullOrWhiteSpace(character.SceneName))
							{
								continue;
							}

							if (sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out WorldSceneDetails details))
							{
								// Check if they are within some bounds, if not we need to move them to a respawn location!
								// TODO: Try to prevent combat escape, maybe this needs to be handled on the game design level?
								if (!details.Boundaries.PointContainedInBoundaries(character.Transform.position))
								{
									CharacterRespawnPositionDetails spawnPoint = details.RespawnPositions.Values.ToList().GetRandom();
									if (spawnPoint == null ||
										character == null ||
										character.Motor == null)
									{
										continue;
									}

									Debug.Log($"{character.CharacterName} is out of bounds.");

									character.Motor.SetPositionAndRotationAndVelocity(spawnPoint.Position, spawnPoint.Rotation, Vector3.zero);
								}
							}
						}
					}
				}
				nextOutOfBoundsCheck -= Time.deltaTime;

				if (nextSave < 0)
				{
					nextSave = SaveRate;

					if (CharactersByID.Count > 0)
					{
						Debug.Log("Character System: Save" + "[" + DateTime.UtcNow + "]");

						// all characters are periodically saved
						using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
						CharacterService.Save(dbContext, new List<IPlayerCharacter>(CharactersByID.Values));
					}
				}
				nextSave -= Time.deltaTime;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
		}

		/// <summary>
		/// When a connection disconnects the server removes all known instances of the character and saves it to the database.
		/// </summary>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped &&
				ServerBehaviour.TryGet(out SceneServerSystem sceneServerSystem))
			{
				RemoveCharacterConnectionMapping(conn);
			}
		}

		/// <summary>
		/// Removes the character connection mapping and saves the character state to the database.
		/// </summary>
		private void RemoveCharacterConnectionMapping(NetworkConnection conn, bool skipOnDisconnect = false)
		{
			// Remove the waiting scene load character if it exists, these characters exist but are not spawned
			if (WaitingSceneLoadCharacters.TryGetValue(conn, out IPlayerCharacter waitingSceneCharacter))
			{
				WaitingSceneLoadCharacters.Remove(conn);

				OnDisconnect?.Invoke(conn, waitingSceneCharacter);

				Server.NetworkManager.StorePooledInstantiated(waitingSceneCharacter.NetworkObject, true);
			}

			if (!ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				return;
			}

			// Remove the connection->character entry
			ConnectionCharacters.Remove(conn);

			// Remove the characterID->character entry
			CharactersByID.Remove(character.ID);
			// Remove the characterName->character entry
			CharactersByLowerCaseName.Remove(character.CharacterNameLower);
			// Remove the worldid<characterID->character> entry
			if (CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, IPlayerCharacter> characters))
			{
				characters.Remove(character.ID);
			}

			if (!skipOnDisconnect)
			{
				OnDisconnect?.Invoke(conn, character);
			}

			SaveAndDespawnCharacter(conn, character);
		}

		private void SaveAndDespawnCharacter(NetworkConnection conn, IPlayerCharacter character)
		{
			// Save the character and set online status to false
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			CharacterService.Save(dbContext, character, false);

			// Immediately log out for now.. we could add a timeout later on..?
			if (character.NetworkObject.IsSpawned)
			{
				OnDespawnCharacter?.Invoke(conn, character);

				ServerManager.Despawn(character.NetworkObject, DespawnType.Pool);
			}
		}

		private void Authenticator_OnClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			// Is the character already loading?
			if (WaitingSceneLoadCharacters.ContainsKey(conn))
			{
				return;
			}
			if (!authenticated ||
				!AccountManager.GetAccountNameByConnection(conn, out string accountName) ||
				!ServerBehaviour.TryGet(out SceneServerSystem sceneServerSystem))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}
			// Create the db context
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

			if (CharacterService.TryGetSelectedCharacterID(dbContext, accountName, out long selectedCharacterID))
			{
				if (CharactersByID.ContainsKey(selectedCharacterID))
				{
					Debug.Log(selectedCharacterID + " is already loaded or loading.");

					// Character load already started or complete
					conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
					return;
				}

				OnBeforeLoadCharacter?.Invoke(conn, selectedCharacterID);
				if (CharacterService.TryGet(dbContext, selectedCharacterID, Server.NetworkManager, out IPlayerCharacter character))
				{
					string sceneName = character.SceneName;
					int sceneHandle = character.SceneHandle;

					// Check if the character is in an instance or not.
					if (character.Flags.IsFlagged(CharacterFlags.IsInInstance))
					{
						SceneEntity sceneEntity = SceneService.GetInstanceByID(dbContext, character.InstanceID);
						if (sceneEntity != null)
						{
							// Have the player enter the instance.
							sceneName = sceneEntity.SceneName;
							sceneHandle = sceneEntity.SceneHandle;

							// Cache the Instance Scene Name and Instance Scene Handle
							character.InstanceSceneName = sceneName;
							character.InstanceSceneHandle = sceneHandle;
						}
					}

					//Debug.Log($"Character loaded into {sceneName}:{sceneHandle}.");
					
					// Check if the scene is valid, loaded, and cached properly
					if (sceneServerSystem.TryGetSceneInstanceDetails(character.WorldServerID, sceneName, sceneHandle, out SceneInstanceDetails instance) &&
						sceneServerSystem.TryLoadSceneForConnection(conn, instance))
					{
						OnAfterLoadCharacter?.Invoke(conn, character);

						WaitingSceneLoadCharacters.Add(conn, character);

						//Debug.Log($"Character System: {character.CharacterName} is loading Scene: {sceneName}:{sceneHandle}");
					}
					else
					{
						Debug.Log($"Character System: Failed to load scene for connection.");

						// Send the character back to the world server.. something went wrong
						conn.Disconnect(false);
					}
				}
				else
				{
					Debug.Log($"Character System: Failed to fetch character.");

					// Loading the character failed for some reason, maybe it doesn't exist? we should never get to this point but we will kick the player anyway
					conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				}
			}
			else
			{
				Debug.Log($"Character System: Failed to fetch character ID.");

				// Loading the character data failed to load for some reason, maybe it doesn't exist? we should never get to this point but we will kick the player anyway
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
			}
		}

		/// <summary>
		/// Called when a client loads world scenes after connecting. The character is validated and the client is notified.
		/// </summary>
		private void SceneManager_OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
		{
			// Validate the connection has a character ready to play.
			if (!WaitingSceneLoadCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				return;
			}

			// Get the characters scene.
			Scene scene = SceneManager.GetScene(character.SceneHandle);

			// Validate the characters scene.
			if (scene == null ||
				!scene.IsValid() ||
				!scene.isLoaded)
			{
				Debug.Log("Scene is not valid.");

				WaitingSceneLoadCharacters.Remove(conn);

				Destroy(character.GameObject);
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				return;
			}

			Server.Broadcast(conn, new ClientValidatedSceneBroadcast(), true, Channel.Reliable);
		}

		/// <summary>
		/// Called when a client completely finishes loading into a world scene.
		/// </summary>
		private void OnClientValidatedSceneBroadcastReceived(NetworkConnection conn, ClientValidatedSceneBroadcast msg, Channel channel)
		{
			if (WaitingSceneLoadCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				if (character == null)
				{
					conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
					return;
				}

				// remove the waiting scene load character
				WaitingSceneLoadCharacters.Remove(conn);

				// add a connection->character map for ease of use
				ConnectionCharacters[conn] = character;
				// add a characterName->character map for ease of use
				CharactersByID[character.ID] = character;
				CharactersByLowerCaseName[character.CharacterNameLower] = character;
				// add a worldID<characterID->character> map for ease of use
				if (!CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, IPlayerCharacter> characters))
				{
					CharactersByWorld.Add(character.WorldServerID, characters = new Dictionary<long, IPlayerCharacter>());
				}
				characters[character.ID] = character;

				// get the characters scene
				Scene scene = SceneManager.GetScene(character.SceneHandle);

				// validate the scene
				if (scene == null ||
					!scene.IsValid() ||
					!scene.isLoaded)
				{
					Debug.Log("Scene is not valid.");
					Destroy(character.GameObject);
					conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
					return;
				}

				// set the proper physics scene for the character, scene stacking requires separated physics
				character.Motor?.SetPhysicsScene(scene.GetPhysicsScene());

				// character becomes mortal when loaded into the scene
				if (character.TryGet(out ICharacterDamageController damageController))
				{
					damageController.Immortal = false;
				}

				// ensure the game object is active, pooled objects are disabled
				character.GameObject.SetActive(true);

				// spawn the nob over the network
				ServerManager.Spawn(character.NetworkObject, conn, scene);

				OnSpawnCharacter?.Invoke(conn, character, scene);

				// set the character status to online
				if (AccountManager.GetAccountNameByConnection(conn, out string accountName))
				{
					using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
					CharacterService.SetOnline(dbContext, accountName, character.CharacterName);
				}

				OnConnect?.Invoke(conn, character);

				// Send server character local observer data to the client.
				SendAllCharacterData(character);

				//Debug.Log(character.CharacterName + " has been spawned at: " + character.SceneName + " " + character.Transform.position.ToString());
			}
		}

		/// <summary>
		/// The client notified the server it unloaded scenes.
		/// </summary>
		private void OnClientScenesUnloadedBroadcastReceived(NetworkConnection conn, ClientScenesUnloadedBroadcast msg, Channel channel)
		{
			if (msg.UnloadedScenes == null || msg.UnloadedScenes.Count == 0)
			{
				Debug.Log("No unloaded scenes received.");
				return;
			}

			// Check if the connection has a character loaded.
			if (ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				Debug.Log("Character is still loaded for connection.");
				return;
			}

			//Debug.Log($"Connection unloaded scene: {msg.UnloadedScenes[0].Name}|{msg.UnloadedScenes[0].Handle}");

			// Otherwise disconnect the connection.
			conn.Disconnect(false);
		}

		/// <summary>
		/// Sends all Server Side Character data to the owner. *Expensive*
		/// </summary>
		/// <param name="character"></param>
		public void SendAllCharacterData(IPlayerCharacter character)
		{
			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

			if (character == null)
			{
				return;
			}

			#region Abilities
			if (character.TryGet(out IAbilityController abilityController))
			{
				List<KnownAbilityAddBroadcast> knownAbilityBroadcasts = new List<KnownAbilityAddBroadcast>();

				if (abilityController.KnownBaseAbilities != null)
				{
					// get base ability templates
					foreach (int templateID in abilityController.KnownBaseAbilities)
					{
						// create the new item broadcast
						knownAbilityBroadcasts.Add(new KnownAbilityAddBroadcast()
						{
							TemplateID = templateID,
						});
					}
				}

				if (abilityController.KnownEvents != null)
				{
					// and event templates
					foreach (int templateID in abilityController.KnownEvents)
					{
						// create the new item broadcast
						knownAbilityBroadcasts.Add(new KnownAbilityAddBroadcast()
						{
							TemplateID = templateID,
						});
					}
				}

				// tell the client they have known abilities
				if (knownAbilityBroadcasts.Count > 0)
				{
					Server.Broadcast(character.Owner, new KnownAbilityAddMultipleBroadcast()
					{
						Abilities = knownAbilityBroadcasts,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region Achievements
			if (character.TryGet(out IAchievementController achievementController))
			{
				List<AchievementUpdateBroadcast> achievements = new List<AchievementUpdateBroadcast>();
				foreach (Achievement achievement in achievementController.Achievements.Values)
				{
					achievements.Add(new AchievementUpdateBroadcast()
					{
						TemplateID = achievement.Template.ID,
						Value = achievement.CurrentValue,
						Tier = achievement.CurrentTier,
					});
				}
				if (achievements.Count > 0)
				{
					Server.Broadcast(character.Owner, new AchievementUpdateMultipleBroadcast()
					{
						Achievements = achievements,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region Guild
			if (character.TryGet(out IGuildController guildController) &&
				guildController.ID > 0)
			{
				// get the current guild members from the database
				List<CharacterGuildEntity> dbMembers = CharacterGuildService.Members(dbContext, guildController.ID);

				var addBroadcasts = dbMembers.Select(x => new GuildAddBroadcast()
				{
					GuildID = x.GuildID,
					CharacterID = x.CharacterID,
					Rank = (GuildRank)x.Rank,
					Location = x.Location,
				}).ToList();

				if (addBroadcasts.Count > 0)
				{
					GuildAddMultipleBroadcast guildAddBroadcast = new GuildAddMultipleBroadcast()
					{
						Members = addBroadcasts,
					};
					Server.Broadcast(character.Owner, guildAddBroadcast, true, Channel.Reliable);
				}
			}
			#endregion

			#region Party
			if (character.TryGet(out IPartyController partyController) &&
				partyController.ID > 0)
			{
				// get the current party members from the database
				List<CharacterPartyEntity> dbMembers = CharacterPartyService.Members(dbContext, partyController.ID);

				var addBroadcasts = dbMembers.Select(x => new PartyAddBroadcast()
				{
					PartyID = x.PartyID,
					CharacterID = x.CharacterID,
					Rank = (PartyRank)x.Rank,
					HealthPCT = x.HealthPCT,
				}).ToList();

				if (addBroadcasts.Count > 0)
				{
					PartyAddMultipleBroadcast partyAddBroadcast = new PartyAddMultipleBroadcast()
					{
						Members = addBroadcasts,
					};
					Server.Broadcast(character.Owner, partyAddBroadcast, true, Channel.Reliable);
				}
			}
			#endregion

			#region Friends
			if (character.TryGet(out IFriendController friendController))
			{
				List<FriendAddBroadcast> friends = new List<FriendAddBroadcast>();
				foreach (long friendID in friendController.Friends)
				{
					bool status = CharacterService.ExistsAndOnline(dbContext, friendID);
					friends.Add(new FriendAddBroadcast()
					{
						CharacterID = friendID,
						Online = status,
					});
				}
				if (friends.Count > 0)
				{
					Server.Broadcast(character.Owner, new FriendAddMultipleBroadcast()
					{
						Friends = friends,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region InventoryItems
			if (character.TryGet(out IInventoryController inventoryController))
			{
				List<InventorySetItemBroadcast> itemBroadcasts = new List<InventorySetItemBroadcast>();

				foreach (Item item in inventoryController.Items)
				{
					// just in case..
					if (item == null)
					{
						continue;
					}
					// create the new item broadcast
					itemBroadcasts.Add(new InventorySetItemBroadcast()
					{
						InstanceID = item.ID,
						TemplateID = item.Template.ID,
						Slot = item.Slot,
						Seed = item.IsGenerated ? item.Generator.Seed : 0,
						StackSize = item.IsStackable ? item.Stackable.Amount : 0,
					});
				}

				// tell the client they have items
				if (itemBroadcasts.Count > 0)
				{
					Server.Broadcast(character.Owner, new InventorySetMultipleItemsBroadcast()
					{
						Items = itemBroadcasts,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region BankItems
			if (character.TryGet(out IBankController bankController))
			{
				List<BankSetItemBroadcast> itemBroadcasts = new List<BankSetItemBroadcast>();

				foreach (Item item in bankController.Items)
				{
					// just in case..
					if (item == null)
					{
						continue;
					}
					// create the new item broadcast
					itemBroadcasts.Add(new BankSetItemBroadcast()
					{
						InstanceID = item.ID,
						TemplateID = item.Template.ID,
						Slot = item.Slot,
						Seed = item.IsGenerated ? item.Generator.Seed : 0,
						StackSize = item.IsStackable ? item.Stackable.Amount : 0,
					});
				}

				// tell the client they have items
				if (itemBroadcasts.Count > 0)
				{
					Server.Broadcast(character.Owner, new BankSetMultipleItemsBroadcast()
					{
						Items = itemBroadcasts,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region Hotkeys
			if (character.Hotkeys != null)
			{
				List<HotkeySetBroadcast> hotkeyBroadcasts = new List<HotkeySetBroadcast>();

				foreach (HotkeyData hotkey in character.Hotkeys)
				{
					// just in case..
					if (hotkey == null)
					{
						continue;
					}
					// create the new hotkey broadcast
					hotkeyBroadcasts.Add(new HotkeySetBroadcast()
					{
						HotkeyData = new HotkeyData()
						{
							Type = hotkey.Type,
							Slot = hotkey.Slot,
							ReferenceID = hotkey.ReferenceID,
						}
					});
				}

				// tell the client they have hotkeys
				if (hotkeyBroadcasts.Count > 0)
				{
					Server.Broadcast(character.Owner, new HotkeySetMultipleBroadcast()
					{
						Hotkeys = hotkeyBroadcasts,
					}, true, Channel.Reliable);
				}
			}
			#endregion
		}

		/// <summary>
		/// Allows sending a broadcast to a specific character by their character name.
		/// Returns true if the broadcast was sent successfully.
		/// False if the character could not by found.
		/// </summary>
		public bool SendBroadcastToCharacter<T>(string characterName, T msg) where T : struct, IBroadcast
		{
			if (CharactersByLowerCaseName.TryGetValue(characterName.ToLower(), out IPlayerCharacter character))
			{
				Server.Broadcast(character.Owner, msg, true, Channel.Reliable);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Allows sending a broadcast to a specific character by their character id.
		/// Returns true if the broadcast was sent successfully.
		/// False if the character could not by found.
		/// </summary>
		public bool SendBroadcastToCharacter<T>(long characterID, T msg) where T : struct, IBroadcast
		{
			if (CharactersByID.TryGetValue(characterID, out IPlayerCharacter character))
			{
				Server.Broadcast(character.Owner, msg, true, Channel.Reliable);
				return true;
			}
			return false;
		}

		public void IPlayerCharacter_OnTeleport(IPlayerCharacter character)
		{
			if (character == null)
			{
				Debug.Log("Character doesn't exist..");
				return;
			}

			if (!character.IsTeleporting)
			{
				Debug.Log("Character is not teleporting..");
				return;
			}

			// Should we prevent players from moving to a different scene if they are in combat?
			/*if (character.TryGet(out CharacterDamageController damageController) &&
				  damageController.Attackers.Count > 0)
			{
				return;
			}*/

			if (!ServerBehaviour.TryGet(out SceneServerSystem sceneServerSystem))
			{
				Debug.Log("SceneServerSystem not found!");
				return;
			}

			// Cache the current scene name
			string currentScene = character.SceneName;

			if (sceneServerSystem.WorldSceneDetailsCache == null ||
				!sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(currentScene, out WorldSceneDetails details))
			{
				Debug.Log(currentScene + " not found!");
				return;
			}

			// Check if teleporter is a valid scene teleporter
			if (details.Teleporters.TryGetValue(character.TeleporterName, out SceneTeleporterDetails teleporter))
			{
				//Debug.Log($"Teleporter: {character.TeleporterName} found! Teleporting {character.CharacterName} to {teleporter.ToScene}.");

				//Debug.Log($"Unloading scene for {character.CharacterName}: {character.SceneName}|{character.SceneHandle}");

				// Tell the connection to unload their current world scene.
				// This is no longer used as we automatically unload all previous world scenes on the Client.
				//sceneServerSystem.UnloadSceneForConnection(character.Owner, character.SceneName);

				// Character becomes immortal when teleporting
				if (character.TryGet(out ICharacterDamageController damageController))
				{
					//Debug.Log($"{character.CharacterName} is now immortal.");
					damageController.Immortal = true;
				}

				// Invoke disconnect early when teleporting because we require the scene the character is in.
				OnDisconnect?.Invoke(character.Owner, character);

				character.SceneName = teleporter.ToScene;
				character.Motor.SetPositionAndRotationAndVelocity(teleporter.ToPosition, teleporter.ToRotation, Vector3.zero);

				// Remove the character from an instance if it was in one.
				character.DisableFlags(CharacterFlags.IsInInstance);

				// Save the character and remove it from the scene
				RemoveCharacterConnectionMapping(character.Owner, true);
			}
		}

		private void CharacterDamageController_OnKilled(ICharacter killer, ICharacter defender)
		{
			if (defender == null)
			{
				return;
			}

			if (defender.TryGet(out IBuffController buffController))
			{
				buffController.RemoveAll(true);
			}

			// Handle Player deaths
			IPlayerCharacter playerCharacter = defender as IPlayerCharacter;
			if (playerCharacter != null)
			{
				//Debug.Log($"PlayerCharacter: {playerCharacter.GameObject.name} Died");

				if (playerCharacter.TryGet(out ICharacterDamageController damageController))
				{
					// Full heal the character
					damageController.Heal(null, 999999, true);
				}

				if (playerCharacter.SceneName != playerCharacter.BindScene)
				{
					playerCharacter.SceneName = playerCharacter.BindScene;
					playerCharacter.Motor.SetPositionAndRotationAndVelocity(playerCharacter.BindPosition, playerCharacter.Motor.Transform.rotation, Vector3.zero);
					playerCharacter.NetworkObject.Owner.Disconnect(false);

					// Remove the character from the instance if it dies.
					playerCharacter.DisableFlags(CharacterFlags.IsInInstance);
				}
				else
				{
					playerCharacter.Motor.SetPositionAndRotationAndVelocity(playerCharacter.BindPosition, Quaternion.identity, Vector3.zero);
				}
			}
			else
			{
				// Handle NPC deaths
				NPC npc = defender as NPC;
				if (npc != null)
				{
					Pet pet = defender as Pet;
					if (pet != null)
					{
						//Debug.Log($"Pet: {pet.GameObject.name} Died");

						IPlayerCharacter petOwner = pet.PetOwner as IPlayerCharacter;
						OnPetKilled?.Invoke(petOwner.NetworkObject.Owner, petOwner);
					}
					else
					{
						//Debug.Log($"NPC: {npc.GameObject.name} Died");
						npc.Despawn();
					}
				}
			}
		}
	}
}