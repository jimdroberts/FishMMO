using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Decision 29 of the biome suite (2026-10-18, the Hills round) and decision 23's text columns: ground layers may carry a
	/// temperature and humidity band (TerrainTextureLayer.useClimateBand), climate variants' ground overrides
	/// (BiomeClimateVariant.TextureLayerOverrides) are painted within their envelopes, and a biome's file may rewrite its
	/// template's description and naming block. The pure parts: the band curve (the spawn rules' own), the slot keys, the
	/// painter's climate shares (the weigher on hand-laid palettes; needs managed Perlin noise, so it runs under nativerun or
	/// Unity), the tile climate grid, the spec hooks, the marks, that nothing changes for today's 83 biomes, and the wiring.
	/// </summary>
	public class BiomeClimateGroundTests
	{
		private const string WorldFolder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";

		// ── The band ──────────────────────────────────────────────────

		/// <summary>A ground layer's band is the spawn rules' climate band to the bit, so a reach's ground and its plants change over together.</summary>
		[Test]
		public void Band_IsTheSpawnRulesClimateBand()
		{
			var random = new System.Random(29);
			for (int i = 0; i < 20000; i++)
			{
				var range = new Vector2((float)(random.NextDouble() * 2.4 - 1.2), (float)(random.NextDouble() * 2.4 - 1.2));
				float falloff = i % 7 == 0 ? 0f : (float)random.NextDouble() * 0.5f;
				float value = (float)(random.NextDouble() * 2.2 - 1.1);
				Assert.That(BiomeClimateBand.Weight(range, falloff, value), Is.EqualTo(TerrainScatter.ClimateBand(range, falloff, value)),
					$"range {range}, falloff {falloff}, value {value}");
			}
			Assert.That(BiomeClimateBand.Weight(new Vector2(0.2f, 1f), 0.1f, float.NaN), Is.EqualTo(1f), "no reading passes");
		}

		/// <summary>1 inside, 0 a half-width outside, half at a closed end; open at ±1; a layer with the band off is always 1.</summary>
		[Test]
		public void Band_IsFullInside_HalfAtAnEnd_AndOpenAtTheScalesEnds()
		{
			var warm = new Vector2(0.2f, 1f);
			Assert.That(BiomeClimateBand.Weight(warm, 0.1f, 0.6f), Is.EqualTo(1f));
			Assert.That(BiomeClimateBand.Weight(warm, 0.1f, 0.2f), Is.EqualTo(0.5f).Within(1e-6f));
			Assert.That(BiomeClimateBand.Weight(warm, 0.1f, 0.149f), Is.EqualTo(0f));
			Assert.That(BiomeClimateBand.Weight(warm, 0.1f, 1f), Is.EqualTo(1f), "an end at 1 is open: the hottest place is not halved");
			Assert.That(BiomeClimateBand.Weight(new Vector2(-1f, 1f), 0.1f, -1f), Is.EqualTo(1f));
			Assert.That(BiomeClimateBand.Closes(new Vector2(-1f, 1f)), Is.False);
			Assert.That(BiomeClimateBand.Closes(new Vector2(-1f, 0.3f)), Is.True);

			var layer = new TerrainTextureLayer();
			Assert.That(layer.useClimateBand, Is.False, "off by default");
			Assert.That(layer.ClimateWeight(-0.9f, 0.9f), Is.EqualTo(1f));
			Assert.That(layer.HasClimateBand, Is.False);
			layer.useClimateBand = true;
			Assert.That(layer.HasClimateBand, Is.False, "on but open at every end bands nothing");
			layer.temperatureRange = warm;
			Assert.That(layer.HasClimateBand, Is.True);
			Assert.That(layer.ClimateWeight(-0.5f, 0f), Is.EqualTo(0f));
			Assert.That(layer.ClimateWeight(0.5f, 0f), Is.EqualTo(1f));
			layer.humidityRange = new Vector2(-1f, 0.25f);
			Assert.That(layer.ClimateWeight(0.5f, 0.25f), Is.EqualTo(0.5f).Within(1e-6f), "both axes multiply");
		}

		/// <summary>A variant's texture weight is its envelope as a band, with no climate known it is none at all.</summary>
		[Test]
		public void AVariantsTextureWeight_IsItsEnvelope_AndNoneWithoutAClimate()
		{
			var frozen = new BiomeClimateVariant { Name = "Frozen", MinTemperature = -1f, MaxTemperature = -0.45f };
			Assert.That(frozen.TextureFalloff, Is.EqualTo(BiomeClimateBand.DefaultFalloff));
			Assert.That(frozen.TextureWeight(-0.9f, 0f), Is.EqualTo(1f));
			Assert.That(frozen.TextureWeight(-0.45f, 0f), Is.EqualTo(0.5f).Within(1e-6f));
			Assert.That(frozen.TextureWeight(0f, 0f), Is.EqualTo(0f));
			Assert.That(frozen.TextureWeight(-1f, -1f), Is.EqualTo(1f), "the open ends of the scale are open");
			Assert.That(frozen.TextureWeight(float.NaN, float.NaN), Is.EqualTo(0f), "no climate: no variant");
			Assert.That(frozen.TextureOverrideFor(0), Is.Null);
			frozen.TextureLayerOverrides.Add(new TerrainTextureLayer());
			Assert.That(frozen.TextureOverrideFor(0), Is.SameAs(frozen.TextureLayerOverrides[0]));
			Assert.That(frozen.TextureOverrideFor(1), Is.Null);
			Assert.That(frozen.TextureOverrideFor(-1), Is.Null);
		}

		// ── Slot keys ─────────────────────────────────────────────────

		/// <summary>A variant override's key names the variant and the ground slot it stands in for, and splits back into both.</summary>
		[Test]
		public void VariantSlot_RoundTrips_AndNoOtherKeyParsesAsOne()
		{
			Assert.That(BiomeLocalArt.GroundSlot(0), Is.EqualTo("main"));
			Assert.That(BiomeLocalArt.GroundSlot(3), Is.EqualTo("detail/2"));
			Assert.That(BiomeLocalArt.GroundIndexOf("main"), Is.EqualTo(0));
			Assert.That(BiomeLocalArt.GroundIndexOf("detail/2"), Is.EqualTo(3));
			Assert.That(BiomeLocalArt.GroundIndexOf("cliff/0"), Is.EqualTo(-1));
			Assert.That(BiomeLocalArt.GroundIndexOf("detail/02"), Is.EqualTo(-1));
			Assert.That(BiomeLocalArt.VariantId(new BiomeClimateVariant { Name = "Sun-scorched" }, 4), Is.EqualTo("sunscorched"));
			Assert.That(BiomeLocalArt.VariantId(new BiomeClimateVariant { Name = "" }, 4), Is.EqualTo("4"), "an unnamed variant goes by its place");
			foreach (string id in new[] { "frozen", "scorched", "0", "12" })
			{
				for (int g = 0; g < 12; g++)
				{
					string key = BiomeLocalArt.VariantSlot(id, g);
					Assert.That(BiomeLocalArt.TryParseVariantSlot(key, out string back, out int ground), Is.True, key);
					Assert.That(back, Is.EqualTo(id), key);
					Assert.That(ground, Is.EqualTo(g), key);
					Assert.That(BiomeLocalArt.TryParseWaySlot(key, out _, out _), Is.False, key + " is no way");
					Assert.That(BiomeLocalArt.IsGroundSlot(key), Is.False, key + " carries no ways of its own");
				}
			}
			Assert.That(BiomeLocalArt.VariantSlot("frozen", 0), Is.EqualTo("variant/frozen/main"));
			Assert.That(BiomeLocalArt.VariantSlot("frozen", 2), Is.EqualTo("variant/frozen/detail/1"));
			var others = new List<string>
			{
				"main", "detail/0", "cliff/0", "riverbed", "lakebed", "road", "path", "path/stone", "sediment/sand", "sediment/gravel",
				"main/road", "detail/1/path/stone", null, "", "variant/", "variant//main", "variant/frozen", "variant/frozen/", "variant/frozen/cliff/0",
				"variant/frozen/detail/x", "variant/frozen/main/road",
			};
			foreach (string key in others)
			{
				Assert.That(BiomeLocalArt.TryParseVariantSlot(key, out _, out _), Is.False, key ?? "<null>");
			}
		}

		// ── The painter's shares (managed Perlin noise: nativerun or Unity) ──

		private static SceneTerrainPalette.Entry Entry(int biome, int layer, PaletteRole role, string slot, TerrainTextureLayer source, int ground = -1,
			BiomeClimateVariant variant = null, int variantIndex = -1)
		{
			return new SceneTerrainPalette.Entry
			{
				BiomeIndex = biome, LayerIndex = layer, Role = role, Slot = slot, Source = source, Ground = ground, Variant = variant, VariantIndex = variantIndex,
			};
		}

		private static TerrainTextureLayer Detail(float noise, float sharpness, Vector2? slope = null)
		{
			var layer = new TerrainTextureLayer { blendNoiseScale = noise, blendSharpness = sharpness };
			if (slope.HasValue)
			{
				layer.useSlopeConstraint = true;
				layer.slopeRange = new MinMaxRange(slope.Value.x, slope.Value.y);
			}
			return layer;
		}

		/// <summary>Two biomes, west and east, meeting down the middle of a 128 m field.</summary>
		private static SceneBiomeField TwoBiomes()
		{
			var cells = new byte[8 * 8];
			for (int z = 0; z < 8; z++)
			{
				for (int x = 0; x < 8; x++)
				{
					cells[z * 8 + x] = (byte)(x < 4 ? 0 : 1);
				}
			}
			return SceneBiomeField.FromCells(8, 8, 16f, Vector2.zero, new BiomeTemplate[] { null, null }, cells, 24f, 7u);
		}

		/// <summary>The entries every palette below shares: each biome's main, details, a cliff and a bed; one detail art shared.</summary>
		private static List<SceneTerrainPalette.Entry> Plain(TerrainTextureLayer westMain = null, TerrainTextureLayer westDetail = null)
		{
			var cliff = new CliffTextureLayer { minCliffAngle = 38f, maxCliffAngle = 90f };
			return new List<SceneTerrainPalette.Entry>
			{
				Entry(0, 0, PaletteRole.Main, "main", westMain ?? new TerrainTextureLayer(), 0),
				Entry(0, 1, PaletteRole.Detail, "detail/0", westDetail ?? Detail(40f, 2f), 1),
				Entry(0, 2, PaletteRole.Detail, "detail/1", Detail(90f, 1.5f, new Vector2(10f, 35f)), 2),
				Entry(0, 3, PaletteRole.Cliff, "cliff/0", cliff),
				Entry(0, 4, PaletteRole.Submerged, "lakebed", new TerrainTextureLayer()),
				Entry(1, 5, PaletteRole.Main, "main", new TerrainTextureLayer(), 0),
				Entry(1, 1, PaletteRole.Detail, "detail/0", Detail(64f, 3f), 1),
			};
		}

		private static BiomeSplatOptions Options() => new BiomeSplatOptions { NormalizedHeight = (x, y, z) => 0.45f + y * 0.001f, Seed = 11u };

		/// <summary>
		/// Defaults are identical: a biome sent down the climate's path with nothing banded and a variant that claims nothing
		/// shares its ground out to the bit as the old path does, everywhere, and its override is painted nowhere.
		/// </summary>
		[Test]
		public void TheClimatesPath_WithNothingBanded_MatchesTheOldWeightsWithinFloatPrecision()
		{
			SceneBiomeField field = TwoBiomes();
			SceneTerrainPalette plain = SceneTerrainPalette.FromEntries(2, 7, Plain());
			var never = new BiomeClimateVariant { Name = "Never", MinTemperature = 0.9f, MaxTemperature = 1f, TextureFalloff = 0f };
			List<SceneTerrainPalette.Entry> entries = Plain();
			entries.Add(Entry(0, 6, PaletteRole.Main, "variant/never/main", new TerrainTextureLayer(), 0, never, 0));
			entries.Add(Entry(0, 6, PaletteRole.Detail, "variant/never/detail/0", Detail(30f, 2f), 1, never, 0));
			SceneTerrainPalette climatic = SceneTerrainPalette.FromEntries(2, 7, entries);
			Assert.That(plain.NeedsClimate, Is.False);
			Assert.That(climatic.NeedsClimate, Is.True);

			var a = new BiomeSplatPainter.Weigher(plain, field, Options());
			var b = new BiomeSplatPainter.Weigher(climatic, field, Options());
			Assert.That(a.Climatic, Is.False);
			Assert.That(b.Climatic, Is.True);
			var wa = new float[7];
			var wb = new float[7];
			var random = new System.Random(5);
			for (int i = 0; i < 4000; i++)
			{
				float x = (float)random.NextDouble() * 128f, z = (float)random.NextDouble() * 128f;
				float y = (float)(random.NextDouble() * 60.0 - 4.0), slope = (float)random.NextDouble() * 60f;
				var climate = new Vector2((float)(random.NextDouble() * 1.6 - 0.8), (float)(random.NextDouble() * 2.0 - 1.0));
				a.Weigh(x, y, z, slope, wa, climate);
				b.Weigh(x, y, z, slope, wb, climate);
				for (int l = 0; l < 7; l++)
				{
					Assert.That(wb[l], Is.EqualTo(wa[l]).Within(2e-7f), $"layer {l} at ({x}, {y}, {z}) slope {slope} climate {climate}");
				}
				Assert.That(wb[6], Is.EqualTo(0f));
			}
		}

		/// <summary>A banded detail keeps its patches inside its band and leaves the ground to the main layer outside it.</summary>
		[Test]
		public void ABandedDetail_KeepsToItsClimate()
		{
			SceneBiomeField field = TwoBiomes();
			TerrainTextureLayer warm = Detail(40f, 2f);
			warm.useClimateBand = true;
			warm.temperatureRange = new Vector2(0.2f, 1f);
			var banded = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(2, 7, Plain(westDetail: warm)), field, Options());
			var unbanded = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(2, 7, Plain()), field, Options());
			var w = new float[7];
			var u = new float[7];
			int patched = 0;
			for (int i = 0; i < 400; i++)
			{
				// The west biome's own ground only (the east one reaches no nearer than 16 m: boundary 64 m less warp and blend).
				float x = 1f + (i % 20) * 0.5f, z = 10f + (i / 20) * 2.7f;
				unbanded.Weigh(x, 5f, z, 3f, u, new Vector2(0.6f, 0f));
				banded.Weigh(x, 5f, z, 3f, w, new Vector2(0.6f, 0f));
				Assert.That(w, Is.EqualTo(u), "inside its band a banded detail is the unbanded one");
				patched += u[1] > 0.01f ? 1 : 0;
				banded.Weigh(x, 5f, z, 3f, w, new Vector2(-0.5f, 0f));
				Assert.That(w[1], Is.EqualTo(0f), "outside its band");
				Assert.That(w[0] + w[2], Is.EqualTo(u[0] + u[1] + u[2]).Within(1e-5f), "its ground went to the biome's other layers");
				banded.Weigh(x, 5f, z, 3f, w, new Vector2(0.2f, 0f));
				Assert.That(w[1], Is.LessThanOrEqualTo(u[1] + 1e-6f), "half strength at the end");
			}
			Assert.That(patched, Is.GreaterThan(20), "the probe crossed the detail's patches");
		}

		/// <summary>
		/// A variant's main override takes the main slot within its envelope, the biome's own main outside, cross-fading across
		/// the soft edge; two variants on one slot take it in order; the shares always sum to what the main slot holds.
		/// </summary>
		[Test]
		public void AVariantsMainOverride_TakesTheMainSlotWithinItsEnvelope()
		{
			var cells = new byte[16];
			SceneBiomeField field = SceneBiomeField.FromCells(4, 4, 16f, Vector2.zero, new BiomeTemplate[] { null }, cells, 16f);
			var frozen = new BiomeClimateVariant { Name = "Frozen", MinTemperature = -1f, MaxTemperature = -0.45f };
			var bitter = new BiomeClimateVariant { Name = "Bitter", MinTemperature = -1f, MaxTemperature = -0.7f };
			var entries = new List<SceneTerrainPalette.Entry>
			{
				Entry(0, 0, PaletteRole.Main, "main", new TerrainTextureLayer(), 0),
				Entry(0, 1, PaletteRole.Main, "variant/frozen/main", new TerrainTextureLayer(), 0, frozen, 0),
				Entry(0, 2, PaletteRole.Main, "variant/bitter/main", new TerrainTextureLayer(), 0, bitter, 1),
			};
			var weigher = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 3, entries), field, Options());
			var w = new float[3];
			weigher.Weigh(30f, 5f, 30f, 2f, w, new Vector2(0f, 0f));
			Assert.That(w, Is.EqualTo(new[] { 1f, 0f, 0f }), "warm: the biome's own");
			weigher.Weigh(30f, 5f, 30f, 2f, w, new Vector2(-0.6f, 0f));
			Assert.That(w[1], Is.EqualTo(1f).Within(1e-6f), "frozen: the override");
			weigher.Weigh(30f, 5f, 30f, 2f, w, new Vector2(-0.9f, 0f));
			Assert.That(w[1], Is.EqualTo(1f).Within(1e-6f), "the first variant that fits takes the slot");
			Assert.That(w[2], Is.EqualTo(0f));
			weigher.Weigh(30f, 5f, 30f, 2f, w, new Vector2(-0.45f, 0f));
			Assert.That(w[0], Is.EqualTo(0.5f).Within(1e-5f), "half way at the envelope's edge");
			Assert.That(w[1], Is.EqualTo(0.5f).Within(1e-5f));
			float last = 0f;
			for (float t = -0.2f; t >= -0.7f; t -= 0.01f)
			{
				weigher.Weigh(30f, 5f, 30f, 2f, w, new Vector2(t, 0f));
				Assert.That(w[0] + w[1] + w[2], Is.EqualTo(1f).Within(1e-5f));
				Assert.That(w[1], Is.GreaterThanOrEqualTo(last - 1e-6f), "fades in monotonically as it gets colder");
				last = w[1];
			}
			weigher.Weigh(30f, 5f, 30f, 2f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.That(w, Is.EqualTo(new[] { 1f, 0f, 0f }), "no climate known: the biome's own ground");
		}

		/// <summary>Outside a banded main layer the details carry the ground; with no detail there, the main layer stays.</summary>
		[Test]
		public void OutsideABandedMainLayer_TheDetailsCarryTheGround()
		{
			var cells = new byte[16];
			SceneBiomeField field = SceneBiomeField.FromCells(4, 4, 16f, Vector2.zero, new BiomeTemplate[] { null }, cells, 16f);
			var main = new TerrainTextureLayer { useClimateBand = true, temperatureRange = new Vector2(0f, 1f) };
			var withDetail = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 2, new[]
			{
				Entry(0, 0, PaletteRole.Main, "main", main, 0),
				Entry(0, 1, PaletteRole.Detail, "detail/0", Detail(20f, 1f), 1),
			}), field, Options());
			var alone = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 1, new[] { Entry(0, 0, PaletteRole.Main, "main", main, 0) }), field, Options());
			var w = new float[2];
			int carried = 0;
			for (int i = 0; i < 200; i++)
			{
				float x = 2f + i * 0.3f, z = 40f - i * 0.17f;
				withDetail.Weigh(x, 5f, z, 2f, w, new Vector2(-0.6f, 0f));
				if (w[1] > 0f)
				{
					Assert.That(w[0], Is.EqualTo(0f), "a cold main ground gives way wherever a detail can take it");
					Assert.That(w[1], Is.EqualTo(1f).Within(1e-5f));
					carried++;
				}
				else
				{
					Assert.That(w[0], Is.EqualTo(1f), "no detail there: the main layer stays rather than a hole");
				}
			}
			Assert.That(carried, Is.GreaterThan(100));
			var one = new float[1];
			alone.Weigh(10f, 5f, 10f, 2f, one, new Vector2(-0.6f, 0f));
			Assert.That(one[0], Is.EqualTo(1f));
		}

		/// <summary>The same palette, point and climate always weigh the same: two weighers, two runs.</summary>
		[Test]
		public void Weighing_IsDeterministic()
		{
			SceneBiomeField field = TwoBiomes();
			TerrainTextureLayer warm = Detail(40f, 2f);
			warm.useClimateBand = true;
			warm.temperatureRange = new Vector2(0.1f, 1f);
			warm.humidityRange = new Vector2(-1f, 0.3f);
			var frozen = new BiomeClimateVariant { Name = "Frozen", MinTemperature = -1f, MaxTemperature = -0.2f };
			List<SceneTerrainPalette.Entry> list = Plain(westDetail: warm);
			list.Add(Entry(0, 6, PaletteRole.Main, "variant/frozen/main", new TerrainTextureLayer(), 0, frozen, 0));
			SceneTerrainPalette palette = SceneTerrainPalette.FromEntries(2, 7, list);
			var first = new BiomeSplatPainter.Weigher(palette, field, Options());
			var second = new BiomeSplatPainter.Weigher(palette, field, Options());
			var a = new float[7];
			var b = new float[7];
			for (int i = 0; i < 2000; i++)
			{
				float x = (i * 37 % 128), z = (i * 71 % 128), t = (i % 41) / 20f - 1f, h = (i % 13) / 6f - 1f;
				first.Weigh(x, 3f, z, i % 50, a, new Vector2(t, h));
				second.Weigh(x, 3f, z, i % 50, b, new Vector2(t, h));
				Assert.That(b, Is.EqualTo(a));
				float sum = 0f;
				foreach (float v in a)
				{
					sum += v;
				}
				Assert.That(sum, Is.EqualTo(1f).Within(1e-5f));
			}
		}

		// ── The tile climate grid ─────────────────────────────────────

		private static Vector2 Linear(float x, float y, float z) => new Vector2(0.2f + 0.0001f * x - 0.0003f * z - 0.002f * y, -0.1f - 0.0002f * x + 0.001f * y);

		/// <summary>On a climate linear in place and height the grid reads back the climate at any texel's own height exactly (to float).</summary>
		[Test]
		public void TheClimateGrid_FollowsTheLapseToEachTexelsHeight()
		{
			var origin = new Vector3(-500f, 0f, 250f);
			float Ground(float u, float v) => 40f + 25f * Mathf.Sin(u * 9.1f) * Mathf.Cos(v * 7.3f);
			TerrainClimateGrid grid = TerrainClimateGrid.Build(Linear, origin, new Vector2(1000f, 1000f), Ground);
			var random = new System.Random(3);
			for (int i = 0; i < 5000; i++)
			{
				float u = (float)random.NextDouble(), v = (float)random.NextDouble();
				float y = Ground(u, v) + (float)(random.NextDouble() * 6.0 - 3.0);
				Vector2 read = grid.Read(u, v, y);
				Vector2 truth = Linear(origin.x + u * 1000f, y, origin.z + v * 1000f);
				Assert.That(read.x, Is.EqualTo(truth.x).Within(2e-4f));
				Assert.That(read.y, Is.EqualTo(truth.y).Within(2e-4f));
			}
			TerrainClimateGrid again = TerrainClimateGrid.Build(Linear, origin, new Vector2(1000f, 1000f), Ground);
			Assert.That(again.Read(0.3f, 0.7f, 41f), Is.EqualTo(grid.Read(0.3f, 0.7f, 41f)), "deterministic");
			Assert.That(grid.Read(0.5f, 0.5f, -1e6f).x, Is.EqualTo(1f), "clamped to the scale");
		}

		/// <summary>Two tiles side by side read the same climate on their shared edge: a band runs straight through a seam.</summary>
		[Test]
		public void TheClimateGrid_HasNoSeamBetweenTiles()
		{
			float World(float x, float z) => 30f + 0.02f * x + 10f * Mathf.Sin(z * 0.01f);
			Vector2 Climate(float x, float y, float z) => new Vector2(Mathf.Sin(x * 0.003f) * 0.5f - 0.004f * y, Mathf.Cos(z * 0.002f) * 0.5f + 0.001f * y);
			var west = new Vector3(0f, 0f, 0f);
			var east = new Vector3(1000f, 0f, 0f);
			TerrainClimateGrid a = TerrainClimateGrid.Build(Climate, west, new Vector2(1000f, 1000f), (u, v) => World(west.x + u * 1000f, v * 1000f));
			TerrainClimateGrid b = TerrainClimateGrid.Build(Climate, east, new Vector2(1000f, 1000f), (u, v) => World(east.x + u * 1000f, v * 1000f));
			for (int i = 0; i <= 100; i++)
			{
				float v = i / 100f;
				float y = World(1000f, v * 1000f);
				Assert.That(b.Read(0f, v, y), Is.EqualTo(a.Read(1f, v, y)), $"v {v}");
			}
		}

		// ── Spec hooks ────────────────────────────────────────────────

		[Test]
		public void TheSpecsBandHooks_NormaliseAndChain()
		{
			BiomeArtSpec.Layer layer = new BiomeArtSpec.Layer { Family = Ground.Grass };
			Assert.That(layer.HasClimateBand, Is.False);
			Assert.That(layer.Temp(-1f, 1f), Is.SameAs(layer));
			Assert.That(layer.Temperature, Is.Null, "both ends open is no band");
			layer.Temp(-0.35f, 5f, 0.05f).Humid(0.45f, 1f);
			Assert.That(layer.Temperature, Is.EqualTo(new Vector2(-0.35f, 1f)));
			Assert.That(layer.TemperatureFalloff, Is.EqualTo(0.05f));
			Assert.That(layer.Humidity, Is.EqualTo(new Vector2(0.45f, 1f)));
			Assert.That(layer.HumidityFalloff, Is.EqualTo(BiomeClimateBand.DefaultFalloff));
			Assert.That(layer.HasClimateBand, Is.True);

			var entry = new BiomeArtSpec.Entry { Biome = "Test" };
			var snow = new BiomeArtSpec.Layer { Family = Ground.Snow };
			var frost = new BiomeArtSpec.Layer { Family = Ground.Gravel };
			entry.ClimateVariant("Cold", -2f, 0f).ClimateGround("Cold", snow, null, frost).ClimateGround("Scorched");
			Assert.That(entry.Variants.Count, Is.EqualTo(1));
			Assert.That(entry.Variants[0].Temperature, Is.EqualTo(new Vector2(-1f, 0f)));
			Assert.That(entry.Variants[0].Humidity, Is.EqualTo(new Vector2(-1f, 1f)));
			Assert.That(entry.VariantLayers[0].Layers, Is.EqualTo(new[] { snow, null, frost }), "main first, then the details in order");
			Assert.That(entry.VariantLayers[1].Layers, Is.EqualTo(new BiomeArtSpec.Layer[] { null }));

			entry.Describe("Rolling hills").Names(codas: new[] { "edo", "eira" }, poiWords: new[] { ("spring", new[] { "Fonte" }) });
			entry.Names(onsets: new[] { "Cas" });
			Assert.That(entry.Description, Is.EqualTo("Rolling hills"));
			Assert.That(entry.Naming.Codas, Is.EqualTo(new[] { "edo", "eira" }), "a later Names keeps the parts it does not set");
			Assert.That(entry.Naming.Onsets, Is.EqualTo(new[] { "Cas" }));
			Assert.That(entry.Naming.Nuclei, Is.Null);
			Assert.That(entry.Naming.IsEmpty, Is.False);
			Assert.That(new BiomeArtSpec.NamingColumns().IsEmpty, Is.True);
		}

		/// <summary>
		/// Defaults: no biome's file sets a band, a variant, a variant's grounds, a description or a naming part, so the authoring
		/// writes none of them and marks nothing — every template stays as it is.
		/// </summary>
		[Test]
		public void NoBiomesSpec_SetsABandAVariantOrATextColumn_Yet()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.That(entry.Main == null || !entry.Main.HasClimateBand, Is.True, entry.Biome);
				foreach (BiomeArtSpec.Layer detail in entry.Details)
				{
					Assert.That(detail == null || !detail.HasClimateBand, Is.True, entry.Biome);
				}
				Assert.That(entry.Variants, Is.Empty, entry.Biome);
				Assert.That(entry.VariantLayers, Is.Empty, entry.Biome);
				Assert.That(entry.Description, Is.Null, entry.Biome);
				Assert.That(entry.Naming, Is.Null, entry.Biome);
			}
		}

		/// <summary>
		/// Defaults are identical for all 83 biomes: no template's layer has its climate band on and no climate variant lists a
		/// ground override, so no palette has a climatic entry (SceneTerrainPalette.NeedsClimate is false), the painter never
		/// reads the climate and every texel is weighed by the unchanged path.
		/// </summary>
		[Test]
		public void NoTemplate_HasABandOrAVariantGround_SoEveryScenePaintsAsBefore()
		{
			int biomes = 0;
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (!text.Contains("m_Script:") || !text.Contains("mainTextureLayer:"))
				{
					continue;
				}
				biomes++;
				// A layer's band, not a spawn rule's (which shares the name): Unity writes the layer's four band fields and then its ways.
				Assert.That(Regex.IsMatch(text, @"useClimateBand: 1\r?\n(?:[^\n]*\n){4}\s*ways:"), Is.False, path);
				foreach (Match match in Regex.Matches(text, @"TextureLayerOverrides:(.*)"))
				{
					Assert.That(match.Groups[1].Value.Trim(), Is.EqualTo("[]"), path + ": a variant lists a ground override");
				}
			}
			Assert.That(biomes, Is.EqualTo(83));
		}

		// ── Marks ─────────────────────────────────────────────────────

		[Test]
		public void ClimateMarks_AreEmptyForNoBand_AndRoundTripThroughTheTemplateLayer()
		{
			var layer = new TerrainTextureLayer();
			Assert.That(BiomeArtAuthoring.ClimateMark(layer), Is.Empty);
			layer.useClimateBand = true;
			layer.temperatureFalloff = 0.4f;
			Assert.That(BiomeArtAuthoring.ClimateMark(layer), Is.Empty, "on but open everywhere is no band");
			Assert.That(BiomeArtAuthoring.ClimateMark(new BiomeArtSpec.Layer()), Is.Empty);
			Assert.That(BiomeArtAuthoring.ClimateMark((BiomeArtSpec.Layer)null), Is.Empty);

			foreach (BiomeArtSpec.Layer spec in new[]
			{
				new BiomeArtSpec.Layer().Temp(-1f, -0.35f),
				new BiomeArtSpec.Layer().Humid(0.45f, 1f, 0.2f),
				new BiomeArtSpec.Layer().Temp(0.2f, 0.6f, 0f).Humid(-1f, 0.25f),
			})
			{
				var on = new TerrainTextureLayer();
				BiomeArtAuthoring.WriteClimateBand(on, spec);
				Assert.That(on.useClimateBand, Is.True);
				Assert.That(BiomeArtAuthoring.ClimateMark(on), Is.EqualTo(BiomeArtAuthoring.ClimateMark(spec)).And.Not.Empty);
			}
			var swapped = new TerrainTextureLayer { useClimateBand = true, temperatureRange = new Vector2(0.6f, 0.2f), humidityFalloff = 0.7f };
			var ordered = new TerrainTextureLayer { useClimateBand = true, temperatureRange = new Vector2(0.2f, 0.6f) };
			Assert.That(BiomeArtAuthoring.ClimateMark(swapped), Is.EqualTo(BiomeArtAuthoring.ClimateMark(ordered)), "a range in either order, an open axis's falloff ignored");
			var off = new TerrainTextureLayer { useClimateBand = true, temperatureRange = new Vector2(0f, 1f) };
			BiomeArtAuthoring.WriteClimateBand(off, null);
			Assert.That(off.useClimateBand, Is.False);
			Assert.That(off.temperatureRange, Is.EqualTo(new Vector2(-1f, 1f)));
		}

		[Test]
		public void TextMarks_AreStable_AndOrderSensitive()
		{
			Assert.That(BiomeArtAuthoring.Mark((string[])null), Is.EqualTo(BiomeArtAuthoring.Mark(new string[0])));
			Assert.That(BiomeArtAuthoring.Mark(new[] { "a", "b" }), Is.Not.EqualTo(BiomeArtAuthoring.Mark(new[] { "b", "a" })));
			Assert.That(BiomeArtAuthoring.Mark(new[] { "a", "b" }), Is.EqualTo(BiomeArtAuthoring.Mark((IReadOnlyList<string>)new List<string> { "a", "b" })));
			var rows = BiomeArtAuthoring.PoiWordRows(new[] { ("spring", new[] { "Fonte", "Poço" }), ("ruin", new[] { "Castro" }) });
			Assert.That(rows.Count, Is.EqualTo(2));
			Assert.That(rows[0].Key, Is.EqualTo("spring"));
			Assert.That(rows[0].Values, Is.EqualTo(new[] { "Fonte", "Poço" }));
			Assert.That(BiomeArtAuthoring.PoiWordsMark(rows), Is.EqualTo(BiomeArtAuthoring.PoiWordsMark(BiomeArtAuthoring.PoiWordRows(new[] { ("spring", new[] { "Fonte", "Poço" }), ("ruin", new[] { "Castro" }) }))));
			rows.Reverse();
			Assert.That(BiomeArtAuthoring.PoiWordsMark(rows), Is.Not.EqualTo(BiomeArtAuthoring.PoiWordsMark(BiomeArtAuthoring.PoiWordRows(new[] { ("spring", new[] { "Fonte", "Poço" }), ("ruin", new[] { "Castro" }) }))));
			string frozen = BiomeArtAuthoring.VariantMark(new BiomeClimateVariant { Name = "Frozen", MinTemperature = -1f, MaxTemperature = -0.45f });
			Assert.That(frozen, Is.EqualTo(BiomeArtAuthoring.VariantMark("frozen", new Vector2(-1f, -0.45f), new Vector2(-1f, 1f), BiomeClimateBand.DefaultFalloff)));
			Assert.That(frozen, Is.Not.EqualTo(BiomeArtAuthoring.VariantMark("Frozen", new Vector2(-1f, -0.5f), new Vector2(-1f, 1f), BiomeClimateBand.DefaultFalloff)));
		}

		// ── Wiring ────────────────────────────────────────────────────

		private static string Code(string path) => File.ReadAllText(path);

		/// <summary>
		/// The cut and the repaint hand the splat the scene's placement climate (the scatter's) before painting, the horizon its
		/// own, the painter samples it only for a climatic palette and on a tile grid; the authoring writes the new columns; and
		/// nothing else at cut time reads the bands or the overrides.
		/// </summary>
		[Test]
		public void TheCutTheBackdropAndTheAuthoring_AreWired()
		{
			string generator = Code(WorldFolder + "/SceneGenerator.cs");
			int climate = generator.IndexOf("ClimateAt = ClimateOf(placementClimate),", StringComparison.Ordinal);
			int paint = generator.IndexOf("BiomeSplatPainter.Paint(tiles, palette, field, options)", StringComparison.Ordinal);
			int scatter = generator.IndexOf("TerrainScatter.Scatter(tiles, palette, field, options.Seed,", StringComparison.Ordinal);
			Assert.That(climate, Is.GreaterThan(0));
			Assert.That(paint, Is.GreaterThan(climate), "the splat is painted with the climate");
			Assert.That(scatter, Is.GreaterThan(paint));
			Assert.That(generator.Substring(paint, scatter - paint), Does.Not.Contain("ScenePlacementClimate.For("), "one placement climate for splat and scatter");
			Assert.That(generator, Does.Contain("ClimateAt = palette.NeedsClimate ? ClimateOf(ScenePlacementClimate.For("), "the horizon reads the same climate");
			Assert.That(generator, Does.Contain("ClimateSample sample = climate.SampleAt(new Vector3(x, y, z), out _);"));

			string painter = Code(WorldFolder + "/Terrain/BiomeSplatPainter.cs");
			Assert.That(painter, Does.Contain("bool climatic = options.ClimateAt != null && palette.NeedsClimate;"));
			Assert.That(painter, Does.Contain("TerrainClimateGrid climate = climatic"));
			Assert.That(painter, Does.Contain("ShareInClimate(slots, biomeWeight * ground,"));

			string authoring = Code(WorldFolder + "/ProceduralArt/BiomeArtAuthoring.cs");
			foreach (string call in new[]
			{
				"AuthorClimateVariants(biome, entry, source, report, apply);", "AuthorLayerClimate(biome, entry, source, report, apply);",
				"AuthorDescription(biome, entry, report, apply);", "AuthorNaming(biome, entry, report, apply);",
			})
			{
				Assert.That(authoring, Does.Contain(call));
			}
			Assert.That(authoring.IndexOf("AuthorClimateVariants(biome, entry, source, report, apply);", StringComparison.Ordinal),
				Is.LessThan(authoring.IndexOf("AuthorLayerClimate(biome, entry, source, report, apply);", StringComparison.Ordinal)), "overrides exist before their bands are written");

			var allowed = new HashSet<string>
			{
				"BiomeSplatPainter.cs", "SceneTerrainPalette.cs", "BiomeArtAuthoring.cs", "BiomeArtSpec.cs",
			};
			foreach (string path in Directory.GetFiles(WorldFolder, "*.cs", SearchOption.AllDirectories))
			{
				if (allowed.Contains(Path.GetFileName(path)))
				{
					continue;
				}
				string code = Regex.Replace(Code(path), @"//.*", string.Empty);
				Assert.That(code.Contains("TextureLayerOverrides") || code.Contains("TextureOverrideFor(") || code.Contains("ClimateWeight("), Is.False,
					path + " reads a ground band or a variant override outside the painter, the palette and the authoring");
			}
		}
	}
}
