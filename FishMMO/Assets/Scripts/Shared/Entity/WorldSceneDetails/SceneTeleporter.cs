using FishNet.Object;
using Server.Services;
using Server;
using UnityEngine;
using System;

public class SceneTeleporter : NetworkBehaviour
{
	public CharacterSystem CharacterSystem;

	public override void OnStartNetwork()
	{
		base.OnStartNetwork();

		if (!IsServer)
		{
			enabled = false;
		}

		if (CharacterSystem == null)
		{
			CharacterSystem = FindObjectOfType<CharacterSystem>();
		}
	}

	void OnTriggerEnter(Collider other)
	{
		if (other != null && other.gameObject != null && CharacterSystem != null)
		{
			Character character = other.gameObject.GetComponent<Character>();
			if (character != null && !character.isTeleporting)
			{
				if (CharacterSystem.worldSceneDetailsCache != null &&
					CharacterSystem.worldSceneDetailsCache.scenes.TryGetValue(character.sceneName, out WorldSceneDetails details) &&
					details.teleporters.TryGetValue(gameObject.name, out SceneTeleporterDetails teleporter) &&
					gameObject.name.Equals(teleporter.from))
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

					// remove ownership of the connections character
					character.RemoveOwnership();

					character.sceneName = teleporter.toScene;
					character.transform.SetPositionAndRotation(teleporter.toPosition, character.transform.rotation);// teleporter.toRotation);

					Debug.Log("[" + DateTime.UtcNow + "] " + character.characterName + " has been saved at: " + character.transform.position.ToString());

					// save the character with new scene and position
					using var dbContext = CharacterSystem.Server.DbContextFactory.CreateDbContext();
					CharacterService.SaveCharacter(dbContext, character, true);
					dbContext.SaveChanges();

					ServerManager.Despawn(character.NetworkObject, DespawnType.Pool);
					character.gameObject.SetActive(false);

					// tell the client to reconnect to the world server for automatic re-entry
					character.Owner.Broadcast(new SceneWorldReconnectBroadcast()
					{
						address = CharacterSystem.Server.relayAddress,
						port = CharacterSystem.Server.relayPort,
					});
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