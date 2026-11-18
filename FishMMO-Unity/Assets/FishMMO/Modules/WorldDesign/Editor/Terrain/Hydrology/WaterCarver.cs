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

		/// <summary>The highest LAKE surface among the four samples around a position (a lake is flat); negative infinity where none is a lake.</summary>
		public float LakeSurfaceNear(float east, float north)
		{
			float gx = (east - OriginX) / Spacing, gz = (north - OriginZ) / Spacing;
			int x0 = (int)Math.Floor(gx), z0 = (int)Math.Floor(gz);
			float best = float.NegativeInfinity;
			for (int dz = 0; dz <= 1; dz++)
			{
				for (int dx = 0; dx <= 1; dx++)
				{
					int x = x0 + dx, z = z0 + dz;
					if (x >= 0 && z >= 0 && x < Width && z < Depth && Kind[z * Width + x] == WaterKind.Lake)
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
			// A levee's top, out past the drawn water and a sample's diagonal more (RiverSettings.LeveeCrestMetres), then its back slope.
			float leveeCrest = settings.LeveeCrestMetres + 1.5f * grid.Spacing;
			float leveeReach = leveeCrest + settings.LeveeMetres;
			// Per sample: the lowest any river allows, and the hit most inside a channel.
			float[] limit = null;
			float[] inside = null;
			int[] owner = null;
			float[] surface = null;
			float[] channel = null;
			float[] bank = null;
			float[] barWidth = null;
			float[] barSand = null;
			// Per sample: which point of its owner it is most inside (for a ponded stretch, which builds nothing).
			int[] pointOf = null;
			/* Per sample: the highest ground any water within a levee's reach of it needs, from ANY river or reach: that water's
			 * levee top, less its back slope past the crest for how far out from the water's channel the sample stands. Kept as
			 * the highest top and its distance instead, a pool's outlet 11.7 m off (back-sloped to 245.0 m) decided a sample 2.9 m
			 * from the rapid below it (which needed 247.5 m), and the rapid ran 2 m over a scour hole beside it (Flo Monolith,
			 * 2026-10-15). */
			float[] holdNeed = null;
			/* Per sample: the highest water it stands beside, from a channel it lies across from (not off a segment's end): what
			 * the water's edge is held to. The levee hold (holdNeed) reaches a levee's distance round every point, so in a steep rapid it is the
			 * water two or three points upstream, half a metre higher, and an edge held to it stood over the water beside it
			 * (2026-10-13 re-cut: 131 pinched points, from 51). */
			float[] edgeTop = null;
			// Per sample: whether the water its owner gives it is falling, or past the brink of a fall: it holds no levee.
			bool[] noLevee = null;
			var touched = new List<int>();

			for (int r = 0; r < rivers.Count; r++)
			{
				RiverPath river = rivers[r];
				// Per segment, the fall whose basin it runs through; null for none (FallBasin).
				FallBasin[] basinOf = FallBasin.Along(river, settings, grid.Spacing);
				// Per segment, the next fall downstream of it whose brink its levees could reach past.
				FallBasin[] brinkAhead = null;
				if (basinOf != null)
				{
					brinkAhead = new FallBasin[basinOf.Length];
					FallBasin ahead = null;
					for (int v = basinOf.Length - 1; v >= 0; v--)
					{
						if (basinOf[v] != null && basinOf[v].Lip == v)
						{
							ahead = basinOf[v];
						}
						brinkAhead[v] = ahead != null && ahead.Lip > v && ahead.AlongAtLip - river.S[v + 1] <= leveeReach + river.Width[ahead.Lip] ? ahead : null;
					}
				}
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
					/* A fall's brink is rounded under the water going over it. Carved as a slope from the lip to the next point
					 * (60 m lower, 2 m on) the edge ate back into the channel: the ground at the lip stood 7.6 m under its water,
					 * and the river's last metres hung over a V-shaped hole, black from upstream (Flo Monolith). Held at the
					 * lip's level for a grid cell past it instead, the ground stood out as a shelf where the water had already
					 * left it: the curtain leaves at the lip, so its strips struck the shelf and ran along it, and the cell's
					 * step wandered a whole sample across the lip, a ragged comb at the top of every fall (2026-10-13: "the
					 * waterfall lip needs to be smoothed"). Past the lip the ground follows the arc the water falls in, so
					 * wherever the grid's samples land the terrain between them lies under the water and rounds over. */
					bool lipSegment = river.Reach != null && river.Reach[v] != RiverReach.Fall && river.Reach[v + 1] == RiverReach.Fall;
					float jet = lipSegment ? FallBasin.JetCurvature(river, v, settings) : 0f;
					FallBasin basin = basinOf?[v];
					/* The water over a fall's brink and down its face holds nothing: a levee raised from it, or from the lip's
					 * water past the brink, stood the lip's level out over the drop either side of the curtain, a shelf at the
					 * lip and a slot the water fell down (2026-10-13: "weird lips"). Water running on below the step, and the
					 * pool's, holds its banks as any river's does. */
					bool fallingWater = basin != null && v >= basin.Lip && v < basin.Plunge
						&& river.Surface[v] - river.Surface[v + 1] >= 2f * settings.FallGradient * Math.Max(1e-3f, river.S[v + 1] - river.S[v]);
					FallBasin brink = brinkAhead?[v];
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
							/* Beside the segment, or no more than half a sample past its ends, at its water carried on past them on its own
							 * slope: a pool's last point still holds the bank round it, and a rapid's the water level with it. */
							float overshoot = (t < 0f ? -t : t > 1f ? t - 1f : 0f) * length;
							bool lyingBeside = overshoot <= 0.5f * grid.Spacing;
							float waterOn = river.Surface[v] + (river.Surface[v + 1] - river.Surface[v]) * t;
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
							if (lipSegment)
							{
								float under = FallBasin.UnderBrink(jet, settings.BrinkClearanceMetres, grid.Spacing, t * length);
								level = Math.Max(level, river.Surface[v] - under);
								bed = Math.Max(bed, river.Bed[v] - under);
							}
							/* And on past the next point, for two samples: the terrain between samples is a blend of four corners up
							 * to a cell's diagonal apart, so a corner dropped sheer to the pool a point on still pulled the lip's
							 * edges metres under its water. */
							if (basin != null && v > basin.Lip)
							{
								float past = river.S[v] + (river.S[v + 1] - river.S[v]) * t - basin.AlongAtLip;
								if (past < basin.BrinkReach)
								{
									float under = FallBasin.UnderBrink(basin.Jet, basin.BrinkClearance, basin.Spacing, past);
									level = Math.Max(level, basin.LipLevel - under);
									bed = Math.Max(bed, basin.LipBed - under);
								}
							}
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
								float bankLevel = level, slope = bankTan;
								if (basin != null)
								{
									/* In a fall's basin the banks are its walls: steeper than a cut bank, irregular, and with the drop
									 * curved round the pool from the notch instead of ruled across the lip's line (FallBasin). */
									float along = river.S[v] + (river.S[v + 1] - river.S[v]) * t - basin.AlongAtLip;
									float fade = basin.Fade(along);
									bankLevel = level + (basin.BankLevel(river, along, distance, px, pz) - level) * fade;
									slope = bankTan + (basin.WallSlope(distance, px, pz, bankTan) - bankTan) * fade;
								}
								else if (lipSegment)
								{
									// A bank over a brink rounds over with the water.
									bankLevel = level;
								}
								// Behind a bar the bank already stands clear of the water: no freeboard to add.
								allowed = bankLevel + onBar * barTan + (bar > 0f ? 0f : settings.FreeboardMetres) + beyond * slope;
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
								pointOf = new int[count];
								holdNeed = new float[count];
								edgeTop = new float[count];
								noLevee = new bool[count];
								for (int k = 0; k < count; k++)
								{
									limit[k] = float.PositiveInfinity;
									inside[k] = float.PositiveInfinity;
									owner[k] = -1;
									holdNeed[k] = float.NegativeInfinity;
									edgeTop[k] = float.NegativeInfinity;
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
							/* Beside the lip itself the water is still the lip's: that it holds. Past the brink nothing above it does,
							 * the reach of the water before the lip included. */
							bool holdsNothing = (fallingWater && !(v == basin.Lip && t <= 0f)) || (brink != null && brink.PastBrink(px, pz));
							if (outside > 0f && outside < leveeReach && level >= tideLine && !Drowned(river, level, settings) && v >= river.PondUntil
								&& !holdsNothing)
							{
								float need = level + settings.FreeboardMetres - Math.Max(0f, outside - leveeCrest) * bankTan;
								if (need > holdNeed[i])
								{
									holdNeed[i] = need;
								}
								float beside = overshoot > 0f ? waterOn : level;
								if (lyingBeside && outside < grid.Spacing && beside > edgeTop[i])
								{
									edgeTop[i] = beside;
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
								pointOf[i] = v;
								noLevee[i] = holdsNothing;
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
				bool built = surface[i] >= tideLine && !Drowned(river, surface[i], settings) && pointOf[i] >= river.PondUntil && !noLevee[i];
				if (!inChannel && bank[i] < barWidth[i] && built)
				{
					// On a bar: cut down to its gentle slope, and never under the water beside it.
					shaped = Math.Max(shaped, surface[i] + settings.BarLipMetres + bank[i] * barTan * 0.5f);
					shaped = Math.Min(shaped, Math.Max(h, surface[i] + settings.BarLipMetres));
					// Its first sample out from the water flush with it, as any bank's (below): a bar's lip and slope there
					// tilted the ground's plane up into a narrow stream's edge as a bank's height did.
					if (river.Perennial && bank[i] < grid.Spacing)
					{
						shaped = Math.Max(surface[i], edgeTop[i]) - settings.EdgeShelfMetres;
					}
					grid.Kind[i] = WaterKind.Bar;
					grid.Body[i] = river.Id;
					grid.Sand[i] = barSand[i];
					grid.Shore[i] = surface[i];
				}
				else if (!inChannel && river.Perennial && bank[i] < grid.Spacing)
				{
					/* The water's edge, a ground sample wide: held at the water's surface, with no levee. The ground is a grid of
					 * samples about 2 m apart and the terrain between them a plane, so a bank sample standing at its height
					 * next to a channel sample tilted the ground up across the water's last metre or two: a river three or
					 * four metres wide showed one or two, cut off where its banks pinched it, the grass on them and the leaves
					 * lying over it (2026-10-13). Held at the water, the ground can only rise past the water's edge, never into
					 * it; grass rooted there is at the water line and is not grown. */
					/* At the highest water beside it, whoever's: a bank sample owned by the reach below a pool still holds the pool.
					 * And raised to it where the ground beside the water lies lower: this sample is the bank's own edge, and the
					 * levee it replaces raised it. Held only from above, a pool in a hollow lay over ground half a metre under its
					 * water a metre past its bank (2026-10-13). Not where no levee is built (falling water, past a brink, a
					 * lake's or the sea's mouth, a pond): there it is only lowered. */
					float edgeWater = Math.Max(surface[i], edgeTop[i]) - settings.EdgeShelfMetres;
					shaped = built ? edgeWater : Math.Min(shaped, edgeWater);
					grid.Kind[i] = WaterKind.Bank;
					grid.Body[i] = river.Id;
				}
				else if (!inChannel)
				{
					float outsideMetres = bank[i];
					float top = surface[i] + settings.FreeboardMetres;
					/* A levee: its top held out past the water's drawn margin, then a back slope down to the ground. Eased from its
					 * top to the ground across its whole width instead, it sank under the water a metre or two out wherever the
					 * ground beside a river lay lower than it, and the water's surface ended there over sunken ground: the river
					 * perched, its pools not filled to their banks (2026-10-13 coverage map: 852 m² on Flo Monolith). */
					if (built && outsideMetres < leveeReach && shaped < top)
					{
						shaped = Math.Max(shaped, top - Math.Max(0f, outsideMetres - leveeCrest) * bankTan);
					}
					// And whatever water near it needs, whoever owns it: holding water before a bank's cosmetic slope.
					if (holdNeed[i] > shaped)
					{
						shaped = holdNeed[i];
					}
					if (outsideMetres < leveeReach || shaped < h - 0.05f)
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

	/// <summary>
	/// The ground a waterfall has worn round itself, as the carve cuts it: a curved headwall round the plunge pool with the
	/// lip a notch in its brink, and steep, irregular rock walls down to the pool and along the gorge below it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> The carve cuts each river segment's banks up from its own water, and leaves the ground behind a segment's
	/// start to the segment before it, so a point lowered under a fall cannot cut a cone of bank back up through the lip. At
	/// a fall that boundary is the lip's line: every bank the fall's lowered water cut ended on it, so the drop stood as one
	/// straight wall across the whole cut, tens of metres either side of a curtain a few metres wide, with ruled 38° planes
	/// below it and square corners where they met; the lip's hold kept a shelf of its level a cell out over the drop, and
	/// the lip's levee stood up either side of the curtain (2026-10-13: "waterfall terrain cuts are making weird lips").
	/// The lip holds no shelf now: past it the ground rounds over under the falling water (<see cref="UnderBrink"/>).
	/// </para>
	/// <para>
	/// <b>The headwall.</b> A fall wears back into its ledge and scours a pool where it lands, so it stands at the head of a
	/// gorge in a rounded amphitheatre. A bank in the basin is cut up from the water it would stand beside
	/// <see cref="Setback"/> metres further upstream: none at the notch, more the further out across, along a circle of
	/// <see cref="Radius"/> that turns to run downstream as the gorge's rim. So where the river drops over its lip, its banks
	/// drop along that curve, round the pool, and the lip is a notch in the middle of the headwall's brink.
	/// </para>
	/// <para>
	/// <b>The walls.</b> Round the curve the banks drop as the channel does over its lip, rounding over and then straight down,
	/// so the headwall is the face the curtain falls past, upright, and beside the curtain the ground runs on across the
	/// face's foot as a bank, with no slot cut for the water. From <see cref="RiverSettings.FallNotchBlendMetres"/> out the
	/// banks rise from the pool's and the gorge's water as rock walls at <see cref="RiverSettings.FallWallDegrees"/>,
	/// wandering by <see cref="RiverSettings.FallWallJitterDegrees"/>, and the brink wanders in and out by up to
	/// <see cref="RiverSettings.FallRimJitterMetres"/>: value noise seeded by the river and its lip, so the walls are rock,
	/// not ruled planes, and the same on every cut. Ground this steep is what the falls' wall rock dresses
	/// (<see cref="FallLedges"/>, <see cref="FallRockKind.Wall"/>).
	/// </para>
	/// <para>
	/// <b>Only the banks.</b> No channel is touched: the lip holds its bed, the water falls into its pool and drains out as
	/// before, and every bank still stands at least the freeboard over its own water. Past the gorge's foot the basin fades
	/// out (<see cref="FadeStart"/> … <see cref="FadeEnd"/>), so it meets the river's ordinary banks with no step.
	/// </para>
	/// </remarks>
	public sealed class FallBasin
	{
		/// <summary>The lip's point, the first point at the pool's level, and the point after the fall.</summary>
		public int Lip;
		public int Plunge;
		public int Foot;
		/// <summary>The lip's distance along the river (<see cref="RiverPath.S"/>), metres.</summary>
		public float AlongAtLip;
		public float LipLevel;
		/// <summary>The lip's point, and the way its water leaves it (unit, level).</summary>
		public float LipX;
		public float LipZ;
		public float DirX;
		public float DirZ;
		/// <summary>Half the lip's width: the notch.</summary>
		public float HalfLip;
		/// <summary>The curvature of the arc the brink is rounded to (<see cref="JetCurvature"/>), and how far under it the ground is held, metres.</summary>
		public float Jet;
		public float BrinkClearance;
		/// <summary>The ground's sample spacing, metres: over which the clearance under the arc comes in.</summary>
		public float Spacing;
		/// <summary>How far past the lip the brink is rounded, metres (two samples), and the lip's bed under its water.</summary>
		public float BrinkReach;
		public float LipBed;
		/// <summary>The headwall's radius, metres.</summary>
		public float Radius;
		/// <summary>Distances along from the lip over which the basin fades out.</summary>
		public float FadeStart;
		public float FadeEnd;
		public uint Seed;

		private float wallDegrees, wallJitter, notchBlend, rimJitter, spread;

		/// <summary>Wavelengths of the brink's wander and the walls' steepness, metres.</summary>
		private const float RimNoiseMetres = 7f;
		private const float WallNoiseMetres = 8f;

		/// <summary>
		/// Per segment of a river (from point v to v + 1), the basin of the fall it runs through; null where none, and null for
		/// a river with no fall. A fall is a run of <see cref="RiverReach.Fall"/> points (gaps of one bridged) dropping at least
		/// <see cref="RiverSettings.MinPoolDropMetres"/>, from the point before it (its lip) to the point after (its foot):
		/// the falls the curtain is drawn for and the ledges are laid on.
		/// </summary>
		/// <param name="spacing">The ground's sample spacing, metres: over which the brink's clearance comes in (<see cref="UnderBrink"/>).</param>
		public static FallBasin[] Along(RiverPath river, RiverSettings settings, float spacing)
		{
			int n = river.Count;
			if (!river.Perennial || n < 3 || river.Reach == null || river.Reach.Length != n || river.S == null)
			{
				return null;
			}
			FallBasin[] basinOf = null;
			int i = 0;
			while (i < n)
			{
				if (river.Reach[i] != RiverReach.Fall)
				{
					i++;
					continue;
				}
				int last = i;
				while (last + 1 < n && (river.Reach[last + 1] == RiverReach.Fall || (last + 2 < n && river.Reach[last + 2] == RiverReach.Fall)))
				{
					last++;
				}
				int lip = Math.Max(0, i - 1), foot = Math.Min(n - 1, last + 1);
				float drop = river.Surface[lip] - river.Surface[foot];
				i = last + 1;
				if (drop < settings.MinPoolDropMetres || foot <= lip)
				{
					continue;
				}
				basinOf ??= new FallBasin[n - 1];
				FallBasin basin = Make(river, settings, lip, foot, drop, spacing);
				// To the end of its fade, or the next fall's lip, whichever comes first: that fall's basin takes over there.
				int next = n - 1;
				for (int k = foot; k < n; k++)
				{
					if (river.Reach[k] == RiverReach.Fall)
					{
						next = Math.Max(foot, k - 1);
						break;
					}
				}
				float nextAlong = river.S[next] - basin.AlongAtLip;
				if (basin.FadeEnd > nextAlong)
				{
					basin.FadeEnd = Math.Max(1f, nextAlong);
					basin.FadeStart = Math.Min(basin.FadeStart, 0.5f * basin.FadeEnd);
				}
				for (int v = lip; v < n - 1 && v < next; v++)
				{
					if (river.S[v] - basin.AlongAtLip >= basin.FadeEnd)
					{
						break;
					}
					basinOf[v] = basin;
				}
			}
			return basinOf;
		}

		private static FallBasin Make(RiverPath river, RiverSettings settings, int lip, int foot, float drop, float spacing)
		{
			int n = river.Count;
			float pool = river.Surface[foot];
			int plunge = foot;
			for (int k = lip + 1; k <= foot; k++)
			{
				if (river.Surface[k] <= pool + 0.5f)
				{
					plunge = k;
					break;
				}
			}
			float halfLip = 0.5f * Math.Max(1f, river.Width[lip]);
			// The pool's width as its basin was scoured (RiverShaping.PlungePools), a few points past the foot at most.
			float poolWidth = river.Width[lip];
			for (int k = lip; k < n && k <= foot + 8; k++)
			{
				poolWidth = Math.Max(poolWidth, river.Width[k]);
			}
			/* About as far round as the walls stand back from the pool: the drop over the walls' steepness, out past the pool's
			 * own edge. A tighter headwall has the curtain fall into a slot; a wider one is a bay, not an amphitheatre. */
			float wallRun = drop / (float)Math.Tan(Math.Max(1f, settings.FallWallDegrees) * Math.PI / 180.0);
			float radius = Math.Max(halfLip + 3f, Math.Min(40f, 0.5f * (poolWidth - 2f * halfLip) + 0.4f * wallRun));
			float footAlong = river.S[foot] - river.S[lip];
			float fadeStart = Math.Max(2f * radius, footAlong);
			uint seed = unchecked((uint)river.Id * 0x9E3779B1u ^ (uint)river.PlanetRiver * 0x85EBCA6Bu ^ (uint)lip * 0xC2B2AE35u ^ 0xFA11BA5Eu);
			int after = Math.Min(n - 1, lip + 1);
			float dx = river.X[after] - river.X[lip], dz = river.Z[after] - river.Z[lip];
			float dl = (float)Math.Sqrt(dx * dx + dz * dz);
			return new FallBasin
			{
				Lip = lip,
				Plunge = plunge,
				Foot = foot,
				AlongAtLip = river.S[lip],
				LipLevel = river.Surface[lip],
				LipX = river.X[lip],
				LipZ = river.Z[lip],
				DirX = dl > 1e-6f ? dx / dl : 1f,
				DirZ = dl > 1e-6f ? dz / dl : 0f,
				HalfLip = halfLip,
				Jet = JetCurvature(river, lip, settings),
				BrinkClearance = Math.Max(0f, settings.BrinkClearanceMetres),
				Spacing = Math.Max(0.1f, spacing),
				BrinkReach = 2f * Math.Max(0.1f, spacing),
				LipBed = river.Bed[lip],
				Radius = radius,
				FadeStart = fadeStart,
				FadeEnd = fadeStart + Math.Max(20f, 2f * radius),
				Seed = seed,
				wallDegrees = settings.FallWallDegrees,
				wallJitter = settings.FallWallJitterDegrees,
				notchBlend = Math.Max(0.5f, settings.FallNotchBlendMetres),
				rimJitter = Math.Min(settings.FallRimJitterMetres, 0.2f * radius),
				spread = Math.Max(0.05f, settings.FallGorgeSpread),
			};
		}

		/// <summary>Whether a scene position is past the brink: downstream of the lip's line.</summary>
		public bool PastBrink(float east, float north) => (east - LipX) * DirX + (north - LipZ) * DirZ > 0f;

		/// <summary>
		/// How the water falls off a river's lip <paramref name="lip"/>: the curvature g / 2v² of its arc, metres down per
		/// square metre out, for <see cref="RiverSettings.BrinkSpeedShare"/> of the brink speed. Over a free overfall the flow
		/// is critical: the brink's depth 0.715 of the critical depth (q²/g)^⅓ and its speed q over that (FallHydraulics, the
		/// curtain's own figures).
		/// </summary>
		public static float JetCurvature(RiverPath river, int lip, RiverSettings settings)
		{
			const float g = 9.81f;
			float q = river.Discharge != null && river.Width != null ? river.Discharge[lip] / Math.Max(0.1f, river.Width[lip]) : 0f;
			float brink = (float)Math.Pow(g * Math.Max(1e-3f, q), 1.0 / 3.0) / 0.715f;
			float speed = Math.Max(0.5f, settings.BrinkSpeedShare * brink);
			return g / (2f * speed * speed);
		}

		/// <summary>
		/// How far under the lip's level the ground <paramref name="x"/> metres past the brink is rounded to: under the arc
		/// the water falls in (<paramref name="jet"/>, <see cref="JetCurvature"/>), by <paramref name="clearance"/> once a
		/// sample <paramref name="spacing"/> out and none at the lip, so it rounds on from the bed the water leaves.
		/// </summary>
		/// <remarks>
		/// A heightfield's ground between two samples is a straight chord, and the arc curves down: every chord between
		/// samples on it, or from the lip's level behind the brink to one on it, lies under it. So the water clears the ground
		/// wherever the samples land, and the most the brink can eat back under the water before it is a fraction of one
		/// sample's fall (0.15 jet · spacing²), not the drop to the pool.
		/// </remarks>
		public static float UnderBrink(float jet, float clearance, float spacing, float x)
		{
			if (x <= 0f)
			{
				return 0f;
			}
			return jet * x * x + clearance * Smooth(x / Math.Max(0.1f, spacing));
		}

		/// <summary>1 over the basin and the gorge to the foot, easing to 0 by <see cref="FadeEnd"/>.</summary>
		public float Fade(float along)
		{
			if (along <= FadeStart)
			{
				return 1f;
			}
			if (along >= FadeEnd)
			{
				return 0f;
			}
			float t = 1f - (along - FadeStart) / Math.Max(1e-3f, FadeEnd - FadeStart);
			return t * t * (3f - 2f * t);
		}

		/// <summary>
		/// How far downstream of the lip's notch the brink stands, metres, <paramref name="across"/> metres out from the river's
		/// middle at scene (east, north): none across the lip's own water, then round a circle of <see cref="Radius"/>, then on
		/// downstream as the gorge's rim, widening by the spread; wandering, but not beside the curtain.
		/// </summary>
		public float Setback(float across, float east, float north)
		{
			float off = Math.Max(0f, across - HalfLip);
			float round = off <= Radius
				? Radius - (float)Math.Sqrt(Radius * Radius - off * off)
				: Radius + (off - Radius) / spread;
			float wander = rimJitter * Smooth((off - notchBlend) / notchBlend) * Noise(east / RimNoiseMetres, north / RimNoiseMetres, Seed);
			return Math.Max(0f, round + wander);
		}

		/// <summary>
		/// The water a bank of the basin is cut up from: the water the <see cref="Setback"/> further upstream, as the channel
		/// carries it over the lip (rounding over under its arc, then dropping), so beside the curtain the ground drops where the water
		/// does and further round it drops later: the headwall curves round the pool.
		/// </summary>
		/// <param name="along">Metres downstream of the lip, along the river.</param>
		/// <param name="across">Metres out from the river's middle.</param>
		public float BankLevel(RiverPath river, float along, float across, float east, float north)
		{
			float behind = along - Setback(across, east, north);
			if (behind <= 0f)
			{
				return LipLevel;
			}
			/* As WaterCarver lays its water: rounding over under the water's arc, or the water's own fall. A sample past the
			 * brink only: the walls beside the curtain rise from this level, and rounded on as far as the channel is, they stood
			 * metres over the water beside it, a slot for the curtain. */
			int next = Math.Min(river.Count - 1, Lip + 1);
			float first = river.S[next] - AlongAtLip;
			float water = behind < first
				? LipLevel + (river.Surface[next] - LipLevel) * (behind / Math.Max(1e-3f, first))
				: SurfaceAt(river, AlongAtLip + behind);
			return behind < Spacing ? Math.Max(water, LipLevel - UnderBrink(Jet, BrinkClearance, Spacing, behind)) : water;
		}

		/// <summary>
		/// How steeply (rise over run) the basin's bank rises from its water, out from the river's middle: a bank's own slope
		/// beside the curtain, where the face runs on across behind it, and a rock wall's further out, wandering.
		/// </summary>
		public float WallSlope(float across, float east, float north, float bankSlope)
		{
			float off = Math.Max(0f, across - HalfLip);
			float degrees = wallDegrees + wallJitter * Noise(east / WallNoiseMetres, north / WallNoiseMetres, Seed ^ 0x5BD1E995u);
			degrees = Math.Max(1f, Math.Min(85f, degrees));
			float wall = (float)Math.Tan(degrees * Math.PI / 180.0);
			return bankSlope + (wall - bankSlope) * Smooth(off / notchBlend);
		}

		/// <summary>A river's water surface <paramref name="s"/> metres along it (<see cref="RiverPath.S"/>), held at its ends.</summary>
		public static float SurfaceAt(RiverPath river, float s)
		{
			int n = river.Count;
			if (s <= river.S[0])
			{
				return river.Surface[0];
			}
			if (s >= river.S[n - 1])
			{
				return river.Surface[n - 1];
			}
			int lo = 0, hi = n - 1;
			while (hi - lo > 1)
			{
				int mid = (lo + hi) >> 1;
				if (river.S[mid] <= s)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			float span = river.S[hi] - river.S[lo];
			float t = span > 1e-6f ? (s - river.S[lo]) / span : 0f;
			return river.Surface[lo] + (river.Surface[hi] - river.Surface[lo]) * t;
		}

		/// <summary>Two octaves of value noise, −1 … 1, smooth in (x, z), the same for the same seed.</summary>
		public static float Noise(float x, float z, uint seed)
		{
			return 0.7f * Value(x, z, seed) + 0.3f * Value(2.03f * x + 17.1f, 2.03f * z - 9.7f, seed ^ 0x68E31DA4u);
		}

		private static float Value(float x, float z, uint seed)
		{
			int x0 = (int)Math.Floor(x), z0 = (int)Math.Floor(z);
			float fx = x - x0, fz = z - z0;
			fx = fx * fx * (3f - 2f * fx);
			fz = fz * fz * (3f - 2f * fz);
			float a = Lattice(x0, z0, seed), b = Lattice(x0 + 1, z0, seed), c = Lattice(x0, z0 + 1, seed), d = Lattice(x0 + 1, z0 + 1, seed);
			return a + (b - a) * fx + (c - a) * fz + (a - b - c + d) * fx * fz;
		}

		private static float Lattice(int x, int z, uint seed)
		{
			unchecked
			{
				uint h = (uint)x * 0x8DA6B343u ^ (uint)z * 0xD8163841u ^ seed * 0xCB1AB31Fu;
				h ^= h >> 15;
				h *= 0x2C1B3C6Du;
				h ^= h >> 12;
				h *= 0x297A2D39u;
				h ^= h >> 15;
				return (h & 0xFFFFFF) / (float)0x7FFFFF - 1f;
			}
		}

		private static float Smooth(float t)
		{
			t = t < 0f ? 0f : t > 1f ? 1f : t;
			return t * t * (3f - 2f * t);
		}
	}
}
#endif
