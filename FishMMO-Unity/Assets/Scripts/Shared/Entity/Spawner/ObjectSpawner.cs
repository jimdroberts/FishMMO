using FishNet.Object;
using System;
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

		[HideInInspector]
		public bool IsCacheDirty = true;  // Flag to track if the cache needs updating
		public float InitialRespawnTime = 0.0f;
		public int InitialSpawnCount = 0;
		[Tooltip("The maximum number of objects that can be spawned by this spawner.")]
		public int MaxSpawnCount = 1;
		[Tooltip("If true a random number will be selected within the minimum and maximum range provided. Otherwise the maximum respawn time will be used.")]
		public bool RandomRespawnTime = true;
		public ObjectSpawnType SpawnType = ObjectSpawnType.Linear;
		[Tooltip("If true a random spawn position will be picked inside of the bounding box using the current position as the center.")]
		public bool RandomSpawnPosition = true;
		[Tooltip("SphereCast radius used for spawning objects in the world.")]
		public float SphereRadius = 0.5f;
		public Vector3 BoundingBoxSize = Vector3.one;
		[HideInInspector]
		public Vector3 BoundingBoxExtents = Vector3.one;
		public List<SpawnableSettings> Spawnables;
		public Dictionary<long, ISpawnable> Spawned = new Dictionary<long, ISpawnable>();
		public List<DateTime> SpawnableRespawnTimers = new List<DateTime>();

		private int lastSpawnIndex = 0;
		private float cachedTotalSpawnChance = 0f;

		public override void OnStartNetwork()
        {
            base.OnStartNetwork();

			if (!base.IsServerStarted)
			{
				enabled = false;
				return;
			}

			Transform = transform;

			// Extents are always half of BoundingBoxSize
			BoundingBoxExtents = BoundingBoxSize * 0.5f;

			// Validate spawnables
			for (int i = 0; i < Spawnables.Count; ++i)
			{
				Spawnables[i].OnValidate();
			}

			InitialSpawnCount = InitialSpawnCount.Clamp(0, MaxSpawnCount);
			for (int i = 0; i < InitialSpawnCount; ++i)
			{
				SpawnObject();
			}
			for (int i = Spawned.Count; i < MaxSpawnCount; ++i)
			{
				SpawnableSettings spawnableSettings = Spawnables[GetSpawnIndex()];
				if (spawnableSettings == null)
				{
					continue;
				}

				// Get a new respawn time
				DateTime respawnTime = GetNextRespawnTime(spawnableSettings);

				//Debug.Log($"{i} Added new respawn for {respawnTime}");

				// Add a new respawn time
				SpawnableRespawnTimers.Add(respawnTime);
			}
        }

        void Update()
		{
			TryRespawn();
		}

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

			// Get a new respawn time
    		DateTime respawnTime = GetNextRespawnTime(spawnable.SpawnableSettings);

			// Add a new respawn time
			SpawnableRespawnTimers.Add(respawnTime);

			spawnable.ObjectSpawner = null;
			spawnable.SpawnableSettings = null;

			// despawn the object
			ServerManager?.Despawn(spawnable.NetworkObject, DespawnType.Pool);

			//Debug.Log($"Object despawned, added new respawn for {respawnTime}");
		}

		private DateTime GetNextRespawnTime(SpawnableSettings spawnableSettings)
		{
			// Calculate the next respawn time based on a random respawn time or the initial respawn time
			TimeSpan respawnDelay = RandomRespawnTime
				? TimeSpan.FromSeconds(UnityEngine.Random.Range(spawnableSettings.MinimumRespawnTime, spawnableSettings.MaximumRespawnTime))
				: TimeSpan.FromSeconds(InitialRespawnTime);

			// Return the DateTime of when the object should respawn
			return DateTime.UtcNow.Add(respawnDelay);
		}

		public void TryRespawn()
		{
			if (Spawnables == null ||
				Spawnables.Count < 1 ||
				SpawnableRespawnTimers.Count < 1)
			{
				return;
			}

			// Clear the spawnable timers if we reach our maximum spawn count
			if (Spawned.Count >= MaxSpawnCount)
			{
				SpawnableRespawnTimers.Clear();
				return;
			}

			bool shouldRespawn = true;

			// Check if any respawn time has elapsed
			for (int i = 0; i < SpawnableRespawnTimers.Count; ++i)
			{
				DateTime respawnTime = SpawnableRespawnTimers[i];

				if (DateTime.UtcNow >= respawnTime)
				{
					// Check respawn conditions here (e.g., OrConditions and TrueConditions)
					if (OrConditions != null &&
						OrConditions.Count >= 1)
					{
						foreach (BaseRespawnCondition condition in OrConditions)
						{
							if (condition == null)
							{
								continue;
							}
							if (!condition.OnCheckCondition(this))
							{
								shouldRespawn = false;
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
								shouldRespawn = false;
								break;
							}
						}
					}

					if (shouldRespawn)
					{
						SpawnObject();

						if (SpawnableRespawnTimers.Count > 0)
						{
							SpawnableRespawnTimers.RemoveAt(i);
						}
						return;
					}
				}
			}
		}

		private void UpdateTotalSpawnChanceCache()
		{
			if (IsCacheDirty)
			{
				cachedTotalSpawnChance = 0f;
				foreach (var spawnableSettings in Spawnables)
				{
					cachedTotalSpawnChance += spawnableSettings.SpawnChance;
				}
				IsCacheDirty = false;
			}
		}

		private int GetWeightedSpawnIndex()
		{
			UpdateTotalSpawnChanceCache();

			// Pick a random value between 0 and TotalSpawnChance
			float randomValue = UnityEngine.Random.Range(0f, cachedTotalSpawnChance);

			float cumulativeChance = 0f;

			// Iterate through the spawnables and select one based on the random value
			for (int i = 0; i < Spawnables.Count; ++i)
			{
				cumulativeChance += Spawnables[i].SpawnChance;

				// If the random value is less than the cumulative chance, select this spawnable
				if (randomValue <= cumulativeChance)
				{
					return i; // Return the index of the selected spawnable
				}
			}
			// In case something goes wrong, return the first spawnable as a fallback
			return 0;
		}

		public int GetSpawnIndex()
		{
			int spawnIndex;
			switch (SpawnType)
			{
				case ObjectSpawnType.Linear:
					spawnIndex = lastSpawnIndex;
					++lastSpawnIndex;
					if (lastSpawnIndex >= Spawnables.Count)
					{
						lastSpawnIndex = 0;
					}
					break;
				case ObjectSpawnType.Random:
					spawnIndex = UnityEngine.Random.Range(0, Spawnables.Count);
					break;
				case ObjectSpawnType.Weighted:
					spawnIndex = GetWeightedSpawnIndex();
					break;
				default:
					return 0;
			}
			// If the spawn index is greater than the number of spawnables we reset to 0
			if (spawnIndex >= Spawnables.Count)
			{
				spawnIndex = 0;
			}
			return spawnIndex;
		}

		public void SpawnObject()
		{
			SpawnableSettings spawnableSettings = Spawnables[GetSpawnIndex()];
			if (spawnableSettings == null ||
				spawnableSettings.NetworkObject == null)
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
					Vector3 origin = new Vector3(UnityEngine.Random.Range(-BoundingBoxExtents.x, BoundingBoxExtents.x),
												 BoundingBoxExtents.y,
												 UnityEngine.Random.Range(-BoundingBoxExtents.z, BoundingBoxExtents.z));

					// add the spawner position
					origin += spawnPosition;

					if (physicsScene.SphereCast(origin, SphereRadius, Vector3.down, out RaycastHit hit, BoundingBoxSize.y, Constants.Layers.Obstruction, QueryTriggerInteraction.Ignore))
					{
						spawnPosition = hit.point;
						spawnPosition.y += spawnableSettings.YOffset;
					}
				}
			}

			NetworkObject prefab = NetworkManager.SpawnablePrefabs.GetObject(true, spawnableSettings.NetworkObject.PrefabId);

			if (prefab != null)
			{
				// instantiate the an object
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
					nobSpawnable.SpawnableSettings = spawnableSettings;
					Spawned.Add(nobSpawnable.ID, nobSpawnable);

					//Debug.Log($"ISpawnable found.");
				}

				ServerManager.Spawn(nob, null, Transform.gameObject.scene);

				//Debug.Log($"Spawned Count: {Spawned.Count}");

				if (Spawned.Count >= MaxSpawnCount)
				{
					SpawnableRespawnTimers.Clear();
				}

				//Debug.Log($"Spawned {nob.gameObject.name} at {DateTime.UtcNow}");
			}
		}
	}
}