using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Server.Services;
using FishMMO_DB.Entities;
using UnityEngine;

namespace FishMMO.Server
{
	// Character manager handles the players character
	public class CharacterSystem : ServerBehaviour
	{
		private SceneServerAuthenticator loginAuthenticator;
		private LocalConnectionState serverState;

		public float SaveRate = 60.0f;
		private float nextSave = 0.0f;

		public float OutOfBoundsCheckRate = 5f;
		private float nextOutOfBoundsCheck = 0.0f;

		/// <summary>
		/// Triggered before a character is loaded from the database. <conn, CharacterID>
		/// </summary>
		public event Action<NetworkConnection, long> OnBeforeLoadCharacter;
		/// <summary>
		/// Triggered after a character is loaded from the database. <conn, Character>
		/// </summary>
		public event Action<NetworkConnection, Character> OnAfterLoadCharacter;

		public Dictionary<long, Character> CharactersByID = new Dictionary<long, Character>();
		public Dictionary<string, Character> CharactersByLowerCaseName = new Dictionary<string, Character>();
		public Dictionary<long, Dictionary<long, Character>> CharactersByWorld = new Dictionary<long, Dictionary<long, Character>>();
		public Dictionary<NetworkConnection, Character> ConnectionCharacters = new Dictionary<NetworkConnection, Character>();
		public Dictionary<NetworkConnection, Character> WaitingSceneLoadCharacters = new Dictionary<NetworkConnection, Character>();

		public override void InitializeOnce()
		{
			nextSave = SaveRate;

			if (ServerManager != null &&
				Server.SceneServerSystem != null)
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
			if (serverState == LocalConnectionState.Started)
			{
				if(nextOutOfBoundsCheck < 0)
				{
					nextOutOfBoundsCheck = OutOfBoundsCheckRate;

					// TODO: Should the character be doing this and more often?
					// They'd need a cached world boundaries to check themselves against
					// which would prevent the need to do all of this lookup stuff.
					foreach (Character character in ConnectionCharacters.Values)
					{
						if(Server.SceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out WorldSceneDetails details))
						{
							// Check if they are within some bounds, if not we need to move them to a respawn location!
							// TODO: Try to prevent combat escape, maybe this needs to be handled on the game design level?
							if(details.Boundaries.PointContainedInBoundaries(character.Transform.position) == false)
							{
								Vector3 spawnPoint = GetRandomRespawnPoint(details.RespawnPositions);
								character.Motor.SetPositionAndRotationAndVelocity(spawnPoint, character.Transform.rotation, Vector3.zero);
							}
						}
					}
				}
				nextOutOfBoundsCheck -= Time.deltaTime;

				if (nextSave < 0)
				{
					nextSave = SaveRate;
					
					Debug.Log("Character System: Save" + "[" + DateTime.UtcNow + "]");

					// all characters are periodically saved
					using var dbContext = Server.DbContextFactory.CreateDbContext();
					CharacterService.Save(dbContext, new List<Character>(CharactersByID.Values));
					dbContext.SaveChanges();
				}
				nextSave -= Time.deltaTime;
			}
		}

