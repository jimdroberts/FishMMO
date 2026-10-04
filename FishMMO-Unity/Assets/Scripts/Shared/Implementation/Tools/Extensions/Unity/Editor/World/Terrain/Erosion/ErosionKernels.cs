#if UNITY_EDITOR
using System;
using System.Threading.Tasks;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>One scale erosion works at: how coarse a grid, how much rain, how hard.</summary>
	public readonly struct ErosionLevel
	{
		/// <summary>Heightmap samples per cell of this level's grid: 1 is the heightmap itself.</summary>
		public readonly int Coarseness;
		/// <summary>Raindrops released per cell of this level's grid where the rain factor is 1.</summary>
		public readonly float DropletsPerCell;
		/// <summary>This level's share of <see cref="ErosionSettings.Strength"/>.</summary>
		public readonly float Strength;

		public ErosionLevel(int coarseness, float dropletsPerCell, float strength)
		{
			Coarseness = coarseness;
			DropletsPerCell = dropletsPerCell;
			Strength = strength;
		}
	}

	/// <summary>How hard and how long erosion works a scene's ground.</summary>
	/// <remarks>
	/// Lengths are in grid cells or metres as named; the ground's own character (how readily it
	/// washes, how steep it stands) is not here but in <see cref="IErosionGround"/>, from the biomes
	/// and the rock. These are the process's constants, and <see cref="Strength"/> is the one dial.
	/// </remarks>
	public sealed class ErosionSettings
	{
		/// <summary>Scales how much a droplet can carry, and so how deep everything cuts.</summary>
		public float Strength = 1f;

		/// <summary>
		/// The landscape evolution model that builds the scene's drainage first — its valleys, ridges
		/// and the slopes between (<see cref="LandscapeEvolution"/>); null to skip it.
		/// </summary>
		public LandscapeEvolutionSettings Landscape = new LandscapeEvolutionSettings();

		/// <summary>How glaciers carve after the rivers (<see cref="GlacialErosion"/>); null to skip them.</summary>
		public GlacialSettings Glacial = new GlacialSettings();

		/// <summary>How the wind shapes the ground last of all (<see cref="AeolianErosion"/>); null on a world with no air.</summary>
		public AeolianSettings Aeolian = new AeolianSettings();

		/// <summary>How plateaus are stepped before erosion (<see cref="PlateauTerrace"/>); null to skip them.</summary>
		public PlateauSettings Plateau = new PlateauSettings();

		/// <summary>
		/// The scales droplets then work at, coarsest first: each level's change is carried back to
		/// the heightmap before the next begins.
		/// </summary>
		/// <remarks>
		/// The landscape model builds the valleys at about eight metres; a droplet lives
		/// <see cref="MaxLifetime"/> steps of one cell, so at four metres it cuts the side gullies and at
		/// the heightmap's own two the rills.
		/// </remarks>
		public ErosionLevel[] Levels =
		{
			new ErosionLevel(2, 1f, 0.3f),
			new ErosionLevel(1, 0.5f, 0.6f),
		};

		/// <summary>
		/// The drainage area at which a droplet carries twice what it would on a hillside, in m²,
		/// before the ground's channel threshold scales it: where hillside wash becomes a channel.
		/// </summary>
		/// <remarks>
		/// A droplet's capacity is multiplied by 1 + √(area ÷ this) — the stream power law's square
		/// root of drainage area — so a valley floor that drains a square kilometre carries several
		/// times what the slopes above it do, and cuts instead of filling (see <see cref="Drainage"/>).
		/// </remarks>
		public float ChannelAreaSquareMetres = 20000f;

		/// <summary>The most a channel's discharge multiplies a droplet's capacity.</summary>
		public float MaxDischarge = 12f;

		/// <summary>
		/// The most running water may build the ground up above where it started, in metres; what it
		/// would lay down above that is carried out of the scene instead.
		/// </summary>
		/// <remarks>
		/// Ground built from noise is pitted: closed hollows, measured, cover 40% of it, and the
		/// deeper ones survive breaching. Everything washed off the ridges settled in them, and
		/// erosion buried every basin under a flat fill up to 20 m deep, the planet's own shape lost
		/// under it. Real drainage carries most of what it erodes away; the fans and floodplains it
		/// does leave are metres thick, not the depth of the basin.
		/// </remarks>
		public float MaxFillMetres = 2f;

		/// <summary>The run is split into this many rounds, each rain then slumping, so slopes the rain steepens can fail before the next rain.</summary>
		public int Rounds = 4;

		/// <summary>Steps a droplet lives, each about one cell. Capped so a droplet stays within its tile's reach (<see cref="Erosion.TileCells"/>).</summary>
		public int MaxLifetime = 48;

		/// <summary>How much of its last direction a droplet keeps, 0 … 1: higher carves smoother, longer channels.</summary>
		public float Inertia = 0.1f;

		/// <summary>A new droplet's speed.</summary>
		public float InitialSpeed = 1f;

		/// <summary>The fastest a droplet runs, so a cliff does not launch one into the sky.</summary>
		public float MaxSpeed = 10f;

		/// <summary>
		/// Sediment carried, in metres over a cell, per unit of slope, speed and water, before
		/// <see cref="Strength"/> and storminess.
		/// </summary>
		/// <remarks>
		/// Per unit of SLOPE, not of the drop per step: the drop per step grows with the cell, so a
		/// capacity set by it made the same ground carry eight times as much when eroded at eight
		/// times the cell, and coarse passes buried every hollow under a flat fill.
		/// </remarks>
		public float Capacity = 1f;

		/// <summary>The least slope counted when working out capacity, so a droplet on the flat still carries a little.</summary>
		public float MinSlope = 0.001f;

		/// <summary>Share of the spare capacity taken from the ground per step.</summary>
		public float ErodeRate = 0.3f;

		/// <summary>Share of the excess sediment dropped per step.</summary>
		public float DepositRate = 0.2f;

		/// <summary>Share of a droplet's water lost per step.</summary>
		public float Evaporation = 0.02f;

		/// <summary>Speed gained per metre dropped, squared: speed² += drop × gravity.</summary>
		public float Gravity = 9.81f;

		/// <summary>Cells either side of a droplet that it wears, so it cuts a channel rather than a pit.</summary>
		public int BrushRadius = 2;

		/// <summary>Rate bedrock wears at, as a share of loose soil's, for the softest rock (hardness 0); it falls with hardness squared.</summary>
		public float BedrockRate = 0.3f;

		/// <summary>The steepest the softest bare rock stands, in degrees.</summary>
		public float RockAngleSoftDegrees = 40f;

		/// <summary>
		/// The steepest the hardest bare rock stands, in degrees. Not a cliff's 80: a heightmap two
		/// metres a sample draws an 80° step as a one-sample wall, and walls like that cut every
		/// gully into a flat-floored, square-sided slot. Real cliffs are the cliff rocks' job.
		/// </summary>
		public float RockAngleHardDegrees = 62f;

		/// <summary>
		/// Metres of bare, soft rock turned to soil per round, where nothing covers it. Weathering is
		/// what rounds a ledge's lip and a gully's shoulder: without it, rock once exposed keeps
		/// every edge the water cut into it.
		/// </summary>
		public float WeatheringMetres = 0.15f;

		/// <summary>How deep a soil cover halves the weathering under it, in metres (the e-folding depth of the soil production function).</summary>
		public float WeatheringDepthMetres = 0.5f;

		/// <summary>Soil thinner than this, in metres, counts as bare rock for how steep the ground can stand.</summary>
		public float SoilPresenceMetres = 0.05f;

		/// <summary>Slumping passes after each round of rain.</summary>
		public int ThermalIterations = 6;

		/// <summary>Share of an over-steep step's excess that slumps per pass.</summary>
		public float ThermalRate = 0.5f;

		/// <summary>Creep passes after each round of rain.</summary>
		public int CreepIterations = 4;

		/// <summary>Share of the height difference between neighbours that creep evens out per pass, at creep 1, on a cell of <see cref="ReferenceCellMetres"/>.</summary>
		public float CreepRate = 0.1f;

		/// <summary>The cell size the creep rate is stated for, in metres: about the heightmap's own.</summary>
		public float ReferenceCellMetres = 2f;
	}

	/// <summary>What erosion asks about the ground at a cell: the biome's way of wearing and the rock's hardness.</summary>
	/// <remarks>Called from many threads at once; implementations must be read-only while erosion runs.</remarks>
	public interface IErosionGround
	{
		/// <summary>How much rain falls at the cell, relative to the reference rain (1): what scales the droplets released there. 0 where it never rains.</summary>
		float Rain(int cell);

		/// <summary>How readily loose soil at the cell washes away, relative to temperate soil.</summary>
		float SoilErodibility(int cell);

		/// <summary>How much more a flood carries than steady rain at the cell, relative to temperate rain.</summary>
		float Storminess(int cell);

		/// <summary>Hardness, 0 … 1, of the rock at the cell at an altitude in scene metres.</summary>
		float RockHardness(int cell, float altitudeMetres);

		/// <summary>Tangent of the steepest slope loose soil stands at the cell.</summary>
		float SoilTalusTangent(int cell);

		/// <summary>How fast soil creeps at the cell, relative to temperate soil.</summary>
		float Creep(int cell);

		/// <summary>How much ground must drain to the cell before running water there acts as a channel, relative to temperate ground (1).</summary>
		float ChannelThreshold(int cell);

		/// <summary>
		/// How much ice the climate feeds the cell at an altitude, relative to a well-fed snowfield: positive
		/// above the snowline of the last glacial maximum (scaled by the snowfall), negative below it
		/// (melting faster the warmer), and 0 on a world where it never snows.
		/// </summary>
		float IceBalance(int cell, float altitudeMetres);

		/// <summary>How freely the wind works the cell, 0 … 1: dry and bare is 1; wet, vegetated or airless ground 0.</summary>
		float Aeolian(int cell);

		/// <summary>How much loose sand the cell has for dunes, 0 none … 1 a sand sea.</summary>
		float DuneSand(int cell);

		/// <summary>Share of hollows at the cell that stay closed rather than being cut open to drain, 0 … 1 (karst keeps all of its sinkholes).</summary>
		float DepressionKeeping(int cell);
	}

	/// <summary>The ground erosion works on: heights and the loose soil over the rock, in metres.</summary>
	public sealed class ErosionGrid
	{
		public readonly int Width;
		public readonly int Depth;
		public readonly float CellMetres;
		/// <summary>The surface, rock and soil together, in scene metres; changed in place.</summary>
		public readonly float[] Height;
		/// <summary>Loose material over the rock, in metres.</summary>
		public readonly float[] Soil;
		/// <summary>The water's surface: droplets end there and drop their load, and running water wears nothing under it. Negative infinity for a dry world.</summary>
		public float BaseLevel = float.NegativeInfinity;

		/// <summary>Per cell, the highest running water may build the ground; null for no limit (<see cref="ErosionSettings.MaxFillMetres"/>).</summary>
		public float[] Ceiling;

		public ErosionGrid(int width, int depth, float cellMetres, float[] height, float[] soil)
		{
			if (height == null || height.Length != width * depth || soil == null || soil.Length != height.Length)
			{
				throw new ArgumentException("Height and soil need one value per cell.");
			}
			Width = width;
			Depth = depth;
			CellMetres = cellMetres;
			Height = height;
			Soil = soil;
		}
	}

	/// <summary>What a run of erosion did, in cubic metres.</summary>
	public struct ErosionTally
	{
		/// <summary>Taken from the ground by running water.</summary>
		public double Eroded;
		/// <summary>Laid down again by running water.</summary>
		public double Deposited;
		/// <summary>Carried off the edge of the grid or into the sea, or cut away to open a hollow.</summary>
		public double Lost;
		public long Droplets;
		public long Steps;
		/// <summary>Share of the ground glaciers flowed over, at the end of their carving.</summary>
		public float Glaciated;

		public void Add(in ErosionTally other)
		{
			Eroded += other.Eroded;
			Deposited += other.Deposited;
			Lost += other.Lost;
			Droplets += other.Droplets;
			Steps += other.Steps;
			Glaciated = Math.Max(Glaciated, other.Glaciated);
		}
	}

	/// <summary>
	/// Wears a heightfield the way rain, gravity and creep wear ground: running water cutting gullies
	/// and laying fans, over-steep ground slumping to its angle, and soil creeping smooth.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Running water as droplets</b> (the particle method of Mei et al. and Beyer's thesis, as
	/// widely implemented): each raindrop runs downhill with some inertia, picks up sediment while it
	/// is carrying less than its speed, water and slope allow, and drops it where it slows. The
	/// ground has two layers — loose soil over rock — and the water takes soil first; rock gives way
	/// only at a share that falls with its hardness, so hard beds stand as ledges over soft ones.
	/// One correction to the common implementation: speed GROWS downhill
	/// (speed² − drop × gravity, the drop negative going down); the usual code adds the drop and so
	/// slows a droplet as it falls.
	/// </para>
	/// <para>
	/// <b>Deterministic on every core.</b> The grid is split into tiles of <see cref="TileCells"/>, and
	/// a droplet can reach at most its lifetime plus its brush from where it started, which is less
	/// than half a tile. Tiles are run in four phases, a chequerboard of every other tile in each
	/// direction, so two tiles running at once are a whole tile apart and can never touch the same
	/// cell. Within a tile droplets run in a fixed order from a seeded generator. The result is the
	/// same however many threads there are and however they are scheduled.
	/// </para>
	/// <para>
	/// <b>Slumping and creep as grid passes</b> that read one buffer and write another, every flux
	/// between two cells worked out identically from both sides — so they conserve material exactly
	/// and do not care what order cells are visited in either.
	/// </para>
	/// <para>
	/// Pure arithmetic on arrays, with no engine types, so it runs and can be measured outside Unity.
	/// </para>
	/// </remarks>
	public static class Erosion
	{
		/// <summary>Cells per side of a tile. A droplet's reach must stay under half of it.</summary>
		public const int TileCells = 128;

		/// <summary>How far across its cell a droplet may start: short of the whole cell by more than float rounding at any grid size.</summary>
		private const float SpawnSpan = 0.99f;

		/// <summary>Runs every level of <see cref="ErosionSettings.Levels"/>, coarsest first.</summary>
		public static ErosionTally Run(ErosionGrid grid, IErosionGround ground, ErosionSettings settings, int seed)
		{
			if (grid == null || ground == null || settings == null)
			{
				throw new ArgumentNullException();
			}
			var total = new ErosionTally();
			if (settings.MaxFillMetres > 0f && grid.Ceiling == null)
			{
				grid.Ceiling = new float[grid.Height.Length];
				for (int i = 0; i < grid.Height.Length; i++)
				{
					grid.Ceiling[i] = grid.Height[i] + settings.MaxFillMetres;
				}
			}
			if (settings.Landscape != null && settings.Landscape.Steps > 0)
			{
				total.Add(Landscape(grid, ground, settings.Landscape, settings.Glacial));
			}
			ErosionLevel[] levels = settings.Levels ?? Array.Empty<ErosionLevel>();
			for (int l = 0; l < levels.Length; l++)
			{
				ErosionLevel level = levels[l];
				int step = Math.Max(1, level.Coarseness);
				int levelSeed = unchecked(seed * 31 + l);
				if (step == 1)
				{
					total.Add(RunLevel(grid, ground, settings, level, levelSeed, true));
					continue;
				}

				// A coarser copy of the ground, worn, and its change carried back to every sample.
				int width = (grid.Width - 1) / step + 1, depth = (grid.Depth - 1) / step + 1;
				if (width < 3 || depth < 3)
				{
					continue;
				}
				var fine = new int[width * depth];
				var height = new float[fine.Length];
				var soil = new float[fine.Length];
				for (int b = 0; b < depth; b++)
				{
					for (int a = 0; a < width; a++)
					{
						int i = b * step * grid.Width + a * step;
						fine[b * width + a] = i;
						height[b * width + a] = grid.Height[i];
						soil[b * width + a] = grid.Soil[i];
					}
				}
				var before = (float[])height.Clone();
				var soilBefore = (float[])soil.Clone();
				var coarse = new ErosionGrid(width, depth, grid.CellMetres * step, height, soil) { BaseLevel = grid.BaseLevel };
				if (grid.Ceiling != null)
				{
					coarse.Ceiling = new float[fine.Length];
					for (int n = 0; n < fine.Length; n++)
					{
						coarse.Ceiling[n] = grid.Ceiling[fine[n]];
					}
				}
				var coarseGround = new CoarseGround(ground, fine);
				var levelTally = RunLevel(coarse, coarseGround, settings, level, levelSeed, false);

				// What reached the heightmap is what counts: interpolation does not conserve a coarse grid's volume exactly.
				double change = Upsample(grid, step, width, depth, height, before, soil, soilBefore);
				levelTally.Lost = -change * grid.CellMetres * grid.CellMetres;
				total.Add(levelTally);
			}
			if (settings.Aeolian != null)
			{
				// The wind last: dunes are laid on whatever the water and ice left.
				(double cut, double laid) = AeolianErosion.Run(grid, ground, settings.Aeolian, unchecked(seed * 31 + 977));
				total.Eroded += cut;
				total.Lost += cut - laid;
			}
			return total;
		}

		/// <summary>
		/// The landscape evolution model on a coarse copy of the ground, its change carried back to
		/// every sample. What it removes leaves the scene, and is counted as lost.
		/// </summary>
		/// <param name="glacial">Glaciers to carve after the rivers, on the same grid; null for none.</param>
		public static ErosionTally Landscape(ErosionGrid grid, IErosionGround ground, LandscapeEvolutionSettings settings, GlacialSettings glacial = null)
		{
			int step = Math.Max(1, (int)MathF.Round(settings.CellMetres / grid.CellMetres));
			while (((grid.Width - 1) / step + 1) * (long)((grid.Depth - 1) / step + 1) > settings.MaximumCells)
			{
				step++;
			}
			int width = (grid.Width - 1) / step + 1, depth = (grid.Depth - 1) / step + 1;
			var tally = new ErosionTally();
			if (width < 3 || depth < 3)
			{
				return tally;
			}
			int count = width * depth;
			var fine = new int[count];
			var height = new float[count];
			var cells = new LandscapeCells
			{
				RiverErodibility = new float[count],
				Diffusion = new float[count],
				ChannelThreshold = new float[count],
				Sink = new bool[count],
				Hardness = (n, altitude) => ground.RockHardness(fine[n], altitude),
			};
			for (int b = 0; b < depth; b++)
			{
				for (int a = 0; a < width; a++)
				{
					int n = b * width + a;
					int i = b * step * grid.Width + a * step;
					fine[n] = i;
					height[n] = grid.Height[i];
					cells.RiverErodibility[n] = ground.SoilErodibility(i) * ground.Storminess(i) * ground.Rain(i);
					cells.Diffusion[n] = ground.Creep(i);
					cells.ChannelThreshold[n] = ground.ChannelThreshold(i);
				}
			}
			// Where the ground keeps its hollows, their floors are sinks: the water goes underground there.
			for (int b = 1; b < depth - 1; b++)
			{
				for (int a = 1; a < width - 1; a++)
				{
					int n = b * width + a;
					if (ground.DepressionKeeping(fine[n]) < 0.5f)
					{
						continue;
					}
					bool lowest = true;
					for (int dz = -1; dz <= 1 && lowest; dz++)
					{
						for (int dx = -1; dx <= 1; dx++)
						{
							if ((dx != 0 || dz != 0) && height[n + dz * width + dx] < height[n])
							{
								lowest = false;
								break;
							}
						}
					}
					cells.Sink[n] = lowest;
				}
			}

			var before = (float[])height.Clone();
			LandscapeEvolution.Run(height, width, depth, grid.CellMetres * step, cells, grid.BaseLevel, settings);
			if (glacial != null && glacial.Passes > 0)
			{
				// Glaciers take over the valleys the rivers made, where the climate fed them.
				var outlet = new bool[count];
				for (int n = 0; n < count; n++)
				{
					int a = n % width, b = n / width;
					outlet[n] = a == 0 || b == 0 || a == width - 1 || b == depth - 1 || height[n] < grid.BaseLevel || cells.Sink[n];
				}
				tally.Glaciated = GlacialErosion.Run(height, width, depth, grid.CellMetres * step,
					(n, altitude) => ground.IceBalance(fine[n], altitude), cells.Hardness, outlet, glacial);
			}

			/* Its change softened by a cell before it reaches the heightmap: the model routes every cell
			 * to one of its eight neighbours, so its channels run at multiples of 45°, and carried to
			 * samples a quarter its size unsmoothed they showed as blocks. The droplets that follow cut
			 * the fine channels back in, along the flow. */
			var delta = new float[count];
			for (int n = 0; n < count; n++)
			{
				delta[n] = height[n] - before[n];
			}
			var softened = new float[count];
			LandscapeEvolution.SmoothInto(delta, softened, width, depth, 1);
			for (int n = 0; n < count; n++)
			{
				height[n] = before[n] + softened[n];
			}

			double change = Upsample(grid, step, width, depth, height, before, null, null);
			tally.Eroded = Math.Max(0.0, -change) * grid.CellMetres * grid.CellMetres;
			tally.Lost = -change * grid.CellMetres * grid.CellMetres;
			return tally;
		}

		/// <summary>
		/// One level's rounds: rain, weathering (at full resolution, where it rounds the edges a
		/// sample can show), slumping and creep.
		/// </summary>
		public static ErosionTally RunLevel(ErosionGrid grid, IErosionGround ground, ErosionSettings settings, ErosionLevel level, int seed, bool weather)
		{
			int lifetime = Math.Max(1, Math.Min(settings.MaxLifetime, TileCells / 2 - settings.BrushRadius - 2));
			var brush = new Brush(Math.Max(0, settings.BrushRadius));
			var scratchHeight = new float[grid.Height.Length];
			var scratchSoil = new float[grid.Height.Length];
			var tangent = new float[grid.Height.Length];
			int rounds = Math.Max(1, settings.Rounds);
			// Creep is diffusion: per pass it evens out a share of each step, so on a cell k times wider
			// the same share smooths k² times the ground. Scaled back, it means the same at every level.
			float cellRatio = grid.CellMetres / Math.Max(1e-3f, settings.ReferenceCellMetres);
			float creepRate = settings.CreepRate / Math.Max(1f, cellRatio * cellRatio);

			var total = new ErosionTally();
			for (int round = 0; round < rounds; round++)
			{
				// Where the water gathers, re-routed each round as the valleys deepen.
				float[] discharge = Discharge(grid, ground, settings);
				total.Add(Hydraulic(grid, ground, settings, brush, lifetime, rounds, round, seed, level.DropletsPerCell, settings.Strength * level.Strength, discharge));
				if (weather)
				{
					Weather(grid, ground, settings);
				}
				for (int i = 0; i < settings.ThermalIterations; i++)
				{
					Thermal(grid, ground, settings, tangent, scratchHeight, scratchSoil);
				}
				for (int i = 0; i < settings.CreepIterations; i++)
				{
					Creep(grid, ground, creepRate, scratchHeight, scratchSoil);
				}
			}
			return total;
		}

		/// <summary>
		/// How much more than a hillside's wash a droplet carries at each cell: 1 + √(drainage area ÷
		/// the channel area), capped (<see cref="ErosionSettings.ChannelAreaSquareMetres"/>).
		/// </summary>
		public static float[] Discharge(ErosionGrid grid, IErosionGround ground, ErosionSettings settings)
		{
			float[] area = Drainage.Area(grid, ground);
			float reference = Math.Max(1f, settings.ChannelAreaSquareMetres);
			Parallel.For(0, grid.Depth, z =>
			{
				for (int x = 0; x < grid.Width; x++)
				{
					int i = z * grid.Width + x;
					float threshold = Math.Max(0.05f, ground.ChannelThreshold(i));
					area[i] = Math.Min(settings.MaxDischarge, 1f + MathF.Sqrt(area[i] / (reference * threshold)));
				}
			});
			return area;
		}

		/// <summary>
		/// Adds a coarse grid's change to every sample of the heightmap, bilinearly. Returns the
		/// heightmap's change of height summed over its samples.
		/// </summary>
		/// <remarks>With no coarse soil, the soil goes with whatever ground is cut from under it.</remarks>
		private static double Upsample(ErosionGrid grid, int step, int width, int depth, float[] height, float[] before, float[] soil, float[] soilBefore)
		{
			var rowChange = new double[grid.Depth];
			Parallel.For(0, grid.Depth, z =>
			{
				float gz = z / (float)step;
				int b = Math.Min((int)gz, depth - 2);
				float fz = Math.Min(1f, gz - b);
				double sum = 0.0;
				for (int x = 0; x < grid.Width; x++)
				{
					float gx = x / (float)step;
					int a = Math.Min((int)gx, width - 2);
					float fx = Math.Min(1f, gx - a);
					int n = b * width + a;
					float dh = Lerp2(height, before, n, width, fx, fz);
					float ds = soil != null ? Lerp2(soil, soilBefore, n, width, fx, fz) : Math.Min(0f, dh);
					int i = z * grid.Width + x;
					grid.Height[i] += dh;
					grid.Soil[i] = Math.Max(0f, grid.Soil[i] + ds);
					sum += dh;
				}
				rowChange[z] = sum;
			});
			double total = 0.0;
			foreach (double row in rowChange)
			{
				total += row;
			}
			return total;
		}

		private static float Lerp2(float[] after, float[] before, int n, int width, float fx, float fz)
		{
			float d00 = after[n] - before[n], d10 = after[n + 1] - before[n + 1];
			float d01 = after[n + width] - before[n + width], d11 = after[n + width + 1] - before[n + width + 1];
			float south = d00 + (d10 - d00) * fx;
			return south + ((d01 + (d11 - d01) * fx) - south) * fz;
		}

		/// <summary>The ground as a coarse grid's cells see it: each cell asks the heightmap sample it stands on.</summary>
		private sealed class CoarseGround : IErosionGround
		{
			private readonly IErosionGround ground;
			private readonly int[] fine;

			public CoarseGround(IErosionGround ground, int[] fine)
			{
				this.ground = ground;
				this.fine = fine;
			}

			public float Rain(int cell) => ground.Rain(fine[cell]);
			public float SoilErodibility(int cell) => ground.SoilErodibility(fine[cell]);
			public float Storminess(int cell) => ground.Storminess(fine[cell]);
			public float RockHardness(int cell, float altitudeMetres) => ground.RockHardness(fine[cell], altitudeMetres);
			public float SoilTalusTangent(int cell) => ground.SoilTalusTangent(fine[cell]);
			public float Creep(int cell) => ground.Creep(fine[cell]);
			public float DepressionKeeping(int cell) => ground.DepressionKeeping(fine[cell]);
			public float ChannelThreshold(int cell) => ground.ChannelThreshold(fine[cell]);
			public float IceBalance(int cell, float altitudeMetres) => ground.IceBalance(fine[cell], altitudeMetres);
			public float Aeolian(int cell) => ground.Aeolian(fine[cell]);
			public float DuneSand(int cell) => ground.DuneSand(fine[cell]);
		}

		// ── Running water ─────────────────────────────────────────────

		/// <summary>One round of rain over every tile, in four phases.</summary>
		/// <param name="droplets">Raindrops per cell over the whole run where the rain is 1.</param>
		/// <param name="strength">How much a droplet can carry, relative to <see cref="ErosionSettings.Capacity"/>.</param>
		/// <param name="discharge">Per cell, how much more than a hillside's wash a droplet carries there (<see cref="Discharge"/>); null for none.</param>
		public static ErosionTally Hydraulic(ErosionGrid grid, IErosionGround ground, ErosionSettings settings, Brush brush,
			int lifetime, int rounds, int round, int seed, float droplets, float strength, float[] discharge = null)
		{
			int tilesX = (grid.Width + TileCells - 1) / TileCells;
			int tilesZ = (grid.Depth + TileCells - 1) / TileCells;
			var tallies = new ErosionTally[tilesX * tilesZ];
			for (int phase = 0; phase < 4; phase++)
			{
				int ax = phase & 1, az = phase >> 1;
				int countX = (tilesX - ax + 1) / 2, countZ = (tilesZ - az + 1) / 2;
				Parallel.For(0, countX * countZ, k =>
				{
					int tx = ax + 2 * (k % countX), tz = az + 2 * (k / countX);
					tallies[tz * tilesX + tx] = Tile(grid, ground, settings, brush, lifetime, rounds, tx, tz, round, seed, droplets, strength, discharge);
				});
			}
			// Summed in tile order, so the tally is as deterministic as the ground.
			var total = new ErosionTally();
			foreach (ErosionTally tally in tallies)
			{
				total.Add(tally);
			}
			return total;
		}

		private static ErosionTally Tile(ErosionGrid grid, IErosionGround ground, ErosionSettings settings, Brush brush,
			int lifetime, int rounds, int tx, int tz, int round, int seed, float droplets, float strength, float[] discharge)
		{
			var tally = new ErosionTally();
			var random = new SplitMix(Mix((uint)tx * 0x8da6b343u ^ Mix((uint)tz * 0xd8163841u ^ Mix((uint)round * 0xcb1ab31fu ^ Mix((uint)seed)))));
			int x0 = tx * TileCells, z0 = tz * TileCells;
			int x1 = Math.Min(x0 + TileCells, grid.Width - 1), z1 = Math.Min(z0 + TileCells, grid.Depth - 1);
			for (int z = z0; z < z1; z++)
			{
				for (int x = x0; x < x1; x++)
				{
					int cell = z * grid.Width + x;
					if (grid.Height[cell] < grid.BaseLevel)
					{
						continue;
					}
					float rate = droplets * ground.Rain(cell) / rounds;
					int count = (int)rate;
					if (random.NextFloat() < rate - count)
					{
						count++;
					}
					for (int d = 0; d < count; d++)
					{
						/* Inside the cell, never on its far edge: in float, 2559 plus a fraction just under
						 * one rounds to 2560, the last row, whose bilinear neighbours are off the grid. */
						float ox = random.NextFloat() * SpawnSpan, oz = random.NextFloat() * SpawnSpan;
						Droplet(grid, ground, settings, brush, lifetime, strength, discharge, x + ox, z + oz, ref tally);
					}
				}
			}
			return tally;
		}

		private static void Droplet(ErosionGrid grid, IErosionGround ground, ErosionSettings settings, Brush brush, int lifetime,
			float strength, float[] discharge, float px, float pz, ref ErosionTally tally)
		{
			int width = grid.Width;
			float[] height = grid.Height;
			float dirX = 0f, dirZ = 0f;
			float speed = settings.InitialSpeed;
			float water = 1f;
			float sediment = 0f;
			float cellArea = grid.CellMetres * grid.CellMetres;
			tally.Droplets++;

			for (int life = 0; life < lifetime; life++)
			{
				int ix = (int)px, iz = (int)pz;
				float fx = px - ix, fz = pz - iz;
				int i = iz * width + ix;
				float h00 = height[i], h10 = height[i + 1], h01 = height[i + width], h11 = height[i + width + 1];
				float gradX = (h10 - h00) * (1f - fz) + (h11 - h01) * fz;
				float gradZ = (h01 - h00) * (1f - fx) + (h11 - h10) * fx;
				float here = h00 * (1f - fx) * (1f - fz) + h10 * fx * (1f - fz) + h01 * (1f - fx) * fz + h11 * fx * fz;

				dirX = dirX * settings.Inertia - gradX * (1f - settings.Inertia);
				dirZ = dirZ * settings.Inertia - gradZ * (1f - settings.Inertia);
				float length = MathF.Sqrt(dirX * dirX + dirZ * dirZ);
				if (length < 1e-6f)
				{
					break; // a perfectly flat pit: nowhere to go
				}
				dirX /= length;
				dirZ /= length;
				float nx = px + dirX, nz = pz + dirZ;
				if (nx < 0f || nz < 0f || nx >= grid.Width - 1 || nz >= grid.Depth - 1)
				{
					tally.Lost += sediment * cellArea;
					return; // off the grid, with whatever it carries
				}
				tally.Steps++;

				float next = Bilinear(height, width, nx, nz);
				float drop = next - here;
				if (next < grid.BaseLevel)
				{
					// Into the sea or the lake: the current dies and its load settles at the mouth.
					float spilled = Spread(grid, brush, ix, iz, sediment);
					tally.Deposited += (sediment - spilled) * cellArea;
					tally.Lost += spilled * cellArea;
					return;
				}

				float capacity = MathF.Max(-drop / grid.CellMetres, settings.MinSlope) * speed * water
					* settings.Capacity * strength * ground.Storminess(i) * (discharge != null ? discharge[i] : 1f);
				if (drop > 0f)
				{
					// Uphill: it fills the hollow it is in, exactly where the hollow is, as far as its load allows.
					float amount = MathF.Min(drop, sediment);
					float spilled = Deposit(grid, ix, iz, fx, fz, amount);
					sediment -= amount;
					tally.Deposited += (amount - spilled) * cellArea;
					tally.Lost += spilled * cellArea;
				}
				else if (sediment > capacity)
				{
					/* Slowing: it sheds the excess over its brush, as a sheet. Dropped on the four cells
					 * under it, every droplet that slows on the flat left a dot, and the flats came out
					 * stippled. */
					float amount = (sediment - capacity) * settings.DepositRate;
					float spilled = Spread(grid, brush, ix, iz, amount);
					sediment -= amount;
					tally.Deposited += (amount - spilled) * cellArea;
					tally.Lost += spilled * cellArea;
				}
				else
				{
					// Never more than the drop, or the droplet would dig a pit below where it is going.
					float want = MathF.Min((capacity - sediment) * settings.ErodeRate * ground.SoilErodibility(i), -drop);
					float taken = Wear(grid, ground, settings, brush, ix, iz, want);
					sediment += taken;
					tally.Eroded += taken * cellArea;
				}

				speed = MathF.Min(settings.MaxSpeed, MathF.Sqrt(MathF.Max(0f, speed * speed - drop * settings.Gravity)));
				water *= 1f - settings.Evaporation;
				px = nx;
				pz = nz;
			}

			// Its life over, it leaves its load where it stopped rather than taking it out of the world.
			float left = Spread(grid, brush, (int)px, (int)pz, sediment);
			tally.Deposited += (sediment - left) * cellArea;
			tally.Lost += left * cellArea;
		}

		/// <summary>
		/// Takes up to <paramref name="want"/> metres from the cells around (ix, iz), soil first, then
		/// rock at its hardness's share. Returns what was taken.
		/// </summary>
		private static float Wear(ErosionGrid grid, IErosionGround ground, ErosionSettings settings, Brush brush, int ix, int iz, float want)
		{
			if (want <= 0f)
			{
				return 0f;
			}
			int width = grid.Width;
			float[] height = grid.Height, soil = grid.Soil;
			// The brush's weights over the cells actually on the grid and above the water, so its share is all handed out.
			float weightSum = 0f;
			for (int k = 0; k < brush.Count; k++)
			{
				int x = ix + brush.X[k], z = iz + brush.Z[k];
				if (x >= 0 && z >= 0 && x < width && z < grid.Depth && height[z * width + x] >= grid.BaseLevel)
				{
					weightSum += brush.Weight[k];
				}
			}
			if (weightSum <= 0f)
			{
				return 0f;
			}

			float taken = 0f;
			for (int k = 0; k < brush.Count; k++)
			{
				int x = ix + brush.X[k], z = iz + brush.Z[k];
				if (x < 0 || z < 0 || x >= width || z >= grid.Depth)
				{
					continue;
				}
				int j = z * width + x;
				if (height[j] < grid.BaseLevel)
				{
					continue;
				}
				float share = want * brush.Weight[k] / weightSum;
				float fromSoil = MathF.Min(share, soil[j]);
				float rest = share - fromSoil;
				float fromRock = 0f;
				if (rest > 0f)
				{
					float hardness = ground.RockHardness(j, height[j] - soil[j]);
					float softness = 1f - hardness;
					fromRock = rest * settings.BedrockRate * softness * softness;
				}
				soil[j] -= fromSoil;
				height[j] -= fromSoil + fromRock;
				taken += fromSoil + fromRock;
			}
			return taken;
		}

		/// <summary>
		/// Lays <paramref name="amount"/> metres of soil over the four cells around a point, by their
		/// bilinear weights. Returns what would have built a cell above its ceiling and was carried on instead.
		/// </summary>
		private static float Deposit(ErosionGrid grid, int ix, int iz, float fx, float fz, float amount)
		{
			if (amount <= 0f)
			{
				return 0f;
			}
			int width = grid.Width;
			int i = iz * width + ix;
			float overflow = Lay(grid, i, amount * (1f - fx) * (1f - fz));
			overflow += Lay(grid, i + 1, amount * fx * (1f - fz));
			overflow += Lay(grid, i + width, amount * (1f - fx) * fz);
			overflow += Lay(grid, i + width + 1, amount * fx * fz);
			return overflow;
		}

		/// <summary>Lays soil on one cell up to its ceiling. Returns what did not fit.</summary>
		private static float Lay(ErosionGrid grid, int i, float amount)
		{
			float room = grid.Ceiling != null ? Math.Max(0f, grid.Ceiling[i] - grid.Height[i]) : float.MaxValue;
			float laid = Math.Min(amount, room);
			grid.Height[i] += laid;
			grid.Soil[i] += laid;
			return amount - laid;
		}

		/// <summary>Lays <paramref name="amount"/> metres of soil over the brush around a cell, by its weights. Returns what did not fit under the ceilings.</summary>
		private static float Spread(ErosionGrid grid, Brush brush, int ix, int iz, float amount)
		{
			if (amount <= 0f)
			{
				return 0f;
			}
			int width = grid.Width;
			float weightSum = 0f;
			for (int k = 0; k < brush.Count; k++)
			{
				int x = ix + brush.X[k], z = iz + brush.Z[k];
				if (x >= 0 && z >= 0 && x < width && z < grid.Depth)
				{
					weightSum += brush.Weight[k];
				}
			}
			float overflow = 0f;
			for (int k = 0; k < brush.Count; k++)
			{
				int x = ix + brush.X[k], z = iz + brush.Z[k];
				if (x < 0 || z < 0 || x >= width || z >= grid.Depth)
				{
					continue;
				}
				overflow += Lay(grid, z * width + x, amount * brush.Weight[k] / weightSum);
			}
			return overflow;
		}

		private static float Bilinear(float[] height, int width, float x, float z)
		{
			int ix = (int)x, iz = (int)z;
			float fx = x - ix, fz = z - iz;
			int i = iz * width + ix;
			return height[i] * (1f - fx) * (1f - fz) + height[i + 1] * fx * (1f - fz)
				+ height[i + width] * (1f - fx) * fz + height[i + width + 1] * fx * fz;
		}

		// ── Weathering ────────────────────────────────────────────────

		/// <summary>
		/// One round of weathering: rock at the surface turns to soil where it stands, quickest where
		/// it is bare and soft, slowing as the soil it makes covers it.
		/// </summary>
		/// <remarks>
		/// The soil production function measured on real hillslopes (Heimsath et al. 1997): rock turns
		/// to soil at a rate that falls off exponentially with the soil already over it. The surface
		/// does not move — the top of the rock becomes the bottom of the soil — so nothing is made or
		/// lost; what changes is that slumping and creep can now move it, which is what rounds edges.
		/// </remarks>
		public static void Weather(ErosionGrid grid, IErosionGround ground, ErosionSettings settings)
		{
			if (settings.WeatheringMetres <= 0f)
			{
				return;
			}
			int width = grid.Width;
			float[] height = grid.Height, soil = grid.Soil;
			float depth = Math.Max(1e-3f, settings.WeatheringDepthMetres);
			Parallel.For(0, grid.Depth, z =>
			{
				for (int x = 0; x < width; x++)
				{
					int i = z * width + x;
					if (height[i] < grid.BaseLevel)
					{
						continue;
					}
					float hardness = ground.RockHardness(i, height[i] - soil[i]);
					soil[i] += settings.WeatheringMetres * (1f - 0.8f * hardness) * MathF.Exp(-soil[i] / depth);
				}
			});
		}

		// ── Slumping ──────────────────────────────────────────────────

		private static readonly int[] NeighbourX = { -1, 0, 1, -1, 1, -1, 0, 1 };
		private static readonly int[] NeighbourZ = { -1, -1, -1, 0, 0, 1, 1, 1 };
		private static readonly float[] NeighbourDistance = { 1.41421356f, 1f, 1.41421356f, 1f, 1f, 1.41421356f, 1f, 1.41421356f };

		/// <summary>
		/// One pass of slumping: wherever a step between neighbours is steeper than the higher cell's
		/// material stands, part of the excess slides down and lands as loose soil.
		/// </summary>
		/// <remarks>
		/// Soil stands at its talus angle; bare rock at an angle that rises with its hardness, so
		/// hard beds keep their cliffs and soft ones lie back. Rock that fails comes down as soil,
		/// which is where talus cones come from.
		/// </remarks>
		public static void Thermal(ErosionGrid grid, IErosionGround ground, ErosionSettings settings, float[] tangent,
			float[] nextHeight, float[] nextSoil)
		{
			int width = grid.Width, depth = grid.Depth;
			float[] height = grid.Height, soil = grid.Soil;
			float cell = grid.CellMetres;
			float softTangent = MathF.Tan(settings.RockAngleSoftDegrees * MathF.PI / 180f);
			float hardTangent = MathF.Tan(settings.RockAngleHardDegrees * MathF.PI / 180f);

			Parallel.For(0, depth, z =>
			{
				for (int x = 0; x < width; x++)
				{
					int i = z * width + x;
					if (soil[i] > settings.SoilPresenceMetres)
					{
						tangent[i] = ground.SoilTalusTangent(i);
					}
					else
					{
						// Interpolated by angle, not by tangent: the tangent runs away near the vertical.
						float hardness = ground.RockHardness(i, height[i] - soil[i]);
						float angle = settings.RockAngleSoftDegrees + (settings.RockAngleHardDegrees - settings.RockAngleSoftDegrees) * hardness;
						tangent[i] = Math.Clamp(MathF.Tan(angle * MathF.PI / 180f), softTangent, hardTangent);
					}
				}
			});

			float rate = settings.ThermalRate * 0.5f / 8f;
			Parallel.For(0, depth, z =>
			{
				for (int x = 0; x < width; x++)
				{
					int i = z * width + x;
					float hi = height[i];
					float outflow = 0f, inflow = 0f;
					for (int n = 0; n < 8; n++)
					{
						int xn = x + NeighbourX[n], zn = z + NeighbourZ[n];
						if (xn < 0 || zn < 0 || xn >= width || zn >= depth)
						{
							continue;
						}
						int j = zn * width + xn;
						float step = hi - height[j];
						float run = NeighbourDistance[n] * cell;
						if (step > 0f)
						{
							float excess = step - tangent[i] * run;
							if (excess > 0f)
							{
								outflow += excess * rate;
							}
						}
						else
						{
							float excess = -step - tangent[j] * run;
							if (excess > 0f)
							{
								inflow += excess * rate;
							}
						}
					}
					nextHeight[i] = hi - outflow + inflow;
					nextSoil[i] = MathF.Max(0f, soil[i] - outflow) + inflow;
				}
			});
			Array.Copy(nextHeight, height, height.Length);
			Array.Copy(nextSoil, soil, soil.Length);
		}

		// ── Creep ─────────────────────────────────────────────────────

		/// <summary>
		/// One pass of soil creep: loose material drifts from higher cells to their lower neighbours in
		/// proportion to the difference, which rounds crests and softens gully walls.
		/// </summary>
		/// <remarks>Only soil creeps; a pair's flux is limited by the higher cell's soil, and worked out the same from both sides.</remarks>
		/// <param name="creepRate">Share of each step evened out per pass at creep 1, already scaled to the grid's cell.</param>
		public static void Creep(ErosionGrid grid, IErosionGround ground, float creepRate, float[] nextHeight, float[] nextSoil)
		{
			int width = grid.Width, depth = grid.Depth;
			float[] height = grid.Height, soil = grid.Soil;
			float rate = creepRate * 0.25f;

			Parallel.For(0, depth, z =>
			{
				for (int x = 0; x < width; x++)
				{
					int i = z * width + x;
					float hi = height[i];
					float net = 0f;
					for (int n = 0; n < 4; n++)
					{
						int xn = x + (n == 0 ? -1 : n == 1 ? 1 : 0);
						int zn = z + (n == 2 ? -1 : n == 3 ? 1 : 0);
						if (xn < 0 || zn < 0 || xn >= width || zn >= depth)
						{
							continue;
						}
						int j = zn * width + xn;
						float step = hi - height[j];
						float kappa = 0.5f * (ground.Creep(i) + ground.Creep(j));
						if (step > 0f)
						{
							net -= MathF.Min(step * kappa * rate, soil[i] * 0.25f);
						}
						else if (step < 0f)
						{
							net += MathF.Min(-step * kappa * rate, soil[j] * 0.25f);
						}
					}
					nextHeight[i] = hi + net;
					nextSoil[i] = MathF.Max(0f, soil[i] + net);
				}
			});
			Array.Copy(nextHeight, height, height.Length);
			Array.Copy(nextSoil, soil, soil.Length);
		}

		// ── Helpers ───────────────────────────────────────────────────

		/// <summary>The cells a droplet wears and their weights: a disc, heavier at the centre.</summary>
		public sealed class Brush
		{
			public readonly int[] X;
			public readonly int[] Z;
			public readonly float[] Weight;
			public int Count => X.Length;

			public Brush(int radius)
			{
				var xs = new System.Collections.Generic.List<int>();
				var zs = new System.Collections.Generic.List<int>();
				var ws = new System.Collections.Generic.List<float>();
				for (int z = -radius; z <= radius; z++)
				{
					for (int x = -radius; x <= radius; x++)
					{
						float d = MathF.Sqrt(x * x + z * z);
						if (d <= radius + 1e-3f)
						{
							xs.Add(x);
							zs.Add(z);
							ws.Add(1f - d / (radius + 1f));
						}
					}
				}
				X = xs.ToArray();
				Z = zs.ToArray();
				Weight = ws.ToArray();
			}
		}

		private static uint Mix(uint x)
		{
			x ^= x >> 16;
			x *= 0x7feb352du;
			x ^= x >> 15;
			x *= 0x846ca68bu;
			x ^= x >> 16;
			return x;
		}

		/// <summary>A small, fast, seedable generator: the same seed gives the same droplets.</summary>
		private struct SplitMix
		{
			private ulong state;

			public SplitMix(uint seed)
			{
				state = seed * 0x9E3779B97F4A7C15ul + 0x632BE59BD9B4E019ul;
			}

			public float NextFloat()
			{
				state += 0x9E3779B97F4A7C15ul;
				ulong z = state;
				z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
				z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
				z ^= z >> 31;
				return (z >> 40) * (1f / 16777216f);
			}
		}
	}
}
#endif
