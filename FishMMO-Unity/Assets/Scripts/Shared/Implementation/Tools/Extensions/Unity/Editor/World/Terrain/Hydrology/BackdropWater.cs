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
		/// <summary>How far apart a river's rows lie, metres.</summary>
		public const float RowMetres = 20f;
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

		public readonly List<Line> Rivers = new List<Line>();

		private readonly float halfW, halfD, reach;
		private readonly Func<float, float, Vector3> lakeAt;
		private readonly Func<float, float, float> ground;
		private readonly Dictionary<long, List<(int line, int segment)>> hash = new Dictionary<long, List<(int, int)>>();
		private const float HashMetres = 100f;
		private float widest;

		private BackdropWater(float halfW, float halfD, float reach, Func<float, float, Vector3> lakeAt, Func<float, float, float> ground)
		{
			this.halfW = halfW;
			this.halfD = halfD;
			this.reach = reach;
			this.lakeAt = lakeAt;
			this.ground = ground;
		}

		/// <summary>True when there is no water past the edge to draw.</summary>
		public bool Empty { get; private set; } = true;

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
			RiverSettings settings = scene != null ? scene.Settings : new RiverSettings();
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

			var water = new BackdropWater(halfW, halfD, reach, LakeAt, ground) { Empty = levelOf.Count == 0 };

			// Rivers: each planet river's runs inside the backdrop and outside the scene, where water runs all year.
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
				var runX = new List<float>();
				var runZ = new List<float>();
				var runQ = new List<float>();
				bool startsAtScene = false;
				for (int i = 0; i <= n; i++)
				{
					bool keep = i < n && river.Discharge[i] >= perennial && InBackdrop(points[i], halfW, halfD, extentX, extentZ);
					if (keep)
					{
						if (runX.Count == 0 && i > 0 && InScene(points[i - 1], halfW, halfD))
						{
							// Starts where it leaves the scene: on the scene's edge.
							Vector2 edge = EdgeCrossing(points[i - 1], points[i], halfW, halfD);
							runX.Add(edge.x);
							runZ.Add(edge.y);
							runQ.Add(river.Discharge[i]);
							startsAtScene = true;
						}
						runX.Add(points[i].x);
						runZ.Add(points[i].y);
						runQ.Add(river.Discharge[i]);
						continue;
					}
					if (runX.Count > 0)
					{
						bool endsAtScene = false;
						if (i < n && InScene(points[i], halfW, halfD))
						{
							// Ends where it enters the scene.
							Vector2 edge = EdgeCrossing(points[i], points[i - 1], halfW, halfD);
							runX.Add(edge.x);
							runZ.Add(edge.y);
							runQ.Add(river.Discharge[i - 1]);
							endsAtScene = true;
						}
						water.AddRun(river.Id, runX, runZ, runQ, startsAtScene, endsAtScene, scene, ground, settings);
					}
					runX.Clear();
					runZ.Clear();
					runQ.Clear();
					startsAtScene = false;
				}
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

		private void AddRun(int planetRiver, List<float> x, List<float> z, List<float> q, bool startsAtScene, bool endsAtScene,
			SceneWater scene, Func<float, float, float> ground, RiverSettings settings)
		{
			if (x.Count < 2)
			{
				return;
			}
			var xs = new List<float>(x);
			var zs = new List<float>(z);
			var qs = new List<float>(q);
			RiverShaping.Chaikin(xs, zs, qs, 2);
			RiverShaping.Resample(xs, zs, qs, RowMetres);
			int n = xs.Count;
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
			for (int i = 0; i < n; i++)
			{
				line.Points[i] = new Vector2(xs[i], zs[i]);
				RiverShaping.Size(qs[i], 0.5f, settings, out line.Width[i], out line.Depth[i]);
				line.Speed[i] = qs[i] / Mathf.Max(0.05f, line.Width[i] * line.Depth[i] * (2f / 3f));
				widest = Mathf.Max(widest, line.Width[i]);
			}
			line.Width = RiverShaping.SmoothAlong(line.Width, 3);
			line.Depth = RiverShaping.SmoothAlong(line.Depth, 3);

			/* The water's surface: a little under the ground along the line, never rising downstream; where it
			 * leaves the scene, from the scene river's own level there, and where it enters, never under it. */
			float startLevel = startsAtScene ? EdgeLevel(scene, planetRiver, line.Points[0], true) : float.NaN;
			float endLevel = endsAtScene ? EdgeLevel(scene, planetRiver, line.Points[n - 1], false) : float.NaN;
			float running = float.IsNaN(startLevel) ? float.PositiveInfinity : startLevel;
			for (int i = 0; i < n; i++)
			{
				float level = ground(line.Points[i].x, line.Points[i].y) - settings.InsetMetres;
				running = Mathf.Min(running, level);
				line.Surface[i] = running;
			}
			if (!float.IsNaN(endLevel))
			{
				for (int i = 0; i < n; i++)
				{
					line.Surface[i] = Mathf.Max(line.Surface[i], endLevel);
				}
			}
			Rivers.Add(line);
			Empty = false;
		}

		/// <summary>
		/// The level of the scene's own river where it meets the edge at <paramref name="at"/>: its last point if it
		/// leaves the scene there (<paramref name="leaving"/>), its first if it comes in; NaN when the scene has none.
		/// </summary>
		private static float EdgeLevel(SceneWater scene, int planetRiver, Vector2 at, bool leaving)
		{
			if (scene == null)
			{
				return float.NaN;
			}
			float best = float.MaxValue, level = float.NaN;
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
				float d = (new Vector2(path.X[i], path.Z[i]) - at).sqrMagnitude;
				if (d < best)
				{
					best = d;
					level = path.Surface[i];
				}
			}
			return best < 200f * 200f ? level : float.NaN;
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
		/// bank down to the bed, banks cut back at <see cref="BankDegrees"/>, fading out toward <see cref="CutMetres"/>.
		/// </summary>
		public float Cut(float east, float north, float height)
		{
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
		/// The lakes past the scene on a <see cref="LakeCellMetres"/> grid: flooded from each lake's own cells across
		/// the ground lower than its level that joins them, within its cells and the ring round them, so the water
		/// stands wherever the ground is under the level and nowhere it is not (no holes where a planet cell's mean
		/// ground stood higher, no sheet hung over a valley falling away past the outlet).
		/// </summary>
		private Mesh LakeMesh()
		{
			float extentX = halfW + reach, extentZ = halfD + reach;
			int nx = Mathf.CeilToInt(2f * extentX / LakeCellMetres), nz = Mathf.CeilToInt(2f * extentZ / LakeCellMetres);
			int count = nx * nz;
			var level = new float[count];
			var lake = new int[count];
			var under = new bool[count];
			var wet = new bool[count];
			var queue = new Queue<int>();
			for (int z = 0; z < nz; z++)
			{
				for (int x = 0; x < nx; x++)
				{
					int i = z * nx + x;
					float cx = -extentX + (x + 0.5f) * LakeCellMetres, cz = -extentZ + (z + 0.5f) * LakeCellMetres;
					Vector3 at = lakeAt(cx, cz);
					level[i] = at.x;
					lake[i] = (int)at.y;
					if (float.IsNaN(at.x))
					{
						continue;
					}
					// Under the level anywhere in the cell: its centre and corners, so a shore cell is drawn and the ground cuts it.
					float h = Mathf.Min(ground(cx, cz), Mathf.Min(Mathf.Min(ground(cx - 0.5f * LakeCellMetres, cz - 0.5f * LakeCellMetres), ground(cx + 0.5f * LakeCellMetres, cz - 0.5f * LakeCellMetres)),
						Mathf.Min(ground(cx - 0.5f * LakeCellMetres, cz + 0.5f * LakeCellMetres), ground(cx + 0.5f * LakeCellMetres, cz + 0.5f * LakeCellMetres))));
					under[i] = h < at.x;
					if (under[i] && at.z > 0.5f && ground(cx, cz) < at.x)
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
					if (wet[j] || !under[j] || lake[j] != lake[i])
					{
						continue;
					}
					wet[j] = true;
					queue.Enqueue(j);
				}
			}
			var positions = new List<Vector3>();
			var indices = new List<int>();
			for (int z = 0; z < nz; z++)
			{
				float z0 = -extentZ + z * LakeCellMetres, z1 = z0 + LakeCellMetres;
				for (int x = 0; x < nx; x++)
				{
					int i = z * nx + x;
					float x0 = -extentX + x * LakeCellMetres, x1 = x0 + LakeCellMetres;
					// The scene's own lakes cover its ground (the flood still runs through it, to reach a lake's far side).
					if (!wet[i] || (x1 > -halfW && x0 < halfW && z1 > -halfD && z0 < halfD))
					{
						continue;
					}
					int b = positions.Count;
					positions.Add(new Vector3(x0, level[i], z0));
					positions.Add(new Vector3(x1, level[i], z0));
					positions.Add(new Vector3(x1, level[i], z1));
					positions.Add(new Vector3(x0, level[i], z1));
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
					float draped = drawn + 0.25f + 0.002f * past;
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
