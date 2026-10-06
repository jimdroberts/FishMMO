#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What eroding a scene did, for the generator's notes.</summary>
	public sealed class SceneErosionReport
	{
		public bool Ran;
		/// <summary>Why it did not run, when it did not.</summary>
		public string Skipped;
		public ErosionTally Tally;
		public double Seconds;
		/// <summary>Mean change of height over the scene, metres.</summary>
		public float MeanChangeMetres;
		/// <summary>The deepest cut and the thickest fill, metres.</summary>
		public float DeepestCutMetres;
		public float ThickestFillMetres;
		/// <summary>Samples whose rock was worked out on their own, because a province edge crosses them.</summary>
		public int ExactGeologySamples;
		/// <summary>Share of the scene stepped into plateaus, weighted by how fully.</summary>
		public float PlateauShare;
		/// <summary>Per sample, how much of a plateau the ground became, 0 … 1; null when none did. What canyon walls stand in.</summary>
		public float[] PlateauWeight;
		/// <summary>The rock under every sample, as erosion read it.</summary>
		public SceneGeologyGrid Geology;

		public override string ToString()
		{
			if (!Ran)
			{
				return $"Erosion skipped: {Skipped}";
			}
			return $"Erosion: {(PlateauShare > 0.001f ? $"{PlateauShare:P0} of the ground stepped into plateaus; " : string.Empty)}{Tally.Droplets:N0} droplets moved {Tally.Eroded:N0} m³ ({Tally.Lost:N0} m³ carried off the edge) in {Seconds:0.0} s; " +
				$"the ground changed {MeanChangeMetres:0.00} m on average, cut {DeepestCutMetres:0.0} m at most, filled {ThickestFillMetres:0.0} m at most.";
		}
	}

	/// <summary>
	/// Wears a scene's ground before it is written to its tiles: its biomes' ways of wearing, its
	/// climate's rain and its planet's rock, run through <see cref="Erosion"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The edge does not move.</b> The backdrop past the scene's edge is the planet's ground, and the
	/// scene's own neighbours are cut from the same planet, so whatever erosion does is faded out over
	/// <see cref="EdgeFadeMetres"/> to nothing at the edge. Gullies run out into the planet's ground
	/// there rather than ending in a step.
	/// </para>
	/// <para>
	/// <b>The sea is the base level.</b> Droplets end where they reach a sea or lava lake and drop what
	/// they carry at the shore, and running water wears nothing under the liquid's surface; only
	/// slumping goes on below it, as it does on real underwater slopes.
	/// </para>
	/// </remarks>
	public static class SceneErosion
	{
		/// <summary>How far in from the scene's edge erosion fades in, in metres.</summary>
		public const float EdgeFadeMetres = 96f;

		/// <summary>
		/// The most samples erosion takes on. Its working state is about eight values per sample, so
		/// this is roughly 1.3 GB; a scene over about 12 km a side is left as the planet cut it.
		/// </summary>
		public const int MaximumSamples = 40_000_000;

		/// <summary>Coarse cells, in samples, the ground's way of wearing is read at.</summary>
		private const int ProcessCells = 16;

		/// <summary>Wears <paramref name="field"/> in place.</summary>
		/// <param name="field">The scene's ground, sampled and not yet written.</param>
		/// <param name="process">How it wears: profiles, rain and rock, read from the same unshaped ground.</param>
		/// <param name="settings">The process's constants.</param>
		/// <param name="baseLevel">World Y of a sea or lava surface; negative infinity for none.</param>
		/// <param name="seed">The scene's seed, so a re-cut wears the same way.</param>
		/// <param name="lay">
		/// Lays the scene's rivers and lakes into the ground once the plateaus are stepped, and returns the
		/// water's surface per sample (negative infinity where dry): erosion's base level beside the sea's.
		/// Null for none.
		/// </param>
		public static SceneErosionReport Apply(SceneHeightField field, SceneTerrainProcess process, ErosionSettings settings, float baseLevel, uint seed,
			Func<SceneHeightField, float[]> lay = null)
		{
			var report = new SceneErosionReport();
			if (field.Metres.Length > MaximumSamples)
			{
				report.Skipped = $"{field.Metres.Length:N0} samples is more than the {MaximumSamples:N0} erosion takes on.";
				return report;
			}
			Stopwatch clock = Stopwatch.StartNew();

			SceneGeologyGrid geology = SceneGeologyGrid.Build(field, process.ColumnAt);
			report.ExactGeologySamples = geology.ExactSamples;
			report.Geology = geology;
			var ground = new Ground(field, process, geology);

			int width = field.Width, depth = field.Depth;
			float[] height = field.Metres;
			var original = (float[])height.Clone();
			if (settings.Plateau != null)
			{
				report.PlateauShare = Plateaus(field, geology, ground, settings.Plateau, baseLevel, out report.PlateauWeight);
			}
			// The rivers and lakes on the stepped ground, before the rain: the gullies grow toward them.
			float[] water = lay?.Invoke(field);
			var soil = new float[height.Length];

			/* The soil it starts with: the biome's depth on the flat, thinning to nothing where the
			 * ground is as steep as the soil can stand. */
			float cell = field.Spacing;
			Parallel.For(0, depth, z =>
			{
				int za = Math.Max(0, z - 1), zb = Math.Min(depth - 1, z + 1);
				for (int x = 0; x < width; x++)
				{
					int xa = Math.Max(0, x - 1), xb = Math.Min(width - 1, x + 1);
					int i = z * width + x;
					float dx = (height[z * width + xb] - height[z * width + xa]) / ((xb - xa) * cell);
					float dz = (height[zb * width + x] - height[za * width + x]) / ((zb - za) * cell);
					float slope = Mathf.Sqrt(dx * dx + dz * dz);
					soil[i] = ground.SoilDepth(i) * Mathf.Clamp01(1f - slope / Mathf.Max(1e-3f, ground.SoilTalusTangent(i)));
				}
			});

			var grid = new ErosionGrid(width, depth, cell, height, soil) { BaseLevel = baseLevel, WaterLevel = water };
			report.Tally = Erosion.Run(grid, ground, settings, unchecked((int)seed));

			// Faded to nothing at the edge, where the scene meets the backdrop and its neighbours.
			float fade = Mathf.Max(cell, EdgeFadeMetres);
			double change = 0.0;
			float cut = 0f, fill = 0f;
			var rowChange = new double[depth];
			var rowCut = new float[depth];
			var rowFill = new float[depth];
			Parallel.For(0, depth, z =>
			{
				float fromSouth = z * cell, fromNorth = (depth - 1 - z) * cell;
				double sum = 0.0;
				float deepest = 0f, thickest = 0f;
				for (int x = 0; x < width; x++)
				{
					float fromWest = x * cell, fromEast = (width - 1 - x) * cell;
					float edge = Mathf.Min(Mathf.Min(fromSouth, fromNorth), Mathf.Min(fromWest, fromEast));
					float weight = Mathf.SmoothStep(0f, 1f, edge / fade);
					int i = z * width + x;
					float delta = (height[i] - original[i]) * weight;
					height[i] = original[i] + delta;
					sum += Math.Abs(delta);
					deepest = Mathf.Min(deepest, delta);
					thickest = Mathf.Max(thickest, delta);
				}
				rowChange[z] = sum;
				rowCut[z] = deepest;
				rowFill[z] = thickest;
			});
			for (int z = 0; z < depth; z++)
			{
				change += rowChange[z];
				cut = Mathf.Min(cut, rowCut[z]);
				fill = Mathf.Max(fill, rowFill[z]);
			}

			report.Ran = true;
			report.Seconds = clock.Elapsed.TotalSeconds;
			report.MeanChangeMetres = (float)(change / height.Length);
			report.DeepestCutMetres = cut;
			report.ThickestFillMetres = fill;
			return report;
		}

		/// <summary>
		/// The temperature, on the climate scale, below which snow outlasts the summer: where the biomes'
		/// permanent ice begins.
		/// </summary>
		public const float SnowlineTemperature = -0.65f;

		/// <summary>
		/// How much colder the last glacial maximum was than now, on the climate scale: about 6 K, the
		/// cooling that carved most of the glacial landscapes on Earth and left them for today's climate.
		/// </summary>
		public const float GlacialCooling = (float)(6.0 / ClimateModel.KelvinPerUnit);

		/// <summary>The temperature span, on the climate scale, over which feeding turns to melting at the snowline.</summary>
		public const float IceBalanceBand = 0.1f;

		/// <summary>How far in from a province's edge a plateau reaches its full height, in metres: where its rock ends, the benches fade out.</summary>
		public const float PlateauEdgeMetres = 300f;

		/// <summary>
		/// Steps the ground into benches wherever the biome breaks into them and the rock lies flat
		/// (<see cref="PlateauTerrace"/>). Returns the share of the scene stepped, weighted.
		/// </summary>
		private static float Plateaus(SceneHeightField field, SceneGeologyGrid geology, Ground ground, PlateauSettings settings, float baseLevel,
			out float[] weights)
		{
			int count = field.Metres.Length;
			var weight = new float[count];
			weights = null;
			var offset = new float[count];
			var step = new float[count];
			var rowShare = new double[field.Depth];
			Parallel.For(0, field.Depth, z =>
			{
				double sum = 0.0;
				for (int x = 0; x < field.Width; x++)
				{
					int i = z * field.Width + x;
					GeologyColumn column = geology.ColumnOf(i);
					float share = ground.PlateauShare(i);
					if (share <= 0f || !column.FlatLying || column.BedMetres <= 0f)
					{
						continue;
					}
					weight[i] = share * Mathf.SmoothStep(0f, 1f, column.BoundaryMetres / PlateauEdgeMetres);
					offset[i] = geology.OffsetOf(i);
					step[i] = column.BedMetres * settings.BedsPerStep;
					sum += weight[i];
				}
				rowShare[z] = sum;
			});
			double total = 0.0;
			foreach (double row in rowShare)
			{
				total += row;
			}
			if (total <= 0.0)
			{
				return 0f;
			}
			weights = weight;
			PlateauTerrace.Apply(field.Metres, field.Width, field.Depth, field.Spacing, weight, offset, step, baseLevel, settings);
			return (float)(total / count);
		}

		/// <summary>
		/// Rain relative to the moisture model's middle, as erosion's rain factor: the square root,
		/// because a downpour on a desert still cuts and a drizzle on a rainforest still adds up.
		/// Nothing on a world where it never rains.
		/// </summary>
		public static float RainFactor(float rain) => Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0f, rain) / MoistureModel.Midpoint), 0f, 2.5f);

		/// <summary>What erosion reads about a scene's ground: its profiles and rain, read coarsely, and its rock per sample.</summary>
		private sealed class Ground : IErosionGround
		{
			private readonly SceneGeologyGrid geology;
			private readonly SceneTerrainProcess process;
			private readonly float[] eastOf;
			private readonly float[] northOf;
			private readonly int width;
			/// <summary>Per sample column and row: the coarse cell to its west/south and how far across it the sample is.</summary>
			private readonly int[] columnOfX;
			private readonly float[] acrossX;
			private readonly int[] rowOfZ;
			private readonly float[] acrossZ;
			private readonly int coarseWidth;
			private readonly float[] rain;
			private readonly float[] erodibility;
			private readonly float[] storminess;
			private readonly float[] talus;
			private readonly float[] creep;
			private readonly float[] soilDepth;
			private readonly float[] plateau;
			private readonly float[] aeolian;
			private readonly float[] duneSand;
			private readonly float[] keeping;
			private readonly float[] channel;

			public Ground(SceneHeightField field, SceneTerrainProcess process, SceneGeologyGrid geology)
			{
				this.geology = geology;
				this.process = process;
				width = field.Width;
				eastOf = new float[field.Width];
				for (int x = 0; x < field.Width; x++)
				{
					eastOf[x] = field.EastOf(x);
				}
				northOf = new float[field.Depth];
				for (int z = 0; z < field.Depth; z++)
				{
					northOf[z] = field.NorthOf(z);
				}
				coarseWidth = (field.Width - 1) / ProcessCells + 2;
				int coarseDepth = (field.Depth - 1) / ProcessCells + 2;
				/* Bilinear between the coarse cells, not the nearest: erosion acting on values that
				 * step every coarse cell would carve the step into the ground. */
				columnOfX = new int[field.Width];
				acrossX = new float[field.Width];
				for (int x = 0; x < field.Width; x++)
				{
					columnOfX[x] = Math.Min(coarseWidth - 2, x / ProcessCells);
					acrossX[x] = Mathf.Clamp01((x - columnOfX[x] * ProcessCells) / (float)ProcessCells);
				}
				rowOfZ = new int[field.Depth];
				acrossZ = new float[field.Depth];
				for (int z = 0; z < field.Depth; z++)
				{
					rowOfZ[z] = Math.Min(coarseDepth - 2, z / ProcessCells);
					acrossZ[z] = Mathf.Clamp01((z - rowOfZ[z] * ProcessCells) / (float)ProcessCells);
				}

				int cells = coarseWidth * coarseDepth;
				rain = new float[cells];
				erodibility = new float[cells];
				storminess = new float[cells];
				talus = new float[cells];
				creep = new float[cells];
				soilDepth = new float[cells];
				plateau = new float[cells];
				aeolian = new float[cells];
				duneSand = new float[cells];
				keeping = new float[cells];
				channel = new float[cells];
				for (int b = 0; b < coarseDepth; b++)
				{
					float north = field.NorthOf(Math.Min(b * ProcessCells, field.Depth - 1));
					for (int a = 0; a < coarseWidth; a++)
					{
						float east = field.EastOf(Math.Min(a * ProcessCells, field.Width - 1));
						TerrainProcess p = process.ProcessAt(east, north);
						int i = b * coarseWidth + a;
						rain[i] = RainFactor(process.RainAt(east, north));
						erodibility[i] = p.Erodibility * (1f - p.VegetationCohesion);
						storminess[i] = p.Storminess;
						talus[i] = Mathf.Tan(Mathf.Clamp(p.TalusAngleDegrees, 5f, 85f) * Mathf.Deg2Rad);
						creep[i] = p.SoilCreep;
						soilDepth[i] = p.SoilDepthMetres;
						plateau[i] = p.PlateauShare;
						// The wind works dry, bare ground: rain and roots both hold it.
						aeolian[i] = (1f - p.VegetationCohesion) * Mathf.Clamp01(1f - RainFactor(process.RainAt(east, north)));
						duneSand[i] = p.DuneSand;
						keeping[i] = p.DepressionKeeping;
						channel[i] = p.ChannelThreshold;
					}
				}
			}

			private float Sample(float[] values, int cell)
			{
				int x = cell % width, z = cell / width;
				int i = rowOfZ[z] * coarseWidth + columnOfX[x];
				float fx = acrossX[x], fz = acrossZ[z];
				float south = values[i] + (values[i + 1] - values[i]) * fx;
				float north = values[i + coarseWidth] + (values[i + coarseWidth + 1] - values[i + coarseWidth]) * fx;
				return south + (north - south) * fz;
			}

			public float Rain(int cell) => Sample(rain, cell);
			public float SoilErodibility(int cell) => Sample(erodibility, cell);
			public float Storminess(int cell) => Sample(storminess, cell);
			public float RockHardness(int cell, float altitudeMetres) => geology.HardnessAt(cell, altitudeMetres);
			public float SoilTalusTangent(int cell) => Sample(talus, cell);
			public float Creep(int cell) => Sample(creep, cell);
			public float SoilDepth(int cell) => Sample(soilDepth, cell);
			public float PlateauShare(int cell) => Sample(plateau, cell);
			public float Aeolian(int cell) => Sample(aeolian, cell);
			public float DuneSand(int cell) => Sample(duneSand, cell);

			/// <summary>
			/// Fed above the last glacial maximum's snowline in proportion to the snowfall, melted below
			/// it the faster the warmer: see <see cref="IErosionGround.IceBalance"/>.
			/// </summary>
			public float IceBalance(int cell, float altitudeMetres)
			{
				float east = eastOf[cell % width], north = northOf[cell / width];
				float glacial = process.TemperatureAt(east, north, altitudeMetres) - GlacialCooling;
				float balance = Mathf.Clamp((SnowlineTemperature - glacial) / IceBalanceBand, -3f, 1f);
				return balance > 0f ? balance * Mathf.Min(1.5f, Sample(rain, cell)) : balance;
			}
			public float DepressionKeeping(int cell) => Sample(keeping, cell);
			public float ChannelThreshold(int cell) => Sample(channel, cell);
		}
	}
}
#endif
