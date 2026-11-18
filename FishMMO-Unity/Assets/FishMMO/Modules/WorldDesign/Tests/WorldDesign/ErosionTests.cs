using System;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Erosion: that it moves material without making or losing any, the same way every time, never
	/// under the water and less through hard rock; that rivers build branching valleys; and that a
	/// scene's edge does not move.
	/// </summary>
	[TestFixture]
	public class ErosionTests
	{
		private sealed class Uniform : IErosionGround
		{
			public float RainFactor = 1f;
			public float Hardness = 0.5f;
			public float Rain(int cell) => RainFactor;
			public float SoilErodibility(int cell) => 1f;
			public float Storminess(int cell) => 1f;
			public float RockHardness(int cell, float altitudeMetres) => Hardness;
			public float SoilTalusTangent(int cell) => Mathf.Tan(34f * Mathf.Deg2Rad);
			public float Creep(int cell) => 1f;
			public float ChannelThreshold(int cell) => 1f;
			public float DepressionKeeping(int cell) => 0f;
			/// <summary>The snowline's altitude, in metres; ground above it feeds ice. Far above everything by default: no glaciers.</summary>
			public float Snowline = 1e6f;
			public float IceBalance(int cell, float altitudeMetres) => Mathf.Clamp((altitudeMetres - Snowline) / 40f, -2f, 1f);
			public float Wind;
			public float Sand;
			public float Aeolian(int cell) => Wind;
			public float DuneSand(int cell) => Sand;
		}

		/// <summary>Droplets at the heightmap's own scale only: what the kernel tests examine.</summary>
		private static ErosionSettings FineOnly(float weathering = 0.15f) => new ErosionSettings
		{
			Landscape = null,
			Levels = new[] { new ErosionLevel(1, 0.5f, 1f) },
			WeatheringMetres = weathering,
		};

		/// <summary>Rolling hills on a tilt, in metres, two metres a cell.</summary>
		private static ErosionGrid Hills(int width, int depth, float soilMetres = 1f)
		{
			var height = new float[width * depth];
			var soil = new float[width * depth];
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					float e = x * 2f, n = z * 2f;
					height[z * width + x] = 0.08f * e + 12f * Mathf.Sin(e / 70f) * Mathf.Cos(n / 55f) + 5f * Mathf.Sin((e + n) / 23f);
					soil[z * width + x] = soilMetres;
				}
			}
			return new ErosionGrid(width, depth, 2f, height, soil);
		}

		private static double Volume(float[] height, float cell)
		{
			double sum = 0.0;
			foreach (float h in height)
			{
				sum += h;
			}
			return sum * cell * cell;
		}

		[Test]
		public void ErosionMovesMaterialAndNeitherMakesNorLosesAny()
		{
			/* Everything taken is laid down somewhere or carried off the grid's edge, and slumping and
			 * creep only move it: the volume falls by exactly what left. */
			ErosionGrid grid = Hills(300, 200);
			double before = Volume(grid.Height, grid.CellMetres);
			ErosionTally tally = Erosion.Run(grid, new Uniform(), FineOnly(), 7);
			double after = Volume(grid.Height, grid.CellMetres);

			Assert.That(tally.Eroded, Is.GreaterThan(1000.0), "erosion must actually move something");
			Assert.That(after - before, Is.EqualTo(-tally.Lost).Within(Math.Max(1.0, tally.Eroded * 1e-4)));
		}

		[Test]
		public void TheSameSeedWearsTheSameGround()
		{
			// Every stage: the landscape model, the coarse and fine droplets, slumping, creep.
			ErosionGrid a = Hills(400, 300), b = Hills(400, 300);
			Erosion.Run(a, new Uniform(), new ErosionSettings(), 11);
			Erosion.Run(b, new Uniform(), new ErosionSettings(), 11);
			CollectionAssert.AreEqual(a.Height, b.Height);
			CollectionAssert.AreEqual(a.Soil, b.Soil);
		}

		[Test]
		public void RunningWaterNeverWearsGroundUnderTheSea()
		{
			/* A droplet reaching the sea drops its load at the shore; nothing below the surface is cut. */
			ErosionGrid grid = Hills(300, 200);
			grid.BaseLevel = 8f;
			var before = (float[])grid.Height.Clone();
			var settings = FineOnly();
			Erosion.Hydraulic(grid, new Uniform(), settings, new Erosion.Brush(settings.BrushRadius), settings.MaxLifetime, 1, 0, 3, 0.5f, 1f);

			int underwater = 0;
			for (int i = 0; i < before.Length; i++)
			{
				if (before[i] < grid.BaseLevel)
				{
					underwater++;
					Assert.That(grid.Height[i], Is.GreaterThanOrEqualTo(before[i]), $"cell {i} under the sea was worn");
				}
			}
			Assert.That(underwater, Is.GreaterThan(1000), "the test ground must reach under the sea");
		}

		[Test]
		public void HardRockWearsFarLessThanSoft()
		{
			/* Bare rock, no soil: the water can only cut the rock itself, at a share that falls with
			 * its hardness. That is what will leave hard beds standing as ledges. */
			ErosionGrid soft = Hills(300, 200, 0f), hard = Hills(300, 200, 0f);
			/* Weathering off: it turns rock to soil, which then washes at soil's rate whatever the rock —
			 * right for the ground, but not what this pins. */
			ErosionSettings settings = FineOnly(0f);
			settings.ThermalIterations = 0;
			settings.CreepIterations = 0;
			ErosionTally softTally = Erosion.Run(soft, new Uniform { Hardness = 0.1f }, settings, 5);
			ErosionTally hardTally = Erosion.Run(hard, new Uniform { Hardness = 0.9f }, settings, 5);
			Assert.That(hardTally.Eroded, Is.LessThan(softTally.Eroded * 0.2), $"hard {hardTally.Eroded:N0} m³ against soft {softTally.Eroded:N0} m³");
		}

		[Test]
		public void RiversGrowValleysWhereTheWaterGathers()
		{
			/* The landscape model's point: valleys deepen with what drains through them. Under the
			 * same rain and rock, the cells that drain the most must have been cut the most. */
			ErosionGrid grid = Hills(256, 256);
			var before = (float[])grid.Height.Clone();
			float[] area = Drainage.Area(grid, new Uniform());
			Erosion.Landscape(grid, new Uniform(), new LandscapeEvolutionSettings { Steps = 120, PinRate = 0f });

			double gathered = 0.0, spread = 0.0;
			int gatheredCount = 0, spreadCount = 0;
			float threshold = 200f * grid.CellMetres * grid.CellMetres;
			for (int i = 0; i < area.Length; i++)
			{
				int x = i % grid.Width, z = i / grid.Width;
				if (x < 8 || z < 8 || x >= grid.Width - 8 || z >= grid.Depth - 8)
				{
					continue;
				}
				double cut = before[i] - grid.Height[i];
				if (area[i] > threshold) { gathered += cut; gatheredCount++; }
				else { spread += cut; spreadCount++; }
			}
			Assert.That(gatheredCount, Is.GreaterThan(50), "the test ground must have valleys");
			Assert.That(gathered / gatheredCount, Is.GreaterThan(2.0 * spread / spreadCount),
				$"valleys cut {gathered / gatheredCount:0.00} m on average, slopes {spread / spreadCount:0.00} m");
		}

		[Test]
		public void GlaciersTurnAValleyIntoAFlatFlooredTrough()
		{
			/* A V-valley falling from cold heights: the glacier fed above the snowline flows down it and
			 * cuts across its whole width, so the floor widens and flattens — the U that marks glaciated
			 * ground — rather than deepening along a single line as a river does. */
			const int Width = 160, Depth = 200;
			const float Cell = 8f;
			var height = new float[Width * Depth];
			for (int z = 0; z < Depth; z++)
			{
				for (int x = 0; x < Width; x++)
				{
					// Falls north to south; a V across, its axis down the middle.
					height[z * Width + x] = 100f + z * Cell * 0.15f + Mathf.Abs(x - Width / 2) * Cell * 0.5f;
				}
			}
			var outlet = new bool[height.Length];
			for (int n = 0; n < height.Length; n++)
			{
				int x = n % Width, z = n / Width;
				outlet[n] = x == 0 || z == 0 || x == Width - 1 || z == Depth - 1;
			}
			var ground = new Uniform { Snowline = 250f };
			int row = Depth / 3;
			float FloorWidth()
			{
				// Metres across the row within 5 m of its lowest point.
				float lowest = float.MaxValue;
				for (int x = 0; x < Width; x++)
				{
					lowest = Mathf.Min(lowest, height[row * Width + x]);
				}
				int within = 0;
				for (int x = 0; x < Width; x++)
				{
					if (height[row * Width + x] < lowest + 5f)
					{
						within++;
					}
				}
				return within * Cell;
			}
			float before = FloorWidth();
			var original = (float[])height.Clone();
			float iced = GlacialErosion.Run(height, Width, Depth, Cell, ground.IceBalance, (n, a) => 0.5f, outlet, new GlacialSettings());
			float after = FloorWidth();
			int axis = row * Width + Width / 2, wall = axis + 5;
			float axisCut = original[axis] - height[axis], wallCut = original[wall] - height[wall];

			Assert.That(iced, Is.GreaterThan(0f), "the valley head is above the snowline, so a glacier must form");
			Assert.That(after, Is.GreaterThan(before * 2.5f), $"the floor should widen into a trough: {before} m → {after} m");
			Assert.That(wallCut, Is.GreaterThan(axisCut), $"the walls are cut back ({wallCut:0.0} m) more than the centre deepens ({axisCut:0.0} m)");
		}

		[Test]
		public void AWorldWithoutSnowHasNoGlaciers()
		{
			ErosionGrid grid = Hills(128, 128);
			var before = (float[])grid.Height.Clone();
			var outlet = new bool[before.Length];
			float iced = GlacialErosion.Run(grid.Height, grid.Width, grid.Depth, grid.CellMetres, new Uniform().IceBalance, (n, a) => 0.5f, outlet, new GlacialSettings());
			Assert.That(iced, Is.EqualTo(0f));
			CollectionAssert.AreEqual(before, grid.Height);
		}

		private static ErosionGrid Flat(int width, int depth, float cell)
		{
			return new ErosionGrid(width, depth, cell, new float[width * depth], new float[width * depth]);
		}

		[Test]
		public void DunesClimbGentlyIntoTheWindAndFallSteeplyOutOfIt()
		{
			/* Wind blowing east over a sand sea: crests run north-south, across it, and each dune's
			 * eastern (lee) face is its steep slip face, under the angle of repose. */
			ErosionGrid grid = Flat(400, 64, 2f);
			var settings = new AeolianSettings { WindX = 1f, WindZ = 0f };
			AeolianErosion.Run(grid, new Uniform { Wind = 1f, Sand = 1f }, settings, 3);

			float steepestUp = 0f, steepestDown = 0f;
			int z = 32;
			for (int x = 0; x < grid.Width - 1; x++)
			{
				float slope = (grid.Height[z * grid.Width + x + 1] - grid.Height[z * grid.Width + x]) / grid.CellMetres;
				steepestUp = Mathf.Max(steepestUp, slope);
				steepestDown = Mathf.Max(steepestDown, -slope);
			}
			Assert.That(steepestDown, Is.GreaterThan(steepestUp * 2f), $"lee {steepestDown:0.00} against stoss {steepestUp:0.00}");
			Assert.That(Mathf.Atan(steepestDown) * Mathf.Rad2Deg, Is.LessThan(34f), "a slip face stands under sand's angle of repose");

			// Along a crest (north-south) the height changes far less than across the dunes.
			float alongCrest = 0f, acrossDunes = 0f;
			for (int k = 0; k < 40; k++)
			{
				alongCrest += Mathf.Abs(grid.Height[(10 + k + 1) * grid.Width + 200] - grid.Height[(10 + k) * grid.Width + 200]);
				acrossDunes += Mathf.Abs(grid.Height[32 * grid.Width + 200 + k + 1] - grid.Height[32 * grid.Width + 200 + k]);
			}
			Assert.That(acrossDunes, Is.GreaterThan(alongCrest * 2f), "dune crests lie across the wind");
		}

		[Test]
		public void YardangsAreCutOnlyIntoSoftExposedRock()
		{
			ErosionGrid soft = Flat(200, 200, 2f), hard = Flat(200, 200, 2f), wet = Flat(200, 200, 2f);
			var settings = new AeolianSettings();
			(double softCut, _) = AeolianErosion.Run(soft, new Uniform { Wind = 1f, Hardness = 0.1f }, settings, 5);
			(double hardCut, _) = AeolianErosion.Run(hard, new Uniform { Wind = 1f, Hardness = 0.95f }, settings, 5);
			(double wetCut, _) = AeolianErosion.Run(wet, new Uniform { Wind = 0f, Hardness = 0.1f }, settings, 5);
			Assert.That(softCut, Is.GreaterThan(0.0));
			Assert.That(hardCut, Is.LessThan(softCut * 0.05), "hard rock stands");
			Assert.That(wetCut, Is.EqualTo(0.0), "ground the wind cannot work is left alone");
		}

		[Test]
		public void NoRainNoRunningWater()
		{
			ErosionGrid grid = Hills(200, 200);
			var before = (float[])grid.Height.Clone();
			ErosionSettings settings = FineOnly();
			ErosionTally tally = Erosion.Hydraulic(grid, new Uniform { RainFactor = 0f }, settings, new Erosion.Brush(settings.BrushRadius), settings.MaxLifetime, 1, 0, 1, 0.5f, 1f);
			Assert.That(tally.Droplets, Is.EqualTo(0));
			CollectionAssert.AreEqual(before, grid.Height);
			Assert.That(SceneErosion.RainFactor(0f), Is.EqualTo(0f));
			Assert.That(SceneErosion.RainFactor(MoistureModel.Midpoint), Is.EqualTo(1f).Within(1e-5f));
		}

		[Test]
		public void AScenesEdgeDoesNotMove()
		{
			/* The backdrop and the neighbouring scenes are the planet's ground, so erosion fades to
			 * nothing at the edge: every edge sample is exactly what the planet cut, while the inside
			 * has worn. */
			var body = ScriptableObject.CreateInstance<WorldBody>();
			body.name = "Erosion Edge World";
			body.SkyRadiusKm = PlanetSurface.EarthRadiusKm;
			body.Water = 0.7f;
			body.Atmosphere = AtmosphereKind.Standard;
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			biome.name = "Edge Grass";
			try
			{
				var request = new SceneGenerationRequest
				{
					SceneName = "Edge",
					Body = body,
					Latitude = 30.0,
					Longitude = 40.0,
					SizeKm = new Vector2(1.2f, 1.2f),
				};
				var plan = new TerrainTilePlan { CountX = 2, CountZ = 2, TileMetres = 600f, Resolution = 129 };
				SceneHeightField field = SceneHeightField.Sample(request, plan);
				var before = (float[])field.Metres.Clone();

				var cells = new byte[16 * 16];
				SceneBiomeField biomes = SceneBiomeField.FromCells(16, 16, 80f, new Vector2(-640f, -640f), new[] { biome }, cells);
				PlanetGeology geology = PlanetGeology.For(body, BiomeWorldConditions.Earthlike, 30.0);
				SceneTerrainProcess process = SceneTerrainProcess.Build(biomes, geology, plan.WidthMetres, plan.DepthMetres,
					request.Footprint, 30.0, (direction, latitude) => MoistureModel.Midpoint);

				SceneErosionReport report = SceneErosion.Apply(field, process, new ErosionSettings(), float.NegativeInfinity, 9u);
				Assert.That(report.Ran, Is.True, report.Skipped);

				int width = field.Width, depth = field.Depth;
				for (int k = 0; k < width; k++)
				{
					Assert.That(field.Metres[k], Is.EqualTo(before[k]), "south edge");
					Assert.That(field.Metres[(depth - 1) * width + k], Is.EqualTo(before[(depth - 1) * width + k]), "north edge");
				}
				for (int k = 0; k < depth; k++)
				{
					Assert.That(field.Metres[k * width], Is.EqualTo(before[k * width]), "west edge");
					Assert.That(field.Metres[k * width + width - 1], Is.EqualTo(before[k * width + width - 1]), "east edge");
				}
				float inside = 0f;
				for (int z = depth / 4; z < depth * 3 / 4; z++)
				{
					for (int x = width / 4; x < width * 3 / 4; x++)
					{
						inside = Mathf.Max(inside, Mathf.Abs(field.Metres[z * width + x] - before[z * width + x]));
					}
				}
				Assert.That(inside, Is.GreaterThan(0.01f), "the inside of the scene must have worn");
			}
			finally
			{
				Object.DestroyImmediate(biome);
				Object.DestroyImmediate(body);
			}
		}
	}
}
