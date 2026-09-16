using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.Spawner
{
	/// <summary>
	/// Every spawner of one world scene, baked out of the scene for the server.
	/// </summary>
	/// <remarks>
	/// Generated — never edit by hand. The baker rewrites it from the scene's
	/// <see cref="ObjectSpawner"/> components whenever the scene is saved, when a build starts, or
	/// from <c>FishMMO → Spawners → Bake Spawn Tables</c>. Referenced only from
	/// <see cref="SpawnTableCatalogue"/>, which lives in a server-only addressable group.
	/// </remarks>
	public class SceneSpawnTable : ScriptableObject
	{
		/// <summary>
		/// The name of the scene this table was baked from; the key a loading scene is matched by.
		/// </summary>
		public string SceneName;

		/// <summary>
		/// The scene's asset path when it was baked, for tooling.
		/// </summary>
		public string ScenePath;

		/// <summary>
		/// The scene's spawners, in a stable order. A <see cref="RespawnCondition"/> refers to its
		/// siblings by index into this list.
		/// </summary>
		public List<SpawnerDefinition> Spawners = new List<SpawnerDefinition>();
	}
}
