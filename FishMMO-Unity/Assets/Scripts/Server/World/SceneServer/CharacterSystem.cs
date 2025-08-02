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
using FishMMO.Logging;
using FishMMO.Database.Npgsql.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Server
{
	// Character manager handles the players character
	public class CharacterSystem : ServerBehaviour
	{
		/// <summary>
		/// Authenticator for login and character loading.
		/// </summary>
		private SceneServerAuthenticator loginAuthenticator;
		/// <summary>
		/// Current connection state of the server.
		/// </summary>
		private LocalConnectionState serverState;

		/// <summary>
		/// Interval in seconds between periodic character saves.
		/// </summary>
		public float SaveRate = 60.0f;
		/// <summary>
		/// Time remaining until the next character save.
		/// </summary>
		private float nextSave = 0.0f;

		/// <summary>
		/// Interval in seconds between out-of-bounds checks for characters.
		/// </summary>
		public float OutOfBoundsCheckRate = 2.5f;
		/// <summary>
		/// Time remaining until the next out-of-bounds check.
		/// </summary>
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

		/// <summary>
		/// Maps character IDs to player character instances.
		/// </summary>
		public Dictionary<long, IPlayerCharacter> CharactersByID = new Dictionary<long, IPlayerCharacter>();
		/// <summary>
		/// Maps lowercase character names to player character instances.
		/// </summary>
		public Dictionary<string, IPlayerCharacter> CharactersByLowerCaseName = new Dictionary<string, IPlayerCharacter>();
		/// <summary>
		/// Maps world server IDs to dictionaries of character IDs and player character instances.
		/// </summary>
		public Dictionary<long, Dictionary<long, IPlayerCharacter>> CharactersByWorld = new Dictionary<long, Dictionary<long, IPlayerCharacter>>();
		/// <summary>
		/// Maps network connections to player character instances.
		/// </summary>
		public Dictionary<NetworkConnection, IPlayerCharacter> ConnectionCharacters = new Dictionary<NetworkConnection, IPlayerCharacter>();
		/// <summary>
		/// Maps network connections to player characters waiting for scene load.
		/// </summary>
		public Dictionary<NetworkConnection, IPlayerCharacter> WaitingSceneLoadCharacters = new Dictionary<NetworkConnection, IPlayerCharacter>();

		/// <summary>
		/// Initializes the character system, registers event handlers, and sets up character authentication and broadcast handling.
		/// </summary>
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

		/// <summary>
		/// Cleans up the character system, unregisters event handlers, and saves all characters to the database before shutdown.
		/// </summary>
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

		/// <summary>
		/// Unity LateUpdate callback. Periodically checks for out-of-bounds characters and saves character data.
		/// </summary>
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
							if (character == null || string.IsNullOrWhiteSpace(character.SceneName))
							{
								continue;
							}

							var sceneName = string.IsNullOrWhiteSpace(character.InstanceSceneName)
										? character.SceneName
										: character.InstanceSceneName;

							if (sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(sceneName, out WorldSceneDetails details))
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

									Log.Debug("CharacterSystem", $"{character.CharacterName} is out of bounds.");

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
						Log.Debug("CharacterSystem", "Save" + "[" + DateTime.UtcNow + "]");

						// all characters are periodically saved
						using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
						CharacterService.Save(dbContext, new List<IPlayerCharacter>(CharactersByID.Values));
					}
				}
				nextSave -= Time.deltaTime;
			}
		}

		/// <summary>
		/// Handles changes in the server's connection state.
		/// </summary>
		/// <param name="args">Arguments containing the new connection state.</param>
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
		/// <param name="conn">Network connection to remove.</param>
		/// <param name="skipOnDisconnect">If true, skips OnDisconnect event invocation.</param>
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

		/// <summary>
		/// Saves the character state and despawns the character from the scene.
		/// </summary>
		/// <param name="conn">Network connection of the character.</param>
		/// <param name="character">Player character to save and despawn.</param>
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

		/// <summary>
		/// Handles client authentication results, loads character data and initiates scene loading.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="authenticated">True if authentication succeeded.</param>
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
					Log.Debug("CharacterSystem", $"{selectedCharacterID} is already loaded or loading.");

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
					if (character.IsInInstance())
					{
						// Have the player enter the instance.
						sceneName = character.InstanceSceneName;
						sceneHandle = character.InstanceSceneHandle;
					}

					//Log.Debug("CharacterSystem", "$"Character loaded into {sceneName}:{sceneHandle}.");

					// Check if the scene is valid, loaded, and cached properly
					if (sceneServerSystem.TryGetSceneInstanceDetails(character.WorldServerID, sceneName, sceneHandle, out SceneInstanceDetails instance) &&
						sceneServerSystem.TryLoadSceneForConnection(conn, instance))
					{
						OnAfterLoadCharacter?.Invoke(conn, character);

						WaitingSceneLoadCharacters.Add(conn, character);

						//Log.Debug("CharacterSystem", $"{character.CharacterName} is loading Scene: {sceneName}:{sceneHandle}");
					}
					else
					{
						Log.Debug("CharacterSystem", "Failed to load scene for connection.");

						// Send the character back to the world server.. something went wrong
						conn.Disconnect(false);
					}
				}
				else
				{
					Log.Debug("CharacterSystem", "Failed to fetch character.");

					// Loading the character failed for some reason, maybe it doesn't exist? we should never get to this point but we will kick the player anyway
					conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				}
			}
			else
			{
				Log.Debug("CharacterSystem", "Failed to fetch character ID.");

				// Loading the character data failed to load for some reason, maybe it doesn't exist? we should never get to this point but we will kick the player anyway
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
			}
		}

		/// <summary>
		/// Called when a client loads world scenes after connecting. Validates character and scene, then notifies client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="asServer">True if loaded as server.</param>
		private void SceneManager_OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
		{
			// Validate the connection has a character ready to play.
			if (!WaitingSceneLoadCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				return;
			}

			// Get the characters scene
			Scene scene;
			if (character.IsInInstance())
			{
				scene = SceneManager.GetScene(character.InstanceSceneHandle);
			}
			else
			{
				scene = SceneManager.GetScene(character.SceneHandle);
			}

			// Validate the characters scene.
			if (scene == null ||
				!scene.IsValid() ||
				!scene.isLoaded)
			{
				Log.Debug("CharacterSystem", "Scene is not valid.");

				WaitingSceneLoadCharacters.Remove(conn);

				Destroy(character.GameObject);
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				return;
			}

			Server.Broadcast(conn, new ClientValidatedSceneBroadcast(), true, Channel.Reliable);
		}

		/// <summary>
		/// Called when a client completely finishes loading into a world scene. Spawns character and sets online status.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">ClientValidatedSceneBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnClientValidatedSceneBroadcastReceived(NetworkConnection conn, ClientValidatedSceneBroadcast msg, Channel channel)
		{
			if (WaitingSceneLoadCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				if (character == null)
				{
					conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
					return;
				}

				// Remove the waiting scene load character
				WaitingSceneLoadCharacters.Remove(conn);

				// Add a connection->character map for ease of use
				ConnectionCharacters[conn] = character;
				// Add a characterName->character map for ease of use
				CharactersByID[character.ID] = character;
				CharactersByLowerCaseName[character.CharacterNameLower] = character;
				// Add a worldID<characterID->character> map for ease of use
				if (!CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, IPlayerCharacter> characters))
				{
					CharactersByWorld.Add(character.WorldServerID, characters = new Dictionary<long, IPlayerCharacter>());
				}
				characters[character.ID] = character;

				// Get the characters scene
				Scene scene;
				if (character.IsInInstance())
				{
					scene = SceneManager.GetScene(character.InstanceSceneHandle);
				}
				else
				{
					scene = SceneManager.GetScene(character.SceneHandle);
				}

				// Validate the scene
				if (scene == null ||
					!scene.IsValid() ||
					!scene.isLoaded)
				{
					Log.Debug("CharacterSystem", "Scene is not valid.");
					Destroy(character.GameObject);
					conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
					return;
				}

				// Set the proper physics scene for the character, scene stacking requires separated physics
				character.Motor?.SetPhysicsScene(scene.GetPhysicsScene());

				// Character becomes mortal when loaded into the scene
				if (character.TryGet(out ICharacterDamageController damageController))
				{
					damageController.Immortal = false;
				}

				// Ensure the game object is active, pooled objects are disabled
				character.GameObject.SetActive(true);

				// Spawn the nob over the network
				ServerManager.Spawn(character.NetworkObject, conn, scene);

				OnSpawnCharacter?.Invoke(conn, character, scene);

				// Set the character status to online
				if (AccountManager.GetAccountNameByConnection(conn, out string accountName))
				{
					using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
					CharacterService.SetOnline(dbContext, accountName, character.CharacterName);
				}

				OnConnect?.Invoke(conn, character);

				// Send server character local observer data to the client.
				SendAllCharacterData(character);

				//Log.Debug("CharacterSystem", character.CharacterName + " has been spawned at: " + character.SceneName + " " + character.Transform.position.ToString());
			}
		}

		/// <summary>
		/// The client notified the server it unloaded scenes. Disconnects connection if character is not loaded.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">ClientScenesUnloadedBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnClientScenesUnloadedBroadcastReceived(NetworkConnection conn, ClientScenesUnloadedBroadcast msg, Channel channel)
		{
			if (msg.UnloadedScenes == null || msg.UnloadedScenes.Count == 0)
			{
				Log.Debug("CharacterSystem", "No unloaded scenes received.");
				return;
			}

			// Check if the connection has a character loaded.
			if (ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				Log.Debug("CharacterSystem", "Character is still loaded for connection.");
				return;
			}

			//Log.Debug($"Connection unloaded scene: {msg.UnloadedScenes[0].Name}|{msg.UnloadedScenes[0].Handle}");

			// Otherwise disconnect the connection.
			conn.Disconnect(false);
		}

		/// <summary>
		/// Sends all server-side character data to the owner. Expensive operation.
		/// </summary>
		/// <param name="character">Player character to send data for.</param>
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
				List<KnownAbilityEventAddBroadcast> knownAbilityEventBroadcasts = new List<KnownAbilityEventAddBroadcast>();

				if (abilityController.KnownBaseAbilities != null)
				{
					// get base ability templates
					foreach (int templateID in abilityController.KnownBaseAbilities)
					{
						knownAbilityBroadcasts.Add(new KnownAbilityAddBroadcast()
						{
							TemplateID = templateID,
						});
					}
				}

				if (abilityController.KnownAbilityEvents != null)
				{
					// and event templates
					foreach (int templateID in abilityController.KnownAbilityEvents)
					{
						knownAbilityEventBroadcasts.Add(new KnownAbilityEventAddBroadcast()
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

					Server.Broadcast(character.Owner, new KnownAbilityEventAddMultipleBroadcast()
					{
						AbilityEvents = knownAbilityEventBroadcasts,
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
		/// Returns true if the broadcast was sent successfully, false otherwise.
		/// </summary>
		/// <typeparam name="T">Type of broadcast message.</typeparam>
		/// <param name="characterName">Name of the character to send to.</param>
		/// <param name="msg">Broadcast message to send.</param>
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
		/// Allows sending a broadcast to a specific character by their character ID.
		/// Returns true if the broadcast was sent successfully, false otherwise.
		/// </summary>
		/// <typeparam name="T">Type of broadcast message.</typeparam>
		/// <param name="characterID">ID of the character to send to.</param>
		/// <param name="msg">Broadcast message to send.</param>
		public bool SendBroadcastToCharacter<T>(long characterID, T msg) where T : struct, IBroadcast
		{
			if (CharactersByID.TryGetValue(characterID, out IPlayerCharacter character))
			{
				Server.Broadcast(character.Owner, msg, true, Channel.Reliable);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Handles character teleport events, validates teleporter and scene, updates position, and saves state.
		/// </summary>
		/// <param name="character">Player character to teleport.</param>
		public void IPlayerCharacter_OnTeleport(IPlayerCharacter character)
		{
			if (character == null)
			{
				Log.Debug("CharacterSystem", "Character doesn't exist..");
				return;
			}

			if (!character.IsTeleporting)
			{
				Log.Debug("CharacterSystem", "Character is not teleporting..");
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
				Log.Debug("CharacterSystem", "SceneServerSystem not found!");
				return;
			}

			// Cache the current scene name
			string currentScene = character.SceneName;

			if (sceneServerSystem.WorldSceneDetailsCache == null ||
				!sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(currentScene, out WorldSceneDetails details))
			{
				Log.Debug("CharacterSystem", currentScene + " not found!");
				return;
			}

			// Check if teleporter is a valid scene teleporter
			if (details.Teleporters.TryGetValue(character.TeleporterName, out SceneTeleporterDetails teleporter))
			{
				//Log.Debug("CharacterSystem", $"Teleporter: {character.TeleporterName} found! Teleporting {character.CharacterName} to {teleporter.ToScene}.");

				//Log.Debug("CharacterSystem", $"Unloading scene for {character.CharacterName}: {character.SceneName}|{character.SceneHandle}");

				// Tell the connection to unload their current world scene.
				sceneServerSystem.UnloadSceneForConnection(character.Owner, character.SceneName);

				// Character becomes immortal when teleporting
				if (character.TryGet(out ICharacterDamageController damageController))
				{
					//Log.Debug("CharacterSystem", $"{character.CharacterName} is now immortal.");
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
			else
			{
				Log.Debug("CharacterSystem", $"{character.TeleporterName} not found!");
			}
		}

		/// <summary>
		/// Handles character killed events, processes player and NPC deaths, respawns, and updates state.
		/// </summary>
		/// <param name="killer">Character that performed the kill.</param>
		/// <param name="defender">Character that was killed.</param>
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
				//Log.Debug("CharacterSystem", $"PlayerCharacter: {playerCharacter.GameObject.name} Died");

				if (playerCharacter.TryGet(out ICharacterDamageController damageController))
				{
					// Full heal the character
					damageController.Heal(null, 999999, true);
				}

				if (playerCharacter.IsInInstance() && playerCharacter.InstanceSceneName != playerCharacter.BindScene ||
					playerCharacter.SceneName != playerCharacter.BindScene)
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
						//Log.Debug("CharacterSystem", $"Pet: {pet.GameObject.name} Died");

						IPlayerCharacter petOwner = pet.PetOwner as IPlayerCharacter;
						OnPetKilled?.Invoke(petOwner.NetworkObject.Owner, petOwner);
					}
					else
					{
						//Log.Debug("CharacterSystem", $"NPC: {npc.GameObject.name} Died");
						npc.Despawn();
					}
				}
			}
		}
	}
}