using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(NetworkObject))]
	public class ObjectSpawner : NetworkBehaviour
	{
		[HideInInspector]
		public Transform Transform;

		/// <summary>
		/// If any of these conditions return true the object will respawn. This list is iterated first.
		/// </summary>
		public List<BaseRespawnCondition> OrConditions = new List<BaseRespawnCondition>();

		/// <summary>
		/// All conditions must return true for the object to respawn. This list is iterated second.
		/// </summary>
		public List<BaseRespawnCondition> TrueConditions = new List<BaseRespawnCondition>();

		public float InitialRespawnTime = 0.0f;
		[Tooltip("If true a random number will be selected within the minimum and maximum range provided. Otherwise the maximum respawn time will be used.")]
		public bool RandomRespawnTime = true;
		[Tooltip("If true a random prefab will be instantiated during the next respawn.")]
		public bool RandomSpawnable = true;
		public List<SpawnableSettings> Spawnables;
		[Tooltip("The maximum number of objects that can be spawned by this spawner.")]
		public int MaxSpawnCount = 1;
		public Dictionary<long, ISpawnable> Spawned = new Dictionary<long, ISpawnable>();
		[Tooltip("If true a random spawn position will be picked inside of the bounding box using the current position as the center.")]
		public bool RandomSpawnPosition = true;
		[Tooltip("SphereCast radius used for spawning objects in the world.")]
		public float SphereRadius = 0.5f;
		public Vector3 BoundingBoxSize = Vector3.one;
		[HideInInspector]
		public Vector3 BoundingBoxExtents = Vector3.one;

		private float respawnTime = 0.0f;
		private int lastSpawnIndex = 0;

		void Awake()
		{
			Transform = transform;
			respawnTime = InitialRespawnTime;

			// Extents are always half of BoundingBoxSize
			BoundingBoxExtents = BoundingBoxSize * 0.5f;

			// Adjust spawnable Y height offset
			for (int i = 0; i < Spawnables.Count; ++i)
			{
				Spawnables[i].OnValidate();
			}
		}

		void Update()
		{
			if (!base.IsServerStarted)
			{
				enabled = false;
				return;
			}
			TryRespawn();

			respawnTime -= Time.deltaTime;
		}

#if !UNITY_SERVER
		public override void OnStartClient()
		{
			base.OnStartClient();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}
		}
#endif

#if UNITY_EDITOR
		public Color GizmoColor = Color.red;

		void OnDrawGizmos()
		{
			Collider collider = gameObject.GetComponent<Collider>();
			if (collider != null)
			{
				collider.DrawGizmo(GizmoColor);
			}
			else
			{
				Gizmos.color = GizmoColor;
				Gizmos.DrawWireCube(transform.position, BoundingBoxSize);
			}
		}
