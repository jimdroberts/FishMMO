#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared.WorldDesign
{
	public enum AtlasProblemSeverity
	{
		Error = 0,
		Warning = 1,
		Info = 2,
	}

	/// <summary>Something wrong (or worth knowing) about the atlas.</summary>
	public sealed class AtlasProblem
	{
		public AtlasProblemSeverity Severity;
		/// <summary>Lower sorts first within a severity. Missing destinations are 0.</summary>
		public int Order = 5;
		public string Message;
		public WorldAtlasScene Scene;
		public string ScenePath;
		public string TeleporterName;
	}

	/// <summary>The links between two scenes, both ways.</summary>
	public sealed class AtlasScenePair
	{
		public WorldAtlasScene A;
		public WorldAtlasScene B;
		public readonly List<AtlasLink> AToB = new List<AtlasLink>();
		public readonly List<AtlasLink> BToA = new List<AtlasLink>();
		public bool TwoWay => AToB.Count > 0 && BToA.Count > 0;
	}

	/// <summary>
	/// Everything the World Atlas page reads, loaded from the project: the atlas, the solar system,
	/// every atlas entry, the world scenes, and the teleporter links.
	/// </summary>
	public sealed class AtlasModel
	{
		public WorldAtlas Atlas;
		public SolarSystemProfile System;
		public readonly List<WorldAtlasScene> Entries = new List<WorldAtlasScene>();
		public readonly Dictionary<string, string> ScenePaths = new Dictionary<string, string>(StringComparer.Ordinal);
		public readonly Dictionary<string, WorldAtlasScene> BySceneName = new Dictionary<string, WorldAtlasScene>(StringComparer.Ordinal);
		public TeleporterCache Teleporters;
		public List<AtlasLink> Links = new List<AtlasLink>();
		public WorldSceneDetailsCache Details;

		public void Reload()
		{
			Atlas = WorldEditorAssets.FindFirst<WorldAtlas>();
			System = Atlas != null && Atlas.SolarSystem != null ? Atlas.SolarSystem : WorldEditorAssets.FindFirst<SolarSystemProfile>();
			Entries.Clear();
			Entries.AddRange(WorldEditorAssets.FindAll<WorldAtlasScene>());
			BySceneName.Clear();
			foreach (WorldAtlasScene entry in Entries)
			{
				if (!string.IsNullOrEmpty(entry.SceneName) && !BySceneName.ContainsKey(entry.SceneName))
				{
					BySceneName.Add(entry.SceneName, entry);
				}
			}
			ScenePaths.Clear();
			foreach (string path in WorldEditorAssets.WorldScenePaths())
			{
				ScenePaths[Path.GetFileNameWithoutExtension(path)] = path;
			}
			Teleporters = AtlasTeleporters.Cache();
			Links = AtlasTeleporters.Links(Teleporters);
			Details = WorldEditorAssets.SceneDetails();
		}

		/// <summary>Planets and moons scenes can sit on.</summary>
		public List<WorldBody> Bodies()
		{
			var bodies = new List<WorldBody>();
			if (System != null)
			{
				foreach (CelestialBody body in System.Bodies)
				{
					if (body is WorldBody world && !bodies.Contains(world))
					{
						bodies.Add(world);
					}
				}
			}
			foreach (WorldAtlasScene entry in Entries)
			{
				if (entry.Body != null && !bodies.Contains(entry.Body))
				{
					bodies.Add(entry.Body);
				}
			}
			return bodies;
		}

		public WorldBody BodyOf(WorldAtlasScene entry) => entry != null && entry.Body != null ? entry.Body : System != null ? System.HomeWorld : null;

		public List<WorldAtlasLayer> LayersOf(WorldBody body)
		{
			return Atlas != null ? Atlas.LayersOf(body) : new List<WorldAtlasLayer>();
		}

		/// <summary>Placed scenes on a body, optionally in one layer.</summary>
		public List<WorldAtlasScene> On(WorldBody body, WorldAtlasLayer layer)
		{
			var result = new List<WorldAtlasScene>();
			foreach (WorldAtlasScene entry in Entries)
			{
				if (entry.Placed && BodyOf(entry) == body && (layer == null || entry.Layer == layer))
				{
					result.Add(entry);
				}
			}
			return result;
		}

		public List<WorldAtlasScene> Unplaced()
		{
			var result = new List<WorldAtlasScene>();
			foreach (WorldAtlasScene entry in Entries)
			{
				if (!entry.Placed)
				{
					result.Add(entry);
				}
			}
			return result;
		}

		public static double RadiusOf(WorldBody body) => body != null ? body.AtlasRadiusKm : 30.0;

		/// <summary>The radius Auto mode wants for a body now.</summary>
		public float RequiredRadius(WorldBody body)
		{
			if (body == null)
			{
				return 30f;
			}
			var areas = new Dictionary<WorldAtlasLayer, float>();
			float largest = 0f;
			foreach (WorldAtlasScene entry in On(body, null))
			{
				WorldAtlasLayer key = entry.Layer;
				if (key == null)
				{
					key = Atlas != null ? Atlas.SurfaceLayerOf(body) : null;
				}
				areas.TryGetValue(key ?? NullLayer, out float area);
				areas[key ?? NullLayer] = area + Mathf.Abs(entry.SizeKm.x * entry.SizeKm.y);
				largest = Mathf.Max(largest, Mathf.Max(entry.SizeKm.x, entry.SizeKm.y));
			}
			return AtlasGeometry.RequiredRadius(body.MinimumRadiusKm, body.MaxCoveragePercent, areas.Values, largest);
		}

		private static WorldAtlasLayer nullLayer;
		private static WorldAtlasLayer NullLayer
		{
			get
			{
				if (nullLayer == null)
				{
					nullLayer = ScriptableObject.CreateInstance<WorldAtlasLayer>();
					nullLayer.hideFlags = HideFlags.HideAndDontSave;
				}
				return nullLayer;
			}
		}

		/// <summary>Settles an Auto body's radius. Returns true when it changed.</summary>
		public bool SettleRadius(WorldBody body)
		{
			if (body == null || body.RadiusMode != AtlasRadiusMode.Auto)
			{
				return false;
			}
			float wanted = Mathf.Max(body.MinimumRadiusKm, RequiredRadius(body));
			if (Mathf.Abs(wanted - body.CurrentRadiusKm) < 0.001f)
			{
				return false;
			}
			Undo.RecordObject(body, "Atlas radius");
			body.CurrentRadiusKm = wanted;
			EditorUtility.SetDirty(body);
			return true;
		}

		/// <summary>Pairs of overlapping scenes on a body and layer.</summary>
		public List<(WorldAtlasScene a, WorldAtlasScene b)> Overlaps(WorldBody body, WorldAtlasLayer layer)
		{
			var result = new List<(WorldAtlasScene, WorldAtlasScene)>();
			List<WorldAtlasScene> scenes = On(body, layer);
			double radius = RadiusOf(body);
			for (int i = 0; i < scenes.Count; i++)
			{
				for (int j = i + 1; j < scenes.Count; j++)
				{
					if (scenes[i].Layer == scenes[j].Layer && AtlasGeometry.Overlaps(AtlasFootprint.Of(scenes[i]), AtlasFootprint.Of(scenes[j]), radius))
					{
						result.Add((scenes[i], scenes[j]));
					}
				}
			}
			return result;
		}

		/// <summary>Links grouped by the two scenes they join (both must have atlas entries).</summary>
		public List<AtlasScenePair> Pairs()
		{
			var pairs = new Dictionary<(WorldAtlasScene, WorldAtlasScene), AtlasScenePair>();
			foreach (AtlasLink link in Links)
			{
				if (link.Destination == null || link.FromScene == link.ToScene)
				{
					continue;
				}
				if (!BySceneName.TryGetValue(link.FromScene, out WorldAtlasScene from) || !BySceneName.TryGetValue(link.ToScene, out WorldAtlasScene to))
				{
					continue;
				}
				bool forward = string.CompareOrdinal(from.SceneName, to.SceneName) < 0;
				var key = forward ? (from, to) : (to, from);
				if (!pairs.TryGetValue(key, out AtlasScenePair pair))
				{
					pair = new AtlasScenePair { A = key.Item1, B = key.Item2 };
					pairs.Add(key, pair);
				}
				(forward ? pair.AToB : pair.BToA).Add(link);
			}
			return new List<AtlasScenePair>(pairs.Values);
		}

		/// <summary>Links leaving a scene.</summary>
		public List<AtlasLink> LinksFrom(string sceneName)
		{
			return Links.FindAll(l => l.FromScene == sceneName);
		}

		/// <summary>Warnings on a scene: its teleporters with no valid destination.</summary>
		public int BrokenTeleporters(string sceneName)
		{
			int count = 0;
			foreach (AtlasLink link in Links)
			{
				if (link.FromScene == sceneName && (link.Unassigned || link.Missing))
				{
					count++;
				}
			}
			return count;
		}

		// ── Problems ──

		public List<AtlasProblem> Problems()
		{
			var problems = new List<AtlasProblem>();
			void Add(AtlasProblemSeverity severity, string message, WorldAtlasScene scene = null, int order = 5, string teleporter = null, string scenePath = null)
			{
				problems.Add(new AtlasProblem { Severity = severity, Message = message, Scene = scene, Order = order, TeleporterName = teleporter, ScenePath = scenePath ?? (scene != null && ScenePaths.TryGetValue(scene.SceneName ?? string.Empty, out string p) ? p : null) });
			}

			if (Atlas == null)
			{
				Add(AtlasProblemSeverity.Error, "There is no World Atlas asset. Press New on the Solar System page, or create one under FishMMO/World.");
			}
			foreach (string message in SolarSystemChecks.Problems(System))
			{
				Add(AtlasProblemSeverity.Warning, "Solar system: " + message);
			}
			if (Teleporters == null)
			{
				Add(AtlasProblemSeverity.Warning, "The teleporter cache is missing; connections cannot be shown. Rebuild it from World → World Scene Details.");
			}

			foreach (AtlasLink link in Links)
			{
				BySceneName.TryGetValue(link.FromScene ?? string.Empty, out WorldAtlasScene from);
				if (link.Unassigned)
				{
					Add(AtlasProblemSeverity.Error, $"{link.FromScene}: teleporter '{link.Teleporter.TeleporterName}' has no destination.", from, 0, link.Teleporter.TeleporterName, link.Teleporter.ScenePath);
				}
				else if (link.Missing)
				{
					Add(AtlasProblemSeverity.Error, $"{link.FromScene}: teleporter '{link.Teleporter.TeleporterName}' points at a destination that no longer exists.", from, 0, link.Teleporter.TeleporterName, link.Teleporter.ScenePath);
				}
				else if (!BySceneName.TryGetValue(link.ToScene, out WorldAtlasScene to))
				{
					Add(AtlasProblemSeverity.Warning, $"{link.FromScene}: teleporter '{link.Teleporter.TeleporterName}' leads to {link.ToScene}, which is not in the atlas.", from, 2, link.Teleporter.TeleporterName, link.Teleporter.ScenePath);
				}
				else if (!to.Placed)
				{
					Add(AtlasProblemSeverity.Info, $"{link.FromScene}: teleporter '{link.Teleporter.TeleporterName}' leads to {link.ToScene}, which is not placed yet.", from, 3, link.Teleporter.TeleporterName, link.Teleporter.ScenePath);
				}
			}

			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (WorldAtlasScene entry in Entries)
			{
				if (string.IsNullOrEmpty(entry.SceneName))
				{
					Add(AtlasProblemSeverity.Error, $"Atlas entry {entry.name} names no scene.", entry);
					continue;
				}
				if (!seen.Add(entry.SceneName))
				{
					Add(AtlasProblemSeverity.Error, $"{entry.SceneName} has more than one atlas entry; only the first is used.", entry);
				}
				if (!ScenePaths.ContainsKey(entry.SceneName))
				{
					Add(AtlasProblemSeverity.Error, $"Atlas entry {entry.name} names {entry.SceneName}, which is not a world scene.", entry);
				}
				WorldBody body = BodyOf(entry);
				if (body != null && System != null && !System.Bodies.Contains(body))
				{
					Add(AtlasProblemSeverity.Error, $"{entry.SceneName} is on {body.ResolvedName}, which is not in the solar system.", entry);
				}
				if (!entry.Placed)
				{
					Add(AtlasProblemSeverity.Warning, $"{entry.SceneName} is not placed on the map.", entry, 4);
					continue;
				}
				if (entry.Layer != null && Atlas != null && !Atlas.LayersOf(body).Contains(entry.Layer))
				{
					Add(AtlasProblemSeverity.Warning, $"{entry.SceneName} is in layer {entry.Layer.ResolvedName}, which {body?.ResolvedName ?? "its body"} does not have.", entry);
				}
				if (!AtlasGeometry.Fits(AtlasFootprint.Of(entry), RadiusOf(body)))
				{
					Add(AtlasProblemSeverity.Error, $"{entry.SceneName} ({entry.SizeKm.x:0.#} × {entry.SizeKm.y:0.#} km) is too big for {body?.ResolvedName ?? "its body"} at {RadiusOf(body):0.#} km radius.", entry);
				}
				if (entry.TimeMode == SceneTimeMode.Fixed)
				{
					Add(AtlasProblemSeverity.Info, $"{entry.SceneName} shows a fixed time ({SceneTime.Format(entry.FixedTimeOfDay01)}).", entry, 6);
				}
				if (entry.OverrideTimeZone && Math.Abs(entry.TimeZoneHours - AtlasGeometry.TimeZoneOf(entry.Longitude)) > 6)
				{
					Add(AtlasProblemSeverity.Warning, $"{entry.SceneName}'s time zone ({entry.TimeZoneHours:+0;-0} h) is more than 6 hours from its longitude's ({AtlasGeometry.TimeZoneOf(entry.Longitude):+0;-0} h).", entry);
				}
				if (body != null && !body.HasWeather && (entry.Weather == WeatherSceneMode.Own || entry.Weather == WeatherSceneMode.Fixed))
				{
					Add(AtlasProblemSeverity.Info, $"{entry.SceneName} asks for weather, but {body.ResolvedName} has no air, so it gets none.", entry, 6);
				}
			}

			foreach (KeyValuePair<string, string> scene in ScenePaths)
			{
				if (!BySceneName.ContainsKey(scene.Key))
				{
					Add(AtlasProblemSeverity.Warning, $"{scene.Key} is not in the atlas. Open the World Atlas page to add it.", null, 4, null, scene.Value);
				}
			}

			foreach (WorldBody body in Bodies())
			{
				foreach ((WorldAtlasScene a, WorldAtlasScene b) in Overlaps(body, null))
				{
					Add(AtlasProblemSeverity.Error, $"{a.SceneName} overlaps {b.SceneName} on {body.ResolvedName}.", a, 1);
				}
			}

			problems.Sort((x, y) =>
			{
				int c = x.Severity.CompareTo(y.Severity);
				if (c != 0) return c;
				c = x.Order.CompareTo(y.Order);
				return c != 0 ? c : string.CompareOrdinal(x.Message, y.Message);
			});
			return problems;
		}

		/// <summary>Dashboard → Core → Validate.</summary>
		[DashboardTool(DashboardToolAttribute.Validate, "Validate World Atlas", Section = "World", Tooltip = "Checks the solar system, the atlas and every teleporter link, and logs what is wrong.")]
		public static void ValidateFromDashboard()
		{
			var model = new AtlasModel();
			model.Reload();
			List<AtlasProblem> problems = model.Problems();
			int errors = 0;
			foreach (AtlasProblem problem in problems)
			{
				switch (problem.Severity)
				{
					case AtlasProblemSeverity.Error:
						errors++;
						Debug.LogError("[World Atlas] " + problem.Message);
						break;
					case AtlasProblemSeverity.Warning:
						Debug.LogWarning("[World Atlas] " + problem.Message);
						break;
					default:
						Debug.Log("[World Atlas] " + problem.Message);
						break;
				}
			}
			Debug.Log($"[World Atlas] {problems.Count} finding(s), {errors} error(s).");
		}
	}
}
#endif
