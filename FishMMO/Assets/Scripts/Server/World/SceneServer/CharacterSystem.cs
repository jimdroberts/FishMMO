using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing.Client;
using FishNet.Object;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using FishMMO.Server.Services;
using UnityEngine;
using System.Linq;

namespace FishMMO.Server
{
	// Character manager handles the players character
	public class CharacterSystem : ServerBehaviour
	{
		public SceneServerSystem SceneServerSystem;

		private SceneServerAuthenticator loginAuthenticator;
		private LocalConnectionState serverState;

		public float saveRate = 60.0f;
		private float nextSave = 0.0f;

		public float outOfBoundsCheckRate = 5f;
		private float nextOutOfBoundsCheck = 0.0f;

		public Dictionary<long, Character> charactersById = new Dictionary<long, Character>();
		public Dictionary<string, Character> charactersByName = new Dictionary<string, Character>();
		public Dictionary<NetworkConnection, Character> connectionCharacters = new Dictionary<NetworkConnection, Character>();

		public Dictionary<NetworkConnection, Character> waitingSceneLoadCharacters = new Dictionary<NetworkConnection, Character>();

		public override void InitializeOnce()
		{
			nextSave = saveRate;

			if (ServerManager != null &&
				ClientManager != null &&
				SceneServerSystem != null)
			{
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
				nextSave -= Time.deltaTime;
				nextOutOfBoundsCheck -= Time.deltaTime;

				if(nextOutOfBoundsCheck < 0)
				{
					nextOutOfBoundsCheck = outOfBoundsCheckRate;

					// TODO: Should the character be doing this and more often?
					// They'd need a cached world boundaries to check themselves against
					// which would prevent the need to do all of this lookup stuff.
					foreach (Character character in connectionCharacters.Values)
					{
						if(SceneServerSystem.worldSceneDetailsCache.scenes.TryGetValue(character.sceneName, out WorldSceneDetails details))
						{
							// Check if they are within some bounds, if not we need to move them to a respawn location!
							// TODO: Try to prevent combat escape, maybe this needs to be handled on the game design level?
							if(details.boundaries.PointContainedInBoundaries(character.transform.position) == false)
							{
								Vector3 spawnPoint = GetRandomRespawnPoint(details.respawnPositions);
								character.Motor.SetPositionAndRotationAndVelocity(spawnPoint, character.transform.rotation, Vector3.zero);
							}
						}
					}
				}

				if (nextSave < 0)
				{
					nextSave = saveRate;
					
					Debug.Log("[" + DateTime.UtcNow + "] CharacterManager: Save");

					// all characters are periodically saved
					using var dbContext = Server.DbContextFactory.CreateDbContext();
					CharacterService.SaveCharacters(dbContext, new List<Character>(charactersById.Values));
					dbContext.SaveChanges();
				}
			}
		}

