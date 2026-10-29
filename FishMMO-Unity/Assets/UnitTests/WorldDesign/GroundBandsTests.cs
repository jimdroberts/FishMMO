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
	/// Phase GROUND-BANDS of the biome suite (2026-10-26; decisions 34, 71, 84, 87, AbyssalPlain Q5, the drafts' site paint, Valley's
	/// avalanche tracks): the facing band (ground layers and spawn rules), the fall-line stretch, the site band, landform zones on bed
	/// layers, and the shared-clock window drawn on the GPU. Off (the default on every layer, rule and template) nothing changes: no
	/// mark, no weight, the plain painter's weights to the bit, and a layer table whose clock rows say "none". On, each is pure and the
	/// shader mirrors the C#.
	/// </summary>
	[TestFixture]
	public class GroundBandsTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders";
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";

		private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

		/// <summary>A northern scene on plain axes: the equator −z, so poleward is +z.</summary>
		private static readonly BiomeScatterCompass North = BiomeScatterCompass.Plain(new Vector2(1f, 0f));

		// ── Enums, rows, packing ──────────────────────────────────────

		[Test]
		public void Enums_AndTableRows_AreAppendOnly()
		{
			Assert.That((int)BiomeFacing.Poleward, Is.EqualTo(0));
			Assert.That((int)BiomeFacing.Equatorward, Is.EqualTo(1));
			Assert.That((int)BiomeFacing.East, Is.EqualTo(2));
			Assert.That((int)BiomeFacing.West, Is.EqualTo(3));
			Assert.That((int)BiomeFacing.Downwind, Is.EqualTo(4));
			Assert.That((int)BiomeFacing.Upwind, Is.EqualTo(5));
			Assert.That((int)BiomeClockKind.None, Is.EqualTo(0));
			Assert.That((int)BiomeClockKind.Year, Is.EqualTo(1));
			Assert.That((int)BiomeClockKind.Day, Is.EqualTo(2));
			Assert.That(TerrainLayerTable.RowGlow, Is.EqualTo(8));
			Assert.That(TerrainLayerTable.RowClock, Is.EqualTo(9));
			Assert.That(TerrainLayerTable.RowClockShare, Is.EqualTo(10));
			Assert.That(TerrainLayerTable.Rows, Is.EqualTo(11));
			string index = Read(Path.Combine(Shaders, "FishTerrainIndexMap.hlsl"));
			StringAssert.Contains($"#define FISH_LAYER_ROW_CLOCK {TerrainLayerTable.RowClock} ", index);
			StringAssert.Contains($"#define FISH_LAYER_ROW_CLOCK_SHARE {TerrainLayerTable.RowClockShare} ", index);
		}

		// ── Facing ────────────────────────────────────────────────────

		[Test]
		public void FacingBand_CountsOnlyTheFacesThatLookItsWay()
		{
			Assert.That(default(BiomeFacingBand).On, Is.False);
			Assert.That(default(BiomeFacingBand).Weight(Vector2.zero, 0f, in North), Is.EqualTo(1f), "off: every face");
			Assert.That(default(BiomeFacingBand).Signature(), Is.Empty);
			BiomeFacingBand shade = BiomeFacingBand.Of(BiomeFacing.Poleward);
			Assert.That(shade.Weight(new Vector2(0f, 1f), 20f, in North), Is.EqualTo(1f), "a face looking poleward");
			Assert.That(shade.Weight(new Vector2(0f, -1f), 20f, in North), Is.EqualTo(0f), "a face looking at the sun");
			Assert.That(shade.Weight(new Vector2(1f, 0f), 20f, in North), Is.EqualTo(0f), "90° off: past 60° + 15°");
			Assert.That(shade.Weight(new Vector2(0f, 0f), 0f, in North), Is.EqualTo(0f), "flat ground faces nothing");
			Assert.That(shade.Weight(new Vector2(0f, 1f), 2f, in North), Is.EqualTo(0.5f).Within(1e-5f), "half way up the 4° ease-in");
			float edge = shade.Weight(new Vector2(Mathf.Sin(67.5f * Mathf.Deg2Rad), Mathf.Cos(67.5f * Mathf.Deg2Rad)), 20f, in North);
			Assert.That(edge, Is.EqualTo(0.5f).Within(1e-3f), "half way across the 15° soft edge");
			// In the south the equator is +z: the same band looks the other way.
			var south = new BiomeScatterCompass { East = Vector2.right, Equator = Vector2.up };
			Assert.That(shade.Weight(new Vector2(0f, -1f), 20f, in south), Is.EqualTo(1f));
			// A wind band with no wind known passes; with one it counts the lee.
			BiomeFacingBand lee = BiomeFacingBand.Of(BiomeFacing.Downwind);
			Assert.That(lee.Weight(new Vector2(0f, 1f), 20f, BiomeScatterCompass.Plain(Vector2.zero)), Is.EqualTo(1f));
			Assert.That(lee.Weight(new Vector2(1f, 0f), 20f, in North), Is.EqualTo(1f));
			Assert.That(lee.Weight(new Vector2(-1f, 0f), 20f, in North), Is.EqualTo(0f));
			// A zero compass (a hand-made scene) reads the plain axes.
			BiomeScatterCompass zero = default;
			Assert.That(shade.Weight(new Vector2(0f, 1f), 20f, in zero), Is.EqualTo(1f));
			StringAssert.StartsWith("face 0 ", shade.Signature());
		}

		// ── Sites ─────────────────────────────────────────────────────

		[Test]
		public void SiteBand_PaintsOnAndRoundItsKindsOnly()
		{
			var sites = new List<BiomeScatterSite>
			{
				new BiomeScatterSite { Centre = new Vector2(100f, 100f), Radius = 12f, Kind = "SunkenWhaleFall" },
				new BiomeScatterSite { Centre = new Vector2(400f, 100f), Radius = 30f, Kind = "Shipwreck" },
			};
			Assert.That(default(BiomeSiteBand).Weight(sites, 0f, 0f), Is.EqualTo(1f), "off: everywhere");
			BiomeSiteBand halo = BiomeSiteBand.Of("WhaleFall", 10f);
			Assert.That(halo.Weight(sites, 105f, 100f), Is.EqualTo(1f), "inside the footprint");
			Assert.That(halo.Weight(sites, 140f, 100f), Is.EqualTo(0f), "past the reach");
			Assert.That(halo.Weight(sites, 400f, 100f), Is.EqualTo(0f), "a site of another kind");
			Assert.That(halo.Weight(null, 105f, 100f), Is.EqualTo(0f), "no sites: nowhere");
			Assert.That(halo.Weight(sites, 122f, 100f), Is.EqualTo(1f), "within the reach past the footprint");
			Assert.That(halo.Weight(sites, 123.25f, 100f), Is.EqualTo(0.5f).Within(1e-4f), "half way across the fade");
		}

		// ── The clock: window, factor, shader twin ────────────────────

		[Test]
		public void ClockWindow_WrapsAndEases()
		{
			Assert.That(BiomeGroundClock.Window(0.5f, 0.2f, 0.6f, 0.05f), Is.EqualTo(1f));
			Assert.That(BiomeGroundClock.Window(0.1f, 0.8f, 0.45f, 0.05f), Is.EqualTo(1f), "inside a window that wraps past 0");
			Assert.That(BiomeGroundClock.Window(0.6f, 0.8f, 0.45f, 0.05f), Is.EqualTo(0f), "afternoon: the frost is gone");
			Assert.That(BiomeGroundClock.Window(0.475f, 0.8f, 0.45f, 0.05f), Is.EqualTo(0.5f).Within(1e-5f), "half way across the edge");
			Assert.That(BiomeGroundClock.Window(0.3f, 0.4f, 0.4f, 0f), Is.EqualTo(1f), "from == to: the whole clock");
			Assert.That(BiomeGroundClock.Window(0.7f, 0.2f, 0.6f, 0f), Is.EqualTo(0f), "a hard switch");
			Assert.That(BiomeGroundClock.Window(1.5f, 0.2f, 0.6f, 0.05f), Is.EqualTo(1f), "phases wrap");
			BiomeGroundClock none = default;
			Assert.That(none.IsNone && none.Factor(0.3f) == 1f && none.Pack() == Vector4.zero && none.Signature().Length == 0, Is.True);
			BiomeGroundClock fluff = BiomeGroundClock.Year(0.2f, 0.65f, 0.4f, 0.05f);
			Assert.That(fluff.Factor(0.4f), Is.EqualTo(1f));
			Assert.That(fluff.Factor(0.95f), Is.EqualTo(0.4f).Within(1e-6f), "winter: the floor");
			for (int i = 0; i <= 400; i++)
			{
				float p = i / 400f;
				Vector4 clocks = BiomeGroundClock.ShaderClock(p, true, 0f, false);
				Assert.That(BiomeGroundClock.FactorOf(fluff.Pack(), fluff.Floor, clocks), Is.EqualTo(fluff.Factor(p)).Within(1e-6f), $"phase {p}");
			}
			Assert.That(BiomeGroundClock.FactorOf(fluff.Pack(), fluff.Floor, Vector4.zero), Is.EqualTo(1f), "no clock known: as painted");
			Assert.That(BiomeGroundClock.FactorOf(BiomeGroundClock.Day(0.8f, 0.45f).Pack(), 0f, BiomeGroundClock.ShaderClock(0.5f, true, 0.6f, true)), Is.EqualTo(0f));
			Assert.That(BiomeGroundClock.LocalYear(0.1f, 30f), Is.EqualTo(0.1f).Within(1e-6f));
			Assert.That(BiomeGroundClock.LocalYear(0.1f, -30f), Is.EqualTo(0.6f).Within(1e-6f), "turned half a year in the south");
		}

		[Test]
		public void ClockShift_GivesTheShareToTheMainGround()
		{
			var clock = new Vector4[4];
			var share = new Vector4[4];
			for (int i = 0; i < 4; i++)
			{
				share[i] = new Vector4(0f, -1f, 0f, 1f);
			}
			// Layer 2 a dawn frost whose share goes to layer 0 (its main); layer 3 a clocked layer with no main.
			clock[2] = BiomeGroundClock.Day(0.8f, 0.45f, 0f, 0.05f).Pack();
			share[2] = new Vector4(0f, 0f, 0f, 1f);
			clock[3] = BiomeGroundClock.Day(0.8f, 0.45f, 0f, 0.05f).Pack();
			Vector4 noon = BiomeGroundClock.ShaderClock(0f, false, 0.6f, true);
			Vector4 dawn = BiomeGroundClock.ShaderClock(0f, false, 0.2f, true);

			float[] w = { 0.3f, 0.1f, 0.6f, 0f };
			int[] id = { 0, 1, 2, 0 };
			BiomeGroundClock.Shift(w, id, clock, share, dawn);
			Assert.That(w, Is.EqualTo(new[] { 0.3f, 0.1f, 0.6f, 0f }), "inside the window: as painted");
			BiomeGroundClock.Shift(w, id, clock, share, noon);
			Assert.That(w[0], Is.EqualTo(0.9f).Within(1e-6f), "the main among the four takes the frost's share");
			Assert.That(w[2], Is.EqualTo(0f).Within(1e-6f));

			w = new[] { 0.9f, 0.1f, 0f, 0f };
			id = new[] { 2, 1, 1, 1 };
			BiomeGroundClock.Shift(w, id, clock, share, noon);
			Assert.That(w[0] + w[1] + w[2] + w[3], Is.EqualTo(1f).Within(1e-6f));
			int main = Array.IndexOf(id, 0);
			Assert.That(main, Is.GreaterThanOrEqualTo(0), "the main not among the four takes the weakest other slot");
			Assert.That(w[main], Is.EqualTo(0.9f).Within(1e-6f));

			w = new[] { 0.5f, 0.5f, 0f, 0f };
			id = new[] { 3, 1, 1, 1 };
			BiomeGroundClock.Shift(w, id, clock, share, noon);
			Assert.That(w[0], Is.EqualTo(0f).Within(1e-6f), "no main to give to: the others take it by the renormalising");
			Assert.That(w[1], Is.EqualTo(1f).Within(1e-6f));

			// An unclocked scene (the share row's flag 0) is untouched, whatever the clock.
			var plainShare = new Vector4[4];
			for (int i = 0; i < 4; i++)
			{
				plainShare[i] = new Vector4(0f, -1f, 0f, 0f);
			}
			w = new[] { 0.25f, 0.25f, 0.5f, 0f };
			id = new[] { 0, 1, 2, 0 };
			BiomeGroundClock.Shift(w, id, new Vector4[4], plainShare, noon);
			Assert.That(w, Is.EqualTo(new[] { 0.25f, 0.25f, 0.5f, 0f }));
		}

		[Test]
		public void ClockShader_MirrorsTheCSharp()
		{
			string hlsl = Read(Path.Combine(Shaders, "FishGroundClock.hlsl"));
			StringAssert.Contains($"#define FISH_GROUND_CLOCK_YEAR {(int)BiomeClockKind.Year}", hlsl);
			StringAssert.Contains($"#define FISH_GROUND_CLOCK_DAY {(int)BiomeClockKind.Day}", hlsl);
			StringAssert.Contains("float span = frac(to - from);", hlsl);
			StringAssert.Contains("if (frac(phase - from) <= span)", hlsl);
			StringAssert.Contains("float d = min(FishGroundClockRound(phase - from), FishGroundClockRound(phase - to));", hlsl);
			StringAssert.Contains("return 1.0 - t * t * (3.0 - 2.0 * t);", hlsl);
			StringAssert.Contains("return floorShare + (1.0 - floorShare) * w;", hlsl);
			StringAssert.Contains("float4 first = FishLayerRow(ids.x, FISH_LAYER_ROW_CLOCK_SHARE);", hlsl);
			StringAssert.Contains("float kept = FishGroundClockPickId(id, at) == to ? FishGroundClockPick(w, at) : 0.0;", hlsl);
			StringAssert.Contains("w = FishGroundClockPut(w, at, kept + moved);", hlsl);
			// FXC (D3D and the WebGPU path) cannot unroll a loop that writes a vector by index: no "w[...] =" anywhere.
			Assert.That(Regex.IsMatch(hlsl, @"\b(w|id)\[[^\]]+\]\s*[-+*/]?=(?!=)"), Is.False, "no indexed vector l-value in the clock shift");
			StringAssert.Contains("float4 _FishGroundClock;", hlsl);
			string input = Read(Path.Combine(Shaders, "FishWeatherTerrainArrayInput.hlsl"));
			StringAssert.Contains("#include \"FishGroundClock.hlsl\"", input);
			Assert.That(Regex.Matches(input, @"FishGroundClockShift\(weights, ids\);").Count, Is.EqualTo(2), "both FishTopLayers (tile and backdrop)");
			string sky = Read("Assets/Scripts/Client/World/Weather/Sky/GlowSky.cs");
			StringAssert.Contains("Shader.PropertyToID(\"_FishGroundClock\")", sky);
			StringAssert.Contains("Shader.SetGlobalVector(GroundClockId, GroundClockOf(state));", sky);
			StringAssert.Contains("if (state == null)\n\t\t\t{\n\t\t\t\treturn Vector4.zero;", sky);
		}

		[Test]
		public void LayerTable_SaysNoneWithoutAClock_AndFlagsEveryColumnWithOne()
		{
			Vector4[] plain = TerrainLayerTable.Pack(3, null, null, null, null, null, null, null, null, null);
			for (int l = 0; l < TerrainLayerTable.Width; l++)
			{
				Assert.That(plain[TerrainLayerTable.RowClock * TerrainLayerTable.Width + l], Is.EqualTo(Vector4.zero));
				Assert.That(plain[TerrainLayerTable.RowClockShare * TerrainLayerTable.Width + l], Is.EqualTo(new Vector4(0f, -1f, 0f, 0f)));
			}
			var clock = new Vector4[3];
			clock[1] = BiomeGroundClock.Year(0.2f, 0.6f, 0.3f).Pack();
			var share = new[] { Vector4.zero, new Vector4(0.3f, 0f, 0f, 0f), Vector4.zero };
			Vector4[] clocked = TerrainLayerTable.Pack(3, null, null, null, null, null, null, null, null, null, null, null, clock, share);
			Assert.That(clocked[TerrainLayerTable.RowClock * TerrainLayerTable.Width + 1], Is.EqualTo(clock[1]));
			Assert.That(clocked[TerrainLayerTable.RowClockShare * TerrainLayerTable.Width + 1], Is.EqualTo(new Vector4(0.3f, 0f, 0f, 1f)));
			Assert.That(clocked[TerrainLayerTable.RowClockShare * TerrainLayerTable.Width + 0], Is.EqualTo(new Vector4(0f, -1f, 0f, 1f)));
			Assert.That(clocked[TerrainLayerTable.RowClockShare * TerrainLayerTable.Width + 200].w, Is.EqualTo(1f), "every column says so");
			// Every other row is what it was without a clock.
			for (int i = 0; i < TerrainLayerTable.RowClock * TerrainLayerTable.Width; i++)
			{
				Assert.That(clocked[i], Is.EqualTo(plain[i]));
			}
			// The baker with no uses: no clock, no main.
			var c = new List<Vector4>();
			var s = new List<Vector4>();
			TerrainArrayBaker.ClocksOf(new List<TerrainArrayLayerSource> { new TerrainArrayLayerSource(), null }, 2, c, s);
			Assert.That(c, Is.EqualTo(new[] { Vector4.zero, Vector4.zero }));
			Assert.That(s, Is.EqualTo(new[] { new Vector4(0f, -1f, 0f, 0f), new Vector4(0f, -1f, 0f, 0f) }));
		}

		// ── The painter (managed Perlin: nativerun or Unity) ──────────

		private static SceneTerrainPalette.Entry Entry(int biome, int layer, PaletteRole role, string slot, TerrainTextureLayer source, int ground = -1)
			=> new SceneTerrainPalette.Entry { BiomeIndex = biome, LayerIndex = layer, Role = role, Slot = slot, Source = source, Ground = ground, VariantIndex = -1 };

		private static SceneBiomeField OneBiome() => SceneBiomeField.FromCells(8, 8, 64f, Vector2.zero, new BiomeTemplate[] { null }, new byte[64], 24f, 7u);

		private static BiomeSplatOptions Options() => new BiomeSplatOptions { NormalizedHeight = (x, y, z) => 0.45f + y * 0.001f, Seed = 11u, Compass = BiomeScatterCompass.Plain(new Vector2(1f, 0f)) };

		private static List<SceneTerrainPalette.Entry> Palette(TerrainTextureLayer detail, BedTextureLayer bed = null)
		{
			var list = new List<SceneTerrainPalette.Entry>
			{
				Entry(0, 0, PaletteRole.Main, "main", new TerrainTextureLayer(), 0),
				Entry(0, 1, PaletteRole.Detail, "detail/0", detail, 1),
				Entry(0, 2, PaletteRole.Detail, "detail/1", new TerrainTextureLayer { blendNoiseScale = 70f, blendSharpness = 1.5f }, 2),
				Entry(0, 3, PaletteRole.Cliff, "cliff/0", new CliffTextureLayer { minCliffAngle = 40f, maxCliffAngle = 90f }),
				Entry(0, 4, PaletteRole.Submerged, "lakebed", new TerrainTextureLayer()),
			};
			if (bed != null)
			{
				list.Add(Entry(0, 5, PaletteRole.Submerged, "bed/0", bed));
			}
			return list;
		}

		[Test]
		public void Painter_WithNoBandSet_IsTheOldPathToTheBit()
		{
			SceneBiomeField field = OneBiome();
			SceneTerrainPalette palette = SceneTerrainPalette.FromEntries(1, 5, Palette(new TerrainTextureLayer { blendNoiseScale = 40f, blendSharpness = 2f }));
			Assert.That(BiomeSplatPainter.Weigher.NeedsDownhill(palette), Is.False);
			var weigher = new BiomeSplatPainter.Weigher(palette, field, Options());
			var a = new float[5];
			var b = new float[5];
			var random = new System.Random(5);
			for (int i = 0; i < 3000; i++)
			{
				float x = (float)random.NextDouble() * 500f, z = (float)random.NextDouble() * 500f, y = (float)random.NextDouble() * 40f - 4f;
				float slope = (float)random.NextDouble() * 60f;
				var fall = new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);
				weigher.Weigh(x, y, z, slope, a, BiomeSplatPainter.Weigher.NoClimate);
				weigher.Weigh(x, y, z, slope, b, BiomeSplatPainter.Weigher.NoClimate, fall);
				for (int k = 0; k < 5; k++)
				{
					Assert.That(BitConverter.SingleToInt32Bits(b[k]), Is.EqualTo(BitConverter.SingleToInt32Bits(a[k])), $"point {i} layer {k}");
				}
			}
		}

		[Test]
		public void Painter_FacingBand_PaintsOnlyTheShadedFaces()
		{
			SceneBiomeField field = OneBiome();
			var frost = new TerrainTextureLayer { blendNoiseScale = 6f, blendSharpness = 0.1f, facingBand = BiomeFacingBand.Of(BiomeFacing.Poleward) };
			SceneTerrainPalette palette = SceneTerrainPalette.FromEntries(1, 5, Palette(frost));
			Assert.That(BiomeSplatPainter.Weigher.NeedsDownhill(palette), Is.True);
			var weigher = new BiomeSplatPainter.Weigher(palette, field, Options());
			var w = new float[5];
			float shaded = 0f, sunny = 0f;
			for (int i = 0; i < 200; i++)
			{
				float x = 13f + i * 2.3f, z = 40f + i * 1.7f;
				weigher.Weigh(x, 10f, z, 20f, w, BiomeSplatPainter.Weigher.NoClimate, new Vector2(0f, 0.34f));
				shaded += w[1];
				weigher.Weigh(x, 10f, z, 20f, w, BiomeSplatPainter.Weigher.NoClimate, new Vector2(0f, -0.34f));
				sunny += w[1];
				Assert.That(w[1], Is.EqualTo(0f), "never on a face looking at the sun");
			}
			Assert.That(shaded, Is.GreaterThan(20f), "the shaded faces carry it");
		}

		[Test]
		public void Painter_FallLineStretch_DrawsPatchesDownTheSlope()
		{
			SceneBiomeField field = OneBiome();
			var track = new TerrainTextureLayer { blendNoiseScale = 30f, blendSharpness = 1f, fallLineStretch = 4f };
			SceneTerrainPalette palette = SceneTerrainPalette.FromEntries(1, 5, Palette(track));
			var weigher = new BiomeSplatPainter.Weigher(palette, field, Options());
			var w = new float[5];
			Vector2 down = new Vector2(0.5f, 0f);   // falling toward +x at 30°
			float along = 0f, across = 0f;
			int n = 0;
			for (int i = 0; i < 40; i++)
			{
				for (int j = 0; j < 40; j++)
				{
					float x = 50f + i * 7f, z = 60f + j * 7f;
					weigher.Weigh(x, 10f, z, 30f, w, BiomeSplatPainter.Weigher.NoClimate, down);
					float here = w[1];
					weigher.Weigh(x + 6f, 10f, z, 30f, w, BiomeSplatPainter.Weigher.NoClimate, down);
					along += Mathf.Abs(w[1] - here);
					weigher.Weigh(x, 10f, z + 6f, 30f, w, BiomeSplatPainter.Weigher.NoClimate, down);
					across += Mathf.Abs(w[1] - here);
					n++;
				}
			}
			Assert.That(along / n, Is.LessThan(0.75f * across / n), "the ground changes far less down the fall line than across it");
			// Deterministic: the same point weighs the same.
			var again = new BiomeSplatPainter.Weigher(palette, field, Options());
			var a = new float[5];
			weigher.Weigh(123f, 10f, 321f, 30f, w, BiomeSplatPainter.Weigher.NoClimate, down);
			again.Weigh(123f, 10f, 321f, 30f, a, BiomeSplatPainter.Weigher.NoClimate, down);
			Assert.That(a, Is.EqualTo(w));
		}

		[Test]
		public void Painter_SiteBand_PaintsRoundTheSitesOnly()
		{
			SceneBiomeField field = OneBiome();
			var halo = new TerrainTextureLayer { blendNoiseScale = 5f, blendSharpness = 0.1f, siteBand = BiomeSiteBand.Of("WhaleFall", 10f) };
			SceneTerrainPalette palette = SceneTerrainPalette.FromEntries(1, 5, Palette(halo));
			BiomeSplatOptions options = Options();
			options.Sites = new List<BiomeScatterSite> { new BiomeScatterSite { Centre = new Vector2(200f, 200f), Radius = 12f, Kind = "SunkenWhaleFall" } };
			var weigher = new BiomeSplatPainter.Weigher(palette, field, options);
			var w = new float[5];
			weigher.Weigh(300f, 10f, 300f, 2f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.That(w[1], Is.EqualTo(0f), "far from the whale fall");
			float near = 0f;
			for (int i = 0; i < 20; i++)
			{
				weigher.Weigh(195f + i * 0.5f, 10f, 200f, 2f, w, BiomeSplatPainter.Weigher.NoClimate);
				near += w[1];
			}
			Assert.That(near, Is.GreaterThan(2f), "on the whale fall");
			var none = new BiomeSplatPainter.Weigher(palette, field, Options());
			none.Weigh(200f, 10f, 200f, 2f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.That(w[1], Is.EqualTo(0f), "a scene with no sites: nowhere");
		}

		[Test]
		public void Painter_BedZoneBand_KeepsADrownedGroundToItsPart()
		{
			SceneBiomeField field = OneBiome();
			var paving = new BedTextureLayer { waters = BiomeBedWaters.Any, depthRange = Vector2.zero };
			var plain = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 6, Palette(new TerrainTextureLayer(), paving)), field, Options());
			var w = new float[6];
			plain.Weigh(100f, -6f, 100f, 1f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.That(w[5], Is.GreaterThan(0.5f), "unzoned: the bed lies under the sea");
			var zoned = new BedTextureLayer { waters = BiomeBedWaters.Any, depthRange = Vector2.zero, landformBand = BiomeLandformBand.Of(BiomeLandformPart.Floor, "SinkingQuarter") };
			var weigher = new BiomeSplatPainter.Weigher(SceneTerrainPalette.FromEntries(1, 6, Palette(new TerrainTextureLayer(), zoned)), field, Options());
			weigher.Weigh(100f, -6f, 100f, 1f, w, BiomeSplatPainter.Weigher.NoClimate);
			Assert.That(w[5], Is.EqualTo(0f), "zoned with no landform there: nowhere, the lakebed takes the bed");
			Assert.That(w[4], Is.GreaterThan(0.5f));
		}

		// ── Spec, authoring, defaults ─────────────────────────────────

		[Test]
		public void Marks_AreUnchangedWithNoBand_AndCarryEachBandWhenSet()
		{
			var layer = new TerrainTextureLayer();
			Assert.That(BiomeArtAuthoring.ClimateMark(layer), Is.Empty);
			Assert.That(layer.HasGroundBands, Is.False);
			Assert.That(BiomeArtAuthoring.BedMark(new BedTextureLayer()), Is.EqualTo(BiomeArtAuthoring.BedMark(new BiomeArtSpec.Entry.BedSpec { Waters = BiomeBedWaters.Inland })));
			StringAssert.DoesNotContain("|z", BiomeArtAuthoring.BedMark(new BedTextureLayer()));

			var spec = new BiomeArtSpec.Layer { Family = "Frost" }
				.Facing(BiomeFacing.Poleward).FallLine(4f).NearSites("WhaleFall", 10f).Clock(BiomeGroundClock.Day(0.8f, 0.45f));
			Assert.That(spec.HasGroundBands, Is.True);
			BiomeArtAuthoring.WriteClimateBand(layer, spec);
			string mark = BiomeArtAuthoring.ClimateMark(layer);
			Assert.That(mark, Is.EqualTo(BiomeArtAuthoring.ClimateMark(spec)), "written == wanted");
			StringAssert.StartsWith("t-|h-|fface 0 ", mark);
			StringAssert.Contains("|l40000|", mark);
			StringAssert.Contains("|nnear WhaleFall 10", mark);
			StringAssert.Contains("|kclock 2 0.8,0.45,", mark);
			Assert.That(layer.facingBand.On && layer.fallLineStretch == 4f && layer.siteBand.On && layer.clock.Kind == BiomeClockKind.Day, Is.True);
			BiomeArtAuthoring.WriteClimateBand(layer, new BiomeArtSpec.Layer { Family = "Frost" });
			Assert.That(BiomeArtAuthoring.ClimateMark(layer), Is.Empty, "the spec dropping them clears them");
			Assert.That(new BiomeArtSpec.Layer().FallLine(1f).FallLineStretch, Is.EqualTo(0f), "1 is no stretch");

			var bed = new BiomeArtSpec.Entry.BedSpec { Waters = BiomeBedWaters.Lake, Depth = new Vector2(0.2f, 1.5f), Zone = BiomeLandformBand.Of(BiomeLandformPart.Floor, "SinkingQuarter") };
			var slot = new BedTextureLayer();
			BiomeArtAuthoring.WriteBedBands(slot, bed);
			Assert.That(BiomeArtAuthoring.BedMark(slot), Is.EqualTo(BiomeArtAuthoring.BedMark(bed)));
			StringAssert.EndsWith("|zzone Floor SinkingQuarter", BiomeArtAuthoring.BedMark(slot));

			var rule = new PrefabSpawnRule { prefabs = new GameObject[0] };
			string before = BiomeArtAuthoring.Fingerprint(rule);
			Assert.That(before, Is.EqualTo("1c8a98b57d35dd8c"), "a default rule's mark, pinned from before the band");
			rule.facingBand = BiomeFacingBand.Of(BiomeFacing.Poleward);
			Assert.That(BiomeArtAuthoring.Fingerprint(rule), Is.Not.EqualTo(before));
		}

		[Test]
		public void Defaults_NameNoneOfIt_OnEverySpecAndTemplate()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			int rules = 0;
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				if (BiomeAuthoredRounds.Contains(entry.Biome))
				{
					// An authored round names its own bands (BiomeAuthoredRoundTests).
					continue;
				}
				var layers = new List<BiomeArtSpec.Layer> { entry.Main, entry.Bed };
				layers.AddRange(entry.Details);
				foreach (BiomeArtSpec.Entry.BedSpec bed in entry.BedLayers)
				{
					layers.Add(bed.Layer);
					Assert.That(bed.Zone.On, Is.False, $"{entry.Biome} zones no bed yet");
				}
				foreach (BiomeArtSpec.Layer layer in layers)
				{
					if (layer != null)
					{
						Assert.That(layer.HasGroundBands, Is.False, $"{entry.Biome} names no ground band yet (its round authors it)");
					}
				}
				foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter s) in entry.Rules())
				{
					var versions = new List<BiomeArtSpec.Scatter> { s };
					versions.AddRange(s.Earlier);
					foreach (BiomeArtSpec.Scatter v in versions)
					{
						rules++;
						Assert.That(v.Facing.On, Is.False, $"{entry.Biome} '{v.Name}' faces a rule before its round");
						Assert.That(BiomeArtAuthoring.ToRule(entry.Biome, v, new GameObject[0]).facingBand.On, Is.False);
					}
				}
			}
			Assert.That(rules, Is.GreaterThan(0));
			int templates = 0;
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (!Regex.IsMatch(text, "^  MinTemperature:", RegexOptions.Multiline))
				{
					continue;
				}
				templates++;
				string name = Path.GetFileName(path);
				if (BiomeAuthoredRounds.Contains(Path.GetFileNameWithoutExtension(path)))
				{
					continue;
				}
				Assert.That(Regex.IsMatch(text, @"^\s+(facingBand|siteBand):\s*\n\s+On: 1", RegexOptions.Multiline), Is.False, $"{name} has a facing or site band");
				Assert.That(Regex.IsMatch(text, @"^\s+fallLineStretch: [1-9]", RegexOptions.Multiline), Is.False, $"{name} stretches a layer");
				Assert.That(Regex.IsMatch(text, @"^\s+clock:\s*\n\s+Kind: [12]", RegexOptions.Multiline), Is.False, $"{name} clocks a layer");
				Assert.That(Regex.IsMatch(text, @"\|f(face)|\|kclock|\|nnear|\|l\d"), Is.False, $"{name} holds a ground-band mark");
			}
			Assert.That(templates, Is.EqualTo(83));
			var fresh = new TerrainTextureLayer();
			Assert.That(fresh.HasGroundBands || !fresh.clock.IsNone, Is.False);
			Assert.That(new PrefabSpawnRule().facingBand.On, Is.False);
		}

		/// <summary>A sixth detail slot (RGP (f)): nothing caps a biome's details; slot keys past the fifth read and write.</summary>
		[Test]
		public void SixthDetailSlot_HasAKeyAndAGround()
		{
			Assert.That(BiomeLocalArt.DetailSlot(5), Is.EqualTo("detail/5"));
			Assert.That(BiomeLocalArt.GroundIndexOf("detail/5"), Is.EqualTo(6));
			Assert.That(BiomeLocalArt.GroundSlot(6), Is.EqualTo("detail/5"));
		}

		[Test]
		public void Wiring_ReachesTheCutAndTheScatter()
		{
			string generator = Read(Path.Combine(World, "SceneGenerator.cs"));
			StringAssert.Contains("Compass = TerrainScatter.CompassFor(request, system),\n\t\t\t\tSites = TerrainScatter.SitesOf(poi?.Plan),\n\t\t\t};", generator);
			string scatter = Read(Path.Combine(World, "Terrain/TerrainScatter.cs"));
			Assert.That(Regex.Matches(scatter, @"facingBand\.Weight\(Downhill\(frame, heights, heightRes, u, v\)").Count, Is.EqualTo(2), "instances and carpets");
			string painter = Read(Path.Combine(World, "Terrain/BiomeSplatPainter.cs"));
			StringAssert.Contains("bool downhills = Weigher.NeedsDownhill(palette);", painter);
			StringAssert.Contains("Vector2[] fall = downhills ? new Vector2[resolution * resolution] : null;", painter);
		}

		/// <summary>Unity only (a template is a ScriptableObject): a climate variant's glowing override switches the glow table on.</summary>
		[Test]
		public void GlowTable_SeesAClimateVariantsGlowingOverride()
		{
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			try
			{
				Assert.That(FishMMO.Client.GroundGlowTable.Glows(biome), Is.False);
				var overrideLayer = new TerrainTextureLayer { glow = BiomeGroundGlow.Whole() };
				biome.ClimateVariants.Add(new BiomeClimateVariant { Name = "Hot", TextureLayerOverrides = new List<TerrainTextureLayer> { overrideLayer } });
				Assert.That(FishMMO.Client.GroundGlowTable.Glows(biome), Is.True);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(biome);
			}
		}
	}
}
