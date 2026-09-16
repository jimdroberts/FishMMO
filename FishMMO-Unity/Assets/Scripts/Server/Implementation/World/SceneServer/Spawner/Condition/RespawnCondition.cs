using System;

namespace FishMMO.Server.Implementation.World.SceneServer.Spawner
{
	/// <summary>
	/// A rule a spawner must satisfy before it may respawn anything.
	/// </summary>
	/// <remarks>
	/// Plain serialized data, held by <see cref="UnityEngine.SerializeReference"/> on the
	/// <see cref="ObjectSpawner"/> authoring component and baked into the scene's
	/// <see cref="SceneSpawnTable"/>. It used to be a scene component, which cannot survive the
	/// bake: spawners are stripped from shipped scenes, and a condition that referenced other scene
	/// objects would have pointed at nothing.
	/// </remarks>
	[Serializable]
	public abstract class RespawnCondition
	{
		/// <summary>
		/// Resolves authoring-time references into the baked table. Called by the baker on the
		/// table's copy of <paramref name="authored"/>.
		/// </summary>
		/// <remarks>
		/// References are read from <paramref name="authored"/>, not from this copy: a serialized
		/// copy cannot be trusted to carry references to scene objects.
		/// </remarks>
		/// <param name="authored">The condition as it is authored on the scene's spawner.</param>
		/// <param name="resolveSpawnerIndex">Maps an authoring spawner in the same scene to its
		/// index in the table, or -1 when it is not in the table.</param>
		public virtual void Bake(RespawnCondition authored, Func<ObjectSpawner, int> resolveSpawnerIndex) { }

		/// <summary>
		/// Checks whether the condition allows <paramref name="spawner"/> to respawn now.
		/// </summary>
		/// <param name="spawner">The spawner asking.</param>
		/// <returns>True if respawn is allowed.</returns>
		public abstract bool OnCheckCondition(SpawnerRuntime spawner);
	}
}
