using FishNet.Object;
using FishNet.Utility.Performance;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages spawning and respawning of networked objects in the game world. Supports various spawn types, respawn conditions, and object pooling.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public class ObjectSpawner : NetworkBehaviour
	{
		/// <summary>
		/// Cached reference to the spawner's transform.
		/// </summary>
		[HideInInspector]
		public Transform Transform;

		/// <summary>
		/// If any of these conditions return true, the object will respawn. This list is checked first (logical OR).
		/// </summary>
		public List<BaseRespawnCondition> OrConditions = new List<BaseRespawnCondition>();

		/// <summary>
		/// All conditions must return true for the object to respawn. This list is checked second (logical AND).
		/// </summary>
		public List<BaseRespawnCondition> TrueConditions = new List<BaseRespawnCondition>();

		/// <summary>
		/// Flag to track if the spawn chance cache needs updating.
		/// </summary>
		[HideInInspector]
		public bool IsCacheDirty = true;

		/// <summary>
		/// The initial respawn time (in seconds) for spawned objects.
		/// </summary>
		public float InitialRespawnTime = 0.0f;

		/// <summary>
		/// The number of objects to spawn initially when the spawner starts.
		/// </summary>
		public int InitialSpawnCount = 0;

		/// <summary>
		/// The maximum number of objects that can be spawned by this spawner.
		/// </summary>
		[Tooltip("The maximum number of objects that can be spawned by this spawner.")]
		public int MaxSpawnCount = 1;

		/// <summary>
		/// The type of spawn selection (Linear, Random, Weighted).
		/// </summary>
		public ObjectSpawnType SpawnType = ObjectSpawnType.Linear;

		/// <summary>
		/// If true, a random respawn time is selected within the minimum and maximum range. Otherwise, the initial respawn time is used.
		/// </summary>
		[Tooltip("If true a random number will be selected within the minimum and maximum range provided. Otherwise the maximum respawn time will be used.")]
		public bool RandomRespawnTime = true;

		/// <summary>
		/// If true, a random spawn position is picked inside the bounding box using the current position as the center.
		/// </summary>
		[Tooltip("If true a random spawn position will be picked inside of the bounding box using the current position as the center.")]
		public bool RandomSpawnPosition = true;

		/// <summary>
		/// SphereCast radius used for spawning objects in the world.
		/// </summary>
		[Tooltip("SphereCast radius used for spawning objects in the world.")]
		public float SphereRadius = 0.5f;

		/// <summary>
		/// The size of the bounding box used for random spawn position selection.
		/// </summary>
		public Vector3 BoundingBoxSize = Vector3.one;

		/// <summary>
		/// The extents (half-size) of the bounding box, calculated from BoundingBoxSize.
		/// </summary>
		[HideInInspector]
		public Vector3 BoundingBoxExtents = Vector3.one;

		/// <summary>
		/// The list of spawnable settings used to configure each spawnable object.
		/// </summary>
		public List<SpawnableSettings> Spawnables;

		/// <summary>
		/// Dictionary of currently spawned objects, keyed by their unique ID.
		/// </summary>
		public Dictionary<long, ISpawnable> Spawned = new Dictionary<long, ISpawnable>();

		/// <summary>
		/// List of respawn timers for each spawnable object.
		/// </summary>
		public List<DateTime> SpawnableRespawnTimers = new List<DateTime>();

		/// <summary>
		/// Internal index for linear spawn selection.
		/// </summary>
		private int lastSpawnIndex = 0;

		/// <summary>
		/// Cached total spawn chance for weighted spawn selection.
		/// </summary>
		private float cachedTotalSpawnChance = 0f;

		/// <summary>
		/// Called when the network starts. Initializes spawner, validates spawnables, and spawns initial objects.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			if (!base.IsServerStarted || Spawnables == null || Spawnables.Count < 1)
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

				// Get a new respawn time for each spawnable
				DateTime respawnTime = GetNextRespawnTime(spawnableSettings);

				// Add a new respawn time
				SpawnableRespawnTimers.Add(respawnTime);
			}
		}

		/// <summary>
		/// Called every frame. Attempts to respawn objects if conditions and timers are met.
		/// </summary>
		void Update()
		{
			TryRespawn();
		}

