using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public class ObjectSpawner : MonoBehaviour
	{
		[HideInInspector]
		public Transform Transform;

		[Tooltip("If true a random number will be selected within the minimum and maximum range provided. Otherwise the maximum respawn time will be used.")]
		public bool RandomRespawnTime = true;
		[Tooltip("If true a random spawn position will be picked inside of the bounding box using the current position as the center.")]
		public bool RandomSpawnPosition = true;
		public bool CheckTerrain = true;
		public Vector3 BoundingBoxSize = Vector3.one;
		[HideInInspector]
		public Vector3 BoundingBoxExtents = Vector3.one;
		[Tooltip("If true a random Spawnable object will be selected to spawn. Otherwise the list will be spawned in order.")]
		public bool RandomSpawnable = true;
		public List<BaseSpawnable> Spawnables = new List<BaseSpawnable>();

		private BaseSpawnable spawned = null;
		private float respawnTime = 0.0f;
		private int lastSpawnIndex = 0;

		void Awake()
		{
			Transform = transform;

			// Extents are always half of BoundingBoxSize
			BoundingBoxExtents = BoundingBoxSize * 0.5f;
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

		public void Despawn()
		{
			if (spawned != null)
			{

			}
			respawnTime = RandomRespawnTime ? Random.Range(spawned.MinimumRespawnTime, respawnTime) : spawned.MaximumRespawnTime;
		}

		public void TryRespawn()
		{
			if (respawnTime > 0.0f ||
				Spawnables == null ||
				Spawnables.Count < 1)
			{
				return;
			}

			// pick a random index or increment
			int spawnIndex = RandomSpawnable ? Random.Range(0, Spawnables.Count) : ++lastSpawnIndex;

			// if the spawn index is greater than the number of spawnables we reset to 0
			if (spawnIndex >= Spawnables.Count)
			{
				// reset index
				spawnIndex = 0;
			}

			BaseSpawnable spawnable = Spawnables[spawnIndex];
			if (spawnable == null)
			{
				return;
			}

			if (spawnable.Prefab == null)
			{
				return;
			}

			// cache the last spawned object template
			spawned = spawnable;

			Vector3 spawnPosition = Transform.position;

			if (RandomSpawnPosition)
			{
				// pick a random spawn position on top of the ground within the bounding box
				PhysicsScene physicsScene = gameObject.scene.GetPhysicsScene();
				if (physicsScene != null)
				{
					Vector3 bottomPoint = transform.position + new Vector3(Random.Range(-BoundingBoxSize.x, BoundingBoxSize.x),
																		   -BoundingBoxExtents.y,
																		   Random.Range(-BoundingBoxSize.z, BoundingBoxSize.z));

					Vector3 topPoint = new Vector3(bottomPoint.x,
												   BoundingBoxExtents.y,
												   bottomPoint.z);

					Vector3 direction = bottomPoint - topPoint;

					float distance = direction.magnitude;

					direction.Normalize();

					if (physicsScene.SphereCast(topPoint, 0.5f, direction, out RaycastHit hit, distance, Constants.Layers.Ground, QueryTriggerInteraction.Ignore))
					{
						spawnPosition = hit.point;
					}
				}
			}

			/*NetworkObject prefab = networkManager.SpawnablePrefabs.GetObject(true, dbCharacter.RaceID);

			if (prefab != null)
			{
				// instantiate the character object
				NetworkObject nob = networkManager.GetPooledInstantiated(prefab, spawnPosition, Transform.rotation, true);

				ServerManager.Spawn(nob, null, Transform.gameObject.scene);
			}*/
		}

		public Vector3 GetRandomPointInCube(Vector3 size)
		{
			return new Vector3(Random.Range(-size.x, size.x),
							   Random.Range(-size.y, size.y),
							   Random.Range(-size.z, size.z));
		}
	}
}