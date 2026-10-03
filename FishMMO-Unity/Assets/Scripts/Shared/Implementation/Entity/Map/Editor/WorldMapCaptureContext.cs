using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldMaps
{
	/// <summary>
	/// What a world map capture is of: the scene, where it stands, and the moment it is
	/// photographed at. Handed to every <see cref="WorldMapBaker.CaptureStaging"/> hook.
	/// </summary>
	/// <remarks>
	/// <para>Resolved here, once, rather than by each hook, because the editor resolves things
	/// differently from a player. Outside play mode nothing has loaded the cached assets that
	/// <see cref="SolarSystemProfile.Active"/> reads, so it is null, and so is the home world
	/// <see cref="SceneTime.BodyOf"/> falls back to. The atlas entry is fine — it has its own edit-mode
	/// lookup — but the solar system has to be found in the project the way the world designer
	/// pages find it: the atlas's system, else the first system asset.</para>
	/// </remarks>
	public sealed class WorldMapCaptureContext
	{
		/// <summary>The scene being photographed. It is loaded and active.</summary>
		public Scene Scene;

		/// <summary>The scene's settings, or null.</summary>
		public WorldSceneSettings Settings;

		/// <summary>
		/// The scene's day/night cycle, or null. A scene without one has no sky at runtime — the
		/// client's sky system leaves it exactly as authored — and the map must not invent one.
		/// </summary>
		public WorldDayNightCycle Cycle;

		/// <summary>The solar system the scene stands in, or null.</summary>
		public SolarSystemProfile System;

		/// <summary>The planet or moon the scene stands on, or null.</summary>
		public WorldBody Body;

		/// <summary>Latitude the sun path uses, in degrees (the atlas's sun latitude).</summary>
		public double Latitude;

		/// <summary>Longitude the scene keeps its time of day at, in degrees.</summary>
		public double Longitude;

		/// <summary>The scene's heading on its body, in degrees.</summary>
		public float HeadingDegrees;

		/// <summary>
		/// The world time photographed at (<see cref="WorldMapCaptureMoment.EquinoxNoonHours"/>), or
		/// null when the scene is not placed in a solar system.
		/// </summary>
		public double? Hours;

		/// <summary>The capture camera. Hooks may read it; they must not move it.</summary>
		public Camera Camera;

		/// <summary>True when the scene has a sky the map should be lit by: an enabled day/night cycle on an active object.</summary>
		/// <remarks>
		/// Asked of the component's own flags, not <c>isActiveAndEnabled</c>: the cycle does not run in
		/// edit mode, and whether a behaviour that has never been enabled by the player loop reports
		/// itself enabled there is not something to rest the whole sun on.
		/// </remarks>
		public bool HasSky => Cycle != null && Cycle.enabled && Cycle.gameObject.activeInHierarchy;

		/// <summary>Works out the context of an open scene.</summary>
		public static WorldMapCaptureContext For(Scene scene, WorldSceneSettings settings, Camera camera)
		{
			var context = new WorldMapCaptureContext
			{
				Scene = scene,
				Settings = settings,
				Camera = camera,
				Cycle = FindInScene<WorldDayNightCycle>(scene),
				System = SolarSystemProfile.Resolve(settings != null ? settings.Body : null),
			};
			WorldAtlasScene entry = settings != null ? settings.AtlasEntry : null;
			context.Body = settings != null && settings.Body != null ? settings.Body
				: context.System != null ? context.System.HomeWorld : null;
			context.Latitude = settings != null ? settings.Latitude : 0.0;
			context.Longitude = settings != null ? settings.Longitude : 0.0;
			context.HeadingDegrees = entry != null ? entry.HeadingDegrees : 0f;
			context.Hours = WorldMapCaptureMoment.EquinoxNoonHours(context.System, context.Body, context.Longitude);
			return context;
		}

		/// <summary>
		/// The solar system as the game would have it: <see cref="SolarSystemProfile.Resolve"/>, the one
		/// lookup the globe bake and the scene generator also use, so a map is lit by the sun its
		/// globe was baked under.
		/// </summary>
		public static SolarSystemProfile ResolveSystem() => SolarSystemProfile.Resolve();

		private static T FindInScene<T>(Scene scene) where T : Component
		{
			if (!scene.IsValid() || !scene.isLoaded)
			{
				return null;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				T found = root.GetComponentInChildren<T>(true);
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}
	}
}
