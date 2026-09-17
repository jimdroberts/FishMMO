using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared.Atlas
{
	/// <summary>
	/// One scene's place in the world: the body and layer it is on, where on the globe, which way
	/// it faces, and the world settings that follow from that.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The atlas owns this data; <see cref="WorldSceneSettings"/> in the scene reads it. Climate,
	/// biome map and client cap are optional here: empty means the value authored in the scene.
	/// </para>
	/// <para>
	/// The whole scene shares one time: its time of day is taken at <see cref="Longitude"/>, or
	/// at the overridden time zone.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Atlas Scene", menuName = "FishMMO/World/Atlas Scene", order = 16)]
	public class WorldAtlasScene : CachedScriptableObject<WorldAtlasScene>, ICachedObject
	{
		[Tooltip("The scene's name, as in the build settings.")]
		public string SceneName;

		[Header("Placement")]
		[Tooltip("Off: the scene waits in the designer's library.")]
		public bool Placed;
		[Tooltip("The planet or moon. Empty: the home world.")]
		public WorldBody Body;
		public WorldAtlasLayer Layer;
		[Tooltip("Latitude of the scene's centre, in degrees.")]
		public double Latitude;
		[Tooltip("Longitude of the scene's centre, in degrees.")]
		public double Longitude;
		[Tooltip("Which way the scene's +Z faces, clockwise from north, in degrees.")]
		public float HeadingDegrees;
		[Tooltip("The scene's size in km (X, Z), read from its boundaries by the designer.")]
		public Vector2 SizeKm = new Vector2(1f, 1f);

		[Header("Time")]
		public bool OverrideTimeZone;
		[Tooltip("Hours from longitude 0. Used when overridden.")]
		[Range(-12, 12)] public int TimeZoneHours;
		public bool OverrideSunLatitude;
		[Tooltip("Latitude the sun path and auroras use. Used when overridden.")]
		[Range(-90f, 90f)] public float SunLatitude;
		[Tooltip("World follows the body's rotation and sun. Fixed is developer-only, for authored scenes.")]
		public SceneTimeMode TimeMode = SceneTimeMode.World;
		[Range(0f, 1f)] public float FixedTimeOfDay01 = 0.5f;

		[Header("Climate")]
		[Tooltip("Empty: the scene's own, else the body's base climate.")]
		public ClimateSettings Climate;
		[Tooltip("Empty: the scene's own.")]
		public SceneBiomeMap BiomeMap;

		[Header("Weather")]
		[Tooltip("Auto: the layer's default, and then dungeons get none and everything else its own.")]
		public WeatherSceneMode Weather = WeatherSceneMode.Auto;
		public WeatherPreset FixedWeather;
		[Range(0f, 1f)] public float FixedWeatherIntensity = 1f;
		[Tooltip("Let the automatic director start storm cells here.")]
		public bool WeatherDirector = true;

		[Header("Population")]
		[Tooltip("0: the scene's own setting.")]
		[Range(0, WorldSceneSettings.MaximumClientsPerScene)] public int MaxClients;

		[Header("Cartography")]
		[Tooltip("Reveal pieces across the scene, for the in-game atlas.")]
		public Vector2Int RevealGrid = new Vector2Int(4, 4);
		public bool DiscoveredByDefault;

		/// <summary>The time zone: derived from the longitude unless overridden.</summary>
		public int TimeZone => OverrideTimeZone ? TimeZoneHours : AtlasGeometry.TimeZoneOf(Longitude);

		/// <summary>The longitude the scene's time of day is taken at.</summary>
		public double TimeLongitude => OverrideTimeZone ? TimeZoneHours * 15.0 : Longitude;

		/// <summary>The latitude the sun path uses.</summary>
		public double EffectiveSunLatitude => OverrideSunLatitude ? SunLatitude : Latitude;

		/// <summary>The weather mode after the layer's default.</summary>
		public WeatherSceneMode EffectiveWeather => Weather != WeatherSceneMode.Auto || Layer == null ? Weather : Layer.DefaultWeather;

		// ── Lookup by scene name ──────────────────────────────────────

		private static readonly Dictionary<string, WorldAtlasScene> byName = new Dictionary<string, WorldAtlasScene>(StringComparer.Ordinal);

		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);
			if (!string.IsNullOrEmpty(SceneName))
			{
				byName[SceneName] = this;
			}
		}

		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			base.OnUnload(typeName, resourceName, resourceID);
			if (!string.IsNullOrEmpty(SceneName) && byName.TryGetValue(SceneName, out WorldAtlasScene current) && current == this)
			{
				byName.Remove(SceneName);
			}
		}

		/// <summary>
		/// The atlas entry of a scene, or null. At runtime, from the loaded assets; in the editor
		/// outside play mode, from the project.
		/// </summary>
		public static WorldAtlasScene Find(string sceneName)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				return null;
			}
			if (byName.TryGetValue(sceneName, out WorldAtlasScene entry) && entry != null)
			{
				return entry;
			}
#if UNITY_EDITOR
			if (!Application.isPlaying)
			{
				return EditorLookup.Find(sceneName);
			}
#endif
			return null;
		}

#if UNITY_EDITOR
		/// <summary>Project-wide lookup for edit mode, rebuilt whenever the project changes.</summary>
		public static class EditorLookup
		{
			private static Dictionary<string, WorldAtlasScene> map;

			[UnityEditor.InitializeOnLoadMethod]
			private static void Hook()
			{
				UnityEditor.EditorApplication.projectChanged += Invalidate;
			}

			public static void Invalidate() => map = null;

			public static WorldAtlasScene Find(string sceneName)
			{
				if (map == null)
				{
					map = new Dictionary<string, WorldAtlasScene>(StringComparer.Ordinal);
					foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:" + nameof(WorldAtlasScene)))
					{
						var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<WorldAtlasScene>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
						if (asset != null && !string.IsNullOrEmpty(asset.SceneName) && !map.ContainsKey(asset.SceneName))
						{
							map.Add(asset.SceneName, asset);
						}
					}
				}
				return map.TryGetValue(sceneName, out WorldAtlasScene entry) && entry != null ? entry : null;
			}
		}
#endif
	}
}
