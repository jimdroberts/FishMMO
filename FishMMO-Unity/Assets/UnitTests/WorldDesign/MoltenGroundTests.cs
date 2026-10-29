using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Molten ground (2026-10-25, phase I2r; decisions 65, 93, 95): a ground layer's glow a pure function of the shared clock,
	/// continuous across the wrap and FishGroundGlow.hlsl constant for constant; the lava-datum bands and landforms; the lava lines
	/// and moats placed deterministically; the curtains of fire on THE slot function; and every template, spec and preset that names
	/// none of it unchanged.
	/// </summary>
	[TestFixture]
	public class MoltenGroundTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders";
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";

		private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

		private static Vector4 SeamGlow => BiomeGroundGlow.Seams(1400f, 6f, 400).Pack();

		// ── Enums and packing ─────────────────────────────────────────

		[Test]
		public void Enums_AreAppendOnly()
		{
			Assert.That((int)BiomeGroundGlowMode.None, Is.EqualTo(0));
			Assert.That((int)BiomeGroundGlowMode.Seams, Is.EqualTo(1));
			Assert.That((int)BiomeGroundGlowMode.Whole, Is.EqualTo(2));
			Assert.That(BiomeGroundGlowModes.Last, Is.EqualTo(BiomeGroundGlowMode.Whole));
			Assert.That((int)BiomeMoatWater.Wet, Is.EqualTo(0));
			Assert.That((int)BiomeMoatWater.Dry, Is.EqualTo(1));
			Assert.That((int)BiomeMoatWater.Frozen, Is.EqualTo(2));
			Assert.That((int)BiomeMoatWater.Lava, Is.EqualTo(3));
			Assert.That((int)BiomeLandformDatum.Ground, Is.EqualTo(0));
			Assert.That((int)BiomeLandformDatum.SeaSurface, Is.EqualTo(1));
			Assert.That((int)BiomeLandformDatum.LavaSurface, Is.EqualTo(2));
			Assert.That(TerrainLayerTable.RowGlow, Is.EqualTo(8));
			// GROUND-BANDS (2026-10-26) appended the clock rows 9 and 10 after the glow row.
			Assert.That(TerrainLayerTable.Rows, Is.EqualTo(11));
		}

		[Test]
		public void Glow_PacksAndReadsBack_PeriodsDivideTheWrap()
		{
			Assert.That(default(BiomeGroundGlow).IsNone, Is.True);
			Assert.That(default(BiomeGroundGlow).Pack(), Is.EqualTo(Vector4.zero));
			Assert.That(default(BiomeGroundGlow).Signature(), Is.Empty);
			foreach (int p in BiomeGroundGlowModes.Periods)
			{
				Assert.That(BiomeGroundGlow.WrapSeconds % p, Is.EqualTo(0), $"{p} s divides the wrap");
			}
			for (int s = -5; s < 12000; s += 37)
			{
				Assert.That(BiomeGroundGlow.WrapSeconds % BiomeGroundGlowModes.SnapPeriod(s), Is.EqualTo(0));
			}
			BiomeGroundGlow seams = BiomeGroundGlow.Seams(1350f, 8f, 397, 0.8f);
			Vector4 packed = seams.Pack();
			Assert.That(BiomeGroundGlowModes.ModeOf(packed, out int period), Is.EqualTo(BiomeGroundGlowMode.Seams));
			Assert.That(period, Is.EqualTo(400), "snapped to the nearest divisor");
			Assert.That(packed.y, Is.EqualTo(1350f));
			Assert.That(packed.z, Is.EqualTo(0.8f).Within(1e-6f));
			Assert.That(packed.w, Is.EqualTo(8f));
			Assert.That(BiomeGroundGlowModes.ModeOf(BiomeGroundGlow.Whole(50f).Pack(), out int none), Is.EqualTo(BiomeGroundGlowMode.Whole));
			Assert.That(none, Is.EqualTo(0));
			Assert.That(seams.Signature(), Is.EqualTo("Seams|1350|0.8|8|400"));
			Assert.That(BiomeGroundGlowModes.TryParse("whole", out BiomeGroundGlowMode whole) && whole == BiomeGroundGlowMode.Whole, Is.True);
		}

		// ── The seams on the shared clock ─────────────────────────────

		[Test]
		public void Seams_ArePure_Bounded_AndMeetThemselvesAcrossTheWrap()
		{
			var rng = new System.Random(3);
			for (int i = 0; i < 2000; i++)
			{
				float x = (float)(rng.NextDouble() * 4000 - 2000), z = (float)(rng.NextDouble() * 4000 - 2000);
				float t = (float)(rng.NextDouble() * GroundGlow.WrapSeconds);
				float a = GroundGlow.KelvinAt(SeamGlow, x, z, t, 300f);
				Assert.That(GroundGlow.KelvinAt(SeamGlow, x, z, t, 300f), Is.EqualTo(a), "pure");
				Assert.That(a, Is.InRange(GroundGlow.FloorKelvin - 1e-3f, 1400f + 1e-3f));
				// A whole number of every period in the wrap: one wrap later is the same moment.
				float wrapped = GroundGlow.KelvinAt(SeamGlow, x, z, t + GroundGlow.WrapSeconds, 300f);
				Assert.That(wrapped, Is.EqualTo(a).Within(2f), "the wrap holds whole overturns");
			}
			// Continuous at the wrap itself: just before it reads as just after zero.
			for (int i = 0; i < 200; i++)
			{
				float x = i * 3.7f, z = i * -2.3f;
				float before = GroundGlow.KelvinAt(SeamGlow, x, z, GroundGlow.WrapSeconds - 0.01f, 300f);
				float after = GroundGlow.KelvinAt(SeamGlow, x, z, 0.01f, 300f);
				Assert.That(Mathf.Abs(before - after), Is.LessThan(8f), $"no jump at the wrap ({x}, {z})");
			}
		}

		[Test]
		public void Seams_NeverJumpInTime_AndGlowHotterThanThePlates()
		{
			// Every 0.05 s over two full periods at a spread of points: no step larger than the skin's fastest cooling allows.
			float worst = 0f;
			for (int p = 0; p < 40; p++)
			{
				float x = p * 13.1f - 200f, z = p * 7.7f + 50f;
				float last = GroundGlow.KelvinAt(SeamGlow, x, z, 0f, 300f);
				for (float t = 0.05f; t < 800f; t += 0.05f)
				{
					float k = GroundGlow.KelvinAt(SeamGlow, x, z, t, 300f);
					worst = Mathf.Max(worst, Mathf.Abs(k - last));
					last = k;
				}
			}
			Assert.That(worst, Is.LessThan(20f), "kelvin per 0.05 s");
			// On average a seam is hotter than the plate beside it.
			double seam = 0, plate = 0;
			int n = 0, m = 0;
			for (int i = 0; i < 4000; i++)
			{
				float x = i * 0.731f, z = i * 0.377f, t = i * 1.9f;
				float edge = GroundGlow.PlatesAt(x, z, 6f, out uint owner, out uint neighbour);
				float k = GroundGlow.SeamKelvinAt(x, z, t, 1400f, 6f, 400);
				if (edge < 0.05f)
				{
					seam += k;
					n++;
				}
				else if (edge > 1.5f)
				{
					plate += k;
					m++;
				}
				Assert.That(owner, Is.Not.EqualTo(neighbour));
			}
			Assert.That(n, Is.GreaterThan(10));
			Assert.That(m, Is.GreaterThan(10));
			Assert.That(seam / n, Is.GreaterThan(plate / m + 150.0), "seams glow hotter than the plates beside them");
		}

		[Test]
		public void Colour_IsPlanckRed_Rising_AndTheMeltAtItsExposure()
		{
			Assert.That(GroundGlow.Glow(350f, GroundGlow.NightResponse), Is.EqualTo(Vector3.zero), "below the visible floor");
			Vector3 melt = GroundGlow.Glow(GroundGlow.ReferenceKelvin, GroundGlow.NightResponse);
			Assert.That(Mathf.Max(melt.x, Mathf.Max(melt.y, melt.z)), Is.EqualTo(GroundGlow.Exposure).Within(1e-3f));
			float lastY = 0f, lastRatio = float.MaxValue;
			for (float k = 700f; k <= 1600f; k += 50f)
			{
				Vector3 c = GroundGlow.Glow(k, GroundGlow.NightResponse);
				float y = GroundGlow.Luminance(c);
				Assert.That(y, Is.GreaterThan(lastY), $"brighter at {k} K");
				float ratio = c.x / Mathf.Max(1e-30f, c.y);
				Assert.That(ratio, Is.LessThan(lastRatio), $"less red at {k} K");
				Assert.That(c.x, Is.GreaterThan(c.y));
				lastY = y;
				lastRatio = ratio;
			}
			// By day the eye takes in the true ratio: a cooling crust sinks well below the melt.
			Assert.That(GroundGlow.Luminance(GroundGlow.Glow(900f, GroundGlow.DayResponse)), Is.LessThan(GroundGlow.Luminance(GroundGlow.Glow(900f, GroundGlow.NightResponse))));
		}

		[Test]
		public void MaskShare_FollowsTheModes()
		{
			Assert.That(GroundGlow.MaskShare(Vector4.zero, 2000f), Is.EqualTo(0f));
			Assert.That(GroundGlow.MaskShare(BiomeGroundGlow.Whole().Pack(), 740f), Is.EqualTo(0f), "a Venus plain is below the Draper point");
			Assert.That(GroundGlow.MaskShare(BiomeGroundGlow.Whole(0f, 0.5f).Pack(), 1500f), Is.EqualTo(0.5f).Within(1e-5f));
			float seams = GroundGlow.MaskShare(SeamGlow, 300f);
			Assert.That(seams, Is.GreaterThan(0f).And.LessThan(0.5f));
			Assert.That(FishMMO.Client.GroundGlowTable.ShareOfTexel(0x00000001u, 0x000000FFu, new[] { 0f, 0.25f }), Is.EqualTo(0.25f).Within(1e-6f));
			Assert.That(FishMMO.Client.GroundGlowTable.ShareOfTexel(0x00000100u, 0x00007F7Fu, new[] { 0f, 0.5f }), Is.EqualTo(0.25f).Within(1e-3f));
		}

		[Test]
		public void Shader_MirrorsTheTwin_ConstantForConstant()
		{
			string hlsl = Read(Path.Combine(Shaders, "FishGroundGlow.hlsl"));
			float Define(string name)
			{
				Match m = Regex.Match(hlsl, @"#define " + name + @" ([0-9.]+)\s*\n");
				Assert.That(m.Success, Is.True, name);
				return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
			}
			Assert.That(Define("FISH_GROUND_GLOW_FLOOR"), Is.EqualTo(GroundGlow.FloorKelvin));
			Assert.That(Define("FISH_GROUND_GLOW_SKIN_TAU"), Is.EqualTo(GroundGlow.SkinTauSeconds));
			Assert.That(Define("FISH_GROUND_GLOW_SEAM_TAU"), Is.EqualTo(GroundGlow.SeamTauSeconds));
			Assert.That(Define("FISH_GROUND_GLOW_OPEN"), Is.EqualTo(GroundGlow.OpenShare));
			Assert.That(Define("FISH_GROUND_GLOW_SEAM_SHARE"), Is.EqualTo(GroundGlow.SeamShare));
			Assert.That(Define("FISH_GROUND_GLOW_JITTER"), Is.EqualTo(GroundGlow.Jitter));
			Assert.That(Define("FISH_GROUND_GLOW_REFERENCE"), Is.EqualTo(GroundGlow.ReferenceKelvin));
			Assert.That(Define("FISH_GROUND_GLOW_EXPOSURE"), Is.EqualTo(GroundGlow.Exposure));
			Assert.That(Define("FISH_GROUND_GLOW_NIGHT_RESPONSE"), Is.EqualTo(GroundGlow.NightResponse));
			Assert.That(Define("FISH_GROUND_GLOW_DAY_RESPONSE"), Is.EqualTo(GroundGlow.DayResponse));
			Assert.That(Define("FISH_GROUND_GLOW_VISIBLE"), Is.EqualTo(GroundGlow.VisibleFromKelvin));
			Assert.That(Regex.Match(hlsl, @"#define FISH_GROUND_GLOW_SALT 0x([0-9A-Fa-f]+)u").Groups[1].Value,
				Is.EqualTo(GroundGlow.Salt.ToString("X8")));
			Match k = Regex.Match(hlsl, @"#define FISH_GROUND_GLOW_K float3\(([0-9.]+), ([0-9.]+), ([0-9.]+)\)");
			Assert.That(float.Parse(k.Groups[1].Value, CultureInfo.InvariantCulture), Is.EqualTo(GroundGlow.RedK).Within(0.01f));
			Assert.That(float.Parse(k.Groups[2].Value, CultureInfo.InvariantCulture), Is.EqualTo(GroundGlow.GreenK).Within(0.01f));
			Assert.That(float.Parse(k.Groups[3].Value, CultureInfo.InvariantCulture), Is.EqualTo(GroundGlow.BlueK).Within(0.01f));
			Match l = Regex.Match(hlsl, @"#define FISH_GROUND_GLOW_LAMBDA float2\(([0-9.]+), ([0-9.]+)\)");
			Assert.That(float.Parse(l.Groups[1].Value, CultureInfo.InvariantCulture), Is.EqualTo(GroundGlow.RedWeight).Within(1e-6f));
			Assert.That(float.Parse(l.Groups[2].Value, CultureInfo.InvariantCulture), Is.EqualTo(GroundGlow.BlueWeight).Within(1e-6f));
			Assert.That(Read(Path.Combine(Shaders, "FishTerrainIndexMap.hlsl")), Does.Contain($"#define FISH_LAYER_ROW_GLOW {TerrainLayerTable.RowGlow} "));
			// The same hashes, the same order, the same tie rule as the twin.
			StringAssert.Contains("uint h = FishFloraCellHash(c, FISH_GROUND_GLOW_SALT);", hlsl);
			StringAssert.Contains("FishFloraUnit(h ^ 1u), FishFloraUnit(h ^ 2u)", hlsl);
			StringAssert.Contains("frac(seconds / period + FishFloraUnit(plate ^ 3u))", hlsl);
			StringAssert.Contains("float seconds = _FishWeatherMisc.w;", hlsl);
			StringAssert.Contains("whole - 8.0 * period", hlsl);
		}

		[Test]
		public void Terrain_DrawsTheGlow_AndKeepsTheBurn()
		{
			string passes = Read(Path.Combine(Shaders, "FishWeatherTerrainArrayPasses.hlsl"));
			StringAssert.Contains("#include \"FishGroundGlow.hlsl\"", passes);
			StringAssert.Contains("burnEmission = FishBurnGround(inputData.positionWS, inputData.normalWS, burnTrodden, albedo, smoothness);", passes);
			StringAssert.Contains("[branch] if (_FishGroundGlowInfo.x > 0.5)", passes);
			StringAssert.Contains("burnEmission += FishGroundGlow(IN.uvMainAndLM.xy, inputData.positionWS, inputData.normalWS, glowTrodden);", passes);
			Assert.That(Regex.Matches(passes, @"color\.rgb \+= \(?(half3\))?burnEmission;").Count, Is.EqualTo(2), "forward and GBuffer");
			// The glow sits inside the terrain-only block (the backdrop reads no index map).
			int include = passes.IndexOf("#include \"FishGroundGlow.hlsl\"", StringComparison.Ordinal);
			Assert.That(passes.LastIndexOf("#if !defined(FISH_TERRAIN_BACKDROP)", include, StringComparison.Ordinal), Is.GreaterThan(0));
			string flora = Read("Assets/Scripts/Client/World/Terrain/FloraPaletteMap.cs");
			StringAssert.Contains("GroundGlowTable.Build(rowBiomes, system, body);", flora);
			StringAssert.Contains("GroundGlowTable.Publish(on);", flora);
			StringAssert.Contains("GroundGlowTable.Release();", flora);
			StringAssert.Contains("GroundGlowMask.ShareAt", Read("Assets/Plugins/FishMMO Water/Scripts/LavaMask.cs"));
			StringAssert.Contains("GroundGlowMask.Version", Read("Assets/Plugins/FishMMO Water/Scripts/WaterSurface.cs"));
			StringAssert.Contains("plan.Glow.Add(GlowOf(layers[i]));", Read(World + "/Terrain/TerrainArrayBaker.cs"));
		}

		[Test]
		public void LayerTable_CarriesTheGlowRow_ZeroWithoutOne()
		{
			Vector4[] plain = TerrainLayerTable.Pack(3, null, null, null, null, null, null, null, null, null);
			for (int l = 0; l < TerrainLayerTable.Width; l++)
			{
				Assert.That(plain[TerrainLayerTable.RowGlow * TerrainLayerTable.Width + l], Is.EqualTo(Vector4.zero));
			}
			Vector4[] glowing = TerrainLayerTable.Pack(3, null, null, null, null, null, null, null, null, null, null, new[] { Vector4.zero, SeamGlow, SeamGlow, SeamGlow });
			Assert.That(glowing[TerrainLayerTable.RowGlow * TerrainLayerTable.Width + 1], Is.EqualTo(SeamGlow));
			Assert.That(glowing[TerrainLayerTable.RowGlow * TerrainLayerTable.Width + 3], Is.EqualTo(Vector4.zero), "past the scene's layers: none");
			// Every other row is what it was.
			for (int i = 0; i < TerrainLayerTable.RowGlow * TerrainLayerTable.Width; i++)
			{
				Assert.That(glowing[i], Is.EqualTo(plain[i]));
			}
		}

		// ── The lava datum ────────────────────────────────────────────

		[Test]
		public void LavaBand_WeighsTheHeightAboveTheLava()
		{
			var layer = new TerrainTextureLayer();
			Assert.That(layer.HasLavaBand, Is.False);
			Assert.That(layer.LavaWeight(5f), Is.EqualTo(1f), "off: everywhere");
			layer.useLavaBand = true;
			layer.lavaRange = new Vector2(0f, 2f);
			layer.lavaFalloff = 0.5f;
			Assert.That(layer.HasLavaBand, Is.True);
			Assert.That(layer.LavaWeight(float.NaN), Is.EqualTo(1f), "no lava: every band passes");
			Assert.That(layer.LavaWeight(1f), Is.EqualTo(1f));
			Assert.That(layer.LavaWeight(10f), Is.EqualTo(0f));
			Assert.That(layer.LavaWeight(-3f), Is.EqualTo(0f));
			Assert.That(new BiomeSplatOptions().LavaLevel, Is.NaN, "the painter measures no lava unless told");
			var spec = new BiomeArtSpec.Layer().Lava(-1f, 2f, 0.25f);
			Assert.That(spec.LavaBand, Is.EqualTo(new Vector2(-1f, 2f)));
			Assert.That(BiomeArtAuthoring.ClimateMark(spec), Is.EqualTo("t-|h-|v-10000,20000,2500"), "to 1/10 000, as every band");
			var written = new TerrainTextureLayer();
			BiomeArtAuthoring.WriteClimateBand(written, spec);
			Assert.That(BiomeArtAuthoring.ClimateMark(written), Is.EqualTo(BiomeArtAuthoring.ClimateMark(spec)), "written and wanted read the same");
			Assert.That(BiomeArtAuthoring.ClimateMark(new BiomeArtSpec.Layer()), Is.Empty, "no band, no mark: every old mark unchanged");
			string painter = Read(World + "/Terrain/BiomeSplatPainter.cs");
			StringAssert.Contains("aboveLava = anyLavaBand && !float.IsNaN(options.LavaLevel) ? worldY - options.LavaLevel : float.NaN;", painter);
			Assert.That(Regex.Matches(Read(World + "/SceneGenerator.cs"), @"LavaLevel = SceneGround\.LavaLevel\(request, system\)").Count, Is.EqualTo(2), "the scene and its horizon");
		}

		[Test]
		public void LavaDatum_FloorsAPateraUnderTheLava_AndStandsOnTheGroundWithout()
		{
			BiomeLandform patera = BiomeLandformPresets.Named("Patera");
			Assert.That(patera.Datum, Is.EqualTo(BiomeLandformDatum.LavaSurface));
			Assert.That(BiomeLandformPresets.Named("CrustIsland").Datum, Is.EqualTo(BiomeLandformDatum.LavaSurface));
			patera.Size = new Vector2(150f, 250f);
			patera.Density = 5f;
			patera.PerScene = 2;
			var withLava = Input(patera);
			withLava.LavaLevel = -6f;
			List<BiomeLandformSite> lava = SceneLandforms.Plan(withLava);
			Assert.That(lava.Count, Is.GreaterThan(0));
			foreach (BiomeLandformSite site in lava)
			{
				Assert.That(site.Level, Is.InRange(-6f - 4f, -6f - 1f), "the floor 1-4 m under the lava");
			}
			List<BiomeLandformSite> dry = SceneLandforms.Plan(Input(patera));
			Assert.That(dry.Count, Is.EqualTo(lava.Count), "the same places");
			foreach (BiomeLandformSite site in dry)
			{
				Assert.That(site.Level, Is.Not.InRange(-10f, -7f), "no lava: on its ground");
			}
			// A sea-datum entry never reads the lava.
			BiomeLandform guyot = BiomeLandformPresets.Named("Guyot");
			Assert.That(guyot.Datum, Is.EqualTo(BiomeLandformDatum.SeaSurface));
			Assert.That(SceneLandforms.Plan(Input(guyot)).Count, Is.EqualTo(SceneLandforms.Plan(WithLava(Input(guyot))).Count));
		}

		private static LandformPlanInput WithLava(LandformPlanInput input)
		{
			input.LavaLevel = 5f;
			return input;
		}

		private static LandformPlanInput Input(BiomeLandform form)
		{
			const int cells = 64;
			const float cellMetres = 16f, half = cells * cellMetres * 0.5f;
			return new LandformPlanInput
			{
				Seed = 11u,
				CellsX = cells,
				CellsZ = cells,
				CellMetres = cellMetres,
				Origin = new Vector2(-half, -half),
				CellBiome = (x, z) => 0,
				Biomes = new List<(int, IReadOnlyList<BiomeLandform>)> { (1001, new[] { form }) },
				Ground = (x, z) => 2f + 0.01f * x + 3f * Mathf.Sin(x / 90f) * Mathf.Cos(z / 70f),
				SeaLevel = float.NegativeInfinity,
				WindYaw = 30f,
				SceneBounds = new Vector4(-half, -half, half, half),
			};
		}

		// ── Lava lines and moats ──────────────────────────────────────

		[Test]
		public void LavaLines_ComeFromMoltenLandformsOnly_Deterministically()
		{
			foreach (string name in new[] { "LavaChannel", "AaTongue", "Breakout", "EmberRill" })
			{
				Assert.That(BiomeLandformPresets.Named(name).Molten, Is.True, name);
				StringAssert.EndsWith("|molten", BiomeLandformPresets.Named(name).ToString());
			}
			Assert.That(BiomeLandformPresets.Named("lavarill").Kind, Is.EqualTo("EmberRill"));
			int molten = 0;
			foreach (BiomeLandformPresets.Preset preset in BiomeLandformPresets.All)
			{
				if (preset.Form.Molten)
				{
					molten++;
				}
				else
				{
					StringAssert.DoesNotContain("|molten", preset.Form.ToString(), preset.Name);
				}
			}
			Assert.That(molten, Is.EqualTo(4));
			var meander = new BiomeLandformSite
			{
				Form = BiomeLandformPresets.Named("Breakout"),
				BiomeID = 7,
				Seed = 99u,
				Segments = new List<BiomeLandformSegment> { new BiomeLandformSegment(new Vector2(0f, 0f), new Vector2(30f, 0f), 8f, 1f, 0f, 60f),
					new BiomeLandformSegment(new Vector2(30f, 0f), new Vector2(60f, 10f), 8f, 1f, 30f, 60f) },
			};
			var tongue = new BiomeLandformSite { Form = BiomeLandformPresets.Named("AaTongue"), BiomeID = 7, Seed = 5u, Centre = new Vector2(100f, 100f), HalfLength = 50f, HalfWidth = 20f, Yaw = 90f };
			var cold = new BiomeLandformSite { Form = BiomeLandformPresets.Named("Tumuli"), Centre = Vector2.zero, Radius = 10f };
			List<VentLine> lines = BiomeLavaChannels.FromLandforms(new[] { meander, tongue, cold });
			Assert.That(lines.Count, Is.EqualTo(3), "two segments and a tongue's axis; a tumulus none");
			Assert.That(lines[0].Half, Is.EqualTo(8f * BiomeLavaChannels.ChannelShare));
			Assert.That(lines[1].AlongA, Is.EqualTo(30f));
			Assert.That(lines[2].A.y, Is.EqualTo(50f).Within(1e-3f));
			Assert.That(lines[2].B.y, Is.EqualTo(150f).Within(1e-3f));
			Assert.That(lines[2].Half, Is.EqualTo(20f * BiomeLandformPresets.Named("AaTongue").Crown).Within(1e-5f));
			List<VentLine> again = BiomeLavaChannels.FromLandforms(new[] { meander, tongue, cold });
			for (int i = 0; i < lines.Count; i++)
			{
				Assert.That(again[i].A, Is.EqualTo(lines[i].A));
				Assert.That(again[i].B, Is.EqualTo(lines[i].B));
				Assert.That(again[i].Seed, Is.EqualTo(lines[i].Seed));
			}
			Assert.That(BiomeLavaChannels.FromLandforms(new[] { cold }), Is.Empty);
		}

		[Test]
		public void Ribbon_LiesOnTheGround_EveryRowWithinItsSpacing()
		{
			var line = new VentLine { A = new Vector2(0f, 0f), B = new Vector2(41f, 0f), Half = 2f, AlongA = 10f, LineLength = 100f };
			float Ground(float x, float z) => 0.2f * x + 0.1f * z;
			var p = new List<Vector3>();
			var f = new List<Vector2>();
			var idx = new List<int>();
			Assert.That(BiomeLavaChannels.Ribbon(line, Ground, p, f, idx), Is.True);
			int rows = p.Count / 2;
			Assert.That(41f / (rows - 1), Is.LessThanOrEqualTo(BiomeLavaChannels.RowMetres + 1e-4f));
			Assert.That(idx.Count, Is.EqualTo((rows - 1) * 6));
			for (int r = 0; r < rows; r++)
			{
				Vector3 left = p[2 * r], right = p[2 * r + 1];
				float mid = Ground(0.5f * (left.x + right.x), 0.5f * (left.z + right.z));
				Assert.That(left.y, Is.EqualTo(right.y), "level across");
				Assert.That(left.y, Is.GreaterThanOrEqualTo(mid + BiomeLavaChannels.LiftMetres - 1e-4f));
				Assert.That(left.y, Is.GreaterThan(Ground(left.x, left.z)));
				Assert.That(right.y, Is.GreaterThan(Ground(right.x, right.z)));
				Assert.That(f[2 * r].y, Is.EqualTo(-2f));
				Assert.That(f[2 * r + 1].y, Is.EqualTo(2f));
			}
			Assert.That(f[0].x, Is.EqualTo(10f), "metres along the whole line");
			Assert.That(f[f.Count - 1].x, Is.EqualTo(51f).Within(1e-3f));
			// Off the ground: nothing, and nothing left behind.
			p.Clear(); f.Clear(); idx.Clear();
			Assert.That(BiomeLavaChannels.Ribbon(line, (x, z) => x > 20f ? float.NaN : 0f, p, f, idx), Is.False);
			Assert.That(p, Is.Empty);
		}

		[Test]
		public void LavaMoat_DigsDry_FlagsLava_AndRingsItsFloor()
		{
			List<VentLine> ring = BiomeLavaChannels.MoatLines(new Vector3(10f, 0f, -5f), Array.Empty<float>(), 20f, 8f, 3u, 0);
			Assert.That(ring.Count, Is.EqualTo(BiomeLavaChannels.MoatSpokes));
			Assert.That(ring[ring.Count - 1].B, Is.EqualTo(ring[0].A), "closed");
			foreach (VentLine segment in ring)
			{
				Assert.That(Vector2.Distance(segment.A, new Vector2(10f, -5f)), Is.EqualTo(24f).Within(1e-3f), "round the ditch's middle");
				Assert.That(segment.Half, Is.EqualTo(0.5f * BiomeLavaChannels.MoatShare * 8f).Within(1e-5f));
			}
			Assert.That(BiomeLavaChannels.MoatLavaFlag, Is.EqualTo(2f));
			Assert.That(FishMMO.Water.LavaRibbonRenderer.MoatShaperName, Is.EqualTo(MoatShaper.ShaperName));
			string shaper = Read(World + "/PointsOfInterest/Shapers/MoatShaper.cs");
			StringAssert.Contains("if (moat == BiomeMoatWater.Dry || moat == BiomeMoatWater.Lava)", shaper);
			StringAssert.Contains("values[4] = wet ? 1f : LavaMoat(site) ? BiomeLavaChannels.MoatLavaFlag : 0f;", shaper);
			string generator = Read(World + "/SceneGenerator.cs");
			StringAssert.Contains("map.SetLavaLines(BiomeLavaChannels.FromLandforms(SceneLandforms.Sites(scene)));", generator);
			StringAssert.Contains("EnsureLavaRibbons(scene, map, field);", generator);
		}

		// ── Curtains of fire ──────────────────────────────────────────

		[Test]
		public void Curtains_StandOnFissures_OnTheSlotFunction_AcrossTheWrap()
		{
			var fissure = new VentLine { A = Vector2.zero, B = new Vector2(400f, 0f), LineLength = 400f, Half = 3f, Seed = 77u, BiomeID = 5, Layout = BiomeVentLayout.Fissures };
			BiomeFountainEpisodes schedule = BiomeFountainEpisodes.Volcanic;
			Assert.That(LavaCurtains.Holds(fissure, default), Is.False, "no episodes: none");
			VentLine grid = fissure;
			grid.Layout = BiomeVentLayout.Ridges;
			Assert.That(LavaCurtains.Holds(grid, schedule), Is.False, "only fissures");
			var curtains = new List<LavaCurtains.Curtain>();
			int seen = 0;
			for (float t = 0f; t < 10000f; t += 7f)
			{
				curtains.Clear();
				LavaCurtains.Of(fissure, schedule, t, curtains);
				foreach (LavaCurtains.Curtain c in curtains)
				{
					seen++;
					Assert.That(c.Strength, Is.EqualTo(BiomeFountainEpisodes.At(schedule, t, c.Seed)), "THE slot function");
					Assert.That(c.Height, Is.InRange(schedule.Heights.x, schedule.Heights.y));
					Assert.That(c.BiomeID, Is.EqualTo(5));
				}
			}
			Assert.That(seen, Is.GreaterThan(0), "the fissure erupts somewhere in the wrap");
			// Continuous across the wrap and pure.
			for (int k = 0; k < 3; k++)
			{
				uint seed = LavaCurtains.StretchSeed(77u, k * 400f / 3f);
				Assert.That(Mathf.Abs(BiomeFountainEpisodes.At(schedule, 9999.99f, seed) - BiomeFountainEpisodes.At(schedule, 0.01f, seed)), Is.LessThan(0.01f));
			}
			float period = LavaCurtains.FlightPeriod(schedule, 9.81f);
			Assert.That(BiomeGroundGlow.WrapSeconds / period, Is.EqualTo(Mathf.Round(BiomeGroundGlow.WrapSeconds / period)).Within(1e-3f), "the clots' period divides the wrap");
			// Defaults: a scene's lines with templates naming no episodes stand no curtain.
			var list = new List<LavaCurtains.Curtain>();
			LavaCurtains.Of(new[] { fissure }, id => default, 1234f, list);
			Assert.That(list, Is.Empty);
			string shader = Read(Path.Combine(Shaders, "FishVolcanicPlume.shader"));
			StringAssert.Contains("else if (population == 7)", shader);
			StringAssert.Contains("float u = frac(t / max(0.1, _PlumeBallistic.w) + frac(r.w * 7.31 + r.y));", shader);
			StringAssert.Contains("DrawCurtains(camera, material, profile, viewer2, reach);", Read("Assets/Scripts/Client/World/Weather/Sky/VolcanicPlumePresenter.cs"));
		}

		// ── Defaults: nothing changes ─────────────────────────────────

		[Test]
		public void Defaults_NameNoneOfIt_OnEveryTemplate()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				var layers = new List<BiomeArtSpec.Layer> { entry.Main };
				layers.AddRange(entry.Details);
				foreach (BiomeArtSpec.Layer layer in layers)
				{
					if (layer == null)
					{
						continue;
					}
					Assert.That(layer.Glow.IsNone, Is.True, $"{entry.Biome} names no glow yet (its round authors it)");
					Assert.That(layer.LavaBand.HasValue, Is.False, $"{entry.Biome} names no lava band yet");
				}
				Assert.That(entry.MoatWater, Is.Not.EqualTo(BiomeMoatWater.Lava), $"{entry.Biome} names no lava moat yet");
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
				Assert.That(Regex.IsMatch(text, @"^\s+useLavaBand: 1", RegexOptions.Multiline), Is.False, $"{Path.GetFileName(path)} has a lava band");
				Assert.That(Regex.IsMatch(text, @"^\s+glow:\s*\n\s+Mode: [12]", RegexOptions.Multiline), Is.False, $"{Path.GetFileName(path)} glows");
				Match moat = Regex.Match(text, @"^  moatWater: (\d+)", RegexOptions.Multiline);
				Assert.That(!moat.Success || moat.Groups[1].Value != "3", Is.True, $"{Path.GetFileName(path)} has a lava moat");
				Assert.That(Regex.IsMatch(text, @"\|molten"), Is.False, $"{Path.GetFileName(path)} names a molten landform");
			}
			Assert.That(templates, Is.EqualTo(83));
			// Off: the table publishes nothing and the terrain takes its old path; no glowing ground reaches the lava's mask.
			string table = Read("Assets/Scripts/Client/World/Terrain/GroundGlowTable.cs");
			StringAssert.Contains("info = on ? new Vector4(1f, worldKelvin, 0f, 0f) : Vector4.zero;", table);
			StringAssert.Contains("Shader.SetGlobalVector(InfoId, published ? info : Vector4.zero);", table);
			Assert.That(FishMMO.Client.GroundGlowTable.Glows(null), Is.False);
			Assert.That(new TerrainTextureLayer().glow.IsNone, Is.True);
		}
	}
}
