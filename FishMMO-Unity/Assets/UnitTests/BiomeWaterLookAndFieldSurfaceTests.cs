using System;
using System.Collections.Generic;
using System.Globalization;
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
	/// Decision 21 of the biome suite (2026-10-16, phase I2e): each biome's fresh water has its own colour and clarity (the
	/// template's Water Look, read by the inland water for its lakes, pools, rivers, streams and moats), and each crop its
	/// field surface (flat, lazy beds, ridge and furrow). Every biome's defaults are exactly what every lake and every field
	/// had before (the inland water material's look, flat fields), so authoring changes no scene; a body of water takes the
	/// look of the land round its shores; a river runs into the look of the water it meets, so no seam opens where they join;
	/// and the ridges lie as their profile says.
	/// </summary>
	public class BiomeWaterLookAndFieldSurfaceTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string WaterFolder = "Assets/Plugins/FishMMO Water";
		private const string InlandMaterial = WaterFolder + "/Materials/InlandWater.mat";
		private const string InlandShader = WaterFolder + "/Shaders/FishInlandWater.shader";
		private const string InlandHlsl = WaterFolder + "/Shaders/FishInlandWater.hlsl";
		private const string InlandRenderer = WaterFolder + "/Scripts/InlandWaterRenderer.cs";
		private const string InlandLooks = WaterFolder + "/Scripts/InlandWaterRenderer.Looks.cs";
		private const string InlandFlow = WaterFolder + "/Scripts/InlandWaterRenderer.Flow.cs";
		private const string TemplateSource = "Assets/Scripts/Shared/Implementation/Tools/Biomes/BiomeTemplate.cs";
		private const string AuthoringSource = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/ProceduralArt/BiomeArtAuthoring.cs";
		private const string FieldsSource = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/Features/PointOfInterestFields.cs";

		// Approved per-biome water overrides. Keep this independent of the spec builder so the
		// checks catch an unintended edit to either the recipe or the authored template.
		public static BiomeWaterLook ExpectedAuthoredWater(string name)
		{
			if (name == "Sulphur Flats")
				return new BiomeWaterLook(new Color(0.941f, 0.753f, 0.251f, 1f), new Color(0.627f, 0.251f, 0.055f, 1f),
					new Vector3(0.9f, 0.5f, 0.1f), 1f, new Color(0.784f, 0.541f, 0.188f, 1f), new Color(0.784f, 0.541f, 0.188f, 1f)) { Hot = true, Melt = true };
			BiomeWaterLook look = BiomeWaterLooks.Today;
			look.Hot = name == "Geyser Basin";
			return look;
		}

		// ── Defaults: today's look, today's fields ───────────────────

		/// <summary>The look every biome is authored with is the inland water material's, value for value (its stored colours to 1e-6).</summary>
		[Test]
		public void TodaysLook_IsTheInlandWaterMaterials()
		{
			string mat = Read(InlandMaterial);
			BiomeWaterLook today = BiomeWaterLooks.Today;
			AssertColour(MaterialColour(mat, "_ShallowColor"), today.Shallow, "_ShallowColor");
			AssertColour(MaterialColour(mat, "_DeepColor"), today.Deep, "_DeepColor");
			AssertColour(MaterialColour(mat, "_SiltColor"), today.Silt, "_SiltColor");
			AssertColour(MaterialColour(mat, "_FoamColor"), today.Foam, "_FoamColor");
			Color density = MaterialColour(mat, "_WaterDensity");
			Assert.That(density.r, Is.EqualTo(today.Absorption.x).Within(1e-6f));
			Assert.That(density.g, Is.EqualTo(today.Absorption.y).Within(1e-6f));
			Assert.That(density.b, Is.EqualTo(today.Absorption.z).Within(1e-6f));
			Assert.That(MaterialFloat(mat, "_Turbidity"), Is.EqualTo(today.Turbidity).Within(1e-6f));
			Assert.That(today.Defined, Is.True);
			Assert.That(default(BiomeWaterLook).Defined, Is.False, "a zeroed look reads as not filled in");
		}

		/// <summary>Every biome asset's spec look is today's, exactly (the spec's default; no biome's file gives its own yet).</summary>
		[Test]
		public void SpecWaterLook_MatchesApprovedOverrides_ForEveryBiomeAsset()
		{
			int count = 0;
			
			foreach (string name in BiomeAssetNames())
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(name);
				Assert.That(entry, Is.Not.Null, name);
				Assert.That(entry.WaterLook().Signature(), Is.EqualTo(ExpectedAuthoredWater(name).Signature()), name);
				count++;
			}
			Assert.That(count, Is.EqualTo(83));
		}

		/// <summary>
		/// The template's field starts as today's look (so every asset written before the field reads it), the authoring writes
		/// it and marks it, and any look an asset already holds is today's.
		/// </summary>
		[Test]
		public void TemplateLook_DefaultsToTodays_AndAssetsHoldTheirAuthoredLook()
		{
			string template = CodeOnly(Read(TemplateSource));
			StringAssert.Contains("private BiomeWaterLook waterLook = BiomeWaterLooks.Today;", template);
			StringAssert.Contains("public const string WaterLook = nameof(waterLook);", template);
			string authoring = CodeOnly(Read(AuthoringSource));
			StringAssert.Contains("AuthorWaterLook(biome, entry, report, apply);", authoring);
			StringAssert.Contains("BiomeTemplate.Authored.WaterLook", authoring);

			BiomeWaterLook today = BiomeWaterLooks.Today;
			int holding = 0;
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = Read(path);
				Match block = Regex.Match(text, @"^  waterLook:\n((?:    .*\n)+)", RegexOptions.Multiline);
				if (!block.Success)
				{
					continue;
				}
				holding++;
				string body = block.Groups[1].Value;
				var look = new BiomeWaterLook(Colour(body, "Shallow"), Colour(body, "Deep"), Vector(body, "Absorption"), Float(body, "Turbidity"),
					Colour(body, "Silt"), Colour(body, "Foam")) { Hot = Regex.IsMatch(body, @"(?m)^    Hot: 1$"), Melt = Regex.IsMatch(body, @"(?m)^    Melt: 1$") };
				Assert.That(look.Approximately(ExpectedAuthoredWater(Path.GetFileNameWithoutExtension(path)), 1e-5f), Is.True, $"{Path.GetFileName(path)} holds an unexpected authored look: {look}");
			}
			TestContextOut($"{holding} biome asset(s) hold a water look; the rest read the field's default.");
		}

		/// <summary>Every spec biome's crops lie flat, as every field did; the assets' crops (once authored) too.</summary>
		[Test]
		public void SpecCrops_AreFlat_ForEveryBiome()
		{
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach ((string prefab, float _) in entry.FieldCrops())
				{
					Assert.That(entry.FieldSurface(prefab), Is.EqualTo(BiomeFieldSurface.Flat), $"{entry.Biome}: {prefab}");
				}
			}
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				Match crops = Regex.Match(Read(path), @"^  fieldCrops:\n((?:  [- ] .*\n)+)", RegexOptions.Multiline);
				if (!crops.Success)
				{
					continue;
				}
				foreach (Match surface in Regex.Matches(crops.Groups[1].Value, @"^\s+Surface: (\d+)\s*$", RegexOptions.Multiline))
				{
					Assert.That(surface.Groups[1].Value, Is.EqualTo("0"), $"{Path.GetFileName(path)}: a crop not laid flat");
				}
			}
		}

		/// <summary>A flat crop's mark reads as before fields had a surface (so marks already written stay the tool's); a ridged one names it.</summary>
		[Test]
		public void CropMarks_OfFlatCrops_AreAsBefore()
		{
			Assert.That(new BiomeFieldCrop(null, 1f).ToString(), Is.EqualTo("<none>×1"));
			Assert.That(new BiomeFieldCrop(null, 2.5f, BiomeFieldSurface.Flat).ToString(), Is.EqualTo("<none>×2.5"));
			Assert.That(new BiomeFieldCrop(null, 1f, BiomeFieldSurface.LazyBed).ToString(), Is.EqualTo("<none>×1|LazyBed"));
			Assert.That(new BiomeFieldCrop(null, 1f, BiomeFieldSurface.RidgeAndFurrow).ToString(), Is.EqualTo("<none>×1|RidgeAndFurrow"));
			Assert.That((byte)BiomeFieldSurface.Flat, Is.EqualTo(0), "Flat is the zero an old asset reads");
			Assert.That((byte)BiomeFieldSurface.LazyBed, Is.EqualTo(1));
			Assert.That((byte)BiomeFieldSurface.RidgeAndFurrow, Is.EqualTo(2));
		}

		// ── The ridge profile ───────────────────────────────────────

		/// <summary>A flat field: sown everywhere, level, no furrows.</summary>
		[Test]
		public void FlatField_IsSownEverywhere_AndLevel()
		{
			Assert.That(BiomeFieldSurfaces.Ridged(BiomeFieldSurface.Flat), Is.False);
			Assert.That(BiomeFieldSurfaces.RidgeCount(BiomeFieldSurface.Flat, 30f), Is.EqualTo(0));
			for (float x = -2f; x <= 32f; x += 0.37f)
			{
				Assert.That(BiomeFieldSurfaces.OnRidge(BiomeFieldSurface.Flat, 30f, x), Is.True);
				Assert.That(BiomeFieldSurfaces.Height(BiomeFieldSurface.Flat, 30f, x), Is.EqualTo(0f));
				Assert.That(BiomeFieldSurfaces.SownShare(BiomeFieldSurface.Flat, 30f, x, x + 1f), Is.EqualTo(1f));
			}
			Assert.That(BiomeFieldSurfaces.Furrows(BiomeFieldSurface.Flat, 30f, out float width), Is.Empty);
			Assert.That(width, Is.EqualTo(0f));
		}

		/// <summary>
		/// Lazy beds: 1.2 m ridges and 0.6 m trenches, a whole number to the band, each band edge in a trench; the crown 0.35 m
		/// over the trench bottom, flat-topped with rounded shoulders, symmetric about each crown; two thirds of the band sown.
		/// </summary>
		[Test]
		public void LazyBeds_LieAsTheirProfileSays()
		{
			const BiomeFieldSurface lazy = BiomeFieldSurface.LazyBed;
			BiomeFieldSurfaces.Rows rows = BiomeFieldSurfaces.Nominal(lazy);
			Assert.That(rows.Ridge, Is.EqualTo(1.2f));
			Assert.That(rows.Furrow, Is.EqualTo(0.6f));
			Assert.That(rows.Height, Is.EqualTo(0.35f));

			const float band = 18f;
			Assert.That(BiomeFieldSurfaces.RidgeCount(lazy, band), Is.EqualTo(10));
			Assert.That(BiomeFieldSurfaces.Period(lazy, band), Is.EqualTo(1.8f).Within(1e-5f));
			List<float> furrows = BiomeFieldSurfaces.Furrows(lazy, band, out float width);
			Assert.That(furrows.Count, Is.EqualTo(9));
			Assert.That(width, Is.EqualTo(0.6f).Within(1e-5f));
			for (int k = 0; k < furrows.Count; k++)
			{
				Assert.That(furrows[k], Is.EqualTo((k + 1) * 1.8f).Within(1e-4f));
				Assert.That(BiomeFieldSurfaces.OnRidge(lazy, band, furrows[k]), Is.False, "a trench's middle is not sown");
				Assert.That(BiomeFieldSurfaces.Height(lazy, band, furrows[k]), Is.EqualTo(0f));
			}
			Assert.That(BiomeFieldSurfaces.OnRidge(lazy, band, 0.05f), Is.False, "the band's edge lies in a trench");
			Assert.That(BiomeFieldSurfaces.OnRidge(lazy, band, band - 0.05f), Is.False);
			for (int r = 0; r < 10; r++)
			{
				float crown = (r + 0.5f) * 1.8f;
				Assert.That(BiomeFieldSurfaces.OnRidge(lazy, band, crown), Is.True);
				Assert.That(BiomeFieldSurfaces.Height(lazy, band, crown), Is.EqualTo(0.35f).Within(1e-5f), "flat top");
				Assert.That(BiomeFieldSurfaces.Height(lazy, band, crown + 0.35f), Is.EqualTo(0.35f).Within(1e-5f), "flat top to the shoulder");
				for (float d = 0f; d < 0.9f; d += 0.05f)
				{
					Assert.That(BiomeFieldSurfaces.Height(lazy, band, crown - d), Is.EqualTo(BiomeFieldSurfaces.Height(lazy, band, crown + d)).Within(1e-4f), "symmetric");
				}
				float last = float.MaxValue;
				for (float d = 0.4f; d <= 0.6f; d += 0.02f)
				{
					float h = BiomeFieldSurfaces.Height(lazy, band, crown + d);
					Assert.That(h, Is.LessThanOrEqualTo(last + 1e-6f), "the shoulder only falls");
					last = h;
				}
			}
			Assert.That(SownOver(lazy, band), Is.EqualTo(2f / 3f).Within(0.01f));
			Assert.That(BiomeFieldSurfaces.OnRidge(lazy, band, -0.5f), Is.False);
			Assert.That(BiomeFieldSurfaces.Height(lazy, band, band + 0.5f), Is.EqualTo(0f));
		}

		/// <summary>
		/// Ridge and furrow: 6 m ridges and 1 m furrows, the crown 0.5 m and highest on the ridge's middle line, falling
		/// smoothly to the furrows; a band not a whole number of periods is stretched to the nearest (never under one ridge).
		/// </summary>
		[Test]
		public void RidgeAndFurrow_LiesAsItsProfileSays()
		{
			const BiomeFieldSurface strips = BiomeFieldSurface.RidgeAndFurrow;
			Assert.That(BiomeFieldSurfaces.Nominal(strips).Period, Is.EqualTo(7f));
			Assert.That(BiomeFieldSurfaces.RidgeCount(strips, 35f), Is.EqualTo(5));
			Assert.That(BiomeFieldSurfaces.RidgeCount(strips, 38f), Is.EqualTo(5), "38 m holds five stretched ridges");
			Assert.That(BiomeFieldSurfaces.RidgeCount(strips, 3f), Is.EqualTo(1), "a narrow field is one ridge");
			Assert.That(BiomeFieldSurfaces.Height(strips, 35f, 3.5f), Is.EqualTo(0.5f).Within(1e-5f));
			Assert.That(BiomeFieldSurfaces.Height(strips, 35f, 7f), Is.EqualTo(0f));
			float previous = 0.5f;
			for (float d = 0.25f; d < 3f; d += 0.25f)
			{
				float h = BiomeFieldSurfaces.Height(strips, 35f, 3.5f + d);
				Assert.That(h, Is.LessThan(previous), "falls from the crown");
				Assert.That(h, Is.EqualTo(BiomeFieldSurfaces.Height(strips, 35f, 3.5f - d)).Within(1e-4f));
				previous = h;
			}
			List<float> furrows = BiomeFieldSurfaces.Furrows(strips, 35f, out float width);
			Assert.That(furrows, Is.EqualTo(new[] { 7f, 14f, 21f, 28f }).Within(1e-4f));
			Assert.That(width, Is.EqualTo(1f).Within(1e-5f));
			Assert.That(SownOver(strips, 35f), Is.EqualTo(6f / 7f).Within(0.01f));
			Assert.That(BiomeFieldSurfaces.Furrows(strips, 3f, out _), Is.Empty, "one ridge, no furrow between");
		}

		/// <summary>
		/// A field's sown texels: the whole crop on a flat field wherever the field is; on a ridged one the crop times the share
		/// of the texel on a ridge, across the field's short side (the ridges run its length).
		/// </summary>
		[Test]
		public void SownCoverage_IsTheCropOnTheRidgesOnly()
		{
			var size = new Vector2(60f, 30f);
			for (float u = -28f; u <= 28f; u += 3.3f)
			{
				for (float v = -13f; v <= 13f; v += 0.7f)
				{
					Assert.That(PointOfInterestFields.SownCoverage(BiomeFieldSurface.Flat, size, u, v, 1f), Is.EqualTo(PointOfInterestFields.WheatCoverage));
				}
			}
			float band = PointOfInterestFields.RidgeBand(size, 5f, 2f, out float across);
			Assert.That(band, Is.EqualTo(27f), "the short side less the headland each side");
			Assert.That(across, Is.EqualTo(15.5f), "across the short side from the band's edge");
			Assert.That(PointOfInterestFields.RidgeBand(new Vector2(20f, 50f), 2f, 5f, out float acrossU), Is.EqualTo(17f));
			Assert.That(acrossU, Is.EqualTo(10.5f), "a field longer in z runs its ridges along z");

			float period = BiomeFieldSurfaces.Period(BiomeFieldSurface.LazyBed, band);
			float crownV = 0.5f * period - 0.5f * band;
			float furrowV = period - 0.5f * band;
			Assert.That(PointOfInterestFields.SownCoverage(BiomeFieldSurface.LazyBed, size, 0f, crownV, 0.2f), Is.EqualTo(PointOfInterestFields.WheatCoverage));
			Assert.That(PointOfInterestFields.SownCoverage(BiomeFieldSurface.LazyBed, size, 0f, furrowV, 0.2f), Is.EqualTo(0));
			int partial = PointOfInterestFields.SownCoverage(BiomeFieldSurface.LazyBed, size, 0f, furrowV, 1.2f);
			Assert.That(partial, Is.GreaterThan(0).And.LessThan(PointOfInterestFields.WheatCoverage), "a texel over a trench's edge is partly sown");
		}

		// ── Which biome a body of water takes ───────────────────────

		private const int Bog = 11, Meadow = 12, Heath = 13, LakeBiome = 90, RiverBiome = 91;

		private static BiomeWaterRole? Roles(int id)
		{
			switch (id)
			{
				case Bog:
				case Meadow:
				case Heath: return BiomeWaterRole.None;
				case LakeBiome: return BiomeWaterRole.Lake;
				case RiverBiome: return BiomeWaterRole.River;
				default: return null;
			}
		}

		/// <summary>The commonest land biome on the shore wins over the water biome painted under the water; ties go to the smaller ID, whatever the order counted.</summary>
		[Test]
		public void Choose_TakesTheCommonestLandOnTheShore()
		{
			var shore = new Dictionary<int, float> { [LakeBiome] = 50f, [Meadow] = 4f, [Bog] = 7f };
			Assert.That(BiomeWaterLooks.Choose(shore, null, Roles), Is.EqualTo(Bog));
			var tie = new Dictionary<int, float> { [Meadow] = 5f, [Bog] = 5f };
			var tieReversed = new Dictionary<int, float> { [Bog] = 5f, [Meadow] = 5f };
			Assert.That(BiomeWaterLooks.Choose(tie, null, Roles), Is.EqualTo(Bog));
			Assert.That(BiomeWaterLooks.Choose(tieReversed, null, Roles), Is.EqualTo(Bog));
			// No land on the shore: the land under its edge; then the commonest water biome; unknown IDs never.
			var allWater = new Dictionary<int, float> { [LakeBiome] = 3f, [RiverBiome] = 5f, [999] = 40f };
			Assert.That(BiomeWaterLooks.Choose(allWater, new Dictionary<int, float> { [Heath] = 1f }, Roles), Is.EqualTo(Heath));
			Assert.That(BiomeWaterLooks.Choose(allWater, null, Roles), Is.EqualTo(RiverBiome));
			Assert.That(BiomeWaterLooks.Choose(new Dictionary<int, float> { [999] = 1f }, null, Roles), Is.EqualTo(0));
			Assert.That(BiomeWaterLooks.Choose(null, null, Roles), Is.EqualTo(0));
			Assert.That(BiomeWaterLooks.Choose(shore, null, null), Is.EqualTo(0));
			Assert.That(BiomeWaterLooks.IsWaterBody(BiomeWaterRole.Estuary) && BiomeWaterLooks.IsWaterBody(BiomeWaterRole.MethaneLake), Is.True);
			Assert.That(BiomeWaterLooks.IsWaterBody(BiomeWaterRole.SaltFlat) || BiomeWaterLooks.IsWaterBody(BiomeWaterRole.IceSheet), Is.False);
		}

		/// <summary>
		/// A bog pool on a map painted as the scene paints it (the lake biome under and just round the water, bog west of x = 30,
		/// meadow east): the pool reads its shores, not the water biome, and takes the land most of its shore lies in.
		/// </summary>
		[Test]
		public void LakeShore_ReadsTheLandRoundTheWater()
		{
			// 6 × 4 cells of 4 m from (0, 0): x 0 … 24, z 0 … 16.
			SceneHydrology.Lake pool = Lake(new Vector2(0f, 0f), 6, 4);
			int Map(float x, float z)
			{
				bool nearWater = x > -6f && x < 30f && z > -6f && z < 22f;
				return nearWater ? LakeBiome : x < 30f ? Bog : Meadow;
			}
			var shore = new Dictionary<int, float>();
			var under = new Dictionary<int, float>();
			BiomeWaterLooks.LakeShore(pool, Map, 16f, shore, under);
			Assert.That(under.Keys, Is.EquivalentTo(new[] { LakeBiome }));
			Assert.That(shore.ContainsKey(Bog) && shore.ContainsKey(Meadow), Is.True, "read past the water cells on both sides");
			Assert.That(BiomeWaterLooks.Choose(shore, under, Roles), Is.EqualTo(Bog), "most of the pool's shore is bog");
			// Every edge cell's open sides read twice (ShoreCells), interior cells never.
			int edgeSides = 2 * 6 + 2 * 4;
			float total = 0f;
			foreach (float weight in shore.Values)
			{
				total += weight;
			}
			Assert.That(total, Is.EqualTo(edgeSides * BiomeWaterLooks.ShoreCells.Length));
		}

		/// <summary>
		/// A river reads both its banks along its length, each stretch by its length: a burn running 60 m through bog and 140 m
		/// through meadow is a meadow river; one 150 m through bog is a bog river.
		/// </summary>
		[Test]
		public void RiverShore_WeighsTheBanksByLength()
		{
			int Map(float x, float z) => Mathf.Abs(z) < 5f ? RiverBiome : x < -40f ? Bog : Meadow;
			SceneHydrology.River burn = River(new Vector3(-100f, 5f, 0f), new Vector3(100f, 1f, 0f), 21);
			var shore = new Dictionary<int, float>();
			var under = new Dictionary<int, float>();
			BiomeWaterLooks.RiverShore(burn, Map, 16f, shore, under);
			Assert.That(under.Keys, Is.EquivalentTo(new[] { RiverBiome }));
			Assert.That(BiomeWaterLooks.Choose(shore, under, Roles), Is.EqualTo(Meadow));
			Assert.That(shore[Bog] + shore[Meadow], Is.EqualTo(200f * 2f * BiomeWaterLooks.ShoreCells.Length).Within(1e-2f), "every metre on both banks, read twice");

			int BogMap(float x, float z) => Mathf.Abs(z) < 5f ? RiverBiome : x < 50f ? Bog : Meadow;
			shore.Clear();
			under.Clear();
			BiomeWaterLooks.RiverShore(burn, BogMap, 16f, shore, under);
			Assert.That(BiomeWaterLooks.Choose(shore, under, Roles), Is.EqualTo(Bog));
		}

		// ── Where water meets water ──────────────────────────────────

		/// <summary>
		/// A river carries all of the look of the water it meets at that end (so the two meshes are one colour where they meet),
		/// none past its blend run, smoothly between; never more than all of it on a short river; nothing at an end that meets
		/// no water.
		/// </summary>
		[Test]
		public void Blend_IsTheWaterAtEachEnd_AndOnlyThere()
		{
			const float length = 500f, run = 40f;
			Assert.That(BiomeWaterLooks.Blend(-5f, length, run, run, true, true), Is.EqualTo(new Vector2(1f, 0f)), "out of the lake it leaves");
			Assert.That(BiomeWaterLooks.Blend(0f, length, run, run, true, true), Is.EqualTo(new Vector2(1f, 0f)));
			Assert.That(BiomeWaterLooks.Blend(length, length, run, run, true, true), Is.EqualTo(new Vector2(0f, 1f)), "into the water it joins");
			Assert.That(BiomeWaterLooks.Blend(length + 8f, length, run, run, true, true), Is.EqualTo(new Vector2(0f, 1f)), "its rows run on into it");
			Assert.That(BiomeWaterLooks.Blend(250f, length, run, run, true, true), Is.EqualTo(Vector2.zero), "its own look between");
			Assert.That(BiomeWaterLooks.Blend(0f, length, run, run, false, false), Is.EqualTo(Vector2.zero), "a source and the sea meet no look");
			Assert.That(BiomeWaterLooks.Blend(length, length, run, run, false, false), Is.EqualTo(Vector2.zero));
			float last = 1f;
			for (float a = 0f; a <= run; a += 2f)
			{
				float s = BiomeWaterLooks.Blend(a, length, run, run, true, false).x;
				Assert.That(s, Is.LessThanOrEqualTo(last + 1e-6f));
				last = s;
			}
			for (float a = -10f; a <= 50f; a += 1f)
			{
				Vector2 shortRiver = BiomeWaterLooks.Blend(a, 30f, run, run, true, true);
				Assert.That(shortRiver.x + shortRiver.y, Is.LessThanOrEqualTo(1f + 1e-5f), $"along {a}");
				Assert.That(shortRiver.x, Is.GreaterThanOrEqualTo(0f));
				Assert.That(shortRiver.y, Is.GreaterThanOrEqualTo(0f));
			}
			Vector2 middle = BiomeWaterLooks.Blend(15f, 30f, run, run, true, true);
			Assert.That(middle.x, Is.EqualTo(middle.y).Within(1e-5f), "a short river halfway is as much of each end");
			Assert.That(BiomeWaterLooks.BlendMetres(2f), Is.EqualTo(BiomeWaterLooks.MinBlendMetres));
			Assert.That(BiomeWaterLooks.BlendMetres(10f), Is.EqualTo(80f));
		}

		/// <summary>A look's lerp goes value by value, and its signature tells any change apart.</summary>
		[Test]
		public void Looks_LerpAndSign_ValueByValue()
		{
			BiomeWaterLook today = BiomeWaterLooks.Today;
			var peat = new BiomeWaterLook(new Color(0.35f, 0.2f, 0.08f, 1f), new Color(0.03f, 0.015f, 0.005f, 1f), new Vector3(0.9f, 1.8f, 3.2f), 0.2f,
				new Color(0.25f, 0.15f, 0.07f, 1f), new Color(0.85f, 0.78f, 0.62f, 1f));
			Assert.That(BiomeWaterLook.Lerp(today, peat, 0f).Signature(), Is.EqualTo(today.Signature()));
			Assert.That(BiomeWaterLook.Lerp(today, peat, 1f).Signature(), Is.EqualTo(peat.Signature()));
			BiomeWaterLook half = BiomeWaterLook.Lerp(today, peat, 0.5f);
			Assert.That(half.Absorption.z, Is.EqualTo(0.5f * (today.Absorption.z + peat.Absorption.z)).Within(1e-6f));
			Assert.That(half.Deep.r, Is.EqualTo(0.5f * (today.Deep.r + peat.Deep.r)).Within(1e-6f));
			Assert.That(today.Approximately(peat), Is.False);
			Assert.That(peat.Signature(), Is.Not.EqualTo(today.Signature()));
			BiomeWaterLook nudged = today;
			nudged.Foam.b += 1e-3f;
			Assert.That(nudged.Signature(), Is.Not.EqualTo(today.Signature()));
		}

		// ── The shader and the renderer agree ───────────────────────

		/// <summary>
		/// The shader reads the material's colour and clarity only through the look (InlandLookAt), which gives the material's
		/// own values when no look is set: a surface without a block draws exactly as before. The looks' layout (5 vectors × 3
		/// looks, uv5 weights) is the renderer's.
		/// </summary>
		[Test]
		public void ShaderAndRenderer_AgreeOnTheLooks()
		{
			string hlsl = Read(InlandHlsl);
			StringAssert.Contains("#define INLAND_LOOK_VECTORS 5", hlsl);
			StringAssert.Contains("float4 _InlandLooks[15];", hlsl);
			StringAssert.Contains("float _InlandLookOn;", hlsl);
			StringAssert.Contains("float2 look : TEXCOORD5;", hlsl);
			Assert.That(InlandWaterRendererLookVectors() * InlandWaterRendererLookSlots(), Is.EqualTo(15));

			int cbuffer = hlsl.IndexOf("CBUFFER_START(UnityPerMaterial)", StringComparison.Ordinal);
			int cbufferEnd = hlsl.IndexOf("CBUFFER_END", cbuffer, StringComparison.Ordinal);
			int lookAt = hlsl.IndexOf("InlandLook InlandLookAt(", StringComparison.Ordinal);
			int lookAtEnd = hlsl.IndexOf("\n}\n", lookAt, StringComparison.Ordinal);
			Assert.That(cbuffer >= 0 && cbufferEnd > cbuffer && lookAt > cbufferEnd && lookAtEnd > lookAt, Is.True, "the scan's landmarks");
			string function = hlsl.Substring(lookAt, lookAtEnd - lookAt);
			StringAssert.Contains("if (_InlandLookOn < 0.5)", function);
			foreach (string line in new[] { "look.shallow = _ShallowColor.rgb;", "look.deep = _DeepColor.rgb;", "look.density = _WaterDensity.rgb;",
				"look.turbidity = _Turbidity;", "look.silt = _SiltColor.rgb;", "look.foam = _FoamColor.rgb;" })
			{
				StringAssert.Contains(line, function);
			}
			string rest = CodeOnly(hlsl.Remove(lookAt, lookAtEnd - lookAt).Remove(cbuffer, cbufferEnd - cbuffer));
			foreach (string name in new[] { "_ShallowColor", "_DeepColor", "_WaterDensity", "_Turbidity", "_SiltColor", "_FoamColor" })
			{
				Assert.That(Regex.IsMatch(rest, $@"\b{name}\b"), Is.False, $"{name} is read outside the look");
			}
			StringAssert.Contains("output.look = input.look;", hlsl);

			string renderer = CodeOnly(Read(InlandRenderer));
			StringAssert.Contains("mesh.SetUVs(5,", renderer);
			StringAssert.Contains("BiomeWaterLooks.Blend(row.Along, length, lookStart, lookEnd, lookFromStart, lookIntoEnd)", renderer);
			StringAssert.Contains("PrepareLooks(hydrology);", renderer);
			string looks = CodeOnly(Read(InlandLooks));
			StringAssert.Contains("Shader.PropertyToID(\"_InlandLooks\")", looks);
			StringAssert.Contains("Shader.PropertyToID(\"_InlandLookOn\")", looks);
			StringAssert.Contains("BiomeRegistry.Changed += OnBiomesChanged;", looks);
			string flow = CodeOnly(Read(InlandFlow));
			StringAssert.Contains("any |= ApplyRiverLook(pair.Key, flowBlock);", flow);
			StringAssert.Contains("ApplyLakeLooks();", flow);
			// Every pass of the shader still compiles with DXC.
			Assert.That(Regex.Matches(Read(InlandShader), @"HLSLPROGRAM\s*\n\s*#pragma use_dxc vulkan").Count, Is.EqualTo(2));
		}

		/// <summary>The fields lay the crop's own surface, and a flat field's texels take the crop's coverage as before.</summary>
		[Test]
		public void Fields_LayTheCropsSurface_AndFlatAsBefore()
		{
			string fields = CodeOnly(Read(FieldsSource));
			StringAssert.Contains("surface = pick >= 0 ? grown[pick].Surface : BiomeFieldSurface.Flat;", fields);
			StringAssert.Contains("int value = !sown ? 0 : surface == BiomeFieldSurface.Flat ? WheatCoverage : SownCoverage(surface, field, u, v, texel);", fields);
			StringAssert.Contains("if (BiomeFieldSurfaces.Ridged(surface))", fields);
			int furrows = fields.IndexOf("private static void AddFurrows(", StringComparison.Ordinal);
			Assert.That(furrows, Is.GreaterThan(0));
			string body = fields.Substring(furrows, fields.IndexOf("\n\t\t}\n", furrows, StringComparison.Ordinal) - furrows);
			Assert.That(body.Contains("Random"), Is.False, "the furrows draw nothing from the site's sequence, so the fences and later fields lie as before");
		}

		// ── Helpers ─────────────────────────────────────────────────

		// The renderer's layout constants, read as compile-time constants (touching the MonoBehaviour's statics would run its
		// static constructor, Shader.PropertyToID, which is native).
		private static int InlandWaterRendererLookVectors() => FishMMO.Water.InlandWaterRenderer.LookVectors;
		private static int InlandWaterRendererLookSlots() => FishMMO.Water.InlandWaterRenderer.LookSlots;

		private static float SownOver(BiomeFieldSurface surface, float band)
		{
			int sown = 0, total = 0;
			for (float x = 0.005f; x < band; x += 0.01f)
			{
				sown += BiomeFieldSurfaces.OnRidge(surface, band, x) ? 1 : 0;
				total++;
			}
			return sown / (float)total;
		}

		private static SceneHydrology.Lake Lake(Vector2 origin, int width, int height)
		{
			var lake = new SceneHydrology.Lake { Id = 1, MaskOrigin = origin, MaskCell = 4f, MaskWidth = width, MaskHeight = height };
			lake.Mask = new byte[(width * height + 7) / 8];
			for (int bit = 0; bit < width * height; bit++)
			{
				lake.Mask[bit >> 3] |= (byte)(1 << (bit & 7));
			}
			return lake;
		}

		private static SceneHydrology.River River(Vector3 from, Vector3 to, int points)
		{
			var river = new SceneHydrology.River { Id = 1, Points = new Vector3[points], Width = new float[points] };
			for (int i = 0; i < points; i++)
			{
				river.Points[i] = Vector3.Lerp(from, to, i / (float)(points - 1));
				river.Width[i] = 3f;
			}
			return river;
		}

		private static void AssertColour(Color stored, Color expected, string what)
		{
			Assert.That(stored.r, Is.EqualTo(expected.r).Within(1e-6f), what);
			Assert.That(stored.g, Is.EqualTo(expected.g).Within(1e-6f), what);
			Assert.That(stored.b, Is.EqualTo(expected.b).Within(1e-6f), what);
		}

		private static Color MaterialColour(string mat, string name)
		{
			Match m = Regex.Match(mat, $@"- {name}: \{{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}}]+)\}}");
			Assert.That(m.Success, Is.True, $"{name} in the material");
			return new Color(F(m.Groups[1].Value), F(m.Groups[2].Value), F(m.Groups[3].Value), F(m.Groups[4].Value));
		}

		private static float MaterialFloat(string mat, string name)
		{
			Match m = Regex.Match(mat, $@"- {name}: ([^\s]+)");
			Assert.That(m.Success, Is.True, $"{name} in the material");
			return F(m.Groups[1].Value);
		}

		private static Color Colour(string body, string name)
		{
			Match m = Regex.Match(body, $@"{name}: \{{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}}]+)\}}");
			Assert.That(m.Success, Is.True, name);
			return new Color(F(m.Groups[1].Value), F(m.Groups[2].Value), F(m.Groups[3].Value), F(m.Groups[4].Value));
		}

		private static Vector3 Vector(string body, string name)
		{
			Match m = Regex.Match(body, $@"{name}: \{{x: ([^,]+), y: ([^,]+), z: ([^}}]+)\}}");
			Assert.That(m.Success, Is.True, name);
			return new Vector3(F(m.Groups[1].Value), F(m.Groups[2].Value), F(m.Groups[3].Value));
		}

		private static float Float(string body, string name)
		{
			Match m = Regex.Match(body, $@"{name}: ([^\s]+)");
			Assert.That(m.Success, Is.True, name);
			return F(m.Groups[1].Value);
		}

		private static float F(string text) => float.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

		private static string Read(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative)).Replace("\r\n", "\n");

		private static void TestContextOut(string line) => Console.WriteLine(line);

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
