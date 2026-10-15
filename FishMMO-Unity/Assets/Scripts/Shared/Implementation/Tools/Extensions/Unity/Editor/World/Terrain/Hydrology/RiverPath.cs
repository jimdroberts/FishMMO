#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How a scene's rivers are laid: their shape, their size for their discharge, and their banks.</summary>
	public sealed class RiverSettings
	{
		/// <summary>Hydraulic geometry, w = a·Q^0.5 (Leopold &amp; Maddock): a for width, metres per (m³/s)^0.5.</summary>
		public float WidthCoefficient = 4.5f;
		/// <summary>d = c·Q^0.4: c for depth.</summary>
		public float DepthCoefficient = 0.4f;
		/// <summary>The game's clamps on width and depth, metres.</summary>
		public float MinWidth = 3f;
		public float MaxWidth = 60f;
		public float MinDepth = 0.5f;
		public float MaxDepth = 4f;

		/// <summary>A dry wash's width from its drainage area: w = a·(A in km²)^0.3, clamped to the width limits.</summary>
		public float DryWidthCoefficient = 1.5f;
		/// <summary>How deep a dry wash's bed is cut, metres.</summary>
		public float DryDepth = 0.6f;

		/// <summary>How far each side of the planet's line a river looks for its scene's valley floor, metres.</summary>
		public float SnapMetres = 80f;
		/// <summary>Cost per metre of moving away from the planet's line when looking for the floor: prefers the nearer of two hollows.</summary>
		public float SnapPenalty = 0.03f;

		/// <summary>Meander amplitude in widths on a flat floodplain; none on a steep bed.</summary>
		public float MeanderWidths = 1.4f;
		/// <summary>Meander wavelength in widths: 10–14 on real rivers.</summary>
		public float MeanderWavelengthWidths = 11f;
		/// <summary>The bed gradient past which a river runs straight.</summary>
		public float MeanderGradient = 0.012f;

		/// <summary>Within this of a scene's edge the river is held to the planet's line, metres, so a neighbour scene meets it there.</summary>
		public float EdgeHoldMetres = 200f;

		/// <summary>
		/// The tightest bend a river's centre line may take, as a radius in its widths, and at least
		/// <see cref="MinBendRadiusMetres"/>: real channels seldom bend tighter than about twice their width (Leopold &amp;
		/// Wolman's r/w of 2–3), and none turns back on itself. Tighter than half its width the channel's inside bank
		/// folds over itself, and its water's ribbon with it (<see cref="RiverShaping.EaseBends"/>).
		/// </summary>
		public float MinBendRadiusWidths = 1.5f;
		public float MinBendRadiusMetres = 4f;

		/// <summary>
		/// How far the surface may stand above the lower of its banks, metres, held there by a levee,
		/// where the sill below stands higher than the banks. 0: a river cuts through what it cannot
		/// fill below its banks — a rise downstream of it is the same to the water as a dam, and
		/// filling behind it would be a lake, which is the planet's to decide.
		/// </summary>
		public float MaxPerchMetres = 0f;
		/// <summary>The surface sits this far under its lower bank where the ground allows, metres.</summary>
		public float InsetMetres = 0.25f;
		/// <summary>The gentlest the surface falls downstream, metres per metre.</summary>
		public float MinGradient = 0.0003f;
		/// <summary>
		/// The steepest a river grades down onto the water it ends in, metres per metre: a tributary arriving
		/// above the river it joins cuts its lower course down to it over as long a stretch as this needs, so
		/// it runs in as rapids rather than off a ledge, as real tributaries grade to their base level.
		/// </summary>
		public float SettleGradient = 0.04f;
		/// <summary>
		/// How far below mean sea level the tide can fall, metres: a river reaching the sea runs on across the
		/// ground the low tide bares, in its own channel, to the low-water line, so at low tide it still meets
		/// the sea instead of stopping above a bare shore. The sea's tide never passes
		/// <c>WaterEnvironment.MaximumTideMetres</c> (FishMMO.Water); a test holds the two equal.
		/// </summary>
		public float IntertidalMetres = 2.5f;

		/// <summary>Bank above the surface, metres: built up as a levee where the ground is lower.</summary>
		public float FreeboardMetres = 0.4f;

		/// <summary>
		/// How far under the water's surface the ground a sample wide along a river's edge is held, metres (WaterCarver): the
		/// terrain between samples is a plane, and a bank sample at bank height beside the channel tilted it up into the water.
		/// Never flush: ground within a few centimetres of the water over a sample's width is the water's plane, and the
		/// terrain's triangles stood through it in straight-edged facets along the shore (2026-10-13, a pond's arm on Flo
		/// Monolith). A little under, the water stands over it and its shore lies on the levee's face past it.
		/// </summary>
		public float EdgeShelfMetres = 0.05f;
		/// <summary>How far a levee's back slope spreads out past its top, metres.</summary>
		public float LeveeMetres = 6f;
		/// <summary>
		/// How far out from the water a levee's top runs before its back slope (at <see cref="BankDegrees"/>), metres: the
		/// drawn water's margin past its banks (InlandWaterRenderer.MinBankMarginMetres, 2.6 m), so the water's edge always
		/// lies on ground that holds it. The carve adds a sample's diagonal and a half: the terrain between samples is
		/// their plane, and a top one sample wide sank under the water toward the next sample out, already on the back
		/// slope, and the water ran out through it (2026-10-13 coverage map).
		/// </summary>
		public float LeveeCrestMetres = 2.6f;
		/// <summary>The steepest a cut bank stands, degrees.</summary>
		public float BankDegrees = 38f;
		/// <summary>How far each side of the channel a bank is cut back at least, metres; further where the ground stands higher, out to <see cref="MaxCorridorMetres"/>.</summary>
		public float CorridorMetres = 60f;
		/// <summary>
		/// The furthest a bank is cut back, metres: where a river has cut a gorge through a sill (a drained
		/// lake's outlet), its banks reach out until they meet the ground. A gorge deeper than this times the
		/// bank's slope keeps a step at its rim.
		/// </summary>
		public float MaxCorridorMetres = 500f;
		/// <summary>Share of half the width the deepest line moves to the outside of a bend, at most.</summary>
		public float BendShift = 0.35f;

		// ── Bars and beaches ──────────────────────────────────────

		/// <summary>A point bar on the inside of a sharp bend, in river widths across.</summary>
		public float PointBarWidths = 1.2f;
		/// <summary>The bend sharpness (curvature × width) at which a point bar is at its widest.</summary>
		public float PointBarBend = 0.2f;
		/// <summary>A beach along both banks of a slow river, in river widths across.</summary>
		public float BeachWidths = 0.35f;
		/// <summary>The bed gradient past which a river lays no beaches: it carries its sand away.</summary>
		public float BeachGradient = 0.004f;
		/// <summary>How steeply a bar rises out of the water, degrees.</summary>
		public float BarDegrees = 5f;
		/// <summary>How far above the water a bar's edge stands, metres: dry, but only just.</summary>
		public float BarLipMetres = 0.08f;
		/// <summary>
		/// How near the level of the lake it enters or leaves a river's water is the lake's, metres: its drowned
		/// mouth or outlet, where no bar or levee is built, since a bar standing a lip above the lake there dams
		/// the river off from it.
		/// </summary>
		public float MouthMetres = 0.3f;
		/// <summary>
		/// How far a river's channel may run on into the lake it enters or leaves, in its widths: through the
		/// lake's shallow margin, until the lake floor is as deep as its own bed.
		/// </summary>
		public float MouthReachWidths = 6f;
		/// <summary>
		/// The largest grain the water can move, metres, under which its bars are all sand, and over which
		/// <see cref="GravelGrainMetres"/> all gravel: Shields' criterion on the force the flow puts on its bed
		/// (<see cref="RiverShaping.Competence"/>).
		/// </summary>
		public float SandGrainMetres = 0.002f;
		public float GravelGrainMetres = 0.016f;

		// ── Shallows ──────────────────────────────────────────────

		/// <summary>
		/// How a river's width answers its banks: width × (1 + this × (½ − cohesion)), 0.6 … 2.2. Bare banks
		/// (a desert, a gravel plain) let it spread wide and shallow; densely rooted ones keep it narrow and
		/// deep; ordinary vegetated banks (½) are what hydraulic geometry's constants describe.
		/// </summary>
		public float LooseBankSpread = 1.4f;
		/// <summary>Riffle to riffle along a gentle river, in widths: 5–7 on real rivers.</summary>
		public float RiffleSpacingWidths = 6f;
		/// <summary>How far the depth swings between pool and riffle, as a share of the mean.</summary>
		public float RiffleAmplitude = 0.45f;
		/// <summary>How much wider a riffle is than the mean, and a pool narrower, as a share.</summary>
		public float RiffleWidthSwing = 0.15f;
		/// <summary>The bed gradient past which riffles and pools give way to rapids.</summary>
		public float RiffleGradient = 0.02f;
		/// <summary>The shallowest water over a riffle, metres.</summary>
		public float MinRiffleDepth = 0.2f;

		/// <summary>
		/// Manning's roughness of a natural channel: 0.03 clean and straight, 0.035 with pools and
		/// gravel, 0.05 weedy or bouldery. With the slope it sets how deep and fast a discharge runs:
		/// steep reaches shallow and quick, gentle ones deep and slow.
		/// </summary>
		public float Manning = 0.035f;

		/// <summary>
		/// Slope above which a channel's roughness grows with its steepness (Jarrett 1984, n = 0.39 S^0.38 R^−0.16
		/// for mountain streams): steps, pools and boulders slow the water, so a steep stream runs deeper and only
		/// a little faster than a smooth channel would.
		/// </summary>
		public float JarrettSlope = 0.002f;

		/// <summary>
		/// How a channel's width answers its slope at the same discharge, as an exponent: narrow and quick where
		/// it is steep, wide and slow where it is flat. Field studies find about −0.19 (Finnegan et al. 2005,
		/// w ∝ Q^0.38 S^−0.19); a little milder here, because the steep stream's own roughness (Jarrett) already
		/// deepens it, and the full figure made torrents deeper than lowland rivers.
		/// </summary>
		public float WidthSlopeExponent = -0.15f;
		/// <summary>The slope at which the width is hydraulic geometry's own; and the most it narrows and widens either way.</summary>
		public float WidthSlopeReference = 0.003f;
		public float WidthSlopeMin = 0.65f;
		public float WidthSlopeMax = 1.6f;

		// ── Rapids and falls ──────────────────────────────────────

		/// <summary>Surface gradient from which the water is a rapid: broken, white.</summary>
		public float RapidGradient = 0.03f;
		/// <summary>Surface gradient from which it falls: a cascade or a waterfall.</summary>
		public float FallGradient = 0.15f;
		/// <summary>The share of a fall's drop taken in the step at its lip (<see cref="RiverShaping.Knickpoints"/>).</summary>
		public float StepShare = 0.85f;
		/// <summary>The least horizontal run of a fall's step, metres: about a terrain sample, so the ledge stands near upright.</summary>
		public float StepRunMetres = 1.5f;
		/// <summary>The least drop, metres, a fall scours a pool under.</summary>
		public float MinPoolDropMetres = 1.5f;
		/// <summary>The deepest a plunge pool is scoured below the river's own bed, metres.</summary>
		public float MaxPoolMetres = 6f;

		// ── A fall's basin (WaterCarver.FallBasin) ────────────────

		/// <summary>
		/// How steep the walls of a fall's basin and of the gorge below it rise from the water, degrees, and how far that
		/// wanders either way: rock walls, steeper than a cut bank, never one ruled plane. (The headwall itself drops as the
		/// water does over the lip.)
		/// </summary>
		public float FallWallDegrees = 62f;
		public float FallWallJitterDegrees = 12f;
		/// <summary>
		/// How far out from the lip's water the ground beside the curtain runs on as the face behind it, metres: its banks rise
		/// as a cut bank's there, and as a wall from there out, where the brink starts to wander.
		/// </summary>
		public float FallNotchBlendMetres = 4f;
		/// <summary>How far the basin's rim wanders in and out, metres, at most (and a fifth of its radius at most).</summary>
		public float FallRimJitterMetres = 3f;
		/// <summary>How fast the gorge below the basin widens downstream, metres across per metre along, each side.</summary>
		public float FallGorgeSpread = 0.3f;
		/// <summary>
		/// The share of a fall's brink speed (critical flow over a free overfall, from the lip's discharge) its brink is rounded
		/// for: the ground past the lip follows the arc water this slow would fall in, so faster water clears it too.
		/// </summary>
		public float BrinkSpeedShare = 0.8f;
		/// <summary>How far under that arc the ground past the brink is held, metres, once a sample spacing out (none at the lip).</summary>
		public float BrinkClearanceMetres = 0.4f;
	}

	/// <summary>What the water is doing at a point of a river: what its surface looks like and how it sounds.</summary>
	public enum RiverReach : byte
	{
		/// <summary>Smooth, steady flow.</summary>
		Run = 0,
		/// <summary>Shallow and quick over gravel: rippled.</summary>
		Riffle = 1,
		/// <summary>Deep and slow between riffles.</summary>
		Pool = 2,
		/// <summary>Steep and broken: white water.</summary>
		Rapid = 3,
		/// <summary>Falling: a cascade or a waterfall.</summary>
		Fall = 4,
	}

	/// <summary>
	/// One river's run through a scene: its centre line in scene metres from upstream to downstream,
	/// and at every point its size, its water's surface and its bed.
	/// </summary>
	/// <remarks>
	/// The one description every later stage reads: the carve, the lakes it meets, the paint, the
	/// scatter's exclusion and, later, the water's mesh and flow. Pure data and arithmetic.
	/// </remarks>
	public sealed class RiverPath
	{
		public int Id;
		/// <summary>The planet river this run is cut from.</summary>
		public int PlanetRiver;
		public RiverEnd Start;
		public RiverEnd End;
		/// <summary>The lake it leaves or enters, the river it joins (scene ids); −1 for none.</summary>
		public int StartLake = -1;
		public int EndLake = -1;
		public int JoinsRiver = -1;
		/// <summary>False for a dry wash: a bed and banks, no water.</summary>
		public bool Perennial = true;

		public float[] X;
		public float[] Z;
		/// <summary>Distance along the line from its first point, metres.</summary>
		public float[] S;
		public float[] Discharge;
		public float[] Width;
		public float[] Depth;
		/// <summary>The water's surface (a dry wash's: its banks' foot), scene metres; never rises downstream.</summary>
		public float[] Surface;
		/// <summary>The deepest point of the bed, scene metres.</summary>
		public float[] Bed;
		/// <summary>Per point, signed curvature (1/m), positive turning left: which bank is the outside of a bend.</summary>
		public float[] Curvature;
		/// <summary>Per point, what the water is doing there; null where not worked out (reads as a run).</summary>
		public RiverReach[] Reach;
		/// <summary>Per point, the largest grain the water can move, metres (<see cref="RiverShaping.Competence"/>); null where not worked out.</summary>
		public float[] Grain;
		/// <summary>
		/// The points before this index stand in a lake the river ponds (its valley lies below the water it runs on
		/// into): they build no levee or bar, as the lake holds that water, not banks. 0 for none.
		/// </summary>
		public int PondUntil;

		public int Count => X != null ? X.Length : 0;

		/// <summary>Speed of the water at a point, m/s, from continuity: Q / (w · d), the channel taken as parabolic (2/3 of the rectangle).</summary>
		public float SpeedAt(int i) => Perennial ? Discharge[i] / Math.Max(0.05f, Width[i] * Depth[i] * (2f / 3f)) : 0f;
	}

	/// <summary>Shapes a river's line and works out its water's surface along it.</summary>
	public static class RiverShaping
	{
		/// <summary>Chaikin's corner cutting, <paramref name="passes"/> times: the grid's 45° steps rounded into a curve through the same ground.</summary>
		public static void Chaikin(List<float> x, List<float> z, List<float> value, int passes)
		{
			for (int pass = 0; pass < passes && x.Count >= 3; pass++)
			{
				var nx = new List<float>(x.Count * 2);
				var nz = new List<float>(x.Count * 2);
				var nv = new List<float>(x.Count * 2);
				nx.Add(x[0]);
				nz.Add(z[0]);
				nv.Add(value[0]);
				for (int i = 0; i < x.Count - 1; i++)
				{
					nx.Add(0.75f * x[i] + 0.25f * x[i + 1]);
					nz.Add(0.75f * z[i] + 0.25f * z[i + 1]);
					nv.Add(0.75f * value[i] + 0.25f * value[i + 1]);
					nx.Add(0.25f * x[i] + 0.75f * x[i + 1]);
					nz.Add(0.25f * z[i] + 0.75f * z[i + 1]);
					nv.Add(0.25f * value[i] + 0.75f * value[i + 1]);
				}
				nx.Add(x[x.Count - 1]);
				nz.Add(z[z.Count - 1]);
				nv.Add(value[value.Count - 1]);
				x.Clear(); x.AddRange(nx);
				z.Clear(); z.AddRange(nz);
				value.Clear(); value.AddRange(nv);
			}
		}

		/// <summary>Points every <paramref name="spacing"/> metres along a line, its values carried linearly; the ends kept.</summary>
		public static void Resample(List<float> x, List<float> z, List<float> value, float spacing)
		{
			if (x.Count < 2)
			{
				return;
			}
			var length = new float[x.Count];
			for (int i = 1; i < x.Count; i++)
			{
				length[i] = length[i - 1] + Distance(x[i - 1], z[i - 1], x[i], z[i]);
			}
			float total = length[x.Count - 1];
			int steps = Math.Max(1, (int)Math.Round(total / Math.Max(0.1f, spacing)));
			var nx = new List<float>(steps + 1);
			var nz = new List<float>(steps + 1);
			var nv = new List<float>(steps + 1);
			int seg = 0;
			for (int k = 0; k <= steps; k++)
			{
				float s = total * k / steps;
				while (seg < x.Count - 2 && length[seg + 1] < s)
				{
					seg++;
				}
				float span = Math.Max(1e-6f, length[seg + 1] - length[seg]);
				float t = Math.Max(0f, Math.Min(1f, (s - length[seg]) / span));
				nx.Add(x[seg] + (x[seg + 1] - x[seg]) * t);
				nz.Add(z[seg] + (z[seg + 1] - z[seg]) * t);
				nv.Add(value[seg] + (value[seg + 1] - value[seg]) * t);
			}
			x.Clear(); x.AddRange(nx);
			z.Clear(); z.AddRange(nz);
			value.Clear(); value.AddRange(nv);
		}

		/// <summary>
		/// Moves a line sideways onto the lowest ground near it: at each point the lowest of the ground
		/// across the line within <see cref="RiverSettings.SnapMetres"/> (a little dearer the farther),
		/// the moves smoothed along the line so it bends like a river and not like a search, and held to
		/// nothing where <paramref name="hold"/> is 0.
		/// </summary>
		/// <param name="ground">The ground, scene metres, at (east, north): smoothed, so a pebble's dip does not pull the river.</param>
		/// <param name="hold">Per point, 0 … 1: how freely it may move (0 at a scene's edge).</param>
		/// <param name="smoothMetres">Half-width of the smoothing of the moves along the line.</param>
		/// <param name="minBendRadius">
		/// The tightest bend the moved line may be left with, metres; 0 for no limit. Each point moves along its own normal,
		/// so one moved toward the inside of a bend by as much as the bend's radius meets the points beside it there.
		/// </param>
		/// <remarks>
		/// Each move is held to the room on its side of the line (<see cref="RoomBeside"/>) less <paramref name="minBendRadius"/>.
		/// Moved further, the line on the inside of a bend folds over itself: where the lowest ground lies the
		/// same way for a long stretch (a slope, not a valley floor), the points all go most of the reach that way, a rounded
		/// corner of the planet's line is offset past its own centre into a loop, and Untangle cuts the loop out at its
		/// crossing and leaves a hairpin. Flo Monolith's river 0 turned back on itself by 146° at its junction with river 1,
		/// and by 139° near the coast between straight legs 40 m long: the shape of a line offset past its corners
		/// (2026-10-12: "Fix the river issues"). <see cref="EaseBends"/> takes out whatever tight bend is left.
		/// </remarks>
		public static void SnapToFloor(List<float> x, List<float> z, Func<float, float, float> ground, float[] hold, float reach, float penalty, float smoothMetres,
			float minBendRadius = 0f)
		{
			int count = x.Count;
			if (count < 3 || reach <= 0f)
			{
				return;
			}
			Normals(x, z, out float[] nx, out float[] nz);
			var move = new float[count];
			float step = Math.Max(1f, reach / 20f);
			for (int i = 0; i < count; i++)
			{
				float best = float.MaxValue, bestOffset = 0f;
				for (float o = -reach; o <= reach + 1e-3f; o += step)
				{
					float h = ground(x[i] + nx[i] * o, z[i] + nz[i] * o) + penalty * Math.Abs(o);
					if (h < best)
					{
						best = h;
						bestOffset = o;
					}
				}
				move[i] = bestOffset;
			}
			float spacing = Distance(x[0], z[0], x[1], z[1]);
			move = SmoothAlong(move, Math.Max(1, (int)Math.Round(smoothMetres / Math.Max(0.1f, spacing))));
			if (minBendRadius > 0f)
			{
				for (int i = 0; i < count; i++)
				{
					move[i] = Math.Sign(move[i]) * Math.Min(Math.Abs(move[i]), RoomBeside(x, z, nx, nz, i, move[i], minBendRadius));
				}
			}
			for (int i = 0; i < count; i++)
			{
				float m = move[i] * (hold != null ? hold[i] : 1f);
				x[i] += nx[i] * m;
				z[i] += nz[i] * m;
			}
		}

		/// <summary>
		/// Swings a line from side to side where its bed is gentle: wavelength and amplitude from its
		/// width, a second slower swing laid over so the bends are not all alike.
		/// </summary>
		/// <summary>
		/// Takes the loops out of a river's line: wherever it comes back within its channels' half widths and
		/// <paramref name="marginMetres"/> of itself further along (a crossing, or a neck narrower than its banks), the
		/// stretch between is cut out, as a river cuts through a meander's neck and leaves the loop behind. Returns the
		/// points removed. <paramref name="along"/> (the planet line's parameter) is cut with the line.
		/// </summary>
		/// <remarks>
		/// Snapping each point to the valley floor and swinging the line into meanders moved points independently, and a
		/// line pulled into the same valley bottom from both sides crossed itself: Flo Monolith's main river did 36 times,
		/// at a cascade among them, where a lower reach ran through the banks of the pool above it. Neither could be
		/// carved: the ground there cannot be both the pool's bank and the channel below, and the pools hung over air.
		/// </remarks>
		public static int Untangle(List<float> x, List<float> z, List<float> along, IList<float> width, float marginMetres)
		{
			int removed = 0;
			var widths = new List<float>(width);
			for (int i = 0; i < x.Count - 2; i++)
			{
				float arc = 0f;
				int cut = -1;
				for (int j = i + 1; j < x.Count; j++)
				{
					arc += Distance(x[j - 1], z[j - 1], x[j], z[j]);
					float clear = 0.5f * (widths[i] + widths[j]) + marginMetres;
					if (arc <= 3f * clear)
					{
						continue;
					}
					if (SegmentDistance(x[i], z[i], x[j - 1], z[j - 1], x[j], z[j]) < clear)
					{
						cut = j;
					}
				}
				if (cut > i + 1)
				{
					int count = cut - i - 1;
					x.RemoveRange(i + 1, count);
					z.RemoveRange(i + 1, count);
					along.RemoveRange(i + 1, count);
					widths.RemoveRange(i + 1, count);
					removed += count;
				}
			}
			return removed;
		}

		/// <summary>
		/// How far point <paramref name="i"/> of a line may move along its normal toward <paramref name="move"/>'s side and stay
		/// the nearest point of the line to where it lands, less <paramref name="margin"/>, metres (0 for none): the radius
		/// of the largest circle touching the line at the point from that side with no other point of the line inside it.
		/// </summary>
		/// <remarks>
		/// Moved no further, no point passes another: the moved line keeps its order and cannot fold, and where the room is
		/// a bend's own radius it is left a bend at least <paramref name="margin"/> round. A bend's radius alone is not
		/// enough: beside a sharp corner the straight stretches have no curvature, yet moved toward the corner's inside by
		/// more than its radius they run past its centre and cross one another there.
		/// </remarks>
		public static float RoomBeside(IList<float> x, IList<float> z, float[] nx, float[] nz, int i, float move, float margin)
		{
			float magnitude = Math.Abs(move);
			if (magnitude < 1e-4f)
			{
				return 0f;
			}
			float sx = nx[i] * Math.Sign(move), sz = nz[i] * Math.Sign(move);
			// A point further than twice the reach asked for cannot make the circle smaller than the reach: d² / 2(d·n) ≥ |d| / 2.
			float limit = 2f * (magnitude + margin);
			float limitSq = limit * limit;
			float room = float.MaxValue;
			for (int j = 0; j < x.Count; j++)
			{
				float dx = x[j] - x[i], dz = z[j] - z[i];
				float dSq = dx * dx + dz * dz;
				float toward = dx * sx + dz * sz;
				if (j == i || toward <= 1e-4f || dSq > limitSq)
				{
					continue;
				}
				// The circle touching the line at i on that side whose rim passes through j.
				room = Math.Min(room, dSq / (2f * toward));
			}
			return room == float.MaxValue ? magnitude : Math.Max(0f, room - margin);
		}

		/// <summary>
		/// How far a turn of <paramref name="turn"/> radians between chords <paramref name="la"/> and <paramref name="lb"/> m
		/// long overruns a bend of <paramref name="radius"/>: above 1, the bend is tighter. A round bend of that radius through
		/// the turn meets each chord R·tan(θ/2) from the point, and each chord is shared with the point at its other end, so
		/// it must fit in half the shorter chord.
		/// </summary>
		/// <remarks>
		/// It was θ·R against the chords' mean length: alike for a gentle turn on even chords, but long chords let any turn
		/// through, and a cut-off that merged chords could leave a 175° reversal between a 39 m and a 30 m chord standing as
		/// a gentle bend (Flo Monolith's river 0 ran 39 m down its own channel and back, 2026-10-15). Toward a reversal
		/// tan(θ/2) grows without bound, so no chord is long enough.
		/// </remarks>
		public static float BendExcess(float turn, float la, float lb, float radius)
		{
			float shorter = Math.Min(la, lb);
			if (shorter < 1e-4f)
			{
				return float.PositiveInfinity;
			}
			float half = Math.Min(0.5f * turn, 1.5707f);
			return radius * (float)Math.Tan(half) / (0.5f * shorter);
		}

		/// <summary>The most relaxation sweeps a stretch whose tight bend was cut off is given to round out.</summary>
		private const int MaxEaseSweeps = 4000;

		/// <summary>
		/// Takes every bend tighter than its river may take out of a line, and returns the points removed: no point is left
		/// bending tighter than its least radius (<see cref="RiverSettings.MinBendRadiusWidths"/> of its width, and at least
		/// <see cref="RiverSettings.MinBendRadiusMetres"/>), so none turns back on itself. Run after <see cref="Untangle"/>,
		/// on the coarse line, before it is resampled finer. <paramref name="along"/> (the planet line's parameter) is cut
		/// and carried with the line; <paramref name="width"/> is per point of the line as given.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A tight bend is cut off as a river cuts through one: the point turning most for the chords either side of it is
		/// removed, so the line runs on the chord between its neighbours, until each turn has room for its radius (a turn θ
		/// needs R·tan(θ/2) of each chord, half the shorter at most: <see cref="BendExcess"/>). That only ever shortens the line onto the inside of its bend, so it
		/// cannot add a loop, and a hairpin's two strands go with its tip. Each stretch the cuts reached, and as far again
		/// as the bend's diameter beyond, is then resampled at the line's own spacing and relaxed: a point bending tighter
		/// than its radius moves halfway toward the middle of its neighbours, sweep after sweep, until none does. The
		/// stretch's two end points never move, so it meets the line beyond as the line always did, and a line with no tight
		/// bend is returned exactly as it was.
		/// </para>
		/// <para>
		/// Hairpins came out of the snaps, the meanders and Untangle's cuts (a loop cut at its neck joins two strands running
		/// opposite ways): Flo Monolith's river 0 turned back on itself by 146°, 139° and 86° (2026-10-12). A line that
		/// doubles back has a channel whose inside bank lies across its own water, and a ribbon whose quads twist over.
		/// </para>
		/// </remarks>
		public static int EaseBends(List<float> x, List<float> z, List<float> along, IList<float> width, RiverSettings settings)
		{
			int count = x.Count;
			if (count < 3)
			{
				return 0;
			}
			float length = 0f;
			for (int i = 1; i < count; i++)
			{
				length += Distance(x[i - 1], z[i - 1], x[i], z[i]);
			}
			float step = Math.Max(0.5f, length / (count - 1));
			var widths = new List<float>(count);
			for (int i = 0; i < count; i++)
			{
				widths.Add(width != null && i < width.Count ? width[i] : settings.MinWidth);
			}
			float RadiusOf(float w) => Math.Max(settings.MinBendRadiusMetres, settings.MinBendRadiusWidths * w);
			var touched = new List<bool>(new bool[count]);
			var excess = new List<float>(new float[count]);
			float Excess(int i)
			{
				if (i <= 0 || i >= x.Count - 1)
				{
					return 0f;
				}
				float ax = x[i] - x[i - 1], az = z[i] - z[i - 1], bx = x[i + 1] - x[i], bz = z[i + 1] - z[i];
				float la = (float)Math.Sqrt(ax * ax + az * az), lb = (float)Math.Sqrt(bx * bx + bz * bz);
				if (la < 1e-4f || lb < 1e-4f)
				{
					// A point on top of its neighbour turns any way at all: always taken out.
					return float.PositiveInfinity;
				}
				float turn = Math.Abs((float)Math.Atan2(ax * bz - az * bx, ax * bx + az * bz));
				return BendExcess(turn, la, lb, RadiusOf(widths[i]));
			}
			for (int i = 1; i < count - 1; i++)
			{
				excess[i] = Excess(i);
			}
			int removed = 0;
			while (x.Count > 2)
			{
				int worst = -1;
				float most = 1f;
				for (int i = 1; i < x.Count - 1; i++)
				{
					if (excess[i] > most)
					{
						most = excess[i];
						worst = i;
					}
				}
				if (worst < 0)
				{
					break;
				}
				x.RemoveAt(worst);
				z.RemoveAt(worst);
				along.RemoveAt(worst);
				widths.RemoveAt(worst);
				excess.RemoveAt(worst);
				touched.RemoveAt(worst);
				removed++;
				// The two points either side now meet on the chord: only their turns changed.
				touched[worst - 1] = true;
				touched[worst] = true;
				excess[worst - 1] = Excess(worst - 1);
				excess[worst] = Excess(worst);
			}
			if (removed == 0)
			{
				return 0;
			}
			/* Each stretch the cuts reached, and the bend's diameter beyond it (two points at least), to be rounded; stretches
			 * that meet are one, or the point where they met is held by both and keeps whatever turn they leave it. */
			var spans = new List<(int from, int to, float radius)>();
			for (int a = 0; a < x.Count; a++)
			{
				if (!touched[a])
				{
					continue;
				}
				int b = a;
				float widest = widths[a];
				while (b + 1 < x.Count && touched[b + 1])
				{
					b++;
					widest = Math.Max(widest, widths[b]);
				}
				float radius = RadiusOf(widest);
				int beyond = Math.Max(2, (int)Math.Ceiling(2f * radius / step));
				int from = Math.Max(0, a - beyond), to = Math.Min(x.Count - 1, b + beyond);
				if (spans.Count > 0 && from <= spans[spans.Count - 1].to)
				{
					(int f, int t, float r) = spans[spans.Count - 1];
					spans[spans.Count - 1] = (f, Math.Max(t, to), Math.Max(r, radius));
				}
				else
				{
					spans.Add((from, to, radius));
				}
				a = b;
			}
			// From the end, so the indices of those before hold.
			for (int k = spans.Count - 1; k >= 0; k--)
			{
				(int from, int to, float radius) = spans[k];
				if (to - from < 2)
				{
					continue;
				}
				var sx = x.GetRange(from, to - from + 1);
				var sz = z.GetRange(from, to - from + 1);
				var sv = along.GetRange(from, to - from + 1);
				Resample(sx, sz, sv, step);
				Relax(sx, sz, radius);
				x.RemoveRange(from, to - from + 1);
				z.RemoveRange(from, to - from + 1);
				along.RemoveRange(from, to - from + 1);
				x.InsertRange(from, sx);
				z.InsertRange(from, sz);
				along.InsertRange(from, sv);
			}
			return removed;
		}

		/// <summary>
		/// Relaxes a stretch of line until no point bends tighter than <paramref name="radius"/> (<see cref="BendExcess"/>):
		/// such a point moves halfway toward the middle of its neighbours, sweep after
		/// sweep, at most <see cref="MaxEaseSweeps"/> times. The two end points never move.
		/// </summary>
		private static void Relax(List<float> x, List<float> z, float radius)
		{
			int m = x.Count;
			for (int sweep = 0; sweep < MaxEaseSweeps; sweep++)
			{
				bool settled = true;
				for (int i = 1; i < m - 1; i++)
				{
					float ax = x[i] - x[i - 1], az = z[i] - z[i - 1], bx = x[i + 1] - x[i], bz = z[i + 1] - z[i];
					float la = (float)Math.Sqrt(ax * ax + az * az), lb = (float)Math.Sqrt(bx * bx + bz * bz);
					float turn = Math.Abs((float)Math.Atan2(ax * bz - az * bx, ax * bx + az * bz));
					if (BendExcess(turn, la, lb, radius) <= 1.0001f)
					{
						continue;
					}
					x[i] += 0.5f * (0.5f * (x[i - 1] + x[i + 1]) - x[i]);
					z[i] += 0.5f * (0.5f * (z[i - 1] + z[i + 1]) - z[i]);
					settled = false;
				}
				if (settled)
				{
					return;
				}
			}
		}

		/// <summary>
		/// Brings a tributary's last point onto the river it joins, at (<paramref name="toX"/>, <paramref name="toZ"/>), and
		/// returns the line's new point count. The move is eased in over its last stretch (at least 150 m and three times the
		/// move), and that stretch is resampled at <paramref name="spacing"/>, its discharge and drainage area carried along.
		/// </summary>
		/// <remarks>
		/// The ease is measured along the line as it lay before the move. It was measured as the points were moved, from each
		/// point to its already moved neighbour, so the first step counted the whole move as distance along: the ease fell
		/// away within a few points, the last ones were pulled apart, and Flo Monolith's river 1 ended in segments of 7, 11,
		/// 21 and 40 m where its others are 2 m (2026-10-12). Eased over three times the move, no point is carried past
		/// its neighbour, so the line cannot fold; resampled, its last rows stand at the river's own spacing.
		/// </remarks>
		public static int EndOnto(List<float> x, List<float> z, ref float[] discharge, ref float[] area, float toX, float toZ, float spacing)
		{
			int n = x.Count;
			if (n < 2)
			{
				return n;
			}
			float shiftX = toX - x[n - 1], shiftZ = toZ - z[n - 1];
			float shift = (float)Math.Sqrt(shiftX * shiftX + shiftZ * shiftZ);
			if (shift < 1e-4f)
			{
				return n;
			}
			float taper = Math.Max(150f, 3f * shift);
			var fromEnd = new float[n];
			for (int i = n - 2; i >= 0; i--)
			{
				fromEnd[i] = fromEnd[i + 1] + Distance(x[i], z[i], x[i + 1], z[i + 1]);
			}
			int first = n - 1;
			for (int i = n - 1; i >= 0 && fromEnd[i] < taper; i--)
			{
				float w = 1f - SmoothStep(fromEnd[i] / taper);
				x[i] += shiftX * w;
				z[i] += shiftZ * w;
				first = i;
			}
			// Resampled from the last point left where it was, so the stretch meets the rest of the line on it.
			int from = Math.Max(0, first - 1);
			var sx = x.GetRange(from, n - from);
			var sz = z.GetRange(from, n - from);
			var index = new List<float>(n - from);
			for (int i = from; i < n; i++)
			{
				index.Add(i);
			}
			Resample(sx, sz, index, Math.Max(0.5f, spacing));
			x.RemoveRange(from, n - from);
			z.RemoveRange(from, n - from);
			x.AddRange(sx);
			z.AddRange(sz);
			discharge = CarryOnto(discharge, from, index);
			area = CarryOnto(area, from, index);
			return x.Count;
		}

		/// <summary>Values per point with those from <paramref name="from"/> on replaced by their values at the fractional points <paramref name="index"/>.</summary>
		private static float[] CarryOnto(float[] values, int from, List<float> index)
		{
			if (values == null)
			{
				return null;
			}
			var result = new float[from + index.Count];
			Array.Copy(values, result, Math.Min(from, values.Length));
			for (int k = 0; k < index.Count; k++)
			{
				int i0 = Math.Max(0, Math.Min(values.Length - 1, (int)Math.Floor(index[k])));
				int i1 = Math.Min(values.Length - 1, i0 + 1);
				float t = index[k] - (float)Math.Floor(index[k]);
				result[from + k] = values[i0] + (values[i1] - values[i0]) * t;
			}
			return result;
		}

		/// <summary>The distance from (px, pz) to the segment (ax, az)-(bx, bz).</summary>
		private static float SegmentDistance(float px, float pz, float ax, float az, float bx, float bz)
		{
			float ex = bx - ax, ez = bz - az;
			float lengthSq = ex * ex + ez * ez;
			float t = lengthSq > 1e-8f ? Clamp(((px - ax) * ex + (pz - az) * ez) / lengthSq, 0f, 1f) : 0f;
			return Distance(px, pz, ax + ex * t, az + ez * t);
		}

		public static void Meander(List<float> x, List<float> z, float[] width, float[] gradient, float[] hold, RiverSettings settings, uint seed)
		{
			int count = x.Count;
			if (count < 3)
			{
				return;
			}
			Normals(x, z, out float[] nx, out float[] nz);
			float phase = (seed & 0xFFFF) / 65536f * 6.2831853f;
			float phase2 = (seed >> 16) / 65536f * 6.2831853f;
			float s = 0f;
			var offset = new float[count];
			for (int i = 0; i < count; i++)
			{
				if (i > 0)
				{
					float ds = Distance(x[i - 1], z[i - 1], x[i], z[i]);
					float lambda = Math.Max(30f, settings.MeanderWavelengthWidths * width[i]);
					// Phase advances by the local wavelength, so a widening river lengthens its bends without a jump.
					s += ds / lambda;
				}
				float flat = Math.Max(0f, Math.Min(1f, 1f - gradient[i] / Math.Max(1e-5f, settings.MeanderGradient)));
				float amplitude = settings.MeanderWidths * width[i] * flat;
				offset[i] = amplitude * (float)(Math.Sin(6.2831853 * s + phase) * (0.75 + 0.25 * Math.Sin(6.2831853 * s / 3.7 + phase2)));
			}
			for (int i = 0; i < count; i++)
			{
				float m = offset[i] * (hold != null ? hold[i] : 1f);
				x[i] += nx[i] * m;
				z[i] += nz[i] * m;
			}
		}

		/// <summary>
		/// The water's surface along a line: as high as the sill below it holds it, so a dip in the bed
		/// is a pool, but never higher than its own banks, so where a rise in its way stands above them
		/// the river cuts through it; and never rising downstream.
		/// </summary>
		/// <param name="floor">Per point, the lowest ground across the channel (smoothed along the line).</param>
		/// <param name="bank">Per point, the lower of the ground on its two banks (smoothed along the line).</param>
		/// <param name="spacing">Per point, metres from the point before it (index 0 unused).</param>
		/// <param name="startCeiling">The highest the first point may be (a lake it leaves); +∞ for none.</param>
		/// <param name="endFloor">The lowest any point may be (the lake, sea or river it ends in); −∞ for none.</param>
		/// <remarks>
		/// Without the banks a river filled every reach up to the highest ground anywhere downstream of it
		/// and stood above its own floodplain behind levees all the way; without the sill every dip in its
		/// bed held its surface down for the rest of its run and cut a trench through everything below.
		/// </remarks>
		/// <param name="settleMetres">
		/// Over how long a last stretch a river ending in other water eases down to exactly
		/// <paramref name="endFloor"/>, so it meets that water at its level rather than above it: the
		/// surface is lowered by a ramp that never makes it rise. 0 leaves it where the ground put it.
		/// </param>
		/// <param name="meetLevel">
		/// The level of the standing water it ends in (the sea's mean level, a lake's), which fills its shore up to there:
		/// the river is in it from the first point at or under that level, and runs in without a ledge. −∞ for none
		/// (a river joining another, which fills nothing round it).
		/// </param>
		public static float[] Surface(float[] floor, float[] bank, float[] spacing, float startCeiling, float endFloor, RiverSettings settings,
			float settleMetres = 0f, float meetLevel = float.NegativeInfinity)
		{
			int count = floor.Length;
			var surface = new float[count];
			float sill = float.MinValue;
			for (int i = count - 1; i >= 0; i--)
			{
				sill = Math.Max(sill, floor[i]);
				float banks = Math.Max(floor[i], bank[i]);
				surface[i] = Math.Min(sill - settings.InsetMetres, banks - settings.InsetMetres + Math.Min(settings.MaxPerchMetres, Math.Max(0f, sill - banks)));
			}
			surface[0] = Math.Min(surface[0], startCeiling);
			for (int i = 1; i < count; i++)
			{
				surface[i] = Math.Min(surface[i], surface[i - 1] - settings.MinGradient * spacing[i]);
			}
			if (!float.IsNegativeInfinity(endFloor))
			{
				/* Never under the water it ends in, but only as far up as its own banks hold it. Raised everywhere, a
				 * tributary whose valley lies lower than the river it joins stood at that river's level for its whole
				 * length, over air: Flo Monolith's river 1 held 166.4 m for 1.1 km above a floor at 150 m. Where its banks
				 * cannot hold the joined river's level, the river keeps to its own ground and meets the other a little
				 * below it; the backwater reaches up it only as far as its banks stand high enough. */
				bool backwater = true;
				for (int i = count - 1; i >= 0; i--)
				{
					float banks = Math.Max(floor[i], bank[i]) - settings.InsetMetres;
					if (backwater && banks >= endFloor)
					{
						surface[i] = Math.Max(surface[i], endFloor);
					}
					else
					{
						backwater = false;
					}
				}
				// Meets the water it ends in at that water's level: the excess taken off over the last stretch.
				float excess = surface[count - 1] - endFloor;
				if (settleMetres > 0f && excess > 0f)
				{
					// Long enough that the steepest part of the smooth ramp (1.5 × its mean) stays under the settle gradient.
					settleMetres = Math.Max(settleMetres, 1.5f * excess / Math.Max(1e-3f, settings.SettleGradient));
					float fromEnd = 0f;
					for (int i = count - 1; i >= 0; i--)
					{
						if (i < count - 1)
						{
							fromEnd += spacing[i + 1];
						}
						float t = 1f - Clamp(fromEnd / settleMetres, 0f, 1f);
						if (t <= 0f)
						{
							break;
						}
						surface[i] = Math.Max(endFloor, surface[i] - excess * t * t * (3f - 2f * t));
					}
				}
			}
			/* Nor off a ledge into standing water. The settle above measures from the line's last point, which for the sea
			 * lies out past the shore under the low-water line, so it never ran there: Flo Monolith's river 0 held 2.1 m on
			 * its beach crest and dropped to −1.5 m at the shore, named a fall and drawn as one into (and under) the sea.
			 * The whole steep run that crosses the water's level is graded instead: from its brink above to its foot below
			 * (the floor is smoothed along the line, so a bluff spans several points, and only grading the one point where
			 * the surface first met the level left the rest for Knickpoints to gather back into a ledge). The drop is taken
			 * off over the stretch above, so the river cuts down through the crest and runs in at its foot's level, as
			 * rivers grade to their base level. Never under endFloor (the low-water line): what lies under that is the
			 * sea's, and a channel lowered past it inland would be too. */
			if (!float.IsNegativeInfinity(meetLevel))
			{
				int meet = -1;
				for (int i = 1; i < count; i++)
				{
					if (surface[i] <= meetLevel)
					{
						meet = i;
						break;
					}
				}
				if (meet > 0)
				{
					float steep = settings.SettleGradient, search = MouthSearchSettles * Math.Max(15f, settleMetres);
					bool Steep(int i) => surface[i - 1] - surface[i] >= steep * Math.Max(1e-3f, spacing[i]);
					int foot = meet;
					for (float metres = 0f; foot + 1 < count && Steep(foot + 1) && metres <= search; foot++)
					{
						metres += spacing[foot + 1];
					}
					int brink = meet - 1;
					for (float metres = 0f; brink > 0 && Steep(brink) && metres <= search; brink--)
					{
						metres += spacing[brink];
					}
					float target = Math.Max(surface[foot], endFloor);
					float step = surface[brink] - target;
					if (step > 0f)
					{
						// As the settle: long enough that the steepest of the smooth ramp (1.5 × its mean) keeps to the settle gradient.
						float rampMetres = Math.Max(settleMetres, 1.5f * step / Math.Max(1e-3f, settings.SettleGradient));
						float fromFoot = 0f;
						for (int i = foot; i >= 0; i--)
						{
							if (i < foot)
							{
								fromFoot += spacing[i + 1];
							}
							float t = 1f - Clamp(fromFoot / rampMetres, 0f, 1f);
							if (t <= 0f)
							{
								break;
							}
							// Between the brink and the foot it lies (just falling) at the foot's level: the run-in itself.
							surface[i] = Math.Max(Math.Min(surface[i], target + settings.MinGradient * fromFoot), surface[i] - step * t * t * (3f - 2f * t));
						}
					}
				}
			}
			return surface;
		}

		/// <summary>
		/// How far up and down from where a river meets standing water its steep run-in is followed, in settle lengths
		/// (at least 15 m each): a smoothed bluff at the shore, not a mountain stream's whole lower course.
		/// </summary>
		public const float MouthSearchSettles = 4f;

		/// <summary>
		/// The bars along a river: per point, how wide a point bar stands on the inside of its bend, how wide
		/// a beach on either bank, and how sandy (1) or gravelly (0) they are from the water's speed.
		/// </summary>
		public static void Bars(RiverPath river, RiverSettings settings, out float[] point, out float[] beach, out float[] sand)
		{
			int n = river.Count;
			point = new float[n];
			beach = new float[n];
			sand = new float[n];
			float[] gradient = SurfaceGradient(river);
			for (int i = 0; i < n; i++)
			{
				float w = river.Width[i];
				float bend = Math.Abs(river.Curvature[i]) * w / Math.Max(1e-4f, settings.PointBarBend);
				point[i] = settings.PointBarWidths * w * Clamp(bend, 0f, 1f);
				float slow = Clamp(1f - gradient[i] / Math.Max(1e-5f, settings.BeachGradient), 0f, 1f);
				beach[i] = settings.BeachWidths * w * slow;
				// A dry wash's bars are its last flood's sand; a river's, the finest of what its current can move.
				float grain = river.Perennial && river.Grain != null ? river.Grain[i] : settings.SandGrainMetres;
				double t = Math.Log(Math.Max(1e-5f, grain) / settings.SandGrainMetres) / Math.Log(settings.GravelGrainMetres / settings.SandGrainMetres);
				sand[i] = 1f - Clamp((float)t, 0f, 1f);
			}
			/* Nothing settles where the water is speeding over a ledge: no bar on a fall, and none on the run-up to its lip,
			 * fading back in over a few widths upstream. A point bar had lain against Flo Monolith's 61 m lip and its sand
			 * painted on down the cliff (the "dry notch" beside the curtain). */
			if (river.Reach == null)
			{
				return;
			}
			for (int i = 0; i < n; i++)
			{
				if (river.Reach[i] != RiverReach.Fall || (i > 0 && river.Reach[i - 1] == RiverReach.Fall))
				{
					continue;
				}
				int lip = Math.Max(0, i - 1);
				float clear = Math.Max(BrinkClearMetres, 4f * river.Width[lip]);
				for (int k = lip; k >= 0; k--)
				{
					float away = river.S[lip] - river.S[k];
					if (away >= clear)
					{
						break;
					}
					float keep = SmoothStep(away / clear);
					point[k] *= keep;
					beach[k] *= keep;
				}
				/* Nor in the basin the fall scours below it, and only thinly for as far again. A bar is sized by the river's
				 * width, and in the basin that is the pool's: a gravel point bar 15 m wide lay flat beside the pool as a
				 * terrace, ending in a cliff where the river narrowed again (Flo Monolith's 61 m fall). */
				int last = i;
				while (last + 1 < n && river.Reach[last + 1] == RiverReach.Fall)
				{
					last++;
				}
				int foot = Math.Min(n - 1, last + 1);
				float drop = river.Surface[lip] - river.Surface[foot];
				float basin = Math.Max(3f, 0.9f * river.Width[lip] + 0.35f * Math.Max(0f, drop));
				for (int k = foot; k < n; k++)
				{
					float away = river.S[k] - river.S[foot];
					if (away >= 2f * basin)
					{
						break;
					}
					float keep = SmoothStep(away / basin - 1f);
					point[k] *= keep;
					beach[k] *= keep;
				}
			}
			for (int i = 0; i < n; i++)
			{
				if (river.Reach[i] == RiverReach.Fall)
				{
					point[i] = 0f;
					beach[i] = 0f;
				}
			}
		}

		/// <summary>The least distance above a fall's lip kept clear of bars, metres (or four widths, if more).</summary>
		public const float BrinkClearMetres = 20f;

		/// <summary>The surface's fall per metre at each point, over a stretch about a width long each way.</summary>
		public static float[] SurfaceGradient(RiverPath river)
		{
			int n = river.Count;
			var g = new float[n];
			for (int i = 0; i < n; i++)
			{
				float reach = Math.Max(4f, river.Width[i]);
				int a = i, b = i;
				while (a > 0 && river.S[i] - river.S[a] < reach) a--;
				while (b < n - 1 && river.S[b] - river.S[i] < reach) b++;
				float run = river.S[b] - river.S[a];
				g[i] = run > 1e-3f ? Math.Max(0f, (river.Surface[a] - river.Surface[b]) / run) : 0f;
			}
			return g;
		}

		/// <summary>
		/// Gathers each fall's drop into a step at its lip: a real fall drops over a ledge of harder rock (a knickpoint)
		/// rather than sliding down a smooth hillside. <see cref="RiverSettings.StepShare"/> of the drop is taken within
		/// <see cref="RiverSettings.StepRunMetres"/> below the lip, however tall the fall, the rest down to the old foot as
		/// before. (It was a sixth of the drop if longer: a 49 m fall became an 80° chute 8 m long, the water left the lip
		/// far slower than it needed to clear it, and the sheet lay on the chute like a curled quad: Flo Monolith, 2026-10-06.) The surface is only ever lowered, so the carve cuts a gorge below the step, as a fall
		/// eating back into its ledge leaves. Then the reaches are named again: only the step falls; below it the water
		/// pools and runs on as rapids.
		/// </summary>
		public static void Knickpoints(RiverPath river, RiverSettings settings)
		{
			int n = river.Count;
			bool changed = false;
			int i = 0;
			while (i < n)
			{
				if (river.Reach[i] != RiverReach.Fall)
				{
					i++;
					continue;
				}
				int last = i;
				while (last + 1 < n && river.Reach[last + 1] == RiverReach.Fall)
				{
					last++;
				}
				int lip = Math.Max(0, i - 1), foot = Math.Min(n - 1, last + 1);
				float drop = river.Surface[lip] - river.Surface[foot];
				float run = river.S[foot] - river.S[lip];
				if (drop >= settings.MinPoolDropMetres && run > 1e-3f)
				{
					float stepRun = Math.Min(run, settings.StepRunMetres);
					float step = drop * settings.StepShare;
					for (int k = lip + 1; k <= foot; k++)
					{
						float t = river.S[k] - river.S[lip];
						float target = t <= stepRun
							? river.Surface[lip] - step * (t / stepRun)
							: river.Surface[lip] - step - (drop - step) * ((t - stepRun) / Math.Max(1e-3f, run - stepRun));
						if (target < river.Surface[k])
						{
							river.Surface[k] = target;
							river.Bed[k] = target - river.Depth[k];
						}
					}
					changed = true;
				}
				i = last + 1;
			}
			if (!changed)
			{
				return;
			}
			// Named again from the new surface: the step falls, the gentler water below it does not.
			float[] gradient = SurfaceGradient(river);
			for (int k = 0; k < n; k++)
			{
				if (river.Reach[k] != RiverReach.Fall)
				{
					continue;
				}
				river.Reach[k] = gradient[k] >= settings.FallGradient * 2f ? RiverReach.Fall
					: gradient[k] >= settings.RapidGradient ? RiverReach.Rapid : RiverReach.Run;
			}
		}

		/// <summary>
		/// A pool scoured under every fall: where the falling water lands, the bed deepened by about a third of the drop
		/// (at least a metre, at most <see cref="RiverSettings.MaxPoolMetres"/>) and the channel widened, easing back to
		/// the river's own over the pool's reach downstream. The carve cuts it; the falls drawn over it churn it white.
		/// </summary>
		public static void PlungePools(RiverPath river, RiverSettings settings)
		{
			int n = river.Count;
			int i = 0;
			while (i < n)
			{
				if (river.Reach[i] != RiverReach.Fall)
				{
					i++;
					continue;
				}
				int last = i;
				while (last + 1 < n && river.Reach[last + 1] == RiverReach.Fall)
				{
					last++;
				}
				int lip = Math.Max(0, i - 1), foot = Math.Min(n - 1, last + 1);
				float drop = river.Surface[lip] - river.Surface[foot];
				if (drop >= settings.MinPoolDropMetres)
				{
					float poolDepth = Clamp(0.35f * drop + 0.5f, 1f, settings.MaxPoolMetres);
					float radius = Math.Max(3f, 0.9f * river.Width[foot] + 0.35f * drop);
					/* A basin, not the channel a little wider: a tall fall scours a pool far wider than the stream that
					 * feeds it (the river's width and half the drop, up to 30 m). At ×1.4 a 49 m fall landed in a 5 m slot. */
					float poolWidth = Math.Min(30f, Math.Max(1.4f * river.Width[foot], river.Width[foot] + 0.5f * drop));
					/* A pool is level water, held by the sill where it spills out. The rest of a knickpoint's drop ran on below
					 * the foot as a ramp, 6 m down across Flo Monolith's 61 m fall's basin: the tail of the pool stood metres over
					 * the floor the basin's wider rows had scoured beside it. Lowered to the level at the basin's end (only ever
					 * lowered, so the surface still never rises); the bed below follows, as Depth still holds the old depth. */
					int end = foot;
					while (end + 1 < n && Math.Abs(river.S[end + 1] - river.S[foot]) <= radius)
					{
						end++;
					}
					for (int k = foot; k < end; k++)
					{
						river.Surface[k] = Math.Min(river.Surface[k], river.Surface[end]);
					}
					for (int k = Math.Max(lip + 1, foot - 1); k < n; k++)
					{
						float away = Math.Abs(river.S[k] - river.S[foot]);
						if (away > radius)
						{
							break;
						}
						float t = 1f - away / radius;
						float bowl = t * t * (3f - 2f * t);
						river.Bed[k] = Math.Min(river.Bed[k], river.Surface[k] - river.Depth[k] - poolDepth * bowl);
						river.Depth[k] = river.Surface[k] - river.Bed[k];
						/* The basin from the foot down only. A fall's own points stand almost over its foot now the drop is a ledge,
						 * so measured by distance they sat inside the bowl too: the lip was carved pool-wide (a dry shelf beside
						 * the water) and the curtain flared from the river's width to the pool's on the way down. */
						if (k >= foot)
						{
							river.Width[k] += (Math.Max(river.Width[k], poolWidth) - river.Width[k]) * bowl;
						}
						// The foot too: left a rapid, it is as wide as the basin, and its broken water whitened the whole pool to
						// the outlet (SceneWaterBodies.Broken); the fall's own churn is what whitens the impact.
						if (k >= foot && river.Reach[k] != RiverReach.Fall)
						{
							river.Reach[k] = RiverReach.Pool;
						}
					}
				}
				i = last + 1;
			}
		}

		/// <summary>
		/// Lays riffles and pools along a gentle river's bed — shallow, quick water every
		/// <see cref="RiverSettings.RiffleSpacingWidths"/> widths with deep, slow water between — and names
		/// what the water does at each point. The surface is left as it was, but at a fall (<see cref="Knickpoints"/>); otherwise only the bed moves.
		/// </summary>
		public static void Reaches(RiverPath river, RiverSettings settings, uint seed)
		{
			int n = river.Count;
			river.Reach = new RiverReach[n];
			float[] gradient = SurfaceGradient(river);
			float phase = (seed & 0xFFFF) / 65536f * 6.2831853f;
			if (river.Perennial)
			{
				// Narrow where steep, wide where flat, then smoothed so the banks do not step.
				var sloped = new float[n];
				for (int i = 0; i < n; i++)
				{
					float factor = (float)Math.Pow(Math.Max(1e-4, gradient[i]) / settings.WidthSlopeReference, settings.WidthSlopeExponent);
					sloped[i] = river.Width[i] * Clamp(factor, settings.WidthSlopeMin, settings.WidthSlopeMax);
				}
				sloped = SmoothAlong(sloped, 4);
				for (int i = 0; i < n; i++)
				{
					river.Width[i] = Clamp(sloped[i], settings.MinWidth, settings.MaxWidth * 1.5f);
				}

				/* Depth from the slope, by Manning: Q = w · d̄ · d̄^(2/3) · S^(1/2) / n for the mean depth d̄;
				 * the parabolic channel's deepest point is 1.5 d̄. A steep reach runs shallow and fast,
				 * a gentle one deep and slow, at the same discharge. */
				var depth = new float[n];
				for (int i = 0; i < n; i++)
				{
					double slope = Math.Max(1e-4, gradient[i]);
					double width = Math.Max(0.5f, river.Width[i]);
					double mean = Math.Pow(river.Discharge[i] * settings.Manning / (width * Math.Sqrt(slope)), 0.6);
					if (slope > settings.JarrettSlope)
					{
						// A steep stream's roughness depends on its own depth: two rounds settle it.
						for (int k = 0; k < 3; k++)
						{
							double n0 = Math.Max(settings.Manning, 0.39 * Math.Pow(slope, 0.38) * Math.Pow(Math.Max(0.05, mean), -0.16));
							mean = Math.Pow(river.Discharge[i] * n0 / (width * Math.Sqrt(slope)), 0.6);
						}
					}
					depth[i] = Clamp((float)(1.5 * mean), settings.MinRiffleDepth, settings.MaxDepth);
				}
				depth = SmoothAlong(depth, 3);
				for (int i = 0; i < n; i++)
				{
					river.Depth[i] = depth[i];
					river.Bed[i] = river.Surface[i] - depth[i];
				}
			}
			float s = 0f;
			for (int i = 0; i < n; i++)
			{
				if (i > 0)
				{
					s += (river.S[i] - river.S[i - 1]) / Math.Max(4f, settings.RiffleSpacingWidths * river.Width[i]);
				}
				RiverReach reach = RiverReach.Run;
				if (gradient[i] >= settings.FallGradient)
				{
					reach = RiverReach.Fall;
				}
				else if (gradient[i] >= settings.RapidGradient)
				{
					reach = RiverReach.Rapid;
				}
				else if (river.Perennial && gradient[i] < settings.RiffleGradient)
				{
					float swing = (float)Math.Sin(6.2831853 * s + phase);
					float depth = Math.Max(settings.MinRiffleDepth, river.Depth[i] * (1f + settings.RiffleAmplitude * swing));
					river.Depth[i] = depth;
					// A riffle spreads wider over its gravel than the pool it drops into.
					river.Width[i] *= 1f - settings.RiffleWidthSwing * swing;
					river.Bed[i] = river.Surface[i] - depth;
					reach = swing < -0.35f ? RiverReach.Riffle : swing > 0.35f ? RiverReach.Pool : RiverReach.Run;
				}
				river.Reach[i] = reach;
			}
			if (river.Perennial)
			{
				Knickpoints(river, settings);
				PlungePools(river, settings);
			}
			river.Grain = new float[n];
			for (int i = 0; i < n; i++)
			{
				river.Grain[i] = river.Perennial ? Competence(river.Depth[i] * (2f / 3f), gradient[i]) : 0f;
			}
		}

		/// <summary>
		/// The largest grain, metres, a flow of mean depth <paramref name="depth"/> on a slope
		/// <paramref name="slope"/> can move: Shields' criterion, the bed shear stress ρgRS against the
		/// critical 0.045 (ρs − ρ)gD for quartz in water, so D = R S / (1.65 × 0.045).
		/// </summary>
		public static float Competence(float depth, float slope) => Math.Max(0f, depth) * Math.Max(0f, slope) / (1.65f * 0.045f);

		/// <summary>Width and depth for a discharge, by hydraulic geometry within the game's clamps.</summary>
		public static void Size(float discharge, RiverSettings settings, out float width, out float depth)
		{
			float q = Math.Max(0f, discharge);
			width = Clamp(settings.WidthCoefficient * (float)Math.Sqrt(q), settings.MinWidth, settings.MaxWidth);
			depth = Clamp(settings.DepthCoefficient * (float)Math.Pow(q, 0.4), settings.MinDepth, settings.MaxDepth);
		}

		/// <summary>
		/// Width and depth for a discharge where the banks are held by <paramref name="cohesion"/> (0 bare …
		/// 1 densely rooted): wider and shallower the looser the banks.
		/// </summary>
		public static void Size(float discharge, float cohesion, RiverSettings settings, out float width, out float depth)
		{
			Size(discharge, settings, out width, out depth);
			// Hydraulic geometry's own constants are for ordinary vegetated banks (cohesion ½).
			float spread = Clamp(1f + settings.LooseBankSpread * (0.5f - Clamp(cohesion, 0f, 1f)), 0.6f, 2.2f);
			width = Clamp(width * spread, settings.MinWidth, settings.MaxWidth * 1.5f);
			depth = Math.Max(settings.MinRiffleDepth, depth / (float)Math.Pow(spread, 0.7));
		}

		/// <summary>A dry wash's width for its drainage area.</summary>
		public static float DryWidth(float areaSquareMetres, RiverSettings settings)
		{
			return Clamp(settings.DryWidthCoefficient * (float)Math.Pow(Math.Max(0f, areaSquareMetres) / 1e6, 0.3), settings.MinWidth, settings.MaxWidth * 0.5f);
		}

		/// <summary>Unit normals to the left of a line's direction, from central differences.</summary>
		public static void Normals(IList<float> x, IList<float> z, out float[] nx, out float[] nz)
		{
			int count = x.Count;
			nx = new float[count];
			nz = new float[count];
			for (int i = 0; i < count; i++)
			{
				int a = Math.Max(0, i - 1), b = Math.Min(count - 1, i + 1);
				float dx = x[b] - x[a], dz = z[b] - z[a];
				float length = (float)Math.Sqrt(dx * dx + dz * dz);
				if (length < 1e-6f)
				{
					nx[i] = i > 0 ? nx[i - 1] : 1f;
					nz[i] = i > 0 ? nz[i - 1] : 0f;
					continue;
				}
				nx[i] = -dz / length;
				nz[i] = dx / length;
			}
		}

		/// <summary>Signed curvature at each point, 1/m, positive turning left.</summary>
		public static float[] Curvature(IList<float> x, IList<float> z)
		{
			int count = x.Count;
			var k = new float[count];
			for (int i = 1; i < count - 1; i++)
			{
				float ax = x[i] - x[i - 1], az = z[i] - z[i - 1];
				float bx = x[i + 1] - x[i], bz = z[i + 1] - z[i];
				float la = (float)Math.Sqrt(ax * ax + az * az), lb = (float)Math.Sqrt(bx * bx + bz * bz);
				if (la < 1e-6f || lb < 1e-6f)
				{
					continue;
				}
				float turn = (float)Math.Atan2(ax * bz - az * bx, ax * bx + az * bz);
				k[i] = turn / (0.5f * (la + lb));
			}
			if (count > 2)
			{
				k[0] = k[1];
				k[count - 1] = k[count - 2];
			}
			return k;
		}

		/// <summary>A moving average over ±<paramref name="radius"/> points, the ends clamped.</summary>
		public static float[] SmoothAlong(float[] values, int radius)
		{
			int count = values.Length;
			if (radius <= 0 || count < 2)
			{
				return (float[])values.Clone();
			}
			var result = new float[count];
			double sum = 0.0;
			for (int k = -radius; k <= radius; k++)
			{
				sum += values[Math.Max(0, Math.Min(count - 1, k))];
			}
			for (int i = 0; i < count; i++)
			{
				result[i] = (float)(sum / (2 * radius + 1));
				sum += values[Math.Min(count - 1, i + radius + 1)] - values[Math.Max(0, i - radius)];
			}
			return result;
		}

		public static float Distance(float ax, float az, float bx, float bz)
		{
			float dx = bx - ax, dz = bz - az;
			return (float)Math.Sqrt(dx * dx + dz * dz);
		}

		private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

		private static float SmoothStep(float t)
		{
			t = Clamp(t, 0f, 1f);
			return t * t * (3f - 2f * t);
		}
	}
}
#endif
