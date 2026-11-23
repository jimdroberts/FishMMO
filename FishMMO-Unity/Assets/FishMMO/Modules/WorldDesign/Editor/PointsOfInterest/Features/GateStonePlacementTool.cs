#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Adds Gate Stones (issue #241) to world scenes that are already cut, without a re-cut: at every site of a Gate Stone kind
	/// (<see cref="GateStoneKinds"/>) that has no waypoint yet, <see cref="GateStoneFeature"/> is built as the cut would.
	/// </summary>
	/// <remarks>
	/// <para>The stone takes the site's own unlock index, as the cut gives it, so a later re-cut builds the same stone at the
	/// same index and every character keeps its discovery. A site that already has a waypoint or a stone is left alone, so
	/// running it twice adds nothing.</para>
	/// <para>Only the stone is added: the ground under it is the site's as cut (sampled from the terrain), nothing is levelled
	/// or cleared. Rebuild the world scene details cache afterwards; it is what puts the stones on the map and lets a Gate reach
	/// them from another scene server.</para>
	/// </remarks>
	public static class GateStonePlacementTool
	{
		/// <summary>The site kinds that carry a Gate Stone (the defaults' <see cref="GateStoneFeature"/> kinds).</summary>
		public static readonly POIType[] GateStoneKinds =
		{
			POIType.StoneCircle, POIType.LeyNexus, POIType.Henge, POIType.ElfWaystone, POIType.RuneStone,
		};

		[DashboardTool(DashboardToolAttribute.Maintenance, "Place Gate Stones", Section = "World", Order = 23,
			Tooltip = "Adds a Gate Stone at every stone circle, ley nexus, henge, elf waystone and rune stone of the open world scene that has no waypoint yet, at the site's unlock index. Save the scene and rebuild World Scene Details after.")]
		public static void PlaceInOpenScene()
		{
			var notes = new List<string>();
			PlaceInScene(SceneManager.GetActiveScene(), notes);
			Debug.Log("[Gate Stones]\n  " + string.Join("\n  ", notes));
		}

		/// <summary>Batch: every world scene (<see cref="WorldScenePaths.FindAll"/>), saved where stones were added.</summary>
		public static void PlaceInAllWorldScenesBatch()
		{
			var notes = new List<string>();
			bool ok = true;
			try
			{
				foreach (string path in WorldScenePaths.FindAll())
				{
					Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
					int added = PlaceInScene(scene, notes);
					if (added > 0)
					{
						ok &= EditorSceneManager.SaveScene(scene);
						notes.Add($"{scene.name}: saved.");
					}
				}
				AssetDatabase.SaveAssets();
			}
			catch (Exception ex)
			{
				notes.Add($"FAILED: {ex}");
				ok = false;
			}
			Debug.Log("[Gate Stones]\n  " + string.Join("\n  ", notes));
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(ok ? 0 : 1);
			}
		}

		/// <summary>Adds the scene's missing Gate Stones; how many were added.</summary>
		public static int PlaceInScene(Scene scene, List<string> notes)
		{
			ScenePointsOfInterest points = null;
			var sites = new Dictionary<int, ScenePointOfInterest>();
			var usedIndices = new HashSet<int>();
			var terrains = new List<Terrain>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				ScenePointOfInterestSettings settings = root.GetComponentInChildren<ScenePointOfInterestSettings>(true);
				if (points == null && settings != null)
				{
					points = settings.Points;
				}
				foreach (ScenePointOfInterest site in root.GetComponentsInChildren<ScenePointOfInterest>(true))
				{
					sites[site.Id] = site;
				}
				foreach (Waypoint waypoint in root.GetComponentsInChildren<Waypoint>(true))
				{
					usedIndices.Add(waypoint.WaypointIndex);
				}
				terrains.AddRange(root.GetComponentsInChildren<Terrain>(true));
			}
			if (points == null || points.Points == null)
			{
				notes.Add($"{scene.name}: no points-of-interest asset (not cut by the generator); nothing placed.");
				return 0;
			}

			float GroundAt(float x, float z)
			{
				foreach (Terrain terrain in terrains)
				{
					Vector3 p = terrain.transform.position, size = terrain.terrainData.size;
					if (x >= p.x && x <= p.x + size.x && z >= p.z && z <= p.z + size.z)
					{
						return terrain.SampleHeight(new Vector3(x, 0f, z)) + p.y;
					}
				}
				return float.NaN;
			}

			int candidates = 0, added = 0;
			foreach (PointOfInterestRecord record in points.Points)
			{
				if (record == null || Array.IndexOf(GateStoneKinds, record.Kind) < 0)
				{
					continue;
				}
				++candidates;
				string label = $"{record.Name} ({record.Kind}, site {record.Id})";
				if (!sites.TryGetValue(record.Id, out ScenePointOfInterest site) || site == null)
				{
					notes.Add($"{scene.name}: {label} has no site object; skipped.");
					continue;
				}
				if (site.GetComponentInChildren<Waypoint>(true) != null)
				{
					continue;
				}
				if (!WaypointUnlockMask.IsValidIndex(record.UnlockIndex) || usedIndices.Contains(record.UnlockIndex))
				{
					notes.Add($"{scene.name}: {label} has no free unlock index ({record.UnlockIndex}); skipped.");
					continue;
				}

				var context = new PointOfInterestSiteContext
				{
					Scene = scene,
					Root = site.transform,
					Record = record,
					Points = points,
					GroundAt = (x, z) =>
					{
						float y = GroundAt(x, z);
						return float.IsNaN(y) ? record.Position.y : y;
					},
					Notes = new List<string>(),
				};
				new GateStoneFeature().Build(context);
				foreach (string note in context.Notes)
				{
					notes.Add($"{scene.name}: {label}: {note}");
				}
				Waypoint stone = site.GetComponentInChildren<Waypoint>(true);
				if (stone != null && stone.Kind == WaypointKind.GateStone)
				{
					usedIndices.Add(stone.WaypointIndex);
					++added;
					notes.Add($"{scene.name}: Gate Stone at {label}, index {stone.WaypointIndex}, {stone.transform.position}.");
				}
			}
			if (added > 0)
			{
				EditorSceneManager.MarkSceneDirty(scene);
			}
			notes.Add($"{scene.name}: {candidates} Gate Stone site(s), {added} stone(s) added.");
			return added;
		}
	}
}
#endif
