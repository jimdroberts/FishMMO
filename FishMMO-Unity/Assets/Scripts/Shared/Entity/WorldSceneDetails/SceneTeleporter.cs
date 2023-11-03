using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
#if UNITY_SERVER
using FishMMO.Server;
using FishMMO.Server.DatabaseServices;
using FishMMO.Database.Entities;
#endif

namespace FishMMO.Shared
{
	public class SceneTeleporter : MonoBehaviour
	{
#if UNITY_SERVER
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
				if (character != null && !character.IsTeleporting)
				{
					if (sceneServerSystem.WorldSceneDetailsCache != null &&
						sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out WorldSceneDetails details) &&
						details.Teleporters.TryGetValue(gameObject.name, out SceneTeleporterDetails teleporter))
					{
						character.IsTeleporting = true;

						// should we prevent players from moving to a different scene if they are in combat?
						/*if (character.DamageController.Attackers.Count > 0)
						{
							return;
						}*/

						// make the character immortal for teleport
						if (character.DamageController != null)
						{
							character.DamageController.Immortal = true;
						}

						string playerScene = character.SceneName;
						character.SceneName = teleporter.ToScene;
						character.Motor.SetPositionAndRotation(teleporter.ToPosition, character.Transform.rotation);// teleporter.toRotation);

						// save the character with new scene and position
						using var dbContext = sceneServerSystem.Server.DbContextFactory.CreateDbContext();
						CharacterService.Save(dbContext, character, false);
						dbContext.SaveChanges();

						Debug.Log(character.CharacterName + " has been saved at: " + character.Transform.position.ToString());

						WorldServerEntity worldServer = WorldServerService.GetServer(dbContext, character.WorldServerID);
						if (worldServer != null)
						{
							// tell the client to reconnect to the world server for automatic re-entry
							character.Owner.Broadcast(new SceneWorldReconnectBroadcast()
							{
								address = worldServer.Address,
								port = worldServer.Port,
								teleporterName = gameObject.name,
								sceneName = playerScene
							}, true, Channel.Reliable);
						}
						else
						{
							// world not found?
							character.Owner.Kick(FishNet.Managing.Server.KickReason.UnexpectedProblem);
						}

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
					character.IsTeleporting = false;
				}
			}
		}
#endif
	}
}