#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The constants drainage is worked out with.</summary>
	public sealed class DrainageSettings
	{
		/// <summary>How much each cell of a filled hollow stands above the one it drains to, in metres: gives a flat a direction.</summary>
		public double FillStep = 1e-3;

		/// <summary>A hollow shallower than this, in metres from its floor to its spill point, is filled and passed over rather than holding a lake.</summary>
		public float MinLakeDepthMetres = 3f;

		/// <summary>A hollow smaller than this at its spill level, in m², is filled rather than holding a lake.</summary>
		public double MinLakeAreaSquareMetres = 120_000.0;

		/// <summary>
		/// The least discharge, in m³/s, that runs all year as a river with water in it. About a large
		/// creek's: a catchment of some 20 km² under a temperate climate's runoff.
		/// </summary>
		public float MinRiverDischarge = 0.2f;

		/// <summary>
		/// The drainage area, in m², past which water gathers into a channel even where too little
		/// falls to keep it running: a dry wash, a wadi — riverbed, no water.
		/// </summary>
		public double MinChannelAreaSquareMetres = 40_000_000.0;

		/// <summary>
		/// How far an overflowing lake's outflow has cut its sill down, metres per (m³/s)^0.5 of outflow.
		/// A river leaving a lake wears its outlet the faster the more it carries (stream power), so over
		/// a landscape's life a shallow basin drains and only a deep one keeps a lake, lower than its
		/// first spill point. 0 keeps every lake full to its sill.
		/// </summary>
		/// <remarks>
		/// Noise-built ground is pitted with closed basins no real land of its age keeps: with no breaching
		/// Arthis had lakes over 14.6% of its land. Lakes cover about 3.7% of Earth's land outside the ice
		/// sheets (Verpoorter et al. 2014); 200 brings Arthis to 3.6%, and 20 only to 11.9%. Measured on
		/// Arthis's own cells with the offline harness.
		/// </remarks>
		public float BreachMetres = 200f;

		/// <summary>
		/// How far a karst sinkhole's water may travel underground to come out again, in metres: it rises as a
		/// spring at the lowest ground within this reach that stands <see cref="SpringDropMetres"/> under the
		/// sink, as karst water resurges where the soluble rock gives out at the valley floor. 0 loses it.
		/// </summary>
		public float SpringReachMetres = 4000f;

		/// <summary>How much lower than the sink its spring must be, metres: water underground still runs downhill.</summary>
		public float SpringDropMetres = 5f;
	}

	/// <summary>How a river begins or ends.</summary>
	public enum RiverEnd : byte
	{
		/// <summary>Where enough water first gathers: a spring, the head of a valley.</summary>
		Source = 0,
		/// <summary>Out of a lake, over its spill point; or into one.</summary>
		Lake = 1,
		/// <summary>Into the sea.</summary>
		Sea = 2,
		/// <summary>Into a larger river.</summary>
		Confluence = 3,
		/// <summary>Into the ground: a sinkhole, or a basin with no lake to hold it.</summary>
		Sink = 4,
		/// <summary>Across the edge of what was cut: a scene's boundary.</summary>
		Edge = 5,
	}

	/// <summary>One lake drainage found: a hollow and the water standing in it.</summary>
	public sealed class DrainageLake
	{
		public int Id;
		/// <summary>The water's surface, in the ground's metres.</summary>
		public float Level;
		/// <summary>The hollow's spill point: the level a full lake stands at.</summary>
		public float SpillLevel;
		/// <summary>The lowest ground under it.</summary>
		public float Floor;
		/// <summary>Every cell of the hollow, lowest first; the lake covers those below <see cref="Level"/>.</summary>
		public int[] Cells;
		/// <summary>The water reaching it, m³/s: its catchment's runoff and the rain on it.</summary>
		public float Inflow;
		/// <summary>What leaves over the spill point, m³/s; 0 for a terminal lake.</summary>
		public float Outflow;
		/// <summary>True when evaporation takes all it gets before it fills: a salt lake, a playa.</summary>
		public bool Terminal => Outflow <= 0f;
		/// <summary>The cell its outflow runs on into; −1 for a terminal lake.</summary>
		public int OutletCell = -1;
		/// <summary>Area of open water at <see cref="Level"/>, m².</summary>
		public double AreaSquareMetres;
		/// <summary>How far its outflow has cut its sill below <see cref="SpillLevel"/>, metres.</summary>
		public float Breach;
		/// <summary>True when its outlet has cut down to its floor and no lake is left: the river runs through.</summary>
		public bool Drained;
	}

	/// <summary>One river: a run of cells from where it starts to where it joins something bigger.</summary>
	public sealed class DrainageRiver
	{
		public int Id;
		/// <summary>
		/// Its cells, from upstream to downstream; the last is the cell it ends in (sea, lake, the river it
		/// joins), and for a river leaving a lake the first is the lake's cell it leaves from, so the river
		/// begins at the water.
		/// </summary>
		public int[] Cells;
		/// <summary>Discharge at each cell, m³/s.</summary>
		public float[] Discharge;
		/// <summary>Drainage area at each cell, m².</summary>
		public float[] Area;
		public RiverEnd Start;
		public RiverEnd End;
		/// <summary>The lake it leaves or enters, or the river it joins; −1 for none.</summary>
		public int StartLake = -1;
		public int EndLake = -1;
		public int JoinsRiver = -1;
	}

	/// <summary>What drainage found.</summary>
	public sealed class DrainageResult
	{
		/// <summary>The ground with every hollow filled to its spill point (and a hair over, to drain).</summary>
		public double[] Filled;
		/// <summary>Per cell, the neighbour its water runs to; −1 at an outlet.</summary>
		public int[] Receiver;
		/// <summary>Per cell, m³/s of water passing through it.</summary>
		public float[] Discharge;
		/// <summary>Per cell, m² of ground rain falls on draining through it.</summary>
		public float[] Area;
		/// <summary>Per cell, the lake standing over it, or −1.</summary>
		public int[] LakeOf;
		/// <summary>Per cell, the river whose run holds it, or −1.</summary>
		public int[] RiverOf;
		public List<DrainageLake> Lakes = new List<DrainageLake>();
		public List<DrainageRiver> Rivers = new List<DrainageRiver>();
		/// <summary>m³/s reaching the sea or the edge, and m³/s lakes evaporated: with what sinks took, the runoff's whole budget.</summary>
		public double ToOutlets;
		public double Evaporated;
		/// <summary>m³/s the land ran off in all: what <see cref="ToOutlets"/> and <see cref="Evaporated"/> account for between them.</summary>
		public double Runoff;
		/// <summary>The ground water runs over: the given ground with every overflowing lake's outlet cut down by its breach.</summary>
		public float[] Ground;
		/// <summary>Basins whose outlets cut down to their floors, so they hold no lake at all.</summary>
		public int DrainedBasins;
	}

	/// <summary>
	/// Where water goes on a ground: which way it runs from every cell, where it gathers into lakes and
	/// how high they stand, how much passes each point, and the rivers that carry it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Routing</b> is a priority flood from the outlets (Barnes et al. 2014): cells are taken from the
	/// lowest outlet upwards, so every hollow is filled to its spill point and its water given a way out
	/// over the rim. Each cell then sends its water to its steepest neighbour on the filled ground.
	/// </para>
	/// <para>
	/// <b>Lakes stand where the water balance puts them.</b> A hollow worth a lake (deeper than
	/// <see cref="DrainageSettings.MinLakeDepthMetres"/>, larger than <see cref="DrainageSettings.MinLakeAreaSquareMetres"/>)
	/// collects its catchment's runoff and the rain on its own surface, and loses what its surface
	/// evaporates. If the water outruns evaporation the lake fills to its spill point and the surplus
	/// runs on as its outflow — a humid climate's lake, with a river leaving it. If evaporation wins, the
	/// lake stands only as high as makes its surface lose exactly what it receives: a terminal lake, a
	/// salt lake or a dry playa, and nothing runs on downstream. Lakes are worked out in the order the
	/// water reaches them, so an upstream lake's evaporation is lost to every lake below it.
	/// </para>
	/// <para>
	/// <b>A partial lake fills its hollow from the bottom</b> by height alone, as though the hollow were
	/// one basin all the way down. A hollow with two pits joined above the lake's level is drawn as one
	/// lake over both; the pits' shapes are then the scene's concern, which floods the real ground.
	/// </para>
	/// <para>
	/// <b>Rivers</b> are runs of cells carrying at least <see cref="DrainageSettings.MinRiverDischarge"/>,
	/// or dry washes where the drainage area passes <see cref="DrainageSettings.MinChannelAreaSquareMetres"/>
	/// with too little water to run. Each cell belongs to one run: at a confluence the run carrying
	/// more water goes on and the other ends there, so a trunk is one river from its farthest source to
	/// its mouth.
	/// </para>
	/// <para>Works on any graph of cells (a sphere's, a test's plane). Pure arithmetic: deterministic everywhere.</para>
	/// </remarks>
	public static class DrainageSolver
	{
		public const double SecondsPerYear = 365.25 * 86400.0;

		/// <param name="height">Ground height per cell, metres.</param>
		/// <param name="area">Cell area, m².</param>
		/// <param name="neighbourStart">Compressed neighbour lists (see <see cref="CubeSphereGrid"/>).</param>
		/// <param name="neighbours">The neighbours.</param>
		/// <param name="neighbourMetres">Distance to each neighbour, metres.</param>
		/// <param name="sea">True under the sea: where rivers end and water leaves.</param>
		/// <param name="sink">True where the ground swallows water (karst sinkholes); null for none.</param>
		/// <param name="precipitation">Rain per cell, m/year.</param>
		/// <param name="runoff">What of it runs off the land, m/year (the rest evaporates or transpires).</param>
		/// <param name="evaporation">What an open water surface would evaporate per cell, m/year.</param>
		public static DrainageResult Solve(float[] height, double[] area, int[] neighbourStart, int[] neighbours, float[] neighbourMetres,
			bool[] sea, bool[] sink, float[] precipitation, float[] runoff, float[] evaporation, DrainageSettings settings)
		{
			settings ??= new DrainageSettings();
			DrainageResult result = SolveOnce(height, area, neighbourStart, neighbours, neighbourMetres, sea, sink, precipitation, runoff, evaporation, settings, false);
			result.Ground = height;
			if (settings.BreachMetres <= 0f)
			{
				return result;
			}

			/* Each overflowing lake's outlet cut down by what its outflow wears away, measured from the
			 * spill point its basin first had, and everything worked out again on the cut ground — where a
			 * lowered lake's dry floor drains into it and it fills to its cut. Repeated, because a lowered
			 * lake often spills into the next basin down and the two stand as one at that one's sill until
			 * its outlet is cut too. */
			var originalSpill = new float[height.Length];
			foreach (DrainageLake lake in result.Lakes)
			{
				foreach (int c in lake.Cells)
				{
					originalSpill[c] = Math.Max(originalSpill[c], lake.SpillLevel);
				}
			}
			var cut = (float[])height.Clone();
			var drainedFloors = new HashSet<int>();
			for (int pass = 0; pass < MaxBreachPasses; pass++)
			{
				bool changed = false;
				foreach (DrainageLake lake in result.Lakes)
				{
					if (lake.Terminal)
					{
						continue;
					}
					float spill = lake.SpillLevel;
					int start = lake.Cells[0];
					foreach (int c in lake.Cells)
					{
						spill = Math.Max(spill, originalSpill[c]);
						// Where the lake's own water gathers before it leaves: the most-fed cell of the basin.
						if (result.Discharge[c] > result.Discharge[start])
						{
							start = c;
						}
					}
					float target = spill - settings.BreachMetres * (float)Math.Sqrt(lake.Outflow);
					if (target - lake.Floor < settings.MinLakeDepthMetres)
					{
						target = lake.Floor;
						drainedFloors.Add(lake.Cells[0]);
					}
					if (lake.Level <= target + 0.5f)
					{
						continue;
					}
					changed = true;
					double level = target;
					var seen = new HashSet<int>();
					var inLake = new HashSet<int>(lake.Cells);
					for (int c = start; c >= 0 && seen.Add(c); c = result.Receiver[c])
					{
						level -= settings.FillStep * 10.0;
						if (cut[c] <= level && !inLake.Contains(c))
						{
							break;
						}
						cut[c] = (float)Math.Min(cut[c], level);
					}
				}
				if (!changed)
				{
					break;
				}
				result = SolveOnce(cut, area, neighbourStart, neighbours, neighbourMetres, sea, sink, precipitation, runoff, evaporation, settings, false);
				result.Ground = cut;
			}
			result.DrainedBasins = drainedFloors.Count;
			foreach (DrainageLake lake in result.Lakes)
			{
				float spill = 0f;
				foreach (int c in lake.Cells)
				{
					spill = Math.Max(spill, originalSpill[c]);
				}
				lake.Breach = spill > lake.Level ? spill - lake.Level : 0f;
			}
			return result;
		}

		/// <summary>
		/// Per cell, the spring a karst sinkhole's water rises at, or −1: the lowest cell within
		/// <see cref="DrainageSettings.SpringReachMetres"/> that stands <see cref="DrainageSettings.SpringDropMetres"/>
		/// under it, is no sinkhole or sea itself and holds no lake, and comes later in the order the water is worked
		/// out in, so its water is added before that cell passes it on. With no such cell the water is lost to the
		/// ground (or the sea beyond reach).
		/// </summary>
		private static int[] Springs(float[] height, int[] neighbourStart, int[] neighbours, float[] neighbourMetres, bool[] sea, bool[] sink,
			int[] order, int[] component, int[] lakeOfComponent, DrainageSettings settings)
		{
			if (sink == null || settings.SpringReachMetres <= 0f)
			{
				return null;
			}
			int count = height.Length;
			var rank = new int[count];
			for (int t = 0; t < order.Length; t++)
			{
				rank[order[t]] = t;
			}
			var spring = new int[count];
			var distance = new Dictionary<int, float>();
			var frontier = new SortedSet<(float metres, int cell)>();
			for (int c = 0; c < count; c++)
			{
				spring[c] = -1;
				if (!sink[c] || sea[c])
				{
					continue;
				}
				// Outward from the sink by distance over the ground (Dijkstra on the cell graph), out to the reach.
				distance.Clear();
				frontier.Clear();
				distance[c] = 0f;
				frontier.Add((0f, c));
				int best = -1;
				float ceiling = height[c] - settings.SpringDropMetres;
				while (frontier.Count > 0)
				{
					(float metres, int at) = frontier.Min;
					frontier.Remove(frontier.Min);
					if (metres > distance[at])
					{
						continue;
					}
					if (at != c && height[at] < ceiling && !sink[at] && !sea[at] && rank[at] < rank[c]
						&& (component[at] < 0 || lakeOfComponent[component[at]] < 0) && (best < 0 || height[at] < height[best]))
					{
						best = at;
					}
					for (int k = neighbourStart[at]; k < neighbourStart[at + 1]; k++)
					{
						int m = neighbours[k];
						float next = metres + neighbourMetres[k];
						if (next > settings.SpringReachMetres || (distance.TryGetValue(m, out float known) && known <= next))
						{
							continue;
						}
						distance[m] = next;
						frontier.Add((next, m));
					}
				}
				spring[c] = best;
			}
			return spring;
		}

		/// <summary>The most rounds of cutting lakes' outlets and working the water out again.</summary>
		public const int MaxBreachPasses = 6;

		private static DrainageResult SolveOnce(float[] height, double[] area, int[] neighbourStart, int[] neighbours, float[] neighbourMetres,
			bool[] sea, bool[] sink, float[] precipitation, float[] runoff, float[] evaporation, DrainageSettings settings, bool breach)
		{
			int count = height.Length;
			var result = new DrainageResult
			{
				Filled = new double[count],
				Receiver = new int[count],
				Discharge = new float[count],
				Area = new float[count],
				LakeOf = new int[count],
				RiverOf = new int[count],
			};

			// ── Routing ────────────────────────────────────────────────
			var outlets = new bool[count];
			bool any = false;
			for (int c = 0; c < count; c++)
			{
				outlets[c] = sea[c] || (sink != null && sink[c]);
				any |= outlets[c];
			}
			if (!any)
			{
				// A world with nowhere to drain drains to its lowest point.
				int lowest = 0;
				for (int c = 1; c < count; c++)
				{
					if (height[c] < height[lowest])
					{
						lowest = c;
					}
				}
				outlets[lowest] = true;
			}
			int[] order = Flood(height, neighbourStart, neighbours, outlets, settings.FillStep, result.Filled);
			int[] receiver = result.Receiver;
			for (int c = 0; c < count; c++)
			{
				receiver[c] = -1;
				if (outlets[c])
				{
					continue;
				}
				double best = 0.0;
				for (int k = neighbourStart[c]; k < neighbourStart[c + 1]; k++)
				{
					int m = neighbours[k];
					double slope = (result.Filled[c] - result.Filled[m]) / Math.Max(1e-3, neighbourMetres[k]);
					if (slope > best)
					{
						best = slope;
						receiver[c] = m;
					}
				}
			}

			// ── Hollows ────────────────────────────────────────────────
			double tolerance = settings.FillStep * 0.5;
			int[] component = Components(height, result.Filled, neighbourStart, neighbours, outlets, tolerance, out int components);
			var cellsOf = new List<int>[components];
			for (int c = 0; c < count; c++)
			{
				if (component[c] >= 0)
				{
					(cellsOf[component[c]] ??= new List<int>()).Add(c);
				}
			}
			var lakeOfComponent = new int[components];
			var exitsLeft = new int[components];
			var pool = new double[components];
			var poolArea = new double[components];
			for (int k = 0; k < components; k++)
			{
				lakeOfComponent[k] = -1;
				List<int> cells = cellsOf[k];
				double surface = 0.0, floor = double.MaxValue, minFilled = double.MaxValue;
				foreach (int c in cells)
				{
					surface += area[c];
					floor = Math.Min(floor, height[c]);
					minFilled = Math.Min(minFilled, result.Filled[c]);
				}
				double spill = minFilled - settings.FillStep;
				if (spill - floor < settings.MinLakeDepthMetres || surface < settings.MinLakeAreaSquareMetres)
				{
					continue;
				}
				cells.Sort((a, b) => height[a].CompareTo(height[b]) != 0 ? height[a].CompareTo(height[b]) : a.CompareTo(b));
				var lake = new DrainageLake
				{
					Id = result.Lakes.Count,
					SpillLevel = (float)spill,
					Level = (float)spill,
					Floor = (float)floor,
					Cells = cells.ToArray(),
				};
				lakeOfComponent[k] = lake.Id;
				result.Lakes.Add(lake);
				foreach (int c in cells)
				{
					int r = receiver[c];
					if (r < 0 || component[r] != k)
					{
						exitsLeft[k]++;
					}
				}
			}

			// ── Karst springs: where each sinkhole's water comes out again ─
			int[] spring = Springs(height, neighbourStart, neighbours, neighbourMetres, sea, sink, order, component, lakeOfComponent, settings);

			// ── Water, from the highest cells down ─────────────────────
			double[] q = new double[count];
			double[] drained = new double[count];
			for (int c = 0; c < count; c++)
			{
				q[c] = Math.Max(0f, runoff[c]) * area[c] / SecondsPerYear;
				result.Runoff += q[c];
				// Only ground rain falls on drains: an airless moon's hollows gather no channels at all.
				drained[c] = precipitation[c] > 0f ? area[c] : 0.0;
				result.LakeOf[c] = -1;
				result.RiverOf[c] = -1;
			}
			for (int t = order.Length - 1; t >= 0; t--)
			{
				int c = order[t];
				int r = receiver[c];
				int k = component[c];
				int lakeId = k >= 0 ? lakeOfComponent[k] : -1;
				if (lakeId >= 0 && (r < 0 || component[r] != k))
				{
					// Leaving the hollow: into the lake's pool until every way out has delivered.
					pool[k] += q[c];
					poolArea[k] += drained[c];
					if (--exitsLeft[k] == 0)
					{
						DrainageLake lake = result.Lakes[lakeId];
						Balance(lake, pool[k], height, area, precipitation, runoff, evaporation, result, breach ? settings.BreachMetres : 0f, settings.MinLakeDepthMetres);
						if (lake.Outflow > 0f && r >= 0)
						{
							q[r] += lake.Outflow;
							drained[r] += poolArea[k];
							lake.OutletCell = r;
						}
						else if (lake.Outflow > 0f)
						{
							result.ToOutlets += lake.Outflow;
						}
					}
					continue;
				}
				if (r >= 0)
				{
					q[r] += q[c];
					drained[r] += drained[c];
				}
				else if (spring != null && spring[c] >= 0)
				{
					// Down a sinkhole and out at its spring, which the order reaches later (it stands lower).
					q[spring[c]] += q[c];
					drained[spring[c]] += drained[c];
				}
				else
				{
					result.ToOutlets += q[c];
				}
			}
			for (int c = 0; c < count; c++)
			{
				result.Discharge[c] = (float)q[c];
				result.Area[c] = (float)drained[c];
			}
			// A hollow no water reaches holds no lake: dropped, the rest renumbered.
			var holding = new List<DrainageLake>(result.Lakes.Count);
			foreach (DrainageLake lake in result.Lakes)
			{
				if (lake.AreaSquareMetres > 0.0)
				{
					lake.Id = holding.Count;
					holding.Add(lake);
				}
			}
			result.Lakes = holding;
			foreach (DrainageLake lake in result.Lakes)
			{
				foreach (int c in lake.Cells)
				{
					if (height[c] < lake.Level)
					{
						result.LakeOf[c] = lake.Id;
					}
				}
			}
			Trace(result, height, sea, outlets, settings);
			return result;
		}

		/// <summary>
		/// A lake's level from its water balance: full with an outflow if what reaches it outruns what
		/// its full surface evaporates, otherwise as high as makes the two equal.
		/// </summary>
		private static void Balance(DrainageLake lake, double pooled, float[] height, double[] area, float[] precipitation, float[] runoff, float[] evaporation,
			DrainageResult result, float breachMetres, float minLakeDepth)
		{
			/* The pool counted every cell of the hollow as land, running off. A cell under the lake runs
			 * nothing off; it takes the rain on it and loses its evaporation instead. */
			double need = 0.0;
			double surface = 0.0;
			int covered = 0;
			for (; covered < lake.Cells.Length; covered++)
			{
				int c = lake.Cells[covered];
				double loss = (Math.Max(0f, runoff[c]) - precipitation[c] + evaporation[c]) * area[c] / SecondsPerYear;
				if (need + loss > pooled)
				{
					break;
				}
				need += loss;
				surface += area[c];
			}
			lake.Inflow = (float)pooled;
			if (covered >= lake.Cells.Length)
			{
				lake.Outflow = (float)Math.Max(1e-6, pooled - need);
				lake.Level = lake.SpillLevel;
				lake.AreaSquareMetres = surface;
				// Its outflow has worn its sill down: the lake stands lower, over less of its basin, or is gone.
				float cut = breachMetres > 0f ? Math.Min(lake.SpillLevel - lake.Floor, breachMetres * (float)Math.Sqrt(lake.Outflow)) : 0f;
				if (cut > 0f)
				{
					lake.Breach = cut;
					lake.Level = lake.SpillLevel - cut;
					if (lake.Level - lake.Floor < minLakeDepth)
					{
						lake.Drained = true;
						lake.Level = lake.Floor;
					}
					need = 0.0;
					surface = 0.0;
					foreach (int c in lake.Cells)
					{
						if (height[c] < lake.Level)
						{
							need += (Math.Max(0f, runoff[c]) - precipitation[c] + evaporation[c]) * area[c] / SecondsPerYear;
							surface += area[c];
						}
					}
					lake.AreaSquareMetres = surface;
					lake.Outflow = (float)Math.Max(1e-6, pooled - need);
				}
				result.Evaporated += pooled - lake.Outflow;
				return;
			}
			/* Terminal: the level reaches part way up the next cell's ground, as far as the water left over
			 * would cover of that cell's loss. */
			lake.Outflow = 0f;
			result.Evaporated += pooled;
			lake.AreaSquareMetres = surface;
			int next = lake.Cells[covered];
			float below = covered > 0 ? height[lake.Cells[covered - 1]] : lake.Floor;
			double nextLoss = (Math.Max(0f, runoff[next]) - precipitation[next] + evaporation[next]) * area[next] / SecondsPerYear;
			float share = nextLoss > 0.0 ? (float)Math.Max(0.0, Math.Min(1.0, (pooled - need) / nextLoss)) : 0f;
			lake.Level = below + (height[next] - below) * share;
		}

		/// <summary>Priority flood from the outlets; fills <paramref name="filled"/> and returns the cells in the order taken.</summary>
		internal static int[] Flood(float[] height, int[] neighbourStart, int[] neighbours, bool[] outlets, double fillStep, double[] filled)
		{
			int count = height.Length;
			var order = new int[count];
			var queued = new bool[count];
			var heap = new DoubleHeap(count);
			for (int c = 0; c < count; c++)
			{
				filled[c] = height[c];
				if (outlets[c])
				{
					queued[c] = true;
					heap.Push(height[c], c);
				}
			}
			int taken = 0;
			while (heap.Count > 0)
			{
				int c = heap.Pop();
				order[taken++] = c;
				for (int k = neighbourStart[c]; k < neighbourStart[c + 1]; k++)
				{
					int m = neighbours[k];
					if (queued[m])
					{
						continue;
					}
					queued[m] = true;
					filled[m] = Math.Max(height[m], filled[c] + fillStep);
					heap.Push(filled[m], m);
				}
			}
			if (taken < count)
			{
				Array.Resize(ref order, taken);
			}
			return order;
		}

		/// <summary>Groups the cells under filled water (filled above their ground) into connected hollows; −1 elsewhere.</summary>
		private static int[] Components(float[] height, double[] filled, int[] neighbourStart, int[] neighbours, bool[] outlets, double tolerance, out int components)
		{
			int count = height.Length;
			var component = new int[count];
			for (int c = 0; c < count; c++)
			{
				component[c] = -1;
			}
			components = 0;
			var stack = new Stack<int>();
			for (int c = 0; c < count; c++)
			{
				if (component[c] >= 0 || outlets[c] || filled[c] - height[c] <= tolerance)
				{
					continue;
				}
				int id = components++;
				component[c] = id;
				stack.Push(c);
				while (stack.Count > 0)
				{
					int a = stack.Pop();
					for (int k = neighbourStart[a]; k < neighbourStart[a + 1]; k++)
					{
						int m = neighbours[k];
						if (component[m] < 0 && !outlets[m] && filled[m] - height[m] > tolerance)
						{
							component[m] = id;
							stack.Push(m);
						}
					}
				}
			}
			return component;
		}

		/// <summary>Splits the channel cells into rivers, each from its source to where it ends.</summary>
		private static void Trace(DrainageResult result, float[] height, bool[] sea, bool[] outlets, DrainageSettings settings)
		{
			int count = height.Length;
			int[] receiver = result.Receiver;
			var channel = new bool[count];
			for (int c = 0; c < count; c++)
			{
				channel[c] = !outlets[c] && result.LakeOf[c] < 0
					&& (result.Discharge[c] >= settings.MinRiverDischarge || result.Area[c] >= settings.MinChannelAreaSquareMetres);
			}
			// Each channel cell's main upstream: the channel neighbour draining into it with the most water.
			var main = new int[count];
			var upstream = new int[count];
			for (int c = 0; c < count; c++)
			{
				main[c] = -1;
			}
			for (int c = 0; c < count; c++)
			{
				int r = receiver[c];
				if (!channel[c] || r < 0 || !channel[r])
				{
					continue;
				}
				upstream[r]++;
				int m = main[r];
				if (m < 0 || result.Discharge[c] > result.Discharge[m] || (result.Discharge[c] == result.Discharge[m] && result.Area[c] > result.Area[m])
					|| (result.Discharge[c] == result.Discharge[m] && result.Area[c] == result.Area[m] && c < m))
				{
					main[r] = c;
				}
			}
			var lakeOutlet = new Dictionary<int, int>();
			foreach (DrainageLake lake in result.Lakes)
			{
				if (lake.OutletCell >= 0)
				{
					lakeOutlet[lake.OutletCell] = lake.Id;
				}
			}

			// Sources: channel cells nothing in the network runs into, biggest first so the ids rank by size.
			var sources = new List<int>();
			for (int c = 0; c < count; c++)
			{
				if (channel[c] && upstream[c] == 0)
				{
					sources.Add(c);
				}
			}
			var runOf = new Dictionary<int, DrainageRiver>();
			var cells = new List<int>();
			foreach (int source in sources)
			{
				cells.Clear();
				int c = source;
				bool fromLake = lakeOutlet.TryGetValue(source, out int lakeId);
				var river = new DrainageRiver { Start = fromLake ? RiverEnd.Lake : RiverEnd.Source, StartLake = fromLake ? lakeId : -1 };
				if (fromLake)
				{
					// The lake's cell it leaves from: the one whose water runs into its first cell.
					foreach (int lc in result.Lakes[lakeId].Cells)
					{
						if (receiver[lc] == source && result.LakeOf[lc] == lakeId)
						{
							cells.Add(lc);
							break;
						}
					}
				}
				while (true)
				{
					cells.Add(c);
					int r = receiver[c];
					if (r < 0)
					{
						river.End = RiverEnd.Sink;
						break;
					}
					if (outlets[r])
					{
						cells.Add(r);
						river.End = sea[r] ? RiverEnd.Sea : RiverEnd.Sink;
						break;
					}
					if (result.LakeOf[r] >= 0)
					{
						cells.Add(r);
						river.End = RiverEnd.Lake;
						river.EndLake = result.LakeOf[r];
						break;
					}
					if (!channel[r])
					{
						// Into a hollow too small for a lake, or one a terminal lake's water never left.
						cells.Add(r);
						river.End = RiverEnd.Sink;
						break;
					}
					if (main[r] != c)
					{
						cells.Add(r);
						river.End = RiverEnd.Confluence;
						break;
					}
					c = r;
				}
				river.Cells = cells.ToArray();
				runOf[source] = river;
			}

			// Ids by size at the mouth, largest first; the confluences then name the river joined.
			var rivers = new List<DrainageRiver>(runOf.Values);
			rivers.Sort((a, b) =>
			{
				float qa = result.Discharge[a.Cells[a.Cells.Length - 2 >= 0 ? a.Cells.Length - 2 : 0]];
				float qb = result.Discharge[b.Cells[b.Cells.Length - 2 >= 0 ? b.Cells.Length - 2 : 0]];
				int byQ = qb.CompareTo(qa);
				return byQ != 0 ? byQ : a.Cells[0].CompareTo(b.Cells[0]);
			});
			for (int i = 0; i < rivers.Count; i++)
			{
				DrainageRiver river = rivers[i];
				river.Id = i;
				river.Discharge = new float[river.Cells.Length];
				river.Area = new float[river.Cells.Length];
				bool fromWater = river.Start == RiverEnd.Lake && river.Cells.Length > 1 && result.LakeOf[river.Cells[0]] >= 0;
				for (int v = 0; v < river.Cells.Length; v++)
				{
					// A lake's cell carries what the lake lets out, not the lake's own gathering.
					int at = fromWater && v == 0 ? river.Cells[1] : river.Cells[v];
					river.Discharge[v] = result.Discharge[at];
					river.Area[v] = result.Area[at];
				}
				// Every cell of its run but the last (and a lake's first), which belong to the water at its ends.
				for (int v = fromWater ? 1 : 0; v < river.Cells.Length - 1; v++)
				{
					result.RiverOf[river.Cells[v]] = i;
				}
			}
			foreach (DrainageRiver river in rivers)
			{
				if (river.End == RiverEnd.Confluence)
				{
					river.JoinsRiver = result.RiverOf[river.Cells[river.Cells.Length - 1]];
				}
			}
			result.Rivers = rivers;
		}

		/// <summary>A binary min-heap of cells keyed by double, ties broken by cell index so the order never depends on insertion.</summary>
		private sealed class DoubleHeap
		{
			private readonly double[] keys;
			private readonly int[] cells;
			public int Count { get; private set; }

			public DoubleHeap(int capacity)
			{
				keys = new double[Math.Max(1, capacity)];
				cells = new int[Math.Max(1, capacity)];
			}

			public void Push(double key, int cell)
			{
				int i = Count++;
				while (i > 0)
				{
					int parent = (i - 1) >> 1;
					if (!Less(key, cell, keys[parent], cells[parent]))
					{
						break;
					}
					keys[i] = keys[parent];
					cells[i] = cells[parent];
					i = parent;
				}
				keys[i] = key;
				cells[i] = cell;
			}

			public int Pop()
			{
				int top = cells[0];
				int last = --Count;
				double key = keys[last];
				int cell = cells[last];
				int i = 0;
				while (true)
				{
					int child = 2 * i + 1;
					if (child >= Count)
					{
						break;
					}
					if (child + 1 < Count && Less(keys[child + 1], cells[child + 1], keys[child], cells[child]))
					{
						child++;
					}
					if (!Less(keys[child], cells[child], key, cell))
					{
						break;
					}
					keys[i] = keys[child];
					cells[i] = cells[child];
					i = child;
				}
				keys[i] = key;
				cells[i] = cell;
				return top;
			}

			private static bool Less(double ka, int ca, double kb, int cb) => ka < kb || (ka == kb && ca < cb);
		}
	}
}
#endif
