using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Generated points of interest on the maps (Jim, 2026-10-10): harvested into the scene details whether
	/// or not a map is baked, hidden until discovered, one marker type per kind styled and filtered by group.
	/// </summary>
	[TestFixture]
	public class PointOfInterestMapTests
	{
		private const string SharedSheetPath = "Assets/Scripts/Client/GUI/World/Map/UIMapShared.uss";
		private const string PointOfInterestSheetPath = "Assets/Scripts/Client/GUI/World/Map/UIMapPOI.uss";
		private const string ThemePath = "Assets/Scripts/Client/GUI/FishMMO-Theme.uss";

		private readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();

		[TearDown]
		public void TearDown()
		{
			foreach (UnityEngine.Object item in temporary)
			{
				if (item != null)
				{
					UnityEngine.Object.DestroyImmediate(item);
				}
			}
			temporary.Clear();
			Cartography.SetProvider(null);
		}

		// ── Harvest ────────────────────────────────────────────────

		[Test]
		public void Harvest_ReadsGeneratedAndHandPlacedPoints_FromItsOwnSceneOnly()
		{
			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

			var generated = new GameObject("Generated Fall").AddComponent<ScenePointOfInterest>();
			generated.Apply(new PointOfInterestRecord
			{
				Id = 7,
				Kind = POIType.Waterfall,
				Name = "Silverspill Falls",
				Position = new Vector3(120f, 14f, -40f),
				DetailTier = 2,
				RequiresDiscovery = true,
			});

			var handPlaced = new GameObject("Old Mill").AddComponent<MapPointOfInterest>();
			handPlaced.transform.position = new Vector3(-30f, 0f, 60f);
			handPlaced.Type = MapMarkerType.Landmark;

			var disabled = new GameObject("Switched Off").AddComponent<ScenePointOfInterest>();
			disabled.Kind = POIType.Peak;
			disabled.gameObject.SetActive(false);

			// A POI in another loaded scene must never be harvested into this one.
			// A preview scene: an additive NewScene throws while the runner's untitled scene is unsaved.
			Scene neighbour = EditorSceneManager.NewPreviewScene();
			GameObject stray = new GameObject("Neighbour Peak");
			SceneManager.MoveGameObjectToScene(stray, neighbour);
			stray.AddComponent<ScenePointOfInterest>().Kind = POIType.Peak;

			try
			{
				List<MapPointOfInterestDetails> harvested = WorldSceneDetailsCacheReader.HarvestPointsOfInterest(scene);

				LogAssert.AreEqual(2, harvested.Count, "the generated fall and the hand-placed landmark; not the disabled one, not the neighbour's");
				// Sorted by type: Landmark's ordinal is below every point-of-interest kind's.
				LogAssert.AreEqual(MapMarkerType.Landmark, harvested[0].Type);
				LogAssert.AreEqual("Old Mill", harvested[0].Name);
				LogAssert.AreEqual(MapMarkerType.Waterfall, harvested[1].Type, "a kind is drawn as the marker type of the same name");
				LogAssert.AreEqual("Silverspill Falls", harvested[1].Name);
				LogAssert.AreEqual(new Vector3(120f, 14f, -40f), harvested[1].Position);
				LogAssert.IsTrue(harvested[1].RequiresDiscovery, "every generated point of interest waits for discovery");
			}
			finally
			{
				EditorSceneManager.ClosePreviewScene(neighbour);
			}
		}

		[Test]
		public void Harvest_IsDeterministic()
		{
			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			foreach (string name in new[] { "Bravo", "Alpha", "Charlie" })
			{
				var point = new GameObject(name).AddComponent<ScenePointOfInterest>();
				point.Kind = POIType.Shrine;
				point.PointName = name;
			}

			List<MapPointOfInterestDetails> harvested = WorldSceneDetailsCacheReader.HarvestPointsOfInterest(scene);
			LogAssert.AreEqual("Alpha,Bravo,Charlie", string.Join(",", harvested.ConvertAll(p => p.Name)),
				"component order in a scene is not stable; the committed cache must not churn with it");
		}

		// ── MapContent ─────────────────────────────────────────────

		[Test]
		public void PointOfInterest_ShowsOnlyOnceDiscovered()
		{
			var details = new WorldSceneDetails();
			details.PointsOfInterest.Add(new MapPointOfInterestDetails
			{
				Name = "Hollow Barrow",
				Position = new Vector3(100f, 0f, 100f),
				Type = MapMarkerType.Barrow,
				DetailTier = 2,
				RequiresDiscovery = true,
			});
			var fog = new FogOfWarMap(new Rect(0f, 0f, 200f, 200f), FogOfWarDefaults.ChunkSize);
			var results = new List<MapMarkerSnapshot>();

			MapContent.AppendPointsOfInterest(results, details, null, fog, true);
			LogAssert.AreEqual(0, results.Count, "a point in unexplored ground must not be on the map");

			LogAssert.IsTrue(fog.Reveal(new Vector3(100f, 0f, 100f)), "the point must lie inside the fog grid, or this proves nothing");
			MapContent.AppendPointsOfInterest(results, details, null, fog, true);
			LogAssert.AreEqual(1, results.Count, "found, it is drawn");
			LogAssert.AreEqual(MapMarkerType.Barrow, results[0].Type);
			LogAssert.AreEqual("Hollow Barrow", results[0].Label);
		}

		[Test]
		public void PointsOfInterest_FromDetailsAndDefinition_AreDrawnOnce()
		{
			var details = new WorldSceneDetails();
			details.PointsOfInterest.Add(new MapPointOfInterestDetails { Name = "Old Mill", Position = new Vector3(10.2f, 0f, 20.4f), RequiresDiscovery = false });

			var definition = ScriptableObject.CreateInstance<WorldMapDefinition>();
			temporary.Add(definition);
			// The same hand-placed landmark, baked into the definition too, a few centimetres apart.
			definition.PointsOfInterest.Add(new MapPointOfInterestDetails { Name = "Old Mill", Position = new Vector3(10.0f, 3f, 20.3f), RequiresDiscovery = false });
			definition.PointsOfInterest.Add(new MapPointOfInterestDetails { Name = "Lookout", Position = new Vector3(50f, 0f, 50f), RequiresDiscovery = false });

			var results = new List<MapMarkerSnapshot>();
			MapContent.AppendPointsOfInterest(results, details, definition, null, true);

			LogAssert.AreEqual(2, results.Count, "the mill once, and the definition-only lookout");
			LogAssert.AreEqual("Old Mill", results[0].Label);
			LogAssert.AreEqual("Lookout", results[1].Label);

			// And with no bake at all (Remove Baked Maps), the details alone still draw it.
			results.Clear();
			MapContent.AppendPointsOfInterest(results, details, null, null, true);
			LogAssert.AreEqual(1, results.Count, "a landmark must not vanish because no map is baked");
		}

		[Test]
		public void DeepTiers_KeepTheirIcon_AndLoseTheirNameWhenZoomedOut()
		{
			LogAssert.AreEqual(1, MapContent.LabelTierForZoom(1000f, 1000f), "the whole scene in view names settlements only");
			LogAssert.AreEqual(2, MapContent.LabelTierForZoom(400f, 1000f));
			LogAssert.AreEqual(3, MapContent.LabelTierForZoom(200f, 1000f), "a quarter of the scene names everything");

			var details = new WorldSceneDetails();
			details.PointsOfInterest.Add(new MapPointOfInterestDetails { Name = "Town", Type = MapMarkerType.Town, DetailTier = 1, RequiresDiscovery = false });
			details.PointsOfInterest.Add(new MapPointOfInterestDetails { Name = "Rapids", Type = MapMarkerType.Rapids, DetailTier = 3, Position = Vector3.one * 5f, RequiresDiscovery = false });
			var results = new List<MapMarkerSnapshot>();
			MapContent.AppendPointsOfInterest(results, details, null, null, true, labelTier: 1);

			LogAssert.AreEqual(2, results.Count, "both are drawn: the tier only decides the name");
			LogAssert.AreEqual("Town", results[0].Label);
			LogAssert.IsNull(results[1].Label, "a tier-3 name waits for the view to close in");
		}

		[Test]
		public void EveryPointOfInterestTier_IsInsideTheCartographyRange()
		{
			foreach (PointOfInterestKindInfo info in PointOfInterestKinds.All)
			{
				LogAssert.IsTrue(info.DetailTier >= 0 && info.DetailTier <= Cartography.MaximumDetailTier,
					$"{info.Kind} has tier {info.DetailTier}, outside 0..{Cartography.MaximumDetailTier}: no Cartography level could ever show it");
			}
			LogAssert.AreEqual(Cartography.MaximumDetailTier, Cartography.VisibleContentTier, "with no provider every tier is shown");
		}

		// ── Styles ─────────────────────────────────────────────────

		[Test]
		public void EveryPointOfInterestType_IsStyledInItsGroupsColour()
		{
			string source = File.ReadAllText(PointOfInterestSheetPath);
			var rules = new List<(string selectors, string body)>();
			foreach (Match match in Regex.Matches(Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"([^{}]+)\{([^}]*)\}"))
			{
				rules.Add((match.Groups[1].Value, match.Groups[2].Value));
			}

			foreach (MapMarkerType type in (MapMarkerType[])Enum.GetValues(typeof(MapMarkerType)))
			{
				if (type == MapMarkerType.Landmark || type == MapMarkerType.DungeonEntrance
					|| !PointOfInterestKinds.TryKindOf(type, out POIType kind) || !PointOfInterestKinds.IsKnown(kind))
				{
					continue;
				}
				string selector = "." + UITKMapView.MarkerTypeClassPrefix + type.ToString().ToLowerInvariant();
				string token = "var(--map-poi-" + PointOfInterestKinds.Info(kind).Group.ToString().ToLowerInvariant() + ")";
				bool found = false;
				foreach ((string selectors, string body) in rules)
				{
					if (Regex.IsMatch(selectors, Regex.Escape(selector) + @"[\s,]") && body.Contains(token))
					{
						found = true;
						break;
					}
				}
				LogAssert.IsTrue(found, $"{type} is not in a rule coloured {token} in {PointOfInterestSheetPath}; regenerate the sheet from PointOfInterestKinds");
			}
		}

		[Test]
		public void BothMapPanels_LoadThePointOfInterestSheet()
		{
			foreach (string uxml in new[] { "Assets/Scripts/Client/GUI/World/Map/UIMap.uxml", "Assets/Scripts/Client/GUI/World/Minimap/UIMinimap.uxml" })
			{
				LogAssert.IsTrue(File.ReadAllText(uxml).Contains("UIMapPOI.uss\""), $"{uxml} must load UIMapPOI.uss, or every point of interest draws in the fallback grey");
			}
			LogAssert.IsTrue(File.Exists(SharedSheetPath));
		}

		[Test]
		public void EveryGroupToken_IsInTheTheme_AndTheAtlasUsesTheSameColour()
		{
			string theme = File.ReadAllText(ThemePath);
			foreach (PointOfInterestGroup group in (PointOfInterestGroup[])Enum.GetValues(typeof(PointOfInterestGroup)))
			{
				string token = "--map-poi-" + group.ToString().ToLowerInvariant();
				Match match = Regex.Match(theme, Regex.Escape(token) + @":\s*rgb\((\d+),\s*(\d+),\s*(\d+)\)");
				LogAssert.IsTrue(match.Success, $"{token} is not declared in {ThemePath}");

				Color atlas = AtlasPointsOfInterest.GroupColour(group);
				for (int i = 0; i < 3; i++)
				{
					int theirs = int.Parse(match.Groups[i + 1].Value, CultureInfo.InvariantCulture);
					LogAssert.AreEqual(theirs, Mathf.RoundToInt(atlas[i] * 255f), $"the atlas draws {group} in a different colour from the map's {token}");
				}
			}
		}

		// ── Filters ────────────────────────────────────────────────

		[Test]
		public void EveryPointOfInterestType_FiltersUnderItsGroup()
		{
			foreach (MapMarkerType type in (MapMarkerType[])Enum.GetValues(typeof(MapMarkerType)))
			{
				if (type == MapMarkerType.Landmark || type == MapMarkerType.DungeonEntrance
					|| !PointOfInterestKinds.TryKindOf(type, out POIType kind) || !PointOfInterestKinds.IsKnown(kind))
				{
					continue;
				}
				MapFilterCategory expected = MapFilters.CategoryFor(PointOfInterestKinds.Info(kind).Group);
				LogAssert.AreEqual(expected, MapFilters.Categorize(type), $"{type} must filter with its group, not fall into Landmarks by default");
				LogAssert.IsTrue(Array.IndexOf(MapFilters.PlaceCategories, expected) >= 0, $"{type}'s group row is missing from PLACES");
			}

			LogAssert.AreEqual(MapFilterCategory.Landmarks, MapFilters.Categorize(MapMarkerType.Landmark), "hand-placed landmarks keep their row");
			foreach (MapFilterCategory category in MapFilters.Categories)
			{
				LogAssert.IsTrue((int)category < 32, $"{category} is past the 32 bits the saved filter mask holds");
			}
		}

		[Test]
		public void EveryGroup_HasItsOwnCategory()
		{
			var seen = new HashSet<MapFilterCategory>();
			foreach (PointOfInterestGroup group in (PointOfInterestGroup[])Enum.GetValues(typeof(PointOfInterestGroup)))
			{
				MapFilterCategory category = MapFilters.CategoryFor(group);
				LogAssert.IsTrue(seen.Add(category), $"{group} shares a filter row with another group");
				LogAssert.IsTrue(MapFilters.TryGroupOf(category, out PointOfInterestGroup back) && back == group, $"{category} does not lead back to {group}");
				LogAssert.IsTrue(Array.IndexOf(MapFilters.MarkerCategories, category) < 0);
			}
		}

		// ── North offset ───────────────────────────────────────────

		[Test]
		public void TurnedMapImage_IsSampledAsTheBakeCameraFramedIt()
		{
			var rect = new Rect(-500f, -300f, 1000f, 600f);
			var world = new Vector3(120f, 0f, -80f);

			Vector2 axisAligned = UITKMapView.WorldToTextureUV(rect, world, 0f);
			LogAssert.AreEqual(new Vector2((world.x - rect.xMin) / rect.width, (world.z - rect.yMin) / rect.height), axisAligned);

			// The atlas's AtlasImage.UV is the inverse of the same bake camera; the two must agree.
			foreach (float north in new[] { 0f, 30f, 90f, -45f })
			{
				Vector2 client = UITKMapView.WorldToTextureUV(rect, world, north);
				Vector2 atlas = new AtlasImage(null, rect, north, true).UV(new Vector2(world.x, world.z));
				LogAssert.IsTrue((client - atlas).sqrMagnitude < 1e-8f, $"at {north}° the map samples {client} where the atlas samples {atlas}");
			}
		}

		// ── Pre-cut prompt ─────────────────────────────────────────

		[Test]
		public void PromptChoice_KeepsOnlyRealOverrides_InKindOrder()
		{
			var settings = ScriptableObject.CreateInstance<PointOfInterestSettings>();
			temporary.Add(settings);

			PointOfInterestChoice choice = PointOfInterestChoice.From(null);
			LogAssert.IsFalse(choice.Capital);
			LogAssert.AreEqual(PointOfInterestDensity.Normal, choice.Density, "a scene never asked opens on Normal");

			choice.Capital = true;
			choice.Density = PointOfInterestDensity.Custom;
			choice.Row(POIType.Village).Max = 2;
			choice.Row(POIType.Cave);
			choice.Row(POIType.Camp).Enabled = false;
			choice.ApplyTo(settings);

			LogAssert.IsTrue(settings.Capital);
			LogAssert.AreEqual(PointOfInterestDensity.Custom, settings.Density);
			LogAssert.AreEqual(2, settings.Overrides.Count, "the untouched Cave row is not an override");
			LogAssert.AreEqual(POIType.Camp, settings.Overrides[0].Kind, "sorted by kind, so the asset does not churn");
			LogAssert.AreEqual(POIType.Village, settings.Overrides[1].Kind);

			PointOfInterestChoice reopened = PointOfInterestChoice.From(settings);
			LogAssert.IsTrue(reopened.Capital, "a re-cut opens on the scene's own answers");
			reopened.Row(POIType.Village).Max = 9;
			LogAssert.AreEqual(2, settings.Overrides[1].Max, "editing the prompt must not write through to the asset before it is accepted");
		}

		[Test]
		public void AtlasWatcher_NoticesPointOfInterestAndMapAssets()
		{
			LogAssert.IsTrue(AtlasAssetWatcher.Touches(new[] { "Assets/Scenes/WorldScenes/Home/Coast Terrain/Coast Points of Interest.asset" }));
			LogAssert.IsTrue(AtlasAssetWatcher.Touches(new[] { "Assets/Templates/World/Atlas/Scenes/Coast POI Settings.asset" }));
			LogAssert.IsTrue(AtlasAssetWatcher.Touches(new[] { WorldMapDefinition.BakedImagePath("Coast") }));
			LogAssert.IsFalse(AtlasAssetWatcher.Touches(new[] { "Assets/Prefabs/Tree.prefab" }));
		}
	}
}
