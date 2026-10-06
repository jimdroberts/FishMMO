#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>One lake as a scene has it: the planet's lake's level, and where on the scene's ground it stands.</summary>
	public sealed class SceneLake
	{
		public int Id;
		public int PlanetLake;
		public float Level;
		public float SpillLevel;
		public bool Terminal;
		public bool Frozen;
		public float Outflow;
		/// <summary>Scene positions under its water to flood from.</summary>
		public List<Vector2> Seeds = new List<Vector2>();
		/// <summary>The rectangle it may cover.</summary>
		public Rect Bounds;
		/// <summary>Whether it may cover a sample; null for anywhere in <see cref="Bounds"/>.</summary>
		public Func<float, float, bool> MayCover;
		/// <summary>Samples it covers on the finished ground.</summary>
		public int Covered;
	}

	/// <summary>
	/// A scene's rivers and lakes: the planet's network (<see cref="PlanetDrainage"/>) brought onto the
	/// scene's own ground, laid into it, and asked by everything that dresses the scene afterwards.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>From the planet's line to the scene's river.</b> Each planet river crossing the scene is
	/// carried into scene metres, its 200 m grid steps rounded off (<see cref="RiverShaping.Chaikin"/>),
	/// moved onto the scene's own valley floor, which its finer ground puts tens of metres from the
	/// planet's (<see cref="RiverShaping.SnapToFloor"/>), and swung into meanders where its bed is gentle.
	/// Within <see cref="RiverSettings.EdgeHoldMetres"/> of the scene's edge it is held to the planet's
	/// line, so the neighbour scene cut across the same river meets it where it crosses.
	/// </para>
	/// <para>
	/// <b>Its water</b> is sized by its discharge (<see cref="RiverShaping.Size"/>), its surface never
	/// rises downstream (<see cref="RiverShaping.Surface"/>) and meets whatever it ends in at that
	/// one's level: a lake, the sea, the larger river it joins.
	/// </para>
	/// <para>
	/// <b>Laid twice.</b> Once before erosion (<see cref="Lay"/>), so the scene's gullies grow toward the
	/// rivers and drop their load at the banks, the water standing as their base level; then again on
	/// the eroded ground (<see cref="Finish"/>), past erosion's edge fade, so the channel the water fills
	/// is the channel the ground has, edge to edge.
	/// </para>
	/// </remarks>
	public sealed class SceneWater
	{
		/// <summary>How far past the scene's edge planet rivers are picked up, metres: room to smooth the line the same in neighbouring scenes.</summary>
		public const float HaloMetres = 1500f;
		/// <summary>The coarse grid distances to water are kept on, metres.</summary>
		public const float DistanceCellMetres = 8f;
		/// <summary>Distances to water are kept out to this, metres.</summary>
		public const float DistanceReachMetres = 400f;
		/// <summary>A lake's sill stands this far over its level where it has to be raised.</summary>
		public const float SillMetres = 0.4f;
		public const float MaxSillRaiseMetres = 4f;
		/// <summary>How far round a river's mouth or outlet, past two of its widths, a lake may stand outside its planet cells, metres.</summary>
		public const float MouthClearMetres = 12f;

		public readonly List<RiverPath> Rivers = new List<RiverPath>();
		public readonly List<SceneLake> Lakes = new List<SceneLake>();
		public readonly RiverSettings Settings;
		public float SeaLevel = float.NegativeInfinity;
		/// <summary>What the water stands on, sample by sample; built by <see cref="Lay"/> and <see cref="Finish"/>.</summary>
		public SceneWaterGrid Grid { get; private set; }
		/// <summary>What was found, for the generator's notes.</summary>
		public readonly List<string> Notes = new List<string>();

		private float[] distance;
		private Func<float, float, float> cohesion;

		/// <summary>How high above a terminal lake's level its salt flat reaches, metres.</summary>
		public const float PlayaRiseMetres = 1.2f;

		/// <summary>
		/// Boulders in and beside the rivers, x, y, z and radius in scene metres: placed by
		/// <see cref="RiverBoulders"/>, and what the water's flow will run round.
		/// </summary>
		public readonly List<Vector4> Boulders = new List<Vector4>();
		private int distanceWidth, distanceDepth;
		private float distanceOriginX, distanceOriginZ;

		public SceneWater(RiverSettings settings = null)
		{
			Settings = settings ?? new RiverSettings();
		}

		public bool Any => Rivers.Count > 0 || Lakes.Count > 0;

		// ── Building from the planet ─────────────────────────────────

		/// <summary>
		/// The rivers and lakes of the planet's drainage that cross a scene, shaped to its ground as it
		/// stands now (sampled, plateaus stepped, not yet eroded). Main thread.
		/// </summary>
		/// <param name="bankCohesion">How firmly roots hold the banks at (east, north), 0 bare … 1 rooted: loose banks make wide, shallow rivers. Null for halfway.</param>
		public static SceneWater Build(SceneGenerationRequest request, SolarSystemProfile system, SceneHeightField field, RiverSettings settings = null,
			Func<float, float, float> bankCohesion = null)
		{
			var water = new SceneWater(settings);
			water.cohesion = bankCohesion;
			Stopwatch clock = Stopwatch.StartNew();
			PlanetDrainage drainage = PlanetDrainage.For(request, system);
			if (drainage == null)
			{
				water.Notes.Add("No rivers or lakes: the scene stands on no body, or underground.");
				return water;
			}
			water.SeaLevel = drainage.SeaLevel;
			var frame = new SceneFrame(request);
			float halfW = field.Plan.WidthMetres * 0.5f, halfD = field.Plan.DepthMetres * 0.5f;
			var blurred = new GroundSampler(field, 4);

			// Lakes first: rivers that meet them end at their level.
			var lakeOfPlanet = new Dictionary<int, SceneLake>();
			foreach (DrainageLake lake in drainage.Result.Lakes)
			{
				SceneLake sceneLake = water.LakeFrom(lake, drainage, frame, halfW, halfD);
				if (sceneLake != null)
				{
					sceneLake.Id = water.Lakes.Count;
					water.Lakes.Add(sceneLake);
					lakeOfPlanet[lake.Id] = sceneLake;
				}
			}

			// Rivers biggest first, so a tributary finds the river it joins already laid.
			var riverOfPlanet = new Dictionary<int, RiverPath>();
			foreach (DrainageRiver river in drainage.Result.Rivers)
			{
				foreach (RiverPath path in water.RunsOf(river, drainage, frame, halfW, halfD, field, blurred, lakeOfPlanet, riverOfPlanet))
				{
					path.Id = water.Rivers.Count;
					water.Rivers.Add(path);
					// The last run of a river in the scene is what a tributary joining it meets.
					riverOfPlanet[river.Id] = path;
				}
			}
			int wet = 0;
			foreach (RiverPath path in water.Rivers)
			{
				wet += path.Perennial ? 1 : 0;
			}
			water.Notes.Add($"Water: {wet} river run(s), {water.Rivers.Count - wet} dry wash(es) and {water.Lakes.Count} lake(s) from the planet's " +
				$"{drainage.Result.Rivers.Count:N0} rivers and {drainage.Result.Lakes.Count:N0} lakes ({drainage.Seconds:0.0} s for the planet, {clock.Elapsed.TotalSeconds:0.0} s for the scene).");
			return water;
		}

		private SceneLake LakeFrom(DrainageLake lake, PlanetDrainage drainage, SceneFrame frame, float halfW, float halfD)
		{
			float margin = 400f;
			var seeds = new List<Vector2>();
			float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
			var cells = new HashSet<int>();
			bool any = false;
			foreach (int c in lake.Cells)
			{
				if (drainage.Result.Ground[c] >= lake.Level)
				{
					continue;
				}
				Vector2 p = frame.ToScene(drainage.CentreOf(c));
				if (Mathf.Abs(p.x) > halfW + margin || Mathf.Abs(p.y) > halfD + margin)
				{
					continue;
				}
				any = true;
				cells.Add(c);
				if (Mathf.Abs(p.x) <= halfW && Mathf.Abs(p.y) <= halfD)
				{
					seeds.Add(p);
				}
			}
			if (!any)
			{
				return null;
			}
			// May cover its own cells and a ring round them: the scene's shoreline is the scene's ground's.
			var allowed = new HashSet<int>(cells);
			foreach (int c in cells)
			{
				for (int k = drainage.Grid.NeighbourStart[c]; k < drainage.Grid.NeighbourStart[c + 1]; k++)
				{
					allowed.Add(drainage.Grid.Neighbours[k]);
				}
			}
			foreach (int c in allowed)
			{
				Vector2 p = frame.ToScene(drainage.CentreOf(c));
				minX = Mathf.Min(minX, p.x);
				minZ = Mathf.Min(minZ, p.y);
				maxX = Mathf.Max(maxX, p.x);
				maxZ = Mathf.Max(maxZ, p.y);
			}
			float cell = (float)(drainage.Grid.RadiusMetres * Math.PI * 0.5 / drainage.Grid.N);
			float coldest = float.MaxValue;
			foreach (int c in cells)
			{
				coldest = Mathf.Min(coldest, drainage.Celsius[c]);
			}
			float mean = 0f;
			foreach (int c in cells)
			{
				mean += drainage.Celsius[c];
			}
			mean /= Mathf.Max(1, cells.Count);
			return new SceneLake
			{
				PlanetLake = lake.Id,
				Level = lake.Level,
				SpillLevel = lake.SpillLevel,
				Terminal = lake.Terminal,
				Outflow = lake.Outflow,
				// Frozen through the year where the mean stays below freezing.
				Frozen = mean < -2f,
				Seeds = seeds,
				Bounds = Rect.MinMaxRect(minX - cell, minZ - cell, maxX + cell, maxZ + cell),
				MayCover = (east, north) => allowed.Contains(drainage.CellOf(frame.ToUnit(east, north))),
			};
		}

		/// <summary>The runs of one planet river inside the scene's halo, each shaped and profiled.</summary>
		private IEnumerable<RiverPath> RunsOf(DrainageRiver river, PlanetDrainage drainage, SceneFrame frame, float halfW, float halfD,
			SceneHeightField field, GroundSampler blurred, Dictionary<int, SceneLake> lakes, Dictionary<int, RiverPath> laid)
		{
			int count = river.Cells.Length;
			var px = new float[count];
			var pz = new float[count];
			var inHalo = new bool[count];
			for (int v = 0; v < count; v++)
			{
				Vector2 p = frame.ToScene(drainage.CentreOf(river.Cells[v]));
				px[v] = p.x;
				pz[v] = p.y;
				inHalo[v] = Mathf.Abs(p.x) <= halfW + HaloMetres && Mathf.Abs(p.y) <= halfD + HaloMetres;
			}
			int v0 = 0;
			while (v0 < count)
			{
				if (!inHalo[v0])
				{
					v0++;
					continue;
				}
				int v1 = v0;
				while (v1 + 1 < count && inHalo[v1 + 1])
				{
					v1++;
				}
				// One cell either side beyond the halo, so the run's ends lie outside it and are not mistaken for the river's own.
				int a = Math.Max(0, v0 - 1), b = Math.Min(count - 1, v1 + 1);
				if (b > a && Crosses(px, pz, a, b, halfW, halfD))
				{
					foreach (RiverPath path in Shape(river, drainage, a, b, px, pz, halfW, halfD, field, blurred, lakes, laid))
					{
						yield return path;
					}
				}
				v0 = v1 + 1;
			}
		}

		private static bool Crosses(float[] px, float[] pz, int a, int b, float halfW, float halfD)
		{
			for (int v = a; v <= b; v++)
			{
				if (Mathf.Abs(px[v]) <= halfW && Mathf.Abs(pz[v]) <= halfD)
				{
					return true;
				}
			}
			for (int v = a; v < b; v++)
			{
				// A segment cutting a corner with both ends outside.
				for (int k = 1; k < 8; k++)
				{
					float t = k / 8f;
					if (Mathf.Abs(px[v] + (px[v + 1] - px[v]) * t) <= halfW && Mathf.Abs(pz[v] + (pz[v + 1] - pz[v]) * t) <= halfD)
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>Shapes the planet river's cells a … b into scene river runs: one, or two where it turns from a dry wash to running water.</summary>
		private List<RiverPath> Shape(DrainageRiver river, PlanetDrainage drainage, int a, int b, float[] px, float[] pz, float halfW, float halfD,
			SceneHeightField field, GroundSampler blurred, Dictionary<int, SceneLake> lakes, Dictionary<int, RiverPath> laid)
		{
			var result = new List<RiverPath>();
			RiverSettings s = Settings;
			var x = new List<float>();
			var z = new List<float>();
			var t = new List<float>();
			for (int v = a; v <= b; v++)
			{
				x.Add(px[v]);
				z.Add(pz[v]);
				t.Add(v);
			}
			RiverShaping.Chaikin(x, z, t, 3);
			RiverShaping.Resample(x, z, t, 4f);
			if (x.Count < 3)
			{
				return result;
			}

			float[] q = Along(t, river.Discharge);
			float[] area = Along(t, river.Area);
			float[] filled = new float[t.Count];
			for (int i = 0; i < t.Count; i++)
			{
				int c0 = river.Cells[Mathf.Clamp(Mathf.FloorToInt(t[i]), 0, river.Cells.Length - 1)];
				int c1 = river.Cells[Mathf.Clamp(Mathf.FloorToInt(t[i]) + 1, 0, river.Cells.Length - 1)];
				float f = t[i] - Mathf.Floor(t[i]);
				filled[i] = (float)(drainage.Result.Filled[c0] + (drainage.Result.Filled[c1] - drainage.Result.Filled[c0]) * f);
			}
			float[] width = new float[t.Count];
			for (int i = 0; i < t.Count; i++)
			{
				if (q[i] >= new DrainageSettings().MinRiverDischarge)
				{
					RiverShaping.Size(q[i], s, out width[i], out _);
				}
				else
				{
					width[i] = RiverShaping.DryWidth(area[i], s);
				}
			}
			float[] gradient = Gradient(x, z, filled);
			float[] hold = Hold(x, z, halfW, halfD, s.EdgeHoldMetres);

			RiverShaping.SnapToFloor(x, z, blurred.At, hold, s.SnapMetres, s.SnapPenalty, Mathf.Max(60f, 8f * Median(width)));
			RiverShaping.SnapToFloor(x, z, blurred.At, hold, s.SnapMetres * 0.4f, s.SnapPenalty, Mathf.Max(40f, 5f * Median(width)));
			RiverShaping.Meander(x, z, width, gradient, hold, s, unchecked((uint)(river.Id * 0x9E3779B1u) ^ (uint)(a * 0x85EBCA6Bu)));
			// No loops: the snaps and the meanders can pull the line across itself (see RiverShaping.Untangle).
			RiverShaping.Untangle(x, z, t, width, 4f);

			// The finer spacing the channel is described at, its values carried along.
			float spacing = Mathf.Clamp(0.5f * Median(width), 2f, 6f);
			var tt = new List<float>(t);
			RiverShaping.Resample(x, z, tt, spacing);
			q = Along(tt, river.Discharge);
			area = Along(tt, river.Area);
			int n = x.Count;

			RiverEnd start = a > 0 || !Inside(x[0], z[0], halfW, halfD) ? RiverEnd.Edge : river.Start;
			RiverEnd end = b < river.Cells.Length - 1 || !Inside(x[n - 1], z[n - 1], halfW, halfD) ? RiverEnd.Edge : river.End;
			SceneLake startLake = start == RiverEnd.Lake && river.StartLake >= 0 && lakes.TryGetValue(river.StartLake, out SceneLake sl) ? sl : null;
			SceneLake endLake = end == RiverEnd.Lake && river.EndLake >= 0 && lakes.TryGetValue(river.EndLake, out SceneLake el) ? el : null;
			RiverPath joined = end == RiverEnd.Confluence && river.JoinsRiver >= 0 && laid.TryGetValue(river.JoinsRiver, out RiverPath jr) ? jr : null;

			/* The planet's line stops at the lake's edge. A river entering or leaving a lake runs on into it on its
			 * own heading, so its channel can be cut through the lake's shallow margin; the trim below keeps only
			 * as much of that as reaches lake floor as deep as its bed. */
			if (endLake != null)
			{
				RiverShaping.Size(q[n - 1], 0.5f, s, out float mouthWidth, out _);
				ExtendIntoLake(x, z, ref q, ref area, true, s.MouthReachWidths * mouthWidth, spacing, halfW, halfD);
			}
			if (startLake != null)
			{
				RiverShaping.Size(q[0], 0.5f, s, out float outletWidth, out _);
				ExtendIntoLake(x, z, ref q, ref area, false, s.MouthReachWidths * outletWidth, spacing, halfW, halfD);
			}
			n = x.Count;

			// A tributary ends on the river it joins, wherever that river was moved to.
			if (joined != null)
			{
				Nearest(joined, x[n - 1], z[n - 1], out float jx, out float jz, out _);
				float shiftX = jx - x[n - 1], shiftZ = jz - z[n - 1];
				float along = 0f;
				for (int i = n - 1; i >= 0; i--)
				{
					if (i < n - 1)
					{
						along += RiverShaping.Distance(x[i], z[i], x[i + 1], z[i + 1]);
					}
					float w = Mathf.Clamp01(1f - along / 150f);
					if (w <= 0f)
					{
						break;
					}
					x[i] += shiftX * w;
					z[i] += shiftZ * w;
				}
			}

			// Each point's floor: the lowest ground across where its channel will be.
			float threshold = new DrainageSettings().MinRiverDischarge;
			var widthAt = new float[n];
			var depthAt = new float[n];
			var wetAt = new bool[n];
			for (int i = 0; i < n; i++)
			{
				wetAt[i] = q[i] >= threshold;
				if (wetAt[i])
				{
					RiverShaping.Size(q[i], cohesion != null ? cohesion(x[i], z[i]) : 0.5f, s, out widthAt[i], out depthAt[i]);
				}
				else
				{
					widthAt[i] = RiverShaping.DryWidth(area[i], s);
					depthAt[i] = s.DryDepth;
				}
			}
			widthAt = RiverShaping.SmoothAlong(widthAt, 4);
			depthAt = RiverShaping.SmoothAlong(depthAt, 4);
			RiverShaping.Normals(x, z, out float[] nx, out float[] nz);
			var floor = new float[n];
			var crest = new float[n];
			var bank = new float[n];
			for (int i = 0; i < n; i++)
			{
				float half = 0.5f * widthAt[i];
				float lowest = float.MaxValue, highest = float.MinValue;
				for (int k = -2; k <= 2; k++)
				{
					float o = half * k / 2f;
					float ground = field.MetresAt(x[i] + nx[i] * o, z[i] + nz[i] * o);
					lowest = Mathf.Min(lowest, ground);
					highest = Mathf.Max(highest, ground);
				}
				floor[i] = lowest;
				// The highest ground across where its channel will be: what a mouth cut through a lake's rim must clear.
				crest[i] = highest;
				/* The lower of its two banks, where the water meets them: at the channel's edge, and a little way out,
				 * whichever is lower. Read only a way out (as it was, 3 m and more past the edge), a river in a V or a
				 * gorge took its banks from up the valley's sides or its walls and stood its surface there, over the
				 * ground at its own edge: water hanging in the air, metres above its banks (Jim, 2026-10-06: "the
				 * pools are floating"; Flo Monolith had a quarter of a river's length over air, up to 13.6 m). */
				float reach = half + Mathf.Max(3f, half);
				float edge = half + 0.25f;
				float far = Mathf.Min(field.MetresAt(x[i] + nx[i] * reach, z[i] + nz[i] * reach), field.MetresAt(x[i] - nx[i] * reach, z[i] - nz[i] * reach));
				float near = Mathf.Min(field.MetresAt(x[i] + nx[i] * edge, z[i] + nz[i] * edge), field.MetresAt(x[i] - nx[i] * edge, z[i] - nz[i] * edge));
				bank[i] = Mathf.Min(far, near);
			}
			int smoothing = Mathf.Max(1, Mathf.RoundToInt(1.5f * Median(widthAt) / spacing));
			floor = RiverShaping.SmoothAlong(floor, smoothing);
			// Smoothed, but never above the ground itself: an average lifts the banks over a dip and the water with them.
			var rawBank = bank;
			bank = RiverShaping.SmoothAlong(bank, smoothing);
			for (int i = 0; i < n; i++)
			{
				bank[i] = Mathf.Min(bank[i], rawBank[i]);
			}

			float MetresBetween(int from, int to)
			{
				float metres = 0f;
				for (int k = Math.Min(from, to); k < Math.Max(from, to); k++)
				{
					metres += RiverShaping.Distance(x[k], z[k], x[k + 1], z[k + 1]);
				}
				return metres;
			}
			/* Trimmed where it runs into standing water: the sea, its end lake, or out of its start lake. Not at
			 * the shoreline: a river's channel runs on through a lake's shallow margin and rim until no ground across
			 * it stands above its own bed (at most MouthReachWidths widths in), so its mouth or outlet is a channel
			 * cut through the shore, not a stream stopping behind a lip. */
			int first = 0, last = n - 1;
			int shoreFirst = first, shoreLast = last;
			if (startLake != null)
			{
				int shore = first;
				while (shore < last - 2 && floor[shore] < startLake.Level - 0.3f)
				{
					shore++;
				}
				first = shoreFirst = shore;
				float reach = s.MouthReachWidths * widthAt[shore];
				while (first > 0 && crest[first - 1] >= startLake.Level - depthAt[first - 1] - 0.3f && MetresBetween(first - 1, shore) <= reach)
				{
					first--;
				}
			}
			// The sea: its low-water line, so the river's channel and water cross the shore the tide bares.
			float endLevel = end == RiverEnd.Sea ? SeaLevel - s.IntertidalMetres : endLake != null ? endLake.Level : float.NegativeInfinity;
			if (!float.IsNegativeInfinity(endLevel))
			{
				for (int i = first + 2; i <= last; i++)
				{
					if (floor[i] < endLevel - 0.3f)
					{
						last = i;
						break;
					}
				}
				shoreLast = last;
				if (endLake != null)
				{
					int shore = last;
					float reach = s.MouthReachWidths * widthAt[shore];
					while (last < n - 1 && crest[last + 1] >= endLevel - depthAt[last + 1] - 0.3f && MetresBetween(shore, last + 1) <= reach)
					{
						last++;
					}
				}
			}
			/* Only what lies inside the scene, and a point past its edge to meet the backdrop's river there
			 * (BackdropWater). The line runs on past the edge for the edge hold and the margin's ground, but drawn
			 * there it lay over the backdrop's coarser ground, buried in places, and its boulders took their
			 * height from the edge tile and stood in the air. */
			if (start == RiverEnd.Edge)
			{
				int inside = first;
				while (inside <= last && !Inside(x[inside], z[inside], halfW, halfD))
				{
					inside++;
				}
				first = shoreFirst = Math.Max(first, inside - 1);
			}
			if (end == RiverEnd.Edge)
			{
				for (int i = first + 1; i <= last; i++)
				{
					if (Inside(x[i - 1], z[i - 1], halfW, halfD) && !Inside(x[i], z[i], halfW, halfD))
					{
						last = shoreLast = i;
						break;
					}
				}
			}
			if (last - first < 2 || shoreLast - shoreFirst < 2)
			{
				return result;
			}

			var stepMetres = new float[n];
			for (int i = 1; i < n; i++)
			{
				stepMetres[i] = RiverShaping.Distance(x[i - 1], z[i - 1], x[i], z[i]);
			}
			float startCeiling = startLake != null ? startLake.Level : float.PositiveInfinity;
			float endFloor = endLevel;
			if (joined != null)
			{
				Nearest(joined, x[last], z[last], out _, out _, out int at);
				endFloor = joined.Surface[at];
			}
			/* Settles onto the level of what it runs into over its last few widths: no step where it meets it.
			 * Worked out from shore to shore, so the low floor of a lake's margin does not hold the river down;
			 * the stretch cut through a lake's margin stands at the lake's level. */
			float[] shoreSurface = RiverShaping.Surface(Slice(floor, shoreFirst, shoreLast), Slice(bank, shoreFirst, shoreLast), Slice(stepMetres, shoreFirst, shoreLast),
				startCeiling, endFloor, s, Mathf.Max(15f, 3f * Median(widthAt)));
			var surface = new float[last - first + 1];
			for (int i = first; i <= last; i++)
			{
				surface[i - first] = i < shoreFirst ? startLake.Level : i > shoreLast ? endLake.Level : shoreSurface[i - shoreFirst];
			}

			// Split where it turns from a dry wash into running water: the wash ends where the river begins.
			int wetFrom = first;
			while (wetFrom <= last && !wetAt[wetFrom])
			{
				wetFrom++;
			}
			if (wetFrom > first + 2 && wetFrom < last - 2)
			{
				RiverPath dry = Path(river, x, z, q, widthAt, depthAt, surface, first, first, wetFrom, false);
				RiverPath wet = Path(river, x, z, q, widthAt, depthAt, surface, first, wetFrom, last, true);
				dry.Start = start;
				dry.End = RiverEnd.Confluence;
				wet.Start = RiverEnd.Source;
				wet.End = end;
				Link(wet, startLake: null, endLake, joined);
				Flare(wet, joined, LakeFloorPast(field, wet, endLake));
				RunIntoLake(wet);
				dry.StartLake = startLake != null ? startLake.Id : -1;
				result.Add(dry);
				result.Add(wet);
				// The wash is joined to the river next added, which follows it in the list.
				dry.JoinsRiver = -2;
			}
			else
			{
				bool perennial = wetFrom <= first + 2;
				RiverPath path = Path(river, x, z, q, widthAt, depthAt, surface, first, first, last, perennial);
				path.Start = start;
				path.End = end;
				Link(path, startLake, endLake, joined);
				Flare(path, joined, LakeFloorPast(field, path, endLake));
				RunIntoLake(path);
				result.Add(path);
			}
			return result;
		}

		private static void Link(RiverPath path, SceneLake startLake, SceneLake endLake, RiverPath joined)
		{
			path.StartLake = startLake != null ? startLake.Id : -1;
			path.EndLake = endLake != null ? endLake.Id : -1;
			path.JoinsRiver = joined != null ? joined.Id : -1;
		}

		private static RiverPath Path(DrainageRiver river, List<float> x, List<float> z, float[] q, float[] width, float[] depth, float[] surface,
			int surfaceFirst, int from, int to, bool perennial)
		{
			int n = to - from + 1;
			var path = new RiverPath
			{
				PlanetRiver = river.Id,
				Perennial = perennial,
				X = new float[n],
				Z = new float[n],
				S = new float[n],
				Discharge = new float[n],
				Width = new float[n],
				Depth = new float[n],
				Surface = new float[n],
				Bed = new float[n],
			};
			for (int k = 0; k < n; k++)
			{
				int i = from + k;
				path.X[k] = x[i];
				path.Z[k] = z[i];
				path.S[k] = k == 0 ? 0f : path.S[k - 1] + RiverShaping.Distance(x[i - 1], z[i - 1], x[i], z[i]);
				path.Discharge[k] = q[i];
				path.Width[k] = width[i];
				path.Depth[k] = depth[i];
				path.Surface[k] = surface[i - surfaceFirst];
				path.Bed[k] = path.Surface[k] - depth[i];
			}
			path.Curvature = RiverShaping.SmoothAlong(RiverShaping.Curvature(path.X, path.Z), 3);
			RiverShaping.Reaches(path, new RiverSettings(), unchecked((uint)(river.Id * 0x27D4EB2Du) ^ (uint)(from * 0x165667B1u)));
			return path;
		}

		/// <summary>
		/// Opens a river's mouth where it runs into another river or a lake: its last few widths flare wider,
		/// as real mouths do, so its channel merges into the water it meets instead of meeting it corner to
		/// corner across a bank. A tributary's bed also falls to the bed of the river it joins (Playfair's
		/// accordant junction): it does not hang above it as a step, which the water shows as a hard line where
		/// its shallows meet the deeper channel.
		/// </summary>
		private static void Flare(RiverPath path, RiverPath joined, float lakeFloor = float.NaN)
		{
			int last = path.Count - 1;
			if (last < 2)
			{
				return;
			}
			// A lake's outlet: the lake narrowing into the river, a funnel over its first widths.
			if (path.Start == RiverEnd.Lake)
			{
				float into = 3f * path.Width[0];
				for (int i = 0; i <= last && path.S[i] <= into; i++)
				{
					float t = 1f - path.S[i] / into;
					path.Width[i] *= 1f + 1f * t * t;
				}
			}
			/* An estuary: the river opening out toward the sea over its lower course, as the tide working in and
			 * out of it keeps it — a funnel three times the river's width at the low-water line, so the coast
			 * has an inlet where it comes in, and the sea floods up it at high water. */
			if (path.End == RiverEnd.Sea)
			{
				float estuary = Mathf.Max(EstuaryMinMetres, EstuaryWidths * path.Width[last]);
				for (int i = last; i >= 0; i--)
				{
					float fromEnd = path.S[last] - path.S[i];
					if (fromEnd > estuary)
					{
						break;
					}
					float t = 1f - fromEnd / estuary;
					path.Width[i] *= 1f + 2f * t * t;
				}
				return;
			}
			if (path.End != RiverEnd.Confluence && path.End != RiverEnd.Lake)
			{
				return;
			}
			/* Into a lake: a drowned mouth, longer and wider than a tributary's, its bed falling toward the lake
			 * floor past it, so the lake's water reaches up into the river as a small bay rather than meeting a
			 * thin stream across a beach. */
			bool lake = path.End == RiverEnd.Lake && !float.IsNaN(lakeFloor);
			float reach = lake ? Mathf.Max(3f * path.Width[last], 20f) : 3f * path.Width[last];
			float mouthDepth = lake ? Mathf.Clamp(path.Surface[last] - lakeFloor, path.Depth[last], Mathf.Max(1.5f, 4f * path.Depth[last])) : 0f;
			for (int i = last; i >= 0; i--)
			{
				float fromEnd = path.S[last] - path.S[i];
				if (fromEnd > reach)
				{
					break;
				}
				float t = 1f - fromEnd / reach;
				path.Width[i] *= 1f + (lake ? 1.6f : 1.2f) * t * t;
				if (lake)
				{
					path.Depth[i] = Mathf.Lerp(path.Depth[i], Mathf.Max(path.Depth[i], mouthDepth), t * t * (3f - 2f * t));
					path.Bed[i] = path.Surface[i] - path.Depth[i];
				}
				if (joined != null)
				{
					Nearest(joined, path.X[i], path.Z[i], out _, out _, out int at);
					float target = Mathf.Max(path.Depth[i], path.Surface[i] - joined.Bed[at]);
					path.Depth[i] = Mathf.Lerp(path.Depth[i], target, t * t * (3f - 2f * t));
					path.Bed[i] = path.Surface[i] - path.Depth[i];
				}
			}
		}

		/// <summary>
		/// Runs a river ending in a lake on into it for two widths (at least 10 m), at the lake's level and its
		/// mouth's bed. Erosion, which runs after the rivers are laid, drops its load where the water slows into
		/// the lake and builds a lip across the mouth; carving only ever lowers the ground, so this stretch cuts
		/// through whatever lip formed when the rivers are laid again on the eroded ground, and leaves the deeper
		/// lake floor beyond as it is.
		/// </summary>
		private static void RunIntoLake(RiverPath path)
		{
			int n = path.Count;
			if (path.End != RiverEnd.Lake || n < 4)
			{
				return;
			}
			int last = n - 1;
			float hx = path.X[last] - path.X[last - 3], hz = path.Z[last] - path.Z[last - 3];
			float length = Mathf.Sqrt(hx * hx + hz * hz);
			if (length < 1e-3f)
			{
				return;
			}
			hx /= length;
			hz /= length;
			float metres = Mathf.Max(10f, 2f * path.Width[last]);
			float step = Mathf.Max(1f, length / 3f);
			int added = Mathf.Max(1, Mathf.CeilToInt(metres / step));
			float[] Grow(float[] values, Func<int, float> at)
			{
				if (values == null)
				{
					return null;
				}
				var grown = new float[n + added];
				Array.Copy(values, grown, n);
				for (int k = 1; k <= added; k++)
				{
					grown[last + k] = at(k);
				}
				return grown;
			}
			path.X = Grow(path.X, k => path.X[last] + hx * step * k);
			path.Z = Grow(path.Z, k => path.Z[last] + hz * step * k);
			path.S = Grow(path.S, k => path.S[last] + step * k);
			path.Discharge = Grow(path.Discharge, _ => path.Discharge[last]);
			path.Width = Grow(path.Width, _ => path.Width[last]);
			path.Depth = Grow(path.Depth, _ => path.Depth[last]);
			path.Surface = Grow(path.Surface, _ => path.Surface[last]);
			path.Bed = Grow(path.Bed, _ => path.Bed[last]);
			path.Curvature = Grow(path.Curvature, _ => 0f);
			path.Grain = Grow(path.Grain, _ => path.Grain[last]);
			if (path.Reach != null)
			{
				var reach = new RiverReach[n + added];
				Array.Copy(path.Reach, reach, n);
				for (int k = 1; k <= added; k++)
				{
					reach[last + k] = path.Reach[last];
				}
				path.Reach = reach;
			}
		}

		/// <summary>
		/// The lake floor just past a river's mouth: the lowest ground one to three widths on along its heading,
		/// NaN when it does not end in <paramref name="lake"/>.
		/// </summary>
		private static float LakeFloorPast(SceneHeightField field, RiverPath path, SceneLake lake)
		{
			int last = path.Count - 1;
			if (lake == null || path.End != RiverEnd.Lake || last < 3)
			{
				return float.NaN;
			}
			float hx = path.X[last] - path.X[last - 3], hz = path.Z[last] - path.Z[last - 3];
			float length = Mathf.Sqrt(hx * hx + hz * hz);
			if (length < 1e-3f)
			{
				return float.NaN;
			}
			hx /= length;
			hz /= length;
			float width = path.Width[last], lowest = float.PositiveInfinity;
			for (float d = width; d <= 3f * width; d += Mathf.Max(1f, 0.5f * width))
			{
				lowest = Mathf.Min(lowest, field.MetresAt(path.X[last] + hx * d, path.Z[last] + hz * d));
			}
			return float.IsPositiveInfinity(lowest) ? float.NaN : Mathf.Min(lowest, lake.Level);
		}

		// ── Laying it into the ground ────────────────────────────────

		/// <summary>
		/// Carves the rivers and floods the lakes into the ground, and returns the water's surface per
		/// sample (negative infinity on dry ground): what erosion takes as its base level.
		/// </summary>
		public float[] Lay(SceneHeightField field)
		{
			LayInto(field, true);
			return Grid.Level;
		}

		/// <summary>Lays it again on the finished (eroded) ground, and works out how far every point is from water.</summary>
		public void Finish(SceneHeightField field)
		{
			LayInto(field, true);
			BuildDistance();
		}

		/// <summary>Marks where the water stands on ground that is not to be changed: a repaint of a sculpted scene.</summary>
		public void Mark(SceneHeightField field)
		{
			LayInto(field, false);
			BuildDistance();
		}

		private void LayInto(SceneHeightField field, bool write)
		{
			Grid = new SceneWaterGrid(field.Width, field.Depth, field.Spacing, field.EastOf(0), field.NorthOf(0));
			// Fix the river ids the dry washes join, now the list is final.
			for (int r = 0; r < Rivers.Count; r++)
			{
				if (Rivers[r].JoinsRiver == -2)
				{
					Rivers[r].JoinsRiver = r + 1 < Rivers.Count ? r + 1 : -1;
				}
			}
			WaterCarver.CarveRivers(field.Metres, Grid, Rivers, Settings, write, SeaLevel + Settings.IntertidalMetres);
			foreach (SceneLake lake in Lakes)
			{
				var seeds = new List<int>();
				foreach (Vector2 seed in lake.Seeds)
				{
					int i = Grid.SampleAt(seed.x, seed.y);
					if (i >= 0)
					{
						seeds.Add(i);
					}
				}
				SceneWaterGrid grid = Grid;
				Rect bounds = lake.Bounds;
				Func<float, float, bool> may = lake.MayCover;
				/* Round the mouth of each river entering or leaving it, the lake may stand on ground outside its
				 * planet cells: that ground is where the river comes in, and sealing it (the sill that keeps a lake
				 * from leaking over low ground past its footprint) dammed every mouth shut. */
				var mouths = new List<Vector3>();
				foreach (RiverPath river in Rivers)
				{
					if (river.Count < 2)
					{
						continue;
					}
					if (river.End == RiverEnd.Lake && river.EndLake == lake.Id)
					{
						int last = river.Count - 1;
						mouths.Add(new Vector3(river.X[last], river.Z[last], 2f * river.Width[last] + MouthClearMetres));
					}
					if (river.Start == RiverEnd.Lake && river.StartLake == lake.Id)
					{
						mouths.Add(new Vector3(river.X[0], river.Z[0], 2f * river.Width[0] + MouthClearMetres));
					}
				}
				lake.Covered = WaterCarver.FloodLake(field.Metres, Grid, lake.Level, seeds, i =>
				{
					float east = grid.EastOf(i % grid.Width), north = grid.NorthOf(i / grid.Width);
					if (!bounds.Contains(new Vector2(east, north)))
					{
						return false;
					}
					if (may == null || may(east, north))
					{
						return true;
					}
					foreach (Vector3 mouth in mouths)
					{
						float dx = east - mouth.x, dz = north - mouth.y;
						if (dx * dx + dz * dz <= mouth.z * mouth.z)
						{
							return true;
						}
					}
					return false;
				}, lake.Id, SillMetres, MaxSillRaiseMetres, write);
				if (lake.Terminal && !lake.Frozen)
				{
					// The salt flat round a lake that only ever evaporates.
					WaterCarver.MarkPlaya(field.Metres, Grid, lake.Id, lake.Level, PlayaRiseMetres, i =>
					{
						float east = grid.EastOf(i % grid.Width), north = grid.NorthOf(i / grid.Width);
						return bounds.Contains(new Vector2(east, north)) && (may == null || may(east, north));
					});
				}
			}
		}

		// ── Asking it ────────────────────────────────────────────────

		/// <summary>The water's surface over a scene position, scene metres; negative infinity on dry ground.</summary>
		public float SurfaceAt(float east, float north) => Grid != null ? Grid.SurfaceNear(east, north) : float.NegativeInfinity;

		/// <summary>What stands at a scene position.</summary>
		public WaterKind KindAt(float east, float north) => Grid != null ? Grid.KindAt(east, north) : WaterKind.None;

		/// <summary>
		/// How much of a scene position is a river's bar, 0 … 1 over a round kernel about it, and how
		/// sandy (1) or gravelly (0) the bar there is: what the sediment paint reads.
		/// </summary>
		public Vector2 BarAt(float east, float north)
		{
			if (Grid == null)
			{
				return Vector2.zero;
			}
			/* Organic, not stair-stepped: the grid marks bars cell by cell (4 m), and read straight its cells painted as
			 * blocks. Even read bilinearly, a cell-by-cell field's edge follows the cells and shows them as steps with
			 * round corners. So it is read through a warp (a broad one that bends the edge, a fine one that roughens it),
			 * smoothed over a round kernel a cell and a half across (rings at 0.7 and 1.4 cells), and eased in: a bar's
			 * edge curves as one laid by water does, and where it lies is unchanged. */
			float warp = Grid.Spacing * 0.6f, roughen = Grid.Spacing * 0.25f;
			float wx = east + warp * (WarpNoise(east * 0.07f, north * 0.07f) * 2f - 1f)
				+ roughen * (WarpNoise(east * 0.27f + 5.1f, north * 0.27f + 9.7f) * 2f - 1f);
			float wz = north + warp * (WarpNoise(east * 0.07f + 31.7f, north * 0.07f + 17.3f) * 2f - 1f)
				+ roughen * (WarpNoise(east * 0.27f + 47.3f, north * 0.27f + 2.9f) * 2f - 1f);
			Vector2 sum = BarSample(wx, wz);
			float weights = 1f;
			for (int k = 0; k < 6; k++)
			{
				float inner = k * (Mathf.PI / 3f), outer = inner + Mathf.PI / 6f;
				float ri = Grid.Spacing * 0.7f, ro = Grid.Spacing * 1.4f;
				sum += 0.8f * BarSample(wx + ri * Mathf.Cos(inner), wz + ri * Mathf.Sin(inner));
				sum += 0.45f * BarSample(wx + ro * Mathf.Cos(outer), wz + ro * Mathf.Sin(outer));
				weights += 0.8f + 0.45f;
			}
			float share = sum.x / weights;
			float sand = sum.x > 1e-4f ? sum.y / sum.x : 0f;
			float eased = share <= 0f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((share - 0.15f) / 0.6f));
			return new Vector2(eased, sand);
		}

		/// <summary>Smooth value noise, 0 … 1, for warping where the bars are read.</summary>
		private static float WarpNoise(float x, float z)
		{
			int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
			float fx = x - ix, fz = z - iz;
			fx = fx * fx * (3f - 2f * fx);
			fz = fz * fz * (3f - 2f * fz);
			float Hash(int a, int b)
			{
				uint h = (uint)(a * 374761393 + b * 668265263);
				h = (h ^ (h >> 13)) * 1274126177u;
				return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
			}
			float top = Mathf.Lerp(Hash(ix, iz), Hash(ix + 1, iz), fx);
			float bottom = Mathf.Lerp(Hash(ix, iz + 1), Hash(ix + 1, iz + 1), fx);
			return Mathf.Lerp(top, bottom, fz);
		}

		/// <summary>The bar share (and share × sand) round one point, bilinear over the four samples round it.</summary>
		private Vector2 BarSample(float east, float north)
		{
			float gx = (east - Grid.OriginX) / Grid.Spacing, gz = (north - Grid.OriginZ) / Grid.Spacing;
			int x0 = Mathf.FloorToInt(gx), z0 = Mathf.FloorToInt(gz);
			float fx = gx - x0, fz = gz - z0, share = 0f, sand = 0f;
			for (int dz = 0; dz <= 1; dz++)
			{
				for (int dx = 0; dx <= 1; dx++)
				{
					int x = x0 + dx, z = z0 + dz;
					if (x < 0 || z < 0 || x >= Grid.Width || z >= Grid.Depth)
					{
						continue;
					}
					int i = z * Grid.Width + x;
					if (Grid.Kind[i] != WaterKind.Bar)
					{
						continue;
					}
					float w = (dx == 0 ? 1f - fx : fx) * (dz == 0 ? 1f - fz : fz);
					share += w;
					sand += w * Grid.Sand[i];
				}
			}
			// The share, and the share times its sand, so samples average properly.
			return new Vector2(share, sand);
		}

		/// <summary>The surface of the water beside a bar or a salt flat at a scene position; negative infinity elsewhere.</summary>
		public float ShoreAt(float east, float north)
		{
			int i = Grid != null ? Grid.SampleAt(east, north) : -1;
			return i >= 0 ? Grid.Shore[i] : float.NegativeInfinity;
		}

		/// <summary>How far up from its mouth a river reaching the sea is an estuary: this many widths, and at least <see cref="EstuaryMinMetres"/>.</summary>
		public const float EstuaryWidths = 6f;
		public const float EstuaryMinMetres = 150f;

		/// <summary>True where a scene position is in the channel of a river near where it meets the sea: tidal, brackish water.</summary>
		public bool IsEstuary(float east, float north)
		{
			if (Grid == null)
			{
				return false;
			}
			int i = Grid.SampleAt(east, north);
			if (i < 0 || Grid.Kind[i] != WaterKind.River || Grid.Body[i] < 0 || Grid.Body[i] >= Rivers.Count)
			{
				return false;
			}
			RiverPath river = Rivers[Grid.Body[i]];
			if (river.End != RiverEnd.Sea || river.Count < 2)
			{
				return false;
			}
			int last = river.Count - 1;
			float reach = Mathf.Max(EstuaryMinMetres, EstuaryWidths * river.Width[last]);
			return RiverShaping.Distance(east, north, river.X[last], river.Z[last]) <= reach;
		}

		/// <summary>Metres from a scene position to the nearest river or lake, out to <see cref="DistanceReachMetres"/>.</summary>
		public float DistanceAt(float east, float north)
		{
			if (distance == null)
			{
				return DistanceReachMetres;
			}
			float gx = (east - distanceOriginX) / DistanceCellMetres, gz = (north - distanceOriginZ) / DistanceCellMetres;
			int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, distanceWidth - 2), z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, distanceDepth - 2);
			float fx = Mathf.Clamp01(gx - x0), fz = Mathf.Clamp01(gz - z0);
			int i = z0 * distanceWidth + x0;
			float south = Mathf.Lerp(distance[i], distance[i + 1], fx);
			float northRow = Mathf.Lerp(distance[i + distanceWidth], distance[i + distanceWidth + 1], fx);
			return Mathf.Lerp(south, northRow, fz);
		}

		/// <summary>Chamfer distance on a coarse grid from every cell holding running or standing water.</summary>
		private void BuildDistance()
		{
			int step = Mathf.Max(1, Mathf.RoundToInt(DistanceCellMetres / Grid.Spacing));
			distanceWidth = (Grid.Width - 1) / step + 2;
			distanceDepth = (Grid.Depth - 1) / step + 2;
			distanceOriginX = Grid.OriginX;
			distanceOriginZ = Grid.OriginZ;
			float cell = step * Grid.Spacing;
			distance = new float[distanceWidth * distanceDepth];
			for (int i = 0; i < distance.Length; i++)
			{
				distance[i] = DistanceReachMetres;
			}
			for (int z = 0; z < Grid.Depth; z++)
			{
				for (int x = 0; x < Grid.Width; x++)
				{
					WaterKind kind = Grid.Kind[z * Grid.Width + x];
					if (kind == WaterKind.River || kind == WaterKind.Lake)
					{
						distance[((z + step / 2) / step) * distanceWidth + (x + step / 2) / step] = 0f;
					}
				}
			}
			float straight = cell, diagonal = cell * 1.41421356f;
			for (int z = 0; z < distanceDepth; z++)
			{
				for (int x = 0; x < distanceWidth; x++)
				{
					int i = z * distanceWidth + x;
					float d = distance[i];
					if (x > 0) d = Mathf.Min(d, distance[i - 1] + straight);
					if (z > 0) d = Mathf.Min(d, distance[i - distanceWidth] + straight);
					if (x > 0 && z > 0) d = Mathf.Min(d, distance[i - distanceWidth - 1] + diagonal);
					if (x < distanceWidth - 1 && z > 0) d = Mathf.Min(d, distance[i - distanceWidth + 1] + diagonal);
					distance[i] = d;
				}
			}
			for (int z = distanceDepth - 1; z >= 0; z--)
			{
				for (int x = distanceWidth - 1; x >= 0; x--)
				{
					int i = z * distanceWidth + x;
					float d = distance[i];
					if (x < distanceWidth - 1) d = Mathf.Min(d, distance[i + 1] + straight);
					if (z < distanceDepth - 1) d = Mathf.Min(d, distance[i + distanceWidth] + straight);
					if (x < distanceWidth - 1 && z < distanceDepth - 1) d = Mathf.Min(d, distance[i + distanceWidth + 1] + diagonal);
					if (x > 0 && z < distanceDepth - 1) d = Mathf.Min(d, distance[i + distanceWidth - 1] + diagonal);
					distance[i] = Mathf.Min(d, DistanceReachMetres);
				}
			}
		}

		// ── The asset ────────────────────────────────────────────────

		/// <summary>Writes the rivers and lakes to the scene's hydrology asset, replacing what it held.</summary>
		public void WriteTo(SceneHydrology asset)
		{
			asset.Rivers.Clear();
			asset.Lakes.Clear();
			asset.SeaLevel = SeaLevel;
			asset.Boulders = new List<Vector4>(Boulders);
			foreach (RiverPath path in Rivers)
			{
				var river = new SceneHydrology.River
				{
					Id = path.Id,
					PlanetRiver = path.PlanetRiver,
					Perennial = path.Perennial,
					Start = (SceneHydrology.End)path.Start,
					Finish = (SceneHydrology.End)path.End,
					StartLake = path.StartLake,
					EndLake = path.EndLake,
					JoinsRiver = path.JoinsRiver,
					Points = new Vector3[path.Count],
					Bed = (float[])path.Bed.Clone(),
					Width = (float[])path.Width.Clone(),
					Depth = (float[])path.Depth.Clone(),
					Discharge = (float[])path.Discharge.Clone(),
					Speed = new float[path.Count],
					Reach = new byte[path.Count],
				};
				for (int i = 0; i < path.Count; i++)
				{
					river.Points[i] = new Vector3(path.X[i], path.Surface[i], path.Z[i]);
					river.Speed[i] = path.SpeedAt(i);
					river.Reach[i] = path.Reach != null ? (byte)path.Reach[i] : (byte)0;
				}
				asset.Rivers.Add(river);
			}
			foreach (SceneLake lake in Lakes)
			{
				var entry = new SceneHydrology.Lake
				{
					Id = lake.Id,
					PlanetLake = lake.PlanetLake,
					Level = lake.Level,
					SpillLevel = lake.SpillLevel,
					Terminal = lake.Terminal,
					Frozen = lake.Frozen,
					Outflow = lake.Outflow,
					Seeds = lake.Seeds.ToArray(),
					Bounds = lake.Bounds,
				};
				WriteMask(entry, lake.Id);
				asset.Lakes.Add(entry);
			}
		}

		/// <summary>The cell size lake masks are kept at, metres: the water's own surface, drawn and queried at runtime.</summary>
		public const float LakeMaskCell = 4f;

		/// <summary>
		/// Where a lake's water stands, from the grid it was flooded on, as one bit per <see cref="LakeMaskCell"/>
		/// cell: set where any sample in the cell is under it. Empty when there is no grid to read.
		/// </summary>
		private void WriteMask(SceneHydrology.Lake entry, int lakeId)
		{
			if (Grid == null)
			{
				return;
			}
			int minX = int.MaxValue, minZ = int.MaxValue, maxX = -1, maxZ = -1;
			for (int z = 0; z < Grid.Depth; z++)
			{
				for (int x = 0; x < Grid.Width; x++)
				{
					int i = z * Grid.Width + x;
					if (Grid.Kind[i] == WaterKind.Lake && Grid.Body[i] == lakeId)
					{
						minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
						minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
					}
				}
			}
			if (maxX < 0)
			{
				entry.Mask = Array.Empty<byte>();
				entry.MaskWidth = entry.MaskHeight = 0;
				return;
			}
			float cell = LakeMaskCell;
			var origin = new Vector2(Grid.EastOf(minX) - cell, Grid.NorthOf(minZ) - cell);
			int width = Mathf.CeilToInt((Grid.EastOf(maxX) - origin.x) / cell) + 2;
			int height = Mathf.CeilToInt((Grid.NorthOf(maxZ) - origin.y) / cell) + 2;
			var bits = new byte[(width * height + 7) / 8];
			for (int z = minZ; z <= maxZ; z++)
			{
				for (int x = minX; x <= maxX; x++)
				{
					int i = z * Grid.Width + x;
					if (Grid.Kind[i] != WaterKind.Lake || Grid.Body[i] != lakeId)
					{
						continue;
					}
					int cx = Mathf.FloorToInt((Grid.EastOf(x) - origin.x) / cell), cz = Mathf.FloorToInt((Grid.NorthOf(z) - origin.y) / cell);
					int bit = cz * width + cx;
					bits[bit >> 3] |= (byte)(1 << (bit & 7));
				}
			}
			entry.Mask = bits;
			entry.MaskOrigin = origin;
			entry.MaskCell = cell;
			entry.MaskWidth = width;
			entry.MaskHeight = height;
		}

		/// <summary>The water an existing scene's asset describes, ready to <see cref="Mark"/> on its ground.</summary>
		public static SceneWater FromAsset(SceneHydrology asset, RiverSettings settings = null)
		{
			var water = new SceneWater(settings);
			if (asset == null)
			{
				return water;
			}
			water.SeaLevel = asset.SeaLevel;
			if (asset.Boulders != null)
			{
				water.Boulders.AddRange(asset.Boulders);
			}
			foreach (SceneHydrology.River river in asset.Rivers)
			{
				int n = river.Points != null ? river.Points.Length : 0;
				if (n < 2)
				{
					continue;
				}
				var path = new RiverPath
				{
					Id = river.Id,
					PlanetRiver = river.PlanetRiver,
					Perennial = river.Perennial,
					Start = (RiverEnd)river.Start,
					End = (RiverEnd)river.Finish,
					StartLake = river.StartLake,
					EndLake = river.EndLake,
					JoinsRiver = river.JoinsRiver,
					X = new float[n],
					Z = new float[n],
					S = new float[n],
					Surface = new float[n],
					Bed = Fit(river.Bed, n),
					Width = Fit(river.Width, n),
					Depth = Fit(river.Depth, n),
					Discharge = Fit(river.Discharge, n),
				};
				for (int i = 0; i < n; i++)
				{
					path.X[i] = river.Points[i].x;
					path.Z[i] = river.Points[i].z;
					path.Surface[i] = river.Points[i].y;
					path.S[i] = i == 0 ? 0f : path.S[i - 1] + RiverShaping.Distance(path.X[i - 1], path.Z[i - 1], path.X[i], path.Z[i]);
				}
				path.Curvature = RiverShaping.SmoothAlong(RiverShaping.Curvature(path.X, path.Z), 3);
				if (river.Reach != null && river.Reach.Length == n)
				{
					path.Reach = new RiverReach[n];
					for (int i = 0; i < n; i++)
					{
						path.Reach[i] = (RiverReach)river.Reach[i];
					}
				}
				water.Rivers.Add(path);
			}
			foreach (SceneHydrology.Lake lake in asset.Lakes)
			{
				water.Lakes.Add(new SceneLake
				{
					Id = lake.Id,
					PlanetLake = lake.PlanetLake,
					Level = lake.Level,
					SpillLevel = lake.SpillLevel,
					Terminal = lake.Terminal,
					Frozen = lake.Frozen,
					Outflow = lake.Outflow,
					Seeds = new List<Vector2>(lake.Seeds ?? Array.Empty<Vector2>()),
					Bounds = lake.Bounds,
				});
			}
			return water;
		}

		// ── Helpers ─────────────────────────────────────────────────

		private static float[] Fit(float[] values, int n)
		{
			var result = new float[n];
			if (values != null)
			{
				Array.Copy(values, result, Math.Min(n, values.Length));
			}
			return result;
		}

		/// <summary>A planet river's per-cell values at fractional cell positions along it.</summary>
		private static float[] Along(List<float> t, float[] values)
		{
			var result = new float[t.Count];
			for (int i = 0; i < t.Count; i++)
			{
				int i0 = Mathf.Clamp(Mathf.FloorToInt(t[i]), 0, values.Length - 1);
				int i1 = Mathf.Min(values.Length - 1, i0 + 1);
				result[i] = Mathf.Lerp(values[i0], values[i1], t[i] - Mathf.Floor(t[i]));
			}
			return result;
		}

		/// <summary>The bed's fall per metre along a line, from the planet's filled ground, smoothed over a few hundred metres.</summary>
		private static float[] Gradient(List<float> x, List<float> z, float[] filled)
		{
			int n = x.Count;
			var g = new float[n];
			for (int i = 0; i < n; i++)
			{
				int a = Math.Max(0, i - 25), b = Math.Min(n - 1, i + 25);
				float run = 0f;
				for (int k = a; k < b; k++)
				{
					run += RiverShaping.Distance(x[k], z[k], x[k + 1], z[k + 1]);
				}
				g[i] = run > 1f ? Mathf.Max(0f, (filled[a] - filled[b]) / run) : 0f;
			}
			return g;
		}

		/// <summary>How freely each point may move off the planet's line: 0 at the scene's edge and outside it, 1 past <paramref name="holdMetres"/> in.</summary>
		private static float[] Hold(List<float> x, List<float> z, float halfW, float halfD, float holdMetres)
		{
			var hold = new float[x.Count];
			for (int i = 0; i < x.Count; i++)
			{
				float inside = Mathf.Min(halfW - Mathf.Abs(x[i]), halfD - Mathf.Abs(z[i]));
				hold[i] = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(inside / Mathf.Max(1f, holdMetres)));
			}
			return hold;
		}

		/// <summary>
		/// Runs a river's line on from its last point (<paramref name="atEnd"/>) or back from its first, on its
		/// own heading, up to <paramref name="metres"/> and no further than the scene's edge, its discharge and
		/// area carried unchanged.
		/// </summary>
		private static void ExtendIntoLake(List<float> x, List<float> z, ref float[] q, ref float[] area, bool atEnd, float metres, float spacing,
			float halfW, float halfD)
		{
			int count = x.Count;
			if (count < 4)
			{
				return;
			}
			int from = atEnd ? count - 1 : 0, toward = atEnd ? count - 4 : 3;
			float hx = x[from] - x[toward], hz = z[from] - z[toward];
			float length = Mathf.Sqrt(hx * hx + hz * hz);
			if (length < 1e-3f)
			{
				return;
			}
			hx /= length;
			hz /= length;
			var addX = new List<float>();
			var addZ = new List<float>();
			for (float d = spacing; d <= metres; d += spacing)
			{
				float px = x[from] + hx * d, pz = z[from] + hz * d;
				if (!Inside(px, pz, halfW, halfD))
				{
					break;
				}
				addX.Add(px);
				addZ.Add(pz);
			}
			int added = addX.Count;
			if (added == 0)
			{
				return;
			}
			var newQ = new float[count + added];
			var newArea = new float[count + added];
			if (atEnd)
			{
				x.AddRange(addX);
				z.AddRange(addZ);
				Array.Copy(q, newQ, count);
				Array.Copy(area, newArea, count);
				for (int k = count; k < count + added; k++)
				{
					newQ[k] = q[count - 1];
					newArea[k] = area[count - 1];
				}
			}
			else
			{
				addX.Reverse();
				addZ.Reverse();
				x.InsertRange(0, addX);
				z.InsertRange(0, addZ);
				Array.Copy(q, 0, newQ, added, count);
				Array.Copy(area, 0, newArea, added, count);
				for (int k = 0; k < added; k++)
				{
					newQ[k] = q[0];
					newArea[k] = area[0];
				}
			}
			q = newQ;
			area = newArea;
		}

		private static bool Inside(float x, float z, float halfW, float halfD) => Mathf.Abs(x) <= halfW && Mathf.Abs(z) <= halfD;

		private static float Median(float[] values)
		{
			if (values.Length == 0)
			{
				return 0f;
			}
			var copy = (float[])values.Clone();
			Array.Sort(copy);
			return copy[copy.Length / 2];
		}

		private static float[] Slice(float[] values, int from, int to)
		{
			var result = new float[to - from + 1];
			Array.Copy(values, from, result, 0, result.Length);
			return result;
		}

		/// <summary>The point of a laid river nearest a position, and the index of its nearest line point.</summary>
		private static void Nearest(RiverPath river, float px, float pz, out float x, out float z, out int index)
		{
			float best = float.MaxValue;
			x = px;
			z = pz;
			index = 0;
			for (int v = 0; v + 1 < river.Count; v++)
			{
				float ax = river.X[v], az = river.Z[v], ex = river.X[v + 1] - ax, ez = river.Z[v + 1] - az;
				float t = Mathf.Clamp01(((px - ax) * ex + (pz - az) * ez) / Mathf.Max(1e-8f, ex * ex + ez * ez));
				float cx = ax + ex * t, cz = az + ez * t;
				float d = (cx - px) * (cx - px) + (cz - pz) * (cz - pz);
				if (d < best)
				{
					best = d;
					x = cx;
					z = cz;
					index = t < 0.5f ? v : v + 1;
				}
			}
		}

		/// <summary>Scene metres to the globe and back, at the radius the scene is cut at.</summary>
		internal readonly struct SceneFrame
		{
			private readonly AtlasFootprint footprint;
			private readonly double radiusKm;
			private readonly double cos;
			private readonly double sin;

			public SceneFrame(SceneGenerationRequest request)
			{
				footprint = request.Footprint;
				radiusKm = request.ResolvedRadiusKm;
				double h = footprint.HeadingDegrees * AtlasGeometry.Deg2Rad;
				cos = Math.Cos(h);
				sin = Math.Sin(h);
			}

			public Vector3 ToUnit(float east, float north) => AtlasGeometry.SceneToUnit(footprint, east / 1000.0, north / 1000.0, radiusKm).ToVector3();

			/// <summary>A unit direction as scene metres: the inverse of the footprint's turn and walk.</summary>
			public Vector2 ToScene(Vector3 direction)
			{
				Vector2 km = AtlasGeometry.Project(footprint.Latitude, footprint.Longitude, new Vector3d(direction.x, direction.y, direction.z), radiusKm);
				double x = km.x * cos - km.y * sin;
				double z = km.x * sin + km.y * cos;
				return new Vector2((float)(x * 1000.0), (float)(z * 1000.0));
			}
		}

		/// <summary>The scene's ground smoothed over a few samples, bilinear; the planet's past the edge.</summary>
		private sealed class GroundSampler
		{
			private readonly SceneHeightField field;
			private readonly float[] smooth;

			public GroundSampler(SceneHeightField field, int radius)
			{
				this.field = field;
				smooth = new float[field.Metres.Length];
				LandscapeEvolution.SmoothInto(field.Metres, smooth, field.Width, field.Depth, radius);
			}

			public float At(float east, float north)
			{
				if (!field.Contains(east, north))
				{
					return field.MetresAt(east, north);
				}
				float gx = (east - field.EastOf(0)) / field.Spacing, gz = (north - field.NorthOf(0)) / field.Spacing;
				int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, field.Width - 2), z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, field.Depth - 2);
				float fx = Mathf.Clamp01(gx - x0), fz = Mathf.Clamp01(gz - z0);
				int i = z0 * field.Width + x0;
				return Mathf.Lerp(Mathf.Lerp(smooth[i], smooth[i + 1], fx), Mathf.Lerp(smooth[i + field.Width], smooth[i + field.Width + 1], fx), fz);
			}
		}
	}
}
#endif
