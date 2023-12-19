using UnityEngine;
#if UNITY_SERVER
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

		[Tooltip("Assign a SceneName if you wish to teleport to an entirely different scene.")]
		public string SceneName;
		[Tooltip("Assign a teleporter destination name and bake it into WorldSceneDetails. Ex Name: FromMyTeleporterDestination")]
		public string TeleporterDestinationName;
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

			// check if we are a scene teleporter
			if (sceneServerSystem != null &&
				sceneServerSystem.WorldSceneDetailsCache != null &&
				sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(SceneName, out WorldSceneDetails details) &&
				details.Teleporters.TryGetValue(TeleporterDestinationName, out SceneTeleporterDetails teleporter))
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

				// cache the current scene name
				string playerScene = character.SceneName;
				character.SceneName = SceneName;
				character.Motor.SetPositionAndRotationAndVelocity(teleporter.ToPosition, teleporter.ToRotation, Vector3.zero);

				// save the character with new scene and position
				using var dbContext = sceneServerSystem.Server.NpgsqlDbContextFactory.CreateDbContext();
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
						teleporterName = TeleporterDestinationName,
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
				Debug.Log(SceneName + " not found!");
			}
#endif
			return true;
		}
	}
}