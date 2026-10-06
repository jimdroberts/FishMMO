#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What stands on each sample of a scene's ground once its rivers and lakes are laid.</summary>
	public enum WaterKind : byte
	{
		None = 0,
		/// <summary>In a river's channel, under running water.</summary>
		River = 1,
		/// <summary>In a dry wash's channel: bed, no water.</summary>
		DryWash = 2,
		/// <summary>Under a lake.</summary>
		Lake = 3,
		/// <summary>On a river's bank or levee: dry ground the river shaped.</summary>
		Bank = 4,
		/// <summary>On a bar or beach the river laid: sand or gravel sloping gently into the water, just above it.</summary>
		Bar = 5,
		/// <summary>On the salt flat round a terminal lake: lake bed the water stands off most of the year.</summary>
		Playa = 6,
	}

	/// <summary>Per sample of a scene's ground: the water on it, its surface, and which river or lake it belongs to.</summary>
	public sealed class SceneWaterGrid
	{
		public readonly int Width;
		public readonly int Depth;
		/// <summary>Metres between samples.</summary>
		public readonly float Spacing;
		/// <summary>Scene position of sample (0, 0).</summary>
		public readonly float OriginX;
		public readonly float OriginZ;
		/// <summary>The water's surface, scene metres; negative infinity on dry ground (a dry wash included).</summary>
		public readonly float[] Level;
		public readonly WaterKind[] Kind;
		/// <summary>The river's or lake's id; −1 for none.</summary>
		public readonly int[] Body;
		/// <summary>On a bar, 0 gravel … 1 sand: what the water beside it was slow enough to drop.</summary>
		public readonly float[] Sand;
		/// <summary>On a bar or a salt flat, the surface of the water beside it; negative infinity elsewhere.</summary>
		public readonly float[] Shore;

		public SceneWaterGrid(int width, int depth, float spacing, float originX, float originZ)
		{
			Width = width;
			Depth = depth;
			Spacing = spacing;
			OriginX = originX;
			OriginZ = originZ;
			int count = width * depth;
			Level = new float[count];
			Kind = new WaterKind[count];
			Body = new int[count];
			Sand = new float[count];
			Shore = new float[count];
			Clear();
		}

		/// <summary>Back to dry ground everywhere.</summary>
		public void Clear()
		{
			for (int i = 0; i < Level.Length; i++)
			{
				Level[i] = float.NegativeInfinity;
				Kind[i] = WaterKind.None;
				Body[i] = -1;
				Sand[i] = 0f;
				Shore[i] = float.NegativeInfinity;
			}
		}

		public float EastOf(int x) => OriginX + x * Spacing;
		public float NorthOf(int z) => OriginZ + z * Spacing;

		/// <summary>The sample nearest a scene position, or −1 off the grid.</summary>
		public int SampleAt(float east, float north)
		{
			int x = (int)Math.Round((east - OriginX) / Spacing), z = (int)Math.Round((north - OriginZ) / Spacing);
			return x < 0 || z < 0 || x >= Width || z >= Depth ? -1 : z * Width + x;
		}

		/// <summary>
		/// The highest water surface among the four samples around a scene position: what a point near a
		/// shore is under, if it is under anything. Negative infinity where all four are dry.
		/// </summary>
		public float SurfaceNear(float east, float north)
		{
			float gx = (east - OriginX) / Spacing, gz = (north - OriginZ) / Spacing;
			int x0 = (int)Math.Floor(gx), z0 = (int)Math.Floor(gz);
			float best = float.NegativeInfinity;
			for (int dz = 0; dz <= 1; dz++)
			{
				for (int dx = 0; dx <= 1; dx++)
				{
					int x = x0 + dx, z = z0 + dz;
					if (x >= 0 && z >= 0 && x < Width && z < Depth)
					{
						best = Math.Max(best, Level[z * Width + x]);
					}
				}
			}
			return best;
		}

		/// <summary>The kind of water at the nearest sample.</summary>
		public WaterKind KindAt(float east, float north)
		{
			int i = SampleAt(east, north);
			return i >= 0 ? Kind[i] : WaterKind.None;
		}
	}

	/// <summary>Carves rivers' channels into a scene's ground and floods its lakes.</summary>
	/// <remarks>
	/// <para>
	/// <b>A channel</b> is a parabola from bank to bank, its deepest line moved toward the outside of a
	/// bend so the outer bank is the cut bank and the inner the shallow point bar. <b>Banks</b> beyond it
	/// are cut back no steeper than <see cref="RiverSettings.BankDegrees"/>, so a river crossing a rise
	/// runs in a cutting and through a ridge in a gorge; where the ground beside it is lower than its
	/// water, a levee is built up to hold it. Ground already lower than the channel is left: deep water
	/// in a pool, not a raised bed.
	/// </para>
	/// <para>
	/// <b>Where rivers meet</b> each sample takes the lowest any of them cuts it to, and belongs to the
	/// river whose channel it is most inside.
	/// </para>
	/// <para>Pure arithmetic on arrays: deterministic, and testable outside Unity.</para>
	/// </remarks>
	public static class WaterCarver
	{
		/// <summary>
		/// Lays every river: marks its channel and banks on <paramref name="grid"/> and, when
		/// <paramref name="write"/>, cuts the ground in <paramref name="height"/>.
		/// </summary>
		/// <param name="tideLine">
		/// The highest the sea reaches (mean level and the tide's range); negative infinity for no sea. No levee
		/// and no bar is built where a river's water stands under it: high water floods that ground whatever the
		/// bank, and a river crossing the shore the low tide bares runs in its channel between bare flats, not
		/// walled in.
		/// </param>
		public static void CarveRivers(float[] height, SceneWaterGrid grid, IReadOnlyList<RiverPath> rivers, RiverSettings settings, bool write,
			float tideLine = float.NegativeInfinity)
		{
			float tan = (float)Math.Tan(settings.BankDegrees * Math.PI / 180.0);
			var corridor = new float[rivers.Count][];
			for (int r = 0; r < rivers.Count; r++)
			{
				corridor[r] = Corridor(height, grid, rivers[r], settings, tan);
			}
			var pointBar = new float[rivers.Count][];
			var lateralBar = new float[rivers.Count][];
			var sandOf = new float[rivers.Count][];
			for (int r = 0; r < rivers.Count; r++)
			{
				RiverShaping.Bars(rivers[r], settings, out pointBar[r], out lateralBar[r], out sandOf[r]);
			}
			float barTan = (float)Math.Tan(settings.BarDegrees * Math.PI / 180.0);
			int width = grid.Width, depth = grid.Depth;
			int count = width * depth;
			float bankTan = (float)Math.Tan(settings.BankDegrees * Math.PI / 180.0);
			// Per sample: the lowest any river allows, and the hit most inside a channel.
			float[] limit = null;
			float[] inside = null;
			int[] owner = null;
			float[] surface = null;
			float[] channel = null;
			float[] bank = null;
			float[] barWidth = null;
			float[] barSand = null;
			// Per sample: the highest water within a levee's reach of it, from ANY river or reach, and how far out from
			// that water's channel it stands (the levee's falloff).
			float[] holdTop = null;
			float[] holdOutside = null;
			var touched = new List<int>();

			for (int r = 0; r < rivers.Count; r++)
			{
				RiverPath river = rivers[r];
				for (int v = 0; v + 1 < river.Count; v++)
				{
					float ax = river.X[v], az = river.Z[v], bx = river.X[v + 1], bz = river.Z[v + 1];
					float halfMax = 0.5f * Math.Max(river.Width[v], river.Width[v + 1]);
					float segmentCorridor = Math.Max(corridor[r][v], corridor[r][v + 1]);
					float reach = halfMax + segmentCorridor;
					int x0 = Math.Max(0, (int)Math.Floor((Math.Min(ax, bx) - reach - grid.OriginX) / grid.Spacing));
					int x1 = Math.Min(width - 1, (int)Math.Ceiling((Math.Max(ax, bx) + reach - grid.OriginX) / grid.Spacing));
					int z0 = Math.Max(0, (int)Math.Floor((Math.Min(az, bz) - reach - grid.OriginZ) / grid.Spacing));
					int z1 = Math.Min(depth - 1, (int)Math.Ceiling((Math.Max(az, bz) + reach - grid.OriginZ) / grid.Spacing));
					if (x0 > x1 || z0 > z1)
					{
						continue;
					}
					float ex = bx - ax, ez = bz - az;
					float lengthSq = Math.Max(1e-8f, ex * ex + ez * ez);
					float length = (float)Math.Sqrt(lengthSq);
					for (int z = z0; z <= z1; z++)
					{
						float pz = grid.NorthOf(z);
						for (int x = x0; x <= x1; x++)
						{
							float px = grid.EastOf(x);
							float t = ((px - ax) * ex + (pz - az) * ez) / lengthSq;
							/* Behind a segment's start the segment before it carves (at that shared point it gives the
							 * same level, bed and width). Carved here too, a point lowered under a fall cut a cone of bank
							 * back up behind it, through the ground round the fall's lip, and the river above hung over it. */
							if (t < 0f && v > 0)
							{
								continue;
							}
							t = t < 0f ? 0f : t > 1f ? 1f : t;
							float cx = ax + ex * t, cz = az + ez * t;
							float dx = px - cx, dz = pz - cz;
							float distance = (float)Math.Sqrt(dx * dx + dz * dz);
							float half = 0.5f * (river.Width[v] + (river.Width[v + 1] - river.Width[v]) * t);
							float outside = distance - half;
							if (outside > segmentCorridor)
							{
								continue;
							}
							// Left of the flow is positive.
							float n = (ex * (pz - az) - ez * (px - ax)) / length;
							float level = river.Surface[v] + (river.Surface[v + 1] - river.Surface[v]) * t;
							float bed = river.Bed[v] + (river.Bed[v + 1] - river.Bed[v]) * t;
							float curvature = river.Curvature[v] + (river.Curvature[v + 1] - river.Curvature[v]) * t;

							float allowed;
							float channelHeight = float.PositiveInfinity;
							float bar = 0f;
							if (outside <= 0f)
							{
								// The deepest line toward the outside of the bend: the right bank on a left turn.
								float shift = -settings.BendShift * half * Clamp(curvature * half * 4f, -1f, 1f);
								float across = n >= shift ? (n - shift) / Math.Max(1e-3f, half - shift) : (shift - n) / Math.Max(1e-3f, half + shift);
								across = Clamp(across, 0f, 1f);
								channelHeight = bed + (level - bed) * across * across;
								allowed = channelHeight;
							}
							else
							{
								/* A bar on this bank: the inside of a bend gets a point bar, a slow river a beach on
								 * both sides. It slopes gently out of the water, then the bank rises as usual. */
								bool insideOfBend = (curvature > 0f && n > 0f) || (curvature < 0f && n < 0f);
								bar = lateralBar[r][v] + (lateralBar[r][v + 1] - lateralBar[r][v]) * t;
								if (insideOfBend)
								{
									bar += pointBar[r][v] + (pointBar[r][v + 1] - pointBar[r][v]) * t;
								}
								float onBar = Math.Min(outside, bar);
								float beyond = Math.Max(0f, outside - bar);
								// Behind a bar the bank already stands clear of the water: no freeboard to add.
								allowed = level + onBar * barTan + (bar > 0f ? 0f : settings.FreeboardMetres) + beyond * bankTan;
							}

							int i = z * width + x;
							if (limit == null)
							{
								limit = new float[count];
								inside = new float[count];
								owner = new int[count];
								surface = new float[count];
								channel = new float[count];
								bank = new float[count];
								barWidth = new float[count];
								barSand = new float[count];
								holdTop = new float[count];
								holdOutside = new float[count];
								for (int k = 0; k < count; k++)
								{
									limit[k] = float.PositiveInfinity;
									inside[k] = float.PositiveInfinity;
									owner[k] = -1;
									holdTop[k] = float.NegativeInfinity;
								}
							}
							if (owner[i] < 0)
							{
								touched.Add(i);
							}
							if (allowed < limit[i])
							{
								limit[i] = allowed;
							}
							/* Whatever water this sample stands beside, it holds: a cell beside a pool above a fall, or a meander's
							 * loop, is often most inside ANOTHER reach's channel, lower down, and took its levee from that water; the
							 * pool above stood over the gap, metres over air (Flo Monolith: "the pools are floating"). */
							if (outside > 0f && outside < settings.LeveeMetres && level >= tideLine && !Drowned(river, level, settings))
							{
								float top = level + settings.FreeboardMetres;
								if (top > holdTop[i] + 1e-3f || (Math.Abs(top - holdTop[i]) <= 1e-3f && outside < holdOutside[i]))
								{
									holdTop[i] = top;
									holdOutside[i] = outside;
								}
							}
							float depthInside = outside / Math.Max(0.5f, half);
							if (depthInside < inside[i])
							{
								inside[i] = depthInside;
								owner[i] = r;
								surface[i] = level;
								channel[i] = channelHeight;
								bank[i] = outside;
								barWidth[i] = bar;
								barSand[i] = sandOf[r][v] + (sandOf[r][v + 1] - sandOf[r][v]) * t;
							}
						}
					}
				}
			}
			if (limit == null)
			{
				return;
			}

			foreach (int i in touched)
			{
				RiverPath river = rivers[owner[i]];
				float h = height[i];
				float shaped = Math.Min(h, limit[i]);
				bool inChannel = inside[i] <= 0f;
				/* Neither bar nor levee where the river's water is standing water's: under the tide line (the tide
				 * reworks that sand, and a bank raised above low water walls the channel in), or at the level of the
				 * lake it runs into or out of (a bar there dams it off from the lake). */
				bool built = surface[i] >= tideLine && !Drowned(river, surface[i], settings);
				if (!inChannel && bank[i] < barWidth[i] && built)
				{
					// On a bar: cut down to its gentle slope, and never under the water beside it.
					shaped = Math.Max(shaped, surface[i] + settings.BarLipMetres + bank[i] * barTan * 0.5f);
					shaped = Math.Min(shaped, Math.Max(h, surface[i] + settings.BarLipMetres));
					grid.Kind[i] = WaterKind.Bar;
					grid.Body[i] = river.Id;
					grid.Sand[i] = barSand[i];
					grid.Shore[i] = surface[i];
				}
				else if (!inChannel)
				{
					float outsideMetres = bank[i];
					float top = surface[i] + settings.FreeboardMetres;
					if (built && outsideMetres < settings.LeveeMetres && shaped < top)
					{
						float t = Smooth(outsideMetres / Math.Max(0.1f, settings.LeveeMetres));
						shaped = Math.Max(shaped, top + (shaped - top) * t);
					}
					// And the highest water near it, whoever owns it: holding water before a bank's cosmetic slope.
					if (holdTop[i] > shaped)
					{
						float t = Smooth(holdOutside[i] / Math.Max(0.1f, settings.LeveeMetres));
						shaped = Math.Max(shaped, holdTop[i] + (shaped - holdTop[i]) * t);
					}
					if (outsideMetres < settings.LeveeMetres || shaped < h - 0.05f)
					{
						grid.Kind[i] = WaterKind.Bank;
						grid.Body[i] = river.Id;
					}
				}
				else
				{
					grid.Kind[i] = river.Perennial ? WaterKind.River : WaterKind.DryWash;
					grid.Body[i] = river.Id;
					if (river.Perennial)
					{
						grid.Level[i] = surface[i];
					}
				}
				if (write)
				{
					height[i] = shaped;
				}
			}
		}

		/// <summary>Whether water standing at <paramref name="level"/> in a river is within <see cref="RiverSettings.MouthMetres"/> of the lake it enters or leaves.</summary>
		private static bool Drowned(RiverPath river, float level, RiverSettings settings)
		{
			int last = river.Count - 1;
			return (river.End == RiverEnd.Lake && level < river.Surface[last] + settings.MouthMetres)
				|| (river.Start == RiverEnd.Lake && level > river.Surface[0] - settings.MouthMetres);
		}

		/// <summary>
		/// Per point of a river, how far out its banks must be cut back for the cut to meet the ground on
		/// both sides: walking out from the channel's edge until the ground falls under the bank's slope
		/// from the water's surface. At least <see cref="RiverSettings.CorridorMetres"/>, at most <see cref="RiverSettings.MaxCorridorMetres"/>.
		/// </summary>
		private static float[] Corridor(float[] height, SceneWaterGrid grid, RiverPath river, RiverSettings settings, float bankTan)
		{
			int n = river.Count;
			var reach = new float[n];
			RiverShaping.Normals(river.X, river.Z, out float[] nx, out float[] nz);
			float step = Math.Max(grid.Spacing * 2f, 4f);
			for (int i = 0; i < n; i++)
			{
				float half = 0.5f * river.Width[i];
				float top = river.Surface[i] + settings.FreeboardMetres;
				float needed = settings.CorridorMetres;
				for (int side = -1; side <= 1; side += 2)
				{
					for (float d = 0f; d <= settings.MaxCorridorMetres; d += step)
					{
						float px = river.X[i] + nx[i] * side * (half + d), pz = river.Z[i] + nz[i] * side * (half + d);
						int s = grid.SampleAt(px, pz);
						if (s < 0)
						{
							break;
						}
						if (height[s] <= top + d * bankTan)
						{
							needed = Math.Max(needed, d + step);
							break;
						}
						if (d + step > settings.MaxCorridorMetres)
						{
							needed = settings.MaxCorridorMetres;
						}
					}
				}
				reach[i] = Math.Min(settings.MaxCorridorMetres, needed);
			}
			// Smoothed along the river, so a bank's reach does not jump from one point to the next.
			float[] smooth = RiverShaping.SmoothAlong(reach, 4);
			for (int i = 0; i < n; i++)
			{
				reach[i] = Math.Max(reach[i], smooth[i]);
			}
			return reach;
		}

		/// <summary>
		/// Floods a lake from its seed samples over every connected sample lower than its level that it
		/// may cover, never into a river's channel; then, when <paramref name="write"/>, raises the ground
		/// just outside it that is lower than the level (where the scene's ground lets the water out
		/// somewhere the planet's did not) into a sill. Returns the samples covered.
		/// </summary>
		/// <param name="eligible">Whether the lake may reach a sample: where the planet's lake is, and a margin round it.</param>
		/// <param name="sealMetres">How far above the level a sill stands.</param>
		/// <param name="maxRaiseMetres">The most a sill raises the ground; past that the water's edge is left.</param>
		public static int FloodLake(float[] height, SceneWaterGrid grid, float level, IEnumerable<int> seeds, Func<int, bool> eligible,
			int lakeId, float sealMetres, float maxRaiseMetres, bool write)
		{
			int width = grid.Width, depth = grid.Depth;
			var queue = new Queue<int>();
			var reached = new HashSet<int>();
			foreach (int seed in seeds)
			{
				if (seed >= 0 && seed < height.Length && height[seed] < level && grid.Kind[seed] != WaterKind.River && eligible(seed) && reached.Add(seed))
				{
					queue.Enqueue(seed);
				}
			}
			int covered = 0;
			var edge = new List<int>();
			while (queue.Count > 0)
			{
				int i = queue.Dequeue();
				grid.Kind[i] = WaterKind.Lake;
				grid.Level[i] = level;
				grid.Body[i] = lakeId;
				covered++;
				int x = i % width, z = i / width;
				for (int k = 0; k < 4; k++)
				{
					int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
					if (nx < 0 || nz < 0 || nx >= width || nz >= depth)
					{
						continue;
					}
					int j = nz * width + nx;
					if (reached.Contains(j) || height[j] >= level || grid.Kind[j] == WaterKind.River || grid.Kind[j] == WaterKind.Lake)
					{
						continue;
					}
					if (!eligible(j))
					{
						edge.Add(j);
						continue;
					}
					reached.Add(j);
					queue.Enqueue(j);
				}
			}
			if (write)
			{
				foreach (int j in edge)
				{
					if (grid.Kind[j] != WaterKind.Lake && height[j] < level)
					{
						height[j] = Math.Min(level + sealMetres, height[j] + maxRaiseMetres);
					}
				}
			}
			return covered;
		}

		/// <summary>
		/// Marks the salt flat round a terminal lake: the ground connected to its water that stands less than
		/// <paramref name="riseMetres"/> above its level, where it may reach — lake bed the water leaves dry in
		/// the dry season and crusts with salt. Returns the samples marked.
		/// </summary>
		public static int MarkPlaya(float[] height, SceneWaterGrid grid, int lakeId, float level, float riseMetres, Func<int, bool> eligible)
		{
			int width = grid.Width, depth = grid.Depth;
			var queue = new Queue<int>();
			var reached = new HashSet<int>();
			for (int i = 0; i < height.Length; i++)
			{
				if (grid.Kind[i] == WaterKind.Lake && grid.Body[i] == lakeId)
				{
					queue.Enqueue(i);
					reached.Add(i);
				}
			}
			int marked = 0;
			while (queue.Count > 0)
			{
				int i = queue.Dequeue();
				int x = i % width, z = i / width;
				for (int k = 0; k < 4; k++)
				{
					int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
					if (nx < 0 || nz < 0 || nx >= width || nz >= depth)
					{
						continue;
					}
					int j = nz * width + nx;
					if (!reached.Add(j) || grid.Kind[j] != WaterKind.None || height[j] >= level + riseMetres || !eligible(j))
					{
						continue;
					}
					grid.Kind[j] = WaterKind.Playa;
					grid.Body[j] = lakeId;
					grid.Shore[j] = level;
					marked++;
					queue.Enqueue(j);
				}
			}
			return marked;
		}

		private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
		private static float Smooth(float t)
		{
			t = Clamp(t, 0f, 1f);
			return t * t * (3f - 2f * t);
		}
	}
}
#endif
