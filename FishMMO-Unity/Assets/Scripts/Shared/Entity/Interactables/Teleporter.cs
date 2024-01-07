using UnityEngine;
#if UNITY_SERVER
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using FishMMO.Server;
using FishMMO.Server.DatabaseServices;
using FishMMO.Database.Npgsql.Entities;
#endif

namespace FishMMO.Shared
{
	public class Teleporter : Interactable
	{
		public Transform Target;

#if UNITY_SERVER
		private SceneServerSystem sceneServerSystem;

		public override void OnStarting()
		{
			if (sceneServerSystem == null)
			{
				sceneServerSystem = ServerBehaviour.Get<SceneServerSystem>();
			}
		}
#endif

		public override bool OnInteract(Character character)
		{
			if (!base.OnInteract(character))
			{
				return false;
			}

#if UNITY_SERVER
			if (character.IsTeleporting)
			{
				return false;
			}

			if (Target != null)
			{

				// move the character
				character.Motor.SetPositionAndRotationAndVelocity(Target.position, Target.rotation, Vector3.zero);
				return true;
			}

			if (sceneServerSystem == null)
			{
				Debug.Log("SceneServerSystem not found!");
				return false;
			}

			if (sceneServerSystem.WorldSceneDetailsCache == null)
			{
				Debug.Log("SceneServerSystem: World Scene Details Cache not found!");
				return false;
			}

			// cache the current scene name
			string playerScene = character.SceneName;

			if (!sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(playerScene, out WorldSceneDetails details))
			{
				Debug.Log(playerScene + " not found!");
				return false;
			}

			// check if we are a scene teleporter
			if (!details.Teleporters.TryGetValue(gameObject.name, out SceneTeleporterDetails teleporter))
			{
				Debug.Log("Teleporter: " + gameObject.name + " not found!");
				return false;
			}

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

			// update scene instance details
			if (sceneServerSystem.TryGetSceneInstanceDetails(character.WorldServerID,
															 playerScene,
															 character.SceneHandle,
															 out SceneInstanceDetails instance))
			{
				--instance.CharacterCount;
			}

			character.SceneName = teleporter.ToScene;
			character.Motor.Transform.SetPositionAndRotation(teleporter.ToPosition, teleporter.ToRotation);

			// save the character with new scene and position
			using var dbContext = sceneServerSystem.Server.NpgsqlDbContextFactory.CreateDbContext();
			CharacterService.Save(dbContext, character, false);
			dbContext.SaveChanges();

			NetworkConnection conn = character.Owner;
			long worldServerId = character.WorldServerID;

			sceneServerSystem.ServerManager.Despawn(character.NetworkObject, DespawnType.Pool);

			WorldServerEntity worldServer = WorldServerService.GetServer(dbContext, worldServerId);
			if (worldServer != null)
			{
				// tell the client to reconnect to the world server for automatic re-entry
				conn.Broadcast(new SceneWorldReconnectBroadcast()
				{
					address = worldServer.Address,
					port = worldServer.Port,
					sceneName = playerScene,
					teleporterName = gameObject.name,
				}, true, Channel.Reliable);
			}
			else
			{
				// world not found?
				conn.Kick(FishNet.Managing.Server.KickReason.UnexpectedProblem);
			}
#endif
			return true;
		}
	}
}