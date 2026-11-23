using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The habitat of the ground at a point: what the spawners ask before
	/// they place a creature. The World Simulation module installs <see cref="Source"/> from the scene's biome map; with none
	/// installed, or no map in the scene, every point answers <see cref="BiomeHabitat.None"/> and <see cref="HasMap"/> is
	/// false, so a spawner places its creatures anywhere, as it does in a scene with no biome map.
	/// </summary>
	public static class BiomeHabitatQuery
	{
		/// <summary>Answers whether the scene has a biome map covering the point, and the habitat there; installed by the module.</summary>
		public delegate bool HabitatSource(Scene scene, Vector3 position, out bool hasMap, out BiomeHabitat habitat);

		public static HabitatSource Source;

		/// <summary>
		/// The habitat at <paramref name="position"/>: false when the point is off the scene's map (or there is none);
		/// <paramref name="hasMap"/> says whether the scene has a map at all.
		/// </summary>
		public static bool TryGet(Scene scene, Vector3 position, out bool hasMap, out BiomeHabitat habitat)
		{
			if (Source == null)
			{
				hasMap = false;
				habitat = BiomeHabitat.None;
				return false;
			}
			return Source(scene, position, out hasMap, out habitat);
		}
	}
}
