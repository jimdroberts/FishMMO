using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using FishMMO.Water;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Decision 32 of the biome suite (2026-10-18, phase I2k): the sea's SURFACE takes the look of its coast from a small texture
	/// baked with the scene's biome map (BiomeSeaCoast), on the same blend as the underwater look (BiomeSeaLooks.Coast), fading to
	/// the planet's sea offshore; and a site's fields never lie over another site's (PointOfInterestFieldClaims). Every default is
	/// what was drawn before: no biome has a sea look of its own, so no scene bakes a texture and the sea shader reads none.
	/// </summary>
	public class BiomeSeaCoastAndFieldClaimTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string WaterFolder = "Assets/Plugins/FishMMO Water";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string FieldsSource = World + "/PointsOfInterest/Features/PointOfInterestFields.cs";
		private const string StageSource = World + "/PointsOfInterest/PointOfInterestStage.cs";
		private const string ContextSource = World + "/PointsOfInterest/PointOfInterestSiteContext.cs";
		private const string FieldSource = World + "/Terrain/SceneBiomeField.cs";
		private const string MapSource = "Assets/Scripts/Shared/Implementation/Tools/Biomes/SceneBiomeMap.cs";

		private const int Bog = 7, Sea = 9, Meadow = 11;
		private static readonly BiomeSeaLook Peat = new BiomeSeaLook(new Color(0.18f, 0.12f, 0.05f, 1f), 6f, 1.6f);

		/// <summary>A 4 km × 2 km map: bog west of x = 0, open sea (today's look) east of it, 25 m cells.</summary>
		private static (int w, int h, int[] ids, Vector2 origin, Vector2 size) BogCoast(int west = Bog)
		{
			int w = 160, h = 80;
			var ids = new int[w * h];
			for (int j = 0; j < h; j++)
			{
				for (int i = 0; i < w; i++)
				{
					ids[j * w + i] = i < w / 2 ? west : Sea;
				}
			}
			return (w, h, ids, new Vector2(-2000f, -1000f), new Vector2(4000f, 2000f));
		}

		private static BiomeSeaLook? LookOf(int id) => id == Bog ? Peat : id == Sea || id == Meadow ? BiomeSeaLooks.Today : (BiomeSeaLook?)null;

		// ── The bake ──────────────────────────────────────────────────

		/// <summary>No biome with a look of its own (today's, none, or unknown IDs): no texture at all, and the read is exactly 1.</summary>
		[Test]
		public void Bake_IsEmpty_WhenNoBiomeHasALookOfItsOwn()
		{
			var (w, h, ids, origin, size) = BogCoast(Meadow);
			BiomeSeaCoast.Bake bake = BiomeSeaCoast.For(w, h, ids, origin, size, LookOf);
			Assert.That(bake.Any, Is.False);
			Assert.That(bake.Texels, Is.Empty);
			Assert.That(BiomeSeaCoast.For(w, h, ids, origin, size, id => null).Any, Is.False);
			Assert.That(BiomeSeaCoast.For(w, h, ids, origin, size, null).Any, Is.False);
			Assert.That(BiomeSeaCoast.For(w, h, new int[w * h], origin, size, LookOf).Any, Is.False, "no biome at all");
			Assert.That(BiomeSeaCoast.For(w, h, ids, origin, Vector2.zero, LookOf).Any, Is.False);
			Assert.That(BiomeSeaCoast.Sample(bake, 0f, 0f), Is.EqualTo(Vector4.one));
			Assert.That(BiomeSeaCoast.Ratios(Peat, 0f), Is.EqualTo(Vector4.one), "no coverage: exactly today's");
			var today = new byte[4];
			BiomeSeaCoast.Encode(BiomeSeaLooks.Today, 1f, today, 0);
			Assert.That(today, Is.EqualTo(new[] { BiomeSeaCoast.Neutral, BiomeSeaCoast.Neutral, BiomeSeaCoast.Neutral, BiomeSeaCoast.Neutral }), "today's look codes as 1");
		}

		/// <summary>Every one of the 83 biomes' spec and asset sea looks is today's: a map holding all of them bakes nothing.</summary>
		[Test]
		public void Bake_IsEmpty_ForEveryBiomeAsAuthored()
		{
			var looks = new Dictionary<int, BiomeSeaLook?>();
			int id = 1;
			foreach (string name in BiomeAssetNames())
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(name);
				Assert.That(entry, Is.Not.Null, name);
				looks[id++] = entry.SeaLook();
			}
			Assert.That(looks.Count, Is.EqualTo(83));
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				Match block = Regex.Match(Read(path), @"^  seaLook:\n((?:    .*\n)+)", RegexOptions.Multiline);
				if (block.Success)
				{
					string body = block.Groups[1].Value;
					looks[id++] = new BiomeSeaLook(Colour(body, "Tint"), Float(body, "Visibility"), Float(body, "Murk"));
				}
			}
			var ids = new int[looks.Count];
			int k = 0;
			foreach (int key in looks.Keys)
			{
				ids[k++] = key;
			}
			BiomeSeaCoast.Bake bake = BiomeSeaCoast.For(ids.Length, 1, ids, Vector2.zero, new Vector2(ids.Length * 25f, 25f), i => looks.TryGetValue(i, out BiomeSeaLook? l) ? l : null);
			Assert.That(bake.Any, Is.False, "a biome's look is not today's");
		}

		/// <summary>
		/// The bog's stain is strongest inland, about half at the shoreline, gone (exactly 1) past the reach, never stepping; and each
		/// texel is the underwater blend at its centre, coded.
		/// </summary>
		[Test]
		public void Bake_FallsOffFromTheCoast_AsTheUnderwaterBlendDoes()
		{
			var (w, h, ids, origin, size) = BogCoast();
			BiomeSeaCoast.Bake bake = BiomeSeaCoast.For(w, h, ids, origin, size, LookOf);
			Assert.That(bake.Any, Is.True);
			Assert.That(bake.Width, Is.EqualTo(BiomeSeaCoast.Texels(size.x + 2f * BiomeSeaCoast.Margin)));
			Assert.That(bake.Height, Is.EqualTo(BiomeSeaCoast.Texels(size.y + 2f * BiomeSeaCoast.Margin)));
			Assert.That(bake.Width, Is.InRange(120, 160), "about 128² for a 4 km scene");

			float reach = BiomeSeaLooks.ReachMetres;
			Vector4 inland = BiomeSeaCoast.Sample(bake, -600f, 13f);
			Vector4 shore = BiomeSeaCoast.Sample(bake, 0f, 13f);
			Vector4 open = BiomeSeaCoast.Sample(bake, reach + 2f * BiomeSeaCoast.TexelMetres, 13f);
			// Peat: redder than today's teal, far less green and blue, far murkier.
			Assert.That(inland.x, Is.GreaterThan(1.5f));
			Assert.That(inland.y, Is.LessThan(0.7f));
			Assert.That(inland.z, Is.LessThan(0.5f));
			Assert.That(inland.w, Is.GreaterThan(4f));
			Assert.That(shore.w, Is.GreaterThan(1.2f).And.LessThan(inland.w));
			Assert.That(open, Is.EqualTo(Vector4.one), "offshore: the planet's sea, exactly");

			float last = float.PositiveInfinity;
			for (float x = -400f; x <= reach + 80f; x += 7.3f)
			{
				float a = BiomeSeaCoast.Sample(bake, x, 13f).w;
				Assert.That(a, Is.LessThanOrEqualTo(last + 1e-4f), $"monotone at {x}");
				Assert.That(float.IsInfinity(last) ? 0f : last - a, Is.LessThan(0.6f), $"no step at {x}");
				last = a;
			}

			// Each texel is the underwater pass's blend at its centre, coded.
			Func<float, float, int> idAt = (x, z) => BiomeSeaCoast.MapID(w, h, ids, origin, size, x, z);
			for (int j = 0; j < bake.Height; j += 7)
			{
				for (int i = 0; i < bake.Width; i += 5)
				{
					float x = bake.Rect.x + (i + 0.5f) * bake.Rect.z / bake.Width, z = bake.Rect.y + (j + 0.5f) * bake.Rect.w / bake.Height;
					BiomeSeaLook coast = BiomeSeaLooks.Coast(x, z, idAt, LookOf, reach, BiomeSeaLooks.SpacingMetres, out float coverage);
					var expected = new byte[4];
					BiomeSeaCoast.Encode(coast, coverage, expected, 0);
					for (int c = 0; c < 4; c++)
					{
						Assert.That(bake.Texels[(j * bake.Width + i) * 4 + c], Is.EqualTo(expected[c]), $"texel ({i}, {j}) channel {c}");
					}
				}
			}

			// The rectangle's edge texels say nothing, so the clamped read past them is the planet's sea to the horizon.
			for (int i = 0; i < bake.Width; i++)
			{
				foreach (int j in new[] { 0, bake.Height - 1 })
				{
					for (int c = 0; c < 4; c++)
					{
						Assert.That(bake.Texels[(j * bake.Width + i) * 4 + c], Is.EqualTo(BiomeSeaCoast.Neutral));
					}
				}
			}
			for (int j = 0; j < bake.Height; j++)
			{
				foreach (int i in new[] { 0, bake.Width - 1 })
				{
					Assert.That(bake.Texels[(j * bake.Width + i) * 4 + 3], Is.EqualTo(BiomeSeaCoast.Neutral));
				}
			}
			Assert.That(BiomeSeaCoast.Sample(bake, -9000f, 13f), Is.EqualTo(Vector4.one), "past the bog's own edge, far out");
		}

		/// <summary>The same map bakes the same bytes, whatever order its looks are first asked in.</summary>
		[Test]
		public void Bake_IsDeterministic()
		{
			var (w, h, ids, origin, size) = BogCoast();
			BiomeSeaCoast.Bake a = BiomeSeaCoast.For(w, h, ids, origin, size, LookOf);
			var copy = (int[])ids.Clone();
			BiomeSeaCoast.Bake b = BiomeSeaCoast.For(w, h, ids, origin, size, id => LookOf(id));
			Assert.That(b.Texels, Is.EqualTo(a.Texels));
			Assert.That(b.Rect, Is.EqualTo(a.Rect));
			BiomeSeaCoast.Bake c = BiomeSeaCoast.For(w, h, copy, origin, size, LookOf);
			Assert.That(c.Texels, Is.EqualTo(a.Texels));
		}

		/// <summary>The coding: 128 is exactly 1, 32 steps a doubling, clamped; the shader's constants and the falls' floor agree.</summary>
		[Test]
		public void Coding_IsDoublings_AndMatchesTheShader()
		{
			Assert.That(BiomeSeaCoast.Code(1f), Is.EqualTo(BiomeSeaCoast.Neutral));
			Assert.That(BiomeSeaCoast.Ratio(BiomeSeaCoast.Neutral), Is.EqualTo(1f));
			Assert.That(BiomeSeaCoast.Code(2f), Is.EqualTo(160));
			Assert.That(BiomeSeaCoast.Code(0.5f), Is.EqualTo(96));
			Assert.That(BiomeSeaCoast.Code(1000f), Is.EqualTo(255));
			Assert.That(BiomeSeaCoast.Code(1e-6f), Is.EqualTo(0));
			for (float r = 0.07f; r < 15f; r *= 1.13f)
			{
				Assert.That(BiomeSeaCoast.Ratio(BiomeSeaCoast.Code(r)) / r, Is.InRange(0.98f, 1.02f), $"ratio {r}");
			}
			Assert.That(BiomeSeaCoast.RatioFloor, Is.EqualTo(InlandWaterRenderer.FallLookFloor));
			Assert.That(BiomeSeaCoast.Margin, Is.GreaterThan(BiomeSeaLooks.ReachMetres + 0.5f * BiomeSeaCoast.TexelMetres));

			string shading = CodeOnly(Read(WaterFolder + "/Shaders/FishWaterShading.hlsl"));
			StringAssert.Contains("* 255.0 - 128.0) / 32.0;", shading);
			Assert.That(BiomeSeaCoast.Neutral, Is.EqualTo(128));
			Assert.That(BiomeSeaCoast.StepsPerDoubling, Is.EqualTo(32f));
		}

		// ── The sea shader and its feed ───────────────────────────────

		/// <summary>
		/// One texture read, behind a uniform test that skips it in a scene with no look, through the caustics' sampler (no slot of
		/// its own), LOD 0; the climate's colours reach the shading only through it; the globals sit outside UnityPerMaterial.
		/// </summary>
		[Test]
		public void SeaShader_ReadsTheCoastOnce_AndNotAtAllWithoutOne()
		{
			string shading = CodeOnly(Read(WaterFolder + "/Shaders/FishWaterShading.hlsl"));
			Assert.That(Regex.Matches(shading, @"SAMPLE_TEXTURE2D\w*\(_FishSeaCoast\b").Count, Is.EqualTo(1));
			StringAssert.Contains("SAMPLE_TEXTURE2D_LOD(_FishSeaCoast, sampler_linear_repeat, uv, 0)", shading);
			int function = shading.IndexOf("void FishSeaCoastLook(", StringComparison.Ordinal);
			Assert.That(function, Is.GreaterThan(0));
			int skip = shading.IndexOf("if (_FishSeaCoastRect.z <= 0.0)", function, StringComparison.Ordinal);
			int read = shading.IndexOf("SAMPLE_TEXTURE2D_LOD(_FishSeaCoast", function, StringComparison.Ordinal);
			Assert.That(skip, Is.GreaterThan(function).And.LessThan(read), "the skip comes before the read");
			StringAssert.Contains("FishSeaCoastLook(flatXZ, shallowColor, deepColor, shoreColor, scatterColor, waterDensity);", shading);
			foreach (string uniform in new[] { "_ShallowColor", "_DeepColor", "_ShoreColor", "_ScatterColor", "_WaterDensity" })
			{
				Assert.That(Regex.Matches(shading, uniform + @"\b").Count, Is.EqualTo(1), $"{uniform} is read once, into its coast-tinted local");
			}
			StringAssert.Contains("SAMPLER(sampler_linear_repeat);", Read(WaterFolder + "/Shaders/FishWaterCausticsCommon.hlsl"));

			string input = CodeOnly(Read(WaterFolder + "/Shaders/FishWaterInput.hlsl"));
			int end = input.IndexOf("CBUFFER_END", StringComparison.Ordinal);
			foreach (string global in new[] { "TEXTURE2D(_FishSeaCoast);", "float4 _FishSeaCoastRect;", "float4 _FishSeaCoastTexel;" })
			{
				Assert.That(input.IndexOf(global, StringComparison.Ordinal), Is.GreaterThan(end), $"{global} is a global, after UnityPerMaterial");
			}
			// Both shaders that shade the sea compile with DXC.
			foreach (string shader in new[] { "FishWater.shader", "FishWaterBreaker.shader" })
			{
				string text = Read(WaterFolder + "/Shaders/" + shader);
				int passes = Regex.Matches(text, @"HLSLPROGRAM").Count;
				Assert.That(Regex.Matches(text, @"HLSLPROGRAM\s*\n\s*#pragma use_dxc vulkan").Count, Is.EqualTo(passes), shader);
			}
		}

		/// <summary>The surface publishes the bake every camera, the rectangle zero where the scene has none; the map bakes it with the grid.</summary>
		[Test]
		public void Bake_IsWrittenWithTheMap_AndPublishedForTheSea()
		{
			string surface = CodeOnly(Read(WaterFolder + "/Scripts/WaterSurface.cs"));
			int begin = surface.IndexOf("private void BeginCamera(Camera camera)", StringComparison.Ordinal);
			int lava = surface.IndexOf("BeginLava(camera);", begin, StringComparison.Ordinal);
			int publish = surface.IndexOf("PublishSeaCoast();", begin, StringComparison.Ordinal);
			Assert.That(publish, Is.GreaterThan(lava), "the sea's, not the lava's");
			StringAssert.Contains("Shader.SetGlobalVector(SeaCoastRectId, Vector4.zero);", surface);
			StringAssert.Contains("seaLook.Dispose();", surface);
			string look = CodeOnly(Read(WaterFolder + "/Scripts/WaterSeaLook.cs"));
			StringAssert.Contains("if (map == null || !map.HasCoastLook)", look);
			StringAssert.Contains("TextureFormat.RGBA32, false, true", look);

			string map = CodeOnly(Read(MapSource));
			int set = map.IndexOf("public void Set(int width, int height, int[] biomeIDs, Vector2 worldOrigin, Vector2 worldSize, float[] groundAltitudes, float verticalScale)", StringComparison.Ordinal);
			Assert.That(map.IndexOf("SetCoastLook(BiomeSeaCoast.Bake.None);", set, StringComparison.Ordinal), Is.GreaterThan(set), "a new grid drops the old grid's coast");
			string field = CodeOnly(Read(FieldSource));
			int write = field.IndexOf("map.Set(Width, Height, ids, Origin, Size, groundAltitudes, verticalScale);", StringComparison.Ordinal);
			Assert.That(field.IndexOf("map.BakeCoastLook(", write, StringComparison.Ordinal), Is.GreaterThan(write), "baked after the grid is written");
		}

		// ── Fields never over another site's ──────────────────────────

		/// <summary>A field off every claim keeps its size; one partly over another site's shrinks about its middle; one on top is left out.</summary>
		[Test]
		public void Claims_ShrinkOrLeaveOut_OnlyAnotherSitesOverlap()
		{
			var claims = new PointOfInterestFieldClaims();
			claims.Add(1, new Vector2(0f, 0f), 0f, new Vector2(40f, 30f));

			var size = new Vector2(50f, 30f);
			// Clear: 10 m between their edges (more than the gap).
			Assert.That(PointOfInterestFieldClaims.Fit(claims, 2, new Vector2(20f + 25f + 10f, 0f), 0f, size, out Vector2 fitted), Is.True);
			Assert.That(fitted, Is.EqualTo(size));
			// Within the gap (5 m): no longer clear.
			Assert.That(claims.Clear(2, new Vector2(20f + 25f + 5f, 0f), 0f, size), Is.False);
			Assert.That(claims.Clear(2, new Vector2(20f + 25f + PointOfInterestFieldClaims.Gap + 0.5f, 0f), 0f, size), Is.True);
			// The same site's own claims are its own business (its own siting keeps them apart).
			Assert.That(claims.Clear(1, new Vector2(5f, 0f), 0f, size), Is.True);

			// Partly over: shrunk to the first step that clears.
			Vector2 near = new Vector2(20f + 25f + 2f, 0f);
			Assert.That(PointOfInterestFieldClaims.Fit(claims, 2, near, 0f, size, out fitted), Is.True);
			Assert.That(fitted.x, Is.LessThan(size.x));
			Assert.That(fitted, Is.EqualTo(size * PointOfInterestFieldClaims.ShrinkSteps[0]).Or.EqualTo(size * PointOfInterestFieldClaims.ShrinkSteps[1]));
			Assert.That(claims.Clear(2, near, 0f, fitted), Is.True);

			// On top: left out, its size untouched.
			Assert.That(PointOfInterestFieldClaims.Fit(claims, 2, new Vector2(3f, 2f), 17f, size, out fitted), Is.False);
			Assert.That(fitted, Is.EqualTo(size));

			// Too small to shrink: left out rather than laid a garden plot.
			var small = new Vector2(18f, 13f);
			Assert.That(PointOfInterestFieldClaims.Fit(claims, 2, new Vector2(20f + 9f + 4f, 0f), 0f, small, out fitted), Is.False);

			// No claims (a harness): always laid as drawn.
			Assert.That(PointOfInterestFieldClaims.Fit(null, 2, Vector2.zero, 0f, size, out fitted), Is.True);
			Assert.That(fitted, Is.EqualTo(size));
		}

		/// <summary>Rotated fields are tested as the boxes they are; the answer is the same on every run.</summary>
		[Test]
		public void Claims_AreDeterministic_AndRotationAware()
		{
			var claims = new PointOfInterestFieldClaims();
			claims.Add(4, new Vector2(100f, 100f), 45f, new Vector2(60f, 10f));
			// Beside the long diagonal box, inside its axis-aligned bounds: clear (yaw turns clockwise: its length runs east-south).
			Assert.That(claims.Clear(5, new Vector2(100f + 15f, 100f + 15f), 45f, new Vector2(20f, 14f)), Is.True);
			// Along its length: not.
			Assert.That(claims.Clear(5, new Vector2(100f + 15f, 100f - 15f), 45f, new Vector2(20f, 14f)), Is.False);
			for (int run = 0; run < 3; run++)
			{
				Assert.That(PointOfInterestFieldClaims.Fit(claims, 5, new Vector2(118f, 112f), 10f, new Vector2(40f, 24f), out Vector2 fitted), Is.EqualTo(
					PointOfInterestFieldClaims.Fit(claims, 5, new Vector2(118f, 112f), 10f, new Vector2(40f, 24f), out Vector2 again)));
				Assert.That(fitted, Is.EqualTo(again));
			}
		}

		/// <summary>
		/// The wiring: one set of claims a placing, handed to every site; a site's fields tested after all were drawn (the draws as
		/// before), claimed once laid, and a field left out or shrunk still making its fence's draws so the fences after it keep theirs.
		/// </summary>
		[Test]
		public void Fields_ClaimTheirGround_AndKeepTheirDrawsInStep()
		{
			string stage = CodeOnly(Read(StageSource));
			int claims = stage.IndexOf("var fieldClaims = new PointOfInterestFieldClaims();", StringComparison.Ordinal);
			int loop = stage.IndexOf("foreach (PointOfInterestRecord record in asset.Points)", claims, StringComparison.Ordinal);
			Assert.That(claims, Is.GreaterThan(0));
			Assert.That(loop, Is.GreaterThan(claims), "one set for the whole placing, made before the first site");
			StringAssert.Contains("FieldClaims = fieldClaims,", stage);
			StringAssert.Contains("public PointOfInterestFieldClaims FieldClaims;", CodeOnly(Read(ContextSource)));

			string fields = CodeOnly(Read(FieldsSource));
			int drawn = fields.IndexOf("if (Fits(context, field, fields, ways))", StringComparison.Ordinal);
			int fit = fields.IndexOf("PointOfInterestFieldClaims.Fit(context.FieldClaims, record.Id, drawn.Centre, drawn.Yaw, drawn.Size, out Vector2 fitted)", StringComparison.Ordinal);
			int claim = fields.IndexOf("context.FieldClaims?.Add(record.Id, field.Centre, field.Yaw, field.Size);", StringComparison.Ordinal);
			int build = fields.IndexOf("List<Terrain> terrains = TerrainsOf(context);", StringComparison.Ordinal);
			Assert.That(drawn, Is.GreaterThan(0));
			Assert.That(fit, Is.GreaterThan(drawn), "tested after every field was drawn");
			Assert.That(claim, Is.GreaterThan(fit).And.LessThan(build));
			StringAssert.Contains("if (fitted != drawn.Size && !FitsGround(context, field, ways))", fields);
			Assert.That(Regex.Matches(fields, @"Fence\(context, drawn, site, abandoned, context\.Random, dry: true\);").Count, Is.EqualTo(2), "left out and shrunk");
			StringAssert.Contains("if (dry || context.IsWet(p.x, p.y, 0f))", fields);
			Assert.That(fields, Does.Not.Contain("DeterministicRNG random = context.Random;\n\t\t\tfor (int s = 0; s < 4; s++)"));
			// The crop is still drawn by the field's index, so a later field's crop does not change when an earlier one is left out.
			StringAssert.Contains("CropIndex(grown.ConvertAll(c => c.Weight), context.Record.SiteSeed, index)", fields);
		}

		// ── Helpers ───────────────────────────────────────────────────

		private static Color Colour(string body, string name)
		{
			Match m = Regex.Match(body, $@"{name}: \{{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}}]+)\}}");
			Assert.That(m.Success, Is.True, name);
			return new Color(F(m.Groups[1].Value), F(m.Groups[2].Value), F(m.Groups[3].Value), F(m.Groups[4].Value));
		}

		private static float Float(string body, string name)
		{
			Match m = Regex.Match(body, $@"{name}: ([^\s]+)");
			Assert.That(m.Success, Is.True, name);
			return F(m.Groups[1].Value);
		}

		private static float F(string text) => float.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

		private static string Read(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative)).Replace("\r\n", "\n");

		private static IEnumerable<string> BiomeAssetNames()
		{
			var names = new List<string>();
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (!Regex.IsMatch(text, "^  MinTemperature:", RegexOptions.Multiline))
				{
					continue;
				}
				Match m = Regex.Match(text, "^  m_Name: ?(.*)$", RegexOptions.Multiline);
				names.Add(m.Groups[1].Value.Trim().Trim('\r'));
			}
			names.Sort(StringComparer.Ordinal);
			return names;
		}

		/// <summary>The source with comment lines dropped, so a scan sees code and not prose.</summary>
		private static string CodeOnly(string source)
		{
			var code = new System.Text.StringBuilder(source.Length);
			foreach (string line in source.Replace("\r\n", "\n").Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}
				code.Append(line).Append('\n');
			}
			return code.ToString();
		}
	}
}
