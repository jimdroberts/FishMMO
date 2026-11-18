#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The plants floating on a scene's still fresh water (2026-10-18, phase I2i, decision 27): duckweed carpets, water-lilies,
	/// water hyacinth and frogbit on its lakes, ponds, moats and the slack pools of its rivers, each biome's own
	/// (BiomeTemplate.FloatingPlants), laid as baked props at the water's surface.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where.</b> Still water only: every lake the scene's water holds (natural lakes and ponds, and the moats, MoatShaper's)
	/// that is open (not frozen), stands over the sea's level (a plant under the sea's lowest tide is folded away by the
	/// vegetation shader, FishSeaCeiling) and is not a grotto's pool (in a cave); and every river's Pool reaches, between its
	/// banks. A fast river carries none. Not where a way crosses (a drawbridge, a bridge's deck: the ways' trodden width) nor
	/// round a river's boulders.
	/// </para>
	/// <para>
	/// <b>How thickly.</b> On a lattice <see cref="CellMetres"/> apart, fixed to the scene's own metres, each point draws its
	/// plant from its biome's list by weight and keeps it by that plant's cover there (<see cref="BiomeFloatingPlants.Cover"/>:
	/// the water's depth over the bed, the point's distance from the shore, how sheltered the body is), broken into patches by
	/// a slow value noise. Every draw is a hash of the point and the scene's seed, never a running sequence, so a point's plant
	/// is the same whatever else changed, and a re-cut or a repaint lays exactly the same.
	/// </para>
	/// <para>
	/// <b>When.</b> After the scene's water is measured on the finished ground and saved (SceneGenerator.SaveWater: WaterSettle
	/// may lower a lake or a moat to its rim), so every plant lies on the surface the inland water then draws. A repaint lays them
	/// again from the water it saved. Never a collider: they are swum and waded through.
	/// </para>
	/// </remarks>
	public static class FloatingPlants
	{
		/// <summary>The source the plants are baked under (<see cref="ScenePropBaker"/>).</summary>
		public const string PropSource = "Floating Plants";

		/// <summary>How far apart the points a plant may stand on are, metres.</summary>
		public const float CellMetres = 2f;

		/// <summary>How far apart the patchiness noise's lattice points are, metres.</summary>
		public const float PatchMetres = 14f;

		/// <summary>The most plants one scene takes.</summary>
		public const int MaxPlants = 30000;

		/// <summary>How far over the sea's level still water must stand to carry plants, metres.</summary>
		public const float SeaClearMetres = 0.05f;

		/// <summary>How far clear of a river boulder's side a plant keeps, metres.</summary>
		public const float BoulderClearMetres = 0.5f;

		/// <summary>One body of still water, as a mask of points (pure).</summary>
		public sealed class StillWater
		{
			public int Id;
			/// <summary>The water's surface, metres.</summary>
			public float Level;
			/// <summary>The body's own shelter over its open water (<see cref="BiomeFloatingPlants.LakeShelter"/>; a moat's is 1).</summary>
			public float Shelter = 1f;
			/// <summary>The mask: <see cref="Width"/> × <see cref="Depth"/> points <see cref="Spacing"/> apart from <see cref="Origin"/> (the first point's position).</summary>
			public int Width, Depth;
			public float Spacing = 4f;
			public Vector2 Origin;
			public bool[] Wet;

			/// <summary>The mask point nearest a position, or −1 outside the mask.</summary>
			public int PointAt(float x, float z)
			{
				int i = Mathf.RoundToInt((x - Origin.x) / Spacing), j = Mathf.RoundToInt((z - Origin.y) / Spacing);
				return i < 0 || j < 0 || i >= Width || j >= Depth ? -1 : j * Width + i;
			}

			/// <summary>How much water the mask holds, square metres.</summary>
			public float Area
			{
				get
				{
					int n = 0;
					if (Wet != null)
					{
						foreach (bool w in Wet)
						{
							n += w ? 1 : 0;
						}
					}
					return n * Spacing * Spacing;
				}
			}
		}

		/// <summary>One plant laid: its kind, its origin on the water, its turn (degrees) and its size as a share of its mesh.</summary>
		public struct Plant
		{
			public BiomeFloatingPlantKind Kind;
			public Vector3 Position;
			public float Yaw;
			public float Scale;
		}

		// ── Pure ──────────────────────────────────────────────────────

		/// <summary>A well-mixed hash of a seed, two lattice coordinates and a salt.</summary>
		public static uint Hash(uint seed, int a, int b, int salt)
		{
			unchecked
			{
				uint h = seed ^ 0x9E3779B9u;
				h ^= (uint)a * 0x85EBCA6Bu;
				h = (h << 13) | (h >> 19);
				h ^= (uint)b * 0xC2B2AE35u;
				h = (h << 11) | (h >> 21);
				h ^= (uint)salt * 0x27D4EB2Fu;
				h ^= h >> 16;
				h *= 0x7FEB352Du;
				h ^= h >> 15;
				h *= 0x846CA68Bu;
				h ^= h >> 16;
				return h;
			}
		}

		/// <summary>A hash as a number in [0, 1).</summary>
		public static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

		/// <summary>
		/// How much a patch at (x, z) is let grow, about 0.25 … 1.35 (pure): a value noise on a <see cref="PatchMetres"/> lattice,
		/// so plants gather in rafts and leave lanes of open water between them.
		/// </summary>
		public static float Patch(uint seed, float x, float z)
		{
			float gx = x / PatchMetres, gz = z / PatchMetres;
			int x0 = Mathf.FloorToInt(gx), z0 = Mathf.FloorToInt(gz);
			float fx = gx - x0, fz = gz - z0;
			fx = fx * fx * (3f - 2f * fx);
			fz = fz * fz * (3f - 2f * fz);
			float a = Unit(Hash(seed, x0, z0, 0x5A7C)), b = Unit(Hash(seed, x0 + 1, z0, 0x5A7C));
			float c = Unit(Hash(seed, x0, z0 + 1, 0x5A7C)), d = Unit(Hash(seed, x0 + 1, z0 + 1, 0x5A7C));
			float n = Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
			return 0.25f + 1.1f * n;
		}

		/// <summary>
		/// Each wet point's distance to the nearest dry one (or the mask's edge), metres (pure): a two-pass chamfer over the mask,
		/// less half a spacing, so a point beside the shore is about half a spacing out. Dry points are 0.
		/// </summary>
		public static float[] ShoreDistances(bool[] wet, int width, int depth, float spacing)
		{
			var d = new float[width * depth];
			const float Far = 1e9f;
			float straight = spacing, diagonal = spacing * 1.41421356f;
			for (int j = 0; j < depth; j++)
			{
				for (int i = 0; i < width; i++)
				{
					int k = j * width + i;
					// The mask's own edge counts as shore: what lies past it is not this body's water.
					bool edge = i == 0 || j == 0 || i == width - 1 || j == depth - 1;
					d[k] = !wet[k] ? 0f : edge ? spacing : Far;
				}
			}
			for (int j = 0; j < depth; j++)
			{
				for (int i = 0; i < width; i++)
				{
					int k = j * width + i;
					if (d[k] <= 0f)
					{
						continue;
					}
					float best = d[k];
					if (i > 0) best = Mathf.Min(best, d[k - 1] + straight);
					if (j > 0) best = Mathf.Min(best, d[k - width] + straight);
					if (i > 0 && j > 0) best = Mathf.Min(best, d[k - width - 1] + diagonal);
					if (i < width - 1 && j > 0) best = Mathf.Min(best, d[k - width + 1] + diagonal);
					d[k] = best;
				}
			}
			for (int j = depth - 1; j >= 0; j--)
			{
				for (int i = width - 1; i >= 0; i--)
				{
					int k = j * width + i;
					if (d[k] <= 0f)
					{
						continue;
					}
					float best = d[k];
					if (i < width - 1) best = Mathf.Min(best, d[k + 1] + straight);
					if (j < depth - 1) best = Mathf.Min(best, d[k + width] + straight);
					if (i < width - 1 && j < depth - 1) best = Mathf.Min(best, d[k + width + 1] + diagonal);
					if (i > 0 && j < depth - 1) best = Mathf.Min(best, d[k + width - 1] + diagonal);
					d[k] = best;
				}
			}
			for (int k = 0; k < d.Length; k++)
			{
				if (d[k] > 0f)
				{
					d[k] = Mathf.Max(0.25f * spacing, d[k] - 0.5f * spacing);
				}
			}
			return d;
		}

		/// <summary>
		/// The plant one lattice point carries, if any (pure): its kind drawn from <paramref name="plants"/> by weight, kept by its
		/// cover at that depth, shore distance and shelter times the patch noise, turned and sized by the point's own hashes.
		/// </summary>
		public static bool TryPlant(uint seed, int i, int j, float x, float z, float level, float depth, float shore, float bodyShelter,
			IReadOnlyList<BiomeFloatingPlant> plants, out Plant plant)
		{
			plant = default;
			if (!BiomeFloatingPlants.TryPick(plants, Unit(Hash(seed, i, j, 1)), out BiomeFloatingPlantKind kind))
			{
				return false;
			}
			float cover = BiomeFloatingPlants.Cover(kind, depth, shore, BiomeFloatingPlants.Shelter(bodyShelter, shore));
			if (!(cover > 0f) || Unit(Hash(seed, i, j, 2)) >= Mathf.Min(1f, cover * Patch(seed ^ (uint)kind * 0x1F123BB5u, x, z)))
			{
				return false;
			}
			Vector2 range = BiomeFloatingPlants.ScaleRange(kind);
			plant = new Plant
			{
				Kind = kind,
				Position = new Vector3(x, level, z),
				Yaw = Unit(Hash(seed, i, j, 3)) * 360f,
				Scale = Mathf.Lerp(range.x, range.y, Unit(Hash(seed, i, j, 4))),
			};
			return true;
		}

		/// <summary>
		/// The plants on one body of still water (pure), appended to <paramref name="into"/> until it holds <paramref name="max"/>:
		/// one chance a lattice point (<see cref="CellMetres"/> apart on the scene's own metres, a little jittered within its cell)
		/// over the body's wet mask. <paramref name="ground"/> gives the bed (depth = level − bed), <paramref name="plantsAt"/> the
		/// biome's list at a point (null or empty: none), <paramref name="excluded"/> the points kept clear (may be null).
		/// </summary>
		public static int PlanBody(StillWater body, Func<float, float, float> ground, Func<float, float, IReadOnlyList<BiomeFloatingPlant>> plantsAt,
			Func<float, float, bool> excluded, uint seed, List<Plant> into, int max = MaxPlants)
		{
			if (body == null || body.Wet == null || body.Width <= 0 || body.Depth <= 0 || ground == null || plantsAt == null)
			{
				return 0;
			}
			float[] shore = ShoreDistances(body.Wet, body.Width, body.Depth, body.Spacing);
			float half = 0.5f * body.Spacing;
			float minX = body.Origin.x - half, minZ = body.Origin.y - half;
			float maxX = body.Origin.x + (body.Width - 0.5f) * body.Spacing, maxZ = body.Origin.y + (body.Depth - 0.5f) * body.Spacing;
			int i0 = Mathf.CeilToInt(minX / CellMetres), i1 = Mathf.FloorToInt(maxX / CellMetres);
			int j0 = Mathf.CeilToInt(minZ / CellMetres), j1 = Mathf.FloorToInt(maxZ / CellMetres);
			uint bodySeed = seed;
			int laid = 0;
			for (int j = j0; j <= j1 && into.Count < max; j++)
			{
				for (int i = i0; i <= i1 && into.Count < max; i++)
				{
					float x = (i + 0.4f * (Unit(Hash(bodySeed, i, j, 5)) * 2f - 1f)) * CellMetres;
					float z = (j + 0.4f * (Unit(Hash(bodySeed, i, j, 6)) * 2f - 1f)) * CellMetres;
					int k = body.PointAt(x, z);
					if (k < 0 || !body.Wet[k] || excluded != null && excluded(x, z))
					{
						continue;
					}
					IReadOnlyList<BiomeFloatingPlant> plants = plantsAt(x, z);
					if (plants == null || plants.Count == 0)
					{
						continue;
					}
					float depth = body.Level - ground(x, z);
					if (TryPlant(bodySeed, i, j, x, z, body.Level, depth, shore[k], body.Shelter, plants, out Plant plant))
					{
						into.Add(plant);
						laid++;
					}
				}
			}
			return laid;
		}

		/// <summary>
		/// The plants on a river's slack pools (pure): along every stretch between two points whose reach is
		/// <see cref="RiverReach.Pool"/>, a lattice <see cref="CellMetres"/> apart along the channel and across it between its banks,
		/// the surface the river's own there, the shore the distance to the nearer bank, the shelter a pool's
		/// (<see cref="BiomeFloatingPlants.PoolShelter"/>). <paramref name="wetAt"/> confirms a point is the river's water (may be null).
		/// </summary>
		public static int PlanPools(int riverId, IReadOnlyList<float> xs, IReadOnlyList<float> zs, IReadOnlyList<float> widths, IReadOnlyList<float> surfaces,
			IReadOnlyList<RiverReach> reaches, Func<float, float, float> ground, Func<float, float, IReadOnlyList<BiomeFloatingPlant>> plantsAt,
			Func<float, float, bool> wetAt, Func<float, float, bool> excluded, uint seed, List<Plant> into, int max = MaxPlants)
		{
			if (xs == null || reaches == null || ground == null || plantsAt == null || xs.Count < 2 || reaches.Count != xs.Count)
			{
				return 0;
			}
			uint riverSeed = Hash(seed, riverId, 0, 0x7001);
			int laid = 0;
			for (int p = 1; p < xs.Count && into.Count < max; p++)
			{
				if (reaches[p - 1] != RiverReach.Pool || reaches[p] != RiverReach.Pool)
				{
					continue;
				}
				float dx = xs[p] - xs[p - 1], dz = zs[p] - zs[p - 1];
				float length = Mathf.Sqrt(dx * dx + dz * dz);
				if (length < 1e-3f)
				{
					continue;
				}
				float tx = dx / length, tz = dz / length;
				int steps = Mathf.Max(1, Mathf.RoundToInt(length / CellMetres));
				for (int s = 0; s < steps && into.Count < max; s++)
				{
					float t = (s + 0.5f) / steps;
					float half = 0.5f * Mathf.Lerp(widths[p - 1], widths[p], t);
					float level = Mathf.Lerp(surfaces[p - 1], surfaces[p], t);
					int across = Mathf.FloorToInt(half / CellMetres);
					for (int a = -across; a <= across && into.Count < max; a++)
					{
						int i = p * 4096 + s, j = a;
						float off = a * CellMetres + 0.3f * (Unit(Hash(riverSeed, i, j, 5)) * 2f - 1f) * CellMetres;
						float shore = half - Mathf.Abs(off);
						if (shore <= 0.1f)
						{
							continue;
						}
						float x = Mathf.Lerp(xs[p - 1], xs[p], t) - tz * off, z = Mathf.Lerp(zs[p - 1], zs[p], t) + tx * off;
						if (wetAt != null && !wetAt(x, z) || excluded != null && excluded(x, z))
						{
							continue;
						}
						IReadOnlyList<BiomeFloatingPlant> plants = plantsAt(x, z);
						if (plants == null || plants.Count == 0)
						{
							continue;
						}
						if (TryPlant(riverSeed, i, j, x, z, level, level - ground(x, z), shore, BiomeFloatingPlants.PoolShelter, plants, out Plant plant))
						{
							into.Add(plant);
							laid++;
						}
					}
				}
			}
			return laid;
		}

		/// <summary>True for a lake id the still-water plants never take: a grotto's pool (in a cave, CaveShaper).</summary>
		public static bool IsGrottoPool(int id) => id >= CaveShaper.PoolIdBase && id < MoatShaper.LakeIdBase;

		/// <summary>True for a moat's lake (MoatShaper).</summary>
		public static bool IsMoat(int id) => id >= MoatShaper.LakeIdBase;

		// ── The scene ─────────────────────────────────────────────────

		/// <summary>
		/// Every plant the scene's still water carries (deterministic in the seed): its lakes and moats from the water grid, then its
		/// rivers' pools. <paramref name="lakePlants"/> and <paramref name="riverPlants"/> give a body's list by its id: its biome's
		/// (<see cref="BodyBiomes"/>), null or empty for none.
		/// </summary>
		public static List<Plant> Plan(SceneWater water, Func<float, float, float> ground, Func<int, IReadOnlyList<BiomeFloatingPlant>> lakePlants,
			Func<int, IReadOnlyList<BiomeFloatingPlant>> riverPlants, Func<float, float, bool> excluded, uint seed)
		{
			var plants = new List<Plant>();
			SceneWaterGrid grid = water?.Grid;
			if (grid == null || lakePlants == null || riverPlants == null)
			{
				return plants;
			}
			Func<float, float, bool> clear = (x, z) =>
			{
				if (excluded != null && excluded(x, z))
				{
					return true;
				}
				foreach (Vector4 b in water.Boulders)
				{
					float dx = x - b.x, dz = z - b.z, r = b.w + BoulderClearMetres;
					if (dx * dx + dz * dz < r * r)
					{
						return true;
					}
				}
				return false;
			};
			foreach (SceneLake lake in water.Lakes)
			{
				if (lake == null || lake.Frozen || IsGrottoPool(lake.Id) || lake.Level < water.SeaLevel + SeaClearMetres)
				{
					continue;
				}
				IReadOnlyList<BiomeFloatingPlant> own = lakePlants(lake.Id);
				if (!BiomeFloatingPlants.Any(own))
				{
					continue;
				}
				StillWater body = BodyOf(grid, lake);
				if (body == null)
				{
					continue;
				}
				body.Shelter = IsMoat(lake.Id) ? 1f : BiomeFloatingPlants.LakeShelter(body.Area);
				PlanBody(body, ground, (x, z) => own, clear, Hash(seed, lake.Id, 0, 0x1A4E), plants);
			}
			foreach (RiverPath river in water.Rivers)
			{
				if (river == null || !river.Perennial || river.Count < 2 || river.Reach == null)
				{
					continue;
				}
				if (river.Surface != null && river.Surface.Length > 0 && Max(river.Surface) < water.SeaLevel + SeaClearMetres)
				{
					continue;
				}
				IReadOnlyList<BiomeFloatingPlant> own = riverPlants(river.Id);
				if (!BiomeFloatingPlants.Any(own))
				{
					continue;
				}
				PlanPools(river.Id, river.X, river.Z, river.Width, river.Surface, river.Reach, ground, (x, z) => own,
					(x, z) => water.KindAt(x, z) == WaterKind.River, clear, seed, plants);
			}
			return plants;
		}

		private static float Max(float[] values)
		{
			float m = float.NegativeInfinity;
			foreach (float v in values)
			{
				m = Mathf.Max(m, v);
			}
			return m;
		}

		/// <summary>
		/// The biome each body of the scene's water lies in, by id (<paramref name="lakes"/>, <paramref name="rivers"/>): the commonest
		/// land biome on its shores, as its water's look is chosen (BiomeWaterLooks.Choose; the lake and river biomes painted under
		/// the water are the water's, not its country's). 0 where none answers.
		/// </summary>
		public static void BodyBiomes(SceneHydrology hydrology, SceneBiomeMap map, Dictionary<int, int> lakes, Dictionary<int, int> rivers)
		{
			if (hydrology == null || map == null || map.Width <= 0 || map.Height <= 0)
			{
				return;
			}
			float mapCell = Mathf.Max(map.WorldSize.x / map.Width, map.WorldSize.y / map.Height);
			int IdAt(float x, float z) => map.IDAt(new Vector3(x, 0f, z));
			BiomeWaterRole? RoleOf(int id) => BiomeRegistry.TryGetByID(id, out BiomeTemplate biome) && biome != null ? biome.WaterRole : (BiomeWaterRole?)null;
			foreach (SceneHydrology.Lake lake in hydrology.Lakes)
			{
				if (lake == null || lakes.ContainsKey(lake.Id))
				{
					continue;
				}
				var shore = new Dictionary<int, float>();
				var under = new Dictionary<int, float>();
				BiomeWaterLooks.LakeShore(lake, IdAt, mapCell, shore, under);
				lakes[lake.Id] = BiomeWaterLooks.Choose(shore, under, RoleOf);
			}
			foreach (SceneHydrology.River river in hydrology.Rivers)
			{
				if (river == null || rivers.ContainsKey(river.Id))
				{
					continue;
				}
				var shore = new Dictionary<int, float>();
				var under = new Dictionary<int, float>();
				BiomeWaterLooks.RiverShore(river, IdAt, mapCell, shore, under);
				rivers[river.Id] = BiomeWaterLooks.Choose(shore, under, RoleOf);
			}
		}

		/// <summary>
		/// Lays the plants of the scene's still water from scratch (<see cref="Plan"/>, <see cref="Place"/>): each body's plants its
		/// shore biome's (<see cref="BodyBiomes"/>, BiomeTemplateArt.FloatingPlants). With no water, no map, or no biome of the scene
		/// floating anything, the last plants go and nothing else is done. Returns how many were laid.
		/// </summary>
		public static int Lay(Scene scene, SceneWater water, SceneHydrology hydrology, SceneBiomeMap map, Func<float, float, float> ground,
			Func<float, float, bool> excluded, uint seed, List<string> notes)
		{
			if (water == null || !water.Any || hydrology == null || map == null)
			{
				ScenePropBaker.Clear(scene, PropSource);
				return 0;
			}
			var lakes = new Dictionary<int, int>();
			var rivers = new Dictionary<int, int>();
			BodyBiomes(hydrology, map, lakes, rivers);
			var lists = new Dictionary<int, IReadOnlyList<BiomeFloatingPlant>>();
			bool any = false;
			IReadOnlyList<BiomeFloatingPlant> ListOf(int biomeId)
			{
				if (!lists.TryGetValue(biomeId, out IReadOnlyList<BiomeFloatingPlant> list))
				{
					list = biomeId != 0 && BiomeRegistry.TryGetByID(biomeId, out BiomeTemplate biome) ? BiomeTemplateArt.FloatingPlants(biome) : null;
					lists[biomeId] = list;
				}
				return list;
			}
			foreach (int id in lakes.Values)
			{
				any |= BiomeFloatingPlants.Any(ListOf(id));
			}
			foreach (int id in rivers.Values)
			{
				any |= BiomeFloatingPlants.Any(ListOf(id));
			}
			if (!any)
			{
				// Every biome's default: no water carries anything, and the cut lays nothing (nor spends any time on it).
				ScenePropBaker.Clear(scene, PropSource);
				return 0;
			}
			List<Plant> plants = Plan(water, ground, id => lakes.TryGetValue(id, out int b) ? ListOf(b) : null,
				id => rivers.TryGetValue(id, out int b) ? ListOf(b) : null, excluded, seed);
			return Place(scene, plants, notes);
		}

		/// <summary>A lake's points on the scene's water grid as a still-water mask; null when it covers none.</summary>
		public static StillWater BodyOf(SceneWaterGrid grid, SceneLake lake)
		{
			int x0 = Mathf.Max(0, Mathf.FloorToInt((lake.Bounds.xMin - grid.OriginX) / grid.Spacing) - 1);
			int x1 = Mathf.Min(grid.Width - 1, Mathf.CeilToInt((lake.Bounds.xMax - grid.OriginX) / grid.Spacing) + 1);
			int z0 = Mathf.Max(0, Mathf.FloorToInt((lake.Bounds.yMin - grid.OriginZ) / grid.Spacing) - 1);
			int z1 = Mathf.Min(grid.Depth - 1, Mathf.CeilToInt((lake.Bounds.yMax - grid.OriginZ) / grid.Spacing) + 1);
			if (x1 < x0 || z1 < z0)
			{
				return null;
			}
			var body = new StillWater
			{
				Id = lake.Id,
				Level = lake.Level,
				Width = x1 - x0 + 1,
				Depth = z1 - z0 + 1,
				Spacing = grid.Spacing,
				Origin = new Vector2(grid.EastOf(x0), grid.NorthOf(z0)),
			};
			body.Wet = new bool[body.Width * body.Depth];
			bool any = false;
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					int g = z * grid.Width + x;
					bool wet = grid.Kind[g] == WaterKind.Lake && grid.Body[g] == lake.Id;
					body.Wet[(z - z0) * body.Width + (x - x0)] = wet;
					any |= wet;
				}
			}
			return any ? body : null;
		}

		/// <summary>
		/// Lays the plants as the scene's <see cref="PropSource"/> props (no collision), replacing the last; with none, removes them.
		/// Returns how many were laid.
		/// </summary>
		public static int Place(Scene scene, List<Plant> plants, List<string> notes)
		{
			if (plants == null || plants.Count == 0)
			{
				ScenePropBaker.Clear(scene, PropSource);
				return 0;
			}
			var prototypes = new List<ScenePropSet.Prototype>();
			var prototypeOf = new Dictionary<BiomeFloatingPlantKind, int>();
			var props = new List<ScenePropSet.Prop>(plants.Count);
			var counts = new Dictionary<BiomeFloatingPlantKind, int>();
			int missing = 0;
			foreach (Plant plant in plants)
			{
				if (!prototypeOf.TryGetValue(plant.Kind, out int prototype))
				{
					string name = ProceduralArtCatalogue.FloatingPrefab(plant.Kind);
					var prefab = name != null ? AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(name)) : null;
					prototype = prefab != null ? prototypes.Count : -1;
					if (prefab != null)
					{
						prototypes.Add(new ScenePropSet.Prototype { Prefab = prefab, Layer = -1 });
					}
					prototypeOf[plant.Kind] = prototype;
				}
				if (prototype < 0)
				{
					missing++;
					continue;
				}
				props.Add(new ScenePropSet.Prop
				{
					Prototype = prototype,
					Position = plant.Position,
					Rotation = Quaternion.Euler(0f, plant.Yaw, 0f),
					Scale = Vector3.one * plant.Scale,
				});
				counts[plant.Kind] = counts.TryGetValue(plant.Kind, out int c) ? c + 1 : 1;
			}
			ScenePropBaker.Write(scene, PropSource, prototypes, props);
			var kinds = new List<string>();
			foreach (KeyValuePair<BiomeFloatingPlantKind, int> kv in counts)
			{
				kinds.Add($"{kv.Value:N0} {kv.Key}");
			}
			notes?.Add($"Floating plants: {props.Count:N0} laid on still water as baked props ({string.Join(", ", kinds)})" +
				(missing > 0 ? $"; {missing:N0} skipped, their art is missing (run Generate biome art)." : "."));
			return props.Count;
		}
	}
}
#endif
