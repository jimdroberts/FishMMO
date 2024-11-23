using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Server
{
	// Character manager handles the players character
	public class PetSystem : ServerBehaviour
	{
		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				Server.RegisterBroadcast<PetFollowBroadcast>(OnPetFollowBroadcastReceived, true);
				Server.RegisterBroadcast<PetStayBroadcast>(OnPetStayBroadcastReceived, true);
				Server.RegisterBroadcast<PetSummonBroadcast>(OnPetSummonBroadcastReceived, true);
				Server.RegisterBroadcast<PetReleaseBroadcast>(OnPetReleaseBroadcastReceived, true);
				
				AbilityObject.OnPetSummon += AbilityObject_OnPetSummon;

				if (ServerBehaviour.TryGet(out CharacterSystem characterSystem))
				{
					characterSystem.OnSpawnCharacter += CharacterSystem_OnSpawnCharacter;
					characterSystem.OnDespawnCharacter += CharacterSystem_OnDespawnCharacter;
					characterSystem.OnPetKilled += CharacterSystem_OnPetKilled;
				}
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			if (ServerManager != null)
			{
				Server.UnregisterBroadcast<PetFollowBroadcast>(OnPetFollowBroadcastReceived);
				Server.UnregisterBroadcast<PetStayBroadcast>(OnPetStayBroadcastReceived);
				Server.UnregisterBroadcast<PetSummonBroadcast>(OnPetSummonBroadcastReceived);
				Server.UnregisterBroadcast<PetReleaseBroadcast>(OnPetReleaseBroadcastReceived);

				AbilityObject.OnPetSummon -= AbilityObject_OnPetSummon;

				if (ServerBehaviour.TryGet(out CharacterSystem characterSystem))
				{
					characterSystem.OnSpawnCharacter -= CharacterSystem_OnSpawnCharacter;
					characterSystem.OnDespawnCharacter -= CharacterSystem_OnDespawnCharacter;
					characterSystem.OnPetKilled -= CharacterSystem_OnPetKilled;
				}
			}
		}

		private void OnPetFollowBroadcastReceived(NetworkConnection conn, PetFollowBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}

			IPetController petController = conn.FirstObject.GetComponent<IPetController>();
			if (petController == null || petController.Pet == null)
			{
				// no pet exists
				return;
			}

			if (petController.Pet.TryGet(out IAIController aiController))
			{
				aiController.Home = petController.Character.Transform.position;
				aiController.Target = petController.Character.Transform;
			}
		}

		private void OnPetStayBroadcastReceived(NetworkConnection conn, PetStayBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}

			IPetController petController = conn.FirstObject.GetComponent<IPetController>();
			if (petController == null || petController.Pet == null)
			{
				// no pet exists
				return;
			}

			if (petController.Pet.TryGet(out IAIController aiController))
			{
				aiController.Home = petController.Pet.Transform.position;
				aiController.Target = null;
			}
		}

		private void OnPetSummonBroadcastReceived(NetworkConnection conn, PetSummonBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}

			IPetController petController = conn.FirstObject.GetComponent<IPetController>();
			if (petController == null || petController.Pet == null)
			{
				// no pet exists
				return;
			}

			if (petController.Pet.TryGet(out IAIController aiController))
			{
				aiController.Agent.Warp(petController.Character.Transform.position);
			}
		}

		private void OnPetReleaseBroadcastReceived(NetworkConnection conn, PetReleaseBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}

			IPetController petController = conn.FirstObject.GetComponent<IPetController>();
			if (petController == null || petController.Pet == null)
			{
				// no pet exists
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

			if (dbContext == null)
			{
				return;
			}

			CharacterPetService.Save(dbContext, petController.Character, false);

			if (petController.Pet != null &&
				petController.Pet.NetworkObject.IsSpawned)
			{
				ServerManager.Despawn(petController.Pet.NetworkObject, DespawnType.Pool);
			}
			petController.Pet.PetOwner = null;
			petController.Pet = null;

			Server.Broadcast(conn, new PetRemoveBroadcast(), true, Channel.Reliable);
		}

		private void CharacterSystem_OnSpawnCharacter(NetworkConnection conn, IPlayerCharacter character, Scene scene)
		{
			if (character == null)
			{
				return;
			}

			if (scene == null)
			{
				return;
			}

			if (!character.TryGet(out IPetController petController))
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

			if (dbContext == null)
			{
				return;
			}

			if (!CharacterPetService.TryLoad(dbContext, character, out Pet pet))
			{
				return;
			}

			pet.PetOwner = character;
			petController.Pet = pet;

			if (pet.TryGet(out IAIController aiController))
			{
				// Initialize AI Controller
				aiController.Initialize(Vector3.zero);
				aiController.Target = character.Transform;
			}

			UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(pet.GameObject, character.GameObject.scene);

			// Ensure the game object is active, pooled objects are disabled
			pet.GameObject.SetActive(true);

			ServerManager.Spawn(pet.GameObject, character.NetworkObject.Owner, character.GameObject.scene);

			if (pet.TryGet(out IFactionController petFactionController))
			{
				if (character.TryGet(out IFactionController casterFactionController))
				{
					petFactionController.CopyFrom(casterFactionController);
				}
			}

			Server.Broadcast(conn, new PetAddBroadcast() { ID = pet.ID }, true, Channel.Reliable);
		}

		private void CharacterSystem_OnDespawnCharacter(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}

			if (!character.TryGet(out IPetController petController))
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

			if (dbContext == null)
			{
				return;
			}

			float currentHealth = 0.0f;
			if (petController.Pet != null &&
				petController.Pet.TryGet(out ICharacterAttributeController petAttributeController) &&
				petAttributeController.TryGetHealthAttribute(out CharacterResourceAttribute health))
			{
				currentHealth = health.CurrentValue;
			}

			CharacterPetService.Save(dbContext, character, petController.Pet != null && currentHealth > 0.0f);

			if (petController.Pet != null &&
				petController.Pet.NetworkObject.IsSpawned)
			{
				ServerManager.Despawn(petController.Pet.NetworkObject, DespawnType.Pool);
			}
		}

		private void CharacterSystem_OnPetKilled(NetworkConnection conn, IPlayerCharacter character)
		{
			CharacterSystem_OnDespawnCharacter(conn, character);

			Server.Broadcast(conn, new PetRemoveBroadcast(), true, Channel.Reliable);
		}

		private void AbilityObject_OnPetSummon(PetAbilityTemplate petAbilityTemplate, IPlayerCharacter caster)
		{
			if (petAbilityTemplate == null)
			{
				return;
			}

			if (!caster.TryGet(out IPetController petController))
			{
				return;
			}

			if (petAbilityTemplate.PetPrefab == null)
			{
				return;
			}

			PhysicsScene physicsScene = caster.GameObject.scene.GetPhysicsScene();
			if (physicsScene == null)
			{
				return;
			}

			// get a random point at the top of the bounding box
			Vector3 origin = new Vector3(UnityEngine.Random.Range(-petAbilityTemplate.SpawnBoundingBox.x, petAbilityTemplate.SpawnBoundingBox.x),
										 petAbilityTemplate.SpawnBoundingBox.y,
										 UnityEngine.Random.Range(-petAbilityTemplate.SpawnBoundingBox.z, petAbilityTemplate.SpawnBoundingBox.z));

			Vector3 spawnPosition = caster.Transform.position;

			// add the spawner position
			origin += spawnPosition;

			if (physicsScene.SphereCast(origin, petAbilityTemplate.SpawnDistance, Vector3.down, out RaycastHit hit, 20.0f, 1 << Constants.Layers.Ground, QueryTriggerInteraction.Ignore))
			{
				spawnPosition = hit.point;
			}

			NetworkObject nob = Server.NetworkManager.GetPooledInstantiated(petAbilityTemplate.PetPrefab, spawnPosition, caster.Transform.rotation, true);
			Pet pet = nob.GetComponent<Pet>();
			if (pet == null)
			{
				//throw exception
				return;
			}
			pet.PetOwner = caster;
			pet.PetAbilityTemplate = petAbilityTemplate;
			petController.Pet = pet;

			if (pet.TryGet(out IAIController aiController))
			{
				// Initialize AI Controller
				aiController.Initialize(spawnPosition);
				aiController.Target = caster.Transform;
			}

			UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(nob.gameObject, caster.GameObject.scene);

			// Ensure the game object is active, pooled objects are disabled
			pet.GameObject.SetActive(true);

			ServerManager.Spawn(nob.gameObject, caster.NetworkObject.Owner, caster.GameObject.scene);

			if (pet.TryGet(out IFactionController petFactionController))
			{
				if (caster.TryGet(out IFactionController casterFactionController))
				{
					petFactionController.CopyFrom(casterFactionController);
				}
			}

			Server.Broadcast(caster.Owner, new PetAddBroadcast() { ID = pet.ID }, true, Channel.Reliable);
		}
	}
}