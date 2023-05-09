using FishNet.Object;
using Server.Services;
using Server;
using UnityEngine;
using System;

public class SceneTeleporter : NetworkBehaviour
{
	private SceneServerSystem sceneServerSystem;

	void Awake()
	{
		if (sceneServerSystem == null)
		{
			sceneServerSystem = ServerBehaviour.Get<SceneServerSystem>();
		}
	}

	void OnTriggerEnter(Collider other)
	{
		if (other != null && other.gameObject != null && sceneServerSystem != null)
		{
			Character character = other.gameObject.GetComponent<Character>();
			if (character != null && !character.isTeleporting)
			{
				if (sceneServerSystem.worldSceneDetailsCache != null &&
					sceneServerSystem.worldSceneDetailsCache.scenes.TryGetValue(character.sceneName, out WorldSceneDetails details) &&
					details.teleporters.TryGetValue(gameObject.name, out SceneTeleporterDetails teleporter))
				{
					character.isTeleporting = true;

					// should we prevent players from moving to a different scene if they are in combat?
					/*if (character.DamageController.Attackers.Count > 0)
					{
						return;
					}*/

					// make the character immortal for teleport
					if (character.DamageController != null)
					{
						character.DamageController.immortal = true;
					}

					character.sceneName = teleporter.toScene;
					character.Motor.SetPositionAndRotation(teleporter.toPosition, character.transform.rotation);// teleporter.toRotation);

					// save the character with new scene and position
					using var dbContext = sceneServerSystem.Server.DbContextFactory.CreateDbContext();
					CharacterService.SaveCharacter(dbContext, character, false);
					dbContext.SaveChanges();

					Debug.Log("[" + DateTime.UtcNow + "] " + character.characterName + " has been saved at: " + character.transform.position.ToString());

					// tell the client to reconnect to the world server for automatic re-entry
					character.Owner.Broadcast(new SceneWorldReconnectBroadcast()
					{
						address = sceneServerSystem.Server.relayAddress,
						port = sceneServerSystem.Server.relayPort,
					});

					sceneServerSystem.ServerManager.Despawn(character.NetworkObject, DespawnType.Pool);
				}
				else
				{
					// destination not found
					return;
				}
			}
		}
	}

	void OnTriggerExit(Collider other)
	{
		if (other != null && other.gameObject != null)
		{
			Character character = other.gameObject.GetComponent<Character>();
			if (character != null)
			{
				character.isTeleporting = false;
			}
		}
	}
}