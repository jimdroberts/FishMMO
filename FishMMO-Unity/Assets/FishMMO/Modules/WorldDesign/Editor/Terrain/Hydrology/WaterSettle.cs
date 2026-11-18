#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Builds a scene's water from the ground it ends up in: run once nothing will change the ground again, it measures every
	/// river's banks and every lake's rim on the finished terrain and stands each surface no higher than they hold.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Laid, then measured.</b> A river's line, its junctions, its falls and their pools are planned and cut into the ground
	/// first (<see cref="SceneWater"/>, <see cref="WaterCarver"/>), at the levels they were planned at. Everything after that
	/// reshapes the ground again: the sites' pads and moats, the ways and their carve, the cliffs and their talus. Water kept at
	/// its planned level wherever that ground was lowered stood over the air beside it (2026-10-15: a scour hole beside a
	/// rapid, a mouth flared over a falling river), and each case had its own repair. Here the water is taken from the ground
	/// as it is: whatever cut a bank, the water stands no higher than the bank, and a pool, a pond or a moat no higher than the
	/// lowest point of its rim.
	/// </para>
	/// <para>
	/// <b>Only ever lowered.</b> The planned level is what the channel was cut for, and a level the ground holds is never raised
	/// past it. Water runs downhill, so a river lowered at a point is lowered at every point after it that stood higher, and
	/// level water (a pool, a pond, a slack reach) is lowered as one, to its lowest rim. A tributary meets the river it joins
	/// at that river's level, and a river meets its lake at the lake's.
	/// </para>
	/// </remarks>
	public static class WaterSettle
	{
		/// <summary>How far out from its channel a river's banks are looked for, metres: as far as its drawn shore can reach (<see cref="RiverShores.MaxMetres"/>).</summary>
		public const float HoldReachMetres = RiverShores.MaxMetres;
		/// <summary>The step the banks are walked at, metres.</summary>
		public const float WalkStepMetres = 0.5f;
		/// <summary>Other water standing within this of a level holds it (a confluence, a lake shore), metres.</summary>
		public const float HeldByWaterMetres = 0.3f;
		/// <summary>A drop no larger than this from one point to the next is level water, lowered together, metres.</summary>
		public const float LevelRunMetres = 0.05f;
		/// <summary>How far under the lowest point of its rim a lake that would spill over it is stood, metres.</summary>
		public const float LakeFreeboardMetres = 0.05f;
		/// <summary>Lowered by less than this, a level is left as it was, metres (the ground's own sampling noise).</summary>
		public const float ToleranceMetres = 0.02f;


		/// <summary>
		/// Settles every lake and river of <paramref name="water"/> onto <paramref name="ground"/> (scene metres, finished), in
		/// place, and returns what changed, one line each.
		/// </summary>
		public static List<string> Settle(SceneWater water, Func<float, float, float> ground)
		{
			var notes = new List<string>();
			if (water == null || ground == null || !water.Any)
			{
				return notes;
			}
			SettleLakes(water, ground, notes, null);
			SettleRivers(water, ground, notes, true);
			/* A lake stands no higher than the water leaving it: an outlet whose banks would not hold the lake's level lowers the
			 * lake to it (and the rivers running into it with it), rather than leaving a lake over a river that runs out of it lower. */
			for (int round = 0; round < 3; round++)
			{
				var lower = new Dictionary<SceneLake, float>();
				foreach (SceneLake lake in water.Lakes)
				{
					foreach (RiverPath river in water.Rivers)
					{
						if (river.Perennial && river.Count > 1 && river.Start == RiverEnd.Lake && river.StartLake == lake.Id
							&& river.Surface[0] < lake.Level - ToleranceMetres)
						{
							lower[lake] = lower.TryGetValue(lake, out float level) ? Math.Min(level, river.Surface[0]) : river.Surface[0];
						}
					}
				}
				if (lower.Count == 0)
				{
					break;
				}
				SettleLakes(water, ground, notes, lower);
				SettleRivers(water, ground, notes, false);
			}
			return notes;
		}

		// ── Rivers ─────────────────────────────────────────────────

		private static void SettleRivers(SceneWater water, Func<float, float, float> ground, List<string> notes, bool first)
		{
			var done = new HashSet<RiverPath>();
			var stretches = new List<string>();
			int lowered = 0, falls = 0, dry = 0;
			float deepest = 0f;
			void Settle(RiverPath river, int depth)
			{
				if (river == null || done.Contains(river) || depth > water.Rivers.Count)
				{
					return;
				}
				done.Add(river);
				// What it joins first: it meets that river at its settled level.
				RiverPath joined = river.End == RiverEnd.Confluence && river.JoinsRiver >= 0 && river.JoinsRiver < water.Rivers.Count ? water.Rivers[river.JoinsRiver] : null;
				if (joined != null && joined != river)
				{
					Settle(joined, depth + 1);
				}
				if (!river.Perennial || river.Count < 2)
				{
					return;
				}
				int n = river.Count;
				var planned = (float[])river.Surface.Clone();
				bool[] fixedAt = Fixed(river, water);
				float[] hold = new float[n];
				RiverShaping.Normals(river.X, river.Z, out float[] nx, out float[] nz);
				for (int i = 0; i < n; i++)
				{
					hold[i] = fixedAt[i] ? planned[i] : Hold(water, river, i, nx[i], nz[i], planned[i], ground);
				}
				float startCeiling = float.PositiveInfinity, endCeiling = float.PositiveInfinity;
				if (river.Start == RiverEnd.Lake && LakeOf(water, river.StartLake) is SceneLake startLake)
				{
					startCeiling = startLake.Level;
				}
				if (river.End == RiverEnd.Lake && LakeOf(water, river.EndLake) is SceneLake endLake)
				{
					endCeiling = endLake.Level;
				}
				else if (joined != null && joined.Count > 1)
				{
					endCeiling = SurfaceNear(joined, river.X[n - 1], river.Z[n - 1]);
				}
				float[] settled = Levels(planned, hold, fixedAt, startCeiling, endCeiling);
				int first = -1;
				float most = 0f;
				for (int i = 0; i < n; i++)
				{
					float drop = planned[i] - settled[i];
					if (drop <= ToleranceMetres)
					{
						if (first >= 0)
						{
							stretches.Add($"river {river.Id} points {first}–{i - 1} (up to {most:0.00} m)");
							first = -1;
						}
						continue;
					}
					lowered++;
					deepest = Math.Max(deepest, drop);
					if (first < 0)
					{
						first = i;
						most = 0f;
					}
					most = Math.Max(most, drop);
					river.Surface[i] = settled[i];
					river.Depth[i] = Math.Max(0.05f, settled[i] - river.Bed[i]);
					if (settled[i] < river.Bed[i])
					{
						dry++;
					}
				}
				if (first >= 0)
				{
					stretches.Add($"river {river.Id} points {first}–{n - 1} (up to {most:0.00} m)");
				}
				// A step the settling left where the river ran on: drawn as a fall (its curtain), not a sheet hung down it.
				if (river.Reach != null)
				{
					for (int i = 1; i < n; i++)
					{
						if (river.Surface[i - 1] - river.Surface[i] >= water.Settings.MinPoolDropMetres
							&& planned[i - 1] - planned[i] < water.Settings.MinPoolDropMetres
							&& river.Reach[i] != RiverReach.Fall && river.Reach[i - 1] != RiverReach.Fall)
						{
							river.Reach[i] = RiverReach.Fall;
							falls++;
						}
					}
				}
				RelevelGrid(water, river, planned);
			}
			foreach (RiverPath river in water.Rivers)
			{
				Settle(river, 0);
			}
			if (lowered > 0)
			{
				notes.Add($"Water measured on the finished ground: {lowered} river point(s) stood no higher than their banks, up to {deepest:0.00} m lower " +
					$"({string.Join("; ", stretches.GetRange(0, Math.Min(8, stretches.Count)))}{(stretches.Count > 8 ? $"; and {stretches.Count - 8} more" : string.Empty)})" +
					(falls > 0 ? $", {falls} new step(s) drawn as falls" : string.Empty) + (dry > 0 ? $", {dry} point(s) left under their beds (dry)" : string.Empty) + ".");
			}
			else if (first)
			{
				notes.Add("Water measured on the finished ground: every river's banks hold it at the level it was cut for.");
			}
		}

		/// <summary>
		/// Per point of a river, whether its level is kept as planned rather than measured: water going over a fall and the
		/// lip it leaves (the ground beside a brink drops with the water by design), and water down at the sea's tide line,
		/// which the sea holds.
		/// </summary>
		private static bool[] Fixed(RiverPath river, SceneWater water)
		{
			int n = river.Count;
			var fixedAt = new bool[n];
			float tide = float.IsNegativeInfinity(water.SeaLevel) ? float.NegativeInfinity : water.SeaLevel + water.Settings.IntertidalMetres;
			for (int i = 0; i < n; i++)
			{
				bool falling = river.Reach != null && river.Reach[i] == RiverReach.Fall;
				bool lip = river.Reach != null && i + 1 < n && river.Reach[i + 1] == RiverReach.Fall;
				fixedAt[i] = falling || lip || river.Surface[i] <= tide;
			}
			return fixedAt;
		}

		/// <summary>
		/// The highest level a river's banks hold at point <paramref name="i"/>, no higher than <paramref name="planned"/>: on
		/// each side, walked straight out from its channel's edge, the highest ground met before the water would reach past
		/// <see cref="HoldReachMetres"/>, or <paramref name="planned"/> where the ground rises to it or other water as high
		/// stands there.
		/// </summary>
		/// <remarks>
		/// Straight out only. Walked on a slant as well, the walk met the banks of the same river further down, which hold its
		/// lower water there, and beside a fall it ran out over the brink: the level reach above Flo Monolith's 49 m fall was
		/// lowered 4.5 m for ground that never held it (2026-10-15).
		/// </remarks>
		public static float Hold(SceneWater water, RiverPath river, int i, float nx, float nz, float planned, Func<float, float, float> ground)
		{
			float half = 0.5f * river.Width[i];
			float hold = planned;
			for (int side = -1; side <= 1; side += 2)
			{
				{
					float dx = side * nx, dz = side * nz;
					float best = float.NegativeInfinity;
					for (float d = half + 0.25f; d <= half + HoldReachMetres; d += WalkStepMetres)
					{
						float px = river.X[i] + dx * d, pz = river.Z[i] + dz * d;
						if (HeldByWater(water, river, px, pz, planned, out bool outside))
						{
							best = planned;
							break;
						}
						if (outside)
						{
							// Off the scene: whatever is there is the backdrop's, and holds.
							best = planned;
							break;
						}
						float g = ground(px, pz);
						if (g > best)
						{
							best = g;
							if (best >= planned)
							{
								break;
							}
						}
					}
					hold = Math.Min(hold, best);
					if (hold <= float.NegativeInfinity)
					{
						return hold;
					}
				}
			}
			return hold;
		}

		/// <summary>Whether water other than this river's own stands at a sample within <see cref="HeldByWaterMetres"/> of <paramref name="level"/>: a lake, another river, the sea.</summary>
		private static bool HeldByWater(SceneWater water, RiverPath river, float east, float north, float level, out bool outside)
		{
			outside = false;
			SceneWaterGrid grid = water?.Grid;
			if (grid == null)
			{
				return false;
			}
			int s = grid.SampleAt(east, north);
			if (s < 0)
			{
				outside = true;
				return false;
			}
			WaterKind kind = grid.Kind[s];
			if ((kind == WaterKind.River && grid.Body[s] != river.Id) || kind == WaterKind.Lake)
			{
				return grid.Level[s] >= level - HeldByWaterMetres;
			}
			return false;
		}

		/// <summary>
		/// A river's settled levels from its planned ones and what its banks hold (<paramref name="hold"/>): each point no higher
		/// than its banks, its start lake (<paramref name="startCeiling"/>) and its end's (<paramref name="endCeiling"/>: the
		/// lake or river it runs into); never higher than the point before it; and level water lowered as one, so a pool stands
		/// at its lowest rim. Points in <paramref name="fixedAt"/> keep their own level unless water before them is lower.
		/// </summary>
		public static float[] Levels(float[] planned, float[] hold, bool[] fixedAt, float startCeiling, float endCeiling)
		{
			int n = planned.Length;
			var s = new float[n];
			for (int i = 0; i < n; i++)
			{
				s[i] = fixedAt != null && fixedAt[i] ? planned[i] : Math.Min(planned[i], hold[i]);
			}
			if (n == 0)
			{
				return s;
			}
			s[0] = Math.Min(s[0], startCeiling);
			s[n - 1] = Math.Min(s[n - 1], endCeiling);
			for (int pass = 0; pass < 2; pass++)
			{
				// Downhill: no point above the one before it.
				for (int i = 1; i < n; i++)
				{
					s[i] = Math.Min(s[i], s[i - 1]);
				}
				// Level water lowered as one: back up through every drop no larger than a level run's.
				for (int i = n - 2; i >= 0; i--)
				{
					float drop = planned[i] - planned[i + 1];
					if (drop <= LevelRunMetres && !(fixedAt != null && fixedAt[i]))
					{
						s[i] = Math.Min(s[i], s[i + 1] + Math.Max(0f, drop));
					}
				}
			}
			return s;
		}

		/// <summary>The grid's river samples round a river's lowered points, lowered with them.</summary>
		private static void RelevelGrid(SceneWater water, RiverPath river, float[] planned)
		{
			SceneWaterGrid grid = water.Grid;
			if (grid == null)
			{
				return;
			}
			for (int i = 0; i < river.Count; i++)
			{
				if (planned[i] - river.Surface[i] <= ToleranceMetres)
				{
					continue;
				}
				float reach = 0.5f * river.Width[i] + grid.Spacing;
				int x0 = (int)Math.Floor((river.X[i] - reach - grid.OriginX) / grid.Spacing), x1 = (int)Math.Ceiling((river.X[i] + reach - grid.OriginX) / grid.Spacing);
				int z0 = (int)Math.Floor((river.Z[i] - reach - grid.OriginZ) / grid.Spacing), z1 = (int)Math.Ceiling((river.Z[i] + reach - grid.OriginZ) / grid.Spacing);
				for (int z = Math.Max(0, z0); z <= Math.Min(grid.Depth - 1, z1); z++)
				{
					for (int x = Math.Max(0, x0); x <= Math.Min(grid.Width - 1, x1); x++)
					{
						int k = z * grid.Width + x;
						if (grid.Kind[k] == WaterKind.River && grid.Body[k] == river.Id)
						{
							grid.Level[k] = Math.Min(grid.Level[k], river.Surface[i]);
						}
					}
				}
			}
		}

		/// <summary>A river's surface at the point of its line nearest (east, north).</summary>
		private static float SurfaceNear(RiverPath river, float east, float north)
		{
			float best = float.MaxValue, level = river.Surface[river.Count - 1];
			for (int v = 0; v + 1 < river.Count; v++)
			{
				float ax = river.X[v], az = river.Z[v], ex = river.X[v + 1] - ax, ez = river.Z[v + 1] - az;
				float t = Mathf.Clamp01(((east - ax) * ex + (north - az) * ez) / Mathf.Max(1e-8f, ex * ex + ez * ez));
				float dx = ax + ex * t - east, dz = az + ez * t - north;
				float d = dx * dx + dz * dz;
				if (d < best)
				{
					best = d;
					level = river.Surface[v] + (river.Surface[v + 1] - river.Surface[v]) * t;
				}
			}
			return level;
		}

		private static SceneLake LakeOf(SceneWater water, int id)
		{
			foreach (SceneLake lake in water.Lakes)
			{
				if (lake.Id == id)
				{
					return lake;
				}
			}
			return null;
		}

		// ── Lakes ──────────────────────────────────────────────────

		/// <summary>
		/// Each lake (a pond, a moat) stood no higher than the lowest point of its rim on the finished ground, less
		/// <see cref="LakeFreeboardMetres"/> (a moat its own freeboard), and its water flooded again over that ground. With
		/// <paramref name="ceilings"/>, only those lakes, no higher than the level given each.
		/// </summary>
		private static void SettleLakes(SceneWater water, Func<float, float, float> ground, List<string> notes, Dictionary<SceneLake, float> ceilings)
		{
			SceneWaterGrid grid = water.Grid;
			if (grid == null || water.Lakes.Count == 0)
			{
				return;
			}
			var height = new float[grid.Width * grid.Depth];
			for (int k = 0; k < height.Length; k++)
			{
				height[k] = float.PositiveInfinity;
			}
			foreach (SceneLake lake in water.Lakes)
			{
				// A grotto's pool stands in its cave over holed ground, and the cave writes it after the water is saved (CaveShaper).
				bool grotto = lake.Id >= CaveShaper.PoolIdBase && lake.Id < MoatShaper.LakeIdBase;
				if (lake.Seeds == null || lake.Seeds.Count == 0 || grotto || (ceilings != null && !ceilings.ContainsKey(lake)))
				{
					continue;
				}
				float before = lake.Level;
				if (ceilings != null)
				{
					lake.Level = lake.SpillLevel = Math.Min(lake.Level, ceilings[lake]);
				}
				// The finished ground over the lake's rectangle and a sample past it.
				Rect bounds = lake.Bounds;
				int x0 = Math.Max(0, (int)Math.Floor((bounds.xMin - grid.OriginX) / grid.Spacing) - 1), x1 = Math.Min(grid.Width - 1, (int)Math.Ceiling((bounds.xMax - grid.OriginX) / grid.Spacing) + 1);
				int z0 = Math.Max(0, (int)Math.Floor((bounds.yMin - grid.OriginZ) / grid.Spacing) - 1), z1 = Math.Min(grid.Depth - 1, (int)Math.Ceiling((bounds.yMax - grid.OriginZ) / grid.Spacing) + 1);
				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						height[z * grid.Width + x] = ground(grid.EastOf(x), grid.NorthOf(z));
					}
				}
				Func<int, bool> eligible = Eligible(water, lake);
				// The water of the rivers into and out of it is theirs: their channels and banks are where they carry it, not a rim it spills over.
				var own = new HashSet<int>();
				foreach (RiverPath river in water.Rivers)
				{
					if ((river.End == RiverEnd.Lake && river.EndLake == lake.Id) || (river.Start == RiverEnd.Lake && river.StartLake == lake.Id))
					{
						own.Add(river.Id);
					}
				}
				bool Wall(int k) => grid.Kind[k] == WaterKind.River || grid.Kind[k] == WaterKind.DryWash
					|| ((grid.Kind[k] == WaterKind.Bank || grid.Kind[k] == WaterKind.Bar) && own.Contains(grid.Body[k]));
				var seeds = new List<int>();
				foreach (Vector2 seed in lake.Seeds)
				{
					int k = grid.SampleAt(seed.x, seed.y);
					if (k >= 0)
					{
						seeds.Add(k);
					}
				}
				float spill = Spill(height, grid, seeds, eligible, Wall, lake.Level, x0, x1, z0, z1);
				bool moat = lake.Id >= MoatShaper.LakeIdBase;
				float freeboard = moat ? MoatShaper.Freeboard : LakeFreeboardMetres;
				if (spill < lake.Level + (moat ? MoatShaper.Freeboard : 0f) - ToleranceMetres)
				{
					lake.Level = Math.Min(lake.Level, spill - freeboard);
					lake.SpillLevel = lake.Level;
				}
				// Its water again, over the finished ground at that level.
				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						int k = z * grid.Width + x;
						if (grid.Kind[k] == WaterKind.Lake && grid.Body[k] == lake.Id)
						{
							grid.Kind[k] = WaterKind.None;
							grid.Level[k] = float.NegativeInfinity;
							grid.Body[k] = -1;
						}
					}
				}
				int covered = WaterCarver.FloodLake(height, grid, lake.Level, seeds, eligible, lake.Id, 0f, 0f, false);
				string name = moat ? "Moat" : lake.PlanetLake < 0 ? "Pond" : "Lake";
				if (before - lake.Level > ToleranceMetres)
				{
					string why = ceilings != null ? "at the river leaving it" : "at its rim";
					notes.Add($"Water measured on the finished ground: {name.ToLowerInvariant()} {lake.Id} stood {why}, {before - lake.Level:0.00} m lower ({lake.Level:0.00} m), over {covered:N0} sample(s).");
				}
				else if (lake.Covered > 0 && covered < lake.Covered / 2)
				{
					notes.Add($"Water measured on the finished ground: {name.ToLowerInvariant()} {lake.Id} covers {covered:N0} sample(s) of the {lake.Covered:N0} it was laid over.");
				}
				lake.Covered = covered;
				// Back to unknown, so the next lake reads its own ground.
				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						height[z * grid.Width + x] = float.PositiveInfinity;
					}
				}
			}
		}

		/// <summary>Where a lake may stand: as <see cref="SceneWater"/> floods it (its rectangle, what it may cover, and round each river's mouth into or out of it).</summary>
		private static Func<int, bool> Eligible(SceneWater water, SceneLake lake)
		{
			SceneWaterGrid grid = water.Grid;
			Rect bounds = lake.Bounds;
			Func<float, float, bool> may = lake.MayCover;
			var mouths = new List<Vector3>();
			foreach (RiverPath river in water.Rivers)
			{
				if (river.Count < 2)
				{
					continue;
				}
				if (river.End == RiverEnd.Lake && river.EndLake == lake.Id)
				{
					int last = river.Count - 1;
					mouths.Add(new Vector3(river.X[last], river.Z[last], 2f * river.Width[last] + SceneWater.MouthClearMetres));
				}
				if (river.Start == RiverEnd.Lake && river.StartLake == lake.Id)
				{
					mouths.Add(new Vector3(river.X[0], river.Z[0], 2f * river.Width[0] + SceneWater.MouthClearMetres));
				}
			}
			return k =>
			{
				float east = grid.EastOf(k % grid.Width), north = grid.NorthOf(k / grid.Width);
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
			};
		}

		/// <summary>
		/// The lowest level at which water standing over <paramref name="seeds"/> would run out of where its lake may stand: the
		/// highest ground on the lowest way out (a priority flood). +∞ where it cannot run out below <paramref name="ceiling"/>.
		/// </summary>
		public static float Spill(float[] height, SceneWaterGrid grid, List<int> seeds, Func<int, bool> eligible, Func<int, bool> wall, float ceiling,
			int x0, int x1, int z0, int z1)
		{
			var open = new SortedSet<(float h, int k)>();
			var seen = new HashSet<int>();
			foreach (int k in seeds)
			{
				if (k >= 0 && k < height.Length && seen.Add(k))
				{
					open.Add((height[k], k));
				}
			}
			float water = float.NegativeInfinity;
			while (open.Count > 0)
			{
				(float h, int k) = open.Min;
				open.Remove(open.Min);
				water = Math.Max(water, h);
				if (water >= ceiling + 1f)
				{
					return float.PositiveInfinity;
				}
				int x = k % grid.Width, z = k / grid.Width;
				// Out of where it may stand. The scene's own edge is not a way out: the ground goes on past it.
				if (!eligible(k))
				{
					return water;
				}
				for (int n = 0; n < 4; n++)
				{
					int nx = x + (n == 0 ? 1 : n == 1 ? -1 : 0), nz = z + (n == 2 ? 1 : n == 3 ? -1 : 0);
					if (nx < 0 || nz < 0 || nx >= grid.Width || nz >= grid.Depth)
					{
						continue;
					}
					int j = nz * grid.Width + nx;
					if (!seen.Add(j) || wall(j))
					{
						continue;
					}
					open.Add((height[j], j));
				}
			}
			return float.PositiveInfinity;
		}
	}
}
#endif
