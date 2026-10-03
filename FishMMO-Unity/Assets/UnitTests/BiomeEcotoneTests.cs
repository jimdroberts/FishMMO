using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration.Editor;
using FishMMO.Shared.WorldDesign;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Biome borders are ecotones, not contour lines: <see cref="PlanetClimateField"/> chooses a biome
	/// from a copy of the point moved by a coherent, deterministic, zero-mean field
	/// (<see cref="PlanetClimateField.SelectionPoint"/>), while the climate it reports, and every
	/// decision about where a liquid stands, keep the real values.
	/// </summary>
	/// <remarks>
	/// The regression these guard: the resolver picks exactly one biome from a height tier and a
	/// climate envelope, so round a massif the globe drew glacier, scree and rock as concentric rings
	/// at the exact tier altitudes, and elsewhere biomes met along hard, smooth curves.
	/// </remarks>
	[TestFixture]
	public class BiomeEcotoneTests
	{
		private const int Points = 20000;

		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void DestroyCreated()
		{
			foreach (Object asset in created)
			{
				if (asset is BiomeTemplate biome)
				{
					BiomeRegistry.Unregister(biome);
				}
				Object.DestroyImmediate(asset);
			}
			created.Clear();
		}

		private WorldBody MakeBody(uint seed)
		{
			var body = ScriptableObject.CreateInstance<WorldBody>();
			body.Atmosphere = AtmosphereKind.Standard;
			body.Water = 0.7f;
			body.SkyRadiusKm = PlanetSurface.EarthRadiusKm;
			body.RotationHours = 24f;
			body.TerrainSeed = seed;
			created.Add(body);
			return body;
		}

		private BiomeTemplate MakeBiome(string name, int tier, float minT, float maxT, float minH, float maxH)
		{
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			biome.name = name;
			biome.DisplayName = name;
			biome.ElevationTier = tier;
			biome.MinTemperature = minT;
			biome.MaxTemperature = maxT;
			biome.MinHumidity = minH;
			biome.MaxHumidity = maxH;
			biome.SelectionWeight = 1f;
			created.Add(biome);
			return biome;
		}

		/// <summary>An honest lowland point: 700 m up, temperate, middling humidity.</summary>
		private static PlanetSurfacePoint LowlandPoint(PlanetClimateField field, float temperature, float humidity)
		{
			const float Altitude = 700f;
			float normalized = field.HeightOfAltitude(Altitude);
			return new PlanetSurfacePoint
			{
				Height = 0.6f,
				AltitudeMetres = Altitude,
				NormalizedHeight = normalized,
				Climate = new ClimateSample
				{
					Temperature = temperature,
					Humidity = humidity,
					ElevationTier = ClimateSettings.TierForHeight(normalized, field.Parameters.ElevationBoundaries),
				},
			};
		}

		// ── The noise ────────────────────────────────────────────────

		[Test]
		public void TheSelectionNoise_IsDeterministic_ZeroMean_AndOnTheScale()
		{
			PlanetClimateField a = PlanetClimateField.For(null, null);
			PlanetClimateField b = PlanetClimateField.For(null, null);
			double[] sum = new double[3], squares = new double[3];
			for (int i = 0; i < Points; i++)
			{
				Vector3 d = PlanetSurface.FibonacciDirection(i, Points);
				a.SelectionNoise(d, out float h1, out float t1, out float w1);
				b.SelectionNoise(d, out float h2, out float t2, out float w2);
				Assert.AreEqual(h1, h2, $"height noise at point {i} is a pure function of the seed and direction");
				Assert.AreEqual(t1, t2, $"temperature noise at point {i}");
				Assert.AreEqual(w1, w2, $"humidity noise at point {i}");
				float[] values = { h1, t1, w1 };
				for (int c = 0; c < 3; c++)
				{
					Assert.That(values[c], Is.InRange(-1f, 1f));
					sum[c] += values[c];
					squares[c] += values[c] * values[c];
				}
			}
			for (int c = 0; c < 3; c++)
			{
				double mean = sum[c] / Points;
				double deviation = Math.Sqrt(squares[c] / Points - mean * mean);
				Assert.Less(Math.Abs(mean), 0.03, $"channel {c} is zero-mean, so coverage shares barely move (mean {mean:0.0000})");
				Assert.That(deviation, Is.InRange(0.3, 0.7), $"channel {c} uses its range without living at the clamp (sd {deviation:0.000}; measured 0.48)");
			}
		}

		[Test]
		public void TheThreeChannels_AreIndependent_AndEachBodyHasItsOwn()
		{
			PlanetClimateField earth = PlanetClimateField.For(null, null);
			PlanetClimateField other = PlanetClimateField.For(null, MakeBody(98765u));
			int sameChannel = 0, sameBody = 0;
			for (int i = 0; i < 2000; i++)
			{
				Vector3 d = PlanetSurface.FibonacciDirection(i, 2000);
				earth.SelectionNoise(d, out float h, out float t, out float w);
				other.SelectionNoise(d, out float oh, out _, out _);
				if (Mathf.Abs(h - t) < 1e-4f || Mathf.Abs(t - w) < 1e-4f)
				{
					sameChannel++;
				}
				if (Mathf.Abs(h - oh) < 1e-4f)
				{
					sameBody++;
				}
			}
			Assert.Less(sameChannel, 100, "height, temperature and humidity borders bend independently, so they interlock");
			Assert.Less(sameBody, 100, "the field is seeded from the body's terrain seed");
		}

		[Test]
		public void TheNoise_IsCoherent_NotPixelNoise()
		{
			// Neighbouring globe pixels (about 0.003 rad apart on a 2048-wide bake) see nearly the same
			// noise: the borders meander, they do not speckle.
			PlanetClimateField field = PlanetClimateField.For(null, null);
			const float Step = 2f * Mathf.PI / 2048f;
			double total = 0;
			for (int i = 0; i < 2000; i++)
			{
				Vector3 d = PlanetSurface.FibonacciDirection(i, 2000);
				Vector3 side = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
				field.SelectionNoise(d, out float a, out _, out _);
				field.SelectionNoise((d + side * Step).normalized, out float b, out _, out _);
				total += Mathf.Abs(a - b);
			}
			// Measured 0.11 (the finest octave's cells are about 3 pixels); independent noise at this
			// spread would move about 0.54 between neighbours.
			Assert.Less(total / 2000, 0.2, "one pixel moves the noise by a small fraction of its range on average");
		}

		// ── The moved point ──────────────────────────────────────────

		[Test]
		public void TheMovedAltitude_NeverCrossesTheWaterLine_AndStaysWithinItsReach()
		{
			PlanetClimateField field = PlanetClimateField.For(null, null);
			float earthScale = Mathf.Max(1e-3f, field.ReliefMetres / PlanetSurface.EarthReliefMetres);
			float[] altitudes = { -10911f, -4000f, -200f, -1f, -0.01f, 0.01f, 1f, 29f, 31f, 1500f, 4500f, 8848f, 12000f };
			for (int i = 0; i < 2000; i++)
			{
				Vector3 d = PlanetSurface.FibonacciDirection(i, 2000);
				foreach (float altitude in altitudes)
				{
					PlanetSurfacePoint honest = field.At(d, altitude);
					PlanetSurfacePoint moved = field.SelectionPoint(d, honest);
					Assert.AreEqual(honest.UnderWater, moved.UnderWater, $"{altitude} m at point {i}: a biome is never chosen from the other side of the shore");
					float reach = Mathf.Min(PlanetClimateField.SelectionHeightFraction * Mathf.Abs(altitude), PlanetClimateField.SelectionHeightMetres * earthScale);
					Assert.LessOrEqual(Mathf.Abs(moved.AltitudeMetres - altitude), reach + 1e-3f, $"{altitude} m at point {i}");
					Assert.AreEqual(ClimateSettings.TierForHeight(moved.NormalizedHeight, field.Parameters.ElevationBoundaries), moved.Climate.ElevationTier,
						"the tier is recomputed from the moved height");
				}
			}
		}

		[Test]
		public void TheMovedClimate_StaysOnTheScale_AndAReadingPinnedAtACornerStaysThere()
		{
			PlanetClimateField field = PlanetClimateField.For(null, null);
			for (int i = 0; i < 2000; i++)
			{
				Vector3 d = PlanetSurface.FibonacciDirection(i, 2000);
				PlanetSurfacePoint corner = field.SelectionPoint(d, LowlandPoint(field, -1f, -1f));
				Assert.AreEqual(-1f, corner.Climate.Temperature, "an airless or frost-line world's readings keep choosing by weight, as before");
				Assert.AreEqual(-1f, corner.Climate.Humidity);
				PlanetSurfacePoint hot = field.SelectionPoint(d, LowlandPoint(field, 1f, 1f));
				Assert.AreEqual(1f, hot.Climate.Temperature);
				Assert.AreEqual(1f, hot.Climate.Humidity);

				PlanetSurfacePoint middle = field.SelectionPoint(d, LowlandPoint(field, 0.2f, 0.1f));
				Assert.LessOrEqual(Mathf.Abs(middle.Climate.Temperature - 0.2f), PlanetClimateField.SelectionTemperature + 1e-6f);
				Assert.LessOrEqual(Mathf.Abs(middle.Climate.Humidity - 0.1f), PlanetClimateField.SelectionHumidity + 1e-6f);

				PlanetSurfacePoint edge = field.SelectionPoint(d, LowlandPoint(field, 0.98f, -0.97f));
				Assert.That(edge.Climate.Temperature, Is.InRange(-1f, 1f), "tapered near the end of the scale, so it needs no clamp");
				Assert.That(edge.Climate.Humidity, Is.InRange(-1f, 1f));
			}
		}

		[Test]
		public void TheReportedClimate_IsTheHonestOne()
		{
			// Weather, exposure and naming read the point BiomeAt hands back; only the choice moves.
			PlanetClimateField field = PlanetClimateField.For(null, null);
			float[] altitudes = { -3000f, 5f, 600f, 2400f, 5200f };
			for (int i = 0; i < 1000; i++)
			{
				Vector3 d = PlanetSurface.FibonacciDirection(i, 1000);
				foreach (float altitude in altitudes)
				{
					PlanetSurfacePoint expected = field.At(d, altitude);
					field.BiomeAt(d, altitude, out PlanetSurfacePoint reported);
					Assert.AreEqual(expected.AltitudeMetres, reported.AltitudeMetres);
					Assert.AreEqual(expected.NormalizedHeight, reported.NormalizedHeight);
					Assert.AreEqual(expected.Climate.Temperature, reported.Climate.Temperature);
					Assert.AreEqual(expected.Climate.Humidity, reported.Climate.Humidity);
					Assert.AreEqual(expected.Climate.ElevationTier, reported.Climate.ElevationTier);
				}
				PlanetSurface.LatLong(d, out double latitude, out double longitude);
				PlanetSurfacePoint globe = field.At(latitude, longitude);
				field.BiomeAt(latitude, longitude, out PlanetSurfacePoint globeReported);
				Assert.AreEqual(globe.Climate.Temperature, globeReported.Climate.Temperature);
				Assert.AreEqual(globe.Climate.Humidity, globeReported.Climate.Humidity);
				Assert.AreEqual(globe.NormalizedHeight, globeReported.NormalizedHeight);
			}
		}

		// ── The choice ───────────────────────────────────────────────

		[Test]
		public void APointFarFromAnyBoundary_KeepsItsBiome_AndAPointOnOneIsShared()
		{
			BiomeRegistry.Clear();
			BiomeTemplate temperate = MakeBiome("Ecotone Temperate", 4, -0.2f, 0.6f, -0.3f, 0.5f);
			BiomeTemplate warm = MakeBiome("Ecotone Warm", 4, 0.6f, 1f, -1f, 1f);
			BiomeTemplate cold = MakeBiome("Ecotone Cold", 4, -1f, -0.2f, -1f, 1f);
			BiomeTemplate upland = MakeBiome("Ecotone Upland", 5, -1f, 1f, -1f, 1f);
			BiomeRegistry.Register(temperate);
			BiomeRegistry.Register(warm);
			BiomeRegistry.Register(cold);
			BiomeRegistry.Register(upland);
			try
			{
				PlanetClimateField field = PlanetClimateField.For(null, null);
				PlanetSurfacePoint central = LowlandPoint(field, 0.2f, 0.1f);
				PlanetSurfacePoint border = LowlandPoint(field, 0.6f, 0.1f);
				int borderTemperate = 0, borderWarm = 0;
				for (int i = 0; i < 5000; i++)
				{
					Vector3 d = PlanetSurface.FibonacciDirection(i, 5000);
					Assert.AreSame(temperate, field.SelectBiome(d, central),
						$"point {i}: 700 m is far inside the lowland tier and (0.2, 0.1) far inside the envelope, so no move can change it");
					BiomeTemplate chosen = field.SelectBiome(d, border);
					if (chosen == temperate)
					{
						borderTemperate++;
					}
					else if (chosen == warm)
					{
						borderWarm++;
					}
				}
				Assert.Greater(borderTemperate, 500, "on the isotherm between them, each biome takes a share of the ground");
				Assert.Greater(borderWarm, 500, "so the border meanders instead of running along the isotherm");
			}
			finally
			{
				NamingTemplateEditorLoader.Reload();
			}
		}

		// ── Liquids stay on real values ──────────────────────────────

		private const string BakerPath = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PlanetSurfaceBaker.cs";

		private static readonly string[] LiquidSources =
		{
			BakerPath,
			"Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/SceneGenerator.cs",
			"Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/SceneGeneration.cs",
			"Assets/Scripts/Shared/Implementation/Tools/Biomes/SurfaceLiquid.cs",
		};

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			Assert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The source with comment lines removed, so prose about a construct does not trip a scan for it.</summary>
		private static string CodeOnly(string source)
		{
			var code = new StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
					trimmed.StartsWith("/*", StringComparison.Ordinal) ||
					trimmed.StartsWith("*", StringComparison.Ordinal) ||
					trimmed.StartsWith("///", StringComparison.Ordinal))
				{
					continue;
				}
				code.Append(line).Append('\n');
			}
			return code.ToString();
		}

		[Test]
		public void TheGlobesSeaAndLava_AreDecidedFromTheSurfaceField_NeverFromTheSelection()
		{
			// The scene generator stands sea and lava at the real levels; the globe's shores must agree.
			string baker = CodeOnly(ReadSource(BakerPath));
			StringAssert.Contains("float h = rowHeights[x];", baker, "pass 2 reads the raw surface height the bake sampled");
			StringAssert.Contains("float altitude = rowAltitudes[x];", baker, "and the altitude AltitudeFromHeight gave it");
			StringAssert.Contains("Classify(hasLava, lavaLevelMetres, hasOcean, profile.SeaLevel, h, altitude)", baker,
				"sea and lava are classified from exactly those");
			foreach (string path in LiquidSources)
			{
				string code = CodeOnly(ReadSource(path));
				StringAssert.DoesNotContain("SelectionPoint(", code, $"{path}: liquids never read a moved height");
				StringAssert.DoesNotContain("SelectionNoise(", code, $"{path}: liquids never read the selection noise");
			}
		}

		[Test]
		public void ThePerturbation_NeverChangesWhichGlobePixelsAreSeaOrLava()
		{
			// Over the whole sphere, with lava levels across the world's range: the bake classifies
			// every pixel from the surface field's own height and altitude, which is exactly what the
			// honest point carries, and the point the biome is chosen from stays on its side of the sea.
			PlanetClimateField field = PlanetClimateField.For(null, null);
			float[] lavaLevels = { -2000f, 0f, 600f };
			int lavaWouldMove = 0;
			for (int i = 0; i < Points; i++)
			{
				Vector3 d = PlanetSurface.FibonacciDirection(i, Points);
				double latitude = PlanetClimateField.LatitudeOf(d);
				float h = PlanetSurface.Height(field.Seed, d, field.Cratering);
				float altitude = PlanetSurface.AltitudeFromHeight(h, field.Profile, field.ReliefMetres);
				field.BiomeAt(latitude, d, h, altitude, out PlanetSurfacePoint honest);
				PlanetSurfacePoint moved = field.SelectionPoint(d, honest);

				Assert.AreEqual(h, honest.Height, $"point {i}: the honest point carries the bake's own raw height");
				Assert.AreEqual(altitude, honest.AltitudeMetres, $"point {i}: and its own altitude");
				Assert.AreEqual(honest.UnderWater, moved.UnderWater, $"point {i}: the biome is chosen on the side of the sea the pixel is on");
				foreach (float lava in lavaLevels)
				{
					PlanetSurfaceBaker.GlobePixel real = PlanetSurfaceBaker.Classify(true, lava, true, field.Profile.SeaLevel, h, altitude);
					Assert.AreEqual(real, PlanetSurfaceBaker.Classify(true, lava, true, field.Profile.SeaLevel, honest.Height, honest.AltitudeMetres), $"point {i}, lava at {lava} m");
					if (real != PlanetSurfaceBaker.Classify(true, lava, true, field.Profile.SeaLevel, h, moved.AltitudeMetres))
					{
						lavaWouldMove++;
					}
				}
			}
			// Why the source scan above matters: classified from the moved altitude, a lava shore would
			// wander off the level the scene generator stands its lava at.
			Assert.Greater(lavaWouldMove, 0, "the moved altitude does cross a lava level that is not the sea's");
		}
	}
}
