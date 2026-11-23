using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// Where an NPC can get out of the weather: the nearest shelter volume's
	/// bounds. The World Simulation module installs <see cref="Source"/> from its weather volumes; with none installed there is
	/// no shelter anywhere, and an NPC that would seek one stays where it is.
	/// </summary>
	public static class ShelterQuery
	{
		/// <summary>The nearest shelter of at least the strength within reach of a point; installed by the module.</summary>
		public delegate bool NearestSource(Scene scene, Vector3 from, float maxDistance, float minimumStrength, out Bounds bounds);

		public static NearestSource Source;

		/// <summary>The bounds of the nearest shelter within <paramref name="maxDistance"/> of <paramref name="from"/> that shelters
		/// at least <paramref name="minimumStrength"/>; false when there is none.</summary>
		public static bool TryNearest(Scene scene, Vector3 from, float maxDistance, float minimumStrength, out Bounds bounds)
		{
			if (Source == null)
			{
				bounds = default;
				return false;
			}
			return Source(scene, from, maxDistance, minimumStrength, out bounds);
		}
	}
}
