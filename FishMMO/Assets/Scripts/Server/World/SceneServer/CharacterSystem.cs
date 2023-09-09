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
		private SceneServerAuthenticator loginAuthenticator;
		private LocalConnectionState serverState;

		public float SaveRate = 60.0f;
		private float nextSave = 0.0f;

		public float OutOfBoundsCheckRate = 5f;
		private float nextOutOfBoundsCheck = 0.0f;

		public Dictionary<long, Character> CharactersById = new Dictionary<long, Character>();
		public Dictionary<string, Character> CharactersByName = new Dictionary<string, Character>();
		public Dictionary<NetworkConnection, Character> ConnectionCharacters = new Dictionary<NetworkConnection, Character>();
		public Dictionary<NetworkConnection, Character> WaitingSceneLoadCharacters = new Dictionary<NetworkConnection, Character>();

		public override void InitializeOnce()
		{
			nextSave = SaveRate;

			if (ServerManager != null &&
				ClientManager != null &&
				Server.SceneServerSystem != null)
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
					
					Debug.Log("CharacterManager: Save" + "[" + DateTime.UtcNow + "]");

					// all characters are periodically saved
					using var dbContext = Server.DbContextFactory.CreateDbContext();
					CharacterService.SaveCharacters(dbContext, new List<Character>(CharactersById.Values));
					dbContext.SaveChanges();
				}
				nextSave -= Time.deltaTime;
			}
		}

		private void OnApplicationQuit()
		{
			Debug.Log("Disconnecting...");
			// save all the characters before we quit
			using var dbContext = Server.DbContextFactory.CreateDbContext();
			CharacterService.SaveCharacters(dbContext, new List<Character>(CharactersById.Values), false);
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
					return;
				}

				if (ConnectionCharacters.TryGetValue(conn, out Character character))
				{
					if (character == null)
					{
						// character is missing.. socket is closed but we kick just incase
						conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
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

					// remove the characterId->character entry
					CharactersById.Remove(character.ID);
					// remove the characterName->character entry
					CharactersByName.Remove(character.CharacterName);
					// remove the connection->character entry
					ConnectionCharacters.Remove(conn);

					if (character.IsTeleporting)
					{
						// teleporter handles the rest
						return;
					}

					// tell the world server the character disconnected, this only happens on a full disconnect
					if (ClientManager != null)
					{
						ClientManager.Broadcast(new SceneCharacterDisconnectedBroadcast()
						{
							characterId = character.ID,
						});
					}

					// character becomes immortal on disconnect and mortal when fully loaded into the scene
					if (character.DamageController != null)
					{
						character.DamageController.Immortal = true;
					}

					// save the character and set online to false
					using var dbContext = Server.DbContextFactory.CreateDbContext();
					CharacterService.SaveCharacter(dbContext, character, false);
					dbContext.SaveChanges();

					Debug.Log(character.CharacterName + " has been saved at: " + character.Transform.position.ToString());

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
				if (CharactersById.ContainsKey(selectedCharacterId))
				{
					Debug.Log(selectedCharacterId + " is already loaded or loading. FIXME");

					// character load already started or complete
					conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
					return;
				}

				if (CharacterService.TryLoadCharacter(dbContext, selectedCharacterId, Server.NetworkManager, out Character character))
				{
					WaitingSceneLoadCharacters.Add(conn, character);

					// check if the scene is valid, loaded, and cached properly
					if (Server.SceneServerSystem.TryGetValidScene(character.SceneName, out SceneInstanceDetails instance))
					{
						Debug.Log(character.CharacterName + " is loading Scene: " + character.SceneName);

						if (Server.SceneServerSystem.TryLoadSceneForConnection(conn, instance))
						{
							// assign scene handle for later..
							character.SceneHandle = instance.Handle;
						}
						else
						{
							Debug.Log(character.CharacterName + " scene failed to load for connection.");

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
							address = Server.RelayAddress,
							port = Server.RelayPort
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
			if (WaitingSceneLoadCharacters.TryGetValue(conn, out Character character))
			{
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

				/* test
				if (character.AttributeController.TryGetResourceAttribute("Health", out CharacterResourceAttribute health))
				{
					health.SetCurrentValue(50);
					character.Owner.Broadcast(new CharacterResourceAttributeUpdateBroadcast()
					{
						templateID = health.Template.ID,
						value = health.CurrentValue,
						max = health.FinalValue,
					});
				}*/

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
				if (CharactersById.ContainsKey(character.ID))
				{
					CharactersById[character.ID] = character;
					CharactersByName[character.CharacterName] = character;
				}
				else
				{
					CharactersById.Add(character.ID, character);
					CharactersByName.Add(character.CharacterName, character);
				}

				// set the character status to online
				if (AccountManager.GetAccountNameByConnection(conn, out string accountName))
				{
					// doesn't contain any important functionality yet.. we just do it for fun
					using var dbContext = Server.DbContextFactory.CreateDbContext();
					CharacterService.TrySetCharacterOnline(dbContext, accountName, character.CharacterName);
					dbContext.SaveChanges();
				}

				// tell the world server the character is active
				if (ClientManager != null)
				{
					ClientManager.Broadcast(new SceneCharacterConnectedBroadcast()
					{
						characterId = character.ID,
						sceneName = character.SceneName,
					});
				}

				Debug.Log(character.CharacterName + " has been spawned at: " + character.SceneName + " " + character.Transform.position.ToString());
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
			if (CharactersByName.TryGetValue(characterName, out Character character))
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
			if (CharactersById.TryGetValue(characterId, out Character character))
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