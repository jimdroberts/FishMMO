using FishMMO.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// Which way the planet's north lies in a scene, for the maps' compasses.
	/// </summary>
	/// <remarks>
	/// The maps are drawn with the scene's +Z up and call that "world north" (marker facings, the Map module's <c>MapViewTransform</c>),
	/// but a scene sits on its planet turned by its atlas heading (which way +Z faces, clockwise from north), so the planet's
	/// north is somewhere else on the map. This gives it as a bearing in the maps' own terms, degrees clockwise from +Z,
	/// measured where the player stands (<see cref="IWorldSceneEntry.NorthBearingAt"/>: near a pole it turns across a
	/// scene). A scene with no world entry (no world design installed, or a scene not in it) keeps +Z as north.
	/// </remarks>
	public static class TrueNorth
	{
		private static Scene cachedScene;
		private static IWorldSceneEntry cachedEntry;
		private static bool cachedKnown;

		/// <summary>Degrees clockwise from the scene's +Z to the planet's north at a position in a scene; 0 where unknown.</summary>
		public static float BearingAt(Scene scene, Vector3 position)
		{
			IWorldSceneEntry entry = EntryFor(scene);
			return entry != null ? entry.NorthBearingAt(position) : 0f;
		}

		/// <summary>The scene's world entry, looked up once per scene (it is fixed for a scene's life).</summary>
		private static IWorldSceneEntry EntryFor(Scene scene)
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
			cachedScene = scene;
			cachedKnown = true;
			cachedEntry = settings.Entry;
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
