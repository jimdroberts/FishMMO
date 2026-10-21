#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Water channels (2026-10-18, phase I2f): irrigation leats, bisses and rogge carried at a gentle fall from a river or lake above a
	/// settlement's fields to them and along their head, and standing ditches along lanes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What a channel is (decided 2026-10-18, I2f).</b> Neither a river of the scene's hydrology (its carve, flow, falls and
	/// settling are the cut's water stage, long done when the sites are placed, and a narrow leat is far finer than its grid) nor
	/// paint alone (a painted strip has no water in it). It is a shallow trench carved into the terrain (PointOfInterestFieldCarve's
	/// record and restore, as ridge and furrow and paddies are), its downhill bank raised of its own spoil, and its water a chain of
	/// level pounds, each a lake of the scene's hydrology on a one-metre mask (as a paddy's water is, PointOfInterestPaddies): drawn,
	/// reflected and waded like any lake, hidden under the banks wherever its sheet reaches past the trench. Each pound steps down to
	/// the next at a plank sluice (a stone one on a lined channel). Where the ground falls away too steeply for a bank to hold it the
	/// water crosses in a timber flume on trestles; under a cart track or road it runs through a culvert. Walkable throughout: the
	/// trench is under half a metre deep and a few metres across, gently sided.
	/// </para>
	/// <para>
	/// <b>Its route.</b> Planned from the water down to the fields, never the other way: the take-off is the nearest river or lake
	/// within the style's reach whose surface stands high enough above the fields' head to fall to it at
	/// <see cref="ChannelGrade"/>, and the route between is the cheapest on a lattice (<see cref="Route"/>) whose ground keeps nearest a
	/// level falling evenly from one end to the other: on a hillside the contour, as a real leat runs; on a plain nearly straight. It
	/// is then carried along the uphill side of the highest of the settlement's fields. Pure and deterministic.
	/// </para>
	/// <para>
	/// <b>Ids and clearing.</b> A pound's lake takes an id in the paddies' range (<see cref="ChannelIdBase"/>), so the place stage's
	/// clearing of the paddies' water (PointOfInterestPaddies.ClearWater) and the cut's water leaving them out (SceneWater.FromAsset)
	/// apply to channels unchanged; the trench's carve is recorded under the scene's POI root and restored before anything reads the
	/// ground again, as the fields' are.
	/// </para>
	/// </remarks>
	public static partial class PointOfInterestLinearFeatures
	{
		/// <summary>Lake ids channels take: inside the paddies' range (<see cref="PointOfInterestPaddies.IsPaddy"/>), bit 27 set, which no paddy id has.</summary>
		public const int ChannelIdBase = PointOfInterestPaddies.LakeIdBase | (1 << 27);
		/// <summary>The fall a leat is carried at, rise over run (a quarter of a percent).</summary>
		public const float ChannelGrade = 0.0025f;
		/// <summary>How far its banks stand over its water, metres.</summary>
		public const float ChannelFreeboard = 0.18f;
		/// <summary>The most a bank is built up over the ground; past it the water crosses in a flume, metres.</summary>
		public const float ChannelMaxFill = 0.9f;
		/// <summary>How far apart a channel's stations stand, metres.</summary>
		public const float ChannelStationMetres = 2f;
		/// <summary>The longest pound of a leat, metres; a standing ditch's.</summary>
		public const float PoundMetres = 120f, DitchPoundMetres = 60f;
		/// <summary>The most a leat's water steps down at one sluice, metres.</summary>
		public const float MaxStep = 0.35f;
		/// <summary>How far round its fields' head a channel looks for water, metres, when its style names no reach.</summary>
		public const float SearchMetres = 900f;
		/// <summary>The lattice a channel is routed on, metres, and how far past its two ends it may wander.</summary>
		public const float RouteCellMetres = 4f, RouteMarginMetres = 150f;
		/// <summary>Past the trench's edge its bank, then the bank's fall back to the ground, metres.</summary>
		public const float BankMetres = 2.5f, BankFallMetres = 1.5f;
		/// <summary>The most channels one scene carries.</summary>
		public const int MaxChannels = 16;

		/// <summary>A pound's lake id.</summary>
		public static int ChannelLakeId(int channel, int pound) => ChannelIdBase | ((channel & 0x7FFF) << 12) | (pound & 0xFFF);

		/// <summary>True for a channel pound's lake id.</summary>
		public static bool IsChannel(int id) => (id >> 27) == (ChannelIdBase >> 27);

		/// <summary>A channel's half width, metres: an earth leat 0.7, a lined channel 1.0.</summary>
		public static float ChannelHalfWidth(BiomeLinearForm form) => form == BiomeLinearForm.LinedChannel ? 1f : 0.7f;

		/// <summary>What a channel's station is: carved and flooded, carried in a flume, under a road in a culvert.</summary>
		public enum ChannelPart : byte
		{
			Cut = 0,
			Flume = 1,
			Culvert = 2,
		}

		/// <summary>One station of a channel: where, how far along, the ground there before the carve, its water's level, its pound, what it is.</summary>
		public struct ChannelStation
		{
			public Vector2 At;
			public float Along;
			public float Ground;
			public float Level;
			public int Pound;
			public ChannelPart Part;
		}

		// ── Pure: the route ───────────────────────────────────────────

		/// <summary>
		/// The cheapest route on a lattice <paramref name="cell"/> metres apart (fixed to the scene's metres) from one point to another
		/// (pure): eight neighbours, each step's cost its length times one plus four times the square of how far (past 0.3 m) the ground
		/// there stands from the target falling evenly from <paramref name="fromGround"/> to <paramref name="toGround"/> by progress along
		/// the straight line between them; <paramref name="blocked"/> cells never entered (but the two ends'). A* on the straight
		/// distance, ties broken by cell, so the same ground gives the same route. False when no route is found.
		/// </summary>
		public static bool Route(Vector2 from, Vector2 to, float fromGround, float toGround, Func<float, float, float> ground, Func<Vector2, bool> blocked,
			float cell, float margin, out List<Vector2> path)
		{
			path = new List<Vector2>();
			if (ground == null || !(cell > 0f))
			{
				return false;
			}
			int x0 = Mathf.FloorToInt((Mathf.Min(from.x, to.x) - margin) / cell), z0 = Mathf.FloorToInt((Mathf.Min(from.y, to.y) - margin) / cell);
			int x1 = Mathf.CeilToInt((Mathf.Max(from.x, to.x) + margin) / cell), z1 = Mathf.CeilToInt((Mathf.Max(from.y, to.y) + margin) / cell);
			int w = x1 - x0 + 1, h = z1 - z0 + 1;
			if (w <= 0 || h <= 0 || (long)w * h > 400000)
			{
				return false;
			}
			Vector2 P(int i) => new Vector2((x0 + i % w) * cell, (z0 + i / w) * cell);
			int Index(Vector2 p) => Mathf.Clamp(Mathf.RoundToInt(p.y / cell) - z0, 0, h - 1) * w + Mathf.Clamp(Mathf.RoundToInt(p.x / cell) - x0, 0, w - 1);
			int start = Index(from), goal = Index(to);
			Vector2 axis = to - from;
			float axisLen2 = Mathf.Max(1e-6f, axis.sqrMagnitude);
			var g = new float[w * h];
			var parent = new int[w * h];
			var closed = new bool[w * h];
			var cache = new float[w * h];
			for (int i = 0; i < g.Length; i++)
			{
				g[i] = float.PositiveInfinity;
				parent[i] = -1;
				cache[i] = float.NaN;
			}
			float Mismatch(int i)
			{
				if (float.IsNaN(cache[i]))
				{
					Vector2 p = P(i);
					float progress = Mathf.Clamp01(Vector2.Dot(p - from, axis) / axisLen2);
					float target = Mathf.Lerp(fromGround, toGround, progress);
					float m = Mathf.Max(0f, Mathf.Abs(ground(p.x, p.y) - target) - 0.3f);
					cache[i] = m;
				}
				return cache[i];
			}
			var open = new SortedSet<(float F, int I)>();
			g[start] = 0f;
			open.Add(((P(start) - P(goal)).magnitude, start));
			int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
			while (open.Count > 0)
			{
				(float f, int at) = open.Min;
				open.Remove(open.Min);
				if (closed[at])
				{
					continue;
				}
				closed[at] = true;
				if (at == goal)
				{
					break;
				}
				int ax = at % w, az = at / w;
				for (int n = 0; n < 8; n++)
				{
					int nx = ax + dx[n], nz = az + dz[n];
					if (nx < 0 || nz < 0 || nx >= w || nz >= h)
					{
						continue;
					}
					int next = nz * w + nx;
					if (closed[next] || next != goal && next != start && blocked != null && blocked(P(next)))
					{
						continue;
					}
					float step = n < 4 ? cell : cell * 1.41421356f;
					float m = Mismatch(next);
					float cost = g[at] + step * (1f + 4f * m * m);
					if (cost < g[next])
					{
						g[next] = cost;
						parent[next] = at;
						open.Add((cost + (P(next) - P(goal)).magnitude, next));
					}
				}
			}
			if (!closed[goal])
			{
				return false;
			}
			var cells = new List<Vector2>();
			for (int i = goal; i >= 0; i = parent[i])
			{
				cells.Add(P(i));
				if (i == start)
				{
					break;
				}
			}
			cells.Reverse();
			cells[0] = from;
			cells[cells.Count - 1] = to;
			// Its corners cut, unless that would take it into a cell it may not enter.
			path = Smooth(cells, 2);
			for (int i = 1; blocked != null && i < path.Count - 1; i++)
			{
				if ((path[i] - from).magnitude > cell && (path[i] - to).magnitude > cell && blocked(path[i]))
				{
					path = cells;
					break;
				}
			}
			return true;
		}

		/// <summary>A polyline cut at its corners (Chaikin) <paramref name="passes"/> times, its ends kept (pure).</summary>
		public static List<Vector2> Smooth(List<Vector2> line, int passes)
		{
			List<Vector2> at = line;
			for (int p = 0; p < passes && at.Count > 2; p++)
			{
				var next = new List<Vector2>(at.Count * 2) { at[0] };
				for (int i = 0; i + 1 < at.Count; i++)
				{
					next.Add(Vector2.Lerp(at[i], at[i + 1], 0.25f));
					next.Add(Vector2.Lerp(at[i], at[i + 1], 0.75f));
				}
				next.Add(at[at.Count - 1]);
				at = next;
			}
			return at;
		}

		// ── Pure: the water ───────────────────────────────────────────

		/// <summary>
		/// A channel's stations and its water (pure): stations every <see cref="ChannelStationMetres"/> along <paramref name="line"/>, the
		/// ground at each before any carve, and each one's water level and pound. A falling leat (<paramref name="falling"/>) holds each
		/// pound level, never higher than the last, and starts a lower one where the planned fall (from <paramref name="startLevel"/> to
		/// <paramref name="endLevel"/>) has dropped <see cref="MaxStep"/> under it or the pound has run <see cref="PoundMetres"/>; a standing
		/// ditch keeps its water <see cref="ChannelFreeboard"/> and a hand under its ground, a new pound where the ground has moved a
		/// quarter metre or after <see cref="DitchPoundMetres"/>. <paramref name="culvert"/> marks the stations under a road; a station
		/// whose lower side (<paramref name="lowSide"/>, the ground a bank's width out) would need more than <see cref="ChannelMaxFill"/>
		/// built up is carried in a flume. Each run of flume or culvert stations ends its pound.
		/// </summary>
		public static List<ChannelStation> Pounds(IReadOnlyList<Vector2> line, Func<float, float, float> ground, bool falling, float startLevel, float endLevel,
			Func<Vector2, bool> culvert = null, Func<Vector2, Vector2, float> lowSide = null)
		{
			var stations = new List<ChannelStation>();
			List<Station> at = Stations(line, ChannelStationMetres);
			if (at.Count < 2 || ground == null)
			{
				return stations;
			}
			float length = at[at.Count - 1].Along;
			float level = float.NaN, poundStart = 0f;
			int pound = -1;
			ChannelPart last = ChannelPart.Cut;
			for (int i = 0; i < at.Count; i++)
			{
				Vector2 p = at[i].At;
				float g = ground(p.x, p.y);
				float rest = g - ChannelFreeboard - 0.12f;
				float plan = falling ? Mathf.Lerp(startLevel, endLevel, length > 0f ? at[i].Along / length : 0f) : rest;
				ChannelPart part = culvert != null && culvert(p) ? ChannelPart.Culvert : ChannelPart.Cut;
				bool fresh = pound < 0 || part == ChannelPart.Cut && last != ChannelPart.Cut;
				if (!fresh)
				{
					fresh = falling ? level - plan > MaxStep || at[i].Along - poundStart > PoundMetres
						: Mathf.Abs(rest - level) > 0.25f || at[i].Along - poundStart > DitchPoundMetres;
				}
				if (fresh && part == ChannelPart.Cut)
				{
					level = falling ? (float.IsNaN(level) ? plan : Mathf.Min(level, plan)) : rest;
					poundStart = at[i].Along;
					pound++;
				}
				else if (float.IsNaN(level))
				{
					level = falling ? plan : rest;
					pound = Mathf.Max(pound, 0);
				}
				if (part == ChannelPart.Cut)
				{
					Vector2 dir = at[Mathf.Min(at.Count - 1, i + 1)].At - at[Mathf.Max(0, i - 1)].At;
					float low = lowSide != null ? lowSide(p, dir) : g;
					if (level + ChannelFreeboard - Mathf.Min(g, low) > ChannelMaxFill)
					{
						part = ChannelPart.Flume;
					}
				}
				stations.Add(new ChannelStation { At = p, Along = at[i].Along, Ground = g, Level = level, Pound = pound, Part = part });
				last = part;
			}
			return stations;
		}

		/// <summary>
		/// The ground a channel leaves at a point (pure), from the ground there and its distance <paramref name="d"/> from the channel's
		/// line: inside its half width a trench whose floor lies <paramref name="depth"/> under its banks, eased up its sides; where the ground stands over the banks, the cut's sides rising from them at one in one; across its bank
		/// (<see cref="BankMetres"/>) lower ground raised (never lowered) to its banks' height, easing back to the ground over
		/// <see cref="BankFallMetres"/>; beyond, the ground as it was. Continuous for a cut up to the bank's width deep.
		/// </summary>
		public static float ChannelGround(float d, float ground, float level, float half, float depth)
		{
			float bank = level + ChannelFreeboard;
			if (d <= half)
			{
				// Dug down to it where the ground stands higher, built up to it where lower (a bank's width of fill at most: past that
				// the water crosses in a flume), so its sides always meet its banks.
				float floor = bank - depth;
				return Mathf.Lerp(floor, bank, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d / half - 0.45f) / 0.55f)));
			}
			float cut = Mathf.Min(ground, bank + (d - half));
			if (d <= half + BankMetres)
			{
				return Mathf.Max(cut, bank);
			}
			if (d <= half + BankMetres + BankFallMetres)
			{
				return Mathf.Max(cut, Mathf.Lerp(bank, ground, (d - half - BankMetres) / BankFallMetres));
			}
			return ground;
		}

		/// <summary>
		/// One pound's water as a lake of the scene's hydrology (pure): every one-metre cell, on a lattice fixed to the scene's metres,
		/// whose middle lies within 0.55 of the half width of the pound's carved stretch; null when none does.
		/// </summary>
		public static SceneHydrology.Lake PoundLake(int id, IReadOnlyList<ChannelStation> stations, int pound, float half)
		{
			const float cell = 1f;
			var segments = new List<(Vector2 A, Vector2 B)>();
			float level = float.NaN;
			float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
			for (int i = 0; i + 1 < stations.Count; i++)
			{
				ChannelStation a = stations[i], b = stations[i + 1];
				if (a.Pound != pound || b.Pound != pound || a.Part != ChannelPart.Cut || b.Part != ChannelPart.Cut)
				{
					continue;
				}
				level = a.Level;
				segments.Add((a.At, b.At));
				minX = Mathf.Min(minX, Mathf.Min(a.At.x, b.At.x));
				maxX = Mathf.Max(maxX, Mathf.Max(a.At.x, b.At.x));
				minZ = Mathf.Min(minZ, Mathf.Min(a.At.y, b.At.y));
				maxZ = Mathf.Max(maxZ, Mathf.Max(a.At.y, b.At.y));
			}
			if (segments.Count == 0)
			{
				return null;
			}
			float x0 = Mathf.Floor((minX - half - 1f) / cell) * cell, z0 = Mathf.Floor((minZ - half - 1f) / cell) * cell;
			int width = Mathf.CeilToInt((maxX + half + 1f - x0) / cell), height = Mathf.CeilToInt((maxZ + half + 1f - z0) / cell);
			var bits = new byte[(width * height + 7) / 8];
			int covered = 0;
			Vector2 seed = default;
			for (int z = 0; z < height; z++)
			{
				for (int x = 0; x < width; x++)
				{
					var p = new Vector2(x0 + (x + 0.5f) * cell, z0 + (z + 0.5f) * cell);
					float best = float.MaxValue;
					foreach ((Vector2 a, Vector2 b) in segments)
					{
						best = Mathf.Min(best, HedgeDistance(p, a, b, out _));
					}
					if (best > 0.55f * half)
					{
						continue;
					}
					int bit = z * width + x;
					bits[bit >> 3] |= (byte)(1 << (bit & 7));
					if (covered++ == 0)
					{
						seed = p;
					}
				}
			}
			if (covered == 0)
			{
				return null;
			}
			return new SceneHydrology.Lake
			{
				Id = id,
				PlanetLake = -1,
				Level = level,
				SpillLevel = level,
				Floor = level - SceneHydrology.SurfacePoolFloorDepth,
				Seeds = new[] { seed },
				Bounds = new Rect(x0, z0, width * cell, height * cell),
				Mask = bits,
				MaskOrigin = new Vector2(x0, z0),
				MaskCell = cell,
				MaskWidth = width,
				MaskHeight = height,
			};
		}

		/// <summary>The nearest of a channel's carved segments to a point: its distance, and the station its level is taken from (−1 for none in reach).</summary>
		public static float NearestCut(IReadOnlyList<ChannelStation> stations, Dictionary<long, List<int>> grid, float gridCell, Vector2 p, out int station)
		{
			station = -1;
			float best = float.MaxValue;
			long Key(int i, int j) => ((long)i << 32) ^ (uint)j;
			int ci = Mathf.FloorToInt(p.x / gridCell), cj = Mathf.FloorToInt(p.y / gridCell);
			for (int dj = -1; dj <= 1; dj++)
			{
				for (int di = -1; di <= 1; di++)
				{
					if (!grid.TryGetValue(Key(ci + di, cj + dj), out List<int> list))
					{
						continue;
					}
					foreach (int k in list)
					{
						ChannelStation a = stations[k], b = stations[k + 1];
						float d = HedgeDistance(p, a.At, b.At, out float t);
						if (d < best)
						{
							best = d;
							station = t < 0.5f ? k : k + 1;
						}
					}
				}
			}
			return best;
		}

		/// <summary>The segments of a channel's carved stretches, bucketed on a lattice (each segment in every cell its bounds touch) for <see cref="NearestCut"/>.</summary>
		public static Dictionary<long, List<int>> CutGrid(IReadOnlyList<ChannelStation> stations, float gridCell, float reach)
		{
			var grid = new Dictionary<long, List<int>>();
			for (int k = 0; k + 1 < stations.Count; k++)
			{
				if (stations[k].Part != ChannelPart.Cut || stations[k + 1].Part != ChannelPart.Cut || stations[k].Pound != stations[k + 1].Pound)
				{
					continue;
				}
				Vector2 a = stations[k].At, b = stations[k + 1].At;
				int i0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach) / gridCell), i1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + reach) / gridCell);
				int j0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach) / gridCell), j1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y) + reach) / gridCell);
				for (int j = j0; j <= j1; j++)
				{
					for (int i = i0; i <= i1; i++)
					{
						long key = ((long)i << 32) ^ (uint)j;
						if (!grid.TryGetValue(key, out List<int> list))
						{
							list = new List<int>();
							grid[key] = list;
						}
						list.Add(k);
					}
				}
			}
			return grid;
		}

		// ── The scene ─────────────────────────────────────────────────

		/// <summary>The scene's leats: from the water to the fields of each settlement whose biome waters its fields, in its channel style.</summary>
		private static int LayChannels(SceneScope scope)
		{
			if (scope.Claims == null || scope.Water == null)
			{
				return 0;
			}
			int laid = 0, channels = 0, dry = 0;
			foreach (PointOfInterestRecord record in scope.Points.Points)
			{
				if (channels >= MaxChannels || !BiomeRegistry.TryGetByID(record.BiomeID, out BiomeTemplate biome))
				{
					continue;
				}
				foreach ((BiomeLinearStyle style, int index) in StylesAt(biome, BiomeLinearPlace.FieldEdges))
				{
					if (style.Kind != BiomeLinearKind.Channel)
					{
						continue;
					}
					int seed = unchecked((int)BiomeLinearFeatures.Hash(record.SiteSeed, 0x434841, index, 0));
					if (!BiomeLinearFeatures.Admits(style.Density, BiomeLinearFeatures.Hash(seed, 1, 0, 0)) || !HeadOf(scope, record, out List<Vector2> head))
					{
						break;
					}
					if (!TakeOff(scope, head[0], style.Reach > 0f ? style.Reach : SearchMetres, out Vector2 source, out float surface))
					{
						dry++;
						break;
					}
					float startLevel = Mathf.Min(surface - 0.05f, scope.Ground(source.x, source.y) - ChannelFreeboard - 0.12f);
					Vector2 last = head[head.Count - 1];
					float endLevel = Mathf.Min(startLevel, scope.Ground(last.x, last.y) - ChannelFreeboard - 0.12f);
					var exclusion = new PathExclusion(scope.Ways);
					bool Blocked(Vector2 p) => scope.Water.KindAt(p.x, p.y) != WaterKind.None || scope.Points.IsKeptOut(p.x, p.y) && (p - source).magnitude > 12f
						|| scope.Slope != null && scope.Slope(p.x, p.y) > 25f || InClaims(scope.Claims, p, 1f, null) && (p - head[0]).magnitude > 6f;
					if (!Route(source, head[0], startLevel + ChannelFreeboard + 0.12f, scope.Ground(head[0].x, head[0].y), scope.Ground, Blocked, RouteCellMetres, RouteMarginMetres,
						out List<Vector2> route))
					{
						scope.Notes?.Add($"Linear features: {record.Name}'s leat found no way from its water to its fields.");
						break;
					}
					route.AddRange(head.GetRange(1, head.Count - 1));
					laid += BuildChannel(scope, biome, style, route, true, startLevel, endLevel, channels++, seed, record);
					break;
				}
			}
			if (channels + dry > 0)
			{
				scope.Notes?.Add($"Linear features: {channels} leat(s) carried to the fields" + (dry > 0 ? $"; {dry} settlement(s) had no water high enough within reach." : "."));
			}
			return laid;
		}

		/// <summary>
		/// The head of a settlement's fields (pure but for the ground): the uphill side (by its mean ground) of its highest field, moved
		/// out a bank's width, from its higher end to its lower. False when it has no field.
		/// </summary>
		private static bool HeadOf(SceneScope scope, PointOfInterestRecord record, out List<Vector2> head)
		{
			head = null;
			float best = float.NegativeInfinity;
			var site = new Vector2(record.Position.x, record.Position.z);
			foreach (PointOfInterestFieldClaims.Claim claim in scope.Claims.All)
			{
				if (claim.Site != record.Id)
				{
					continue;
				}
				foreach (Side side in RectangleSides(claim.Centre, claim.Yaw, claim.Size, site, BankMetres))
				{
					float mean = 0f;
					for (int k = 0; k <= 4; k++)
					{
						Vector2 p = Vector2.Lerp(side.From, side.To, k / 4f);
						mean += scope.Ground(p.x, p.y) * 0.2f;
					}
					if (mean > best)
					{
						best = mean;
						bool fromHigher = scope.Ground(side.From.x, side.From.y) >= scope.Ground(side.To.x, side.To.y);
						head = fromHigher ? new List<Vector2> { side.From, side.To } : new List<Vector2> { side.To, side.From };
					}
				}
			}
			return head != null;
		}

		/// <summary>
		/// Where a leat takes its water off (pure but for the water): the nearest river or lake within <paramref name="reach"/> of the head
		/// (on an 8 m lattice) whose surface stands high enough over the head's ground to fall to it at the channel's grade, then walked
		/// toward the head to the first dry ground. False when there is none.
		/// </summary>
		private static bool TakeOff(SceneScope scope, Vector2 head, float reach, out Vector2 source, out float surface)
		{
			source = default;
			surface = float.NaN;
			float headLevel = scope.Ground(head.x, head.y) - ChannelFreeboard - 0.12f;
			float best = float.MaxValue;
			const float step = 8f;
			int n = Mathf.CeilToInt(reach / step);
			for (int j = -n; j <= n; j++)
			{
				for (int i = -n; i <= n; i++)
				{
					var p = new Vector2(Mathf.Round(head.x / step + i) * step, Mathf.Round(head.y / step + j) * step);
					float d = (p - head).magnitude;
					if (d > reach || d >= best)
					{
						continue;
					}
					WaterKind kind = scope.Water.KindAt(p.x, p.y);
					if (kind != WaterKind.River && kind != WaterKind.Lake)
					{
						continue;
					}
					float s = scope.Water.SurfaceAt(p.x, p.y);
					if (float.IsInfinity(s) || s < headLevel + ChannelGrade * d * 1.3f + 0.25f)
					{
						continue;
					}
					best = d;
					source = p;
					surface = s;
				}
			}
			if (best == float.MaxValue)
			{
				return false;
			}
			Vector2 dir = (head - source).normalized;
			for (int k = 0; k < 40; k++)
			{
				Vector2 p = source + dir * k;
				if (scope.Water.KindAt(p.x, p.y) == WaterKind.None)
				{
					source = p + dir;
					return true;
				}
			}
			return false;
		}

		/// <summary>A lane's standing ditch along one verge run (each pound its own water, its level from the ground).</summary>
		private static int LayDitch(SceneScope scope, Layer layer, List<Vector2> run, int seed)
		{
			BiomeTemplate biome = layer.Biome;
			int index = scope.DitchCount++;
			return BuildChannel(scope, biome, layer.Style, run, false, 0f, 0f, MaxChannels + index, seed, null);
		}

		/// <summary>
		/// Builds one channel along a line: its stations and pounds, the trench carved, each pound's water written, its bed kept bare by a
		/// yard way, its head and steps sluiced, flumes over the ground too low to bank, culverts under roads and footbridges under paths.
		/// Returns the props laid.
		/// </summary>
		private static int BuildChannel(SceneScope scope, BiomeTemplate biome, BiomeLinearStyle style, List<Vector2> line, bool falling, float startLevel, float endLevel,
			int channel, int seed, PointOfInterestRecord record)
		{
			float half = ChannelHalfWidth(style.Form);
			float depth = BiomeLinearFeatures.HeightOf(style);
			var ways = new List<ScenePath>();
			foreach (ScenePath p in scope.Ways)
			{
				if (p != null && p.Count >= 2 && p.Class != ScenePathClass.Yard)
				{
					ways.Add(p);
				}
			}
			var index = new PathSegmentIndex(ways, p => p.MaxHalfWidth + 3f);
			ScenePath WayAt(Vector2 p, float extra, out bool road)
			{
				road = false;
				List<(int path, int point)> near = index.Near(p.x, p.y);
				if (near == null)
				{
					return null;
				}
				foreach ((int path, int point) in near)
				{
					ScenePath way = index.Paths[path];
					if (index.Distance(path, point, p.x, p.y, out float t) < index.HalfWidthAt(path, point, t) + extra)
					{
						road = way.Class != ScenePathClass.Footpath && way.Class != ScenePathClass.Trail;
						return way;
					}
				}
				return null;
			}
			bool Culvert(Vector2 p) => WayAt(p, half + 0.6f, out bool road) != null && road;
			float LowSide(Vector2 p, Vector2 dir)
			{
				Vector2 side = Left(dir) * (half + BankMetres);
				return Mathf.Min(scope.Ground(p.x + side.x, p.y + side.y), scope.Ground(p.x - side.x, p.y - side.y));
			}
			List<ChannelStation> stations = Pounds(line, scope.Ground, falling, startLevel, endLevel, Culvert, LowSide);
			if (stations.Count < 2)
			{
				return 0;
			}
			PointOfInterestSiteContext context = scope.Context(record);

			// The trench and its banks, carved from the ground as it lies (restored before the next run reads it).
			const float gridCell = 6f;
			Dictionary<long, List<int>> grid = CutGrid(stations, gridCell, half + BankMetres + BankFallMetres);
			float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
			foreach (ChannelStation st in stations)
			{
				minX = Mathf.Min(minX, st.At.x);
				maxX = Mathf.Max(maxX, st.At.x);
				minZ = Mathf.Min(minZ, st.At.y);
				maxZ = Mathf.Max(maxZ, st.At.y);
			}
			float reach = half + BankMetres + BankFallMetres + 1f;
			Rect area = Rect.MinMaxRect(minX - reach, minZ - reach, maxX + reach, maxZ + reach);
			int carved = PointOfInterestFieldCarve.CarveTo(context, area, (x, z, g) =>
			{
				float d = NearestCut(stations, grid, gridCell, new Vector2(x, z), out int k);
				if (k < 0 || d > half + BankMetres + BankFallMetres)
				{
					return 0f;
				}
				return ChannelGround(d, g, stations[k].Level, half, depth) - g;
			});

			// The water, a lake a pound, and the bed kept bare under it.
			int pounds = stations[stations.Count - 1].Pound + 1, wet = 0;
			for (int p = 0; p < pounds; p++)
			{
				SceneHydrology.Lake lake = PoundLake(ChannelLakeId(channel, p), stations, p, half);
				if (lake != null && PointOfInterestPaddies.WriteWater(context, lake))
				{
					wet++;
				}
			}
			AddBed(scope, stations, half);
			// Its line stored for the next repaint, which paints the biome's wet margin along it (2026-10-18, I2j-b, decision 38).
			ChannelMarginPaint.Record(context, BiomeRegistry.IDOf(biome), half, stations.ConvertAll(st => (st.At, st.Part == ChannelPart.Cut)));

			// The pieces: the head, a sluice at every step, the flumes, the crossings.
			int laid = 0;
			bool lined = style.Form == BiomeLinearForm.LinedChannel;
			BiomeStructurePalette palette = BiomeTemplateArt.StructurePalette(biome);
			for (int i = 0; i < stations.Count; i++)
			{
				ChannelStation st = stations[i];
				Vector2 dir = stations[Mathf.Min(stations.Count - 1, i + 1)].At - stations[Mathf.Max(0, i - 1)].At;
				bool head = i == 0 && falling;
				bool step = i > 0 && st.Part == ChannelPart.Cut && stations[i - 1].Part == ChannelPart.Cut && st.Pound != stations[i - 1].Pound;
				if (head || step)
				{
					context.Palette = StructurePieces.LinearPalette(palette, style, "ChannelSluice");
					laid += Lay(context, "ChannelSluice", lined || head ? 1 : 0, st.At, i > 0 ? stations[i - 1].Level + ChannelFreeboard : st.Level + ChannelFreeboard, dir,
						new Vector3(2f * half / StructurePieces.ChannelBuiltWidth, 1f, 1f));
				}
				// A footpath across: a plank footbridge over the cut, once a crossing.
				if (st.Part == ChannelPart.Cut && WayAt(st.At, 0.3f, out bool road) != null && !road && (i == 0 || WayAt(stations[i - 1].At, 0.3f, out _) == null))
				{
					context.Palette = palette;
					laid += Lay(context, "ChannelBridge", 0, st.At, st.Level + ChannelFreeboard, dir, new Vector3(2f * half / StructurePieces.ChannelBuiltWidth, 1f, 1f));
				}
			}
			laid += LayRuns(context, palette, style, stations, ChannelPart.Culvert, half);
			laid += LayRuns(context, palette, style, stations, ChannelPart.Flume, half);
			if (falling)
			{
				scope.Notes?.Add($"Linear features: {(record != null ? record.Name + "'s " : string.Empty)}leat of {stations[stations.Count - 1].Along:0} m, {wet} pound(s) of water, {carved:N0} heightmap sample(s) carved, {laid} piece(s).");
			}
			return laid;
		}

		/// <summary>The culverts (one at each run's middle, its headwalls either side of the road) or the flumes (end to end on trestles) of a channel.</summary>
		private static int LayRuns(PointOfInterestSiteContext context, BiomeStructurePalette palette, BiomeLinearStyle style, List<ChannelStation> stations, ChannelPart part, float half)
		{
			int laid = 0;
			for (int i = 0; i < stations.Count; i++)
			{
				if (stations[i].Part != part)
				{
					continue;
				}
				int j = i;
				while (j + 1 < stations.Count && stations[j + 1].Part == part)
				{
					j++;
				}
				Vector2 a = stations[Mathf.Max(0, i - 1)].At, b = stations[Mathf.Min(stations.Count - 1, j + 1)].At;
				float level = stations[i].Level;
				if (part == ChannelPart.Culvert)
				{
					Vector2 mid = 0.5f * (a + b);
					float span = 0.5f * (b - a).magnitude;
					context.Palette = StructurePieces.LinearPalette(palette, style, "ChannelBridge");
					laid += Lay(context, "ChannelBridge", 1, mid, level + ChannelFreeboard, b - a, new Vector3(2f * half / StructurePieces.ChannelBuiltWidth, 1f, Mathf.Max(0.5f, span / 2.15f)));
				}
				else
				{
					context.Palette = palette;
					var run = new List<Vector2> { a, b };
					List<Station> modules = Stations(run, StructurePieces.FlumeModule);
					float rim = level + StructurePieces.FlumeWater;
					GameObject trough = StructureKit.Prefab("ChannelFlume", style.Form == BiomeLinearForm.LinedChannel ? 1 : 0);
					GameObject bent = StructureKit.Prefab("BoardwalkBent", 0);
					for (int k = 0; trough != null && k + 1 < modules.Count; k++)
					{
						Vector2 p0 = modules[k].At, p1 = modules[k + 1].At;
						var mid = new Vector3(0.5f * (p0.x + p1.x), rim, 0.5f * (p0.y + p1.y));
						var along = new Vector3(p1.x - p0.x, 0f, p1.y - p0.y);
						context.AddProp(trough, mid, Quaternion.LookRotation(along.normalized, Vector3.up), new Vector3(1f, 1f, along.magnitude / StructurePieces.FlumeModule));
						laid++;
						if (bent != null)
						{
							float foot = context.GroundAt(p1.x, p1.y);
							float under = rim - 0.55f;
							if (under - foot > 0.3f)
							{
								context.AddProp(bent, new Vector3(p1.x, under, p1.y), Quaternion.LookRotation(along.normalized, Vector3.up),
									new Vector3(0.45f, StructurePieces.BoardwalkBentScale(under - foot), 1f));
								laid++;
							}
						}
					}
				}
				i = j;
			}
			return laid;
		}

		/// <summary>One channel piece at a point at a height, turned to the channel's way (its z along it).</summary>
		private static int Lay(PointOfInterestSiteContext context, string id, int variant, Vector2 at, float y, Vector2 dir, Vector3 scale)
		{
			GameObject prefab = StructureKit.Prefab(id, variant);
			if (prefab == null || dir.sqrMagnitude < 1e-8f)
			{
				return 0;
			}
			context.AddProp(prefab, new Vector3(at.x, y, at.y), Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y).normalized, Vector3.up), scale);
			return 1;
		}

		/// <summary>A yard way down each carved stretch of a channel: its bed bare earth under the water, no grass growing up through it.</summary>
		private static void AddBed(SceneScope scope, List<ChannelStation> stations, float half)
		{
			if (scope.Points.Paths == null)
			{
				return;
			}
			int id = 0;
			foreach (ScenePath existing in scope.Points.Paths)
			{
				id = Math.Max(id, existing != null ? existing.Id : 0);
			}
			int start = -1;
			for (int i = 0; i <= stations.Count; i++)
			{
				bool cut = i < stations.Count && stations[i].Part == ChannelPart.Cut;
				if (cut && start < 0)
				{
					start = i;
				}
				else if (!cut && start >= 0)
				{
					int n = i - start;
					if (n >= 2)
					{
						var points = new Vector3[n];
						var widths = new float[n];
						var wear = new float[n];
						var surface = new byte[n];
						var flags = new byte[n];
						for (int k = 0; k < n; k++)
						{
							ChannelStation st = stations[start + k];
							points[k] = new Vector3(st.At.x, scope.Ground(st.At.x, st.At.y), st.At.y);
							widths[k] = half * 0.8f;
							wear[k] = 0.85f;
							surface[k] = (byte)ScenePathSurface.Earth;
						}
						scope.Points.Paths.Add(new ScenePath { Id = ++id, Class = ScenePathClass.Yard, Points = points, HalfWidth = widths, Wear = wear, Surface = surface, Flags = flags });
					}
					start = -1;
				}
			}
		}
	}
}
#endif