#if UNITY_EDITOR
		/// <summary>
		/// The color used to draw the spawner's gizmo in the editor.
		/// </summary>
		public Color GizmoColor = Color.red;

		/// <summary>
		/// Draws the spawner's bounding box or collider gizmo in the editor for visualization.
		/// </summary>
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

		/// <summary>
		/// Despawns the specified spawnable object, schedules its respawn, and removes it from the spawned dictionary.
		/// </summary>
		/// <param name="spawnable">The spawnable object to despawn.</param>
		public void Despawn(ISpawnable spawnable)
		{
			// Remove the spawnable from the spawned dictionary.
			Spawned.Remove(spawnable.ID);

			// Get a new respawn time for the object.
			DateTime respawnTime = GetNextRespawnTime(spawnable.SpawnableSettings);

			// Add a new respawn time to the timers list.
			SpawnableRespawnTimers.Add(respawnTime);

			// Clear references to the spawner and settings.
			spawnable.ObjectSpawner = null;
			spawnable.SpawnableSettings = null;

			// Despawn the object using the server manager and object pool.
			ServerManager?.Despawn(spawnable.NetworkObject, DespawnType.Pool);
		}

		/// <summary>
		/// Calculates the next respawn time for a spawnable object based on its settings and spawner configuration.
		/// </summary>
		/// <param name="spawnableSettings">The settings for the spawnable object.</param>
		/// <returns>The DateTime when the object should respawn.</returns>
		private DateTime GetNextRespawnTime(SpawnableSettings spawnableSettings)
		{
			// Calculate the next respawn time based on a random respawn time or the initial respawn time.
			TimeSpan respawnDelay = RandomRespawnTime
				? TimeSpan.FromSeconds(UnityEngine.Random.Range(spawnableSettings.MinimumRespawnTime, spawnableSettings.MaximumRespawnTime))
				: TimeSpan.FromSeconds(InitialRespawnTime);

			// Return the DateTime of when the object should respawn.
			return DateTime.UtcNow.Add(respawnDelay);
		}

		/// <summary>
		/// Attempts to respawn objects if their timers have elapsed and respawn conditions are met.
		/// </summary>
		public void TryRespawn()
		{
			if (Spawnables == null ||
				Spawnables.Count < 1 ||
				SpawnableRespawnTimers.Count < 1)
			{
				return;
			}

			// Clear the spawnable timers if we reach our maximum spawn count.
			if (Spawned.Count >= MaxSpawnCount)
			{
				SpawnableRespawnTimers.Clear();
				return;
			}

			bool shouldRespawn = true;

			// Check if any respawn time has elapsed.
			for (int i = 0; i < SpawnableRespawnTimers.Count; ++i)
			{
				DateTime respawnTime = SpawnableRespawnTimers[i];

				if (DateTime.UtcNow >= respawnTime)
				{
					// Check OR respawn conditions (any must be true).
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

					// Check AND respawn conditions (all must be true).
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

					// If all respawn conditions are met, spawn the object and remove its timer.
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

		/// <summary>
		/// Updates the cached total spawn chance for weighted spawn selection. Only recalculates if cache is dirty.
		/// </summary>
		private void UpdateTotalSpawnChanceCache()
		{
			if (Spawnables != null && Spawnables.Count > 0 && IsCacheDirty)
			{
				cachedTotalSpawnChance = 0f;
				foreach (var spawnableSettings in Spawnables)
				{
					cachedTotalSpawnChance += spawnableSettings.SpawnChance;
				}
				IsCacheDirty = false;
			}
		}

		/// <summary>
		/// Selects a spawnable index based on weighted random selection using spawn chances.
		/// </summary>
		/// <returns>The index of the selected spawnable.</returns>
		private int GetWeightedSpawnIndex()
		{
			if (Spawnables == null || Spawnables.Count < 1)
			{
				return 0;
			}

			UpdateTotalSpawnChanceCache();

			// Pick a random value between 0 and total spawn chance.
			float randomValue = UnityEngine.Random.Range(0f, cachedTotalSpawnChance);

			float cumulativeChance = 0f;

			// Iterate through the spawnables and select one based on the random value.
			for (int i = 0; i < Spawnables.Count; ++i)
			{
				cumulativeChance += Spawnables[i].SpawnChance;

				// If the random value is less than the cumulative chance, select this spawnable.
				if (randomValue <= cumulativeChance)
				{
					return i; // Return the index of the selected spawnable.
				}
			}
			// In case something goes wrong, return the first spawnable as a fallback.
			return 0;
		}

		/// <summary>
		/// Gets the index of the next spawnable to use, based on the configured spawn type.
		/// </summary>
		/// <returns>The index of the selected spawnable.</returns>
		public int GetSpawnIndex()
		{
			if (Spawnables == null || Spawnables.Count < 1)
			{
				return 0;
			}

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
			// If the spawn index is greater than the number of spawnables, reset to 0.
			if (spawnIndex >= Spawnables.Count)
			{
				spawnIndex = 0;
			}
			return spawnIndex;
		}

		/// <summary>
		/// Spawns a new object in the world using the selected spawnable settings and position logic.
		/// </summary>
		public void SpawnObject()
		{
			if (Spawnables == null || Spawnables.Count < 1)
			{
				return;
			}

			SpawnableSettings spawnableSettings = Spawnables[GetSpawnIndex()];
			if (spawnableSettings == null ||
				spawnableSettings.NetworkObject == null)
			{
				return;
			}

			// Calculate spawn position.
			Vector3 spawnPosition = Transform.position;
			if (RandomSpawnPosition)
			{
				// Pick a random spawn position on top of the ground within the bounding box.
				PhysicsScene physicsScene = gameObject.scene.GetPhysicsScene();
				if (physicsScene != null)
				{
					// Get a random point at the top of the bounding box.
					Vector3 origin = new Vector3(UnityEngine.Random.Range(-BoundingBoxExtents.x, BoundingBoxExtents.x),
												 BoundingBoxExtents.y,
												 UnityEngine.Random.Range(-BoundingBoxExtents.z, BoundingBoxExtents.z));

					// Add the spawner position.
					origin += spawnPosition;

					if (physicsScene.SphereCast(origin, SphereRadius, Vector3.down, out RaycastHit hit, BoundingBoxSize.y, Constants.Layers.Obstruction, QueryTriggerInteraction.Ignore))
					{
						spawnPosition = hit.point;
						spawnPosition.y += spawnableSettings.YOffset;
					}
				}
			}

			// Get the prefab for the network object from the spawnable settings.
			NetworkObject prefab = NetworkManager.SpawnablePrefabs.GetObject(true, spawnableSettings.NetworkObject.PrefabId);

			if (prefab != null)
			{
				// Instantiate the object using object pooling.
				NetworkObject nob = NetworkManager.GetPooledInstantiated(spawnableSettings.NetworkObject.PrefabId, spawnableSettings.NetworkObject.SpawnableCollectionId, ObjectPoolRetrieveOption.MakeActive, null, spawnPosition, Transform.rotation, null, true);
				if (nob == null)
				{
					return;
				}

				// Move the spawned object to the correct scene.
				UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(nob.gameObject, this.gameObject.scene);

				// Initialize AI controller if present.
				IAIController aiController = nob.GetComponent<IAIController>();
				if (aiController != null)
				{
					aiController.Initialize(spawnPosition);
				}

				// Set up spawnable references and add to the spawned dictionary.
				ISpawnable nobSpawnable = nob.GetComponent<ISpawnable>();
				if (nobSpawnable != null)
				{
					nobSpawnable.ObjectSpawner = this;
					nobSpawnable.SpawnableSettings = spawnableSettings;
					Spawned.Add(nobSpawnable.ID, nobSpawnable);
				}

				// Spawn the object on the server.
				ServerManager.Spawn(nob, null, Transform.gameObject.scene);

				// If we've reached the maximum spawn count, clear respawn timers.
				if (Spawned.Count >= MaxSpawnCount)
				{
					SpawnableRespawnTimers.Clear();
				}
			}
		}
	}
}