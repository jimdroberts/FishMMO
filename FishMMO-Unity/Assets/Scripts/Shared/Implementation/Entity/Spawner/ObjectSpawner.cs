using FishNet.Object;
using FishNet.Utility.Performance;
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages spawning and respawning of networked objects in the game world. Supports various spawn types, respawn conditions, and object pooling.
	/// </summary>
	/// <remarks>
	/// Dedicated Server builds strip shaders/fonts and may leave SerializeReference entries or prefab refs null.
	/// All spawn-list / weight paths must tolerate null entries so scene load can complete and reach SceneStatus.Ready.
	/// </remarks>
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
		/// Supports polymorphic subclasses via <see cref="SerializeReference"/> for type-specific data injection.
		/// </summary>
		[SerializeReference, SubclassSelector]
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
		/// Exceptions are caught so a single bad spawner cannot prevent SceneServer from marking the scene Ready.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			try
			{
				if (!base.IsServerStarted)
				{
					enabled = false;
					return;
				}

				// Compact null / broken SerializeReference entries (common after dedicated-server strip).
				SanitizeSpawnables();

				if (Spawnables == null || Spawnables.Count < 1)
				{
					Log.Warning("ObjectSpawner",
						$"'{name}' has no valid spawnables after sanitize — disabling (scene continues without this spawner).");
					enabled = false;
					return;
				}

				Transform = transform;
				if (Transform == null)
				{
					Log.Warning("ObjectSpawner", $"'{name}' transform is null — disabling.");
					enabled = false;
					return;
				}

				// Extents are always half of BoundingBoxSize
				BoundingBoxExtents = BoundingBoxSize * 0.5f;

				// Runtime validate: refresh YOffset etc. but NEVER clear NetworkObject refs
				// (allowClearInvalidRefs: false). Headless/dedicated builds were wiping every
				// entry via GetIsSpawnable/strip failures → empty NPCSpawner/OrcSpawner.
				for (int i = 0; i < Spawnables.Count; ++i)
				{
					try
					{
						Spawnables[i]?.OnValidate(allowClearInvalidRefs: false);
					}
					catch (Exception ex)
					{
						Log.Warning("ObjectSpawner",
							$"'{name}' Spawnables[{i}].OnValidate failed: {ex.Message}");
					}
				}

				// Only drop true nulls / missing prefab refs — not "not spawnable" flags.
				SanitizeSpawnables();
				if (Spawnables == null || Spawnables.Count < 1)
				{
					Log.Warning("ObjectSpawner",
						$"'{name}' has no valid spawnables after sanitize — disabling " +
						"(check SerializeReference NetworkObject assignments on this spawner).");
					enabled = false;
					return;
				}

				IsCacheDirty = true;
				lastSpawnIndex = 0;

				InitialSpawnCount = InitialSpawnCount.Clamp(0, MaxSpawnCount);
				for (int i = 0; i < InitialSpawnCount; ++i)
				{
					try
					{
						SpawnObject();
					}
					catch (Exception ex)
					{
						Log.Warning("ObjectSpawner",
							$"'{name}' initial SpawnObject[{i}] failed: {ex.Message}");
					}
				}

				for (int i = Spawned.Count; i < MaxSpawnCount; ++i)
				{
					SpawnableSettings spawnableSettings = GetSpawnableSettingsSafe(GetSpawnIndex());
					if (spawnableSettings == null)
					{
						continue;
					}

					DateTime respawnTime = GetNextRespawnTime(spawnableSettings);
					SpawnableRespawnTimers.Add(respawnTime);
				}
			}
			catch (Exception ex)
			{
				// Never let spawner init abort scene network start / Ready registration.
				Log.Error("ObjectSpawner",
					$"'{name}' OnStartNetwork failed (spawner disabled; scene load continues): {ex}");
				enabled = false;
			}
		}

		/// <summary>
		/// Removes null entries and entries whose NetworkObject prefab is missing.
		/// Mutates <see cref="Spawnables"/> in place.
		/// </summary>
		private void SanitizeSpawnables()
		{
			if (Spawnables == null)
			{
				return;
			}

			int write = 0;
			for (int read = 0; read < Spawnables.Count; ++read)
			{
				SpawnableSettings entry = Spawnables[read];
				if (entry == null)
				{
					continue;
				}
				// NetworkObject may be missing when client-only prefabs were stripped or never assigned.
				if (entry.NetworkObject == null)
				{
					continue;
				}
				if (write != read)
				{
					Spawnables[write] = entry;
				}
				write++;
			}

			if (write < Spawnables.Count)
			{
				Spawnables.RemoveRange(write, Spawnables.Count - write);
				IsCacheDirty = true;
			}
		}

		/// <summary>
		/// Called every frame. Attempts to respawn objects if conditions and timers are met.
		/// </summary>
		void Update()
		{
			try
			{
				TryRespawn();
			}
			catch (Exception ex)
			{
				Log.Warning("ObjectSpawner", $"'{name}' TryRespawn failed: {ex.Message}");
			}
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
				ColliderExtensions.DrawCenterMarker(transform.position, GizmoColor);
			}
		}
