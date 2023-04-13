using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing.Client;
using FishNet.Object;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using Server.Services;
using UnityEngine;

namespace Server
{
	// Character manager handles the players character
	public class CharacterSystem : ServerBehaviour
	{
		public SceneServerSystem SceneServerSystem;

		private SceneServerAuthenticator loginAuthenticator;
		private LocalConnectionState serverState;

		public float saveRate = 60.0f;
		private float nextSave = 0.0f;

		public List<NetworkObject> characterPrefabs = new List<NetworkObject>();
		public WorldSceneDetailsCache worldSceneDetailsCache;

		public Dictionary<string, Character> characters = new Dictionary<string, Character>();
		public Dictionary<NetworkConnection, Character> connectionCharacters = new Dictionary<NetworkConnection, Character>();


		public Dictionary<NetworkConnection, Character> waitingSceneLoadCharacters = new Dictionary<NetworkConnection, Character>();

		/// <summary>
		/// WaitingCharacters are all the loaded characters waiting for the server to load a scene
		/// </summary>
		// sceneName, <connection, character>
		public Dictionary<string, Dictionary<NetworkConnection, Character>> waitingCharacters = new Dictionary<string, Dictionary<NetworkConnection, Character>>();

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
				if (nextSave < 0)
				{
					nextSave = saveRate;
					
					Debug.Log("[" + DateTime.UtcNow + "] CharacterManager: Save");

					// all characters are periodically saved
					using var dbContext = Server.DbContextFactory.CreateDbContext();
					CharacterService.SaveCharacters(dbContext, new List<Character>(characters.Values));
					dbContext.SaveChanges();
				}
			}
		}

		private void OnApplicationQuit()
		{
			Debug.Log("Disconnecting...");
			// save all the characters before we quit
			using var dbContext = Server.DbContextFactory.CreateDbContext();
			CharacterService.SaveCharacters(dbContext, new List<Character>(characters.Values), false);
			dbContext.SaveChanges();
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs obj)
		{
			loginAuthenticator = FindObjectOfType<SceneServerAuthenticator>();
			if (loginAuthenticator == null)
				return;

			serverState = obj.ConnectionState;

			if (obj.ConnectionState == LocalConnectionState.Started)
			{
				loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;

				SceneServerSystem.OnSceneLoadComplete += SceneManager_OnSceneLoadComplete;
				SceneServerSystem.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;

				ServerManager.RegisterBroadcast<CharacterSceneChangeRequestBroadcast>(OnServerCharacterSceneChangeRequestReceived, true);
			}
			else if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;

				SceneServerSystem.OnSceneLoadComplete -= SceneManager_OnSceneLoadComplete;
				SceneServerSystem.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;

				ServerManager.UnregisterBroadcast<CharacterSceneChangeRequestBroadcast>(OnServerCharacterSceneChangeRequestReceived);
			}
		}

		/// <summary>
		/// When a connection disconnects the server removes all known instances of the character and saves it to the database.
		/// </summary>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped)
			{
				// remove the waiting scene load character if it exists
				if (waitingSceneLoadCharacters.TryGetValue(conn, out Character waitingSceneCharacter))
				{
					Destroy(waitingSceneCharacter);
					waitingSceneCharacter.gameObject.SetActive(false);
					waitingSceneLoadCharacters.Remove(conn);
				}

				// remove connection from wait list if it's waiting for the scene server to load
				foreach (Dictionary<NetworkConnection, Character> waiting in waitingCharacters.Values)
				{
					if (waiting.TryGetValue(conn, out Character waitingCharacter))
					{
						Destroy(waitingCharacter);
						waitingCharacter.gameObject.SetActive(false);
						waiting.Remove(conn);
						break;
					}
				}

				if (connectionCharacters.TryGetValue(conn, out Character character))
				{
					if (character == null)
					{
						// character is missing.. socket is closed but we kick just incase
						conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
						return;
					}

					// tell the world server the character disconnected
					if (ClientManager != null)
					{
						ClientManager.Broadcast(new SceneCharacterDisconnectedBroadcast()
						{
							characterName = character.characterName,
						});
					}

					// remove the characterName->character entry
					characters.Remove(character.characterName);
					// remove the connection->character entry
					connectionCharacters.Remove(conn);

					// character becomes immortal on disconnect and mortal when fully loaded into the scene
					if (character.DamageController != null)
					{
						character.DamageController.immortal = true;
					}

					Debug.Log("[" + DateTime.UtcNow + "] " + character.characterName + " has been saved at: " + character.transform.position.ToString());

					character.RemoveOwnership();

					// save the character and set online to false
					using var dbContext = Server.DbContextFactory.CreateDbContext();
					CharacterService.SaveCharacter(dbContext, character, false);
					dbContext.SaveChanges();

					// immediately log out for now.. we could add a timeout later on..?
					ServerManager.Despawn(character.NetworkObject, DespawnType.Pool);
					character.gameObject.SetActive(false);
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

			if (Database.Instance.TryGetSelectedCharacterDetails(accountName, out string selectedCharacterName))
			{
				if (characters.ContainsKey(selectedCharacterName) ||
					waitingSceneLoadCharacters.ContainsKey(conn))
				{
					Debug.Log("[" + DateTime.UtcNow + "] " + selectedCharacterName + " is already loaded or loading. FIXME");

					// character load already started or complete
					conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
					return;
				}
				if (Database.Instance.TryLoadCharacter(selectedCharacterName, characterPrefabs, Server.NetworkManager, out Character character))
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
						Debug.Log("[" + DateTime.UtcNow + "] " + character.characterName + " has been enqueued for SceneLoad: " + character.sceneName);

						// if the scene isn't loaded yet put the connection into a wait queue
						if (waitingCharacters.TryGetValue(character.sceneName, out Dictionary<NetworkConnection, Character> waiting))
						{
							if (!waiting.ContainsKey(conn))
							{
								waiting.Add(conn, character);
							}
						}
						else
						{
							waitingCharacters.Add(character.sceneName, new Dictionary<NetworkConnection, Character>()
							{
								{ conn, character },
							});
						}
					}
				}
			}
		}

		/// <summary>
		/// Tell all the players the server finished loading the scene they were trying to enter.
		/// </summary>
		private void SceneManager_OnSceneLoadComplete(string sceneName)
		{
			if (waitingCharacters.TryGetValue(sceneName, out Dictionary<NetworkConnection, Character> characters))
			{
				foreach (Character character in characters.Values)
				{
					// check if the scene is valid, loaded, and cached properly
					if (SceneServerSystem.TryGetValidScene(character.sceneName, out SceneInstanceDetails instance))
					{
						Debug.Log("[" + DateTime.UtcNow + "] " + character.characterName + " is loading Scene: " + character.sceneName);

						if (SceneServerSystem.TryLoadSceneForConnection(character.Owner, instance))
						{
							// assign scene handle for later..
							character.sceneHandle = instance.handle;
						}
					}
				}
				characters.Clear();
				waitingCharacters.Remove(sceneName);
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
				if (characters.ContainsKey(character.characterName))
				{
					characters[character.characterName] = character;
				}
				else
				{
					characters.Add(character.characterName, character);
				}

				// set the character status to online
				if (AccountManager.GetAccountNameByConnection(conn, out string accountName))
				{
					// doesn't contain any important functionality yet.. we just do it for fun
					Database.Instance.TrySetCharacterOnline(accountName, character.characterName);
				}

				// tell the world server the character is active
				if (ClientManager != null)
				{
					ClientManager.Broadcast(new SceneCharacterConnectedBroadcast()
					{
						characterName = character.characterName,
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
		/// Handles changing a characters scene by telling the connection to reconnect to the world server.
		/// </summary>
		private void OnServerCharacterSceneChangeRequestReceived(NetworkConnection conn, CharacterSceneChangeRequestBroadcast msg)
		{
			if (worldSceneDetailsCache != null &&
				connectionCharacters.TryGetValue(conn, out Character character) &&
				worldSceneDetailsCache.scenes.TryGetValue(character.sceneName, out WorldSceneDetails details) &&
				details.teleporters.TryGetValue(msg.teleporterName, out SceneTeleporterDetails teleporter) &&
				msg.fromTeleporter == teleporter.from)
			{
				// should we prevent players from moving to a different scene if they are in combat?
				/*if (character.DamageController.Attackers.Count > 0)
				{
					return;
				}*/

				// remove ownership of the connections character
				character.RemoveOwnership();

				// make the character immortal for teleport
				if (character.DamageController != null)
				{
					character.DamageController.immortal = true;
				}

				character.sceneName = teleporter.toScene;
				character.transform.SetPositionAndRotation(teleporter.toPosition, character.transform.rotation);// teleporter.toRotation);

				// save the character with new scene and position
				using var dbContext = Server.DbContextFactory.CreateDbContext();
				CharacterService.SaveCharacter(dbContext, character, true);
				dbContext.SaveChanges();

				// tell the connection to reconnect to the world server for automatic re-entry?
				SceneWorldReconnectBroadcast sceneReconnect = new SceneWorldReconnectBroadcast()
				{
					address = Server.relayAddress,
					port = Server.relayPort,
				};

				conn.Broadcast(sceneReconnect);
			}
			else
			{
				// destination not found
			}
		}

		/// <summary>
		/// Allows sending a broadcast to a specific character by their character name.
		/// Returns true if the broadcast was sent successfully.
		/// False if the character could not by found.
		/// </summary>
		public bool SendBroadcastToCharacter<T>(string characterName, T msg) where T : struct, IBroadcast
		{
			if (characters.TryGetValue(characterName, out Character character))
			{
				character.Owner.Broadcast(msg);
				return true;
			}
			return false;
		}
	}
}