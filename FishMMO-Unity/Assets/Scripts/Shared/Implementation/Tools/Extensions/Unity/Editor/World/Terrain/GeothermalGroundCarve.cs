#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The hot ground made ground at the cut (2026-10-18, phase I2q, decision 47): every geothermal site the client steams
	/// (GeothermalVents) dug or raised into the heightmap where it stands — a hot spring's funnel pool inside a beaded sinter
	/// rim, flooded to just under its brim; a geyser's sinter cone with its orifice; a mud pot's dish; a fumarole's lip — short
	/// runoff channels run down the ground from the springs and geysers, and the ground's HEAT baked into a mask
	/// (<see cref="BiomeHeatMask"/>) that ground layers and spawn rules band on.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The very sites the client steams.</b> The client finds them round its camera with GeothermalVents.Near: the scene's
	/// biome map, seeded from the map's name, each candidate's biome read from it. The cut runs the same search over the whole
	/// scene (<see cref="GeothermalVents.SceneSites"/>) on a map written from the same biome field under the same name, before the
	/// map asset itself is written, so every cone stands under its eruption and every pool under its steam. A site the client
	/// would not steam (under the sea or a lake) is neither dug nor heated; a site in a point of interest's footprint is heated but
	/// not dug (the site's pad stays level).
	/// </para>
	/// <para>
	/// <b>Carved once, from the ground as it was.</b> The cut runs this in the biome stage, after the sites' pads and before the
	/// splat and the scatter, so the ground layers and plants read the dug ground. What it moved is kept per tile sample as the
	/// terrain took it (<see cref="GeneratedGeothermalGround"/>), and the next repaint takes it out (<see cref="Restore"/>) before
	/// the biome field reads the ground, so the field, the sites and the carve come out exactly as the cut's: idempotent. Each
	/// site's target is read from the ground before any carve (its rim level the mean of eight samples round it), and the moves of
	/// overlapping sites add, in the sites' grid order.
	/// </para>
	/// <para>
	/// <b>Water.</b> The pools are lakes of the scene's hydrology in the paddies' id range (kind 7 of
	/// <see cref="PointOfInterestFieldTerraces.LakeId"/>'s bits), so the place stage's clearing of the paddies' water takes them
	/// out and <see cref="WriteWater"/> puts them back from the record each time it runs, and the cut's own water never reads them
	/// as natural. A scene with no hydrology asset (no river or lake at all) keeps its pools dry.
	/// </para>
	/// </remarks>
	public static class GeothermalGroundCarve
	{
		/// <summary>The lake kind a hot spring's pool takes in the paddies' id range (bits 24–26: 1 and 2 are the fields').</summary>
		public const int PoolLakeKind = 7;
		/// <summary>How far under its rim's level a pool's water stands, m.</summary>
		public const float PoolFreeboard = 0.05f;
		/// <summary>A runoff channel's depth and half width, m.</summary>
		public const float RunoffDepth = 0.2f, RunoffHalfWidth = 1f;
		/// <summary>The step a runoff channel is traced down the ground in, m, and how many headings it tries each step.</summary>
		public const float RunoffStep = 2f;
		public const int RunoffHeadings = 16;
		/// <summary>How far a runoff channel's heat reaches either side of it, m, and its heat at its head and its foot.</summary>
		public const float RunoffHeatReach = 3f, RunoffHeatHead = 0.75f, RunoffHeatFoot = 0.15f;

		/// <summary>A pool's lake id: the paddies' base, kind 7, the site's index among the scene's sites.</summary>
		public static int PoolLakeId(int siteIndex) => PointOfInterestPaddies.LakeIdBase | (PoolLakeKind << 24) | (siteIndex & 0xFFFFFF);

		/// <summary>True for a hot spring pool's lake id.</summary>
		public static bool IsPool(int id) => PointOfInterestPaddies.IsPaddy(id) && ((id >> 24) & 0x7) == PoolLakeKind && (id & (1 << 27)) == 0;

		// ── Pure ──────────────────────────────────────────────────────

		/// <summary>One site the cut digs: the site, its index among the scene's sites, and its rim's level (m).</summary>
		public struct Dug
		{
			public GeothermalSite Site;
			public int Index;
			public float Base;
		}

		/// <summary>What the cut does to a scene's hot ground (<see cref="Make"/>).</summary>
		public sealed class Plan
		{
			/// <summary>Every site of the scene, in grid order (GeothermalVents.SceneSites).</summary>
			public readonly List<GeothermalSite> Sites = new List<GeothermalSite>();
			/// <summary>The sites dug, in grid order.</summary>
			public readonly List<Dug> Dug = new List<Dug>();
			/// <summary>The runoff channels, each a line of points from its head down, and the site index it runs from.</summary>
			public readonly List<List<Vector2>> Runoff = new List<List<Vector2>>();
			public readonly List<int> RunoffFrom = new List<int>();
			/// <summary>The heat's sources: a point a heated site, a line a runoff stretch (append-only shapes: <see cref="BiomeHeatSource"/>).</summary>
			public readonly List<BiomeHeatSource> HeatSources = new List<BiomeHeatSource>();
			/// <summary>The Great Spout's index among <see cref="Sites"/>, −1 with no geyser.</summary>
			public int GreatSpout = -1;
			public BiomeHeatMask Heat = BiomeHeatMask.None;

			public bool Any => Sites.Count > 0;

			/// <summary>The dug sites and runoff stretches whose moves reach each <see cref="IndexCell"/> cell (built by <see cref="Make"/>).</summary>
			internal readonly Dictionary<long, List<int>> DugAt = new Dictionary<long, List<int>>();
			internal readonly Dictionary<long, List<(int Line, int Segment)>> RunoffAt = new Dictionary<long, List<(int, int)>>();

			internal static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

			internal void Index()
			{
				DugAt.Clear();
				RunoffAt.Clear();
				for (int d = 0; d < Dug.Count; d++)
				{
					GeothermalSite site = Dug[d].Site;
					float reach = CarveReach(site);
					Cover(site.Position.x - reach, site.Position.y - reach, site.Position.x + reach, site.Position.y + reach, key =>
					{
						if (!DugAt.TryGetValue(key, out List<int> list))
						{
							DugAt[key] = list = new List<int>();
						}
						list.Add(d);
					});
				}
				for (int l = 0; l < Runoff.Count; l++)
				{
					List<Vector2> line = Runoff[l];
					for (int i = 0; i + 1 < line.Count; i++)
					{
						Vector2 a = line[i], b = line[i + 1];
						int li = l, si = i;
						Cover(Mathf.Min(a.x, b.x) - RunoffHalfWidth, Mathf.Min(a.y, b.y) - RunoffHalfWidth, Mathf.Max(a.x, b.x) + RunoffHalfWidth, Mathf.Max(a.y, b.y) + RunoffHalfWidth, key =>
						{
							if (!RunoffAt.TryGetValue(key, out List<(int, int)> list))
							{
								RunoffAt[key] = list = new List<(int, int)>();
							}
							list.Add((li, si));
						});
					}
				}
			}

			private static void Cover(float x0, float z0, float x1, float z1, Action<long> add)
			{
				int i0 = Mathf.FloorToInt(x0 / IndexCell), i1 = Mathf.FloorToInt(x1 / IndexCell);
				int j0 = Mathf.FloorToInt(z0 / IndexCell), j1 = Mathf.FloorToInt(z1 / IndexCell);
				for (int j = j0; j <= j1; j++)
				{
					for (int i = i0; i <= i1; i++)
					{
						add(Key(i, j));
					}
				}
			}
		}

		/// <summary>The cell the plan's moves are indexed by, m.</summary>
		public const float IndexCell = 16f;

		/// <summary>How far round its middle a dug site moves the ground, m: its pool, cone or dish and its rim.</summary>
		public static float CarveRadius(in GeothermalSite site)
		{
			switch (site.Kind)
			{
				case GeothermalKind.Geyser: return GeothermalVents.ConeRadius(site);
				case GeothermalKind.Fumarole: return GeothermalVents.SourceRadius(site) + 1f;
				default: return GeothermalVents.SourceRadius(site);
			}
		}

		/// <summary>The whole reach of a dug site's moves, rim and all, m.</summary>
		public static float CarveReach(in GeothermalSite site) => CarveRadius(site) + GeothermalVents.RimWidth + 0.5f;

		/// <summary>A site's rim level: the mean ground at eight points on its carve radius, m (pure in the ground).</summary>
		public static float BaseLevel(in GeothermalSite site, Func<float, float, float> groundAt)
		{
			float r = CarveRadius(site), sum = 0f;
			for (int k = 0; k < 8; k++)
			{
				float a = k * Mathf.PI * 0.25f;
				sum += groundAt(site.Position.x + r * Mathf.Cos(a), site.Position.y + r * Mathf.Sin(a));
			}
			return sum / 8f;
		}

		/// <summary>
		/// What one dug site does to the ground at (x, z) whose height there is <paramref name="ground"/>, m (signed, 0 for nothing):
		/// a spring's funnel below its rim level and its sinter rim lip above it, a mud pot's dish and its spatter rim, a geyser's cone
		/// with its orifice dipped, a fumarole's low lip. Pure.
		/// </summary>
		public static float Offset(in Dug dug, float x, float z, float ground)
		{
			GeothermalSite site = dug.Site;
			float r = new Vector2(x - site.Position.x, z - site.Position.y).magnitude;
			float radius = CarveRadius(site);
			switch (site.Kind)
			{
				case GeothermalKind.HotSpring:
				case GeothermalKind.MudPot:
				{
					float depth = site.Kind == GeothermalKind.HotSpring ? GeothermalVents.PoolDepth(site) + PoolFreeboard : GeothermalVents.DishDepth(site);
					if (r < radius)
					{
						float s = r / radius;
						return Mathf.Min(ground, dug.Base - depth * (1f - s * s)) - ground;
					}
					float lip = site.Kind == GeothermalKind.HotSpring ? GeothermalVents.RimMetres : 0.6f * GeothermalVents.RimMetres;
					float across = (r - radius) / GeothermalVents.RimWidth;
					if (across < 1f)
					{
						float bump = Mathf.Sin(Mathf.PI * Mathf.Clamp01(across) * 0.5f + Mathf.PI * 0.5f);
						return Mathf.Max(ground, dug.Base + lip * Mathf.Max(0f, bump)) - ground;
					}
					return 0f;
				}
				case GeothermalKind.Geyser:
				{
					if (r >= radius)
					{
						return 0f;
					}
					float height = GeothermalVents.ConeMetres(site);
					float t = r / radius;
					float cone = dug.Base + height * (1f - t * t * (3f - 2f * t));
					float orifice = Mathf.Max(0.6f, 0.5f * GeothermalVents.SourceRadius(site));
					if (r < orifice)
					{
						cone -= 0.3f * height * (1f - r / orifice);
					}
					return Mathf.Max(ground, cone) - ground;
				}
				default:
				{
					if (r >= radius)
					{
						return 0f;
					}
					return Mathf.Max(ground, dug.Base + 0.3f * (1f - r / radius)) - ground;
				}
			}
		}

		/// <summary>A runoff channel's move at a distance from its line, m (≤ 0): a shallow rounded trench.</summary>
		public static float RunoffOffset(float distance)
		{
			if (!(distance < RunoffHalfWidth))
			{
				return 0f;
			}
			float t = Mathf.Clamp01(distance / RunoffHalfWidth);
			return -RunoffDepth * (1f - t * t * (3f - 2f * t));
		}

		/// <summary>How long a site's runoff runs at most, m: a spring's further than a geyser's; none from a fumarole or a mud pot.</summary>
		public static float RunoffLength(in GeothermalSite site)
		{
			float u = Mathf.Clamp01(site.Size);
			switch (site.Kind)
			{
				case GeothermalKind.HotSpring: return 25f + 100f * u;
				case GeothermalKind.Geyser: return 15f + 60f * u;
				default: return 0f;
			}
		}

		/// <summary>
		/// A site's runoff, traced down the ground (pure): from the lowest of <see cref="RunoffHeadings"/> points just past its rim,
		/// step by <see cref="RunoffStep"/> to the lowest of the headings ahead (never doubling back) while the ground falls, up to
		/// <see cref="RunoffLength"/>, stopping in water, off the scene or in a site's footprint. Fewer than two points: none.
		/// </summary>
		public static List<Vector2> TraceRunoff(in GeothermalSite site, Func<float, float, float> groundAt, Func<float, float, bool> stop, Rect scene)
		{
			var line = new List<Vector2>();
			float length = RunoffLength(site);
			if (!(length > 0f) || groundAt == null)
			{
				return line;
			}
			float start = CarveRadius(site) + GeothermalVents.RimWidth + 1f;
			Vector2 head = site.Position;
			float lowest = float.MaxValue;
			for (int k = 0; k < RunoffHeadings; k++)
			{
				float a = k * Mathf.PI * 2f / RunoffHeadings;
				var p = site.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * start;
				float g = groundAt(p.x, p.y);
				if (g < lowest)
				{
					lowest = g;
					head = p;
				}
			}
			if (!scene.Contains(head) || (stop != null && stop(head.x, head.y)))
			{
				return line;
			}
			line.Add(head);
			Vector2 heading = (head - site.Position).normalized;
			Vector2 at = head;
			float here = lowest, run = 0f;
			while (run + RunoffStep <= length)
			{
				Vector2 best = at;
				float bestGround = here - 0.01f;
				bool found = false;
				for (int k = 0; k < RunoffHeadings; k++)
				{
					float a = k * Mathf.PI * 2f / RunoffHeadings;
					var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
					if (Vector2.Dot(dir, heading) < -0.3f)
					{
						continue;
					}
					Vector2 p = at + dir * RunoffStep;
					float g = groundAt(p.x, p.y);
					if (g < bestGround)
					{
						bestGround = g;
						best = p;
						found = true;
					}
				}
				if (!found || !scene.Contains(best) || (stop != null && stop(best.x, best.y)))
				{
					break;
				}
				heading = (best - at).normalized;
				at = best;
				here = bestGround;
				run += RunoffStep;
				line.Add(at);
			}
			if (line.Count < 2)
			{
				line.Clear();
			}
			return line;
		}

		/// <summary>
		/// The plan for a scene's hot ground (pure in its arguments): of <paramref name="sites"/> (every site, GeothermalVents.SceneSites),
		/// those not standing in water (<paramref name="wet"/>, the client's own rule) are heated, and of those the ones outside a site
		/// footprint (<paramref name="excluded"/>) dug, each at its rim level on <paramref name="groundAt"/>; springs and geysers get
		/// runoff; the Great Spout is found; the heat is baked over <paramref name="scene"/>.
		/// </summary>
		public static Plan Make(IReadOnlyList<GeothermalSite> sites, Func<float, float, float> groundAt, Func<float, float, bool> wet,
			Func<float, float, bool> excluded, Rect scene)
		{
			var plan = new Plan();
			if (sites == null)
			{
				return plan;
			}
			plan.Sites.AddRange(sites);
			plan.GreatSpout = GeothermalVents.GreatSpoutIndex(plan.Sites);
			bool Stops(float x, float z) => (wet != null && wet(x, z)) || (excluded != null && excluded(x, z));
			for (int i = 0; i < plan.Sites.Count; i++)
			{
				GeothermalSite site = plan.Sites[i];
				if (wet != null && wet(site.Position.x, site.Position.y))
				{
					continue;
				}
				plan.HeatSources.Add(BiomeHeatSource.Point(site.Position, GeothermalVents.HeatPeak(site), GeothermalVents.HeatReach(site)));
				if (excluded != null && excluded(site.Position.x, site.Position.y))
				{
					continue;
				}
				plan.Dug.Add(new Dug { Site = site, Index = i, Base = BaseLevel(site, groundAt) });
				List<Vector2> runoff = TraceRunoff(site, groundAt, Stops, scene);
				if (runoff.Count >= 2)
				{
					plan.Runoff.Add(runoff);
					plan.RunoffFrom.Add(i);
					float total = (runoff.Count - 1) * RunoffStep, along = 0f;
					for (int p = 0; p + 1 < runoff.Count; p++)
					{
						float a = Mathf.Lerp(RunoffHeatHead, RunoffHeatFoot, along / total);
						along += RunoffStep;
						float b = Mathf.Lerp(RunoffHeatHead, RunoffHeatFoot, along / total);
						plan.HeatSources.Add(BiomeHeatSource.Line(runoff[p], runoff[p + 1], a, b, RunoffHeatReach));
					}
				}
			}
			plan.Heat = BiomeHeatMask.Bake(plan.HeatSources, scene);
			plan.Index();
			return plan;
		}

		/// <summary>
		/// The ground's move at (x, z) for a whole plan, ground there <paramref name="ground"/> m (pure): every dug site's move whose
		/// reach holds the point, in grid order, plus every runoff line's trench (the deepest of them) away from the dug sites.
		/// </summary>
		public static float PlanOffset(Plan plan, float x, float z, float ground)
		{
			float move = 0f;
			var p = new Vector2(x, z);
			bool inSite = false;
			long key = Plan.Key(Mathf.FloorToInt(x / IndexCell), Mathf.FloorToInt(z / IndexCell));
			if (plan.DugAt.TryGetValue(key, out List<int> dugs))
			{
				// In grid order (the index lists them so), so overlapping moves add in the same order on every run.
				foreach (int d in dugs)
				{
					Dug dug = plan.Dug[d];
					float reach = CarveReach(dug.Site);
					if ((dug.Site.Position - p).sqrMagnitude < reach * reach)
					{
						move += Offset(dug, x, z, ground);
						inSite = true;
					}
				}
			}
			if (!inSite && plan.RunoffAt.TryGetValue(key, out List<(int Line, int Segment)> stretches))
			{
				float trench = 0f;
				foreach ((int l, int i) in stretches)
				{
					Vector2 a = plan.Runoff[l][i], b = plan.Runoff[l][i + 1];
					Vector2 ab = b - a;
					float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					trench = Mathf.Min(trench, RunoffOffset((p - (a + ab * t)).magnitude));
				}
				move += trench;
			}
			return move;
		}

		/// <summary>The world rectangles a plan moves: every dug site's reach and every runoff line's box.</summary>
		public static List<Rect> CarveAreas(Plan plan)
		{
			var areas = new List<Rect>();
			foreach (Dug dug in plan.Dug)
			{
				float reach = CarveReach(dug.Site);
				areas.Add(Rect.MinMaxRect(dug.Site.Position.x - reach, dug.Site.Position.y - reach, dug.Site.Position.x + reach, dug.Site.Position.y + reach));
			}
			foreach (List<Vector2> line in plan.Runoff)
			{
				float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
				foreach (Vector2 v in line)
				{
					x0 = Mathf.Min(x0, v.x);
					z0 = Mathf.Min(z0, v.y);
					x1 = Mathf.Max(x1, v.x);
					z1 = Mathf.Max(z1, v.y);
				}
				areas.Add(Rect.MinMaxRect(x0 - RunoffHalfWidth, z0 - RunoffHalfWidth, x1 + RunoffHalfWidth, z1 + RunoffHalfWidth));
			}
			return areas;
		}

		/// <summary>
		/// A hot spring's pool as a lake of the scene's hydrology (pure): every one-metre cell, on a lattice fixed to the scene's
		/// metres, whose middle lies inside the funnel, at the rim level less <see cref="PoolFreeboard"/>; never frozen. Null for
		/// any other kind.
		/// </summary>
		public static SceneHydrology.Lake PoolLake(in Dug dug)
		{
			if (dug.Site.Kind != GeothermalKind.HotSpring)
			{
				return null;
			}
			const float cell = 1f;
			float radius = CarveRadius(dug.Site) * 0.98f;
			Vector2 c = dug.Site.Position;
			float x0 = Mathf.Floor((c.x - radius - cell) / cell) * cell, z0 = Mathf.Floor((c.y - radius - cell) / cell) * cell;
			int width = Mathf.CeilToInt((c.x + radius + cell - x0) / cell), height = Mathf.CeilToInt((c.y + radius + cell - z0) / cell);
			var bits = new byte[(width * height + 7) / 8];
			int covered = 0;
			Vector2 seed = c;
			float best = float.MaxValue;
			for (int z = 0; z < height; z++)
			{
				for (int x = 0; x < width; x++)
				{
					var p = new Vector2(x0 + (x + 0.5f) * cell, z0 + (z + 0.5f) * cell);
					float d = (p - c).sqrMagnitude;
					if (d >= radius * radius)
					{
						continue;
					}
					int bit = z * width + x;
					bits[bit >> 3] |= (byte)(1 << (bit & 7));
					covered++;
					if (d < best)
					{
						best = d;
						seed = p;
					}
				}
			}
			if (covered == 0)
			{
				return null;
			}
			float level = dug.Base - PoolFreeboard;
			return new SceneHydrology.Lake
			{
				Id = PoolLakeId(dug.Index),
				PlanetLake = -1,
				Level = level,
				SpillLevel = level,
				Floor = level - SceneHydrology.SurfacePoolFloorDepth,
				Frozen = false,
				Seeds = new[] { seed },
				Bounds = new Rect(x0, z0, width * cell, height * cell),
				Mask = bits,
				MaskOrigin = new Vector2(x0, z0),
				MaskCell = cell,
				MaskWidth = width,
				MaskHeight = height,
			};
		}

		// ── The scene ─────────────────────────────────────────────────

		/// <summary>
		/// Plans, carves and records a scene's hot ground (the cut's biome stage and every repaint): the sites of the map
		/// <paramref name="field"/> is about to be written as, under the name <paramref name="mapName"/> (so the seed is the
		/// client's), dug into <paramref name="terrains"/> from the ground as it stands. Returns the plan (its heat for the splat,
		/// the scatter and the map). Call <see cref="Restore"/> first when an earlier run's carve may be in the ground.
		/// </summary>
		public static Plan Lay(Scene scene, Terrain[,] terrains, TerrainTilePlan tiles, SceneBiomeField field, string mapName,
			Func<float, float, bool> wet, Func<float, float, bool> excluded, List<string> notes)
		{
			var sites = new List<GeothermalSite>();
			// The map the client will read, written from the same field under the same name: the client's search, the client's answer.
			var map = ScriptableObject.CreateInstance<SceneBiomeMap>();
			try
			{
				map.name = mapName;
				field.WriteTo(map);
				GeothermalVents.SceneSites(map, sites);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(map);
			}
			Func<float, float, float> groundAt = (east, north) => SceneGenerator.GroundAltitude(terrains, tiles, east, north);
			var sceneRect = new Rect(-tiles.WidthMetres * 0.5f, -tiles.DepthMetres * 0.5f, tiles.WidthMetres, tiles.DepthMetres);
			Plan plan = Make(sites, groundAt, wet, excluded, sceneRect);
			if (!plan.Any)
			{
				return plan;
			}

			GeneratedGeothermalGround record = RecordOf(scene);
			int moved = Carve(plan, terrains, record);
			record.Sites = plan.Sites.Count;
			record.Carved = plan.Dug.Count;
			record.Runoffs = plan.Runoff.Count;
			record.Pools.Clear();
			foreach (Dug dug in plan.Dug)
			{
				SceneHydrology.Lake pool = PoolLake(dug);
				if (pool != null)
				{
					record.Pools.Add(pool);
				}
			}
			record.HasGreatSpout = plan.GreatSpout >= 0;
			if (record.HasGreatSpout)
			{
				GeothermalSite spout = plan.Sites[plan.GreatSpout];
				record.GreatSpoutPosition = spout.Position;
				record.GreatSpoutSeed = spout.Seed;
				record.GreatSpoutSize = spout.Size;
			}
			EditorUtility.SetDirty(record);
			int[] kinds = new int[4];
			foreach (GeothermalSite site in plan.Sites)
			{
				if ((int)site.Kind < kinds.Length)
				{
					kinds[(int)site.Kind]++;
				}
			}
			notes?.Add($"Hot ground: {plan.Sites.Count} geothermal site(s) ({kinds[(int)GeothermalKind.Geyser]} geysers, {kinds[(int)GeothermalKind.HotSpring]} hot springs, " +
				$"{kinds[(int)GeothermalKind.Fumarole]} fumaroles, {kinds[(int)GeothermalKind.MudPot]} mud pots), {plan.Dug.Count} dug ({moved:N0} heightmap sample(s)), " +
				$"{record.Pools.Count} pool(s), {plan.Runoff.Count} runoff channel(s); heat baked {plan.Heat.Width}×{plan.Heat.Height}" +
				(record.HasGreatSpout ? $"; the Great Spout at ({record.GreatSpoutPosition.x:0}, {record.GreatSpoutPosition.y:0})." : "."));
			return plan;
		}

		/// <summary>Moves every tile's ground by the plan, recording what the terrain took. Returns the samples moved.</summary>
		private static int Carve(Plan plan, Terrain[,] terrains, GeneratedGeothermalGround record)
		{
			List<Rect> areas = CarveAreas(plan);
			if (areas.Count == 0 || terrains == null)
			{
				return 0;
			}
			Rect all = areas[0];
			foreach (Rect a in areas)
			{
				all = Rect.MinMaxRect(Mathf.Min(all.xMin, a.xMin), Mathf.Min(all.yMin, a.yMin), Mathf.Max(all.xMax, a.xMax), Mathf.Max(all.yMax, a.yMax));
			}
			var samples = new List<int>();
			var metres = new List<float>();
			int moved = 0;
			for (int tz = 0; tz < terrains.GetLength(1); tz++)
			{
				for (int tx = 0; tx < terrains.GetLength(0); tx++)
				{
					Terrain terrain = terrains[tx, tz];
					TerrainData data = terrain != null ? terrain.terrainData : null;
					if (data == null)
					{
						continue;
					}
					int res = data.heightmapResolution;
					Vector3 origin = terrain.transform.position, size = data.size;
					var tile = new Rect(origin.x, origin.z, size.x, size.z);
					if (!tile.Overlaps(all))
					{
						continue;
					}
					float[,] whole = data.GetHeights(0, 0, res, res);
					Func<float, float, float, float> offset = (x, z, g) => PlanOffset(plan, x, z, g);
					if (PointOfInterestFieldCarve.PlanTo(all, new Vector2(origin.x, origin.z), new Vector2(size.x, size.z), res,
						(i, j) => origin.y + whole[j, i] * size.y, offset, samples, metres) == 0)
					{
						continue;
					}
					PointOfInterestFieldCarve.Bounds(samples, res, out int x0, out int z0, out int w, out int d);
					float[,] before = data.GetHeights(x0, z0, w, d);
					float[,] heights = (float[,])before.Clone();
					PointOfInterestFieldCarve.Raise(heights, x0, z0, res, samples, metres, size.y);
					data.SetHeights(x0, z0, heights);
					float[,] after = data.GetHeights(x0, z0, w, d);
					EditorUtility.SetDirty(data);
					record.Edits.Add(new GeneratedFieldCarve.TileEdit
					{
						TileX = tx,
						TileZ = tz,
						Resolution = res,
						Samples = new List<int>(samples),
						Deltas = PointOfInterestFieldCarve.Deltas(before, after, x0, z0, res, samples),
					});
					moved += samples.Count;
				}
			}
			return moved;
		}

		/// <summary>
		/// Takes the last run's carve out of the ground (newest first) and drops its record, so the ground is as the cut found it
		/// before the hot ground was dug. Returns the samples restored. A record naming a missing tile or another resolution is
		/// dropped with a note (that tile keeps its carve until the scene is cut again).
		/// </summary>
		public static int Restore(Scene scene, Terrain[,] terrains, List<string> notes)
		{
			if (!scene.IsValid() || terrains == null)
			{
				return 0;
			}
			var records = new List<GeneratedGeothermalGround>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				records.AddRange(root.GetComponentsInChildren<GeneratedGeothermalGround>(true));
			}
			int restored = 0, skipped = 0;
			for (int r = records.Count - 1; r >= 0; r--)
			{
				GeneratedGeothermalGround record = records[r];
				for (int e = record.Edits.Count - 1; e >= 0; e--)
				{
					GeneratedFieldCarve.TileEdit edit = record.Edits[e];
					Terrain terrain = edit.TileX >= 0 && edit.TileZ >= 0 && edit.TileX < terrains.GetLength(0) && edit.TileZ < terrains.GetLength(1)
						? terrains[edit.TileX, edit.TileZ] : null;
					TerrainData data = terrain != null ? terrain.terrainData : null;
					if (data == null || data.heightmapResolution != edit.Resolution || edit.Samples.Count != edit.Deltas.Count || edit.Samples.Count == 0)
					{
						skipped += edit.Samples.Count;
						continue;
					}
					int res = data.heightmapResolution;
					PointOfInterestFieldCarve.Bounds(edit.Samples, res, out int x0, out int z0, out int w, out int d);
					if (x0 + w > res || z0 + d > res)
					{
						skipped += edit.Samples.Count;
						continue;
					}
					float[,] heights = data.GetHeights(x0, z0, w, d);
					PointOfInterestFieldCarve.Lower(heights, x0, z0, res, edit.Samples, edit.Deltas);
					data.SetHeights(x0, z0, heights);
					EditorUtility.SetDirty(data);
					restored += edit.Samples.Count;
				}
				UnityEngine.Object.DestroyImmediate(record.gameObject);
			}
			if (restored > 0)
			{
				notes?.Add($"Hot ground: the last run's pools, cones and runoff taken out of {restored:N0} heightmap sample(s) before anything read the ground (dug again below).");
			}
			if (skipped > 0)
			{
				notes?.Add($"Hot ground: {skipped:N0} recorded sample(s) name a tile that is gone or of another resolution and were left as they are; re-cut the scene to clear them.");
			}
			return restored;
		}

		/// <summary>
		/// Writes the scene's hot spring pools (from its record) into its hydrology asset, replacing any pool lake there: the place
		/// stage calls it after clearing the paddies' water, so the pools stand again on every cut, repaint and Regenerate POIs.
		/// Returns how many were written; none without a record or a hydrology asset.
		/// </summary>
		public static int WriteWater(Scene scene, string terrainFolder, string sceneName, List<string> notes)
		{
			if (!scene.IsValid() || string.IsNullOrEmpty(terrainFolder) || string.IsNullOrEmpty(sceneName))
			{
				return 0;
			}
			GeneratedGeothermalGround record = null;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				record ??= root.GetComponentInChildren<GeneratedGeothermalGround>(true);
			}
			if (record == null || record.Pools.Count == 0)
			{
				return 0;
			}
			var hydrology = AssetDatabase.LoadAssetAtPath<SceneHydrology>(SceneGenerator.HydrologyPath(terrainFolder, sceneName));
			if (hydrology == null)
			{
				notes?.Add($"Hot ground: {record.Pools.Count} hot spring pool(s) dug but left dry: the scene has no hydrology asset (no river or lake) to hold their water.");
				return 0;
			}
			hydrology.Lakes.RemoveAll(l => l != null && IsPool(l.Id));
			hydrology.Lakes.AddRange(record.Pools);
			EditorUtility.SetDirty(hydrology);
			AssetDatabase.SaveAssetIfDirty(hydrology);
			return record.Pools.Count;
		}

		/// <summary>The scene's record, a new editor-only root object (an earlier one is dropped by <see cref="Restore"/>).</summary>
		private static GeneratedGeothermalGround RecordOf(Scene scene)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name == GeneratedGeothermalGround.ObjectName && root.TryGetComponent(out GeneratedGeothermalGround found))
				{
					return found;
				}
			}
			var host = new GameObject(GeneratedGeothermalGround.ObjectName) { tag = "EditorOnly" };
			SceneManager.MoveGameObjectToScene(host, scene);
			return host.AddComponent<GeneratedGeothermalGround>();
		}
	}
}
#endif
