#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The planet's lakes and rivers past a scene's edge, for its backdrop: where they lie, the channels the
	/// near backdrop is cut with, and the surfaces drawn over it with the scene's own inland water material.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The same water as the scene's, only coarser.</b> Lakes are the planet drainage's lakes, at their
	/// levels: a sheet over each lake's cells and a ring round them, which the backdrop's ground hides above
	/// the shore, so the shoreline is where the ground meets the level. Rivers are the planet's river lines,
	/// as wide and deep as their discharge makes them (<see cref="RiverShaping.Size(float, float, RiverSettings, out float, out float)"/>).
	/// A river crossing the scene's edge starts or ends at the level the scene's own river has there, so the
	/// two meet.
	/// </para>
	/// <para>
	/// <b>Cut near, draped far.</b> Within <see cref="CutMetres"/> of the scene the backdrop's vertices are
	/// close enough to hold a channel, and the ground is cut down to each river's bed there, so its water
	/// stands in a channel as the scene's does. Further out a channel is narrower than the grid, so the
	/// water lies on the ground and carries its depth for the shader instead.
	/// </para>
	/// <para>
	/// <b>One water across the edge.</b> A pond the scene raised a river into (<see cref="SceneLake.PlanetLake"/>
	/// −1) runs on past the edge over the backdrop's ground below its level, as far as the scene let it spread,
	/// with a sill raised where it would leak further. A river runs on the backdrop's valley floor
	/// (<see cref="RiverShaping.SnapToFloor"/>), not the planet's 200 m cells, from where the scene's own river
	/// leaves it, and stops at the water of any lake it runs into. Its surface never stands above its own ground
	/// to meet what it runs into (<see cref="Profile"/>).
	/// </para>
	/// </remarks>
	public sealed class BackdropWater
	{
		/// <summary>
		/// How far past the scene's edge the backdrop's ground is cut with river channels, metres: the stretch
		/// where a river comes out of the scene's own channel. Past it a channel is narrower than the backdrop's
		/// grid and could only bury the water between its vertices, so the water lies on the ground drawn.
		/// </summary>
		public const float CutMetres = 150f;
		/// <summary>Over how far before <see cref="CutMetres"/> the cut fades out, metres.</summary>
		public const float CutFadeMetres = 50f;
		/// <summary>
		/// The least half-width a channel cut into the backdrop has, metres: three of the first ring's vertices
		/// across (8 m apart), so the grid holds it. Reached <see cref="TrenchTaperMetres"/> past the edge, from
		/// the river's own width at it, where it meets the scene's channel.
		/// </summary>
		public const float TrenchHalfMetres = 12f;
		public const float TrenchTaperMetres = 60f;
		/// <summary>How far apart a river's rows lie, metres: the near ring's grid, so the water follows the ground drawn between them.</summary>
		public const float RowMetres = 8f;
		/// <summary>The farthest a river is moved sideways onto the backdrop's valley floor, metres: about a planet cell.</summary>
		public const float SnapMetres = 150f;
		/// <summary>Over how far from the scene's edge a river is held to the scene's own river where they meet, metres.</summary>
		public const float HoldMetres = 200f;
		/// <summary>Over how many rows a river comes down onto the level of what it runs into.</summary>
		public const int MeetRows = 4;
		/// <summary>How far above a pond's level a sill stands where the pond would leak past its reach, metres.</summary>
		public const float SillMetres = 0.5f;
		/// <summary>The far side of a sill, falling back to the ground, metres.</summary>
		public const float SillRunMetres = LakeCellMetres;
		/// <summary>The grid lakes are laid on past the scene, metres.</summary>
		public const float LakeCellMetres = 50f;
		/// <summary>How steeply a channel's banks are cut, degrees.</summary>
		public const float BankDegrees = 30f;
		/// <summary>The most a channel's banks are cut back past its edge, metres.</summary>
		public const float MaxBankMetres = 30f;

		/// <summary>One river line past the scene's edge.</summary>
		public sealed class Line
		{
			public int PlanetRiver;
			public Vector2[] Points;
			public float[] Surface;
			public float[] Width;
			public float[] Depth;
			public float[] Speed;
		}

		/// <summary>One run of a planet river past the scene's edge, before it is shaped.</summary>
		public sealed class Run
		{
			public int PlanetRiver;
			/// <summary>Upstream to downstream, scene metres.</summary>
			public readonly List<Vector2> Points = new List<Vector2>();
			/// <summary>Discharge at each point, m³/s.</summary>
			public readonly List<float> Discharge = new List<float>();
			/// <summary>It comes out of the scene at its first point.</summary>
			public bool StartsAtScene;
			/// <summary>It goes into the scene at its last point.</summary>
			public bool EndsAtScene;
		}

		public readonly List<Line> Rivers = new List<Line>();

		private readonly float halfW, halfD, reach;
		private readonly Func<float, float, Vector3> lakeAt;
		private readonly Func<float, float, float> ground;
		private readonly Dictionary<long, List<(int line, int segment)>> hash = new Dictionary<long, List<(int, int)>>();
		private const float HashMetres = 100f;
		private float widest;

		// The lakes on the LakeCellMetres grid: each cell's level (NaN for none), whether water stands in it,
		// and the level a sill in it holds back (NaN for none).
		private int lakeNx, lakeNz;
		private float[] lakeLevel;
		private bool[] lakeWet;
		private float[] sillLevel;
		private bool anyLake, anySill;

		private BackdropWater(float halfW, float halfD, float reach, Func<float, float, Vector3> lakeAt, Func<float, float, float> ground)
		{
			this.halfW = halfW;
			this.halfD = halfD;
			this.reach = reach;
			this.lakeAt = lakeAt;
			this.ground = ground;
		}

		/// <summary>True when there is no water past the edge to draw.</summary>
		public bool Empty => !anyLake && Rivers.Count == 0;

		/// <summary>
		/// The water past <paramref name="request"/>'s edge, out to <paramref name="reach"/>, from the planet's
		/// drainage; null on a body with none. <paramref name="ground"/> is the backdrop's own ground.
		/// </summary>
		public static BackdropWater Build(SceneGenerationRequest request, SceneWater scene, Func<float, float, float> ground,
			float halfW, float halfD, float reach)
		{
			PlanetDrainage drainage = PlanetDrainage.For(request, SolarSystemProfile.Resolve(request.Body));
			if (drainage == null || drainage.Result == null)
			{
				return null;
			}
			var frame = new SceneWater.SceneFrame(request);
			float extentX = halfW + reach, extentZ = halfD + reach;

			// Lakes: each lake's cells and a ring of their neighbours, at its level.
			DrainageResult result = drainage.Result;
			var levelOf = new Dictionary<int, (float level, int lake, bool core)>();
			CubeSphereGrid grid = drainage.Grid;
			foreach (DrainageLake lake in result.Lakes)
			{
				bool near = false;
				foreach (int c in lake.Cells)
				{
					Vector2 p = frame.ToScene(drainage.CentreOf(c));
					if (Mathf.Abs(p.x) <= extentX + 400f && Mathf.Abs(p.y) <= extentZ + 400f)
					{
						near = true;
						break;
					}
				}
				if (!near)
				{
					continue;
				}
				foreach (int c in lake.Cells)
				{
					if (result.LakeOf[c] != lake.Id)
					{
						continue;
					}
					levelOf[c] = (lake.Level, lake.Id, true);
					for (int k = grid.NeighbourStart[c]; k < grid.NeighbourStart[c + 1]; k++)
					{
						int m = grid.Neighbours[k];
						if (!levelOf.ContainsKey(m))
						{
							levelOf[m] = (lake.Level, lake.Id, false);
						}
					}
				}
			}
			// Which lake's cells (or the ring round them) a point lies in: (level, lake, 1 for its own cells), level NaN for none.
			Vector3 LakeAt(float east, float north)
			{
				if (levelOf.Count == 0 || !levelOf.TryGetValue(drainage.CellOf(frame.ToUnit(east, north)), out (float level, int lake, bool core) at))
				{
					return new Vector3(float.NaN, -1f, 0f);
				}
				return new Vector3(at.level, at.lake, at.core ? 1f : 0f);
			}

			// Rivers: each planet river's runs inside the backdrop and outside the scene, where water runs all year.
			var runs = new List<Run>();
			float perennial = new DrainageSettings().MinRiverDischarge;
			foreach (DrainageRiver river in result.Rivers)
			{
				int n = river.Cells.Length;
				if (n < 2)
				{
					continue;
				}
				var points = new Vector2[n];
				for (int i = 0; i < n; i++)
				{
					points[i] = frame.ToScene(drainage.CentreOf(river.Cells[i]));
				}
				var run = new Run { PlanetRiver = river.Id };
				for (int i = 0; i <= n; i++)
				{
					bool keep = i < n && river.Discharge[i] >= perennial && InBackdrop(points[i], halfW, halfD, extentX, extentZ);
					if (keep)
					{
						if (run.Points.Count == 0 && i > 0 && InScene(points[i - 1], halfW, halfD))
						{
							// Starts where it leaves the scene: on the scene's edge.
							run.Points.Add(EdgeCrossing(points[i - 1], points[i], halfW, halfD));
							run.Discharge.Add(river.Discharge[i]);
							run.StartsAtScene = true;
						}
						run.Points.Add(points[i]);
						run.Discharge.Add(river.Discharge[i]);
						continue;
					}
					if (run.Points.Count > 0)
					{
						if (i < n && InScene(points[i], halfW, halfD))
						{
							// Ends where it enters the scene.
							run.Points.Add(EdgeCrossing(points[i], points[i - 1], halfW, halfD));
							run.Discharge.Add(river.Discharge[i - 1]);
							run.EndsAtScene = true;
						}
						runs.Add(run);
					}
					run = new Run { PlanetRiver = river.Id };
				}
			}
			return Assemble(scene, ground, halfW, halfD, reach, levelOf.Count > 0 ? LakeAt : (Func<float, float, Vector3>)null, runs);
		}

		/// <summary>
		/// The water past a scene's edge from what it is made of, the planet aside: <paramref name="lakeAt"/> gives the
		/// planet lake at a point as (level, lake, 1 for its own cells), level NaN for none (null for no lakes), and
		/// <paramref name="runs"/> are its rivers' runs past the edge. <paramref name="scene"/>'s ponds and rivers are
		/// what they meet at the edge.
		/// </summary>
		public static BackdropWater Assemble(SceneWater scene, Func<float, float, float> ground, float halfW, float halfD, float reach,
			Func<float, float, Vector3> lakeAt, IEnumerable<Run> runs)
		{
			var water = new BackdropWater(halfW, halfD, reach, lakeAt ?? ((east, north) => new Vector3(float.NaN, -1f, 0f)), ground);
			water.FloodLakes(scene);
			RiverSettings settings = scene != null ? scene.Settings : new RiverSettings();
			foreach (Run run in runs)
			{
				water.AddRun(run, scene, settings);
			}
			water.Index();
			return water;
		}

		private static bool InScene(Vector2 p, float halfW, float halfD) => Mathf.Abs(p.x) < halfW && Mathf.Abs(p.y) < halfD;

		private static bool InBackdrop(Vector2 p, float halfW, float halfD, float extentX, float extentZ) =>
			!InScene(p, halfW, halfD) && Mathf.Abs(p.x) <= extentX && Mathf.Abs(p.y) <= extentZ;

		/// <summary>Where the segment from <paramref name="inside"/> to <paramref name="outside"/> crosses the scene's edge.</summary>
		private static Vector2 EdgeCrossing(Vector2 inside, Vector2 outside, float halfW, float halfD)
		{
			float t = 1f;
			Vector2 d = outside - inside;
			if (Mathf.Abs(d.x) > 1e-4f)
			{
				float tx = ((d.x > 0f ? halfW : -halfW) - inside.x) / d.x;
				if (tx >= 0f)
				{
					t = Mathf.Min(t, tx);
				}
			}
			if (Mathf.Abs(d.y) > 1e-4f)
			{
				float tz = ((d.y > 0f ? halfD : -halfD) - inside.y) / d.y;
				if (tz >= 0f)
				{
					t = Mathf.Min(t, tz);
				}
			}
			return inside + d * Mathf.Clamp01(t);
		}

		private void AddRun(Run run, SceneWater scene, RiverSettings settings)
		{
			if (run.Points.Count < 2)
			{
				return;
			}
			var xs = new List<float>(run.Points.Count);
			var zs = new List<float>(run.Points.Count);
			var qs = new List<float>(run.Discharge);
			foreach (Vector2 p in run.Points)
			{
				xs.Add(p.x);
				zs.Add(p.y);
			}
			RiverShaping.Chaikin(xs, zs, qs, 2);
			RiverShaping.Resample(xs, zs, qs, RowMetres);
			if (xs.Count < 2)
			{
				return;
			}

			/* Where it meets the scene's own river: from that river's end, which the scene moved onto its own valley
			 * floor, and at its level there. */
			float startLevel = float.NaN, endLevel = float.NaN;
			if (run.StartsAtScene && EdgeMeet(scene, run.PlanetRiver, new Vector2(xs[0], zs[0]), true, out startLevel, out Vector2 from))
			{
				ShiftEnd(xs, zs, false, from);
			}
			if (run.EndsAtScene && EdgeMeet(scene, run.PlanetRiver, new Vector2(xs[xs.Count - 1], zs[zs.Count - 1]), false, out endLevel, out Vector2 to))
			{
				ShiftEnd(xs, zs, true, to);
			}

			/* Onto the backdrop's own valley floor: the planet's line runs from cell centre to cell centre, 200 m apart
			 * and more, and laid as it was it crossed hillsides the ground drawn has no valley on. Held where it meets
			 * the scene, so it still comes out of the scene's channel. */
			var along = new float[xs.Count];
			for (int i = 1; i < xs.Count; i++)
			{
				along[i] = along[i - 1] + RiverShaping.Distance(xs[i - 1], zs[i - 1], xs[i], zs[i]);
			}
			float length = along[xs.Count - 1];
			var hold = new float[xs.Count];
			for (int i = 0; i < xs.Count; i++)
			{
				hold[i] = 1f;
				if (run.StartsAtScene)
				{
					hold[i] = Mathf.Min(hold[i], Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(along[i] / HoldMetres)));
				}
				if (run.EndsAtScene)
				{
					hold[i] = Mathf.Min(hold[i], Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((length - along[i]) / HoldMetres)));
				}
			}
			RiverShaping.SnapToFloor(xs, zs, ground, hold, SnapMetres, settings.SnapPenalty, 120f);
			RiverShaping.SnapToFloor(xs, zs, ground, hold, SnapMetres * 0.4f, settings.SnapPenalty, 60f);
			var widths = new float[xs.Count];
			for (int i = 0; i < xs.Count; i++)
			{
				RiverShaping.Size(qs[i], 0.5f, settings, out widths[i], out _);
			}
			RiverShaping.Untangle(xs, zs, qs, widths, 4f);
			int n = xs.Count;
			if (n < 2)
			{
				return;
			}

			/* Under a lake it is the lake's water: it ends at the water it runs into and begins again where it runs out,
			 * a row on into the lake either side so it meets the water, not the shore. */
			var lakeLevelAt = new float[n];
			for (int i = 0; i < n; i++)
			{
				lakeLevelAt[i] = LakeLevelOver(xs[i], zs[i]);
			}
			int k = 0;
			while (k < n)
			{
				if (!float.IsNaN(lakeLevelAt[k]))
				{
					k++;
					continue;
				}
				int a = k, b = k;
				while (b + 1 < n && float.IsNaN(lakeLevelAt[b + 1]))
				{
					b++;
				}
				k = b + 1;
				// A lone row out of the water (the scene's edge, where the backdrop has no lake) is nothing to draw.
				if (b == a && (a > 0 || b < n - 1))
				{
					continue;
				}
				float sLevel = a == 0 ? startLevel : lakeLevelAt[a - 1];
				float eLevel = b == n - 1 ? endLevel : lakeLevelAt[b + 1];
				int from0 = Mathf.Max(0, a - 1), to0 = Mathf.Min(n - 1, b + 1);
				AddLine(run.PlanetRiver, xs, zs, qs, from0, to0, sLevel, eLevel, settings);
			}
		}

		/// <summary>Moves a line's first or last point (<paramref name="atEnd"/>) onto <paramref name="to"/>, the rows within <see cref="HoldMetres"/> of it following less the farther they are.</summary>
		private static void ShiftEnd(List<float> x, List<float> z, bool atEnd, Vector2 to)
		{
			int n = x.Count;
			int from = atEnd ? n - 1 : 0, step = atEnd ? -1 : 1;
			float sx = to.x - x[from], sz = to.y - z[from];
			float along = 0f;
			for (int i = from; i >= 0 && i < n; i += step)
			{
				if (i != from)
				{
					along += RiverShaping.Distance(x[i - step], z[i - step], x[i], z[i]);
				}
				float w = Mathf.Clamp01(1f - along / HoldMetres);
				if (w <= 0f)
				{
					break;
				}
				x[i] += sx * w;
				z[i] += sz * w;
			}
		}

		/// <summary>One shaped stretch of a run, points <paramref name="a"/> … <paramref name="b"/>, meeting <paramref name="startLevel"/> and <paramref name="endLevel"/> (NaN for nothing).</summary>
		private void AddLine(int planetRiver, List<float> xs, List<float> zs, List<float> qs, int a, int b, float startLevel, float endLevel,
			RiverSettings settings)
		{
			int n = b - a + 1;
			if (n < 2)
			{
				return;
			}
			var line = new Line
			{
				PlanetRiver = planetRiver,
				Points = new Vector2[n],
				Surface = new float[n],
				Width = new float[n],
				Depth = new float[n],
				Speed = new float[n],
			};
			var groundAt = new float[n];
			for (int i = 0; i < n; i++)
			{
				line.Points[i] = new Vector2(xs[a + i], zs[a + i]);
				RiverShaping.Size(qs[a + i], 0.5f, settings, out line.Width[i], out line.Depth[i]);
				line.Speed[i] = qs[a + i] / Mathf.Max(0.05f, line.Width[i] * line.Depth[i] * (2f / 3f));
				widest = Mathf.Max(widest, line.Width[i]);
				groundAt[i] = ground(line.Points[i].x, line.Points[i].y);
			}
			line.Width = RiverShaping.SmoothAlong(line.Width, 3);
			line.Depth = RiverShaping.SmoothAlong(line.Depth, 3);
			line.Surface = Profile(groundAt, startLevel, endLevel, settings.InsetMetres, MeetRows);
			Rivers.Add(line);
		}

		/// <summary>
		/// A river's surface along its rows from the ground under them: a little under it, never rising downstream,
		/// starting from <paramref name="startLevel"/> (the water it comes out of; NaN for none) and coming down onto
		/// <paramref name="endLevel"/> (the water it runs into) over its last <paramref name="meetRows"/> rows.
		/// </summary>
		/// <remarks>
		/// Where what it runs into stands higher than its own ground lets it, it is held up from its end only as far
		/// as its ground stands over that level (a backwater), never over its ground: lifted all the way to it, a river
		/// running into a scene's pond lay as one flat sheet over the lower hills it crossed.
		/// </remarks>
		public static float[] Profile(float[] ground, float startLevel, float endLevel, float inset, int meetRows)
		{
			int n = ground.Length;
			var surface = new float[n];
			float running = float.IsNaN(startLevel) ? float.PositiveInfinity : startLevel;
			for (int i = 0; i < n; i++)
			{
				running = Mathf.Min(running, ground[i] - inset);
				surface[i] = running;
			}
			if (n == 0 || float.IsNaN(endLevel))
			{
				return surface;
			}
			int last = n - 1;
			if (surface[last] > endLevel)
			{
				int rows = Mathf.Clamp(meetRows, 1, Mathf.Max(1, last));
				for (int j = 0; j <= rows && last - j >= 0; j++)
				{
					int i = last - j;
					surface[i] = Mathf.Min(surface[i], Mathf.Lerp(endLevel, surface[i], j / (float)rows));
				}
			}
			else
			{
				for (int i = last; i >= 0 && ground[i] - inset >= endLevel; i--)
				{
					surface[i] = Mathf.Max(surface[i], endLevel);
				}
			}
			return surface;
		}

		/// <summary>
		/// The scene's own river where it meets the edge at <paramref name="at"/>: its last point if it leaves the
		/// scene there (<paramref name="leaving"/>), its first if it comes in, and its level there. False when the
		/// scene has none within 200 m.
		/// </summary>
		private static bool EdgeMeet(SceneWater scene, int planetRiver, Vector2 at, bool leaving, out float level, out Vector2 point)
		{
			level = float.NaN;
			point = at;
			if (scene == null)
			{
				return false;
			}
			float best = 200f * 200f;
			bool found = false;
			foreach (RiverPath path in scene.Rivers)
			{
				if (path.PlanetRiver != planetRiver || path.Count < 2 || !path.Perennial)
				{
					continue;
				}
				int i = leaving ? path.Count - 1 : 0;
				if ((leaving ? path.End : path.Start) != RiverEnd.Edge)
				{
					continue;
				}
				var end = new Vector2(path.X[i], path.Z[i]);
				float d = (end - at).sqrMagnitude;
				if (d < best)
				{
					best = d;
					level = path.Surface[i];
					point = end;
					found = true;
				}
			}
			return found;
		}

		// ── The lakes ──────────────────────────────────────────────────

		/// <summary>The lake cell holding a point; −1 off the grid.</summary>
		private int LakeCell(float east, float north)
		{
			if (lakeLevel == null)
			{
				return -1;
			}
			int x = Mathf.FloorToInt((east + halfW + reach) / LakeCellMetres), z = Mathf.FloorToInt((north + halfD + reach) / LakeCellMetres);
			return x < 0 || z < 0 || x >= lakeNx || z >= lakeNz ? -1 : z * lakeNx + x;
		}

		/// <summary>The level of the lake whose water stands over a point past the scene's edge; NaN where none does.</summary>
		public float LakeLevelOver(float east, float north)
		{
			int i = LakeCell(east, north);
			if (i < 0 || !lakeWet[i] || PastEdge(east, north) <= 0f)
			{
				return float.NaN;
			}
			return ground(east, north) < lakeLevel[i] ? lakeLevel[i] : float.NaN;
		}

		/// <summary>
		/// Floods the lakes past the scene onto the <see cref="LakeCellMetres"/> grid: the planet's, from each lake's
		/// own cells across the ground lower than its level that joins them, within its cells and the ring round them,
		/// so the water stands wherever the ground is under the level and nowhere it is not (no holes where a planet
		/// cell's mean ground stood higher, no sheet hung over a valley falling away past the outlet); then the
		/// scene's ponds, on from the edge where the scene's pond meets it.
		/// </summary>
		private void FloodLakes(SceneWater scene)
		{
			float extentX = halfW + reach, extentZ = halfD + reach;
			int nx = lakeNx = Mathf.CeilToInt(2f * extentX / LakeCellMetres), nz = lakeNz = Mathf.CeilToInt(2f * extentZ / LakeCellMetres);
			int count = nx * nz;
			var level = lakeLevel = new float[count];
			var lake = new int[count];
			var low = new float[count];
			var wet = lakeWet = new bool[count];
			var sill = sillLevel = new float[count];
			var queue = new Queue<int>();
			float half = 0.5f * LakeCellMetres;
			for (int z = 0; z < nz; z++)
			{
				for (int x = 0; x < nx; x++)
				{
					int i = z * nx + x;
					float cx = -extentX + (x + 0.5f) * LakeCellMetres, cz = -extentZ + (z + 0.5f) * LakeCellMetres;
					sill[i] = float.NaN;
					low[i] = float.NaN;
					Vector3 at = lakeAt(cx, cz);
					level[i] = at.x;
					lake[i] = (int)at.y;
					if (!float.IsNaN(at.x) && LowestIn(i) < at.x && at.z > 0.5f && ground(cx, cz) < at.x)
					{
						wet[i] = true;
						queue.Enqueue(i);
					}
				}
			}
			while (queue.Count > 0)
			{
				int i = queue.Dequeue();
				int x = i % nx, z = i / nx;
				for (int k = 0; k < 4; k++)
				{
					int ax = x + (k == 0 ? 1 : k == 1 ? -1 : 0), az = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
					if (ax < 0 || az < 0 || ax >= nx || az >= nz)
					{
						continue;
					}
					int j = az * nx + ax;
					if (wet[j] || float.IsNaN(level[j]) || lake[j] != lake[i] || LowestIn(j) >= level[j])
					{
						continue;
					}
					wet[j] = true;
					queue.Enqueue(j);
				}
			}

			if (scene != null)
			{
				foreach (SceneLake pond in scene.Lakes)
				{
					if (pond.PlanetLake < 0)
					{
						FloodPond(pond, LowestIn, queue);
					}
				}
			}
			for (int i = 0; i < count; i++)
			{
				anyLake |= wet[i] && OutsideScene(i);
				anySill |= !float.IsNaN(sill[i]);
			}

			// The lowest of a cell's centre and corners, so a shore cell is drawn and the ground cuts it. Asked only
			// of cells a lake may stand in: across a 10 km backdrop with none, it was 800 000 samples of the ground.
			float LowestIn(int i)
			{
				if (float.IsNaN(low[i]))
				{
					float cx = -extentX + (i % nx + 0.5f) * LakeCellMetres, cz = -extentZ + (i / nx + 0.5f) * LakeCellMetres;
					low[i] = Mathf.Min(ground(cx, cz), Mathf.Min(Mathf.Min(ground(cx - half, cz - half), ground(cx + half, cz - half)),
						Mathf.Min(ground(cx - half, cz + half), ground(cx + half, cz + half))));
				}
				return low[i];
			}
		}

		/// <summary>Whether any of a lake cell lies past the scene's edge.</summary>
		private bool OutsideScene(int i)
		{
			float x0 = -(halfW + reach) + (i % lakeNx) * LakeCellMetres, z0 = -(halfD + reach) + (i / lakeNx) * LakeCellMetres;
			return x0 < -halfW || x0 + LakeCellMetres > halfW || z0 < -halfD || z0 + LakeCellMetres > halfD;
		}

		/// <summary>
		/// A scene's pond past its edge: from the cells along the edge where the pond may stand and the ground there is
		/// under its level, across the backdrop's ground under its level, as far as the scene let it spread
		/// (<see cref="SceneLake.MayCover"/>); a sill where it would run on past that.
		/// </summary>
		private void FloodPond(SceneLake pond, Func<int, float> low, Queue<int> queue)
		{
			float extentX = halfW + reach, extentZ = halfD + reach;
			float level = pond.Level;
			bool May(float east, float north) => pond.MayCover != null ? pond.MayCover(east, north) : pond.Bounds.Contains(new Vector2(east, north));
			queue.Clear();
			for (int i = 0; i < lakeWet.Length; i++)
			{
				float cx = -extentX + (i % lakeNx + 0.5f) * LakeCellMetres, cz = -extentZ + (i / lakeNx + 0.5f) * LakeCellMetres;
				if (lakeWet[i] || PastEdge(cx, cz) > LakeCellMetres || !OutsideScene(i) || low(i) >= level)
				{
					continue;
				}
				// Where the scene's pond meets its edge: the edge's point nearest the cell, its ground under the level.
				Vector2 edge = NearestEdgePoint(cx, cz);
				if (!May(edge.x, edge.y) || ground(edge.x, edge.y) >= level)
				{
					continue;
				}
				lakeWet[i] = true;
				lakeLevel[i] = level;
				queue.Enqueue(i);
			}
			while (queue.Count > 0)
			{
				int i = queue.Dequeue();
				int x = i % lakeNx, z = i / lakeNx;
				for (int k = 0; k < 4; k++)
				{
					int ax = x + (k == 0 ? 1 : k == 1 ? -1 : 0), az = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
					if (ax < 0 || az < 0 || ax >= lakeNx || az >= lakeNz)
					{
						continue;
					}
					int j = az * lakeNx + ax;
					if (lakeWet[j] || !OutsideScene(j) || low(j) >= level)
					{
						continue;
					}
					float cx = -extentX + (ax + 0.5f) * LakeCellMetres, cz = -extentZ + (az + 0.5f) * LakeCellMetres;
					if (!May(cx, cz))
					{
						// Low ground the pond may not spread over: a sill holds it back, or its water stops in a straight line.
						sillLevel[j] = float.IsNaN(sillLevel[j]) ? level : Mathf.Max(sillLevel[j], level);
						continue;
					}
					lakeWet[j] = true;
					lakeLevel[j] = level;
					queue.Enqueue(j);
				}
			}
		}

		/// <summary>The point on the scene's edge nearest a point.</summary>
		private Vector2 NearestEdgePoint(float east, float north)
		{
			float x = Mathf.Clamp(east, -halfW, halfW), z = Mathf.Clamp(north, -halfD, halfD);
			if (Mathf.Abs(east) < halfW && Mathf.Abs(north) < halfD)
			{
				// Inside: out to the nearer side.
				if (halfW - Mathf.Abs(east) < halfD - Mathf.Abs(north))
				{
					x = east < 0f ? -halfW : halfW;
				}
				else
				{
					z = north < 0f ? -halfD : halfD;
				}
			}
			return new Vector2(x, z);
		}

		/// <summary>
		/// The ground raised into a sill where a pond would leak past its reach: up to <see cref="SillMetres"/> over its
		/// level across the sill's cell, falling back to the ground over <see cref="SillRunMetres"/> round it, and
		/// nothing at the scene's edge, where the backdrop meets the scene's ground.
		/// </summary>
		private float Sill(float east, float north, float height)
		{
			int centre = LakeCell(east, north);
			if (centre < 0)
			{
				return height;
			}
			float extentX = halfW + reach, extentZ = halfD + reach;
			int cx = centre % lakeNx, cz = centre / lakeNx;
			float raised = height;
			for (int dz = -1; dz <= 1; dz++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					int x = cx + dx, z = cz + dz;
					if (x < 0 || z < 0 || x >= lakeNx || z >= lakeNz)
					{
						continue;
					}
					float level = sillLevel[z * lakeNx + x];
					if (float.IsNaN(level))
					{
						continue;
					}
					float x0 = -extentX + x * LakeCellMetres, z0 = -extentZ + z * LakeCellMetres;
					float ox = Mathf.Max(0f, Mathf.Max(x0 - east, east - (x0 + LakeCellMetres)));
					float oz = Mathf.Max(0f, Mathf.Max(z0 - north, north - (z0 + LakeCellMetres)));
					float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Sqrt(ox * ox + oz * oz) / SillRunMetres));
					raised = Mathf.Max(raised, Mathf.Lerp(height, level + SillMetres, w));
				}
			}
			return Mathf.Lerp(height, raised, Mathf.Clamp01(PastEdge(east, north) / LakeCellMetres));
		}

		private void Index()
		{
			hash.Clear();
			for (int r = 0; r < Rivers.Count; r++)
			{
				Vector2[] p = Rivers[r].Points;
				for (int i = 0; i + 1 < p.Length; i++)
				{
					Vector2 a = p[i], b = p[i + 1];
					int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / HashMetres), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / HashMetres);
					int z0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / HashMetres), z1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / HashMetres);
					for (int z = z0; z <= z1; z++)
					{
						for (int x = x0; x <= x1; x++)
						{
							long key = Key(x, z);
							if (!hash.TryGetValue(key, out List<(int, int)> list))
							{
								hash[key] = list = new List<(int, int)>();
							}
							list.Add((r, i));
						}
					}
				}
			}
		}

		private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

		/// <summary>
		/// The nearest river segment to a point within <paramref name="radius"/>: its line, the distance, and
		/// the surface, width and depth there. False when none is that close.
		/// </summary>
		private bool Nearest(float east, float north, float radius, out float distance, out float surface, out float width, out float depth)
		{
			distance = float.MaxValue;
			surface = width = depth = 0f;
			int cells = Mathf.CeilToInt(radius / HashMetres);
			int cx = Mathf.FloorToInt(east / HashMetres), cz = Mathf.FloorToInt(north / HashMetres);
			bool any = false;
			var p = new Vector2(east, north);
			for (int z = cz - cells; z <= cz + cells; z++)
			{
				for (int x = cx - cells; x <= cx + cells; x++)
				{
					if (!hash.TryGetValue(Key(x, z), out List<(int line, int segment)> list))
					{
						continue;
					}
					foreach ((int line, int segment) in list)
					{
						Line l = Rivers[line];
						Vector2 a = l.Points[segment], b = l.Points[segment + 1];
						Vector2 ab = b - a;
						float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
						float d = Vector2.Distance(p, a + ab * t);
						if (d < distance && d <= radius)
						{
							distance = d;
							surface = Mathf.Lerp(l.Surface[segment], l.Surface[segment + 1], t);
							width = Mathf.Lerp(l.Width[segment], l.Width[segment + 1], t);
							depth = Mathf.Lerp(l.Depth[segment], l.Depth[segment + 1], t);
							any = true;
						}
					}
				}
			}
			return any;
		}

		/// <summary>How far a point lies past the scene's edge, metres (0 inside it).</summary>
		private float PastEdge(float east, float north) => Mathf.Max(0f, Mathf.Max(Mathf.Abs(east) - halfW, Mathf.Abs(north) - halfD));

		/// <summary>
		/// The backdrop's ground with the river channels cut into it near the scene: a parabola from bank to
		/// bank down to the bed, banks cut back at <see cref="BankDegrees"/>, fading out toward <see cref="CutMetres"/>;
		/// and the sills that hold a pond in (<see cref="Sill"/>).
		/// </summary>
		public float Cut(float east, float north, float height)
		{
			if (anySill)
			{
				height = Sill(east, north, height);
			}
			if (Rivers.Count == 0)
			{
				return height;
			}
			float past = PastEdge(east, north);
			float fade = 1f - Mathf.Clamp01((past - (CutMetres - CutFadeMetres)) / CutFadeMetres);
			if (fade <= 0f)
			{
				return height;
			}
			float reachOut = Mathf.Max(0.5f * widest, TrenchHalfMetres) + MaxBankMetres;
			if (!Nearest(east, north, reachOut, out float distance, out float surface, out float width, out float depth))
			{
				return height;
			}
			float half = TrenchHalf(0.5f * width, past);
			float channel;
			if (distance < half)
			{
				float across = distance / Mathf.Max(0.01f, half);
				channel = surface - depth * (1f - across * across);
			}
			else
			{
				float bank = distance - half;
				if (bank > MaxBankMetres)
				{
					return height;
				}
				channel = surface + 0.4f + bank * Mathf.Tan(BankDegrees * Mathf.Deg2Rad);
			}
			return Mathf.Lerp(height, Mathf.Min(height, channel), fade);
		}

		/// <summary>The half-width of a river's channel and water in the cut stretch, <paramref name="past"/> metres past the edge.</summary>
		private static float TrenchHalf(float half, float past) => Mathf.Lerp(half, Mathf.Max(half, TrenchHalfMetres), Mathf.Clamp01(past / TrenchTaperMetres));

		/// <summary>How wet the ground is at a point for the colour bake, 0 … 1: a river's bed and banks.</summary>
		public float Wetness(float east, float north, float texelMetres)
		{
			if (Rivers.Count == 0)
			{
				return 0f;
			}
			float reachOut = 0.5f * widest + 2f * texelMetres;
			if (!Nearest(east, north, reachOut, out float distance, out _, out float width, out _))
			{
				return 0f;
			}
			float half = 0.5f * width;
			return 1f - Mathf.Clamp01((distance - half) / Mathf.Max(1f, 1.5f * texelMetres));
		}

		// ── The surfaces ───────────────────────────────────────────────

		/// <summary>
		/// The water's meshes: one for the lakes past the scene and one per river, each with the inland water's
		/// vertex channels (InlandWaterRenderer's layout), heights from <paramref name="surface"/> — the
		/// backdrop's own drawn ground, so far water lies on what is drawn.
		/// </summary>
		public List<(string name, Mesh mesh, int order)> Meshes(Func<float, float, float> surface)
		{
			var meshes = new List<(string, Mesh, int)>();
			Mesh lakes = LakeMesh();
			if (lakes != null)
			{
				meshes.Add(("Backdrop Lakes", lakes, 0));
			}
			for (int r = 0; r < Rivers.Count; r++)
			{
				Mesh river = RiverMesh(Rivers[r], surface);
				if (river != null)
				{
					meshes.Add(($"Backdrop River {Rivers[r].PlanetRiver}.{r}", river, 1 + r));
				}
			}
			return meshes;
		}

		/// <summary>
		/// The lakes past the scene (<see cref="FloodLakes"/>): a sheet at its level over every wet cell, clipped at the
		/// scene's edge rather than dropped where a cell overlaps it, so the backdrop's water meets the scene's at the
		/// edge and not up to a cell short of it.
		/// </summary>
		private Mesh LakeMesh()
		{
			if (lakeWet == null)
			{
				return null;
			}
			float extentX = halfW + reach, extentZ = halfD + reach;
			var positions = new List<Vector3>();
			var indices = new List<int>();
			var pieces = new List<Rect>(4);
			for (int i = 0; i < lakeWet.Length; i++)
			{
				if (!lakeWet[i])
				{
					continue;
				}
				float x0 = -extentX + (i % lakeNx) * LakeCellMetres, z0 = -extentZ + (i / lakeNx) * LakeCellMetres;
				// The scene's own lakes cover its ground (the flood still runs through it, to reach a lake's far side).
				OutsideRect(new Rect(x0, z0, LakeCellMetres, LakeCellMetres), halfW, halfD, pieces);
				foreach (Rect piece in pieces)
				{
					int b = positions.Count;
					positions.Add(new Vector3(piece.xMin, lakeLevel[i], piece.yMin));
					positions.Add(new Vector3(piece.xMax, lakeLevel[i], piece.yMin));
					positions.Add(new Vector3(piece.xMax, lakeLevel[i], piece.yMax));
					positions.Add(new Vector3(piece.xMin, lakeLevel[i], piece.yMax));
					indices.Add(b); indices.Add(b + 3); indices.Add(b + 2);
					indices.Add(b); indices.Add(b + 2); indices.Add(b + 1);
				}
			}
			if (indices.Count == 0)
			{
				return null;
			}
			int vertices = positions.Count;
			var uvs = new List<Vector2>(vertices);
			var zero = new List<Vector2>(vertices);
			var colours = new List<Color32>(vertices);
			foreach (Vector3 p in positions)
			{
				uvs.Add(new Vector2(p.x, p.z));
				zero.Add(Vector2.zero);
				colours.Add(new Color32(255, 255, 255, 255));
			}
			return Finish("Backdrop Lakes", positions, uvs, zero, zero, zero, colours, indices);
		}

		/// <summary>
		/// The parts of <paramref name="cell"/> outside the scene's rectangle (±<paramref name="halfW"/>,
		/// ±<paramref name="halfD"/>), as up to four rectangles that do not overlap: the whole cell when it lies
		/// clear of the scene, none when inside it.
		/// </summary>
		public static void OutsideRect(Rect cell, float halfW, float halfD, List<Rect> pieces)
		{
			pieces.Clear();
			if (cell.xMax <= -halfW || cell.xMin >= halfW || cell.yMax <= -halfD || cell.yMin >= halfD)
			{
				pieces.Add(cell);
				return;
			}
			// West and east strips the cell's full depth, then north and south between them.
			if (cell.xMin < -halfW)
			{
				pieces.Add(Rect.MinMaxRect(cell.xMin, cell.yMin, -halfW, cell.yMax));
			}
			if (cell.xMax > halfW)
			{
				pieces.Add(Rect.MinMaxRect(halfW, cell.yMin, cell.xMax, cell.yMax));
			}
			float xMin = Mathf.Max(cell.xMin, -halfW), xMax = Mathf.Min(cell.xMax, halfW);
			if (cell.yMin < -halfD)
			{
				pieces.Add(Rect.MinMaxRect(xMin, cell.yMin, xMax, -halfD));
			}
			if (cell.yMax > halfD)
			{
				pieces.Add(Rect.MinMaxRect(xMin, halfD, xMax, cell.yMax));
			}
		}

		private Mesh RiverMesh(Line line, Func<float, float, float> surface)
		{
			int n = line.Points.Length;
			var positions = new List<Vector3>(n * 3);
			var uvs = new List<Vector2>(n * 3);
			var flows = new List<Vector2>(n * 3);
			var states = new List<Vector2>(n * 3);
			var tides = new List<Vector2>(n * 3);
			var colours = new List<Color32>(n * 3);
			var indices = new List<int>((n - 1) * 12);
			float along = 0f;
			for (int i = 0; i < n; i++)
			{
				if (i > 0)
				{
					along += Vector2.Distance(line.Points[i - 1], line.Points[i]);
				}
				Vector2 forward = line.Points[Mathf.Min(n - 1, i + 1)] - line.Points[Mathf.Max(0, i - 1)];
				forward = forward.sqrMagnitude > 1e-8f ? forward.normalized : Vector2.up;
				var left = new Vector2(-forward.y, forward.x);
				// Near the scene it fills the trench cut for it, at its own level; further out it lies on the ground drawn.
				float centrePast = PastEdge(line.Points[i].x, line.Points[i].y);
				float cut = 1f - Mathf.Clamp01((centrePast - (CutMetres - CutFadeMetres)) / CutFadeMetres);
				float half = Mathf.Lerp(0.5f * line.Width[i], TrenchHalf(0.5f * line.Width[i], centrePast), cut);
				byte shallow = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(line.Depth[i] / 4f));
				byte column = (byte)Mathf.RoundToInt(255f * (1f - Mathf.Clamp01(line.Depth[i] / 8f)));
				for (int k = -1; k <= 1; k++)
				{
					Vector2 at = line.Points[i] + left * (half * k);
					float past = PastEdge(at.x, at.y);
					float drawn = surface(at.x, at.y);
					float draped = drawn + DrapeLift(past);
					float y = cut >= 0.999f ? line.Surface[i] : Mathf.Max(Mathf.Lerp(draped, line.Surface[i], cut), drawn + 0.1f);
					positions.Add(new Vector3(at.x, y, at.y));
					uvs.Add(new Vector2(along, half * k));
					flows.Add(forward * line.Speed[i]);
					states.Add(new Vector2(0f, 1f));
					tides.Add(Vector2.zero);
					colours.Add(new Color32(shallow, 255, column, 255));
				}
			}
			for (int i = 0; i + 1 < n; i++)
			{
				int row = i * 3, next = row + 3;
				for (int k = 0; k < 2; k++)
				{
					indices.Add(row + k); indices.Add(next + k); indices.Add(next + k + 1);
					indices.Add(row + k); indices.Add(next + k + 1); indices.Add(row + k + 1);
				}
			}
			return Finish($"Backdrop River {line.PlanetRiver}", positions, uvs, flows, states, tides, colours, indices);
		}

		/// <summary>
		/// How far over the ground drawn a river lying on it far out is lifted, metres: enough that the depth buffer
		/// keeps it in front of the ground at that distance, and no more. At 0.25 m and 2 mm a metre (2.25 m a
		/// kilometre out) the rivers past the edge read as sheets floating over the hills.
		/// </summary>
		public static float DrapeLift(float pastMetres) => 0.1f + 0.0008f * pastMetres;

		private static Mesh Finish(string name, List<Vector3> positions, List<Vector2> uvs, List<Vector2> flows, List<Vector2> states,
			List<Vector2> tides, List<Color32> colours, List<int> indices)
		{
			var mesh = new Mesh { name = name };
			mesh.indexFormat = positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			mesh.SetVertices(positions);
			mesh.SetUVs(0, uvs);
			mesh.SetUVs(1, flows);
			mesh.SetUVs(2, states);
			mesh.SetUVs(3, tides);
			mesh.SetColors(colours);
			mesh.SetTriangles(indices, 0);
			var normals = new Vector3[positions.Count];
			for (int i = 0; i < normals.Length; i++)
			{
				normals[i] = Vector3.up;
			}
			mesh.normals = normals;
			mesh.RecalculateBounds();
			return mesh;
		}
	}
}
#endif
