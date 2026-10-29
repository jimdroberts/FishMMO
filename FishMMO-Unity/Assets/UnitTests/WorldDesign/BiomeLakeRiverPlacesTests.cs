using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Decision 35 of the biome suite (2026-10-18, phase I2l "lakes and rivers as places"): bed rules under a lake or a river measure
	/// their depth below THAT water's surface; up to three extra bed layers a biome chosen by depth (and a river's current); the
	/// riverbed painted under rivers and the lakebed under lakes and the sea (the riverbed was never painted while a lakebed
	/// existed); aquatic plants stream with a river's current; crossings choose ford, bridge or rope ferry and a bridge belongs to the
	/// banks it joins; in-stream, bank-straddling, over-shallows and lake-sunken sites; inland traffic timed on the shared clock.
	/// Pure, except the painter's (managed Perlin noise: nativerun or Unity).
	/// </summary>
	public class BiomeLakeRiverPlacesTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string ScatterSource = World + "/Terrain/TerrainScatter.cs";
		private const string GeneratorSource = World + "/SceneGenerator.cs";
		private const string PaletteSource = World + "/Terrain/SceneTerrainPalette.cs";
		private const string StageSource = World + "/PointsOfInterest/PointOfInterestStage.cs";
		private const string RulesSource = World + "/PointsOfInterest/PointOfInterestRules.cs";
		private const string PlacementSource = World + "/PointsOfInterest/PointOfInterestPlanner.Placement.cs";
		private const string VegetationSource = "Assets/Prefabs/Client/Weather/Shaders/FishVegetationPasses.hlsl";
		private const string ChannelSource = "Assets/Prefabs/Client/Weather/Shaders/FishChannelFlow.hlsl";
		private const string RendererSource = "Assets/Plugins/FishMMO Water/Scripts/InlandWaterRenderer.cs";
		private const string RendererChannelSource = "Assets/Plugins/FishMMO Water/Scripts/InlandWaterRenderer.Channel.cs";

		// ── The water over the bed ────────────────────────────────────

		[Test]
		public void BedWater_IsTheSeasTheLakesOrTheRiversBelowItsOwnSurface()
		{
			BedWater sea = BedWater.At(-6f, true, 0f, float.NegativeInfinity, Vector3.zero);
			Assert.That((sea.Sea, sea.Lake, sea.River), Is.EqualTo((1f, 0f, 0f)));
			Assert.That(sea.Depth, Is.EqualTo(6f));
			Assert.That(sea.SeaOnly, Is.True);

			BedWater lake = BedWater.At(47f, true, 0f, 50f, Vector3.zero);
			Assert.That((lake.Sea, lake.Lake, lake.River), Is.EqualTo((0f, 1f, 0f)));
			Assert.That(lake.Depth, Is.EqualTo(3f), "below the lake's level, not the sea's");
			Assert.That(lake.Speed, Is.EqualTo(0f));

			BedWater river = BedWater.At(119f, true, 0f, 120.5f, new Vector3(1f, 1.3f, 0f));
			Assert.That((river.Sea, river.Lake, river.River), Is.EqualTo((0f, 0f, 1f)));
			Assert.That(river.Depth, Is.EqualTo(1.5f));
			Assert.That(river.Speed, Is.EqualTo(1.3f));

			BedWater mouth = BedWater.At(1f, true, 0f, 3f, new Vector3(0.25f, 0.8f, 1f));
			Assert.That(mouth.River, Is.EqualTo(0.25f));
			Assert.That(mouth.Sea, Is.EqualTo(0.75f), "a mouth on the coast hands over to the sea, not a lake");
			Assert.That(mouth.Lake, Is.EqualTo(0f));

			// Water the sea's high tide reaches is the sea's: an estuary's channel is sea floor, by its depth below mean sea level.
			BedWater estuary = BedWater.At(-4f, true, 0f, 0.1f, new Vector3(1f, 0.8f, 1f));
			Assert.That((estuary.Sea, estuary.Lake, estuary.River), Is.EqualTo((1f, 0f, 0f)));
			Assert.That(estuary.Depth, Is.EqualTo(4f));
			Assert.That(BiomeBedBands.IsSeas(2.5f, 0f), Is.True);
			Assert.That(BiomeBedBands.IsSeas(2.6f, 0f), Is.False);
			Assert.That(BedWater.At(-4f, false, 0f, 0.1f, Vector3.zero).Lake, Is.EqualTo(1f), "no sea: a low lake is a lake");

			BedWater wash = BedWater.At(30f, true, 0f, float.PositiveInfinity, Vector3.zero);
			Assert.That(wash.River, Is.EqualTo(1f), "a dry wash is a river's channel");
			Assert.That(wash.Depth, Is.EqualTo(0f));

			Assert.That(BedWater.At(30f, false, 0f, float.NegativeInfinity, Vector3.zero).Any, Is.False);
			Assert.That(river.Share(BiomeBedWaters.Inland), Is.EqualTo(1f));
			Assert.That(river.Share(BiomeBedWaters.Sea | BiomeBedWaters.Lake), Is.EqualTo(0f));
			Assert.That(river.Share(BiomeBedWaters.Default), Is.EqualTo(0f));
		}

		/// <summary>
		/// (a) A bed rule under a lake or a river grows by its depth below THAT water's surface: under a lake at 50 m a rule for
		/// 1–6 m grows 3 m down and not 0.2 m down; a lakebed's rules keep to the sea unless they say otherwise.
		/// </summary>
		[Test]
		public void InlandRules_MeasureDepthBelowTheirOwnWater()
		{
			var band = new Vector2(1f, 6f);
			BedWater deep = BedWater.At(47f, true, 0f, 50f, Vector3.zero);
			BedWater skin = BedWater.At(49.85f, true, 0f, 50f, Vector3.zero);
			BedWater dry = BedWater.At(50.5f, true, 0f, 50f, Vector3.zero);
			Assert.That(BiomeBedBands.InlandRule(deep, BiomeBedWaters.Lake, -0.05f, -0.25f, band, Vector2.zero), Is.EqualTo(1f));
			Assert.That(BiomeBedBands.InlandRule(skin, BiomeBedWaters.Lake, -0.05f, -0.25f, band, Vector2.zero), Is.EqualTo(0f), "above its band");
			Assert.That(BiomeBedBands.InlandRule(dry, BiomeBedWaters.Lake, -0.05f, -0.25f, Vector2.zero, Vector2.zero), Is.EqualTo(0f), "on the bank");
			Assert.That(BiomeBedBands.InlandRule(skin, BiomeBedWaters.Lake, -0.05f, -0.25f, Vector2.zero, Vector2.zero), Is.EqualTo(0.5f).Within(1e-4f), "half way through its fade, 15 cm under the surface");
			Assert.That(BiomeBedBands.InlandRule(deep, BiomeBedWaters.Sea, -0.05f, -0.25f, band, Vector2.zero), Is.EqualTo(0f), "a sea rule grows under no lake");
			Assert.That(BiomeBedBands.InlandRule(deep, BiomeBedWaters.River, -0.05f, -0.25f, band, Vector2.zero), Is.EqualTo(0f));

			BedWater quick = BedWater.At(118f, true, 0f, 120f, new Vector3(1f, 1.8f, 0f));
			BedWater slow = BedWater.At(118f, true, 0f, 120f, new Vector3(1f, 0.3f, 0f));
			Assert.That(BiomeBedBands.InlandRule(quick, BiomeBedWaters.River, -0.05f, -0.25f, Vector2.zero, new Vector2(0.8f, 0f)), Is.EqualTo(1f));
			Assert.That(BiomeBedBands.InlandRule(slow, BiomeBedWaters.River, -0.05f, -0.25f, Vector2.zero, new Vector2(0.8f, 0f)), Is.EqualTo(0f), "too slow for a torrent alga");
			Assert.That(BiomeBedBands.InlandRule(BedWater.At(30f, true, 0f, float.PositiveInfinity, Vector3.zero), BiomeBedWaters.Any, -0.05f, -0.25f, Vector2.zero, Vector2.zero),
				Is.EqualTo(0f), "nothing aquatic in a dry wash");

			// The sea-floor band is the scatter's, read below any surface: the same numbers at the sea as TerrainScatter's.
			for (float y = -50f; y <= 2f; y += 0.37f)
			{
				foreach (Vector2 b in new[] { new Vector2(4f, 30f), new Vector2(0f, 3f), new Vector2(8f, 0f), Vector2.zero })
				{
					Assert.That(BiomeBedBands.DepthBand(b, -y), Is.EqualTo(TerrainScatter.DepthBand(b, y)), $"band {b} at {y}");
				}
			}
		}

		[Test]
		public void RuleWaters_DefaultToTheirSlots()
		{
			Assert.That(BiomeBedBands.RuleWaters(BiomeBedWaters.Default, BiomeBedBands.LakebedWaters), Is.EqualTo(BiomeBedWaters.Sea), "the lakebed's rules: the sea only, as always");
			Assert.That(BiomeBedBands.RuleWaters(BiomeBedWaters.Default, BiomeBedBands.RiverbedWaters), Is.EqualTo(BiomeBedWaters.River));
			Assert.That(BiomeBedBands.RuleWaters(BiomeBedWaters.Lake, BiomeBedBands.LakebedWaters), Is.EqualTo(BiomeBedWaters.Lake));
			Assert.That(BiomeBedBands.RuleWaters(BiomeBedWaters.Default, BiomeBedWaters.Default), Is.EqualTo(BiomeBedWaters.Any));
			Assert.That(new PrefabSpawnRule().waters, Is.EqualTo(BiomeBedWaters.Default));
			Assert.That(new PrefabSpawnRule().currentRange, Is.EqualTo(Vector2.zero));
			Assert.That((int)BiomeBedWaters.Sea, Is.EqualTo(1));
			Assert.That((int)BiomeBedWaters.Lake, Is.EqualTo(2));
			Assert.That((int)BiomeBedWaters.River, Is.EqualTo(4));
		}

		// ── Bed layers by depth (b) and the riverbed (h) ──────────────

		[Test]
		public void Bands_AreCentred_AndOpenAtZero()
		{
			var halde = new Vector2(3f, 9f);
			Assert.That(BiomeBedBands.Band(halde, 0.5f, 6f), Is.EqualTo(1f));
			Assert.That(BiomeBedBands.Band(halde, 0.5f, 3f), Is.EqualTo(0.5f).Within(1e-6f));
			Assert.That(BiomeBedBands.Band(halde, 0.5f, 9f), Is.EqualTo(0.5f).Within(1e-6f));
			Assert.That(BiomeBedBands.Band(halde, 0.5f, 2.7f), Is.EqualTo(0f));
			Assert.That(BiomeBedBands.Band(new Vector2(9f, 0f), 0.5f, 400f), Is.EqualTo(1f), "an open deep end");
			Assert.That(BiomeBedBands.Band(new Vector2(0f, 3f), 0.5f, 0f), Is.EqualTo(1f), "an open shallow end");
			Assert.That(BiomeBedBands.Band(Vector2.zero, 0.5f, 123f), Is.EqualTo(1f));
			Assert.That(BiomeBedBands.Band(halde, 0f, 3f), Is.EqualTo(1f), "a hard edge holds its end");
			Assert.That(BiomeBedBands.Band(halde, 0.5f, float.NaN), Is.EqualTo(1f));
		}

		[Test]
		public void ABedLayer_LiesUnderItsWatersWithinItsBands()
		{
			var reef = new BedTextureLayer { waters = BiomeBedWaters.River, depthRange = new Vector2(0f, 1.2f), useCurrentBand = true, currentRange = new Vector2(1.6f, 0f) };
			Assert.That(reef.Strength(BedWater.At(119.5f, true, 0f, 120f, new Vector3(1f, 2.2f, 0f))), Is.EqualTo(1f));
			Assert.That(reef.Strength(BedWater.At(119.5f, true, 0f, 120f, new Vector3(1f, 0.6f, 0f))), Is.EqualTo(0f), "slow water");
			Assert.That(reef.Strength(BedWater.At(118.8f, true, 0f, 120f, new Vector3(1f, 2.2f, 0f))), Is.EqualTo(0.5f).Within(1e-4f), "half at its stated depth");
			Assert.That(reef.Strength(BedWater.At(116f, true, 0f, 120f, new Vector3(1f, 2.2f, 0f))), Is.EqualTo(0f), "too deep");
			Assert.That(reef.Strength(BedWater.At(49f, true, 0f, 50f, Vector3.zero)), Is.EqualTo(0f), "under a lake");
			var any = new BedTextureLayer { waters = BiomeBedWaters.Default };
			Assert.That(any.Strength(BedWater.At(-3f, true, 0f, float.NegativeInfinity, Vector3.zero)), Is.EqualTo(1f), "nothing ticked: every water");
			var fresh = new BedTextureLayer();
			Assert.That(fresh.waters, Is.EqualTo(BiomeBedWaters.Inland), "a new bed layer: lakes and rivers");
			Assert.That(fresh.Strength(BedWater.At(-3f, true, 0f, float.NegativeInfinity, Vector3.zero)), Is.EqualTo(0f));
		}

		/// <summary>(h) The default bed: the riverbed takes a river's share, the lakebed a lake's or the sea's; no river share is exactly as before.</summary>
		[Test]
		public void Split_GivesTheRiverbedTheRiver_AndTheLakebedTheRest()
		{
			var none = Array.Empty<float>();
			BedWater sea = BedWater.At(-6f, true, 0f, float.NegativeInfinity, Vector3.zero);
			float taken = BiomeBedBands.Split(sea, true, true, none, 0, none, out float lake, out float river);
			Assert.That((taken, lake, river), Is.EqualTo((1f, 1f, 0f)), "the sea: the lakebed, exactly 1");
			BiomeBedBands.Split(BedWater.At(47f, true, 0f, 50f, Vector3.zero), true, true, none, 0, none, out lake, out river);
			Assert.That((lake, river), Is.EqualTo((1f, 0f)));
			BiomeBedBands.Split(BedWater.At(119f, true, 0f, 120f, new Vector3(1f, 1f, 0f)), true, true, none, 0, none, out lake, out river);
			Assert.That((lake, river), Is.EqualTo((0f, 1f)), "a river: its riverbed (it lay on the lakebed before)");
			BiomeBedBands.Split(BedWater.At(119f, true, 0f, 120f, new Vector3(0.3f, 1f, 0f)), true, true, none, 0, none, out lake, out river);
			Assert.That(lake + river, Is.EqualTo(1f).Within(1e-6f));
			Assert.That(river, Is.EqualTo(0.3f).Within(1e-6f), "a mouth hands over");
			BiomeBedBands.Split(BedWater.At(119f, true, 0f, 120f, new Vector3(1f, 1f, 0f)), true, false, none, 0, none, out lake, out river);
			Assert.That((lake, river), Is.EqualTo((1f, 0f)), "no riverbed of its own: the lakebed takes the river too (as one channel)");
			BiomeBedBands.Split(sea, false, true, none, 0, none, out lake, out river);
			Assert.That((lake, river), Is.EqualTo((0f, 1f)), "no lakebed: the riverbed takes all, as when it was the only bed");
			Assert.That(BiomeBedBands.Split(sea, false, false, none, 0, none, out _, out _), Is.EqualTo(0f), "no bed: the ground keeps it");

			var strengths = new[] { 0.5f, 0.5f };
			var shares = new float[2];
			taken = BiomeBedBands.Split(BedWater.At(44f, true, 0f, 50f, Vector3.zero), true, true, strengths, 2, shares, out lake, out river);
			Assert.That(shares[0] + shares[1], Is.EqualTo(0.75f).Within(1e-6f), "two halves cover three quarters");
			Assert.That(lake, Is.EqualTo(0.25f).Within(1e-6f));
			Assert.That(taken, Is.EqualTo(1f));
			taken = BiomeBedBands.Split(sea, false, false, strengths, 2, shares, out _, out _);
			Assert.That(taken, Is.EqualTo(0.75f).Within(1e-6f), "extras alone take only their union");
		}

		[Test]
		public void RiverBedShare_RunsOutIntoTheLakeAtItsMouth()
		{
			RiverPath river = Straight(11, RiverEnd.Lake, width: 10f);
			Vector3 upstream = SceneWater.RiverBedShare(river, 1, 0f);
			Assert.That(upstream.x, Is.EqualTo(1f));
			Assert.That(upstream.y, Is.EqualTo(river.SpeedAt(1)).Within(1e-5f));
			Assert.That(upstream.z, Is.EqualTo(0f));
			Vector3 end = SceneWater.RiverBedShare(river, river.Count - 2, 1f);
			Assert.That(end.x, Is.EqualTo(0f), "at the mouth the lake's bed");
			float last = 1f;
			for (float s = 60f; s <= 100f; s += 2f)
			{
				int i = Mathf.Min(river.Count - 2, Mathf.FloorToInt(s / 10f));
				float share = SceneWater.RiverBedShare(river, i, (s - i * 10f) / 10f).x;
				Assert.That(share, Is.LessThanOrEqualTo(last + 1e-6f), "falling steadily into the lake");
				last = share;
			}
			Assert.That(SceneWater.RiverBedShare(Straight(11, RiverEnd.Sea, 10f), 9, 1f).z, Is.EqualTo(1f), "a coast mouth hands over to the sea");
			Assert.That(SceneWater.RiverBedShare(Straight(11, RiverEnd.Confluence, 10f), 9, 1f).x, Is.EqualTo(1f), "into another river: still a river's bed");
		}

		private static RiverPath Straight(int n, RiverEnd end, float width)
		{
			var river = new RiverPath
			{
				End = end,
				X = new float[n], Z = new float[n], S = new float[n], Surface = new float[n], Bed = new float[n],
				Width = new float[n], Depth = new float[n], Discharge = new float[n],
			};
			for (int i = 0; i < n; i++)
			{
				river.X[i] = 0f;
				river.Z[i] = i * 10f;
				river.S[i] = i * 10f;
				river.Surface[i] = 100f - i * 0.1f;
				river.Bed[i] = river.Surface[i] - 2f;
				river.Width[i] = width;
				river.Depth[i] = 2f;
				river.Discharge[i] = 20f + i;
			}
			return river;
		}

		// ── The painter (managed Perlin noise: nativerun or Unity) ──

		private static SceneTerrainPalette.Entry Entry(int biome, int layer, PaletteRole role, string slot, TerrainTextureLayer source, int ground = -1)
		{
			return new SceneTerrainPalette.Entry { BiomeIndex = biome, LayerIndex = layer, Role = role, Slot = slot, Source = source, Ground = ground };
		}

		private static SceneBiomeField OneBiome() => SceneBiomeField.FromCells(4, 4, 32f, Vector2.zero, new BiomeTemplate[] { null }, new byte[16], 16f);

		private static List<SceneTerrainPalette.Entry> Beds(bool riverbed, BedTextureLayer extra = null)
		{
			var entries = new List<SceneTerrainPalette.Entry>
			{
				Entry(0, 0, PaletteRole.Main, "main", new TerrainTextureLayer(), 0),
				Entry(0, 1, PaletteRole.Submerged, "lakebed", new TerrainTextureLayer()),
			};
			if (riverbed)
			{
				entries.Add(Entry(0, 2, PaletteRole.Submerged, "riverbed", new TerrainTextureLayer()));
			}
			if (extra != null)
			{
				entries.Add(Entry(0, 3, PaletteRole.Submerged, "bed/0", extra));
			}
			return entries;
		}

		/// <summary>(h) Under a river the riverbed, under a lake and the sea the lakebed; a biome's ground on its banks as before.</summary>
		[Test]
		public void Painter_PaintsTheRiverbedUnderRivers_AndTheLakebedUnderLakesAndTheSea()
		{
			var options = new BiomeSplatOptions
			{
				NormalizedHeight = (x, y, z) => 0.5f,
				// A river along x < 40 at 100 m, a lake beyond x > 80 at 90 m.
				InlandWaterSurface = (x, z) => x < 40f ? 100f : x > 80f ? 90f : float.NegativeInfinity,
				RiverAt = (x, z) => x < 40f ? new Vector3(1f, 1.2f, 0f) : Vector3.zero,
			};
			var weigher = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 4, Beds(true)), OneBiome(), options);
			var w = new float[4];
			weigher.Weigh(20f, 98f, 50f, 2f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.That(w[2], Is.EqualTo(1f).Within(1e-6f), "under the river: its riverbed");
			Assert.That(w[1], Is.EqualTo(0f));
			weigher.Weigh(100f, 85f, 50f, 2f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.That(w[1], Is.EqualTo(1f).Within(1e-6f), "under the lake: its lakebed");
			weigher.Weigh(60f, -8f, 50f, 2f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.That(w[1], Is.EqualTo(1f).Within(1e-6f), "under the sea: the lakebed");
			weigher.Weigh(60f, 30f, 50f, 2f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.That(w[0], Is.EqualTo(1f).Within(1e-6f), "dry ground: the main layer");
		}

		/// <summary>A biome with no riverbed of its own (or one on the lakebed's art), and every scene with no river: painted exactly as before, to the bit.</summary>
		[Test]
		public void Painter_WithoutARiverbedOrARiver_IsTheOldPaintToTheBit()
		{
			var plain = new BiomeSplatOptions { NormalizedHeight = (x, y, z) => 0.5f, InlandWaterSurface = (x, z) => x > 64f ? 40f : float.NegativeInfinity };
			var rivered = new BiomeSplatOptions { NormalizedHeight = plain.NormalizedHeight, InlandWaterSurface = plain.InlandWaterSurface, RiverAt = (x, z) => Vector3.zero };
			var before = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 4, Beds(false)), OneBiome(), plain);
			// The riverbed on the lakebed's channel: one channel, the whole bed, as before.
			List<SceneTerrainPalette.Entry> shared = Beds(false);
			shared.Add(Entry(0, 1, PaletteRole.Submerged, "riverbed", new TerrainTextureLayer()));
			var sameArt = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 4, shared), OneBiome(),
				new BiomeSplatOptions { NormalizedHeight = plain.NormalizedHeight, InlandWaterSurface = plain.InlandWaterSurface, RiverAt = (x, z) => new Vector3(1f, 1f, 0f) });
			var noRiver = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 4, Beds(true)), OneBiome(), rivered);
			var a = new float[4];
			var b = new float[4];
			var c = new float[4];
			var random = new System.Random(35);
			for (int i = 0; i < 3000; i++)
			{
				float x = (float)random.NextDouble() * 128f, z = (float)random.NextDouble() * 128f;
				float y = (float)(random.NextDouble() * 60.0 - 10.0), slope = (float)random.NextDouble() * 50f;
				before.Weigh(x, y, z, slope, a, BiomeSplatPainter.Weigher.NoClimate);
				sameArt.Weigh(x, y, z, slope, b, BiomeSplatPainter.Weigher.NoClimate);
				noRiver.Weigh(x, y, z, slope, c, BiomeSplatPainter.Weigher.NoClimate);
				for (int l = 0; l < 4; l++)
				{
					Assert.That(b[l], Is.EqualTo(a[l]), $"same art, layer {l} at ({x}, {y}, {z})");
					Assert.That(c[l], Is.EqualTo(a[l]), $"no river, layer {l} at ({x}, {y}, {z})");
				}
			}
		}

		/// <summary>(b) A lake's shelf, slope and deeps: each bed layer by its depth below the lake's own level, the lakebed what they leave.</summary>
		[Test]
		public void Painter_LaysTheBedLayersByDepthBelowTheLake()
		{
			var halde = new BedTextureLayer { waters = BiomeBedWaters.Lake, depthRange = new Vector2(3f, 9f), depthFalloff = 0.5f };
			var options = new BiomeSplatOptions { NormalizedHeight = (x, y, z) => 0.5f, InlandWaterSurface = (x, z) => 200f, RiverAt = (x, z) => Vector3.zero };
			var weigher = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 4, Beds(true, halde)), OneBiome(), options);
			var w = new float[4];
			weigher.Weigh(50f, 199f, 50f, 1f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.That(w[1], Is.EqualTo(1f).Within(1e-6f), "the shelf: the lakebed");
			weigher.Weigh(50f, 194f, 50f, 1f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.That(w[3], Is.EqualTo(1f).Within(1e-6f), "6 m down: the Halde");
			weigher.Weigh(50f, 197f, 50f, 1f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.That(w[3], Is.EqualTo(0.5f).Within(1e-5f), "half at its stated end");
			Assert.That(w[1] + w[3], Is.EqualTo(1f).Within(1e-5f));
			var sea = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 4, Beds(true, halde)), OneBiome(),
				new BiomeSplatOptions { NormalizedHeight = (x, y, z) => 0.5f });
			sea.Weigh(50f, -6f, 50f, 1f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.That(w[3], Is.EqualTo(0f), "a lake's bed layer is not the sea's");
		}

		// ── Crossings (i) ─────────────────────────────────────────────

		[Test]
		public void Crossings_FordBridgeOrFerry_ByTheWaysClass()
		{
			Assert.That(PathCrossings.Choose(true, 300f, 40f), Is.EqualTo(PathCrossingKind.Ford));
			Assert.That(PathCrossings.Choose(false, 30f, 40f), Is.EqualTo(PathCrossingKind.Bridge));
			Assert.That(PathCrossings.Choose(false, 60f, 40f), Is.EqualTo(PathCrossingKind.Ferry));
			Assert.That(PathCrossings.Choose(false, 900f, float.PositiveInfinity), Is.EqualTo(PathCrossingKind.Bridge), "no ferries asked: a bridge, as before");
			Assert.That(PathCrossings.FerryOverMetres(ScenePathClass.Footpath), Is.LessThan(PathCrossings.FerryOverMetres(ScenePathClass.CartTrack)));
			Assert.That(PathCrossings.FerryOverMetres(ScenePathClass.Highway), Is.EqualTo(PathCrossings.MaxBridgeSpanMetres));
			Assert.That(PathCrossings.MaxBridgeSpanMetres, Is.EqualTo(7f * 32f));
			Assert.That((byte)ScenePathPointFlags.Ferry, Is.EqualTo(1 << 6), "the next free flag bit");
		}

		/// <summary>A bridge belongs to the banks it joins: the commonest land biome along its approaches, never the River under its middle.</summary>
		[Test]
		public void ABridge_TakesTheBiomeOfItsEnds()
		{
			// Biome 0 the river (x within 20 m of 0), 1 the west bank, 2 the east bank's first 10 m then 1 again.
			Func<float, float, int> biomeAt = (x, z) => Mathf.Abs(x) < 20f ? 0 : x < 0f ? 1 : x < 30f ? 2 : 1;
			Func<int, bool> water = b => b == 0;
			int chosen = PathCrossings.BridgeBiome(new Vector3(0f, 5f, 0f), 90f, 36f, biomeAt, water, 3);
			Assert.That(chosen, Is.EqualTo(1), "west bank and the east beyond its strip outvote it; the River never");
			Assert.That(PathCrossings.BridgeBiome(new Vector3(0f, 5f, 0f), 90f, 36f, (x, z) => 0, water, 3), Is.EqualTo(0), "only water answers: the water's");
			Assert.That(PathCrossings.BridgeBiome(new Vector3(0f, 5f, 0f), 90f, 36f, (x, z) => -1, water, 3), Is.EqualTo(-1));
			Assert.That(PathCrossings.IsWaterBiome(BiomeWaterRole.River), Is.True);
			Assert.That(PathCrossings.IsWaterBiome(BiomeWaterRole.IceSheet), Is.False, "an ice sheet is ground");
		}

		// ── In-water sites (g, c, d) ──────────────────────────────────

		[Test]
		public void ChannelSpots_MoorInTheCurrent_BowUpstream()
		{
			RiverPath river = Straight(12, RiverEnd.Confluence, 40f);
			Assert.That(PointOfInterestWaterSites.TryChannelSpot(river, 5, 1, 1.5f, 0.2f, 10f, out PointOfInterestWaterSites.ChannelSpot left), Is.True);
			Assert.That(left.Position.x, Is.EqualTo(-10f).Within(1e-4f), "10 m off the left bank of a 40 m river flowing north");
			Assert.That(left.Yaw, Is.EqualTo(180f).Within(1e-3f), "bow into the current (upstream: south)");
			Assert.That(left.Side, Is.EqualTo(1));
			Assert.That(PointOfInterestWaterSites.TryChannelSpot(river, 5, -1, 1.5f, 0.2f, 10f, out PointOfInterestWaterSites.ChannelSpot right), Is.True);
			Assert.That(right.Position.x, Is.EqualTo(10f).Within(1e-4f));
			Assert.That(PointOfInterestWaterSites.TryChannelSpot(river, 5, 1, 3f, 0.2f, 10f, out _), Is.False, "too shallow");
			Assert.That(PointOfInterestWaterSites.TryChannelSpot(river, 5, 1, 1.5f, 5f, 10f, out _), Is.False, "too slow");
			river.Reach = new RiverReach[river.Count];
			river.Reach[5] = RiverReach.Rapid;
			Assert.That(PointOfInterestWaterSites.TryChannelSpot(river, 5, 1, 1.5f, 0.2f, 10f, out _), Is.False, "never in white water");
			Assert.That(PointOfInterestWaterSites.TryBankQuay(Straight(12, RiverEnd.Confluence, 40f), 5, 1, 12f, out PointOfInterestWaterSites.BankQuay quay), Is.True);
			Assert.That(quay.Position.x, Is.EqualTo(-20f).Within(1e-4f), "on the water's edge");
			Assert.That(quay.Yaw, Is.EqualTo(90f).Within(1e-3f), "facing the river (east)");
			Assert.That(PointOfInterestWaterSites.TryBankQuay(Straight(12, RiverEnd.Confluence, 8f), 5, 1, 12f, out _), Is.False, "too narrow for a staple");
		}

		[Test]
		public void Shallows_StandOnPiles_WithACausewayToTheStrand()
		{
			// A lake east of x = 0, its bed falling 0.05 m a metre: 2 m deep at x = 40.
			Func<float, float, float> depthAt = (x, z) => x <= 0f ? -1f : x * 0.05f;
			Assert.That(PointOfInterestWaterSites.TryShallows(new Vector2(40f, 0f), 6f, 0.5f, 3f, 300f, depthAt, out PointOfInterestWaterSites.ShallowsSite site), Is.True);
			Assert.That(site.Depth, Is.EqualTo(2f).Within(1e-5f));
			Assert.That(site.Causeway, Is.Not.Null);
			Vector2 end = site.Causeway[site.Causeway.Count - 1];
			Assert.That(end.x, Is.LessThanOrEqualTo(0.5f), "to the strand, westward");
			Assert.That(site.CausewayLength, Is.EqualTo(34f).Within(2.1f));
			Assert.That(PointOfInterestWaterSites.TryShallows(new Vector2(100f, 0f), 6f, 0.5f, 3f, 300f, depthAt, out _), Is.False, "5 m: past the piles");
			Assert.That(PointOfInterestWaterSites.TryShallows(new Vector2(40f, 0f), 6f, 0.5f, 3f, 20f, depthAt, out _), Is.False, "too far out for its causeway");
			Assert.That(PointOfInterestWaterSites.SunkenDepth(42f, 50f, true, 0f), Is.EqualTo(8f), "below the lake's own level");
			Assert.That(PointOfInterestWaterSites.SunkenDepth(-12f, float.NaN, true, 0f), Is.EqualTo(12f), "else the sea's");
			Assert.That(float.IsNaN(PointOfInterestWaterSites.SunkenDepth(5f, float.NaN, false, 0f)), Is.True);
		}

		// ── Inland traffic on the shared clock (e, j) ────────────────

		[Test]
		public void Drift_IsTheExactIntegral_AndItsInverse()
		{
			var along = new[] { 0f, 100f, 250f, 400f };
			var speed = new[] { 0.5f, 1.5f, 1.5f, 0.8f };
			double exact = PointOfInterestInlandTraffic.DriftSeconds(along, speed, 0, 3);
			// A fine numeric integral of 1/v with v linear between knots.
			double numeric = 0.0;
			for (int i = 0; i < 3; i++)
			{
				const int steps = 20000;
				double ds = (along[i + 1] - along[i]) / (double)steps;
				for (int k = 0; k < steps; k++)
				{
					double t = (k + 0.5) / steps;
					numeric += ds / (speed[i] + (speed[i + 1] - speed[i]) * t);
				}
			}
			Assert.That(exact, Is.EqualTo(numeric).Within(1e-3));
			for (double s = 0.0; s <= exact; s += exact / 37.0)
			{
				double at = PointOfInterestInlandTraffic.DriftAlong(along, speed, 0, 3, s);
				int seg = at >= along[2] ? 2 : at >= along[1] ? 1 : 0;
				double back = PointOfInterestInlandTraffic.DriftSeconds(along, speed, 0, seg)
					+ PointOfInterestInlandTraffic.SegmentSeconds(at - along[seg], speed[seg],
						speed[seg] + (speed[seg + 1] - speed[seg]) * (at - along[seg]) / (along[seg + 1] - along[seg]));
				Assert.That(back, Is.EqualTo(s).Within(1e-3), $"at {s:0.0} s");
			}
			Assert.That(PointOfInterestInlandTraffic.DriftAlong(along, speed, 0, 3, exact * 2.0), Is.EqualTo(400.0), "held at the end");
			float lane = PointOfInterestInlandTraffic.LaneSpeed(along, speed, 0, 3);
			Assert.That(lane, Is.EqualTo((float)(400.0 / exact)).Within(1e-4f), "the harmonic mean of the current");
			Assert.That(PointOfInterestInlandTraffic.LakeCarries(250000f), Is.True);
			Assert.That(PointOfInterestInlandTraffic.LakeCarries(150000f), Is.False);
		}

		[Test]
		public void NavigableStretches_SkipFallsAndShallows()
		{
			RiverPath river = Straight(60, RiverEnd.Confluence, 20f);
			river.Reach = new RiverReach[river.Count];
			river.Reach[30] = RiverReach.Fall;
			List<(int from, int to)> stretches = PointOfInterestInlandTraffic.NavigableStretches(river);
			Assert.That(stretches, Is.EqualTo(new List<(int, int)> { (0, 29), (31, 59) }));
			river.Depth[45] = 0.5f;
			stretches = PointOfInterestInlandTraffic.NavigableStretches(river);
			Assert.That(stretches, Is.EqualTo(new List<(int, int)> { (0, 29) }), "the stretches either side of the shallow are under 200 m");
		}

		// ── Defaults: nothing changes but the riverbed ───────────────

		/// <summary>No spec adds a bed layer, a riverbed rule, a rule's own waters or current; no template holds any.</summary>
		[Test]
		public void Defaults_NoBiomeUsesTheNewSlots()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				// A "no biome uses the slots yet" pin: the authored rounds (Lake, River, Beach...) use them on purpose.
				if (BiomeAuthoredRounds.Contains(entry.Biome))
				{
					continue;
				}
				Assert.That(entry.BedLayers, Is.Empty, $"{entry.Biome} has no bed layer yet");
				Assert.That(entry.RiverBed, Is.Null, $"{entry.Biome} grows nothing on its riverbed yet");
				foreach (BiomeArtSpec.Scatter s in entry.Bed != null ? entry.Bed.Scatter : new List<BiomeArtSpec.Scatter>())
				{
					Assert.That(s.Waters, Is.EqualTo(BiomeBedWaters.Default), $"{entry.Biome} {s.Name}");
					Assert.That(s.Current, Is.EqualTo(Vector2.zero), $"{entry.Biome} {s.Name}");
				}
			}
			int templates = 0;
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (!Regex.IsMatch(text, "^  MinTemperature:", RegexOptions.Multiline))
				{
					continue;
				}
				templates++;
				Assert.That(Regex.IsMatch(text, @"^  bedTextureLayers:\s*\n  - ", RegexOptions.Multiline), Is.False, $"{Path.GetFileName(path)} has a bed layer");
				Assert.That(Regex.IsMatch(text, @"^\s+waters: [1-9]", RegexOptions.Multiline), Is.False, $"{Path.GetFileName(path)} has a rule with its own waters");
			}
			Assert.That(templates, Is.EqualTo(83));
			Assert.That(BiomeTemplate.MaximumBedLayers, Is.EqualTo(3));
		}

		/// <summary>The in-water modes and ferries are off in the catalogue and the stage until their pieces exist.</summary>
		[Test]
		public void Defaults_NoRuleStandsInWater_AndNoFerryIsAsked()
		{
			string rules = Read(RulesSource);
			foreach (string mode in new[] { ".InChannel(", ".StraddlesBank(", ".OverShallows(", ".UnderLakes(" })
			{
				Assert.That(rules, Does.Not.Contain(mode), $"{mode} is switched on with its pieces");
			}
			Assert.That(Read(StageSource), Does.Not.Contain("FerryOver ="), "no ferries until their pieces exist");
			var rule = new PointOfInterestKindRule();
			Assert.That(rule.InStream || rule.BankStraddle || rule.Shallows || rule.InLakes, Is.False);
		}

		// ── Wiring ────────────────────────────────────────────────────

		[Test]
		public void Wiring_RiverbedPaletteScatterShaderAndBridges()
		{
			string palette = Read(PaletteSource);
			StringAssert.Contains("Slot = SlotRiverbed }, riverbedLayer, 1)", palette);
			Assert.That(palette, Does.Not.Contain("lakebed ? null"), "the riverbed is a candidate whatever the lakebed");
			StringAssert.Contains("BiomeLocalArt.BedSlot(k)", palette);

			string generator = Read(GeneratorSource);
			StringAssert.Contains("RiverAt = RiverBed(water),", generator);
			StringAssert.Contains("RiverAt = options.RiverAt", generator);

			string scatter = Read(ScatterSource);
			StringAssert.Contains("float shore = ShoreBand(scatterRule, worldX, worldY, worldZ);", scatter);
			StringAssert.Contains("float shore = ShoreBand(scatterRule, worldX, bedY, worldZ);", scatter);
			Assert.That(scatter, Does.Not.Contain("class ClimateGrid"), "one climate grid: TerrainClimateGrid");
			StringAssert.Contains("TerrainClimateGrid.Build(", scatter);

			string vegetation = Read(VegetationSource);
			StringAssert.Contains("#include \"FishChannelFlow.hlsl\"", vegetation);
			StringAssert.Contains("FishChannelStream(", vegetation);
			string channel = Read(ChannelSource);
			StringAssert.Contains($"#define FISH_CHANNEL_CELLS {FishMMO.Water.InlandWaterRenderer.ChannelCells}", channel);
			string renderer = Read(RendererChannelSource);
			StringAssert.Contains("\"_FishChannelFlow\"", renderer);
			StringAssert.Contains("\"_FishChannelFlowRect\"", renderer);
			StringAssert.Contains("UpdateChannelFlow();", Read(RendererSource));
			StringAssert.Contains("ReleaseChannelFlow();", Read(RendererSource));

			string stage = Read(StageSource);
			StringAssert.Contains("PathCrossings.BridgeBiome(", stage);
			StringAssert.Contains("UnderwaterDepth(rule,", Read(PlacementSource));
		}

		private static string Read(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative)).Replace("\r\n", "\n");
	}
}
