#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>One point of interest as the atlas draws it: on the globe and on the scene preview.</summary>
	public readonly struct AtlasPoint
	{
		/// <summary>The record's id in the scene's POI asset, or -1 for a landmark from the details cache.</summary>
		public readonly int Id;
		public readonly POIType Kind;
		public readonly PointOfInterestGroup Group;
		public readonly string Name;
		/// <summary>World position in the scene, metres.</summary>
		public readonly Vector3 Position;
		/// <summary>The map detail tier: 0 always shown, 3 only close up.</summary>
		public readonly int Tier;

		public AtlasPoint(int id, POIType kind, PointOfInterestGroup group, string name, Vector3 position, int tier)
		{
			Id = id;
			Kind = kind;
			Group = group;
			Name = name;
			Position = position;
			Tier = tier;
		}

		/// <summary>"Name (Kind)" for a tooltip.</summary>
		public string Describe()
		{
			string kind = PointOfInterestKinds.Info(Kind).DisplayName;
			return string.IsNullOrEmpty(Name) ? kind : $"{Name}\n{kind} · {Group}";
		}
	}

	/// <summary>
	/// What the World Atlas page knows about a scene's points of interest, read from assets alone: the
	/// generated <c>&lt;Scene&gt; Points of Interest.asset</c> and the scene's <see cref="PointOfInterestSettings"/>.
	/// No scene is opened for any of it.
	/// </summary>
	public static class AtlasPointsOfInterest
	{
		/// <summary>
		/// The colour of each group on the globe and the preview: the same values as the in-game map's
		/// <c>--map-poi-*</c> tokens in FishMMO-Theme.uss (pinned by a test), so a designer reads the
		/// atlas with the legend the player will have.
		/// </summary>
		public static Color GroupColour(PointOfInterestGroup group) => PointOfInterestKinds.GroupColour(group);

		/// <summary>The generated POI asset's path for a scene, or null when its body is unknown.</summary>
		public static string AssetPathOf(WorldBody body, string sceneName)
		{
			if (body == null || string.IsNullOrEmpty(sceneName))
			{
				return null;
			}
			return ScenePointsOfInterest.AssetPath(SceneGenerator.TerrainFolder(body, sceneName), WorldEditorAssets.Sanitize(sceneName));
		}

		/// <summary>The scene's generated POI asset, or null when it was never cut with points of interest.</summary>
		public static ScenePointsOfInterest Load(WorldBody body, string sceneName)
		{
			string path = AssetPathOf(body, sceneName);
			return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<ScenePointsOfInterest>(path);
		}

		/// <summary>
		/// Every point of interest the atlas can show for a scene: the generated asset's records, else the
		/// details cache's harvested list (hand-placed landmarks, or a generated scene whose asset is gone).
		/// </summary>
		/// <summary>The scene's baked ways (roads, tracks, footpaths, trails, streets), or none.</summary>
		public static IReadOnlyList<ScenePath> PathsFor(WorldBody body, string sceneName)
		{
			ScenePointsOfInterest asset = Load(body, sceneName);
			return asset != null && asset.Paths != null ? asset.Paths : (IReadOnlyList<ScenePath>)Array.Empty<ScenePath>();
		}

		public static List<AtlasPoint> For(WorldBody body, string sceneName, WorldSceneDetailsCache details)
		{
			var result = new List<AtlasPoint>();
			ScenePointsOfInterest asset = Load(body, sceneName);
			if (asset != null && asset.Points != null && asset.Points.Count > 0)
			{
				foreach (PointOfInterestRecord record in asset.Points)
				{
					if (record == null)
					{
						continue;
					}
					PointOfInterestKindInfo info = PointOfInterestKinds.Info(record.Kind);
					string name = string.IsNullOrEmpty(record.Name) ? info.DisplayName : record.Name;
					result.Add(new AtlasPoint(record.Id, record.Kind, info.Group, name, record.Position, Mathf.Max(0, record.DetailTier)));
				}
				return result;
			}

			WorldSceneDetails sceneDetails = null;
			details?.Scenes?.TryGetValue(sceneName ?? string.Empty, out sceneDetails);
			if (sceneDetails?.PointsOfInterest == null)
			{
				return result;
			}
			foreach (MapPointOfInterestDetails landmark in sceneDetails.PointsOfInterest)
			{
				if (landmark == null)
				{
					continue;
				}
				POIType kind = PointOfInterestKinds.TryKindOf(landmark.Type, out POIType found) && found != POIType.Any ? found : POIType.Landmark;
				result.Add(new AtlasPoint(-1, kind, PointOfInterestKinds.Info(kind).Group, landmark.Name, landmark.Position, Mathf.Max(0, landmark.DetailTier)));
			}
			return result;
		}

		/// <summary>The scene's POI settings: the atlas entry's reference, else the asset beside the entry.</summary>
		public static PointOfInterestSettings SettingsOf(WorldAtlasScene entry)
		{
			if (entry == null)
			{
				return null;
			}
			if (entry.PointsOfInterest != null)
			{
				return entry.PointsOfInterest;
			}
			string entryPath = AssetDatabase.GetAssetPath(entry);
			return string.IsNullOrEmpty(entryPath)
				? null
				: AssetDatabase.LoadAssetAtPath<PointOfInterestSettings>(PointOfInterestSettings.PathBeside(entryPath, WorldEditorAssets.Sanitize(entry.SceneName)));
		}

		/// <summary>
		/// The settings path for a scene: beside its atlas entry, or where a fresh cut will create the entry.
		/// </summary>
		public static string SettingsPathFor(string sceneName)
		{
			WorldAtlasScene entry = WorldAtlasScene.Find(sceneName);
			string entryPath = entry != null ? AssetDatabase.GetAssetPath(entry) : null;
			if (string.IsNullOrEmpty(entryPath))
			{
				entryPath = $"{WorldEditorAssets.ScenesFolder}/{WorldEditorAssets.Sanitize(sceneName)}.asset";
			}
			return PointOfInterestSettings.PathBeside(entryPath, WorldEditorAssets.Sanitize(sceneName));
		}

		/// <summary>
		/// Writes a prompt's answer into the settings asset at <paramref name="path"/>, creating it when
		/// there is none. Returns the asset; <paramref name="created"/> says whether this call made it.
		/// </summary>
		public static PointOfInterestSettings Save(string path, PointOfInterestChoice choice, out bool created)
		{
			created = false;
			PointOfInterestSettings settings = AssetDatabase.LoadAssetAtPath<PointOfInterestSettings>(path);
			if (settings == null)
			{
				int slash = path.LastIndexOf('/');
				if (slash > 0)
				{
					WorldEditorAssets.EnsureFolder(path.Substring(0, slash));
				}
				settings = ScriptableObject.CreateInstance<PointOfInterestSettings>();
				choice.ApplyTo(settings);
				AssetDatabase.CreateAsset(settings, path);
				created = true;
			}
			else
			{
				Undo.RecordObject(settings, "Point of interest settings");
				choice.ApplyTo(settings);
				EditorUtility.SetDirty(settings);
			}
			AssetDatabase.SaveAssetIfDirty(settings);
			return settings;
		}

		/// <summary>Points the atlas entry at its settings, if it does not already.</summary>
		public static void Assign(WorldAtlasScene entry, PointOfInterestSettings settings)
		{
			if (entry == null || settings == null || entry.PointsOfInterest == settings)
			{
				return;
			}
			Undo.RecordObject(entry, "Assign point of interest settings");
			entry.PointsOfInterest = settings;
			EditorUtility.SetDirty(entry);
			AssetDatabase.SaveAssetIfDirty(entry);
		}

		/// <summary>
		/// Selects and frames a point of interest's object when its scene is open. False when the scene is
		/// not open or the object is not in it.
		/// </summary>
		public static bool SelectInOpenScene(string scenePath, AtlasPoint point)
		{
			if (string.IsNullOrEmpty(scenePath))
			{
				return false;
			}
			Scene scene = SceneManager.GetSceneByPath(scenePath);
			if (!scene.IsValid() || !scene.isLoaded)
			{
				return false;
			}
			UnityEngine.Object found = null;
			var generated = new List<ScenePointOfInterest>();
			var authored = new List<MapPointOfInterest>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				root.GetComponentsInChildren(true, generated);
				foreach (ScenePointOfInterest candidate in generated)
				{
					if (point.Id >= 0 ? candidate.Id == point.Id : candidate.ResolvedName == point.Name)
					{
						found = candidate.gameObject;
						break;
					}
				}
				if (found == null && point.Id < 0)
				{
					root.GetComponentsInChildren(true, authored);
					foreach (MapPointOfInterest candidate in authored)
					{
						if (candidate.ResolvedName == point.Name)
						{
							found = candidate.gameObject;
							break;
						}
					}
				}
				if (found != null)
				{
					break;
				}
			}
			if (found == null)
			{
				return false;
			}
			Selection.activeObject = found;
			EditorGUIUtility.PingObject(found);
			SceneView.lastActiveSceneView?.FrameSelected();
			return true;
		}
	}

	/// <summary>
	/// Bumps <see cref="Version"/> whenever a points-of-interest asset, a POI settings asset or a baked
	/// world map is imported, moved or deleted, so the World Atlas page redraws without being told.
	/// </summary>
	/// <remarks>
	/// The page polls a dirty stamp four times a second; an in-memory edit shows up in an asset's dirty
	/// count, but a file written by the generator or by the map baker (Bake Maps, Remove Baked Maps) is
	/// only seen through the import.
	/// </remarks>
	public sealed class AtlasAssetWatcher : AssetPostprocessor
	{
		/// <summary>Changes each time a watched asset changes on disk.</summary>
		public static int Version { get; private set; }

		private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
		{
			if (Touches(imported) || Touches(deleted) || Touches(moved) || Touches(movedFrom))
			{
				Version++;
			}
		}

		/// <summary>Whether any path is one the atlas draws from.</summary>
		public static bool Touches(string[] paths)
		{
			if (paths == null)
			{
				return false;
			}
			foreach (string path in paths)
			{
				if (path == null)
				{
					continue;
				}
				if (path.EndsWith(" Points of Interest.asset", StringComparison.Ordinal)
					|| path.EndsWith(" POI Settings.asset", StringComparison.Ordinal)
					|| path.StartsWith(WorldMapDefinition.BakedDirectory, StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}
	}
}
#endif
