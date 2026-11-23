using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared
{
	/// <summary>
	/// A scene's place in the world, as the world modules read it: its atlas entry, the body it stands on, where on it, and
	/// its time of day.
	/// </summary>
	/// <remarks>
	/// The world atlas's extension of the core's <see cref="WorldSceneSettings"/>:
	/// these were properties of the component, and are the same reads, so <c>settings.Body()</c> is what
	/// <c>settings.Body</c> was. The core itself sees only <see cref="WorldSceneSettings.Entry"/>.
	/// </remarks>
	public static class WorldSceneAtlas
	{
		/// <summary>This scene's world atlas entry, or null when it has not been added to the atlas.</summary>
		public static WorldAtlasScene AtlasEntry(this WorldSceneSettings settings)
		{
			return settings != null ? WorldAtlasScene.Find(settings.SceneName) : null;
		}

		/// <summary>
		/// The planet or moon this scene stands on. Null means the home world. Its rotation and sun
		/// give the scene its time of day; its distance from the star and its atmosphere shift the
		/// climate and decide whether weather exists at all.
		/// </summary>
		public static WorldBody Body(this WorldSceneSettings settings)
		{
			WorldAtlasScene entry = settings.AtlasEntry();
			return entry != null ? entry.Body : null;
		}

		/// <summary>Latitude the sun path and auroras use, in degrees.</summary>
		public static float Latitude(this WorldSceneSettings settings)
		{
			WorldAtlasScene entry = settings.AtlasEntry();
			return entry != null ? (float)entry.EffectiveSunLatitude : 0f;
		}

		/// <summary>
		/// The latitude the scene's CLIMATE is read at, degrees: its true place on the atlas. An entry may move the sun's
		/// path for a look (<see cref="WorldAtlasScene.OverrideSunLatitude"/>); that must not move how cold the place is.
		/// </summary>
		public static float ClimateLatitude(this WorldSceneSettings settings)
		{
			WorldAtlasScene entry = settings.AtlasEntry();
			return entry != null ? (float)entry.Latitude : 0f;
		}

		/// <summary>Longitude the whole scene takes its time of day at, in degrees.</summary>
		public static float Longitude(this WorldSceneSettings settings)
		{
			WorldAtlasScene entry = settings.AtlasEntry();
			return entry != null ? (float)entry.TimeLongitude : 0f;
		}

		/// <summary>World time, or a developer-authored fixed time. Nothing changes it at runtime.</summary>
		public static SceneTimeMode TimeMode(this WorldSceneSettings settings)
		{
			WorldAtlasScene entry = settings.AtlasEntry();
			return entry != null ? entry.TimeMode : SceneTimeMode.World;
		}

		/// <summary>The time shown when <see cref="TimeMode"/> is Fixed. 0.5 is noon.</summary>
		public static float FixedTimeOfDay01(this WorldSceneSettings settings)
		{
			WorldAtlasScene entry = settings.AtlasEntry();
			return entry != null ? entry.FixedTimeOfDay01 : 0.5f;
		}
	}
}
