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
		public event Action<NetworkConnection, Character> OnAfterLoadCharacter;
		/// <summary>
		/// Triggered immediately after a character is removed from their respective cache.
		/// </summary>
		public event Action<NetworkConnection, Character> OnDisconnect;

		public event Action<Character, string> OnCharacterChangedScene;

		public Dictionary<long, Character> CharactersByID = new Dictionary<long, Character>();
		public Dictionary<string, Character> CharactersByLowerCaseName = new Dictionary<string, Character>();
		public Dictionary<long, Dictionary<long, Character>> CharactersByWorld = new Dictionary<long, Dictionary<long, Character>>();
		public Dictionary<NetworkConnection, Character> ConnectionCharacters = new Dictionary<NetworkConnection, Character>();
		public Dictionary<NetworkConnection, Character> WaitingSceneLoadCharacters = new Dictionary<NetworkConnection, Character>();

		public override void InitializeOnce()
		{
			nextSave = SaveRate;

			if (ServerManager != null &&
				ServerBehaviour.TryGet(out SceneServerSystem sceneServerSystem))
			{
				loginAuthenticator = FindObjectOfType<SceneServerAuthenticator>();
				if (loginAuthenticator == null)
					return;

				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
				ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started &&
				ServerBehaviour.TryGet(out SceneServerSystem sceneServerSystem))
			{
				if(nextOutOfBoundsCheck < 0)
				{
					nextOutOfBoundsCheck = OutOfBoundsCheckRate;

					if (sceneServerSystem.WorldSceneDetailsCache != null &&
						ConnectionCharacters != null)
					{
						// TODO: Should the character be doing this and more often?
						// They'd need a cached world boundaries to check themselves against
						// which would prevent the need to do all of this lookup stuff.
						foreach (Character character in ConnectionCharacters.Values)
						{
							if (character == null ||
								string.IsNullOrWhiteSpace(character.SceneName.Value))
							{
								continue;
							}

							if (sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(character.SceneName.Value, out WorldSceneDetails details))
							{
								// Check if they are within some bounds, if not we need to move them to a respawn location!
								// TODO: Try to prevent combat escape, maybe this needs to be handled on the game design level?
								if (!details.Boundaries.PointContainedInBoundaries(character.Transform.position))
								{
									RespawnPosition spawnPoint = GetRandomRespawnPoint(details.RespawnPositions);

									if (spawnPoint == null ||
										character == null ||
										character.Motor == null)
									{
										continue;
									}

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
						CharacterService.Save(dbContext, new List<Character>(CharactersByID.Values));
					}
				}
				nextSave -= Time.deltaTime;
			}
		}

		private void OnApplicationQuit()
		{
			if (Server != null && Server.NpgsqlDbContextFactory != null)
			{
				// save all the characters before we quit
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				CharacterService.Save(dbContext, new List<Character>(CharactersByID.Values), false);
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;

			if (args.ConnectionState == LocalConnectionState.Started)
			{
				loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
				Server.NetworkManager.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
				Server.NetworkManager.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;
			}
		}

		/// <summary>
		/// When a connection disconnects the server removes all known instances of the character and saves it to the database.
		/// </summary>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped &&
				ServerBehaviour.TryGet(out SceneServerSystem sceneServerSystem))
			{
				// Remove the waiting scene load character if it exists, these characters exist but are not spawned
				if (WaitingSceneLoadCharacters.TryGetValue(conn, out Character waitingSceneCharacter))
				{
					WaitingSceneLoadCharacters.Remove(conn);

					if (sceneServerSystem.TryGetSceneInstanceDetails(waitingSceneCharacter.WorldServerID,
																	 waitingSceneCharacter.SceneName.Value,
																	 waitingSceneCharacter.SceneHandle,
																	 out SceneInstanceDetails instance))
					{
						--instance.CharacterCount;

						OnDisconnect?.Invoke(conn, waitingSceneCharacter);
					}

					Server.NetworkManager.StorePooledInstantiated(waitingSceneCharacter.NetworkObject, true);
				}

				if (ConnectionCharacters.TryGetValue(conn, out Character character))
				{
					// remove the connection->character entry
					ConnectionCharacters.Remove(conn);

					// remove the characterID->character entry
					CharactersByID.Remove(character.ID);
					// remove the characterName->character entry
					CharactersByLowerCaseName.Remove(character.CharacterNameLower);
					// remove the worldid<characterID->character> entry
					if (CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, Character> characters))
					{
						characters.Remove(character.ID);
					}

					OnDisconnect?.Invoke(conn, character);

					TryTeleport(character);

					// update scene instance details
					if (sceneServerSystem.TryGetSceneInstanceDetails(character.WorldServerID,
																	 character.SceneName.Value,
																	 character.SceneHandle,
																	 out SceneInstanceDetails instance))
					{
						--instance.CharacterCount;
					}

					// save the character and set online status to false
					using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
					CharacterService.Save(dbContext, character, false);

					// immediately log out for now.. we could add a timeout later on..?
					if (character.NetworkObject.IsSpawned)
						ServerManager.Despawn(character.NetworkObject, DespawnType.Pool);
				}
			}
		}

		private void TryTeleport(Character character)
		{
			if (character == null)
			{
				Debug.Log("Character not found!");
				return;
			}

			if (!character.IsTeleporting)
			{
				return;
			}

			// should we prevent players from moving to a different scene if they are in combat?
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

			// cache the current scene name
			string playerScene = character.SceneName.Value;

			if (sceneServerSystem.WorldSceneDetailsCache == null ||
				!sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(playerScene, out WorldSceneDetails details))
			{
				Debug.Log(playerScene + " not found!");
				return;
			}

			// check if we are a scene teleporter
			if (!details.Teleporters.TryGetValue(character.TeleporterName, out SceneTeleporterDetails teleporter))
			{
				Debug.Log("Teleporter: " + character.TeleporterName + " not found!");
				return;
			}

			using var dbContext = sceneServerSystem.Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				Debug.Log("Could not get database context.");
				return;
			}

			// character becomes immortal when teleporting
			if (character.TryGet(out CharacterDamageController damageController))
			{
				damageController.Immortal = true;
			}

			OnCharacterChangedScene?.Invoke(character, teleporter.ToScene);
			character.SceneName.SetInitialValues(teleporter.ToScene);
			character.Motor.SetPositionAndRotationAndVelocity(teleporter.ToPosition, teleporter.ToRotation, Vector3.zero);
		}

		private void Authenticator_OnClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			if (!authenticated ||
				!AccountManager.GetAccountNameByConnection(conn, out string accountName) ||
				!ServerBehaviour.TryGet(out SceneServerSystem sceneServerSystem))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}
			// create the db context
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

			if (CharacterService.TryGetSelectedDetails(dbContext, accountName, out long selectedCharacterID))
			{
				if (CharactersByID.ContainsKey(selectedCharacterID))
				{
					//Debug.Log(selectedCharacterID + " is already loaded or loading. FIXME");

					// character load already started or complete
					conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
					return;
				}

				OnBeforeLoadCharacter?.Invoke(conn, selectedCharacterID);
				if (CharacterService.TryGet(dbContext, selectedCharacterID, Server.NetworkManager, out Character character))
				{
					// check if the scene is valid, loaded, and cached properly
					if (sceneServerSystem.TryGetSceneInstanceDetails(character.WorldServerID, character.SceneName.Value, character.SceneHandle, out SceneInstanceDetails instance) &&
						sceneServerSystem.TryLoadSceneForConnection(conn, character, instance))
					{
						OnAfterLoadCharacter?.Invoke(conn, character);

						WaitingSceneLoadCharacters.Add(conn, character);

						// update character count
						++instance.CharacterCount;

						Debug.Log("Character System: " + character.CharacterName + " is loading Scene: " + character.SceneName.Value + ":" + character.SceneHandle);
					}
					else
					{
						// send the character back to the world server.. something went wrong
						conn.Disconnect(false);
					}
				}
				else
				{
					// loading the character failed for some reason, maybe it doesn't exist? we should never get to this point but we will kick the player anyway
					conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				}
			}
			else
			{
				// loading the character data failed to load for some reason, maybe it doesn't exist? we should never get to this point but we will kick the player anyway
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
			}
		}

		/// <summary>
		/// Called when a client loads scenes after connecting. The character is activated and spawned for all observers.
		/// </summary>
		private void SceneManager_OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
		{
			if (WaitingSceneLoadCharacters.TryGetValue(conn, out Character character))
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
				if (!CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, Character> characters))
				{
					CharactersByWorld.Add(character.WorldServerID, characters = new Dictionary<long, Character>());
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
					Destroy(character.gameObject);
					conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
					return;
				}

				// set the proper physics scene for the character, scene stacking requires separated physics
				character.Motor.SetPhysicsScene(scene.GetPhysicsScene());

				// character becomes mortal when loaded into the scene
				if (character.TryGet(out CharacterDamageController damageController))
				{
					damageController.Immortal = false;
				}

				// ensure the game object is active, pooled objects are disabled
				character.gameObject.SetActive(true);

				// spawn the nob over the network
				ServerManager.Spawn(character.NetworkObject, conn, scene);

				// set the character status to online
				if (AccountManager.GetAccountNameByConnection(conn, out string accountName))
				{
					using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
					CharacterService.SetOnline(dbContext, accountName, character.CharacterName);
				}

				// send server character data to the client
				SendAllCharacterData(character);

				//Debug.Log(character.CharacterName + " has been spawned at: " + character.SceneName + " " + character.Transform.position.ToString());
			}
			else
			{
				// couldn't find the character details for the connection.. kick the player
				conn.Kick(FishNet.Managing.Server.KickReason.UnexpectedProblem);
			}
		}

		/// <summary>
		/// Sends all Server Side Character data to the owner. *Expensive*
		/// </summary>
		/// <param name="character"></param>
		public void SendAllCharacterData(Character character)
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

			// set syncvars dirty which forces it to sync to the client	
			character.Currency.Dirty();
			character.RaceID.Dirty();
			character.RaceName.Dirty();
			character.SceneName.Dirty();

			#region Abilities
			if (character.TryGet(out AbilityController abilityController))
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
							templateID = templateID,
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
							templateID = templateID,
						});
					}
				}

				// tell the client they have known abilities
				if (knownAbilityBroadcasts.Count > 0)
				{
					Server.Broadcast(character.Owner, new KnownAbilityAddMultipleBroadcast()
					{
						abilities = knownAbilityBroadcasts,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region Achievements
			if (character.TryGet(out AchievementController achievementController))
			{
				List<AchievementUpdateBroadcast> achievements = new List<AchievementUpdateBroadcast>();
				foreach (Achievement achievement in achievementController.Achievements.Values)
				{
					achievements.Add(new AchievementUpdateBroadcast()
					{
						templateID = achievement.Template.ID,
						newValue = achievement.CurrentValue,
					});
				}
				Server.Broadcast(character.Owner, new AchievementUpdateMultipleBroadcast()
				{
					achievements = achievements,
				}, true, Channel.Reliable);
			}
			#endregion

			#region Guild
			if (character.TryGet(out GuildController guildController) &&
				guildController.ID > 0)
			{
				// get the current guild members from the database
				List<CharacterGuildEntity> dbMembers = CharacterGuildService.Members(dbContext, guildController.ID);

				var addBroadcasts = dbMembers.Select(x => new GuildAddBroadcast()
				{
					guildID = x.GuildID,
					characterID = x.CharacterID,
					rank = (GuildRank)x.Rank,
					location = x.Location,
				}).ToList();

				GuildAddMultipleBroadcast guildAddBroadcast = new GuildAddMultipleBroadcast()
				{
					members = addBroadcasts,
				};
				Server.Broadcast(character.Owner, guildAddBroadcast, true, Channel.Reliable);
			}
			#endregion

			#region Party
			if (character.TryGet(out PartyController partyController) &&
				partyController.ID > 0)
			{
				// get the current party members from the database
				List<CharacterPartyEntity> dbMembers = CharacterPartyService.Members(dbContext, partyController.ID);

				var addBroadcasts = dbMembers.Select(x => new PartyAddBroadcast()
				{
					partyID = x.PartyID,
					characterID = x.CharacterID,
					rank = (PartyRank)x.Rank,
					healthPCT = x.HealthPCT,
				}).ToList();

				PartyAddMultipleBroadcast partyAddBroadcast = new PartyAddMultipleBroadcast()
				{
					members = addBroadcasts,
				};
				Server.Broadcast(character.Owner, partyAddBroadcast, true, Channel.Reliable);
			}
			#endregion

			#region Friends
			if (character.TryGet(out FriendController friendController))
			{
				List<FriendAddBroadcast> friends = new List<FriendAddBroadcast>();
				foreach (long friendID in friendController.Friends)
				{
					bool status = CharacterService.ExistsAndOnline(dbContext, friendID);
					friends.Add(new FriendAddBroadcast()
					{
						characterID = friendID,
						online = status,
					});
				}
				Server.Broadcast(character.Owner, new FriendAddMultipleBroadcast()
				{
					friends = friends,
				}, true, Channel.Reliable);
			}
			#endregion

			#region InventoryItems
			if (character.TryGet(out InventoryController inventoryController))
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
						instanceID = item.ID,
						templateID = item.Template.ID,
						slot = item.Slot,
						seed = item.IsGenerated ? item.Generator.Seed : 0,
						stackSize = item.IsStackable ? item.Stackable.Amount : 0,
					});
				}

				// tell the client they have items
				if (itemBroadcasts.Count > 0)
				{
					Server.Broadcast(character.Owner, new InventorySetMultipleItemsBroadcast()
					{
						items = itemBroadcasts,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region BankItems
			if (character.TryGet(out BankController bankController))
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
						instanceID = item.ID,
						templateID = item.Template.ID,
						slot = item.Slot,
						seed = item.IsGenerated ? item.Generator.Seed : 0,
						stackSize = item.IsStackable ? item.Stackable.Amount : 0,
					});
				}

				// tell the client they have items
				if (itemBroadcasts.Count > 0)
				{
					Server.Broadcast(character.Owner, new BankSetMultipleItemsBroadcast()
					{
						items = itemBroadcasts,
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
			if (CharactersByLowerCaseName.TryGetValue(characterName.ToLower(), out Character character))
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
			if (CharactersByID.TryGetValue(characterID, out Character character))
			{
				Server.Broadcast(character.Owner, msg, true, Channel.Reliable);
				return true;
			}
			return false;
		}

		private RespawnPosition GetRandomRespawnPoint(RespawnPositionDictionary respawnPoints)
		{
			RespawnPosition[] spawnPoints = respawnPoints.Values.ToArray();

			if (spawnPoints.Length == 0) throw new IndexOutOfRangeException("Failed to get a respawn point! Please ensure you have rebuilt your world scene cache and have respawn points in your scene!");

			int index = UnityEngine.Random.Range(0, spawnPoints.Length);

			return spawnPoints[index];
		}
	}
}