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
	/// Decision 26 of the biome suite (2026-10-18, phase I2h): ridge and furrow carved into the terrain the same on every run
	/// (from the ground as it was, never on top of the last carve), waterfalls and their spray in the look of the water they
	/// pour from, and the sea under its surface in the look of the coast round the eye. Every default is exactly what was drawn
	/// before: today's water and sea looks for every biome, the falls' material colours, the scene's own sea.
	/// </summary>
	public class BiomeFieldCarveAndSeaLookTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string WaterFolder = "Assets/Plugins/FishMMO Water";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string FieldsSource = World + "/PointsOfInterest/Features/PointOfInterestFields.cs";
		private const string CarveSource = World + "/PointsOfInterest/Features/PointOfInterestFieldCarve.cs";
		private const string StageSource = World + "/PointsOfInterest/PointOfInterestStage.cs";
		private const string GeneratorSource = World + "/PointsOfInterest/PointOfInterestGenerator.cs";
		private const string RepaintSource = World + "/Terrain/BiomeRepaintTool.cs";
		private const string TemplateSource = "Assets/Scripts/Shared/Implementation/Tools/Biomes/BiomeTemplate.cs";
		private const string AuthoringSource = World + "/ProceduralArt/BiomeArtAuthoring.cs";

		// ── Ridge and furrow, carved ─────────────────────────────────

		private static readonly Vector2 FieldCentre = new Vector2(37.3f, -12.9f);
		private static readonly Vector2 FieldSize = new Vector2(60f, 30f);

		/// <summary>Only ridge and furrow is carved; flat fields and lazy beds raise nothing anywhere and plan no samples.</summary>
		[Test]
		public void OnlyRidgeAndFurrow_IsCarved()
		{
			Assert.That(BiomeFieldSurfaces.Carved(BiomeFieldSurface.RidgeAndFurrow), Is.True);
			Assert.That(BiomeFieldSurfaces.Carved(BiomeFieldSurface.LazyBed), Is.False, "lazy beds stay painted");
			Assert.That(BiomeFieldSurfaces.Carved(BiomeFieldSurface.Flat), Is.False);
			var samples = new List<int>();
			var metres = new List<float>();
			foreach (BiomeFieldSurface surface in new[] { BiomeFieldSurface.Flat, BiomeFieldSurface.LazyBed, (BiomeFieldSurface)99 })
			{
				for (float x = -40f; x <= 40f; x += 0.37f)
				{
					Assert.That(PointOfInterestFields.CarveRaise(FieldCentre, 17f, FieldSize, surface, FieldCentre.x + x, FieldCentre.y + 0.3f * x, 1.95f), Is.EqualTo(0f), $"{surface}");
				}
				Assert.That(PointOfInterestFieldCarve.Plan(FieldCentre, 17f, FieldSize, surface, new Vector2(-100f, -100f), new Vector2(200f, 200f), 101, samples, metres), Is.EqualTo(0));
				Assert.That(BiomeFieldSurfaces.CarvedHeight(surface, 27f, 3.3f, 1.95f), Is.EqualTo(0f));
			}
		}

		/// <summary>
		/// The carve stands as the profile says, in the frame the crop is sown in: crowns at the ridge's height, furrows, the
		/// band's edges, the headland and the fence line at the ground's own level, each ridge dying into the headland at its
		/// ends; filtered over a heightmap sample, crowns stay high and furrows low; never above the crown, never below the ground.
		/// </summary>
		[Test]
		public void Carve_StandsAsTheProfileSays()
		{
			const BiomeFieldSurface rf = BiomeFieldSurface.RidgeAndFurrow;
			float height = BiomeFieldSurfaces.Nominal(rf).Height;
			// Ridges run along x (the long side); across is z. Band = 30 − 2 × 1.5 headland = 27 m: 4 ridges of 6.75 m.
			float band = PointOfInterestFields.RidgeBand(FieldSize, 0f, 0f, out _);
			Assert.That(band, Is.EqualTo(27f).Within(1e-5f));
			float period = BiomeFieldSurfaces.Period(rf, band);
			Assert.That(BiomeFieldSurfaces.RidgeCount(rf, band), Is.EqualTo(4));
			float Raise(float u, float v, float footprint) => PointOfInterestFields.CarveRaise(FieldCentre, 0f, FieldSize, rf, FieldCentre.x + u, FieldCentre.y + v, footprint);
			float V(float across) => across - 0.5f * band;
			for (int k = 0; k < 4; k++)
			{
				float crown = V((k + 0.5f) * period);
				Assert.That(Raise(0f, crown, 0f), Is.EqualTo(height).Within(1e-5f), $"crown {k}");
				Assert.That(Raise(0f, crown, 1.95f), Is.GreaterThan(0.9f * height), $"crown {k}, filtered over a sample");
				Assert.That(Raise(0f, V(k * period), 0f), Is.EqualTo(0f), $"furrow {k}");
				Assert.That(Raise(0f, V(k * period), 1.95f), Is.LessThan(0.12f * height), $"furrow {k}, filtered");
			}
			Assert.That(Raise(0f, V(band), 0f), Is.EqualTo(0f), "the band's far edge");
			Assert.That(Raise(0f, 14.2f, 1.95f), Is.EqualTo(0f), "the headland");
			Assert.That(Raise(0f, 15.0f, 1.95f), Is.EqualTo(0f), "the fence line");
			Assert.That(Raise(0f, 15.5f, 1.95f), Is.EqualTo(0f), "past the field");
			// Along a crown: full down the middle, nothing at the band's end, smoothly rising over its last 3 m.
			float crownV = V(1.5f * period);
			float end = 0.5f * FieldSize.x - 1.5f;
			Assert.That(Raise(end - 0.001f, crownV, 0f), Is.LessThan(1e-3f));
			Assert.That(Raise(end - 1.5f, crownV, 0f), Is.EqualTo(0.5f * height).Within(1e-4f), "half way up its end");
			Assert.That(Raise(end - 3f, crownV, 0f), Is.EqualTo(height).Within(1e-5f));
			Assert.That(Raise(-(end - 3f), crownV, 0f), Is.EqualTo(height).Within(1e-5f), "both ends");
			Assert.That(Raise(end + 0.5f, crownV, 0f), Is.EqualTo(0f), "the headland at the end");
			float last = 0f;
			for (float u = end; u >= end - 3f; u -= 0.25f)
			{
				float r = Raise(u, crownV, 0f);
				Assert.That(r, Is.GreaterThanOrEqualTo(last - 1e-6f), "rises steadily in from its end");
				last = r;
			}
			for (float u = -31f; u <= 31f; u += 0.71f)
			{
				for (float v = -16f; v <= 16f; v += 0.29f)
				{
					float r = Raise(u, v, 1.95f);
					Assert.That(r, Is.InRange(0f, height + 1e-6f), $"({u}, {v})");
				}
			}
		}

		/// <summary>
		/// The plan depends on nothing but the field and the grid: the same twice over, and the column two tiles share is raised
		/// alike from either (so the seam stays stitched); a field laid at a slant is planned on both tiles it crosses.
		/// </summary>
		[Test]
		public void Plan_IsDeterministic_AndTilesAgreeAtTheirSeam()
		{
			const BiomeFieldSurface rf = BiomeFieldSurface.RidgeAndFurrow;
			var centre = new Vector2(100.4f, 51.7f);
			const float yaw = 23f;
			const int res = 51; // 2 m samples over 100 m tiles
			var size = new Vector2(100f, 100f);
			var sa = new List<int>();
			var ma = new List<float>();
			var sb = new List<int>();
			var mb = new List<float>();
			int a = PointOfInterestFieldCarve.Plan(centre, yaw, FieldSize, rf, new Vector2(0f, 0f), size, res, sa, ma);
			int b = PointOfInterestFieldCarve.Plan(centre, yaw, FieldSize, rf, new Vector2(100f, 0f), size, res, sb, mb);
			Assert.That(a, Is.GreaterThan(50));
			Assert.That(b, Is.GreaterThan(50));
			var again = new List<int>();
			var againMetres = new List<float>();
			PointOfInterestFieldCarve.Plan(centre, yaw, FieldSize, rf, new Vector2(0f, 0f), size, res, again, againMetres);
			Assert.That(again, Is.EqualTo(sa));
			Assert.That(againMetres, Is.EqualTo(ma));
			var left = new Dictionary<int, float>();
			for (int k = 0; k < sa.Count; k++)
			{
				if (sa[k] % res == res - 1)
				{
					left[sa[k] / res] = ma[k];
				}
			}
			int shared = 0;
			for (int k = 0; k < sb.Count; k++)
			{
				if (sb[k] % res == 0)
				{
					Assert.That(left.TryGetValue(sb[k] / res, out float l), Is.True, $"row {sb[k] / res} raised on one side only");
					Assert.That(mb[k], Is.EqualTo(l), $"row {sb[k] / res}");
					shared++;
				}
			}
			Assert.That(shared, Is.EqualTo(left.Count));
			Assert.That(shared, Is.GreaterThan(3));
		}

		/// <summary>
		/// The carve is the same on every run: carved into ground that is stored to 16 bits (as a terrain's is), taken out by what
		/// the ground took, the ground is back bit for bit; carved again from there, it is the first carve bit for bit. Carved on
		/// top of itself instead (no restore), the ridges would pile up: the restore is what makes it idempotent.
		/// </summary>
		[Test]
		public void CarveRestoreCarve_IsBitForBit_TheFirstCarve()
		{
			const BiomeFieldSurface rf = BiomeFieldSurface.RidgeAndFurrow;
			const int res = 129;
			var tileSize = new Vector2(250f, 250f);
			const float heightScale = 600f;
			foreach (float quantum in new[] { 65535f, 32766f })
			{
				float Q(float h) => Mathf.Round(Mathf.Clamp01(h) * quantum) / quantum;
				var ground = new float[res, res];
				for (int j = 0; j < res; j++)
				{
					for (int i = 0; i < res; i++)
					{
						ground[j, i] = Q(0.31f + 0.004f * Mathf.Sin(i * 0.21f) * Mathf.Cos(j * 0.17f) + 0.00003f * i);
					}
				}
				var samples = new List<int>();
				var metres = new List<float>();
				Assert.That(PointOfInterestFieldCarve.Plan(new Vector2(121f, 133f), 31f, FieldSize, rf, Vector2.zero, tileSize, res, samples, metres), Is.GreaterThan(100));
				PointOfInterestFieldCarve.Bounds(samples, res, out int x0, out int z0, out int w, out int d);
				float[,] Block(float[,] all)
				{
					var block = new float[d, w];
					for (int j = 0; j < d; j++)
					{
						for (int i = 0; i < w; i++)
						{
							block[j, i] = all[z0 + j, x0 + i];
						}
					}
					return block;
				}
				float[,] Stored(float[,] block)
				{
					var stored = (float[,])block.Clone();
					for (int j = 0; j < d; j++)
					{
						for (int i = 0; i < w; i++)
						{
							stored[j, i] = Q(stored[j, i]);
						}
					}
					return stored;
				}
				float[,] before = Block(ground);
				float[,] carved = (float[,])before.Clone();
				PointOfInterestFieldCarve.Raise(carved, x0, z0, res, samples, metres, heightScale);
				float[,] after = Stored(carved);
				List<float> deltas = PointOfInterestFieldCarve.Deltas(before, after, x0, z0, res, samples);
				Assert.That(deltas.Exists(x => x > 0f), Is.True);

				float[,] restored = (float[,])after.Clone();
				PointOfInterestFieldCarve.Lower(restored, x0, z0, res, samples, deltas);
				restored = Stored(restored);
				Assert.That(restored, Is.EqualTo(before), $"restored bit for bit ({quantum})");

				float[,] again = (float[,])restored.Clone();
				PointOfInterestFieldCarve.Raise(again, x0, z0, res, samples, metres, heightScale);
				again = Stored(again);
				Assert.That(again, Is.EqualTo(after), $"carved again bit for bit ({quantum})");
				Assert.That(PointOfInterestFieldCarve.Deltas(restored, again, x0, z0, res, samples), Is.EqualTo(deltas));

				// Control: carved on top of itself the field rises by its ridges again.
				float[,] piled = (float[,])after.Clone();
				PointOfInterestFieldCarve.Raise(piled, x0, z0, res, samples, metres, heightScale);
				piled = Stored(piled);
				Assert.That(piled, Is.Not.EqualTo(after), "without the restore, carving again piles ridge on ridge");
			}
		}

		/// <summary>
		/// The carve is wired as its promise needs: the fields carve only a carved surface, before their crop, furrows and fence;
		/// the repaint takes the ridges out before the talus cones and before anything paints, Regenerate POIs before it plans,
		/// the place stage before it replaces the POI root; and the record never reaches a build.
		/// </summary>
		[Test]
		public void Carve_IsTakenOutBeforeAnythingReadsTheGround()
		{
			string fields = CodeOnly(Read(FieldsSource));
			int carve = fields.IndexOf("PointOfInterestFieldCarve.Carve(context, field.Centre, field.Yaw, field.Size, surface)", StringComparison.Ordinal);
			Assert.That(carve, Is.GreaterThan(0));
			int guard = fields.LastIndexOf("if (BiomeFieldSurfaces.Carved(surface))", carve, StringComparison.Ordinal);
			Assert.That(guard, Is.GreaterThan(0).And.LessThan(carve));
			Assert.That(fields.IndexOf("PaintDetails(terrains, field, crop, biome, surface);", carve, StringComparison.Ordinal), Is.GreaterThan(carve), "the crop after the carve");
			Assert.That(fields.IndexOf("AddFurrows(context, field, surface);", carve, StringComparison.Ordinal), Is.GreaterThan(carve));
			// The fence's draws come from the site's sequence, or a shrunk field's own (2026-10-18, I2k): after the carve either way.
			Assert.That(fields.IndexOf("Fence(context, field, site, abandoned, ", carve, StringComparison.Ordinal), Is.GreaterThan(carve));

			string repaint = CodeOnly(Read(RepaintSource));
			int restore = repaint.IndexOf("PointOfInterestFieldCarve.Restore(scene, terrains, result.Notes);", StringComparison.Ordinal);
			Assert.That(restore, Is.GreaterThan(0));
			Assert.That(repaint.IndexOf("CliffPlacer.Clear(scene);", StringComparison.Ordinal), Is.GreaterThan(restore));
			Assert.That(repaint.IndexOf("SceneGenerator.LoadWater(", StringComparison.Ordinal), Is.GreaterThan(restore));
			Assert.That(repaint.IndexOf("SceneGenerator.PaintBiomes(", StringComparison.Ordinal), Is.GreaterThan(restore));

			string generator = CodeOnly(Read(GeneratorSource));
			int regenerate = generator.IndexOf("PointOfInterestFieldCarve.Restore(scene, terrains, notes);", StringComparison.Ordinal);
			Assert.That(regenerate, Is.GreaterThan(0));
			Assert.That(generator.IndexOf("stage.PlanSites(", StringComparison.Ordinal), Is.GreaterThan(regenerate));
			Assert.That(generator.IndexOf("SceneBiomeField.Build(", StringComparison.Ordinal), Is.GreaterThan(regenerate));

			string stage = CodeOnly(Read(StageSource));
			int place = stage.IndexOf("PointOfInterestFieldCarve.Restore(scene, terrains, notes);", StringComparison.Ordinal);
			Assert.That(place, Is.GreaterThan(0));
			Assert.That(stage.IndexOf("GameObject root = EnsureRoot(scene, asset);", StringComparison.Ordinal), Is.GreaterThan(place));

			string carver = CodeOnly(Read(CarveSource));
			StringAssert.Contains("new GameObject(GeneratedFieldCarve.ObjectName) { tag = \"EditorOnly\" }", carver);
			StringAssert.Contains("Deltas(before, after, x0, z0, res, samples)", carver);
			StringAssert.Contains("float[,] after = data.GetHeights(x0, z0, w, d);", carver);
		}

		// ── Falls in the look of the water they pour from ────────────

		private const string FallMaterial = WaterFolder + "/Materials/Waterfall.mat";
		private const string SprayMaterial = WaterFolder + "/Materials/WaterfallSpray.mat";

		/// <summary>Water of the inland water's own look gives every fall exactly its materials' colours and stains nothing more.</summary>
		[Test]
		public void FallLook_OfTheInlandWatersOwnLook_IsTheMaterialsColours()
		{
			string fall = Read(FallMaterial), spray = Read(SprayMaterial);
			Color water = MaterialColour(fall, "_WaterColor"), foam = MaterialColour(fall, "_FoamColor");
			Color drops = MaterialColour(spray, "_SprayColor"), mist = MaterialColour(spray, "_MistColor");
			InlandWaterRenderer.FallLook look = InlandWaterRenderer.FallLookOf(BiomeWaterLooks.Today, BiomeWaterLooks.Today, water, foam, drops, mist);
			Assert.That(look.Water, Is.EqualTo(water));
			Assert.That(look.Foam, Is.EqualTo(foam));
			Assert.That(look.Spray, Is.EqualTo(drops));
			Assert.That(look.Mist, Is.EqualTo(mist));
			Assert.That(look.Absorb, Is.EqualTo(Vector3.zero));
			// Each look is also its own neutral reference, including the sulphur melt override.
			foreach (string name in BiomeAssetNames())
			{
				BiomeWaterLook own = BiomeArtSpec.For(name).WaterLook();
				InlandWaterRenderer.FallLook each = InlandWaterRenderer.FallLookOf(own, own, water, foam, drops, mist);
				Assert.That(each.Water, Is.EqualTo(water), name);
				Assert.That(each.Foam, Is.EqualTo(foam), name);
				Assert.That(each.Absorb, Is.EqualTo(Vector3.zero), name);
			}
		}

		/// <summary>
		/// A peat-stained water gives its fall a darker, browner body, a cream foam and a sheet that takes more of the light through
		/// it, blue most; the mist shifts half as far as the spray; and the colours stay colours.
		/// </summary>
		[Test]
		public void FallLook_FollowsAStainedWater()
		{
			string fall = Read(FallMaterial), spray = Read(SprayMaterial);
			Color water = MaterialColour(fall, "_WaterColor"), foam = MaterialColour(fall, "_FoamColor");
			Color drops = MaterialColour(spray, "_SprayColor"), mist = MaterialColour(spray, "_MistColor");
			var peat = new BiomeWaterLook(new Color(0.35f, 0.2f, 0.08f, 1f), new Color(0.06f, 0.03f, 0.01f, 1f), new Vector3(0.9f, 1.8f, 3.2f), 0.2f,
				new Color(0.25f, 0.15f, 0.07f, 1f), new Color(0.85f, 0.78f, 0.62f, 1f));
			InlandWaterRenderer.FallLook look = InlandWaterRenderer.FallLookOf(peat, BiomeWaterLooks.Today, water, foam, drops, mist);
			Assert.That(look.Water.r / look.Water.b, Is.GreaterThan(water.r / water.b * 2f), "browner over the brink");
			Assert.That(look.Water.g + look.Water.b, Is.LessThan(water.g + water.b), "darker in green and blue");
			Assert.That(look.Foam.b, Is.LessThan(foam.b), "cream foam");
			Assert.That(look.Absorb.z, Is.GreaterThan(look.Absorb.x).And.GreaterThan(0f), "takes the blue first");
			float sprayShift = drops.b - look.Spray.b, mistShift = mist.b - look.Mist.b;
			Assert.That(mistShift, Is.GreaterThan(0f).And.LessThan(sprayShift), "the mist shifts less than the spray");
			foreach (Color c in new[] { look.Water, look.Foam, look.Spray, look.Mist })
			{
				Assert.That(c.r, Is.InRange(0f, 1f));
				Assert.That(c.g, Is.InRange(0f, 1f));
				Assert.That(c.b, Is.InRange(0f, 1f));
			}
		}

		/// <summary>The shaders read what the renderer sets, by the same names, behind <c>_FallLookOn</c>; their passes still compile with DXC.</summary>
		[Test]
		public void FallShaders_ReadWhatTheRendererSets()
		{
			string looks = CodeOnly(Read(WaterFolder + "/Scripts/InlandWaterRenderer.Looks.cs"));
			string hlsl = Read(WaterFolder + "/Shaders/FishWaterfall.hlsl");
			string curtain = Read(WaterFolder + "/Shaders/FishWaterfall.shader");
			string sprayShader = Read(WaterFolder + "/Shaders/FishWaterfallSpray.shader");
			foreach (string name in new[] { "_FallLookOn", "_FallWater", "_FallFoam", "_FallAbsorb", "_FallSpray", "_FallMist" })
			{
				StringAssert.Contains($"Shader.PropertyToID(\"{name}\")", looks, name);
			}
			foreach (string name in new[] { "float _FallLookOn;", "float4 _FallWater;", "float4 _FallFoam;", "float4 _FallAbsorb;" })
			{
				StringAssert.Contains(name, hlsl);
			}
			foreach (string name in new[] { "float _FallLookOn;", "float4 _FallSpray;", "float4 _FallMist;" })
			{
				StringAssert.Contains(name, sprayShader);
			}
			StringAssert.Contains("half3 waterColor = FallWaterColor();", curtain);
			StringAssert.Contains("FallFoamColor() *", curtain);
			StringAssert.Contains("behind *= FallStain(input.sheetDepth.x);", curtain);
			Assert.That(curtain, Does.Not.Contain("_WaterColor.rgb *"), "every read of the water colour goes through FallWaterColor");
			Assert.That(curtain, Does.Not.Contain("_FoamColor.rgb *"));
			Assert.That(Regex.Matches(curtain, @"HLSLPROGRAM\s*\n\s*#pragma use_dxc vulkan").Count, Is.EqualTo(2));
			Assert.That(Regex.Matches(sprayShader, @"HLSLPROGRAM\s*\n\s*#pragma use_dxc vulkan").Count, Is.EqualTo(1));

			string falls = CodeOnly(Read(WaterFolder + "/Scripts/InlandWaterRenderer.Falls.cs"));
			StringAssert.Contains("fallLookParts.Add((sheet, block, fall.River, fall.LipPoint, false));", falls);
			StringAssert.Contains("fallLookParts.Add((sprayRenderer, block, fall.River, fall.LipPoint, true));", falls);
			StringAssert.Contains("fallLookParts.Clear();", falls);
			StringAssert.Contains("ApplyFallLooks();", CodeOnly(Read(WaterFolder + "/Scripts/InlandWaterRenderer.Flow.cs")));
		}

		// ── The sea off each coast ───────────────────────────────────

		/// <summary>Today's sea look is the underwater pass's as every scene had it: WaterSurface's tint and visibility, the pass's murk.</summary>
		[Test]
		public void SeaToday_IsTheUnderwaterPasssOwn()
		{
			string surface = Read(WaterFolder + "/Scripts/WaterSurface.cs");
			Match tint = Regex.Match(surface, @"public Color UnderwaterTint = new Color\(([^,]+)f, ([^,]+)f, ([^)]+)f\);");
			Assert.That(tint.Success, Is.True);
			BiomeSeaLook today = BiomeSeaLooks.Today;
			Assert.That(today.Tint.r, Is.EqualTo(F(tint.Groups[1].Value)));
			Assert.That(today.Tint.g, Is.EqualTo(F(tint.Groups[2].Value)));
			Assert.That(today.Tint.b, Is.EqualTo(F(tint.Groups[3].Value)));
			Match visibility = Regex.Match(surface, @"public float UnderwaterVisibility = ([0-9.]+)f;");
			Assert.That(visibility.Success, Is.True);
			Assert.That(today.Visibility, Is.EqualTo(F(visibility.Groups[1].Value)));
			Match murk = Regex.Match(Read(WaterFolder + "/Shaders/FishWaterVolume.shader"), @"_UnderwaterDensity \(""Murk"", Range\(0, 3\)\) = ([0-9.]+)");
			Assert.That(murk.Success, Is.True);
			Assert.That(today.Murk, Is.EqualTo(F(murk.Groups[1].Value)));
			Assert.That(today.Defined, Is.True);
			Assert.That(default(BiomeSeaLook).Defined, Is.False);
			Assert.That(BiomeSeaLooks.OwnLook(today), Is.False, "today's look says nothing of its own");
		}

		/// <summary>
		/// Every biome's defaults are today's, all 83: the template's field starts as today's sea look, the spec gives today's sea
		/// and water looks, the authoring writes and marks the sea look, and any look an asset already holds is today's.
		/// </summary>
		[Test]
		public void SeaDefaults_AreTodays_AndWaterMatchesApprovedOverrides()
		{
			string template = CodeOnly(Read(TemplateSource));
			StringAssert.Contains("private BiomeSeaLook seaLook = BiomeSeaLooks.Today;", template);
			StringAssert.Contains("public const string SeaLook = nameof(seaLook);", template);
			string authoring = CodeOnly(Read(AuthoringSource));
			StringAssert.Contains("AuthorSeaLook(biome, entry, report, apply);", authoring);
			StringAssert.Contains("BiomeTemplate.Authored.SeaLook", authoring);

			int count = 0;
			foreach (string name in BiomeAssetNames())
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(name);
				Assert.That(entry, Is.Not.Null, name);
				Assert.That(entry.SeaLook().Signature(), Is.EqualTo(BiomeSeaLooks.Today.Signature()), name);
				// A biome file may mark its water hot (phase I2q, .HotWater()); the colours stay today's.
				BiomeWaterLook water = entry.WaterLook();
				Assert.That(water.Signature(), Is.EqualTo(BiomeWaterLookAndFieldSurfaceTests.ExpectedAuthoredWater(name).Signature()), name);
				count++;
			}
			Assert.That(count, Is.EqualTo(83));

			int holding = 0;
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				Match block = Regex.Match(Read(path), @"^  seaLook:\n((?:    .*\n)+)", RegexOptions.Multiline);
				if (!block.Success)
				{
					continue;
				}
				holding++;
				string body = block.Groups[1].Value;
				var look = new BiomeSeaLook(Colour(body, "Tint"), Float(body, "Visibility"), Float(body, "Murk"));
				Assert.That(look.Approximately(BiomeSeaLooks.Today, 1e-5f), Is.True, $"{Path.GetFileName(path)} holds {look}, not today's");
			}
			Console.WriteLine($"{holding} biome asset(s) hold a sea look; the rest read the field's default.");
		}

		/// <summary>The weight round the eye: whole at the eye, smoothly to nothing at the reach, nothing past it.</summary>
		[Test]
		public void Weight_FallsSmoothlyToTheReach()
		{
			Assert.That(BiomeSeaLooks.Weight(0f, 160f), Is.EqualTo(1f));
			Assert.That(BiomeSeaLooks.Weight(160f, 160f), Is.EqualTo(0f));
			Assert.That(BiomeSeaLooks.Weight(400f, 160f), Is.EqualTo(0f));
			Assert.That(BiomeSeaLooks.Weight(10f, 0f), Is.EqualTo(0f));
			float last = 1f;
			for (float d = 0f; d <= 160f; d += 1f)
			{
				float w = BiomeSeaLooks.Weight(d, 160f);
				Assert.That(w, Is.LessThanOrEqualTo(last));
				Assert.That(last - w, Is.LessThan(0.02f), "no step");
				last = w;
			}
			Assert.That(BiomeSeaLooks.Weight(159f, 160f), Is.LessThan(1e-3f), "reaches nothing at the edge");
		}

		/// <summary>
		/// Along a straight coast (a bog to the west with a peat-brown sea look, the sea's own biome to the east with today's):
		/// on the shore half the water is the bog's, out past the reach none, falling steadily and without a step as the eye swims
		/// out; where nothing reaches the sea is the scene's own exactly, even a hand-tuned one; a coast of today's looks reaches
		/// nothing; and the blend is the same answer every time.
		/// </summary>
		[Test]
		public void Coast_BlendsAlongTheShore_AndLeavesOpenWaterTheScenes()
		{
			var peat = new BiomeSeaLook(new Color(0.18f, 0.12f, 0.05f, 1f), 6f, 1.6f);
			const int Bog = 7, Sea = 9;
			Func<float, float, int> idAt = (x, z) => x < 0f ? Bog : Sea;
			Func<int, BiomeSeaLook?> lookOf = id => id == Bog ? peat : id == Sea ? BiomeSeaLooks.Today : (BiomeSeaLook?)null;
			float reach = BiomeSeaLooks.ReachMetres, spacing = BiomeSeaLooks.SpacingMetres;

			BiomeSeaLooks.Coast(-500f, 3f, idAt, lookOf, reach, spacing, out float inland);
			Assert.That(inland, Is.EqualTo(1f).Within(1e-6f));
			BiomeSeaLook atShore = BiomeSeaLooks.Coast(0f, 3f, idAt, lookOf, reach, spacing, out float shore);
			Assert.That(shore, Is.EqualTo(0.5f).Within(0.08f), "half the water on the shoreline");
			Assert.That(atShore.Tint.r, Is.EqualTo(peat.Tint.r).Within(1e-5f), "what reaches is the bog's own look");
			Assert.That(atShore.Extinction, Is.EqualTo(peat.Extinction).Within(1e-5f));
			BiomeSeaLook open = BiomeSeaLooks.Coast(reach + 1f, 3f, idAt, lookOf, reach, spacing, out float offshore);
			Assert.That(offshore, Is.EqualTo(0f));
			Assert.That(open.Signature(), Is.EqualTo(BiomeSeaLooks.Today.Signature()));

			float last = 1f;
			for (float x = -reach; x <= reach + 5f; x += 0.5f)
			{
				BiomeSeaLooks.Coast(x, 11.3f, idAt, lookOf, reach, spacing, out float coverage);
				Assert.That(coverage, Is.LessThanOrEqualTo(last + 1e-5f), $"x {x}: falls steadily out to sea");
				Assert.That(last - coverage, Is.LessThan(0.03f), $"x {x}: no step as the eye moves");
				last = coverage;
			}

			var tuned = new BiomeSeaLook(new Color(0.02f, 0.2f, 0.3f, 1f), 35f, 0.8f);
			Assert.That(BiomeSeaLooks.Mix(tuned, open, offshore).Signature(), Is.EqualTo(tuned.Signature()), "open water: the scene's own, exactly");
			Assert.That(BiomeSeaLooks.Mix(BiomeSeaLooks.Today, open, 0f).Signature(), Is.EqualTo(BiomeSeaLooks.Today.Signature()));
			BiomeSeaLook mixed = BiomeSeaLooks.Mix(tuned, atShore, shore);
			Assert.That(mixed.Visibility, Is.EqualTo(tuned.Visibility), "the scene's visibility, the murk carrying the blend");
			Assert.That(mixed.Extinction, Is.EqualTo(Mathf.Lerp(tuned.Extinction, peat.Extinction, shore)).Within(1e-5f));
			Assert.That(mixed.Tint.r, Is.EqualTo(Mathf.Lerp(tuned.Tint.r, peat.Tint.r, shore)).Within(1e-5f));
			BiomeSeaLook whole = BiomeSeaLooks.Mix(tuned, peat, 1f);
			Assert.That(whole.Tint.r, Is.EqualTo(peat.Tint.r).Within(1e-6f));
			Assert.That(whole.Extinction, Is.EqualTo(peat.Extinction).Within(1e-5f));

			// A coast whose every biome has today's look reaches nothing anywhere.
			Func<int, BiomeSeaLook?> allToday = id => BiomeSeaLooks.Today;
			for (float x = -300f; x <= 300f; x += 13f)
			{
				BiomeSeaLooks.Coast(x, 0f, idAt, allToday, reach, spacing, out float none);
				Assert.That(none, Is.EqualTo(0f), $"x {x}");
			}
			// The same answer every time, at any point.
			BiomeSeaLook a = BiomeSeaLooks.Coast(-37.7f, 81.1f, idAt, lookOf, reach, spacing, out float ca);
			BiomeSeaLook b = BiomeSeaLooks.Coast(-37.7f, 81.1f, idAt, lookOf, reach, spacing, out float cb);
			Assert.That(a.Signature(), Is.EqualTo(b.Signature()));
			Assert.That(ca, Is.EqualTo(cb));
		}

		/// <summary>The underwater pass takes the coast's look only where one reaches, and is set exactly as before everywhere else.</summary>
		[Test]
		public void UnderwaterPass_FallsBackToTheScenesOwnSettings()
		{
			string surface = CodeOnly(Read(WaterFolder + "/Scripts/WaterSurface.cs"));
			int ask = surface.IndexOf("if (seaLook.TryAt(gameObject.scene, camera.transform.position,", StringComparison.Ordinal);
			Assert.That(ask, Is.GreaterThan(0));
			int otherwise = surface.IndexOf("else", ask, StringComparison.Ordinal);
			Assert.That(surface.IndexOf("underwaterMaterial.SetColor(UnderwaterTintId, UnderwaterTint);", otherwise, StringComparison.Ordinal), Is.GreaterThan(otherwise));
			Assert.That(surface.IndexOf("underwaterMaterial.SetFloat(UnderwaterDepthId, UnderwaterVisibility);", otherwise, StringComparison.Ordinal), Is.GreaterThan(otherwise));
			StringAssert.Contains("underwaterMurk = underwaterMaterial.GetFloat(UnderwaterDensityId);", surface);
			string field = CodeOnly(Read(WaterFolder + "/Scripts/WaterSeaLook.cs"));
			StringAssert.Contains("BiomeSeaLooks.Coast(at.x, at.y, IdAt, LookOf, BiomeSeaLooks.ReachMetres, BiomeSeaLooks.SpacingMetres, out lastCoverage)", field);
			StringAssert.Contains("if (!anyOwn)", field);
		}

		// ── Helpers ───────────────────────────────────────────────────

		private static Color MaterialColour(string mat, string name)
		{
			Match m = Regex.Match(mat, $@"- {name}: \{{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}}]+)\}}");
			Assert.That(m.Success, Is.True, $"{name} in the material");
			return new Color(F(m.Groups[1].Value), F(m.Groups[2].Value), F(m.Groups[3].Value), F(m.Groups[4].Value));
		}

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