#endif

		/// <summary>
		/// Despawns the specified spawnable object, schedules its respawn, and removes it from the spawned dictionary.
		/// </summary>
		/// <param name="spawnable">The spawnable object to despawn.</param>
		public void Despawn(ISpawnable spawnable)
		{
			if (spawnable == null)
			{
				return;
			}

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
			if (spawnable.NetworkObject != null)
			{
				ServerManager?.Despawn(spawnable.NetworkObject, DespawnType.Pool);
			}
		}

		/// <summary>
		/// Calculates the next respawn time for a spawnable object based on its settings and spawner configuration.
		/// </summary>
		/// <param name="spawnableSettings">The settings for the spawnable object.</param>
		/// <returns>The DateTime when the object should respawn.</returns>
		private DateTime GetNextRespawnTime(SpawnableSettings spawnableSettings)
		{
			if (spawnableSettings == null)
			{
				return DateTime.UtcNow.Add(TimeSpan.FromSeconds(Mathf.Max(0f, InitialRespawnTime)));
			}

			// Calculate the next respawn time based on a random respawn time or the initial respawn time.
			float min = Mathf.Max(0f, spawnableSettings.MinimumRespawnTime);
			float max = Mathf.Max(min, spawnableSettings.MaximumRespawnTime);
			TimeSpan respawnDelay = RandomRespawnTime
				? TimeSpan.FromSeconds(DeterministicRNG.Shared.Range(min, max))
				: TimeSpan.FromSeconds(Mathf.Max(0f, InitialRespawnTime));

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

			// Check if any respawn time has elapsed.
			for (int i = 0; i < SpawnableRespawnTimers.Count; ++i)
			{
				DateTime respawnTime = SpawnableRespawnTimers[i];

				if (DateTime.UtcNow >= respawnTime)
				{
					bool shouldRespawn = true;

					// Check OR respawn conditions (any one must be true to allow respawn).
					if (OrConditions != null &&
						OrConditions.Count >= 1)
					{
						shouldRespawn = false;
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

					// Check AND respawn conditions (all must be true to allow respawn).
					if (shouldRespawn &&
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

						if (SpawnableRespawnTimers.Count > 0 && i < SpawnableRespawnTimers.Count)
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
		/// Null spawnable entries are skipped (do not throw).
		/// </summary>
		private void UpdateTotalSpawnChanceCache()
		{
			if (Spawnables == null || Spawnables.Count < 1 || !IsCacheDirty)
			{
				return;
			}

			cachedTotalSpawnChance = 0f;
			foreach (var spawnableSettings in Spawnables)
			{
				if (spawnableSettings == null)
				{
					continue;
				}
				cachedTotalSpawnChance += Mathf.Max(0f, spawnableSettings.SpawnChance);
			}
			IsCacheDirty = false;
		}

		/// <summary>
		/// Selects a spawnable index based on weighted random selection using spawn chances.
		/// </summary>
		/// <returns>The index of the selected spawnable, or 0 if none are usable.</returns>
		private int GetWeightedSpawnIndex()
		{
			if (Spawnables == null || Spawnables.Count < 1)
			{
				return 0;
			}

			UpdateTotalSpawnChanceCache();

			// No usable weights — fall back to first non-null entry.
			if (cachedTotalSpawnChance <= 0f)
			{
				return GetFirstValidSpawnIndex();
			}

			// Pick a random value between 0 and total spawn chance.
			float randomValue = DeterministicRNG.Shared.Range(0f, cachedTotalSpawnChance);

			float cumulativeChance = 0f;

			// Iterate through the spawnables and select one based on the random value.
			for (int i = 0; i < Spawnables.Count; ++i)
			{
				SpawnableSettings settings = Spawnables[i];
				if (settings == null)
				{
					continue;
				}

				cumulativeChance += Mathf.Max(0f, settings.SpawnChance);

				// If the random value is less than the cumulative chance, select this spawnable.
				if (randomValue <= cumulativeChance)
				{
					return i;
				}
			}
			// In case something goes wrong, return the first valid spawnable as a fallback.
			return GetFirstValidSpawnIndex();
		}

		/// <summary>
		/// Returns the first index with a non-null spawnable entry, or 0.
		/// </summary>
		private int GetFirstValidSpawnIndex()
		{
			if (Spawnables == null)
			{
				return 0;
			}
			for (int i = 0; i < Spawnables.Count; ++i)
			{
				if (Spawnables[i] != null && Spawnables[i].NetworkObject != null)
				{
					return i;
				}
			}
			return 0;
		}

		/// <summary>
		/// Safe indexer for <see cref="Spawnables"/> that returns null instead of throwing.
		/// </summary>
		private SpawnableSettings GetSpawnableSettingsSafe(int index)
		{
			if (Spawnables == null || index < 0 || index >= Spawnables.Count)
			{
				return null;
			}
			return Spawnables[index];
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
					// Skip nulls so linear mode does not spin forever on empty slots.
					int attempts = Spawnables.Count;
					spawnIndex = lastSpawnIndex;
					while (attempts-- > 0)
					{
						if (spawnIndex < 0 || spawnIndex >= Spawnables.Count)
						{
							spawnIndex = 0;
						}
						if (Spawnables[spawnIndex] != null && Spawnables[spawnIndex].NetworkObject != null)
						{
							lastSpawnIndex = spawnIndex + 1;
							if (lastSpawnIndex >= Spawnables.Count)
							{
								lastSpawnIndex = 0;
							}
							return spawnIndex;
						}
						spawnIndex++;
						if (spawnIndex >= Spawnables.Count)
						{
							spawnIndex = 0;
						}
					}
					return GetFirstValidSpawnIndex();
				case ObjectSpawnType.Random:
					// Prefer valid entries; fall back to any index in range.
					for (int attempt = 0; attempt < Spawnables.Count; ++attempt)
					{
						spawnIndex = DeterministicRNG.Shared.Range(0, Spawnables.Count);
						if (Spawnables[spawnIndex] != null && Spawnables[spawnIndex].NetworkObject != null)
						{
							return spawnIndex;
						}
					}
					return GetFirstValidSpawnIndex();
				case ObjectSpawnType.Weighted:
					spawnIndex = GetWeightedSpawnIndex();
					break;
				default:
					return GetFirstValidSpawnIndex();
			}
			// If the spawn index is greater than the number of spawnables, reset to first valid.
			if (spawnIndex < 0 || spawnIndex >= Spawnables.Count)
			{
				return GetFirstValidSpawnIndex();
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

			if (NetworkManager == null || ServerManager == null)
			{
				Log.Warning("ObjectSpawner",
					$"'{name}' SpawnObject skipped — NetworkManager/ServerManager not ready.");
				return;
			}

			if (Transform == null)
			{
				Transform = transform;
				if (Transform == null)
				{
					return;
				}
			}

			SpawnableSettings spawnableSettings = GetSpawnableSettingsSafe(GetSpawnIndex());
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
				// PhysicsScene is a struct — always valid; use IsValid when available via scene.
				PhysicsScene physicsScene = gameObject.scene.GetPhysicsScene();
				// Get a random point at the top of the bounding box.
				Vector3 origin = new Vector3(DeterministicRNG.Shared.Range(-BoundingBoxExtents.x, BoundingBoxExtents.x),
											 BoundingBoxExtents.y,
											 DeterministicRNG.Shared.Range(-BoundingBoxExtents.z, BoundingBoxExtents.z));

				// Add the spawner position.
				origin += spawnPosition;

				if (physicsScene.SphereCast(origin, SphereRadius, Vector3.down, out RaycastHit hit, BoundingBoxSize.y, Constants.Layers.Obstruction, QueryTriggerInteraction.Ignore))
				{
					spawnPosition = hit.point;
					spawnPosition.y += spawnableSettings.YOffset;
				}
			}

			// Prefab lookup — SpawnablePrefabs can be null if DefaultPrefabObjects failed to load.
			if (NetworkManager.SpawnablePrefabs == null)
			{
				Log.Warning("ObjectSpawner",
					$"'{name}' SpawnObject skipped — NetworkManager.SpawnablePrefabs is null.");
				return;
			}

			NetworkObject settingsNob = spawnableSettings.NetworkObject;
			NetworkObject prefab = NetworkManager.SpawnablePrefabs.GetObject(true, settingsNob.PrefabId);

			if (prefab == null)
			{
				Log.Warning("ObjectSpawner",
					$"'{name}' SpawnObject skipped — prefabId={settingsNob.PrefabId} not in SpawnablePrefabs " +
					$"(dedicated server may be missing addressable/prefab collection entry).");
				return;
			}

			// Instantiate the object using object pooling.
			NetworkObject nob = NetworkManager.GetPooledInstantiated(
				settingsNob.PrefabId,
				settingsNob.SpawnableCollectionId,
				ObjectPoolRetrieveOption.MakeActive,
				null,
				spawnPosition,
				Transform.rotation,
				null,
				true);
			if (nob == null)
			{
				return;
			}

			try
			{
				// Delegate type-specific data injection to the settings subclass.
				spawnableSettings.OnSpawned(nob, this);

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
					// Guard against duplicate keys if pool reuse reuses IDs unexpectedly.
					Spawned[nobSpawnable.ID] = nobSpawnable;
				}

				// Spawn the object on the server.
				ServerManager.Spawn(nob, null, Transform.gameObject.scene);

				// If we've reached the maximum spawn count, clear respawn timers.
				if (Spawned.Count >= MaxSpawnCount)
				{
					SpawnableRespawnTimers.Clear();
				}
			}
			catch (Exception ex)
			{
				Log.Warning("ObjectSpawner",
					$"'{name}' SpawnObject post-instantiate failed for prefabId={settingsNob.PrefabId}: {ex.Message}");
				// Best-effort cleanup of the pooled instance so it is not left half-initialized.
				try
				{
					ServerManager?.Despawn(nob, DespawnType.Pool);
				}
				catch
				{
					// ignored — already logging primary failure
				}
			}
		}
	}
}
