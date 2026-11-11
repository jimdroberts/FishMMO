using FishMMO.Shared;
using FishMMO.Shared.Atlas;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// Which way the planet's north lies in a scene, for the maps' compasses.
	/// </summary>
	/// <remarks>
	/// The maps are drawn with the scene's +Z up and call that "world north" (marker facings, <see cref="MapViewTransform"/>),
	/// but a scene sits on its planet turned by its atlas heading (<see cref="WorldAtlasScene.HeadingDegrees"/>: which way +Z
	/// faces, clockwise from north), so the planet's north is somewhere else on the map. This gives it as a bearing in the
	/// maps' own terms, degrees clockwise from +Z, measured where the player stands (<see cref="AtlasGeometry.NorthBearing"/>:
	/// near a pole it turns across a scene). A scene with no atlas entry keeps +Z as north.
	/// </remarks>
	public static class TrueNorth
	{
		private static Scene cachedScene;
		private static WorldAtlasScene cachedEntry;
		private static bool cachedKnown;

		/// <summary>Degrees clockwise from the scene's +Z to the planet's north at a position in a scene; 0 where unknown.</summary>
		public static float BearingAt(Scene scene, Vector3 position)
		{
			WorldAtlasScene entry = EntryFor(scene);
			if (entry == null)
			{
				return 0f;
			}
			return AtlasGeometry.NorthBearing(AtlasFootprint.Of(entry), position.x / 1000.0, position.z / 1000.0, entry.CutRadiusKm);
		}

		/// <summary>The scene's placed atlas entry, looked up once per scene (it is fixed for a scene's life).</summary>
		private static WorldAtlasScene EntryFor(Scene scene)
		{
			if (cachedKnown && cachedScene == scene)
			{
				return cachedEntry;
			}
			// Not remembered until the scene's settings are found: they register as the scene loads, maybe after a map asks.
			if (!scene.IsValid() || !WorldSceneSettings.TryGetForScene(scene, out WorldSceneSettings settings) || settings == null)
			{
				return null;
			}
			WorldAtlasScene entry = settings.AtlasEntry;
			cachedScene = scene;
			cachedKnown = true;
			cachedEntry = entry != null && entry.Placed ? entry : null;
			return cachedEntry;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			cachedScene = default;
			cachedEntry = null;
			cachedKnown = false;
		}
	}
}
