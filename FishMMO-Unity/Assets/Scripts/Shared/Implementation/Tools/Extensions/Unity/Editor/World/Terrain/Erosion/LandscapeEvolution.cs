#if UNITY_EDITOR
using System;
using System.Threading.Tasks;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How the landscape evolution model runs: how long, how hard, at what scale.</summary>
	public sealed class LandscapeEvolutionSettings
	{
		/// <summary>The cell size the model runs at, in metres, at least; a big scene runs coarser, to stay near <see cref="MaximumCells"/>.</summary>
		public float CellMetres = 8f;

		/// <summary>The most cells the model runs on. Each step is a routing and a pass down every river.</summary>
		public int MaximumCells = 600_000;

		/// <summary>Time steps. Valleys organise into networks over a few hundred.</summary>
		public int Steps = 300;

		/// <summary>
		/// The stream power law's erodibility per step on temperate ground in the reference rain:
		/// how fast rivers cut. Each cell's own is this times its ground's (<see cref="LandscapeCells.RiverErodibility"/>).
		/// </summary>
		public float RiverErodibility = 0.05f;

		/// <summary>
		/// The drainage area below which water runs as sheet wash rather than a channel, in m², on
		/// temperate ground: the hillslope's domain, rounded by creep. The ground's channel threshold
		/// scales it.
		/// </summary>
		public float ChannelAreaSquareMetres = 300f;

		/// <summary>
		/// Share of their full rate rivers keep cutting the hardest rock at; softness raises it with its
		/// square. Low, because a hard cap must hold a mesa's top while the soft beds under its edge wear
		/// back: at 0.1 the caps resisted only about four times better than the soft beds and every
		/// bench was cut through.
		/// </summary>
		public float HardRockFloor = 0.03f;

		/// <summary>
		/// How much more drainage area hard rock needs before a channel starts, at hardness 1, over
		/// soft: the channel area is multiplied by 1 + this × hardness².
		/// </summary>
		public float HardChannelScale = 20f;

		/// <summary>The stream power law's area exponent m (slope exponent 1): 0.4–0.5 on real rivers, giving concave profiles.</summary>
		public float AreaExponent = 0.45f;

		/// <summary>Hillslope diffusivity per step on temperate ground, as a share of a cell's curvature smoothed: rounds the slopes between channels.</summary>
		public float HillslopeDiffusion = 0.005f;

		/// <summary>
		/// How far the planet's own shape is held, in metres: the scale below which the model may
		/// rebuild the ground, and above which it is pinned to the planet's.
		/// </summary>
		public float PinScaleMetres = 900f;

		/// <summary>Share of the gap between the planet's smoothed shape and the ground's that is restored each step, as uplift.</summary>
		public float PinRate = 0.08f;
	}

	/// <summary>
	/// What the landscape evolution model reads about each cell: the biome's way of wearing and the
	/// climate's rain, as the model's own quantities.
	/// </summary>
	/// <remarks>
	/// This is where kinds of ground erode differently. A badland's soft unvegetated beds and flash
	/// floods give a high erodibility and a low channel threshold — dense, sharp gullies; a
	/// periglacial slope's creep gives a high diffusivity — smooth, rounded ground; a rainforest's roots
	/// hold its soil against heavy rain; karst's hollows are sinks, draining underground; a world
	/// with no rain gets no rivers at all, only creep.
	/// </remarks>
	public sealed class LandscapeCells
	{
		/// <summary>Per cell, how readily rivers cut relative to temperate ground in the reference rain: erodibility, roots, storms and rain together.</summary>
		public float[] RiverErodibility;
		/// <summary>Per cell, the hillslope diffusivity relative to temperate ground (the biome's soil creep).</summary>
		public float[] Diffusion;
		/// <summary>Per cell, the channel threshold relative to temperate ground.</summary>
		public float[] ChannelThreshold;
		/// <summary>Per cell, true where the ground drains underground: water reaching it leaves, as at the sea (karst sinkholes).</summary>
		public bool[] Sink;
		/// <summary>The rock's hardness at a cell and altitude, 0 … 1.</summary>
		public Func<int, float, float> Hardness;
	}

	/// <summary>
	/// Rebuilds a scene's ground the way rivers and slopes build real ground: every slope part of a
	/// drainage basin, valleys branching down to the smallest, ridges sharpening between them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A landscape evolution model</b>, the kind geomorphology uses to grow mountain ranges
	/// (Braun &amp; Willett's FastScape, 2013), run for a few hundred steps:
	/// </para>
	/// <list type="bullet">
	/// <item><b>Rivers</b> cut by the stream power law, E = K·A^m·S: drainage area A, slope S. Solved
	/// implicitly down each river from its mouth, so each step is one pass and stays stable however
	/// large: a cell is lowered towards its already-lowered receiver. K is the ground's — its
	/// erodibility and storms from the biome, the softness of the rock at the bed, the rain.</item>
	/// <item><b>Slopes</b> creep by diffusion between the channels, which rounds what the rivers leave
	/// into hillsides.</item>
	/// <item><b>Water</b> is routed from every cell down the steepest way over the ground with its
	/// hollows filled (a priority flood from the edge and the sea, Barnes et al. 2014), so a hollow's
	/// water leaves over its rim — and the rim, carrying the whole hollow's water, is cut down step by
	/// step until the hollow drains, as rivers really breach the basins in their way.</item>
	/// </list>
	/// <para>
	/// <b>The planet keeps its shape.</b> Left alone the model would wear the scene down towards its
	/// edges. Each step adds back, as uplift, part of the difference between the planet's ground and
	/// the eroded ground both smoothed over <see cref="LandscapeEvolutionSettings.PinScaleMetres"/>:
	/// so the mountains, basins and coastline stay where the globe has them, and everything smaller
	/// than that is rebuilt by the rivers. It is the same balance that holds a real range up — uplift
	/// against erosion — with the planet supplying the uplift.
	/// </para>
	/// <para>
	/// Runs on a coarser grid than the heightmap (about eight metres): the networks it builds are
	/// wider than a sample, and droplets then add the finest gullies at full resolution.
	/// Deterministic: routing and rivers are sequential, diffusion double-buffered.
	/// </para>
	/// </remarks>
	public static class LandscapeEvolution
	{
		private const float FillStep = 1e-3f;

		/// <summary>Evolves <paramref name="height"/> (width × depth cells of <paramref name="cellMetres"/>) in place.</summary>
		/// <param name="cells">What each cell's ground is like.</param>
		/// <param name="baseLevel">A sea's surface: cells below it are outlets and never change.</param>
		public static void Run(float[] height, int width, int depth, float cellMetres, LandscapeCells cells,
			float baseLevel, LandscapeEvolutionSettings settings)
		{
			int count = width * depth;
			float[] planet = (float[])height.Clone();
			int pin = Math.Max(1, (int)MathF.Round(settings.PinScaleMetres / cellMetres));
			float[] planetSmooth = Smooth(planet, width, depth, pin);
			var smooth = new float[count];

			var filled = new float[count];
			var receiver = new int[count];
			var distance = new float[count];
			var order = new int[count];
			var area = new float[count];
			var next = new float[count];
			var outlet = new bool[count];
			for (int n = 0; n < count; n++)
			{
				int x = n % width, z = n / width;
				outlet[n] = x == 0 || z == 0 || x == width - 1 || z == depth - 1 || height[n] < baseLevel
					|| (cells.Sink != null && cells.Sink[n]);
			}
			// Per cell, the share of curvature creep smooths per step, kept under the explicit scheme's stability limit.
			var diffusion = new float[count];
			var channelArea = new float[count];
			for (int n = 0; n < count; n++)
			{
				diffusion[n] = Math.Clamp(settings.HillslopeDiffusion * (cells.Diffusion != null ? cells.Diffusion[n] : 1f), 0f, 0.24f);
				channelArea[n] = settings.ChannelAreaSquareMetres * Math.Max(0.05f, cells.ChannelThreshold != null ? cells.ChannelThreshold[n] : 1f);
			}
			float cellArea = cellMetres * cellMetres;
			float diagonal = cellMetres * 1.41421356f;

			for (int step = 0; step < settings.Steps; step++)
			{
				// Uplift: what holds the planet's shape against the rivers.
				if (settings.PinRate > 0f)
				{
					SmoothInto(height, smooth, width, depth, pin);
					Parallel.For(0, depth, z =>
					{
						for (int x = 0; x < width; x++)
						{
							int n = z * width + x;
							if (!outlet[n])
							{
								height[n] += settings.PinRate * (planetSmooth[n] - smooth[n]);
							}
						}
					});
				}

				int ordered = Route(height, width, depth, cellMetres, diagonal, outlet, filled, receiver, distance, order);

				for (int n = 0; n < count; n++)
				{
					area[n] = cellArea;
				}
				for (int t = ordered - 1; t >= 0; t--)
				{
					int n = order[t];
					int r = receiver[n];
					if (r >= 0)
					{
						area[r] += area[n];
					}
				}

				// Rivers, from each mouth upstream.
				for (int t = 0; t < ordered; t++)
				{
					int n = order[t];
					int r = receiver[n];
					/* Not in a hollow: its floor is a lake until the river at its rim cuts down below it,
					 * and the routing across a filled hollow's floor follows the order the flood reached it
					 * — rows and columns — which the rivers cut as straight parallel grooves. */
					if (r < 0 || height[n] <= height[r] || filled[n] > height[n] + 0.5f * FillStep)
					{
						continue;
					}
					/* Sheet wash, not a channel, until enough ground drains here: the slopes stay creep's. Hard
					 * rock needs far more water gathered before it is cut at all, which is why a caprock
					 * plateau is crossed by a few canyons rather than a mesh of gullies. */
					float hardness = cells.Hardness != null ? cells.Hardness(n, height[n]) : 0.5f;
					float channelled = area[n] - channelArea[n] * (1f + settings.HardChannelScale * hardness * hardness);
					if (channelled <= 0f)
					{
						continue;
					}
					float softness = 1f - hardness;
					float k = settings.RiverErodibility * cells.RiverErodibility[n] * (settings.HardRockFloor + (1f - settings.HardRockFloor) * softness * softness);
					float f = k * MathF.Pow(channelled, settings.AreaExponent) / distance[n];
					height[n] = (height[n] + f * height[r]) / (1f + f);
				}

				// Slopes, by diffusion.
				if (settings.HillslopeDiffusion > 0f)
				{
					Parallel.For(0, depth, z =>
					{
						for (int x = 0; x < width; x++)
						{
							int n = z * width + x;
							if (outlet[n])
							{
								next[n] = height[n];
								continue;
							}
							float sum = height[n - 1] + height[n + 1] + height[n - width] + height[n + width];
							next[n] = height[n] + diffusion[n] * (sum * 0.25f - height[n]);
						}
					});
					Array.Copy(next, height, count);
				}
			}
		}

		/// <summary>
		/// Priority flood from the outlets: lists cells from the outlets upward, and gives each its
		/// steepest way down over the ground with its hollows filled. Returns how many were listed.
		/// </summary>
		internal static int Route(float[] height, int width, int depth, float run, float diagonal, bool[] outlet,
			float[] filled, int[] receiver, float[] distance, int[] order)
		{
			int count = width * depth;
			var heap = new CellHeap(count);
			var queued = new bool[count];
			for (int n = 0; n < count; n++)
			{
				filled[n] = height[n];
				receiver[n] = -1;
				if (outlet[n])
				{
					queued[n] = true;
					heap.Push(height[n], n);
				}
			}
			int ordered = 0;
			while (heap.Count > 0)
			{
				int c = heap.Pop();
				order[ordered++] = c;
				int cx = c % width, cz = c / width;
				for (int dz = -1; dz <= 1; dz++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						int x = cx + dx, z = cz + dz;
						if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= width || z >= depth)
						{
							continue;
						}
						int n = z * width + x;
						if (queued[n])
						{
							continue;
						}
						queued[n] = true;
						filled[n] = Math.Max(height[n], filled[c] + FillStep);
						heap.Push(filled[n], n);
					}
				}
			}
			for (int n = 0; n < count; n++)
			{
				if (outlet[n])
				{
					continue;
				}
				int x = n % width, z = n / width;
				float best = 0f;
				for (int dz = -1; dz <= 1; dz++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						if (dx == 0 && dz == 0)
						{
							continue;
						}
						int m = (z + dz) * width + x + dx;
						float d = dx != 0 && dz != 0 ? diagonal : run;
						float slope = (filled[n] - filled[m]) / d;
						if (slope > best)
						{
							best = slope;
							receiver[n] = m;
							distance[n] = d;
						}
					}
				}
			}
			return ordered;
		}

		private static float[] Smooth(float[] source, int width, int depth, int radius)
		{
			var result = new float[source.Length];
			SmoothInto(source, result, width, depth, radius);
			return result;
		}

		/// <summary>Three box blurs of <paramref name="radius"/> cells each way: close to a Gaussian, edges clamped.</summary>
		internal static void SmoothInto(float[] source, float[] result, int width, int depth, int radius)
		{
			var a = (float[])source.Clone();
			var b = new float[source.Length];
			for (int pass = 0; pass < 3; pass++)
			{
				BoxX(a, b, width, depth, radius);
				BoxZ(b, a, width, depth, radius);
			}
			Array.Copy(a, result, a.Length);
		}

		private static void BoxX(float[] from, float[] to, int width, int depth, int radius)
		{
			float scale = 1f / (2 * radius + 1);
			Parallel.For(0, depth, z =>
			{
				int row = z * width;
				double sum = 0.0;
				for (int k = -radius; k <= radius; k++)
				{
					sum += from[row + Math.Clamp(k, 0, width - 1)];
				}
				for (int x = 0; x < width; x++)
				{
					to[row + x] = (float)(sum * scale);
					sum += from[row + Math.Min(width - 1, x + radius + 1)] - from[row + Math.Max(0, x - radius)];
				}
			});
		}

		private static void BoxZ(float[] from, float[] to, int width, int depth, int radius)
		{
			float scale = 1f / (2 * radius + 1);
			Parallel.For(0, width, x =>
			{
				double sum = 0.0;
				for (int k = -radius; k <= radius; k++)
				{
					sum += from[Math.Clamp(k, 0, depth - 1) * width + x];
				}
				for (int z = 0; z < depth; z++)
				{
					to[z * width + x] = (float)(sum * scale);
					sum += from[Math.Min(depth - 1, z + radius + 1) * width + x] - from[Math.Max(0, z - radius) * width + x];
				}
			});
		}
	}
}
#endif
