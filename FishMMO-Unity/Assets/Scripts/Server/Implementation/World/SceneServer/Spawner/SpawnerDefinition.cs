using System;
using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.Spawner
{
	/// <summary>
	/// One spawner as the server runs it: the authored values of an <see cref="ObjectSpawner"/>,
	/// plus where it stood, with no reference back to the scene.
	/// </summary>
	/// <remarks>
	/// Baked from the scene into a <see cref="SceneSpawnTable"/>. Spawners are stripped from every
	/// shipped scene, so this copy is the only one a running server — or a client, which never gets
	/// it — can see. See <see cref="ObjectSpawner"/> for what each value means.
	/// </remarks>
	[Serializable]
	public class SpawnerDefinition
	{
		/// <summary>The authoring GameObject's name, for logs.</summary>
		public string Name;

		/// <summary>World position of the spawner; the centre of its spawn area.</summary>
		public Vector3 Position;

		/// <summary>World rotation of the spawner; spawned objects face this way.</summary>
		public Quaternion Rotation = Quaternion.identity;

		/// <summary>See <see cref="ObjectSpawner.OrConditions"/>.</summary>
		[SerializeReference, SubclassSelector]
		public List<RespawnCondition> OrConditions = new List<RespawnCondition>();

		/// <summary>See <see cref="ObjectSpawner.TrueConditions"/>.</summary>
		[SerializeReference, SubclassSelector]
		public List<RespawnCondition> TrueConditions = new List<RespawnCondition>();

		/// <summary>See <see cref="ObjectSpawner.InitialRespawnTime"/>.</summary>
		public float InitialRespawnTime;

		/// <summary>See <see cref="ObjectSpawner.InitialSpawnCount"/>.</summary>
		public int InitialSpawnCount;

		/// <summary>See <see cref="ObjectSpawner.MaxSpawnCount"/>.</summary>
		public int MaxSpawnCount = 1;

		/// <summary>See <see cref="ObjectSpawner.UniqueSpawnables"/>.</summary>
		public bool UniqueSpawnables;

		/// <summary>See <see cref="ObjectSpawner.SpawnType"/>.</summary>
		public ObjectSpawnType SpawnType = ObjectSpawnType.Linear;

		/// <summary>See <see cref="ObjectSpawner.PrewarmPool"/>.</summary>
		public bool PrewarmPool = true;

		/// <summary>See <see cref="ObjectSpawner.PrewarmHeadroom"/>.</summary>
		public int PrewarmHeadroom = 1;

		/// <summary>See <see cref="ObjectSpawner.RandomRespawnTime"/>.</summary>
		public bool RandomRespawnTime = true;

		/// <summary>See <see cref="ObjectSpawner.RespawnCheckIntervalMinimum"/>.</summary>
		public float RespawnCheckIntervalMinimum = 3.0f;

		/// <summary>See <see cref="ObjectSpawner.RespawnCheckIntervalMaximum"/>.</summary>
		public float RespawnCheckIntervalMaximum = 6.0f;

		/// <summary>See <see cref="ObjectSpawner.RandomSpawnPosition"/>.</summary>
		public bool RandomSpawnPosition = true;

		/// <summary>See <see cref="ObjectSpawner.SphereRadius"/>.</summary>
		public float SphereRadius = 0.5f;

		/// <summary>See <see cref="ObjectSpawner.BoundingBoxSize"/>.</summary>
		public Vector3 BoundingBoxSize = Vector3.one;

		/// <summary>See <see cref="ObjectSpawner.Spawnables"/>.</summary>
		[SerializeReference, SubclassSelector]
		public List<SpawnableSettings> Spawnables = new List<SpawnableSettings>();

		/// <summary>
		/// See <see cref="ObjectSpawner.Pack"/>. A table baked before packs existed has none, and
		/// deserializes a disabled block, so its spawners run exactly as they did.
		/// </summary>
		public NPCPackSettings Pack = new NPCPackSettings();

		/// <summary>Half of <see cref="BoundingBoxSize"/>.</summary>
		public Vector3 BoundingBoxExtents => BoundingBoxSize * 0.5f;
	}
}
