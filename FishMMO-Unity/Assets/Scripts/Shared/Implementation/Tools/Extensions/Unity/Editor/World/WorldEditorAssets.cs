#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Asset plumbing for the Solar System and World Atlas pages: where world assets live, how a
	/// script-made asset becomes loadable at runtime, the example system, and one atlas entry per
	/// world scene.
	/// </summary>
	public static class WorldEditorAssets
	{
		public const string Root = "Assets/Templates/World";
		public const string BodiesFolder = Root + "/Bodies";
		public const string AtlasFolder = Root + "/Atlas";
		public const string LayersFolder = AtlasFolder + "/Layers";
		public const string ScenesFolder = AtlasFolder + "/Scenes";

		/// <summary>Every asset of a type in the project.</summary>
		public static List<T> FindAll<T>() where T : UnityEngine.Object
		{
			var result = new List<T>();
			foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
			{
				T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
				if (asset != null)
				{
					result.Add(asset);
				}
			}
			result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
			return result;
		}

		public static T FindFirst<T>() where T : UnityEngine.Object
		{
			List<T> all = FindAll<T>();
			return all.Count > 0 ? all[0] : null;
		}

		/// <summary>Creates folders on the way to a path.</summary>
		public static void EnsureFolder(string folder)
		{
			folder = folder.Replace('\\', '/').TrimEnd('/');
			if (AssetDatabase.IsValidFolder(folder))
			{
				return;
			}
			string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
		}

		/// <summary>Creates, saves and registers a new asset with a unique file name.</summary>
		public static T Create<T>(string folder, string name, Action<T> setup = null) where T : ScriptableObject
		{
			EnsureFolder(folder);
			T asset = ScriptableObject.CreateInstance<T>();
			string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{Sanitize(name)}.asset");
			asset.name = Path.GetFileNameWithoutExtension(path);
			setup?.Invoke(asset);
			AssetDatabase.CreateAsset(asset, path);
			RegisterAddressable(asset);
			Undo.RegisterCreatedObjectUndo(asset, "Create " + typeof(T).Name);
			return asset;
		}

		/// <summary>Deletes an asset after asking.</summary>
		public static bool DeleteWithConfirm(UnityEngine.Object asset)
		{
			if (asset == null)
			{
				return false;
			}
			string path = AssetDatabase.GetAssetPath(asset);
			if (!EditorUtility.DisplayDialog("Delete asset", $"Delete {path}?\n\nThis cannot be undone.", "Delete", "Cancel"))
			{
				return false;
			}
			return AssetDatabase.DeleteAsset(path);
		}

		public static string Sanitize(string name)
		{
			foreach (char c in Path.GetInvalidFileNameChars())
			{
				name = name.Replace(c, '_');
			}
			return string.IsNullOrWhiteSpace(name) ? "Unnamed" : name.Trim();
		}

		/// <summary>
		/// Puts an asset in the shared static group, labelled and addressed by name, so the client
		/// and the server load and cache it at boot. An asset made by a script is never inspected,
		/// so the inspector's own registration never runs for it.
		/// </summary>
		public static void RegisterAddressable(UnityEngine.Object asset)
		{
			RegisterAddressable(asset, Constants.SharedStaticLabel);
		}

		/// <summary>The client's own static group and label: render assets the server never loads.</summary>
		public const string ClientStaticGroup = "Client_Static_Permanent";

		/// <summary>Registers an asset in a named static group, labelled with the group's name.</summary>
		public static void RegisterAddressable(UnityEngine.Object asset, string groupName)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null || asset == null)
			{
				return;
			}
			string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
			if (string.IsNullOrEmpty(guid))
			{
				return;
			}
			AddressableAssetGroup group = settings.FindGroup(groupName) ?? settings.DefaultGroup;
			AddressableAssetEntry entry = settings.FindAssetEntry(guid);
			if (entry == null || entry.parentGroup != group)
			{
				entry = settings.CreateOrMoveEntry(guid, group);
			}
			if (entry == null)
			{
				Debug.LogError($"[WorldDesign] Could not make {asset.name} addressable.");
				return;
			}
			entry.address = asset.name;
			foreach (string other in new[] { Constants.SharedStaticLabel, ClientStaticGroup })
			{
				if (other != groupName)
				{
					entry.labels.Remove(other);
				}
			}
			if (!entry.labels.Contains(groupName))
			{
				settings.AddLabel(groupName);
				entry.labels.Add(groupName);
			}
			settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
		}

		// ── The example solar system ──────────────────────────────────

		/// <summary>
		/// Creates a small, neutral solar system: one sun, the home world with a 6-hour day and a
		/// 23.4° tilt, one tidally locked moon, the calendar, the atlas and its two layers.
		/// Existing assets are reused, never replaced.
		/// </summary>
		public static SolarSystemProfile CreateExampleSystem()
		{
			SolarSystemProfile system = FindFirst<SolarSystemProfile>();
			if (system != null)
			{
				return system;
			}

			CalendarProfile calendar = FindFirst<CalendarProfile>() ?? Create<CalendarProfile>(Root, "World Calendar");
			StarBody sun = Create<StarBody>(BodiesFolder, "Sun", s =>
			{
				s.DisplayName = "Sun";
				s.SkyRadiusKm = 696000f;
				s.Tint = new Color(1f, 0.96f, 0.86f, 1f);
				s.Orbit = new OrbitSettings { Distance = 1f, PeriodDays = 27.3f };
			});
			WorldBody home = Create<WorldBody>(BodiesFolder, "Home", b =>
			{
				b.DisplayName = "Home";
				b.Kind = WorldBodyKind.Planet;
				b.Parent = sun;
				b.Orbit = new OrbitSettings { Distance = 1f, Eccentricity = 0.0167f, PeriodMode = OrbitPeriodMode.Kepler, PeriodDays = 365f };
				b.RotationHours = 6f;
				b.AxialTiltDegrees = 23.4f;
				b.SkyRadiusKm = 6371f;
				b.Tint = new Color(0.35f, 0.55f, 0.85f, 1f);
				b.CurrentRadiusKm = 30f;
			});
			WorldBody moon = Create<WorldBody>(BodiesFolder, "Moon 1", b =>
			{
				b.DisplayName = "Moon 1";
				b.Kind = WorldBodyKind.Moon;
				b.Parent = home;
				b.TidallyLocked = true;
				b.AxialTiltDegrees = 1.5f;
				b.Orbit = new OrbitSettings { Distance = 384f, InclinationDegrees = 5.1f, PeriodMode = OrbitPeriodMode.Authored, PeriodDays = 7.5f };
				b.SkyRadiusKm = 1737f;
				b.Atmosphere = AtmosphereKind.None;
				b.Water = 0f;
				b.Tint = new Color(0.78f, 0.77f, 0.74f, 1f);
				b.MinimumRadiusKm = 10f;
				b.CurrentRadiusKm = 10f;
			});

			system = Create<SolarSystemProfile>(Root, "Solar System", p =>
			{
				p.Bodies.Add(sun);
				p.Bodies.Add(home);
				p.Bodies.Add(moon);
				p.HomeWorld = home;
				p.Calendar = calendar;
			});
			calendar.CalendarBody = home;
			EditorUtility.SetDirty(calendar);

			EnsureAtlas(system);
			AssetDatabase.SaveAssets();
			return system;
		}

		/// <summary>The project's atlas, created with Overworld and Underworld layers if missing.</summary>
		public static WorldAtlas EnsureAtlas(SolarSystemProfile system)
		{
			WorldAtlas atlas = FindFirst<WorldAtlas>();
			if (atlas == null)
			{
				WorldAtlasLayer overworld = Create<WorldAtlasLayer>(LayersFolder, "Overworld", l =>
				{
					l.DisplayName = "Overworld";
					l.SortOrder = 0;
					l.Tint = new Color(0.55f, 0.78f, 0.55f, 1f);
				});
				WorldAtlasLayer underworld = Create<WorldAtlasLayer>(LayersFolder, "Underworld", l =>
				{
					l.DisplayName = "Underworld";
					l.SortOrder = 1;
					l.Underground = true;
					l.DefaultWeather = FishMMO.Shared.Weather.WeatherSceneMode.None;
					l.Tint = new Color(0.78f, 0.55f, 0.4f, 1f);
				});
				atlas = Create<WorldAtlas>(AtlasFolder, "World Atlas", a =>
				{
					a.SolarSystem = system;
					a.DefaultLayers.Add(overworld);
					a.DefaultLayers.Add(underworld);
					a.DungeonLayer = underworld;
				});
			}
			else if (atlas.SolarSystem == null && system != null)
			{
				Undo.RecordObject(atlas, "Assign solar system");
				atlas.SolarSystem = system;
				EditorUtility.SetDirty(atlas);
			}
			return atlas;
		}

		// ── Scenes ────────────────────────────────────────────────────

		/// <summary>Asset paths of every world scene (and local scenes when enabled).</summary>
		public static List<string> WorldScenePaths()
		{
			var paths = new List<string>();
			AddScenes(Constants.Configuration.WorldScenePath, paths);
			if (EditorPrefs.GetBool("FishMMOEnableLocalDirectory"))
			{
				AddScenes(Constants.Configuration.LocalScenePath, paths);
			}
			paths.Sort(StringComparer.Ordinal);
			return paths;
		}

		private static void AddScenes(string folder, List<string> into)
		{
			folder = folder.Replace('\\', '/').TrimEnd('/');
			if (!Directory.Exists(folder))
			{
				return;
			}
			foreach (string file in Directory.GetFiles(folder, "*.unity", SearchOption.AllDirectories))
			{
				string path = file.Replace('\\', '/');
				if (!into.Contains(path))
				{
					into.Add(path);
				}
			}
		}

		/// <summary>Scene names that are dungeon instances.</summary>
		public static HashSet<string> DungeonSceneNames()
		{
			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (DungeonTemplate template in FindAll<DungeonTemplate>())
			{
				if (!string.IsNullOrEmpty(template.DungeonSceneName))
				{
					names.Add(template.DungeonSceneName);
				}
			}
			return names;
		}

		/// <summary>The scene details cache, or null.</summary>
		public static WorldSceneDetailsCache SceneDetails()
		{
			return AssetDatabase.LoadAssetAtPath<WorldSceneDetailsCache>(WorldSceneDetailsCache.CACHE_FULL_PATH);
		}

		/// <summary>A scene's size in km from its baked boundaries, or null when it has none.</summary>
		public static Vector2? SceneSizeKm(WorldSceneDetailsCache cache, string sceneName)
		{
			if (cache == null || cache.Scenes == null || !cache.Scenes.TryGetValue(sceneName, out WorldSceneDetails details))
			{
				return null;
			}
			Rect rect = MapBoundsResolver.FromSceneBoundaries(details);
			if (rect.width <= 0f || rect.height <= 0f)
			{
				return null;
			}
			return new Vector2(rect.width / 1000f, rect.height / 1000f);
		}

		/// <summary>
		/// Creates an atlas entry for every world scene that has none, unplaced, in the surface
		/// layer (dungeons in the dungeon layer), and refreshes every entry's size from the scene
		/// details cache. Returns how many entries were created.
		/// </summary>
		public static int SyncAtlasScenes(WorldAtlas atlas)
		{
			var existing = new Dictionary<string, WorldAtlasScene>(StringComparer.Ordinal);
			foreach (WorldAtlasScene entry in FindAll<WorldAtlasScene>())
			{
				if (!string.IsNullOrEmpty(entry.SceneName) && !existing.ContainsKey(entry.SceneName))
				{
					existing.Add(entry.SceneName, entry);
				}
			}

			WorldSceneDetailsCache cache = SceneDetails();
			HashSet<string> dungeons = DungeonSceneNames();
			WorldBody home = atlas != null && atlas.SolarSystem != null ? atlas.SolarSystem.HomeWorld : null;
			int created = 0;
			foreach (string path in WorldScenePaths())
			{
				string sceneName = Path.GetFileNameWithoutExtension(path);
				Vector2? size = SceneSizeKm(cache, sceneName);
				if (existing.TryGetValue(sceneName, out WorldAtlasScene entry))
				{
					if (size.HasValue && entry.SizeKm != size.Value)
					{
						Undo.RecordObject(entry, "Refresh scene size");
						entry.SizeKm = size.Value;
						EditorUtility.SetDirty(entry);
					}
					continue;
				}
				bool dungeon = dungeons.Contains(sceneName);
				Create<WorldAtlasScene>(ScenesFolder, sceneName, e =>
				{
					e.SceneName = sceneName;
					e.Body = home;
					e.Layer = atlas == null ? null : dungeon ? atlas.DungeonLayerOf(home) : atlas.SurfaceLayerOf(home);
					e.SizeKm = size ?? new Vector2(1f, 1f);
					e.Placed = false;
				});
				created++;
			}
			if (created > 0)
			{
				WorldAtlasScene.EditorLookup.Invalidate();
				AssetDatabase.SaveAssets();
			}
			return created;
		}

		/// <summary>
		/// Copies each scene's authored climate, biome map and client cap into its atlas entry
		/// where the entry has none. Scenes are only read; nothing in them changes. Returns the
		/// number of entries changed.
		/// </summary>
		public static int CopySceneSettingsIntoAtlas()
		{
			var entries = new Dictionary<string, WorldAtlasScene>(StringComparer.Ordinal);
			foreach (WorldAtlasScene entry in FindAll<WorldAtlasScene>())
			{
				if (!string.IsNullOrEmpty(entry.SceneName))
				{
					entries[entry.SceneName] = entry;
				}
			}
			int changed = 0;
			foreach (string path in WorldScenePaths())
			{
				string sceneName = Path.GetFileNameWithoutExtension(path);
				if (!entries.TryGetValue(sceneName, out WorldAtlasScene entry))
				{
					continue;
				}
				SceneSettingsSnapshot snapshot = ReadSceneSettings(path);
				if (!snapshot.Found)
				{
					continue;
				}
				bool dirty = false;
				Undo.RecordObject(entry, "Copy scene settings into atlas");
				if (entry.Climate == null && snapshot.Climate != null) { entry.Climate = snapshot.Climate; dirty = true; }
				if (entry.BiomeMap == null && snapshot.BiomeMap != null) { entry.BiomeMap = snapshot.BiomeMap; dirty = true; }
				if (entry.MaxClients == 0 && snapshot.MaxClients > 0) { entry.MaxClients = snapshot.MaxClients; dirty = true; }
				if (dirty)
				{
					EditorUtility.SetDirty(entry);
					changed++;
				}
			}
			AssetDatabase.SaveAssets();
			return changed;
		}

		private struct SceneSettingsSnapshot
		{
			public bool Found;
			public FishMMO.Shared.Biomes.ClimateSettings Climate;
			public FishMMO.Shared.Biomes.SceneBiomeMap BiomeMap;
			public int MaxClients;
		}

		/// <summary>
		/// Reads the serialized WorldSceneSettings values from a scene file without opening it:
		/// the component's YAML block names its fields and asset references by GUID.
		/// </summary>
		private static SceneSettingsSnapshot ReadSceneSettings(string scenePath)
		{
			var snapshot = new SceneSettingsSnapshot();
			MonoScript script = FindScript(typeof(WorldSceneSettings));
			if (script == null || !File.Exists(scenePath))
			{
				return snapshot;
			}
			string scriptGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
			string[] lines = File.ReadAllLines(scenePath);
			for (int i = 0; i < lines.Length; i++)
			{
				if (!lines[i].Contains("m_Script:") || !lines[i].Contains(scriptGuid))
				{
					continue;
				}
				snapshot.Found = true;
				for (int j = i + 1; j < lines.Length && !lines[j].StartsWith("---", StringComparison.Ordinal); j++)
				{
					string line = lines[j].Trim();
					if (line.StartsWith("maxClients:", StringComparison.Ordinal) || line.StartsWith("MaxClients:", StringComparison.Ordinal))
					{
						int.TryParse(line.Substring(line.IndexOf(':') + 1).Trim(), out snapshot.MaxClients);
					}
					else if (line.StartsWith("climate:", StringComparison.Ordinal) || line.StartsWith("Climate:", StringComparison.Ordinal))
					{
						snapshot.Climate = LoadReference<FishMMO.Shared.Biomes.ClimateSettings>(line);
					}
					else if (line.StartsWith("biomeMap:", StringComparison.Ordinal) || line.StartsWith("BiomeMap:", StringComparison.Ordinal))
					{
						snapshot.BiomeMap = LoadReference<FishMMO.Shared.Biomes.SceneBiomeMap>(line);
					}
				}
				break;
			}
			return snapshot;
		}

		private static T LoadReference<T>(string line) where T : UnityEngine.Object
		{
			int at = line.IndexOf("guid:", StringComparison.Ordinal);
			if (at < 0)
			{
				return null;
			}
			string guid = line.Substring(at + 5).Trim();
			int comma = guid.IndexOf(',');
			if (comma >= 0)
			{
				guid = guid.Substring(0, comma).Trim();
			}
			string path = AssetDatabase.GUIDToAssetPath(guid);
			return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
		}

		private static MonoScript FindScript(Type type)
		{
			foreach (string guid in AssetDatabase.FindAssets("t:MonoScript " + type.Name))
			{
				MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
				if (script != null && script.GetClass() == type)
				{
					return script;
				}
			}
			return null;
		}
	}
}
#endif