#endif

		public void Despawn(ISpawnable spawnable)
		{
			//Debug.Log($"Despawning {spawnable.NetworkObject.name}");

			Spawned.Remove(spawnable.ID);

			// did we already set a previous respawn time?
			if (respawnTime > 0)
			{
				return;
			}
			// set the next respawn time
			respawnTime = RandomRespawnTime ? Random.Range(spawnable.SpawnableSettings.MinimumRespawnTime, spawnable.SpawnableSettings.MaximumRespawnTime) : InitialRespawnTime;

			spawnable.ObjectSpawner = null;
			spawnable.SpawnableSettings = null;

			// despawn the object
			ServerManager?.Despawn(spawnable.NetworkObject, DespawnType.Pool);

			//Debug.Log($"Object despawned, next respawn at {respawnTime}.");
		}

		public void TryRespawn()
		{
			if (respawnTime > 0.0f ||
				Spawnables == null ||
				Spawnables.Count < 1)
			{
				return;
			}

			if (Spawned.Count >= MaxSpawnCount)
			{
				return;
			}

			bool shouldRespawn = false;

			if (OrConditions != null &&
				OrConditions.Count >= 1)
			{
				foreach (BaseRespawnCondition condition in OrConditions)
				{
					if (condition == null)
					{
						continue;
					}
					if (condition.OnCheckCondition(this))
					{
						shouldRespawn = true;
						break;
					}
				}
			}

			if (!shouldRespawn &&
				TrueConditions != null &&
				TrueConditions.Count >= 1)
			{
				foreach (BaseRespawnCondition condition in TrueConditions)
				{
					if (condition == null)
					{
						continue;
					}
					if (!condition.OnCheckCondition(this))
					{
						return;
					}
				}
			}

			// pick a random index or increment
			int spawnIndex;
			if (RandomSpawnable)
			{
				spawnIndex = Random.Range(0, Spawnables.Count);
			}
			else
			{
				spawnIndex = lastSpawnIndex;
				++lastSpawnIndex;
			}

			// if the spawn index is greater than the number of spawnables we reset to 0
			if (spawnIndex >= Spawnables.Count)
			{
				// reset index
				spawnIndex = 0;
			}

			SpawnableSettings spawnable = Spawnables[spawnIndex];
			if (spawnable == null ||
				spawnable.NetworkObject == null)
			{
				return;
			}

			// calculate spawn position
			Vector3 spawnPosition = Transform.position;
			if (RandomSpawnPosition)
			{
				// pick a random spawn position on top of the ground within the bounding box
				PhysicsScene physicsScene = gameObject.scene.GetPhysicsScene();
				if (physicsScene != null)
				{
					// get a random point at the top of the bounding box
					Vector3 origin = new Vector3(Random.Range(-BoundingBoxExtents.x, BoundingBoxExtents.x),
												 BoundingBoxExtents.y,
												 Random.Range(-BoundingBoxExtents.z, BoundingBoxExtents.z));

					// add the spawner position
					origin += spawnPosition;

					if (physicsScene.SphereCast(origin, SphereRadius, Vector3.down, out RaycastHit hit, BoundingBoxSize.y, Constants.Layers.Obstruction, QueryTriggerInteraction.Ignore))
					{
						spawnPosition = hit.point;
						spawnPosition.y += spawnable.YOffset;
					}
				}
			}

			// spawn the object server side
			if (base.IsServerStarted)
			{
				NetworkObject prefab = NetworkManager.SpawnablePrefabs.GetObject(true, spawnable.NetworkObject.PrefabId);

				if (prefab != null)
				{
					// instantiate the character object
					NetworkObject nob = NetworkManager.GetPooledInstantiated(prefab, spawnPosition, Transform.rotation, true);

					if (nob == null)
					{
						return;
					}

					UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(nob.gameObject, this.gameObject.scene);

					IAIController aiController = nob.GetComponent<IAIController>();
					if (aiController != null)
					{
						aiController.Initialize(spawnPosition);
					}

					ISpawnable nobSpawnable = nob.GetComponent<ISpawnable>();
					if (nobSpawnable != null)
					{
						nobSpawnable.ObjectSpawner = this;
						nobSpawnable.SpawnableSettings = spawnable;
						Spawned.Add(nobSpawnable.ID, nobSpawnable);

						//Debug.Log($"ISpawnable found.");
					}

					ServerManager.Spawn(nob, null, Transform.gameObject.scene);

					//Debug.Log($"Spawned Count: {Spawned.Count}");

					if (Spawned.Count < MaxSpawnCount)
					{
						// set the next respawn time
						respawnTime = RandomRespawnTime ? Random.Range(spawnable.MinimumRespawnTime, spawnable.MaximumRespawnTime) : InitialRespawnTime;

						//Debug.Log($"Respawn time is updating, next respawn in {respawnTime}s");
					}
					//Debug.Log($"Spawned {nob.gameObject.name} at {System.DateTime.UtcNow}");
				}
			}
		}
	}
}