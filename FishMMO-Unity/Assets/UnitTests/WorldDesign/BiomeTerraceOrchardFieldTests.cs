using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Decisions 29, 31, 38 and the field parts of 21/23/27 of the biome suite (2026-10-18, phase I2j-b): terraced fields (drystone,
	/// earth-bund and grass-bank steps, terraced paddies) and the old terraces round a hamlet, orchards as a kind of field, crops the
	/// detail scatter draws sown in rows, the corn-hill, baulatura, marcita and raised-bed surfaces, fields kept to the bottomland and
	/// set to a centuriation grid, and the wet margin painted along stored channel lines. Every default is exactly what was cut before.
	/// </summary>
	public class BiomeTerraceOrchardFieldTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string FieldsSource = World + "/PointsOfInterest/Features/PointOfInterestFields.cs";
		private const string LinearSource = World + "/PointsOfInterest/Features/PointOfInterestLinearFeatures.cs";
		private const string ChannelsSource = World + "/PointsOfInterest/Features/PointOfInterestLinearFeatures.Channels.cs";
		private const string GeneratorSource = World + "/SceneGenerator.cs";
		private const string AuthoringSource = World + "/ProceduralArt/BiomeArtAuthoring.cs";
		private const string ReaderSource = World + "/Terrain/BiomeTemplateArt.cs";
		private const string TemplateSource = "Assets/Scripts/Shared/Implementation/Tools/Biomes/BiomeTemplate.cs";

		private static string Read(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative)).Replace("\r\n", "\n");

		private static string CodeOnly(string source) => Regex.Replace(source, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

		// ── Defaults: nothing changes ─────────────────────────────────

		/// <summary>No biome's spec, nor any template asset, names an orchard, a layout, old terraces, a channel margin, a riser, rows or a new surface.</summary>
		[Test]
		public void Defaults_NoBiomeNamesAnyOfIt()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			// "No biome names it yet" pins: the authored rounds plant orchards, terraces and laid-out fields on purpose.
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries.Where(e => !BiomeAuthoredRounds.Contains(e.Biome)))
			{
				Assert.That(entry.OrchardEntries(), Is.Empty, $"{entry.Biome} plants no orchard yet");
				Assert.That(entry.FieldLayout().Any, Is.False, entry.Biome);
				Assert.That(entry.OldTerraces().Any, Is.False, entry.Biome);
				Assert.That(entry.WetMargin().Defined, Is.False, entry.Biome);
				foreach ((string prefab, float _) in entry.FieldCrops())
				{
					Assert.That(entry.FieldRiser(prefab), Is.EqualTo(BiomeTerraceRiser.None), $"{entry.Biome}: {prefab}");
					Assert.That(entry.FieldRows(prefab), Is.EqualTo(0f), $"{entry.Biome}: {prefab}");
					Assert.That((int)entry.FieldSurface(prefab), Is.LessThan((int)BiomeFieldSurface.Terraced), $"{entry.Biome}: {prefab}");
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
				if (BiomeAuthoredRounds.Contains(Path.GetFileNameWithoutExtension(path)))
				{
					continue;
				}
				string name = Path.GetFileName(path);
				Assert.That(Regex.IsMatch(text, @"^  orchards:\s*\n  - ", RegexOptions.Multiline), Is.False, $"{name} plants an orchard");
				Assert.That(Regex.IsMatch(text, @"^\s*Surface: [4-9]\s*$", RegexOptions.Multiline), Is.False, $"{name} lays a new surface");
				// The abandoned-terrace block carries its own (inert while Flights is 0) Riser; only crop risers count here.
				string crops = Regex.Replace(text, @"^  abandonedTerraces:\s*\n(?:    .*\n)*", "", RegexOptions.Multiline);
				Assert.That(Regex.IsMatch(crops, @"^\s*Riser: [1-9]\s*$", RegexOptions.Multiline), Is.False, $"{name} terraces a crop");
				Assert.That(Regex.IsMatch(text, @"^\s*Rows: (?!0\s*$)", RegexOptions.Multiline), Is.False, $"{name} sows a crop in rows");
				Assert.That(Regex.IsMatch(text, @"^    Bottomland: (?!0\s*$)", RegexOptions.Multiline), Is.False, $"{name} keeps fields to the bottomland");
				Assert.That(Regex.IsMatch(text, @"^    Centuriation: (?!0\s*$)", RegexOptions.Multiline), Is.False, $"{name} lays a centuriation grid");
				Assert.That(Regex.IsMatch(text, @"^    Flights: (?!0\s*$)", RegexOptions.Multiline), Is.False, $"{name} cuts old terraces");
			}
			Assert.That(templates, Is.EqualTo(83));
			string template = Read(TemplateSource);
			StringAssert.Contains("private List<BiomeOrchard> orchards = new List<BiomeOrchard>();", template);
			StringAssert.Contains("private BiomeFieldLayout fieldLayout;", template);
			StringAssert.Contains("private BiomeAbandonedTerraces abandonedTerraces = BiomeAbandonedTerraces.None;", template);
			StringAssert.Contains("private BiomeChannelMargin channelMargin = BiomeChannelMargin.None;", template);
			Assert.That(BiomeAbandonedTerraces.None.Any, Is.False);
			Assert.That(BiomeChannelMargin.None.Defined, Is.False);
			Assert.That(default(BiomeChannelMargin).Defined, Is.False, "an unset margin paints nothing");
			Assert.That(default(BiomeFieldLayout).Any, Is.False);
		}

		/// <summary>The enums are append-only (their numbers are what assets store) and a crop entry's mark reads as before unless it uses the new values.</summary>
		[Test]
		public void NewValues_AreAppended_AndOldMarksReadTheSame()
		{
			Assert.That((byte)BiomeFieldSurface.Paddy, Is.EqualTo(3));
			Assert.That((byte)BiomeFieldSurface.Terraced, Is.EqualTo(4));
			Assert.That((byte)BiomeFieldSurface.CornHills, Is.EqualTo(5));
			Assert.That((byte)BiomeFieldSurface.Baulatura, Is.EqualTo(6));
			Assert.That((byte)BiomeFieldSurface.Marcita, Is.EqualTo(7));
			Assert.That((byte)BiomeFieldSurface.RaisedBeds, Is.EqualTo(8));
			Assert.That((byte)BiomeTerraceRiser.None, Is.EqualTo(0), "the zero an old asset reads");
			Assert.That((byte)BiomeTerraceRiser.GrassBank, Is.EqualTo(3));
			Assert.That(new BiomeFieldCrop(null, 2.5f).ToString(), Is.EqualTo("<none>×2.5"));
			Assert.That(new BiomeFieldCrop(null, 1f, BiomeFieldSurface.RidgeAndFurrow).ToString(), Is.EqualTo("<none>×1|RidgeAndFurrow"));
			Assert.That(new BiomeFieldCrop(null, 1f, BiomeFieldSurface.Paddy, BiomeTerraceRiser.EarthBund).ToString(), Is.EqualTo("<none>×1|Paddy|EarthBund"));
			Assert.That(new BiomeFieldCrop(null, 0.2f, BiomeFieldSurface.Terraced, BiomeTerraceRiser.Drystone, 1.8f).ToString(), Is.EqualTo("<none>×0.2|Terraced|Drystone|rows 1.8"));
			Assert.That(new BiomeOrchard(null, 0.3f).ToString(), Is.EqualTo("<none>×0.3 @6m"));
			Assert.That(new BiomeOrchard(null, 0.3f, 5f, null, BiomeFieldSurface.Terraced).ToString(), Is.EqualTo("<none>×0.3 @5m|Terraced"));
			BiomeFieldCrop crop = BiomeOrchards.AsCrop(new BiomeOrchard(null, 0.4f, 0f, null, BiomeFieldSurface.Terraced, BiomeTerraceRiser.GrassBank));
			Assert.That(crop.Weight, Is.EqualTo(0.4f));
			Assert.That(crop.Rows, Is.EqualTo(BiomeOrchards.DefaultSpacing));
			Assert.That(crop.Riser, Is.EqualTo(BiomeTerraceRiser.GrassBank));
		}

		/// <summary>The spec's hooks write what they say, and an entry naming none of them gives the defaults.</summary>
		[Test]
		public void SpecHooks_ChainAndDefault()
		{
			var entry = new BiomeArtSpec.Entry { Biome = "Test" };
			Assert.That(entry.FieldRiser("Detail_CropRice"), Is.EqualTo(BiomeTerraceRiser.None));
			entry.CropRiser("Detail_CropRice", BiomeTerraceRiser.EarthBund).CropRows("Detail_Bush_Chanoki", 1.8f).FieldsNearRivers(150f).Centuriation()
				.AbandonedTerraces(2).ChannelMargin(3, 6f)
				.Orchards(new BiomeArtSpec.Entry.Orchard("Olive", 0.3f, surface: BiomeFieldSurface.Terraced), new BiomeArtSpec.Entry.Orchard("Tree_Almond", 0.2f, 7f, "Detail_GrassSheep"));
			Assert.That(entry.FieldRiser("Detail_CropRice"), Is.EqualTo(BiomeTerraceRiser.EarthBund));
			Assert.That(entry.FieldRows("Detail_Bush_Chanoki"), Is.EqualTo(1.8f));
			Assert.That(entry.FieldLayout().Bottomland, Is.EqualTo(150f));
			Assert.That(entry.FieldLayout().Centuriation, Is.EqualTo(710f), "the Roman grid by default");
			Assert.That(entry.OldTerraces().Flights, Is.EqualTo(2));
			Assert.That(entry.OldTerraces().Riser, Is.EqualTo(BiomeTerraceRiser.Drystone));
			Assert.That(entry.WetMargin().DetailLayer, Is.EqualTo(3));
			Assert.That(entry.WetMargin().Width, Is.EqualTo(6f));
			Assert.That(entry.OrchardEntries()[0].TreePrefab, Is.EqualTo("Tree_Olive"), "a species name is its generated tree");
			Assert.That(entry.OrchardEntries()[1].TreePrefab, Is.EqualTo("Tree_Almond"), "a prefab name is kept");
			var missing = new List<string>();
			Assert.That(entry.FieldOrchards(_ => null, missing), Is.Empty);
			Assert.That(missing, Is.EqualTo(new[] { "Tree_Olive", "Tree_Almond" }));
		}

		// ── Terraces: the bench profile ───────────────────────────────

		[Test]
		public void RiserOf_TerracesOnlyFlatPaddyAndTerraced()
		{
			Assert.That(BiomeTerraces.RiserOf(BiomeFieldSurface.Terraced, BiomeTerraceRiser.None), Is.EqualTo(BiomeTerraceRiser.Drystone));
			Assert.That(BiomeTerraces.RiserOf(BiomeFieldSurface.Terraced, BiomeTerraceRiser.GrassBank), Is.EqualTo(BiomeTerraceRiser.GrassBank));
			Assert.That(BiomeTerraces.RiserOf(BiomeFieldSurface.Flat, BiomeTerraceRiser.None), Is.EqualTo(BiomeTerraceRiser.None), "a flat field stays flat");
			Assert.That(BiomeTerraces.RiserOf(BiomeFieldSurface.Paddy, BiomeTerraceRiser.EarthBund), Is.EqualTo(BiomeTerraceRiser.EarthBund), "terraced paddies");
			foreach (BiomeFieldSurface other in new[] { BiomeFieldSurface.LazyBed, BiomeFieldSurface.RidgeAndFurrow, BiomeFieldSurface.CornHills, BiomeFieldSurface.Baulatura,
				BiomeFieldSurface.Marcita, BiomeFieldSurface.RaisedBeds })
			{
				Assert.That(BiomeTerraces.RiserOf(other, BiomeTerraceRiser.Drystone), Is.EqualTo(BiomeTerraceRiser.None), other.ToString());
			}
			Assert.That(BiomeTerraces.TreadSurface(BiomeFieldSurface.Terraced), Is.EqualTo(BiomeFieldSurface.Flat));
			Assert.That(BiomeTerraces.TreadSurface(BiomeFieldSurface.Paddy), Is.EqualTo(BiomeFieldSurface.Paddy));
		}

		[Test]
		public void RiserHeight_FollowsTheSlope_WithinItsRiserRange()
		{
			foreach (BiomeTerraceRiser riser in new[] { BiomeTerraceRiser.Drystone, BiomeTerraceRiser.EarthBund, BiomeTerraceRiser.GrassBank })
			{
				BiomeTerraces.RiserRange(riser, out float min, out float max);
				float last = 0f;
				for (float slope = 3f; slope <= 30f; slope += 1f)
				{
					float h = BiomeTerraces.RiserHeight(riser, slope);
					Assert.That(h, Is.InRange(min, max), $"{riser} {slope}°");
					Assert.That(h, Is.GreaterThanOrEqualTo(last), "steeper ground, higher steps");
					last = h;
				}
			}
			Assert.That(BiomeTerraces.RiserHeight(BiomeTerraceRiser.Drystone, 12f), Is.EqualTo(BiomeTerraces.TreadMetres * Mathf.Tan(12f * Mathf.Deg2Rad)).Within(1e-4f));
		}

		[Test]
		public void Bench_IsLevelOnEachTread_ContinuousUpItsStep_AndBalancesCutAndFill()
		{
			foreach (float share in new[] { 0.15f, 0.3f, 0.5f })
			{
				float previous = BiomeTerraces.Bench(-3f, share);
				double offset = 0.0;
				int n = 0;
				for (float u = -3f; u <= 3f; u += 0.001f)
				{
					float b = BiomeTerraces.Bench(u, share);
					Assert.That(b - previous, Is.InRange(-1e-4f, 0.05f), $"continuous and never falling ({share}, {u})");
					previous = b;
					if (u >= -2f && u < 2f)
					{
						offset += b - u;
						n++;
					}
					float t = BiomeTerraces.Phase(u);
					if (t < 1f - share - 1e-3f)
					{
						Assert.That(b, Is.EqualTo(BiomeTerraces.TreadLevel(BiomeTerraces.BandOf(u), share)).Within(1e-5f), "level across the tread");
					}
				}
				Assert.That(offset / n, Is.EqualTo(0f).Within(0.02f), $"as much cut as filled ({share})");
			}
			Assert.That(BiomeTerraces.Share(BiomeTerraceRiser.Drystone, 8f), Is.EqualTo(0.2f).Within(1e-5f));
			Assert.That(BiomeTerraces.Share(BiomeTerraceRiser.GrassBank, 8f), Is.EqualTo(0.4f).Within(1e-5f));
			Assert.That(BiomeTerraces.Share(BiomeTerraceRiser.EarthBund, 2f), Is.EqualTo(0.6f), "never more than 0.6");
		}

		[Test]
		public void Lip_Margins_AndPaddyTread()
		{
			Assert.That(BiomeTerraces.LipRaise(BiomeTerraceRiser.EarthBund, 0.5f), Is.EqualTo(BiomeTerraces.LipRise).Within(1e-5f));
			Assert.That(BiomeTerraces.LipRaise(BiomeTerraceRiser.EarthBund, 0f), Is.EqualTo(0f).Within(1e-6f));
			Assert.That(BiomeTerraces.LipRaise(BiomeTerraceRiser.Drystone, 0.5f), Is.EqualTo(0f));
			Assert.That(BiomeTerraces.LipRaise(BiomeTerraceRiser.EarthBund, 1.5f), Is.EqualTo(0f));
			Assert.That(BiomeTerraces.OnTread(BiomeTerraceRiser.Drystone, 0.5f, 0.2f, 8f), Is.True);
			Assert.That(BiomeTerraces.OnTread(BiomeTerraceRiser.Drystone, 0.79f, 0.2f, 8f), Is.False, "the wall's footing");
			Assert.That(BiomeTerraces.OnTread(BiomeTerraceRiser.EarthBund, 0.05f, 0.3f, 8f), Is.False, "the bund's lip");
			Assert.That(BiomeTerraces.OnTread(BiomeTerraceRiser.EarthBund, 0.9f, 0.3f, 8f), Is.False, "the bank");
			Assert.That(BiomeTerraces.KeepsTurf(BiomeTerraceRiser.EarthBund) && BiomeTerraces.KeepsTurf(BiomeTerraceRiser.GrassBank), Is.True);
			Assert.That(BiomeTerraces.KeepsTurf(BiomeTerraceRiser.Drystone), Is.False);
			const float water = 10f;
			Assert.That(BiomeTerraces.PaddyTread(1f, water), Is.EqualTo(water - BiomeFieldSurfaces.PaddyWaterDepth));
			Assert.That(BiomeTerraces.PaddyTread(-1f, water), Is.EqualTo(water + BiomeFieldSurfaces.PaddyBundRise));
			Assert.That(BiomeTerraces.PaddyTread(-0.25f, water), Is.InRange(water - BiomeFieldSurfaces.PaddyWaterDepth, water + BiomeFieldSurfaces.PaddyBundRise));
			Assert.That(BiomeTerraces.Fade(0f), Is.EqualTo(0f));
			Assert.That(BiomeTerraces.Fade(BiomeTerraces.EdgeFadeMetres), Is.EqualTo(1f));
		}

		// ── Terraces: a field's plan ──────────────────────────────────

		private static readonly Vector2 FieldCentre = new Vector2(64f, 58f);
		private static readonly Vector2 FieldSize = new Vector2(44f, 30f);
		private const float FieldYaw = 17f;

		private static Func<float, float, float> Slope(float degrees, float gx = 0.3f, float gz = 1f)
		{
			var dir = new Vector2(gx, gz).normalized;
			float tan = Mathf.Tan(degrees * Mathf.Deg2Rad);
			return (x, z) => 120f + tan * (dir.x * x + dir.y * z);
		}

		[Test]
		public void Plan_OnASlope_CutsLevelBenchesAlongTheContour_AndLeavesTheGroundRoundIt()
		{
			Func<float, float, float> ground = Slope(15f);
			PointOfInterestFieldTerraces.Plan plan = PointOfInterestFieldTerraces.Make(FieldCentre, FieldYaw, FieldSize, BiomeTerraceRiser.Drystone, false, ground);
			Assert.That(plan.Terraced, Is.True);
			Assert.That(plan.Slope, Is.EqualTo(15f).Within(0.2f), "a plane's smoothed slope is its own");
			Assert.That(plan.Rise, Is.EqualTo(BiomeTerraces.RiserHeight(BiomeTerraceRiser.Drystone, plan.Slope)).Within(1e-4f));
			Assert.That(plan.Base, Is.EqualTo(ground(FieldCentre.x, FieldCentre.y)).Within(1e-3f));
			double moved = 0.0;
			int core = 0;
			for (float x = FieldCentre.x - 40f; x <= FieldCentre.x + 40f; x += 0.7f)
			{
				for (float z = FieldCentre.y - 40f; z <= FieldCentre.y + 40f; z += 0.7f)
				{
					float g = ground(x, z);
					float offset = plan.Offset(x, z, g);
					float inside = plan.Inside(x, z);
					if (inside <= 0f)
					{
						Assert.That(offset, Is.EqualTo(0f), "nothing outside the field");
						continue;
					}
					Assert.That(Mathf.Abs(offset), Is.LessThanOrEqualTo(plan.Rise), "never more than a step of cut or fill");
					if (inside < BiomeTerraces.EdgeFadeMetres)
					{
						continue;
					}
					core++;
					moved += offset;
					float u = plan.U(x, z);
					float t = BiomeTerraces.Phase(u);
					if (t < 1f - plan.Share - 0.02f)
					{
						Assert.That(g + offset, Is.EqualTo(plan.Level(BiomeTerraces.BandOf(u))).Within(2e-3f), "a level bench");
					}
				}
			}
			Assert.That(core, Is.GreaterThan(500));
			Assert.That(moved / core, Is.EqualTo(0.0).Within(0.15 * plan.Rise), "cut and fill about balanced");
			// The steps' walls run along the contour, on the steps, inside the field.
			List<(int Band, List<Vector2> Line)> risers = PointOfInterestFieldTerraces.RiserLines(plan);
			Assert.That(risers.Count, Is.GreaterThanOrEqualTo(3));
			foreach ((int band, List<Vector2> line) in risers)
			{
				foreach (Vector2 p in line)
				{
					Assert.That(plan.Inside(p.x, p.y), Is.GreaterThanOrEqualTo(PointOfInterestFieldTerraces.WallInsideMetres - 1e-3f));
					Assert.That(ground(p.x, p.y), Is.EqualTo(plan.Base + plan.Rise * (band + 1f - 0.5f * plan.Share)).Within(0.02f), "on its step's middle");
				}
			}
			foreach ((int band, List<Vector2> line) in PointOfInterestFieldTerraces.TreadLines(plan, 3f))
			{
				foreach (Vector2 p in line)
				{
					Assert.That(BiomeTerraces.Phase(plan.U(p.x, p.y)), Is.EqualTo(0.5f * (1f - plan.Share)).Within(0.01f), "mid-bench");
				}
			}
		}

		[Test]
		public void Plan_OnGentleGround_LiesFlat_AndIsTheSameEveryTime()
		{
			PointOfInterestFieldTerraces.Plan gentle = PointOfInterestFieldTerraces.Make(FieldCentre, FieldYaw, FieldSize, BiomeTerraceRiser.Drystone, false, Slope(2f));
			Assert.That(gentle.Terraced, Is.False);
			Assert.That(gentle.Offset(FieldCentre.x, FieldCentre.y, 120f), Is.EqualTo(0f));
			Assert.That(PointOfInterestFieldTerraces.RiserLines(gentle), Is.Empty);
			PointOfInterestFieldTerraces.Plan none = PointOfInterestFieldTerraces.Make(FieldCentre, FieldYaw, FieldSize, BiomeTerraceRiser.None, false, Slope(15f));
			Assert.That(none.Terraced, Is.False, "no riser, no terraces");
			Func<float, float, float> hill = (x, z) => 80f + 0.25f * z + 3f * Mathf.Sin(0.05f * x) + 0.4f * Mathf.Sin(0.31f * x + 0.7f * z);
			PointOfInterestFieldTerraces.Plan a = PointOfInterestFieldTerraces.Make(FieldCentre, FieldYaw, FieldSize, BiomeTerraceRiser.GrassBank, false, hill);
			PointOfInterestFieldTerraces.Plan b = PointOfInterestFieldTerraces.Make(FieldCentre, FieldYaw, FieldSize, BiomeTerraceRiser.GrassBank, false, hill);
			Assert.That(a.Smooth, Is.EqualTo(b.Smooth));
			Assert.That((a.Base, a.Rise, a.Share), Is.EqualTo((b.Base, b.Rise, b.Share)));
			Assert.That(a.Origin.x % a.Cell, Is.EqualTo(0f).Within(1e-4f), "on the scene's own lattice");
			Func<float, float, float> missing = (x, z) => x > FieldCentre.x + 10f ? float.NaN : 100f + 0.3f * z;
			Assert.That(PointOfInterestFieldTerraces.Make(FieldCentre, FieldYaw, FieldSize, BiomeTerraceRiser.Drystone, false, missing).Terraced, Is.False, "no ground, no terraces");
		}

		[Test]
		public void TerracedPaddies_FloodEachBench_AndTheirSheetShowsNowhereOffItsFloor()
		{
			foreach (Func<float, float, float> ground in new[] { Slope(12f), (x, z) => 50f + 0.22f * z + 0.002f * (x - 64f) * (x - 64f) })
			{
				PointOfInterestFieldTerraces.Plan plan = PointOfInterestFieldTerraces.Make(FieldCentre, FieldYaw, FieldSize, BiomeTerraceRiser.EarthBund, true, ground);
				Assert.That(plan.Terraced, Is.True);
				List<SceneHydrology.Lake> lakes = PointOfInterestFieldTerraces.TreadLakes(plan, piece => PointOfInterestFieldTerraces.LakeId(PointOfInterestFieldTerraces.TreadLakeKind, 7, 2, piece));
				Assert.That(lakes.Count, Is.GreaterThanOrEqualTo(2), "a lake a bench");
				float Final(float x, float z) => ground(x, z) + plan.Offset(x, z, ground(x, z));
				foreach (SceneHydrology.Lake lake in lakes)
				{
					Assert.That(PointOfInterestPaddies.IsPaddy(lake.Id), Is.True);
					Assert.That(lake.Covers(lake.Seeds[0].x, lake.Seeds[0].y), Is.True);
					int band = BiomeTerraces.BandOf(plan.U(lake.Seeds[0].x, lake.Seeds[0].y));
					Assert.That(lake.Level, Is.EqualTo(plan.Level(band)));
					for (int z = -1; z <= lake.MaskHeight; z++)
					{
						for (int x = -1; x <= lake.MaskWidth; x++)
						{
							float cx = lake.MaskOrigin.x + (x + 0.5f) * lake.MaskCell, cz = lake.MaskOrigin.y + (z + 0.5f) * lake.MaskCell;
							bool near = false;
							for (int dz = -1; dz <= 1 && !near; dz++)
							{
								for (int dx = -1; dx <= 1 && !near; dx++)
								{
									near = lake.Covers(cx + dx * lake.MaskCell, cz + dz * lake.MaskCell);
								}
							}
							if (!near)
							{
								continue;
							}
							for (int s = 0; s < 16; s++)
							{
								float px = cx + ((s & 3) - 1.5f) * lake.MaskCell / 4f, pz = cz + ((s >> 2) - 1.5f) * lake.MaskCell / 4f;
								float u = plan.U(px, pz);
								float t = BiomeTerraces.Phase(u);
								bool sameBench = BiomeTerraces.BandOf(u) == band && t <= 1f - plan.Share;
								float inner = BiomeTerraces.PaddyInner(t, plan.Share, plan.PeriodAt(px, pz), plan.Inside(px, pz));
								if (sameBench && inner > -BiomeTerraces.PaddyFace)
								{
									continue;
								}
								Assert.That(Final(px, pz), Is.GreaterThanOrEqualTo(lake.Level - 1e-3f), $"the water's sheet shows off its floor at ({px:0.0}, {pz:0.0})");
							}
						}
					}
				}
			}
		}

		[Test]
		public void TerraceCarve_RestoreCarve_IsBitForBit()
		{
			const int res = 129;
			var tileSize = new Vector2(256f, 256f);
			const float heightScale = 600f;
			Func<float, float, float> slope = Slope(14f);
			foreach (float quantum in new[] { 65535f, 32766f })
			{
				float Q(float h) => Mathf.Round(Mathf.Clamp01(h) * quantum) / quantum;
				var ground = new float[res, res];
				float step = tileSize.x / (res - 1);
				for (int j = 0; j < res; j++)
				{
					for (int i = 0; i < res; i++)
					{
						ground[j, i] = Q(slope(i * step, j * step) / heightScale + 0.0001f * Mathf.Sin(i * 0.37f) * Mathf.Cos(j * 0.23f));
					}
				}
				float Natural(float x, float z)
				{
					int i = Mathf.Clamp(Mathf.RoundToInt(x / step), 0, res - 1), j = Mathf.Clamp(Mathf.RoundToInt(z / step), 0, res - 1);
					return ground[j, i] * heightScale;
				}
				PointOfInterestFieldTerraces.Plan plan = PointOfInterestFieldTerraces.Make(FieldCentre, FieldYaw, FieldSize, BiomeTerraceRiser.Drystone, false, Natural);
				Assert.That(plan.Terraced, Is.True);
				var samples = new List<int>();
				var metres = new List<float>();
				Assert.That(PointOfInterestFieldCarve.PlanTo(plan.Area(), Vector2.zero, tileSize, res, (i, j) => ground[j, i] * heightScale, plan.Offset, samples, metres), Is.GreaterThan(100));
				Assert.That(metres.Exists(m => m < 0f) && metres.Exists(m => m > 0f), Is.True, "cut and filled");
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
				float[,] restored = (float[,])after.Clone();
				PointOfInterestFieldCarve.Lower(restored, x0, z0, res, samples, deltas);
				restored = Stored(restored);
				Assert.That(restored, Is.EqualTo(before), $"restored bit for bit ({quantum})");
				var whole = (float[,])ground.Clone();
				for (int j = 0; j < d; j++)
				{
					for (int i = 0; i < w; i++)
					{
						whole[z0 + j, x0 + i] = restored[j, i];
					}
				}
				float Restored(float x, float z)
				{
					int i = Mathf.Clamp(Mathf.RoundToInt(x / step), 0, res - 1), j = Mathf.Clamp(Mathf.RoundToInt(z / step), 0, res - 1);
					return whole[j, i] * heightScale;
				}
				PointOfInterestFieldTerraces.Plan again = PointOfInterestFieldTerraces.Make(FieldCentre, FieldYaw, FieldSize, BiomeTerraceRiser.Drystone, false, Restored);
				var samples2 = new List<int>();
				var metres2 = new List<float>();
				PointOfInterestFieldCarve.PlanTo(again.Area(), Vector2.zero, tileSize, res, (i, j) => whole[j, i] * heightScale, again.Offset, samples2, metres2);
				Assert.That(samples2, Is.EqualTo(samples), "the same benches from the restored ground");
				Assert.That(metres2, Is.EqualTo(metres));
			}
		}

		[Test]
		public void FieldLakeIds_AreThePaddiesButNeverAPaddysOrAChannels()
		{
			var seen = new HashSet<int>();
			foreach (int kind in new[] { PointOfInterestFieldTerraces.TreadLakeKind, PointOfInterestFieldTerraces.RillLakeKind })
			{
				foreach (int site in new[] { 0, 1, 4095, 4096 + 3 })
				{
					for (int field = 0; field < 16; field += 5)
					{
						for (int piece = 0; piece < 256; piece += 51)
						{
							int id = PointOfInterestFieldTerraces.LakeId(kind, site, field, piece);
							Assert.That(PointOfInterestPaddies.IsPaddy(id), Is.True, "cleared with the paddies and left out of the cut's water");
							Assert.That(PointOfInterestLinearFeatures.IsChannel(id), Is.False);
							Assert.That((id >> 24) & 0x7, Is.EqualTo(kind));
							seen.Add(id);
						}
					}
				}
			}
			for (int site = 0; site < 64; site++)
			{
				for (int field = 0; field < 16; field++)
				{
					Assert.That(seen.Contains(PointOfInterestPaddies.LakeId(site, field)), Is.False, "never a paddy's own id");
					Assert.That((PointOfInterestPaddies.LakeId(site, field) >> 24) & 0x7, Is.EqualTo(0));
				}
			}
		}

		// ── Crops: rows, hills, beds ──────────────────────────────────

		[Test]
		public void CropShare_IsTheOldCoverageForOldFields_AndRowsHillsAndBedsTakeTheirShare()
		{
			var size = new Vector2(48f, 30f);
			for (float u = -20f; u <= 20f; u += 1.3f)
			{
				for (float v = -12f; v <= 12f; v += 0.9f)
				{
					foreach (BiomeFieldSurface surface in new[] { BiomeFieldSurface.Flat, BiomeFieldSurface.LazyBed, BiomeFieldSurface.RidgeAndFurrow })
					{
						int old = PointOfInterestFields.SownCoverage(surface, size, u, v, 1f);
						int now = Mathf.RoundToInt(PointOfInterestFields.WheatCoverage * PointOfInterestFields.CropShare(surface, size, u, v, 1f, 0f, null, default));
						Assert.That(now, Is.EqualTo(old), $"{surface} ({u}, {v})");
					}
				}
			}
			// Rows: about their share of the field, none between.
			double rows = 0.0;
			int n = 0;
			for (float a = 0f; a < 36f; a += 0.05f)
			{
				rows += PointOfInterestFields.RowCover(a, 1.8f, 0.05f);
				n++;
			}
			Assert.That(rows / n, Is.EqualTo(PointOfInterestFields.RowShare).Within(0.02));
			Assert.That(PointOfInterestFields.RowCover(0.9f, 1.8f, 0.2f), Is.EqualTo(1f), "a row's middle");
			Assert.That(PointOfInterestFields.RowCover(0.02f, 1.8f, 0.02f), Is.EqualTo(0f), "between rows");
			Assert.That(PointOfInterestFields.RowCover(5f, 0f, 1f), Is.EqualTo(1f), "no rows: edge to edge");
			// Corn hills: a disc a hill on a 1.3 m lattice.
			double hills = 0.0;
			n = 0;
			for (float u = 0f; u < 13f; u += 0.13f)
			{
				for (float v = 0f; v < 13f; v += 0.13f)
				{
					hills += BiomeFieldSurfaces.HillShare(u, v, 0.13f);
					n++;
				}
			}
			float disc = Mathf.PI * BiomeFieldSurfaces.CornHillRadius * BiomeFieldSurfaces.CornHillRadius / (BiomeFieldSurfaces.CornHillSpacing * BiomeFieldSurfaces.CornHillSpacing);
			Assert.That(hills / n, Is.EqualTo(disc).Within(0.03));
			Assert.That(BiomeFieldSurfaces.HillShare(0f, 0f, 0.2f), Is.EqualTo(1f), "a hill on the field's middle");
			// Raised beds: across as ridges (1.2 m beds, 0.5 m paths), along broken every 6.5 m.
			Assert.That(BiomeFieldSurfaces.Ridged(BiomeFieldSurface.RaisedBeds) && !BiomeFieldSurfaces.Carved(BiomeFieldSurface.RaisedBeds), Is.True);
			Assert.That(BiomeFieldSurfaces.BedShareAlong(BiomeFieldSurface.RaisedBeds, 2f, 3f), Is.EqualTo(1f));
			Assert.That(BiomeFieldSurfaces.BedShareAlong(BiomeFieldSurface.RaisedBeds, 6.1f, 6.4f), Is.EqualTo(0f), "a cross path");
			Assert.That(BiomeFieldSurfaces.BedShareAlong(BiomeFieldSurface.Flat, 6.1f, 6.4f), Is.EqualTo(1f));
			Assert.That(BiomeFieldSurfaces.CrossPaths(BiomeFieldSurface.RaisedBeds, 21f), Is.EqualTo(new[] { 6.25f, 12.75f, 19.25f }));
			Assert.That(BiomeFieldSurfaces.CrossPaths(BiomeFieldSurface.Flat, 21f), Is.Empty);
			Assert.That(BiomeFieldSurfaces.RaisedBedGarden.x, Is.GreaterThanOrEqualTo(BiomeFieldSurfaces.RaisedBedGarden.y));
		}

		[Test]
		public void BaulaturaAndMarcita_AreCarvedRidges_CornHillsAndRaisedBedsAreNot()
		{
			Assert.That(BiomeFieldSurfaces.Carved(BiomeFieldSurface.Baulatura) && BiomeFieldSurfaces.Carved(BiomeFieldSurface.Marcita), Is.True);
			Assert.That(BiomeFieldSurfaces.Carved(BiomeFieldSurface.CornHills) || BiomeFieldSurfaces.Ridged(BiomeFieldSurface.CornHills), Is.False);
			Assert.That(BiomeFieldSurfaces.Carved(BiomeFieldSurface.Terraced) || BiomeFieldSurfaces.Ridged(BiomeFieldSurface.Terraced), Is.False, "terraces carve their own way");
			Assert.That(BiomeFieldSurfaces.Nominal(BiomeFieldSurface.Baulatura).Period, Is.EqualTo(25.5f));
			Assert.That(BiomeFieldSurfaces.Height(BiomeFieldSurface.Baulatura, 51f, 12.75f), Is.EqualTo(0.6f).Within(1e-4f), "the crown");
			Assert.That(BiomeFieldSurfaces.Height(BiomeFieldSurface.Marcita, 36f, 4.5f), Is.EqualTo(0.3f).Within(1e-4f));
			Assert.That(PointOfInterestFieldKinds.Crowns(BiomeFieldSurface.Marcita, 36f), Is.EqualTo(new[] { 4.5f, 13.5f, 22.5f, 31.5f }));
			Assert.That(BiomeFieldSurfaces.Furrows(BiomeFieldSurface.Baulatura, 51f, out float ditch), Is.EqualTo(new[] { 25.5f }));
			Assert.That(ditch, Is.EqualTo(1.5f).Within(1e-4f));
		}

		// ── Orchards, gardens, rills ──────────────────────────────────

		[Test]
		public void OrchardRows_FitTheFieldAtTheirSpacing()
		{
			Assert.That(BiomeOrchards.Rows(30f, 6f), Is.EqualTo(new[] { -12f, -6f, 0f, 6f, 12f }));
			Assert.That(BiomeOrchards.Rows(4f, 6f), Is.EqualTo(new[] { 0f }), "one down the middle of a narrow field");
			List<List<Vector2>> rows = PointOfInterestFieldKinds.OrchardRows(FieldCentre, FieldYaw, FieldSize, 6f, 1.5f);
			Assert.That(rows.Count, Is.EqualTo(BiomeOrchards.Rows(FieldSize.y - 3f, 6f).Count));
			foreach (List<Vector2> row in rows)
			{
				foreach (Vector2 p in row)
				{
					Assert.That(PointOfInterestPaddies.Inside(FieldCentre, FieldYaw, FieldSize, p.x, p.y), Is.GreaterThanOrEqualTo(1.5f + 3f - 1e-3f), "half a spacing inside the headland");
				}
			}
			Assert.That(BiomeOrchards.SpacingOf(new BiomeOrchard(null, 1f)), Is.EqualTo(6f));
		}

		[Test]
		public void GardenBeds_AndBaulaturaBanks_AndContourYaw()
		{
			var garden = new Vector2(24f, 16f);
			List<List<Vector2>> edges = PointOfInterestFieldKinds.BedEdges(FieldCentre, 0f, garden, 1.5f);
			int beds = BiomeFieldSurfaces.RidgeCount(BiomeFieldSurface.RaisedBeds, 13f);
			Assert.That(beds, Is.EqualTo(8));
			// 21 m along: three full beds and a 1.5 m stub left out, two sides each.
			Assert.That(edges.Count, Is.EqualTo(beds * 3 * 2));
			foreach (List<Vector2> edge in edges)
			{
				Assert.That(PointOfInterestLinearFeatures.Length(edge), Is.InRange(2f, BiomeFieldSurfaces.BedLength + 1e-3f));
			}
			List<List<Vector2>> banks = PointOfInterestFieldKinds.AlongLines(FieldCentre, 0f, new Vector2(60f, 54f), 1.5f, new[] { 26.6f });
			Assert.That(banks.Count, Is.EqualTo(1));
			Assert.That(PointOfInterestLinearFeatures.Length(banks[0]), Is.EqualTo(60f - 3f - 2f * BiomeFieldSurfaces.RidgeEndMetres).Within(1e-3f));
			// A flight's long side runs along the contour: across the fall.
			foreach (Vector2 fall in new[] { new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(-0.6f, 0.8f) })
			{
				float yaw = PointOfInterestFieldKinds.ContourYaw(fall);
				PointOfInterestFieldKinds.Axes(yaw, out Vector2 axisX, out Vector2 axisZ);
				Assert.That(Vector2.Dot(axisX, fall.normalized), Is.EqualTo(0f).Within(1e-4f));
				Assert.That(Vector2.Dot(axisZ, fall.normalized), Is.EqualTo(1f).Within(1e-4f));
			}
		}

		// ── Layout: bottomland and centuriation ───────────────────────

		[Test]
		public void NearWater_FindsWaterWithinReach_AndNoFurther()
		{
			Func<float, float, bool> river = (x, z) => x >= 200f && x <= 215f;
			Assert.That(PointOfInterestFieldKinds.NearWater(river, new Vector2(100f, 0f), 120f), Is.True);
			Assert.That(PointOfInterestFieldKinds.NearWater(river, new Vector2(100f, 0f), 80f), Is.False);
			Assert.That(PointOfInterestFieldKinds.NearWater(river, new Vector2(205f, 0f), 10f), Is.True, "on it");
			Assert.That(PointOfInterestFieldKinds.NearWater(river, new Vector2(0f, 0f), 0f), Is.True, "no reach: anywhere");
		}

		[Test]
		public void Centuriation_SetsFieldsSquareInTheGridsCells_TheSameEveryTime()
		{
			const float spacing = 710f;
			float cell = BiomeCenturiation.CellOf(spacing);
			Assert.That(spacing / cell, Is.EqualTo(Mathf.Round(spacing / cell)).Within(1e-3f), "a whole number of cells a square");
			Assert.That(cell, Is.InRange(30f, 50f));
			foreach (int seed in new[] { 1, 77, -9000, 123456789 })
			{
				float gridYaw = BiomeCenturiation.GridYaw(seed);
				Assert.That(gridYaw, Is.InRange(0f, 90f));
				Vector2 origin = BiomeCenturiation.GridOrigin(seed, spacing);
				for (int k = 0; k < 40; k++)
				{
					var centre = new Vector2(37f * k - 500f, 23f * k - 300f);
					float yaw = 13f * k;
					var size = new Vector2(30f + k, 20f + k * 0.5f);
					BiomeCenturiation.Snap(centre, yaw, size, seed, spacing, out Vector2 c, out float y, out Vector2 s);
					BiomeCenturiation.Snap(centre, yaw, size, seed, spacing, out Vector2 c2, out float y2, out Vector2 s2);
					Assert.That((c2, y2, s2), Is.EqualTo((c, y, s)), "deterministic");
					float quarter = Mathf.DeltaAngle(gridYaw, y);
					Assert.That(Mathf.Abs(quarter) < 1e-3f || Mathf.Abs(Mathf.Abs(quarter) - 90f) < 1e-3f, Is.True, "square to the grid");
					PointOfInterestFieldKinds.Axes(gridYaw, out Vector2 ax, out Vector2 az);
					float a = Vector2.Dot(c - origin, ax) / cell, b = Vector2.Dot(c - origin, az) / cell;
					Assert.That(a - Mathf.Floor(a), Is.EqualTo(0.5f).Within(1e-3f), "a cell's middle");
					Assert.That(b - Mathf.Floor(b), Is.EqualTo(0.5f).Within(1e-3f));
					Assert.That(Mathf.Max(s.x, s.y), Is.LessThanOrEqualTo(cell - BiomeCenturiation.Gap + 1e-3f), "a lane's room between cells");
					Assert.That((c - centre).magnitude, Is.LessThanOrEqualTo(cell * 0.71f + 1e-3f), "in the cell it lay in");
				}
			}
			Assert.That(BiomeCenturiation.GridYaw(1), Is.Not.EqualTo(BiomeCenturiation.GridYaw(2)), "each scene its own turn");
		}

		// ── Channel margins ───────────────────────────────────────────

		[Test]
		public void ChannelMargin_WeightsBlendsAndRuns()
		{
			Assert.That(BiomeChannelMargin.Weight(0.3f, 0.7f, 3f), Is.EqualTo(1f), "in the channel");
			Assert.That(BiomeChannelMargin.Weight(0.7f + 1.5f, 0.7f, 3f), Is.EqualTo(1f), "the first two thirds of the margin");
			Assert.That(BiomeChannelMargin.Weight(0.7f + 3f, 0.7f, 3f), Is.EqualTo(0f).Within(1e-6f), "none past it");
			Assert.That(BiomeChannelMargin.Weight(0.7f + 2.5f, 0.7f, 3f), Is.InRange(0f, 1f));
			Assert.That(BiomeChannelMargin.Weight(0f, 0.7f, 0f), Is.EqualTo(0f));
			var weights = new[] { 0.5f, 0.3f, 0.2f, 0f };
			ChannelMarginPaint.Blend(weights, 3, 0.6f);
			Assert.That(weights[0] + weights[1] + weights[2] + weights[3], Is.EqualTo(1f).Within(1e-5f), "still summing to one");
			Assert.That(weights[3], Is.EqualTo(0.6f).Within(1e-5f));
			Assert.That(weights[0] / weights[1], Is.EqualTo(0.5f / 0.3f).Within(1e-4f), "the others in proportion");
			ChannelMarginPaint.Blend(weights, 3, 0f);
			Assert.That(weights[3], Is.EqualTo(0.6f).Within(1e-5f), "no share, no change");
			List<List<Vector2>> runs = ChannelMarginPaint.Runs(new List<(Vector2, bool)>
			{
				(new Vector2(0, 0), true), (new Vector2(2, 0), true), (new Vector2(4, 0), false), (new Vector2(6, 0), true),
				(new Vector2(8, 0), true), (new Vector2(10, 0), true), (new Vector2(12, 0), false), (new Vector2(14, 0), true),
			});
			Assert.That(runs.Count, Is.EqualTo(2), "flumes and culverts break it; a lone station is no run");
			Assert.That(runs[1].Count, Is.EqualTo(3));
			// The buckets find what a search of every segment finds.
			var lines = new List<(List<Vector2> Points, float Half)>
			{
				(new List<Vector2> { new Vector2(0f, 0f), new Vector2(30f, 5f), new Vector2(60f, -10f) }, 0.7f),
				(new List<Vector2> { new Vector2(10f, 40f), new Vector2(12f, 80f) }, 1f),
			};
			Dictionary<long, List<(int Line, int Segment)>> buckets = ChannelMarginPaint.Buckets(lines, 4f);
			for (float x = -10f; x <= 70f; x += 0.83f)
			{
				for (float z = -20f; z <= 90f; z += 0.91f)
				{
					var p = new Vector2(x, z);
					float brute = 0f;
					foreach ((List<Vector2> points, float half) in lines)
					{
						for (int s = 0; s + 1 < points.Count; s++)
						{
							brute = Mathf.Max(brute, BiomeChannelMargin.Weight(PointOfInterestLinearFeatures.HedgeDistance(p, points[s], points[s + 1], out _), half, 4f));
						}
					}
					Assert.That(ChannelMarginPaint.Share(p, lines, buckets, 4f), Is.EqualTo(brute).Within(1e-6f), $"({x}, {z})");
				}
			}
		}

		// ── Wiring ────────────────────────────────────────────────────

		[Test]
		public void Wiring_LayoutBeforeTheFit_TerracesBeforeTheCrop_OldTerracesAfterTheFields_MarginsAfterTheSplat()
		{
			string fields = CodeOnly(Read(FieldsSource));
			int snap = fields.IndexOf("BiomeCenturiation.Snap(", StringComparison.Ordinal);
			int near = fields.IndexOf("!NearWater(context, field.Centre, layout.Bottomland)", StringComparison.Ordinal);
			int terraced = fields.IndexOf("field.Terraced = TerracedAt(context, field, fields.Count);", StringComparison.Ordinal);
			int fits = fields.IndexOf("if (Fits(context, field, fields, ways))", StringComparison.Ordinal);
			Assert.That(snap, Is.GreaterThan(fields.IndexOf("double roll = random.NextDouble();", StringComparison.Ordinal)), "after every draw");
			Assert.That(near, Is.GreaterThan(snap).And.LessThan(fits));
			Assert.That(terraced, Is.GreaterThan(near).And.LessThan(fits));
			int fit = fields.IndexOf("PointOfInterestFieldClaims.Fit(context.FieldClaims, record.Id, drawn.Centre, drawn.Yaw, drawn.Size, out Vector2 fitted)", StringComparison.Ordinal);
			int garden = fields.IndexOf("fitted = GardenSize(context, drawn, f, fitted);", StringComparison.Ordinal);
			Assert.That(garden, Is.GreaterThan(fit).And.LessThan(fields.IndexOf("context.FieldClaims?.Add(record.Id, field.Centre, field.Yaw, field.Size);", StringComparison.Ordinal)));
			int benches = fields.IndexOf("carved += PointOfInterestFieldTerraces.Carve(context, plan);", StringComparison.Ordinal);
			int paint = fields.IndexOf("PaintDetails(terrains, field, crop, biome, surface);", StringComparison.Ordinal);
			Assert.That(benches, Is.GreaterThan(0).And.LessThan(paint), "carved before it is sown");
			Assert.That(fields.IndexOf("PointOfInterestFieldKinds.LayOrchard(", StringComparison.Ordinal), Is.GreaterThan(paint), "the trees on the carved ground");
			StringAssert.Contains("grown.AddRange(orchards.ConvertAll(BiomeOrchards.AsCrop));", fields);
			int flights = fields.IndexOf("PointOfInterestFieldKinds.LayAbandonedTerraces(context, ways, ref carved, ref pieces);", StringComparison.Ordinal);
			Assert.That(flights, Is.GreaterThan(fields.LastIndexOf("Fence(context, field, site, abandoned, ", StringComparison.Ordinal)), "after every field and fence");
			StringAssert.Contains("dry: true);", fields.Substring(fields.IndexOf("if (!flooded && benched)", StringComparison.Ordinal), 400));
			string kinds = CodeOnly(Read(World + "/PointsOfInterest/Features/PointOfInterestFieldKinds.cs")) + CodeOnly(Read(World + "/PointsOfInterest/Features/PointOfInterestFieldTerraces.cs"));
			Assert.That(kinds.IndexOf("Random.", StringComparison.Ordinal), Is.LessThan(0), "never a site's sequence");
			string generator = CodeOnly(Read(GeneratorSource));
			int splat = generator.IndexOf("BiomeSplatPainter.Paint(tiles, palette, field, options);", StringComparison.Ordinal);
			int margin = generator.IndexOf("ChannelMarginPaint.Paint(scene, tiles, palette, result.Notes);", StringComparison.Ordinal);
			Assert.That(margin, Is.GreaterThan(splat).And.LessThan(generator.IndexOf("TerrainArraySetup.Apply(", StringComparison.Ordinal)), "before the arrays and the scatter read the ground");
			string linear = CodeOnly(Read(LinearSource));
			int clear = linear.IndexOf("ChannelMarginPaint.Clear(scene);", StringComparison.Ordinal);
			Assert.That(clear, Is.GreaterThan(0).And.LessThan(linear.IndexOf("if (!AnyIn(biomes))", StringComparison.Ordinal)), "cleared before the fast path");
			StringAssert.Contains("ChannelMarginPaint.Record(context, BiomeRegistry.IDOf(biome), half,", CodeOnly(Read(ChannelsSource)));
			StringAssert.Contains("AuthorFieldKinds(biome, entry, source, report, apply);", Read(AuthoringSource));
			StringAssert.Contains("entry.FieldRiser(name), entry.FieldRows(name)", Read(AuthoringSource));
			string reader = Read(ReaderSource);
			foreach (string signature in new[]
			{
				"public static List<BiomeOrchard> Orchards(BiomeTemplate biome)", "public static BiomeFieldLayout FieldLayout(BiomeTemplate biome)",
				"public static BiomeAbandonedTerraces AbandonedTerraces(BiomeTemplate biome)", "public static BiomeChannelMargin ChannelMargin(BiomeTemplate biome)",
			})
			{
				StringAssert.Contains(signature, reader);
			}
		}
	}
}
