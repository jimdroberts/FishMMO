#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The later drafts' channel forms (2026-10-26, phase I2f-b) on the I2f channel machinery: a bisse's bracketed flumes and its
	/// water-hammer, a roggia laid on the land's grid with its partition, the scene's one great canal off a river weir with its
	/// laterals, a qanat gallery carried underground from a scarp's foot.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Roggia.</b> Routed on a lattice turned to the land's grid (the centuriation's bearing, BiomeCenturiation.GridYaw, which
	/// the fields snap to), moving only along its two axes, every turn costing <see cref="RoggiaTurnMetres"/> (<see cref="GridRoute"/>),
	/// so it runs straight and turns at right angles; its pounds 150–300 m between stone sluices; a stone partition where it meets
	/// the fields' head.
	/// </para>
	/// <para>
	/// <b>Great canal.</b> The scene's one (BiomeLinearFeatures.SceneCapOf): the first settlement whose biome names it and that has
	/// a river within reach high enough to fall to its fields; taken off at a brush-and-boulder weir laid across the river where it
	/// is narrowest (<see cref="AcrossWater"/>), 12 m wide and 3 m deep; laterals (earth ditches) carried off it to every other
	/// settlement naming it whose fields' head lies within <see cref="LateralMetres"/> of it and below its water.
	/// </para>
	/// <para>
	/// <b>Qanat gallery (decision 67).</b> Its head the nearest scarp foot (ground at least <see cref="GalleryRiseMetres"/> over the
	/// fields' head, steeper there than <see cref="ScarpDegrees"/>; else the nearest such rise) within reach (2 km); carried from there
	/// underground (no trench) on the shortest way round water, keep-outs and other fields, a shaft mouth every 15–20 m over it (a
	/// windlass on every fourth); daylighting <see cref="DaylightMetres"/> before the fields' head at an arched gallery mouth set in a
	/// deep stone-revetted cutting, a lined channel from there; at most four a scene. The gallery is walkable: its floor and the ground
	/// under its ledges are cut down under them (<see cref="GalleryFloor"/>).
	/// </para>
	/// </remarks>
	public static partial class PointOfInterestLinearFeatures
	{
		/// <summary>What a roggia's turn costs on its route, metres of straight channel.</summary>
		public const float RoggiaTurnMetres = 40f;
		/// <summary>How far from a great canal a lateral may be carried to a settlement's fields, metres.</summary>
		public const float LateralMetres = 1500f;
		/// <summary>The most laterals a great canal carries.</summary>
		public const int MaxLaterals = 6;
		/// <summary>How far before the fields' head a qanat daylights, metres; the shortest gallery worth carrying underground.</summary>
		public const float DaylightMetres = 30f, MinGalleryMetres = 45f;
		/// <summary>How far apart a qanat's shafts stand, metres (each drawn between).</summary>
		public const float ShaftMinMetres = 15f, ShaftMaxMetres = 20f;
		/// <summary>How much higher than the fields' head a qanat's head must stand, metres, and how steep a scarp's foot is, degrees.</summary>
		public const float GalleryRiseMetres = 6f, ScarpDegrees = 10f;
		/// <summary>How far back past its mouth a qanat's cutting is carried into the slope, metres (the gallery mouth's tunnel stands in it).</summary>
		public const float GalleryBackMetres = 5f;

		// ── Pure ──────────────────────────────────────────────────────

		/// <summary>
		/// The lattice a channel's route is found on, metres (pure): the I2f forms' 4 m always; the later forms' 4 m too, coarser where the
		/// route's box would pass the lattice's limit (a bisse's or a canal's kilometres).
		/// </summary>
		public static float RouteCell(BiomeLinearForm form, Vector2 from, Vector2 to)
		{
			if (form == BiomeLinearForm.EarthDitch || form == BiomeLinearForm.LinedChannel)
			{
				return RouteCellMetres;
			}
			float extent = Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y)) + 2f * RouteMarginMetres;
			return Mathf.Max(RouteCellMetres, Mathf.Ceil(extent / 600f));
		}

		/// <summary>A roggia's pound length, metres (pure): 150–300, drawn once a channel.</summary>
		public static float RoggiaPoundMetres(int seed) => 150f + 150f * BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, 5, 0x524F47, 0));

		/// <summary>
		/// The cheapest route from one point to another moving only along a grid turned <paramref name="yawDegrees"/> (pure; the axes a
		/// field's yaw lays: x (cos, −sin), z (sin, cos)), on its lattice <paramref name="cell"/> metres apart through the origin: four
		/// neighbours, each step's cost as <see cref="Route"/>'s (its ground against a level falling evenly from end to end), each turn
		/// <paramref name="turnMetres"/> more. A* on the grid's Manhattan distance, ties by state. Its path is the corners only, its ends
		/// the two points. False when no route is found.
		/// </summary>
		public static bool GridRoute(Vector2 from, Vector2 to, float yawDegrees, float fromGround, float toGround, Func<float, float, float> ground,
			Func<Vector2, bool> blocked, float cell, float margin, float turnMetres, out List<Vector2> path)
		{
			path = new List<Vector2>();
			if (ground == null || !(cell > 0f))
			{
				return false;
			}
			float r = yawDegrees * Mathf.Deg2Rad;
			var u = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var v = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			Vector2 lf = new Vector2(Vector2.Dot(from, u), Vector2.Dot(from, v)), lt = new Vector2(Vector2.Dot(to, u), Vector2.Dot(to, v));
			int i0 = Mathf.FloorToInt((Mathf.Min(lf.x, lt.x) - margin) / cell), j0 = Mathf.FloorToInt((Mathf.Min(lf.y, lt.y) - margin) / cell);
			int i1 = Mathf.CeilToInt((Mathf.Max(lf.x, lt.x) + margin) / cell), j1 = Mathf.CeilToInt((Mathf.Max(lf.y, lt.y) + margin) / cell);
			int w = i1 - i0 + 1, h = j1 - j0 + 1;
			if (w <= 0 || h <= 0 || (long)w * h > 400000)
			{
				return false;
			}
			Vector2 P(int c) => u * ((i0 + c % w) * cell) + v * ((j0 + c / w) * cell);
			int Cell(Vector2 local) => Mathf.Clamp(Mathf.RoundToInt(local.y / cell) - j0, 0, h - 1) * w + Mathf.Clamp(Mathf.RoundToInt(local.x / cell) - i0, 0, w - 1);
			int start = Cell(lf), goal = Cell(lt);
			Vector2 axis = to - from;
			float axisLen2 = Mathf.Max(1e-6f, axis.sqrMagnitude);
			var cache = new float[w * h];
			for (int c = 0; c < cache.Length; c++)
			{
				cache[c] = float.NaN;
			}
			float Mismatch(int c)
			{
				if (float.IsNaN(cache[c]))
				{
					Vector2 p = P(c);
					float progress = Mathf.Clamp01(Vector2.Dot(p - from, axis) / axisLen2);
					cache[c] = Mathf.Max(0f, Mathf.Abs(ground(p.x, p.y) - Mathf.Lerp(fromGround, toGround, progress)) - 0.3f);
				}
				return cache[c];
			}
			float Heuristic(int c) => (Mathf.Abs(c % w - goal % w) + Mathf.Abs(c / w - goal / w)) * cell;
			// States: a cell and the way it was entered (0 +x, 1 −x, 2 +z, 3 −z); the start entered every way at no cost.
			int states = w * h * 4;
			var g = new float[states];
			var parent = new int[states];
			var closed = new bool[states];
			for (int k = 0; k < states; k++)
			{
				g[k] = float.PositiveInfinity;
				parent[k] = -1;
			}
			var open = new SortedSet<(float F, int S)>();
			for (int d = 0; d < 4; d++)
			{
				g[start * 4 + d] = 0f;
				open.Add((Heuristic(start), start * 4 + d));
			}
			int[] di = { 1, -1, 0, 0 }, dj = { 0, 0, 1, -1 };
			int found = -1;
			while (open.Count > 0)
			{
				(float _, int at) = open.Min;
				open.Remove(open.Min);
				if (closed[at])
				{
					continue;
				}
				closed[at] = true;
				int c = at / 4, dir = at % 4;
				if (c == goal)
				{
					found = at;
					break;
				}
				int ci = c % w, cj = c / w;
				for (int n = 0; n < 4; n++)
				{
					int ni = ci + di[n], nj = cj + dj[n];
					if (ni < 0 || nj < 0 || ni >= w || nj >= h)
					{
						continue;
					}
					int next = nj * w + ni, ns = next * 4 + n;
					if (closed[ns] || next != goal && next != start && blocked != null && blocked(P(next)))
					{
						continue;
					}
					float m = Mismatch(next);
					float cost = g[at] + cell * (1f + 4f * m * m) + (n != dir && c != start ? turnMetres : 0f);
					if (cost < g[ns])
					{
						g[ns] = cost;
						parent[ns] = at;
						open.Add((cost + Heuristic(next), ns));
					}
				}
			}
			if (found < 0)
			{
				return false;
			}
			var cells = new List<int>();
			for (int k = found; k >= 0; k = parent[k])
			{
				cells.Add(k);
			}
			cells.Reverse();
			path.Add(from);
			for (int k = 1; k < cells.Count - 1; k++)
			{
				// Only the corners: where the way it leaves differs from the way it came.
				if (cells[k] % 4 != cells[k + 1] % 4)
				{
					path.Add(P(cells[k] / 4));
				}
			}
			path.Add(to);
			return true;
		}

		/// <summary>
		/// A qanat's head (pure but for its functions): the nearest point within <paramref name="reach"/> of the fields' head (on an 8 m
		/// lattice, 16 m past 1.2 km) whose ground stands <see cref="GalleryRiseMetres"/> over the head's and whose slope is past
		/// <see cref="ScarpDegrees"/> (a scarp's foot); else the nearest that only stands so high. Never where <paramref name="blocked"/>.
		/// False when there is neither.
		/// </summary>
		public static bool GalleryHead(Vector2 head, float reach, Func<float, float, float> ground, Func<float, float, float> slope, Func<Vector2, bool> blocked,
			out Vector2 mother)
		{
			mother = default;
			if (ground == null)
			{
				return false;
			}
			float need = ground(head.x, head.y) + GalleryRiseMetres;
			float step = reach > 1200f ? 16f : 8f;
			int n = Mathf.CeilToInt(reach / step);
			float bestScarp = float.MaxValue, bestRise = float.MaxValue;
			Vector2 scarp = default, rise = default;
			for (int j = -n; j <= n; j++)
			{
				for (int i = -n; i <= n; i++)
				{
					var p = new Vector2(Mathf.Round(head.x / step + i) * step, Mathf.Round(head.y / step + j) * step);
					float d = (p - head).magnitude;
					if (d > reach || d >= bestRise && d >= bestScarp)
					{
						continue;
					}
					float g = ground(p.x, p.y);
					if (float.IsNaN(g) || float.IsInfinity(g) || g < need || blocked != null && blocked(p))
					{
						continue;
					}
					if (d < bestRise)
					{
						bestRise = d;
						rise = p;
					}
					if (d < bestScarp && slope != null && slope(p.x, p.y) >= ScarpDegrees)
					{
						bestScarp = d;
						scarp = p;
					}
				}
			}
			if (bestScarp < float.MaxValue)
			{
				mother = scarp;
				return true;
			}
			if (bestRise < float.MaxValue)
			{
				mother = rise;
				return true;
			}
			return false;
		}

		/// <summary>
		/// The along-route distances a qanat's shafts stand at (pure), walking back from <paramref name="mouth"/> metres along its route
		/// (from its head) to its head: the first 15–20 m back, each next 15–20 m on, the last on the head itself (the mother well), none
		/// within half a gap of it before that, each gap drawn by its own hash of <paramref name="seed"/>. Head first.
		/// </summary>
		public static List<float> ShaftStations(float mouth, int seed)
		{
			var at = new List<float>();
			float s = mouth;
			for (int k = 0; ; k++)
			{
				s -= ShaftMinMetres + (ShaftMaxMetres - ShaftMinMetres) * BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, k, 0x534854, 0));
				if (s <= ShaftMinMetres * 0.5f)
				{
					break;
				}
				at.Add(s);
			}
			at.Add(0f);
			at.Reverse();
			return at;
		}

		/// <summary>
		/// The shortest chord across water through a point (pure but for <paramref name="wet"/>): of twelve bearings, the one whose walk
		/// both ways to dry ground (1 m steps, at most <paramref name="most"/> each way) is shortest. False when the point is dry or no
		/// bearing reaches dry ground both ways.
		/// </summary>
		public static bool AcrossWater(Func<Vector2, bool> wet, Vector2 p, float most, out Vector2 a, out Vector2 b)
		{
			a = b = p;
			if (wet == null || !wet(p))
			{
				return false;
			}
			float best = float.MaxValue;
			for (int k = 0; k < 12; k++)
			{
				float r = k * 15f * Mathf.Deg2Rad;
				var dir = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
				float plus = Walk(dir), minus = Walk(-dir);
				if (plus < 0f || minus < 0f || plus + minus >= best)
				{
					continue;
				}
				best = plus + minus;
				a = p - dir * minus;
				b = p + dir * plus;
			}
			return best < float.MaxValue;

			float Walk(Vector2 dir)
			{
				for (float s = 1f; s <= most; s += 1f)
				{
					if (!wet(p + dir * s))
					{
						return s;
					}
				}
				return -1f;
			}
		}

		// ── The scene: roggia, bisse ──────────────────────────────────

		/// <summary>The land's grid a roggia is laid on: the centuriation's bearing for the scene (the fields' own snap), degrees.</summary>
		private static float RoggiaYaw(SceneScope scope) => BiomeCenturiation.GridYaw(scope.Seed);

		/// <summary>A roggia's stone partition where it meets its fields' head and divides along it.</summary>
		private static int Partition(SceneScope scope, BiomeTemplate biome, BiomeLinearStyle style, List<ChannelStation> stations, Vector2 at)
		{
			int best = -1;
			float near = float.MaxValue;
			for (int i = 0; i < stations.Count; i++)
			{
				float d = (stations[i].At - at).sqrMagnitude;
				if (stations[i].Part == ChannelPart.Cut && d < near)
				{
					near = d;
					best = i;
				}
			}
			if (best < 0 || near > 16f)
			{
				return 0;
			}
			PointOfInterestSiteContext context = scope.Context();
			context.Palette = StructurePieces.LinearPalette(BiomeTemplateArt.StructurePalette(biome), style, "ChannelSluice");
			Vector2 dir = stations[Mathf.Min(stations.Count - 1, best + 1)].At - stations[Mathf.Max(0, best - 1)].At;
			float half = ChannelHalfWidth(style.Form);
			return Lay(context, "ChannelSluice", 1, stations[best].At, stations[best].Level + ChannelFreeboard, dir, new Vector3(2f * half / StructurePieces.ChannelBuiltWidth, 1f, 1f));
		}

		/// <summary>A bisse's water-hammer at the first carved station a few metres below its head.</summary>
		private static int Hammer(PointOfInterestSiteContext context, BiomeStructurePalette palette, BiomeLinearStyle style, List<ChannelStation> stations, int seed)
		{
			for (int i = 2; i < Mathf.Min(stations.Count - 1, 12); i++)
			{
				ChannelStation st = stations[i];
				if (st.Part != ChannelPart.Cut || stations[i - 1].Part != ChannelPart.Cut || stations[i + 1].Part != ChannelPart.Cut || st.Pound != stations[i - 1].Pound)
				{
					continue;
				}
				context.Palette = StructurePieces.LinearPalette(palette, style, "ChannelHammer");
				int laid = Lay(context, "ChannelHammer", (int)(BiomeLinearFeatures.Hash(seed, 0x484D52, 0, 0) & 1u), st.At, st.Level + ChannelFreeboard,
					stations[i + 1].At - stations[i - 1].At, Vector3.one);
				context.Palette = palette;
				return laid;
			}
			return 0;
		}

		/// <summary>
		/// Whether a bisse's flume module at <paramref name="mid"/> (its way <paramref name="along"/>) has a cliff to hang on beside it
		/// (pure but for the ground): the ground 2 m to one side standing 0.8 m over its rim; <paramref name="turn"/> 1 when that is its
		/// left (the piece's −x as laid), −1 when its right (the piece turned about).
		/// </summary>
		private static bool BracketSide(PointOfInterestSiteContext context, Vector2 mid, Vector2 along, float rim, out float turn)
		{
			turn = 1f;
			if (context.GroundAt == null || along.sqrMagnitude < 1e-8f)
			{
				return false;
			}
			Vector2 left = Left(along) * 2f;
			float gl = context.GroundAt(mid.x + left.x, mid.y + left.y), gr = context.GroundAt(mid.x - left.x, mid.y - left.y);
			float high = Mathf.Max(gl, gr);
			if (float.IsNaN(high) || high < rim + 0.8f)
			{
				return false;
			}
			// Looking along its way, the piece's −x is the channel's left.
			turn = gl >= gr ? 1f : -1f;
			return true;
		}

		// ── The scene: the great canal ────────────────────────────────

		/// <summary>The scene's one great canal: from a river weir to the first settlement naming it that a river can water, its laterals to the others.</summary>
		private static int LayGreatCanal(SceneScope scope)
		{
			var naming = new List<(PointOfInterestRecord Record, BiomeTemplate Biome, BiomeLinearStyle Style, int Index)>();
			foreach (PointOfInterestRecord record in scope.Points.Points)
			{
				if (!BiomeRegistry.TryGetByID(record.BiomeID, out BiomeTemplate biome))
				{
					continue;
				}
				foreach ((BiomeLinearStyle style, int index) in StylesAt(biome, BiomeLinearPlace.FieldEdges))
				{
					if (style.Form == BiomeLinearForm.GreatCanal)
					{
						naming.Add((record, biome, style, index));
						break;
					}
				}
			}
			if (naming.Count == 0)
			{
				return 0;
			}
			int laid = 0;
			foreach ((PointOfInterestRecord record, BiomeTemplate biome, BiomeLinearStyle style, int index) in naming)
			{
				if (scope.ChannelCount >= MaxChannels || !scope.CapLeft(BiomeLinearForm.GreatCanal))
				{
					break;
				}
				int seed = unchecked((int)BiomeLinearFeatures.Hash(record.SiteSeed, 0x434841, index, 0));
				if (!BiomeLinearFeatures.Admits(style.Density, BiomeLinearFeatures.Hash(seed, 1, 0, 0)) || !HeadOf(scope, record, out List<Vector2> head)
					|| !TakeOff(scope, head[0], style.Reach > 0f ? style.Reach : SearchMetres, true, out Vector2 source, out float surface, out Vector2 water))
				{
					continue;
				}
				float startLevel = Mathf.Min(surface - 0.05f, scope.Ground(source.x, source.y) - ChannelFreeboard - 0.12f);
				Vector2 last = head[head.Count - 1];
				float endLevel = Mathf.Min(startLevel, scope.Ground(last.x, last.y) - ChannelFreeboard - 0.12f);
				bool Blocked(Vector2 p) => scope.Water.KindAt(p.x, p.y) != WaterKind.None || scope.Points.IsKeptOut(p.x, p.y) && (p - source).magnitude > 20f
					|| scope.Slope != null && scope.Slope(p.x, p.y) > 18f || InClaims(scope.Claims, p, 6f, null) && (p - head[0]).magnitude > 10f;
				if (!Route(source, head[0], startLevel + ChannelFreeboard + 0.12f, scope.Ground(head[0].x, head[0].y), scope.Ground, Blocked,
					RouteCell(style.Form, source, head[0]), RouteMarginMetres, out List<Vector2> route))
				{
					scope.Notes?.Add($"Linear features: {record.Name}'s great canal found no way from its river to its fields.");
					continue;
				}
				route.AddRange(head.GetRange(1, head.Count - 1));
				var stations = new List<ChannelStation>();
				scope.TakeCap(BiomeLinearForm.GreatCanal);
				scope.Watered.Add(record.Id);
				laid += BuildChannel(scope, biome, style, route, true, startLevel, endLevel, scope.ChannelCount++, seed, record, stations, true, 300f);
				laid += Weir(scope, biome, style, water, surface);
				int laterals = 0;
				foreach ((PointOfInterestRecord other, BiomeTemplate otherBiome, BiomeLinearStyle otherStyle, int _) in naming)
				{
					if (laterals >= MaxLaterals || scope.ChannelCount >= MaxChannels || scope.Watered.Contains(other.Id))
					{
						continue;
					}
					int lateral = Lateral(scope, otherBiome, otherStyle, other, stations, unchecked(seed + other.Id * 31));
					if (lateral > 0)
					{
						laid += lateral;
						laterals++;
						scope.Watered.Add(other.Id);
					}
				}
				scope.Notes?.Add($"Linear features: {record.Name}'s great canal off its river weir, {laterals} lateral(s).");
				break;
			}
			return laid;
		}

		/// <summary>A great canal's weir across its river at the take-off: along the river's narrowest chord there, at its surface.</summary>
		private static int Weir(SceneScope scope, BiomeTemplate biome, BiomeLinearStyle style, Vector2 water, float surface)
		{
			if (!AcrossWater(p => scope.Water.KindAt(p.x, p.y) == WaterKind.River, water, 80f, out Vector2 a, out Vector2 b))
			{
				return 0;
			}
			GameObject prefab = StructureKit.Prefab("ChannelWeir", (int)(BiomeLinearFeatures.Hash(scope.Seed, 0x574552, 0, 0) & 1u));
			if (prefab == null)
			{
				scope.Notes?.Add("Linear features: the great canal's weir is not generated yet (Biome Tools → Generate biome art, then regenerate).");
				return 0;
			}
			// A metre into either bank, so its ends are founded.
			Vector2 d = (b - a).normalized;
			a -= d;
			b += d;
			PointOfInterestSiteContext context = scope.Context();
			context.Palette = StructurePieces.LinearPalette(BiomeTemplateArt.StructurePalette(biome), style, "ChannelWeir");
			Vector2 mid = 0.5f * (a + b);
			context.AddProp(prefab, new Vector3(mid.x, surface, mid.y), AlongX(new Vector3(a.x, 0f, a.y), new Vector3(b.x, 0f, b.y)),
				new Vector3((b - a).magnitude / StructurePieces.WeirModule, 1f, 1f));
			return 1;
		}

		/// <summary>
		/// A lateral off a great canal to a settlement's fields: an earth ditch from the canal's nearest carved station within
		/// <see cref="LateralMetres"/> of the fields' head whose water stands high enough over it. Returns the props laid (0: none fits).
		/// </summary>
		private static int Lateral(SceneScope scope, BiomeTemplate biome, BiomeLinearStyle canal, PointOfInterestRecord record, List<ChannelStation> stations, int seed)
		{
			if (!HeadOf(scope, record, out List<Vector2> head))
			{
				return 0;
			}
			float headLevel = scope.Ground(head[0].x, head[0].y) - ChannelFreeboard - 0.12f;
			int best = -1;
			float near = float.MaxValue;
			for (int i = 0; i < stations.Count; i++)
			{
				float d = (stations[i].At - head[0]).magnitude;
				if (stations[i].Part == ChannelPart.Cut && d < near && d <= LateralMetres && stations[i].Level >= headLevel + ChannelGrade * d * 1.3f + 0.25f)
				{
					near = d;
					best = i;
				}
			}
			if (best < 0)
			{
				return 0;
			}
			Vector2 dir = stations[Mathf.Min(stations.Count - 1, best + 1)].At - stations[Mathf.Max(0, best - 1)].At;
			Vector2 side = Left(dir);
			float half = ChannelHalfWidth(canal.Form);
			// Off the bank nearer the fields.
			if (Vector2.Dot(side, head[0] - stations[best].At) < 0f)
			{
				side = -side;
			}
			Vector2 from = stations[best].At + side * (half + 0.5f);
			float startLevel = stations[best].Level - 0.1f;
			float endLevel = Mathf.Min(startLevel, scope.Ground(head[head.Count - 1].x, head[head.Count - 1].y) - ChannelFreeboard - 0.12f);
			bool Blocked(Vector2 p) => scope.Water.KindAt(p.x, p.y) != WaterKind.None || scope.Points.IsKeptOut(p.x, p.y)
				|| scope.Slope != null && scope.Slope(p.x, p.y) > 25f || InClaims(scope.Claims, p, 1f, null) && (p - head[0]).magnitude > 6f;
			if (!Route(from, head[0], startLevel + ChannelFreeboard + 0.12f, scope.Ground(head[0].x, head[0].y), scope.Ground, Blocked,
				RouteCell(BiomeLinearForm.GreatCanal, from, head[0]), RouteMarginMetres, out List<Vector2> route))
			{
				return 0;
			}
			route.AddRange(head.GetRange(1, head.Count - 1));
			var ditch = new BiomeLinearStyle(BiomeLinearForm.EarthDitch, BiomeLinearPlace.FieldEdges)
			{
				OwnMaterial = canal.OwnMaterial,
				Material = canal.Material,
				Tint = canal.Tint,
			};
			return BuildChannel(scope, biome, ditch, route, true, startLevel, endLevel, scope.ChannelCount++, seed, record, null, false);
		}

		// ── The scene: the qanat gallery ──────────────────────────────

		/// <summary>
		/// A settlement's qanat (decision 67): its head at a scarp's foot, its gallery carried underground with a shaft mouth every
		/// 15–20 m, daylighting at its gallery mouth into a lined channel the last <see cref="DaylightMetres"/> to the fields. Returns the
		/// props laid; at most four a scene.
		/// </summary>
		private static int LayGallery(SceneScope scope, PointOfInterestRecord record, BiomeTemplate biome, BiomeLinearStyle style, List<Vector2> head, int seed)
		{
			if (!scope.CapLeft(BiomeLinearForm.Gallery))
			{
				scope.Notes?.Add($"Linear features: {record.Name}'s qanat not laid: the scene has its {BiomeLinearFeatures.SceneCapOf(BiomeLinearForm.Gallery)} already.");
				return 0;
			}
			float reach = style.Reach > 0f ? style.Reach : 2000f;
			bool Blocked(Vector2 p) => scope.Water.KindAt(p.x, p.y) != WaterKind.None || scope.Points.IsKeptOut(p.x, p.y) || InClaims(scope.Claims, p, 2f, null);
			if (!GalleryHead(head[0], reach, scope.Ground, scope.Slope, Blocked, out Vector2 mother))
			{
				scope.Notes?.Add($"Linear features: {record.Name}'s qanat found no scarp or rise {GalleryRiseMetres:0} m over its fields within {reach:0} m.");
				return 0;
			}
			// Underground the ground does not matter: the shortest way round what it may not pass under.
			bool Under(Vector2 p) => Blocked(p) && (p - mother).magnitude > 8f && (p - head[0]).magnitude > 8f;
			if (!Route(mother, head[0], 0f, 0f, (x, z) => 0f, Under, RouteCell(style.Form, mother, head[0]), 60f, out List<Vector2> route))
			{
				scope.Notes?.Add($"Linear features: {record.Name}'s qanat found no way from its scarp to its fields.");
				return 0;
			}
			float[] along = Along(route);
			float length = along[along.Length - 1];
			if (length < MinGalleryMetres)
			{
				scope.Notes?.Add($"Linear features: {record.Name}'s qanat head lies only {length:0} m from its fields: no gallery.");
				return 0;
			}
			float mouthAt = length - DaylightMetres;
			Vector2 mouth = PointAt(route, along, mouthAt);
			Vector2 back = PointAt(route, along, Mathf.Max(0f, mouthAt - GalleryBackMetres));
			// The open channel: from just inside its cutting, out of the mouth, down to the fields' head and along it.
			var open = new List<Vector2> { back, mouth };
			for (int i = 0; i < route.Count; i++)
			{
				if (along[i] > mouthAt + 0.5f)
				{
					open.Add(route[i]);
				}
			}
			open.AddRange(head.GetRange(1, head.Count - 1));
			float headLevel = scope.Ground(head[0].x, head[0].y) - ChannelFreeboard - 0.12f;
			float startLevel = Mathf.Min(scope.Ground(mouth.x, mouth.y) - 0.4f, headLevel + ChannelGrade * DaylightMetres + 0.05f);
			Vector2 last = head[head.Count - 1];
			float endLevel = Mathf.Min(startLevel, scope.Ground(last.x, last.y) - ChannelFreeboard - 0.12f);
			scope.TakeCap(BiomeLinearForm.Gallery);
			var stations = new List<ChannelStation>();
			int laid = BuildChannel(scope, biome, style, open, true, startLevel, endLevel, scope.ChannelCount++, seed, record, stations, false);
			PointOfInterestSiteContext context = scope.Context(record);
			BiomeStructurePalette palette = BiomeTemplateArt.StructurePalette(biome);
			float bank = startLevel + ChannelFreeboard;
			Vector2 way = mouth - back;
			GalleryFloor(context, mouth, way, bank);
			GameObject gallery = StructureKit.Prefab("QanatGallery", (int)(BiomeLinearFeatures.Hash(seed, 0x47414C, 0, 0) & 1u));
			if (gallery != null && way.sqrMagnitude > 1e-6f)
			{
				context.Palette = StructurePieces.LinearPalette(palette, style, "QanatGallery");
				context.AddProp(gallery, new Vector3(mouth.x, bank, mouth.y), Quaternion.LookRotation(new Vector3(way.x, 0f, way.y).normalized, Vector3.up), Vector3.one);
				laid++;
			}
			// The shaft mouths over the gallery, from its head to behind its mouth (the cutting's own length clear).
			var exclusion = new PathExclusion(scope.Ways);
			context.Palette = StructurePieces.LinearPalette(palette, style, "QanatShaft");
			List<float> shafts = ShaftStations(mouthAt - GalleryBackMetres, seed);
			int shaftsLaid = 0;
			for (int k = 0; k < shafts.Count; k++)
			{
				Vector2 at = PointAt(route, along, shafts[k]);
				float g = scope.Ground(at.x, at.y);
				if (float.IsNaN(g) || scope.Water.KindAt(at.x, at.y) != WaterKind.None || exclusion.Trees(at.x, at.y) || !context.InScene(at.x, at.y, 2f))
				{
					continue;
				}
				// A windlass over every fourth, counted from the mother well.
				GameObject shaft = StructureKit.Prefab("QanatShaft", k % 4 == 0 ? 1 : 0);
				if (shaft == null)
				{
					break;
				}
				Vector2 d = PointAt(route, along, Mathf.Min(length, shafts[k] + 2f)) - PointAt(route, along, Mathf.Max(0f, shafts[k] - 2f));
				d = d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.up;
				context.AddProp(shaft, new Vector3(at.x, g - 0.05f, at.y), Quaternion.LookRotation(new Vector3(d.x, 0f, d.y), Vector3.up), Vector3.one);
				laid++;
				shaftsLaid++;
			}
			scope.Notes?.Add($"Linear features: {record.Name}'s qanat: {mouthAt:0} m underground from its scarp, {shaftsLaid} shaft mouth(s), daylighting {DaylightMetres:0} m above its fields.");
			return laid;
		}

		/// <summary>
		/// A gallery mouth's floor: the ground under its tunnel and ledges cut down under them (lowered only), so it can be walked into
		/// (its walls stand on the cut's sides; the ground above its vault is the slope's own).
		/// </summary>
		private static void GalleryFloor(PointOfInterestSiteContext context, Vector2 mouth, Vector2 way, float bank)
		{
			if (way.sqrMagnitude < 1e-8f)
			{
				return;
			}
			Vector2 fwd = way.normalized, right = -Left(fwd);
			float halfX = StructurePieces.GalleryHalfWidth + 0.2f, back = StructurePieces.GalleryDepth + 0.3f;
			float floor = bank - StructurePieces.GalleryLedge - 0.12f;
			float reach = Mathf.Sqrt(halfX * halfX + back * back) + 1f;
			var area = new Rect(mouth.x - reach, mouth.y - reach, 2f * reach, 2f * reach);
			PointOfInterestFieldCarve.CarveTo(context, area, (x, z, g) =>
			{
				Vector2 rel = new Vector2(x, z) - mouth;
				float across = Vector2.Dot(rel, right), on = Vector2.Dot(rel, fwd);
				if (Mathf.Abs(across) > halfX || on < -back || on > 0.3f)
				{
					return 0f;
				}
				return Mathf.Min(0f, floor - g);
			});
		}
	}
}
#endif
