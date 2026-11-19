using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldMaps
{
	/// <summary>
	/// What a world map capture is of: the scene, where it stands, and the moment it is
	/// photographed at. Handed to every <see cref="WorldMapBaker.CaptureStaging"/> hook.
	/// </summary>
	/// <remarks>
	/// <para>Resolved once, rather than by each hook. Where the scene stands on its world and the moment it is photographed at
	/// belong to the World Simulation module (the atlas, the solar system, the day-night cycle): it fills them in through
	/// <see cref="Resolver"/> (Phase 4 of the core and modules plan, 2026-11-18), and keeps its own state in <see cref="Sky"/>.
	/// Without the module the map is photographed as the scene is lit, at no particular moment.</para>
	/// </remarks>
	public sealed class WorldMapCaptureContext
	{
		public Scene Scene;

		public WorldSceneSettings Settings;

		/// <summary>Where the scene stands on its world, degrees.</summary>
		public double Latitude;

		public double Longitude;

		/// <summary>Which way the scene's north faces, degrees.</summary>
		public float HeadingDegrees;

		/// <summary>The world hours the map is lit at (the equinox noon), or null when the scene has none.</summary>
		public double? Hours;

		public Camera Camera;

		/// <summary>Whether the scene has a sky to light the map by (set by the module).</summary>
		public bool HasSky;

		/// <summary>The module's own state for this capture (its day-night cycle, solar system and body).</summary>
		public object Sky;

		/// <summary>Fills the scene's place, moment and sky in; installed by the World Simulation module's editor.</summary>
		public static Action<WorldMapCaptureContext> Resolver;

		public static WorldMapCaptureContext For(Scene scene, WorldSceneSettings settings, Camera camera)
		{
			var context = new WorldMapCaptureContext
			{
				Scene = scene,
				Settings = settings,
				Camera = camera,
			};
			Resolver?.Invoke(context);
			return context;
		}
	}
}
