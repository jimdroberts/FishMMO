using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2m of the biome suite (2026-10-25: decisions 34, 36 (a)–(d), (f), 38, 40, 45) "the deep floor and the shelf": sea-floor
	/// biomes paint their own grounds and grow their rules under the sea; signed depth bands (metres or tides) on ground layers; the tide
	/// a coast asks of its sea; the daylight dimmed with depth on what lies under the sea; underwater site kinds and per-biome site
	/// depths; the Abyss selectable; the Eel shape and Lurk behaviour on the shared clock. Pure, except the painter's (managed Perlin:
	/// nativerun or Unity).
	/// </summary>
	public class BiomeSeaFloorTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders";

		private static string Read(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative)).Replace("\r\n", "\n");

		// ── The pure arithmetic ───────────────────────────────────────

		[Test]
		public void BedShare_ShoreIsTheShoresOwn_GroundTakesNone_ToLowWaterOnlyBelowTheLowestTide()
		{
			for (float depth = -3f; depth <= 12f; depth += 0.37f)
			{
				float shore = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(depth / 1.5f));
				Assert.AreEqual(shore, BiomeSeaFloors.BedShare(BiomeSeaFloor.Shore, shore, depth, 1.5f, 2.5f), "Shore: the very value, bit for bit");
				Assert.AreEqual(0f, BiomeSeaFloors.BedShare(BiomeSeaFloor.Ground, shore, depth, 1.5f, 2.5f), "Ground: never the bed under the sea");
			}
			Assert.AreEqual(0f, BiomeSeaFloors.BedShare(BiomeSeaFloor.ToLowWater, 1f, 2f, 1.5f, 2.5f), "above the lowest tide: its own grounds");
			Assert.AreEqual(1f, BiomeSeaFloors.BedShare(BiomeSeaFloor.ToLowWater, 1f, 6f, 1.5f, 2.5f), 1e-6f, "well below it: the bed");
			Assert.AreEqual(1f, BiomeSeaFloors.BedShare(BiomeSeaFloor.ToLowWater, 1f, 2.6f, 0f, 2.5f), "no fade: a hard line at low water");
		}

		[Test]
		public void DepthBand_IsSigned_OpenAtTheEnds_NaNPasses_AndTidesConvert()
		{
			var band = new Vector2(1f, 30f);
			Assert.AreEqual(1f, BiomeSeaFloors.DepthBand(band, 1f, 10f));
			Assert.AreEqual(0.5f, BiomeSeaFloors.DepthBand(band, 1f, 1f), 1e-5f, "half at a closed end");
			Assert.AreEqual(0f, BiomeSeaFloors.DepthBand(band, 1f, -2f));
			Assert.AreEqual(0f, BiomeSeaFloors.DepthBand(band, 1f, 40f));
			Assert.AreEqual(1f, BiomeSeaFloors.DepthBand(band, 1f, float.NaN), "no sea: every band passes");
			var open = new Vector2(-BiomeSeaFloors.OpenDepth, BiomeSeaFloors.OpenDepth);
			Assert.IsFalse(BiomeSeaFloors.Closes(open));
			Assert.AreEqual(1f, BiomeSeaFloors.DepthBand(open, 1f, 9000f));
			// A marsh zone "from −0.6 to 0.3 tides" (heights) is the depth band (−0.3, 0.6) in tides.
			Assert.AreEqual(new Vector2(-0.3f, 0.6f), BiomeSeaFloors.TideHeightsAsDepths(-0.6f, 0.3f));
			Assert.AreEqual(0.5f, BiomeSeaFloors.InTides(5f, 10f), 1e-6f);
			Assert.AreEqual(50f, BiomeSeaFloors.InTides(5f, 0f), 1e-3f, "a sea with no tide still has a unit");

			var layer = new TerrainTextureLayer();
			Assert.AreEqual(1f, layer.SeaDepthWeight(500f, 2.5f), "off: as before the band existed");
			layer.useSeaDepthBand = true;
			layer.seaDepthRange = new Vector2(-0.3f, 0.6f);
			layer.seaDepthInTides = true;
			layer.seaDepthFalloff = 0f;
			Assert.AreEqual(1f, layer.SeaDepthWeight(-0.5f, 2.5f), "0.5 m above mean sea on a 2.5 m tide: −0.2 tides, inside");
			Assert.AreEqual(0f, layer.SeaDepthWeight(-2f, 2.5f), "−0.8 tides: above the zone");
		}

		[Test]
		public void Tide_CoastStrengthIsOneUnlessAsked_CappedAtFourMetres_AndTheDefaultsAreTheSeas()
		{
			Assert.AreEqual(1f, BiomeSeaFloors.CoastStrength(new[] { 1f, 1f }, new[] { 10f, 3f }));
			Assert.AreEqual(1f, BiomeSeaFloors.CoastStrength(new[] { 4f }, new[] { 0f }), "no coast found: 1");
			Assert.AreEqual(2.5f, BiomeSeaFloors.CoastStrength(new[] { 1f, 4f }, new[] { 1f, 1f }), 1e-5f, "weighted by coast held");
			Assert.AreEqual(4f, BiomeSeaFloors.Strength(9f));
			Assert.AreEqual(1f, BiomeSeaFloors.Strength(float.NaN));
			Assert.AreEqual(BiomeSeaFloors.DefaultTideMetres, BiomeSeaFloors.TideMetres(1f), "strength 1: the sea's own default");
			Assert.AreEqual(BiomeSeaFloors.MaximumTideMetres, BiomeSeaFloors.TideMetres(4f), "an estuary's ×4 stops at 4 m");
			Assert.AreEqual(BiomeSeaFloors.DefaultAmplification, BiomeSeaFloors.Amplification(1f));
			Assert.IsTrue(BiomeSeaFloors.IsAuthoredTide(BiomeSeaFloors.DefaultAmplification, BiomeSeaFloors.DefaultTideMetres), "the defaults may be written over");
			Assert.IsTrue(BiomeSeaFloors.IsAuthoredTide(BiomeSeaFloors.Amplification(4f), BiomeSeaFloors.TideMetres(4f)));
			Assert.IsFalse(BiomeSeaFloors.IsAuthoredTide(3.1f, 7f), "a hand-tuned sea is kept");
			// The constants mirror the water plugin's defaults (FishMMO.Water is not referenced by FishMMO.Shared).
			Assert.AreEqual(FishMMO.Water.WaterEnvironment.DefaultMaximumTideMetres, BiomeSeaFloors.DefaultTideMetres);
			StringAssert.Contains("public float CoastalAmplification = 2.5f;", Read("Assets/Plugins/FishMMO Water/Scripts/WaterEnvironment.cs"));
		}

		[Test]
		public void Daylight_IsExpOfDepth_ZeroWithNoSeaFloorBiome_AndTheShaderAgrees()
		{
			Assert.AreEqual(0f, BiomeSeaFloors.DaylightExtinction(new List<(BiomeSeaLook, float)>()), "no sea-floor biome: no dimming");
			var look = new BiomeSeaLook(new Color(0.02f, 0.03f, 0.05f, 1f), 28f, 1.6f);
			float k = BiomeSeaFloors.DaylightExtinction(new[] { (look, 3f) });
			Assert.AreEqual(BiomeSeaFloors.DaylightShare * look.Extinction, k, 1e-6f);
			Assert.AreEqual(1f, BiomeSeaFloors.Daylight(k, 0f));
			Assert.AreEqual(1f, BiomeSeaFloors.Daylight(0f, 4000f));
			Assert.AreEqual(Mathf.Exp(-k * 100f), BiomeSeaFloors.Daylight(k, 100f), 1e-7f);
			Assert.Less(BiomeSeaFloors.Daylight(k, 4000f), 1e-6f, "an abyss is lightless");

			string weather = Read(Shaders + "/FishWeather.hlsl");
			StringAssert.Contains("half FishSeaDaylight(float3 worldPos)", weather);
			StringAssert.Contains("exp(-_FishOcclusionWater.w * depth)", weather);
			StringAssert.Contains("_FishOcclusionWater.w > 0.0 && depth > 0.0", weather, "exactly 1 with no extinction or above the water");
			StringAssert.Contains("FishSeaDaylight(inputData.positionWS)", Read(Shaders + "/FishGroundLighting.hlsl"));
			StringAssert.Contains("FishSeaDaylight(inputData.positionWS)", Read(Shaders + "/FishVegetationPasses.hlsl"));
			StringAssert.Contains("FishSeaDaylight(inputData.positionWS)", Read(Shaders + "/FishWeatherLitForwardPass.hlsl"));
			StringAssert.Contains("FishSeaDaylight(input.positionWS)", Read(Shaders + "/FishSeaLife.hlsl"));
			StringAssert.Contains("_FishOcclusionWater.w > 0.0 ? half3(1.0, 1.0, 1.0) : behindLight", Read("Assets/Plugins/FishMMO Water/Shaders/FishWaterVolume.shader"),
				"the underwater pass does not dim twice (nor the glow) on a scene whose floor dims its own daylight");
			StringAssert.Contains("SurfaceWater.DaylightExtinction", Read("Assets/Scripts/Client/World/Weather/Presentation/SkyOcclusionMap.cs"));
		}

		[Test]
		public void GrowsUnderSea_OnlyASeaFloorBiomesRulesThatNameTheSea()
		{
			Assert.IsFalse(BiomeSeaFloors.GrowsUnderSea(BiomeSeaFloor.Shore, BiomeBedWaters.Sea));
			Assert.IsFalse(BiomeSeaFloors.GrowsUnderSea(BiomeSeaFloor.Ground, BiomeBedWaters.Default), "a land rule stays a land rule");
			Assert.IsTrue(BiomeSeaFloors.GrowsUnderSea(BiomeSeaFloor.Ground, BiomeBedWaters.Sea));
			Assert.IsTrue(BiomeSeaFloors.GrowsUnderSea(BiomeSeaFloor.ToLowWater, BiomeBedWaters.Any));
			StringAssert.Contains("BiomeSeaFloors.GrowsUnderSea(entry.Biome.SeaFloor, rule.waters)", Read(World + "/Terrain/TerrainScatter.cs"));
		}

		[Test]
		public void SiteDepth_IsTheBiomesWhenItGivesOne_ElseTheKinds()
		{
			Assert.AreEqual(new Vector2(5f, 40f), BiomeSeaFloors.SiteDepth(Vector2.zero, 5f, 40f));
			Assert.AreEqual(new Vector2(3000f, 11000f), BiomeSeaFloors.SiteDepth(new Vector2(3000f, 11000f), 5f, 40f));
			Assert.AreEqual(new Vector2(5f, 40f), BiomeSeaFloors.SiteDepth(new Vector2(10f, 2f), 5f, 40f), "a reversed band is no band");
			string placement = Read(World + "/PointsOfInterest/PointOfInterestPlanner.Placement.cs");
			StringAssert.Contains("Vector2 band = DepthBand(rule, i);", placement);
			StringAssert.Contains("UnderwaterDepth = biome.UnderwaterSiteDepth", Read(World + "/PointsOfInterest/PointOfInterestStage.cs"));
		}

		// ── The painter (managed Perlin: nativerun or Unity) ─────────

		private static SceneTerrainPalette.Entry Entry(int biome, int layer, PaletteRole role, string slot, TerrainTextureLayer source, int ground = -1)
		{
			return new SceneTerrainPalette.Entry { BiomeIndex = biome, LayerIndex = layer, Role = role, Slot = slot, Source = source, Ground = ground };
		}

		/// <summary>A template made without Unity (never registered): only its serialized fields are read.</summary>
		private static BiomeTemplate Template(BiomeSeaFloor floor)
		{
			var template = (BiomeTemplate)FormatterServices.GetUninitializedObject(typeof(BiomeTemplate));
			template.SeaFloor = floor;
			return template;
		}

		private static SceneBiomeField OneBiome(BiomeTemplate template) => SceneBiomeField.FromCells(4, 4, 32f, Vector2.zero, new[] { template }, new byte[16], 16f);

		private static List<SceneTerrainPalette.Entry> Grounds(TerrainTextureLayer main, TerrainTextureLayer detail)
		{
			return new List<SceneTerrainPalette.Entry>
			{
				Entry(0, 0, PaletteRole.Main, "main", main, 0),
				Entry(0, 1, PaletteRole.Submerged, "lakebed", new TerrainTextureLayer()),
				Entry(0, 2, PaletteRole.Detail, "detail/0", detail, 1),
			};
		}

		private static TerrainTextureLayer Detail(Vector2? depth)
		{
			var layer = new TerrainTextureLayer { blendNoiseScale = 1000f, blendSharpness = 0.1f };
			if (depth.HasValue)
			{
				layer.useSeaDepthBand = true;
				layer.seaDepthRange = depth.Value;
				layer.seaDepthFalloff = 0f;
			}
			return layer;
		}

		[Test]
		public void Painter_ShoreBiomes_AreTheOldPaintToTheBit()
		{
			var options = new BiomeSplatOptions { NormalizedHeight = (x, y, z) => 0.5f };
			var unbanded = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 3, Grounds(new TerrainTextureLayer(), Detail(null))), OneBiome(null), options);
			var shore = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 3, Grounds(new TerrainTextureLayer(), Detail(null))), OneBiome(Template(BiomeSeaFloor.Shore)), options);
			var a = new float[3];
			var b = new float[3];
			var random = new System.Random(5);
			for (int i = 0; i < 3000; i++)
			{
				float x = (float)random.NextDouble() * 120f, z = (float)random.NextDouble() * 120f, y = (float)random.NextDouble() * 40f - 20f;
				unbanded.Weigh(x, y, z, 5f, a, BiomeSplatPainter.Weigher.NoClimate);
				shore.Weigh(x, y, z, 5f, b, BiomeSplatPainter.Weigher.NoClimate);
				CollectionAssert.AreEqual(a, b);
			}
			unbanded.Weigh(50f, -8f, 50f, 2f, a, BiomeSplatPainter.Weigher.NoClimate);
			Assert.AreEqual(1f, a[1], 1e-6f, "under the sea a shore biome's lakebed, as always");
		}

		[Test]
		public void Painter_ASeaFloorBiomesOwnGroundsPaintUnderTheSea_ZonedByDepth()
		{
			var options = new BiomeSplatOptions { NormalizedHeight = (x, y, z) => 0.5f };
			// The detail (always strong, its noise flattened) only 10–30 m down.
			var weigher = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 3, Grounds(new TerrainTextureLayer(), Detail(new Vector2(10f, 30f)))),
				OneBiome(Template(BiomeSeaFloor.Ground)), options);
			var w = new float[3];
			weigher.Weigh(50f, -5f, 50f, 2f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.AreEqual(0f, w[1], "no lakebed under the sea");
			Assert.AreEqual(1f, w[0], 1e-5f, "5 m down: its main ground");
			weigher.Weigh(50f, -20f, 50f, 2f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.Greater(w[2], 0.5f, "20 m down: the detail of that depth");
			weigher.Weigh(50f, -50f, 50f, 2f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.AreEqual(0f, w[2], "below its band: none");

			// A main layer banded 0–40 m: deeper the detail carries the ground; where none does the main stays (never a hole).
			var mainBand = new TerrainTextureLayer { useSeaDepthBand = true, seaDepthRange = new Vector2(-BiomeSeaFloors.OpenDepth, 40f), seaDepthFalloff = 0f };
			var deep = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 3, Grounds(mainBand, Detail(null))), OneBiome(Template(BiomeSeaFloor.Ground)), options);
			deep.Weigh(50f, -100f, 50f, 2f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.AreEqual(0f, w[0], 1e-6f, "outside its band the main gives way");
			Assert.AreEqual(1f, w[2], 1e-5f, "to the detail");
		}

		[Test]
		public void Painter_AnEstuaryPaintsItsOwnGroundsDownToLowWater()
		{
			var options = new BiomeSplatOptions { NormalizedHeight = (x, y, z) => 0.5f, TideMetres = 4f };
			var weigher = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 3, Grounds(new TerrainTextureLayer(), Detail(new Vector2(99f, 100f)))),
				OneBiome(Template(BiomeSeaFloor.ToLowWater)), options);
			var w = new float[3];
			weigher.Weigh(50f, -2f, 50f, 2f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.AreEqual(1f, w[0], 1e-5f, "2 m down on a 4 m tide: the flats the ebb lays bare");
			weigher.Weigh(50f, -9f, 50f, 2f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.AreEqual(1f, w[1], 1e-5f, "below low water: the lakebed");
		}

		// ── Template, spec and authoring ─────────────────────────────

		[Test]
		public void Defaults_EverySpecAndEveryAsset_ChangeNothing()
		{
			Assert.AreEqual(83, BiomeArtSpec.Entries.Length);
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.AreEqual(BiomeSeaFloor.Shore, entry.SeaFloorKind, entry.Biome);
				Assert.AreEqual(1f, entry.TideStrength, entry.Biome);
				Assert.AreEqual(Vector2.zero, entry.UnderwaterSiteDepth, entry.Biome);
				foreach (BiomeArtSpec.Layer layer in Layers(entry))
				{
					Assert.IsFalse(layer.SeaDepth.HasValue, entry.Biome + ": no depth band until its round");
				}
			}
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string yaml = File.ReadAllText(path);
				Assert.IsFalse(Regex.IsMatch(yaml, @"^\s*seaFloor: [12]", RegexOptions.Multiline), path);
				Assert.IsFalse(Regex.IsMatch(yaml, @"^\s*useSeaDepthBand: 1", RegexOptions.Multiline), path);
				Match tide = Regex.Match(yaml, @"^\s*tideStrength: (\S+)", RegexOptions.Multiline);
				Assert.IsTrue(!tide.Success || tide.Groups[1].Value == "1", path);
			}
		}

		private static IEnumerable<BiomeArtSpec.Layer> Layers(BiomeArtSpec.Entry entry)
		{
			if (entry.Main != null)
			{
				yield return entry.Main;
			}
			foreach (BiomeArtSpec.Layer layer in entry.Details)
			{
				if (layer != null)
				{
					yield return layer;
				}
			}
		}

		[Test]
		public void Spec_DepthAndTideBands_AndTheirMark_RoundTrip()
		{
			var depth = new BiomeArtSpec.Layer { Family = "X" }.Depth(1f, 30f);
			Assert.AreEqual(new Vector2(1f, 30f), depth.SeaDepth.Value);
			Assert.IsFalse(depth.SeaDepthInTides);
			Assert.IsFalse(new BiomeArtSpec.Layer().Depth(0f, 0f).SeaDepth.HasValue, "both ends open: no band");
			Assert.AreEqual(-BiomeSeaFloors.OpenDepth, new BiomeArtSpec.Layer().Depth(0f, 40f).SeaDepth.Value.x, "0 leaves an end open");
			var tide = new BiomeArtSpec.Layer().Tide(-0.6f, 0.3f);
			Assert.IsTrue(tide.SeaDepthInTides);
			Assert.AreEqual(new Vector2(-0.3f, 0.6f), tide.SeaDepth.Value);

			Assert.AreEqual(string.Empty, BiomeArtAuthoring.ClimateMark(new BiomeArtSpec.Layer()), "no band: the mark every layer had");
			string mark = BiomeArtAuthoring.ClimateMark(tide);
			StringAssert.Contains("|s", mark);
			StringAssert.EndsWith(",t", mark);
			var layer = new TerrainTextureLayer();
			BiomeArtAuthoring.WriteClimateBand(layer, tide);
			Assert.IsTrue(layer.useSeaDepthBand && layer.seaDepthInTides);
			Assert.AreEqual(mark, BiomeArtAuthoring.ClimateMark(layer), "what is written reads back as its mark");
			BiomeArtAuthoring.WriteClimateBand(layer, new BiomeArtSpec.Layer());
			Assert.IsFalse(layer.useSeaDepthBand);
			Assert.AreEqual(string.Empty, BiomeArtAuthoring.ClimateMark(layer));
		}

		[Test]
		public void Spec_SeaFloorTideAndSiteColumns_AndTheirWiring()
		{
			var entry = new BiomeArtSpec.Entry { Biome = "Test" }.UnderTheSea().Tides(9f).SitesBetween(6000f, 3000f);
			Assert.AreEqual(BiomeSeaFloor.Ground, entry.SeaFloorKind);
			Assert.AreEqual(4f, entry.TideStrength);
			Assert.AreEqual(new Vector2(3000f, 6000f), entry.UnderwaterSiteDepth);
			Assert.AreEqual(BiomeSeaFloor.ToLowWater, new BiomeArtSpec.Entry { Biome = "Estuary" }.UnderTheSea(BiomeSeaFloor.ToLowWater).SeaFloorKind);

			string authoring = Read(World + "/ProceduralArt/BiomeArtAuthoring.cs");
			StringAssert.Contains("AuthorSeaFloor(biome, entry, report, apply);", authoring);
			StringAssert.Contains("BiomeTemplate.Authored.UnderwaterSiteDepth", authoring);
			string spec = Read(World + "/ProceduralArt/BiomeArtSpec.cs");
			StringAssert.Contains("float slopeMax = 40f, float slopeMin = 0f)", spec, "Sea() takes a least slope (decision 38)");
			string generator = Read(World + "/SceneGenerator.cs");
			int apply = generator.IndexOf("SceneSeaFloor.Apply(scene, field, terrains, plan, result.Notes)", StringComparison.Ordinal);
			int splat = generator.IndexOf("BiomeSplatReport splat = BiomeSplatPainter.Paint(tiles, palette, field, options);", StringComparison.Ordinal);
			Assert.That(apply, Is.GreaterThan(0).And.LessThan(splat), "the sea's tide is written before the splat reads it");
			StringAssert.Contains("TideMetres = seaTide,", generator);
			string art = Read(World + "/ProceduralArt/BiomeArtGenerator.cs");
			StringAssert.Contains("if (aquatic && spec.Glow.maxColorComponent > 0f)", art, "glowing sea details on the cave glow's material");
		}

		// ── Underwater sites ──────────────────────────────────────────

		private static readonly POIType[] SeaFloorKinds = { POIType.SunkenShrine, POIType.SeaMonsterDen, POIType.SeaCrystalFormation, POIType.SunkenMonument, POIType.SunkenStatue };

		[Test]
		public void UnderwaterKinds_AreUnderwater_DeepRanged_HostedByNoBiomeYet_AndNamed()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			foreach (POIType kind in SeaFloorKinds)
			{
				Assert.IsTrue(PointOfInterestKinds.Info(kind).Has(PointOfInterestTraits.Underwater), kind.ToString());
				Assert.AreEqual(kind.ToString(), ((MapMarkerType)Enum.Parse(typeof(MapMarkerType), kind.ToString())).ToString());
				PointOfInterestKindRule rule = rules.RuleFor(kind);
				Assert.IsNotNull(rule, kind.ToString());
				Assert.IsTrue(rule.Underwater, kind.ToString());
				Assert.AreEqual(PointOfInterestKindRule.Unbounded, rule.MaxDepth, kind + ": no depth cap (the biome's band narrows it)");
				Assert.AreEqual(0f, rule.DefaultBiomeWeight, kind + ": no biome hosts it until its round, so no scene changes");
				Assert.IsNotEmpty(PointOfInterestTemplateSpecs.For(kind), kind + ": placeable with the land kinds' pieces");
				Assert.GreaterOrEqual(PlaceNameDefaults.TypeWordsFor(kind).Length, 6, kind.ToString());
			}
			foreach (string biome in new[] { "Abyss", "Coastal Water", "Grassland" })
			{
				foreach ((POIType kind, float weight) in rules.KindWeightsFor(biome))
				{
					Assert.IsFalse(Array.IndexOf(SeaFloorKinds, kind) >= 0 && weight > 0f, $"{biome} hosts {kind} before its round");
				}
			}
			// Appended, never inserted: the ordinals are serialized.
			Assert.AreEqual((int)POIType.LumberMill + 1, (int)POIType.SunkenShrine);
		}

		[Test]
		public void Abyss_IsSelectable_AsTheHadalBand()
		{
			BiomeSpecTable.Entry abyss = Array.Find(BiomeSpecTable.Entries, e => e.Name == "Abyss");
			Assert.IsNotNull(abyss);
			StringAssert.Contains("new Entry(\"Abyss\", 0, 0f, 0.14f, -1f, 1f, -1f, 1f, 1.2f, A.Living, true, R.None)", Read(World + "/BiomeSpecTable.cs"));
			Assert.IsTrue(File.Exists(BiomeFolder + "/Abyss.asset"));
		}

		// ── Sea life: the Eel and the Lurk ───────────────────────────

		[Test]
		public void Lurk_RestsInItsDen_ThenDartsOutAndBack_OnTheSharedClock()
		{
			const double period = 30.0;
			float rested = 0f;
			int samples = 0, outside = 0;
			for (double t = 0.0; t < period; t += 0.05)
			{
				float d = SeaLifePlacement.LurkAt(t, period, 0.37f);
				Assert.That(d, Is.InRange(0f, 1f));
				Assert.AreEqual(d, SeaLifePlacement.LurkAt(t + period * 1000.0, period, 0.37f), 1e-3f, "a pure function of the clock");
				samples++;
				outside += d > 0f ? 1 : 0;
				rested = Mathf.Max(rested, d);
			}
			Assert.AreEqual(SeaLifePlacement.LurkDartShare, outside / (float)samples, 0.02f, "out of its den only for the dart");
			Assert.Greater(rested, 0.99f, "a full dart");
			Assert.IsTrue(SeaLifePlacement.OnFloor(new SeaCreatureKind { Behaviour = SeaCreatureBehaviour.Lurk }));
			Assert.IsFalse(SeaLifePlacement.OnFloor(new SeaCreatureKind { Behaviour = SeaCreatureBehaviour.School, NearFloor = true }), "a near-floor swimmer still swims");
			Assert.AreEqual((int)SeaCreatureShape.Crab + 1, (int)SeaCreatureShape.Eel, "appended");
			Assert.AreEqual((int)SeaCreatureBehaviour.Crawl + 1, (int)SeaCreatureBehaviour.Lurk, "appended");
			StringAssert.Contains("Eel, 6, ", Read(Shaders + "/FishSeaLife.shader")); // 2026-10-25: Mola 7, Nautilus 8 appended after it
			StringAssert.Contains("case SeaCreatureShape.Eel: return 6f;", Read("Assets/Scripts/Client/World/SeaLife/SeaLifeSystem.cs"));
			StringAssert.Contains("h = h * 31 + (kind.NearFloor ? 1 : 0);", Read("Assets/Scripts/Client/World/SeaLife/SeaLifeSystem.cs"), "a change re-places the cells");
		}
	}
}