		private void OnApplicationQuit()
		{
			if (Server != null && Server.DbContextFactory != null)
			{
				// save all the characters before we quit
				using var dbContext = Server.DbContextFactory.CreateDbContext();
				CharacterService.Save(dbContext, new List<Character>(CharactersByID.Values), false);
				dbContext.SaveChanges();
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;

			if (args.ConnectionState == LocalConnectionState.Started)
			{
				loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
				Server.SceneServerSystem.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
				Server.SceneServerSystem.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;
			}
		}

		/// <summary>
		/// When a connection disconnects the server removes all known instances of the character and saves it to the database.
		/// </summary>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped)
			{
				// Remove the waiting scene load character if it exists
				if(WaitingSceneLoadCharacters.TryGetValue(conn, out Character waitingSceneCharacter))
				{
					Server.NetworkManager.StorePooledInstantiated(waitingSceneCharacter.NetworkObject, true);
					WaitingSceneLoadCharacters.Remove(conn);

					if (Server.SceneServerSystem.TryGetSceneInstanceDetails(waitingSceneCharacter.WorldServerID,
																			waitingSceneCharacter.SceneName,
																			waitingSceneCharacter.SceneHandle,
																			out SceneInstanceDetails instance))
					{
						--instance.CharacterCount;
					}
					return;
				}

				if (ConnectionCharacters.TryGetValue(conn, out Character character))
				{
					// remove the characterID->character entry
					CharactersByID.Remove(character.ID);
					// remove the characterName->character entry
					CharactersByLowerCaseName.Remove(character.CharacterNameLower);
					// remove the worldid<characterID->character> entry
					if (CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, Character> characters))
					{
						characters.Remove(character.ID);
					}
					// remove the connection->character entry
					ConnectionCharacters.Remove(conn);

					if (Server.SceneServerSystem.TryGetSceneInstanceDetails(character.WorldServerID,
																			character.SceneName,
																			character.SceneHandle,
																			out SceneInstanceDetails instance))
					{
						--instance.CharacterCount;
					}

					// no character so we can just skip the rest
					if (character == null)
					{
						return;
					}

					// remove the characters pending guild invite request
					if (Server.GuildSystem != null)
					{
						Server.GuildSystem.RemovePending(character.ID);
					}

					// remove the characters pending party invite request
					if (Server.PartySystem != null)
					{
						Server.PartySystem.RemovePending(character.ID);
					}

					if (character.IsTeleporting)
					{
						// teleporter handles the rest
						return;
					}

					// character becomes immortal on disconnect and mortal when fully loaded into the scene
					if (character.DamageController != null)
					{
						character.DamageController.Immortal = true;
					}

					// save the character and set online to false
					using var dbContext = Server.DbContextFactory.CreateDbContext();
					CharacterService.Save(dbContext, character, false);
					dbContext.SaveChanges();

					// immediately log out for now.. we could add a timeout later on..?
					if (character.NetworkObject.IsSpawned)
						ServerManager.Despawn(character.NetworkObject, DespawnType.Pool);
				}
			}
		}

		private void Authenticator_OnClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			if (!authenticated || !AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}
			// create the db context
			using var dbContext = Server.DbContextFactory.CreateDbContext();

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
					if (Server.SceneServerSystem.TryGetSceneInstanceDetails(character.WorldServerID, character.SceneName, character.SceneHandle, out SceneInstanceDetails instance) &&
						Server.SceneServerSystem.TryLoadSceneForConnection(conn, instance))
					{
						OnAfterLoadCharacter?.Invoke(conn, character);

						WaitingSceneLoadCharacters.Add(conn, character);

						// update character count
						++instance.CharacterCount;

						Debug.Log("Character System: " + character.CharacterName + " is loading Scene: " + character.SceneName + ":" + character.SceneHandle);
					}
					else
					{
						// send the character back to the world server.. something went wrong
						WorldServerEntity worldServer = WorldServerService.GetServer(dbContext, character.WorldServerID);
						if (worldServer != null)
						{
							// Scene loading is the responsibility of the world server, send them over there to reconnect to a scene server
							conn.Broadcast(new SceneWorldReconnectBroadcast()
							{
								address = worldServer.Address,
								port = worldServer.Port
							});
						}
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
				// add a connection->character map for ease of use
				if (ConnectionCharacters.ContainsKey(conn))
				{
					ConnectionCharacters[conn] = character;
				}
				else
				{
					ConnectionCharacters.Add(conn, character);
				}

				// add a characterName->character map for ease of use
				if (CharactersByID.ContainsKey(character.ID))
				{
					CharactersByID[character.ID] = character;
					CharactersByLowerCaseName[character.CharacterNameLower] = character;
				}
				else
				{
					CharactersByID.Add(character.ID, character);
					CharactersByLowerCaseName.Add(character.CharacterNameLower, character);
				}

				// add a worldID<characterID->character> map for ease of use
				if (!CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, Character> characters))
				{
					CharactersByWorld.Add(character.WorldServerID, characters = new Dictionary<long, Character>());
				}
				if (characters.ContainsKey(character.ID))
				{
					characters[character.ID] = character;
				}
				else
				{
					characters.Add(character.ID, character);
				}

				// remove the waiting scene load character
				WaitingSceneLoadCharacters.Remove(conn);

				// character becomes immortal on disconnect and mortal when loaded into the scene
				if (character.DamageController != null)
				{
					character.DamageController.Immortal = false;
				}

				// set the proper physics scene for the character, scene stacking requires separated physics
				if (character.Motor != null)
				{
					Server.SceneServerSystem.AssignPhysicsScene(character);
				}

				// ensure the game object is active, pooled objects are disabled
				character.gameObject.SetActive(true);
					
				// spawn the nob over the network
				ServerManager.Spawn(character.NetworkObject, conn);

				// set the character status to online
				if (AccountManager.GetAccountNameByConnection(conn, out string accountName))
				{
					using var dbContext = Server.DbContextFactory.CreateDbContext();
					CharacterService.SetOnline(dbContext, accountName, character.CharacterName);
					dbContext.SaveChanges();
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
			if (Server.DbContextFactory == null)
			{
				return;
			}

			using var dbContext = Server.DbContextFactory.CreateDbContext();

			#region Attributes
			if (character.AttributeController != null)
			{
				List<CharacterAttributeUpdateBroadcast> attributes = new List<CharacterAttributeUpdateBroadcast>();
				foreach (CharacterAttribute attribute in character.AttributeController.Attributes.Values)
				{
					if (attribute.Template.IsResourceAttribute)
						continue;

					attributes.Add(new CharacterAttributeUpdateBroadcast()
					{
						templateID = attribute.Template.ID,
						value = attribute.FinalValue,
					});
				}
				character.Owner.Broadcast(new CharacterAttributeUpdateMultipleBroadcast()
				{
					attributes = attributes,
				});

				List<CharacterResourceAttributeUpdateBroadcast> resourceAttributes = new List<CharacterResourceAttributeUpdateBroadcast>();
				foreach (CharacterResourceAttribute attribute in character.AttributeController.ResourceAttributes.Values)
				{
					resourceAttributes.Add(new CharacterResourceAttributeUpdateBroadcast()
					{
						templateID = attribute.Template.ID,
						value = attribute.CurrentValue,
						max = attribute.FinalValue,
					});
				}
				character.Owner.Broadcast(new CharacterResourceAttributeUpdateMultipleBroadcast()
				{
					attributes = resourceAttributes,
				});
			}
			#endregion

			#region Achievements
			if (character.AchievementController != null)
			{
				List<AchievementUpdateBroadcast> achievements = new List<AchievementUpdateBroadcast>();
				foreach (Achievement achievement in character.AchievementController.Achievements.Values)
				{
					achievements.Add(new AchievementUpdateBroadcast()
					{
						templateID = achievement.Template.ID,
						newValue = achievement.CurrentValue,
					});
				}
				character.Owner.Broadcast(new AchievementUpdateMultipleBroadcast()
				{
					achievements = achievements,
				});
			}
			#endregion

			#region Guild
			if (character.GuildController != null && character.GuildController.ID > 0)
			{
				// get the current guild members from the database
				List<CharacterGuildEntity> dbMembers = CharacterGuildService.Members(dbContext, character.GuildController.ID);

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
				character.Owner.Broadcast(guildAddBroadcast);
			}
			#endregion

			#region Party
			if (character.PartyController != null && character.PartyController.ID > 0)
			{
				// get the current party members from the database
				List<CharacterPartyEntity> dbMembers = CharacterPartyService.Members(dbContext, character.PartyController.ID);

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
				character.Owner.Broadcast(partyAddBroadcast);
			}
			#endregion

			#region Friends
			if (character.FriendController != null)
			{
				List<FriendAddBroadcast> friends = new List<FriendAddBroadcast>();
				foreach (long friendID in character.FriendController.Friends)
				{
					bool status = CharacterService.ExistsAndOnline(dbContext, friendID);
					friends.Add(new FriendAddBroadcast()
					{
						characterID = friendID,
						online = status,
					});
				}
				character.Owner.Broadcast(new FriendAddMultipleBroadcast()
				{
					friends = friends,
				});
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
				character.Owner.Broadcast(msg);
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
				character.Owner.Broadcast(msg);
				return true;
			}
			return false;
		}

		private Vector3 GetRandomRespawnPoint(RespawnPositionDictionary respawnPoints)
		{
			Vector3[] spawnPoints = respawnPoints.Values.ToArray();

			if (spawnPoints.Length == 0) throw new IndexOutOfRangeException("Failed to get a respawn point! Please ensure you have rebuilt your world scene cache and have respawn points in your scene!");

			return spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Length)];
		}
	}
}