		private void OnApplicationQuit()
		{
			Debug.Log("Disconnecting...");
			// save all the characters before we quit
			using var dbContext = Server.DbContextFactory.CreateDbContext();
			CharacterService.SaveCharacters(dbContext, new List<Character>(charactersById.Values), false);
			dbContext.SaveChanges();
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			loginAuthenticator = FindObjectOfType<SceneServerAuthenticator>();
			if (loginAuthenticator == null)
				return;

			serverState = args.ConnectionState;

			if (args.ConnectionState == LocalConnectionState.Started)
			{
				loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
				SceneServerSystem.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
				SceneServerSystem.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;
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
				if(waitingSceneLoadCharacters.TryGetValue(conn, out Character waitingSceneCharacter))
				{
					Server.NetworkManager.StorePooledInstantiated(waitingSceneCharacter.NetworkObject, true);
					waitingSceneLoadCharacters.Remove(conn);
					return;
				}

				if (connectionCharacters.TryGetValue(conn, out Character character))
				{
					if (character == null)
					{
						// character is missing.. socket is closed but we kick just incase
						conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
						return;
					}

					// remove the characterId->character entry
					charactersById.Remove(character.id);
					// remove the characterName->character entry
					charactersByName.Remove(character.characterName);
					// remove the connection->character entry
					connectionCharacters.Remove(conn);

					if (character.isTeleporting)
					{
						// teleporter handles the rest
						return;
					}

					// tell the world server the character disconnected, this only happens on a full disconnect
					if (ClientManager != null)
					{
						ClientManager.Broadcast(new SceneCharacterDisconnectedBroadcast()
						{
							characterId = character.id,
						});
					}

					// character becomes immortal on disconnect and mortal when fully loaded into the scene
					if (character.DamageController != null)
					{
						character.DamageController.immortal = true;
					}

					// save the character and set online to false
					using var dbContext = Server.DbContextFactory.CreateDbContext();
					CharacterService.SaveCharacter(dbContext, character, false);
					dbContext.SaveChanges();

					Debug.Log("[" + DateTime.UtcNow + "] " + character.characterName + " has been saved at: " + character.transform.position.ToString());

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

			if (CharacterService.TryGetSelectedCharacterDetails(dbContext, accountName, out long selectedCharacterId))
			{
				if (charactersById.ContainsKey(selectedCharacterId))
				{
					Debug.Log("[" + DateTime.UtcNow + "] " + selectedCharacterId + " is already loaded or loading. FIXME");

					// character load already started or complete
					conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
					return;
				}

				if (CharacterService.TryLoadCharacter(dbContext, selectedCharacterId, Server.NetworkManager, out Character character))
				{
					waitingSceneLoadCharacters.Add(conn, character);

					// check if the scene is valid, loaded, and cached properly
					if (SceneServerSystem.TryGetValidScene(character.sceneName, out SceneInstanceDetails instance))
					{
						Debug.Log("[" + DateTime.UtcNow + "] " + character.characterName + " is loading Scene: " + character.sceneName);

						if (SceneServerSystem.TryLoadSceneForConnection(conn, instance))
						{
							// assign scene handle for later..
							character.sceneHandle = instance.handle;
						}
						else
						{
							Debug.Log("[" + DateTime.UtcNow + "] " + character.characterName + " scene failed to load for connection.");

							// character scene not found even after validated
							conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
							return;
						}
					}
					else
					{
						// Scene loading is the responsibility of the world server, send them over there to reconnect to a scene server
						conn.Broadcast(new SceneWorldReconnectBroadcast()
						{
							address = Server.relayAddress,
							port = Server.relayPort
						});
					}
				}
			}
		}

		/// <summary>
		/// Called when a client loads scenes after connecting. The character is activated and spawned for all observers.
		/// </summary>
		private void SceneManager_OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
		{
			if (waitingSceneLoadCharacters.TryGetValue(conn, out Character character))
			{
				// remove the waiting scene load character
				waitingSceneLoadCharacters.Remove(conn);

				// character becomes immortal on disconnect and mortal when loaded into the scene
				if (character.DamageController != null)
				{
					character.DamageController.immortal = false;
				}

				// set the proper physics scene for the character, scene stacking requires separated physics
				if (character.Motor != null)
				{
					SceneServerSystem.AssignPhysicsScene(character);
				}

				// ensure the game object is active, pooled objects are disabled
				character.gameObject.SetActive(true);
					
				// spawn the nob over the network
				ServerManager.Spawn(character.NetworkObject, conn);

				// add a connection->character map for ease of use
				if (connectionCharacters.ContainsKey(conn))
				{
					connectionCharacters[conn] = character;
				}
				else
				{
					connectionCharacters.Add(conn, character);
				}

				// add a characterName->character map for ease of use
				if (charactersById.ContainsKey(character.id))
				{
					charactersById[character.id] = character;
					charactersByName[character.characterName] = character;
				}
				else
				{
					charactersById.Add(character.id, character);
					charactersByName.Add(character.characterName, character);
				}

				// set the character status to online
				if (AccountManager.GetAccountNameByConnection(conn, out string accountName))
				{
					// doesn't contain any important functionality yet.. we just do it for fun
					using var dbContext = Server.DbContextFactory.CreateDbContext();
					CharacterService.TrySetCharacterOnline(dbContext, accountName, character.characterName);
					dbContext.SaveChanges();
				}

				// tell the world server the character is active
				if (ClientManager != null)
				{
					ClientManager.Broadcast(new SceneCharacterConnectedBroadcast()
					{
						characterId = character.id,
						sceneName = character.sceneName,
					});
				}

				Debug.Log("[" + DateTime.UtcNow + "] " + character.characterName + " has been spawned at: " + character.sceneName + " " + character.transform.position.ToString());
			}
			else
			{
				// couldn't find the character details for the connection.. kick the player
				conn.Kick(FishNet.Managing.Server.KickReason.UnexpectedProblem);
			}
		}

		/// <summary>
		/// Allows sending a broadcast to a specific character by their character name.
		/// Returns true if the broadcast was sent successfully.
		/// False if the character could not by found.
		/// </summary>
		public bool SendBroadcastToCharacter<T>(string characterName, T msg) where T : struct, IBroadcast
		{
			if (charactersByName.TryGetValue(characterName, out Character character))
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
		public bool SendBroadcastToCharacter<T>(long characterId, T msg) where T : struct, IBroadcast
		{
			if (charactersById.TryGetValue(characterId, out Character character))
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