#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The floating islands of a cloud world (<see cref="Celestial.CloudWorld"/>, <see cref="SceneCloudWorld"/>): where they
	/// float, their outlines, the ground on top of each, and which of the scene's heightmap cells are island and which are
	/// open sky. Pure: the same scene size and seed give the same islands.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Islands are the scene's own terrain, holed.</b> The scene keeps its ordinary tiles; every cell outside an island
	/// is a terrain hole, so the grass, the biomes, the scatter, the sites, the NavMesh and the character controller all
	/// work on an island exactly as on any ground. Under each, a rock underside (<see cref="CloudIslandUndersides"/>) hangs
	/// down into the void.
	/// </para>
	/// <para>
	/// <b>Laid out as an archipelago,</b> not scattered: the city island first, near the middle; then each further island
	/// grown off one already placed, a rope bridge's span or an airship's crossing away from it, and a little higher or
	/// lower than it. So islands come in chains and clusters a player can cross on foot, with the farther ones reached by air.
	/// </para>
	/// <para>
	/// <b>A cell is island only when its four corners are on the same island,</b> so no solid cell ever stretches across a
	/// gap or down from an island's rim to the filler under the void. The heights under the void are the nearest island's
	/// rim, so a read of the ground there is that island's edge, never a cliff down to nothing.
	/// </para>
	/// </remarks>
	public static class CloudIslands
	{
		/// <summary>The deck's top, y: the islands' heights are above it.</summary>
		public const float DeckTop = 0f;

		/// <summary>The lowest island ground, metres above the deck.</summary>
		public const float FloorAboveDeck = 140f;

		/// <summary>The longest gap a rope bridge spans, metres; farther islands are reached by air.</summary>
		public const float LongestBridge = 64f;

		/// <summary>The shortest gap between two islands, metres.</summary>
		public const float ShortestGap = 10f;

		/// <summary>The steepest a bridge may climb, rise over run.</summary>
		public const float BridgeGrade = 0.22f;

		/// <summary>Keep-out from the scene's edge, metres, past an island's reach.</summary>
		public const float EdgeMargin = 40f;

		/// <summary>One island.</summary>
		public sealed class Island
		{
			public SceneCloudWorld.IslandKind Kind;
			/// <summary>Centre, metres east and north of the scene's centre.</summary>
			public Vector2 Centre;
			/// <summary>The outline's mean radius, metres.</summary>
			public float Radius;
			/// <summary>The ground's base height, y.</summary>
			public float Height;
			/// <summary>How high the crown rises above the base at the centre, metres.</summary>
			public float Dome;
			/// <summary>How far the ground rolls up and down, metres.</summary>
			public float Roll;
			/// <summary>A hill on the island: its centre (scene metres), height and reach; Height 0 for none.</summary>
			public Vector2 HillAt;
			public float HillHeight, HillReach;
			/// <summary>How deep the underside hangs below the rim, metres.</summary>
			public float Keel;
			public uint Seed;
			/// <summary>The outline's harmonics: amplitude and phase of each, k = 2 … .</summary>
			public float[] Amplitude = Array.Empty<float>();
			public float[] Phase = Array.Empty<float>();

			/// <summary>The outline's radius toward <paramref name="angle"/> (radians from +x toward +z).</summary>
			public float OutlineAt(float angle)
			{
				float r = 1f;
				for (int k = 0; k < Amplitude.Length; k++)
				{
					r += Amplitude[k] * Mathf.Sin((k + 2) * angle + Phase[k]);
				}
				return Radius * Mathf.Clamp(r, 0.62f, 1.18f);
			}

			/// <summary>The widest the outline reaches, metres.</summary>
			public float Reach => Radius * 1.18f;

			/// <summary>How far in from the outline a point is, as a share of the outline's radius toward it: under 1 inside.</summary>
			public float Inside(float east, float north)
			{
				float dx = east - Centre.x, dz = north - Centre.y;
				float d = Mathf.Sqrt(dx * dx + dz * dz);
				if (d < 1e-4f)
				{
					return 0f;
				}
				return d / OutlineAt(Mathf.Atan2(dz, dx));
			}

			/// <summary>The island's ground at a point (its own surface, extended past its outline to the rim's height).</summary>
			public float GroundAt(float east, float north)
			{
				float t = Mathf.Min(1f, Inside(east, north));
				float y = Height + Dome * (1f - t * t);
				y += Roll * (ValueNoise(east / 38f, north / 38f, Seed) - 0.5f) * 2f;
				y += Roll * 0.4f * (ValueNoise(east / 11f, north / 11f, Seed ^ 0x51u) - 0.5f) * 2f;
				if (HillHeight > 0f)
				{
					float hx = east - HillAt.x, hz = north - HillAt.y;
					float h = Mathf.Exp(-(hx * hx + hz * hz) / (HillReach * HillReach));
					y += HillHeight * h;
				}
				// The edge rounds over, as weathered ground does at a drop.
				float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1f, t));
				return y - edge * Mathf.Min(3f, Radius * 0.04f);
			}
		}

		/// <summary>A rope bridge between two islands: its ends, each a little in from its island's outline.</summary>
		public struct Bridge
		{
			public int A, B;
			/// <summary>Ends in the plane: east, north.</summary>
			public Vector2 FromAt, ToAt;
		}

		/// <summary>An airship landing stage: the island it is on and the way out from it.</summary>
		public struct Dock
		{
			public int Island;
			/// <summary>The outline's point the stage leaves from (east, north).</summary>
			public Vector2 Edge;
			/// <summary>Unit, horizontal, from the island out over the void.</summary>
			public Vector2 Outward;
		}

		/// <summary>Open sky kept from the city's edge out to the scene's: no island is laid in it, so an airship always has a way out.</summary>
		public struct Corridor
		{
			/// <summary>From the city's edge (east, north) to the scene's.</summary>
			public Vector2 From, To;
			/// <summary>Unit, from the city out.</summary>
			public Vector2 Outward;
		}

		/// <summary>
		/// Half the width of a corridor, metres: the room an airship's lane keeps from any island beside it
		/// (SkyLanePlanner.Margin, 45 m) and a little more.
		/// </summary>
		public const float CorridorHalfWidth = 52f;

		/// <summary>A scene's islands, bridges and docks.</summary>
		public sealed class Plan
		{
			public readonly List<Island> Islands = new List<Island>();
			public readonly List<Corridor> Corridors = new List<Corridor>();
			public readonly List<Bridge> Bridges = new List<Bridge>();
			public readonly List<Dock> Docks = new List<Dock>();
			public float Floor, Ceiling;
			public float Width, Depth;

			/// <summary>The island under a point, by the outline (not the heightmap's cells): -1 over the void.</summary>
			public int IslandAt(float east, float north)
			{
				int best = -1;
				float nearest = 1f;
				for (int i = 0; i < Islands.Count; i++)
				{
					Island island = Islands[i];
					float dx = east - island.Centre.x, dz = north - island.Centre.y;
					if (dx * dx + dz * dz > island.Reach * island.Reach)
					{
						continue;
					}
					float t = island.Inside(east, north);
					if (t < nearest)
					{
						nearest = t;
						best = i;
					}
				}
				return best;
			}
		}

		// ── Planning ──────────────────────────────────────────────

		/// <summary>The islands of a scene <paramref name="width"/> × <paramref name="depth"/> metres.</summary>
		public static Plan Lay(float width, float depth, uint seed)
		{
			var plan = new Plan { Width = width, Depth = depth };
			var random = new DeterministicRNG(unchecked((int)(seed * 0x9E3779B1u ^ 0xC10D15u)));
			float size = Mathf.Min(width, depth);
			// The band the islands float in: deeper in a bigger scene, so the farthest are a long way up or down.
			float band = Mathf.Clamp(size * 0.22f, 110f, 560f);
			plan.Floor = DeckTop + FloorAboveDeck;
			plan.Ceiling = plan.Floor + band;
			float halfW = width * 0.5f, halfD = depth * 0.5f;

			bool Fits(Vector2 at, float reach)
			{
				if (Mathf.Abs(at.x) + reach + EdgeMargin > halfW || Mathf.Abs(at.y) + reach + EdgeMargin > halfD)
				{
					return false;
				}
				foreach (Island other in plan.Islands)
				{
					if (Vector2.Distance(at, other.Centre) < reach + other.Reach + ShortestGap)
					{
						return false;
					}
				}
				foreach (Corridor corridor in plan.Corridors)
				{
					if (DistanceToSegment(at, corridor.From, corridor.To) < reach + CorridorHalfWidth)
					{
						return false;
					}
				}
				return true;
			}

			// The city's island, near the middle.
			float cityRadius = Mathf.Clamp(size * 0.2f, 75f, 420f);
			Vector2 cityAt = new Vector2(random.Range(-0.08f, 0.08f) * width, random.Range(-0.08f, 0.08f) * depth);
			Island city = Make(SceneCloudWorld.IslandKind.City, cityAt, cityRadius, Mathf.Lerp(plan.Floor, plan.Ceiling, random.Range(0.3f, 0.5f)), random);
			if (!Fits(cityAt, city.Reach))
			{
				// A scene too small for its city at that size: the city as big as fits in the middle.
				city = Make(SceneCloudWorld.IslandKind.City, Vector2.zero, Mathf.Max(30f, (size * 0.5f - EdgeMargin) / 1.18f), city.Height, random);
			}
			plan.Islands.Add(city);
			Corridors(plan, city, random);

			// Then the rest grown off those already placed, until a third of the sky is island or nothing more fits.
			float area = width * depth, covered = Mathf.PI * city.Radius * city.Radius;
			int misses = 0;
			while (covered < area * 0.3f && misses < 400 && plan.Islands.Count < 400)
			{
				SceneCloudWorld.IslandKind kind = PickKind(random);
				float radius = RadiusFor(kind, size, random);
				Island from = plan.Islands[random.Next(plan.Islands.Count)];
				float angle = random.Range(0f, Mathf.PI * 2f);
				// Mostly a bridge's span off its neighbour; one in four an airship's crossing.
				float gap = random.NextFloat() < 0.75f ? random.Range(ShortestGap + 2f, LongestBridge - 8f) : random.Range(LongestBridge + 10f, LongestBridge + 160f);
				var at = from.Centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (from.Radius + radius + gap);
				Island island = Make(kind, at, radius, 0f, random);
				// A little up or down from its neighbour, never steeper than a bridge can climb when a bridge is in reach.
				float rise = gap * BridgeGrade * 0.8f * random.Range(-1f, 1f);
				island.Height = Mathf.Clamp(from.Height + rise, plan.Floor, plan.Ceiling);
				if (!Fits(at, island.Reach))
				{
					misses++;
					continue;
				}
				plan.Islands.Add(island);
				covered += Mathf.PI * radius * radius;
				misses = 0;
			}

			PlanBridges(plan, random);
			PlanDocks(plan, random);
			return plan;
		}

		/// <summary>
		/// The open sky kept for the airships: one way out from the city's edge to the scene's nearest edge (two in a scene
		/// big enough to spare them), a little off square, which the city's first landing stages face.
		/// </summary>
		private static void Corridors(Plan plan, Island city, DeterministicRNG random)
		{
			int wanted = Mathf.Min(plan.Width, plan.Depth) >= 1200f ? 2 : 1;
			float halfW = plan.Width * 0.5f, halfD = plan.Depth * 0.5f;
			// The four ways out, nearest edge first.
			var ways = new List<(float run, float angle)>
			{
				(halfW - city.Centre.x, 0f),
				(halfD - city.Centre.y, Mathf.PI * 0.5f),
				(halfW + city.Centre.x, Mathf.PI),
				(halfD + city.Centre.y, Mathf.PI * 1.5f),
			};
			ways.Sort((a, b) => a.run.CompareTo(b.run));
			for (int k = 0; k < ways.Count && plan.Corridors.Count < wanted; k++)
			{
				float angle = ways[k].angle + random.Range(-0.3f, 0.3f);
				var outward = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
				Vector2 from = city.Centre + outward * city.OutlineAt(angle);
				float tx = outward.x > 1e-4f ? (halfW - from.x) / outward.x : outward.x < -1e-4f ? (-halfW - from.x) / outward.x : float.PositiveInfinity;
				float tz = outward.y > 1e-4f ? (halfD - from.y) / outward.y : outward.y < -1e-4f ? (-halfD - from.y) / outward.y : float.PositiveInfinity;
				float run = Mathf.Min(tx, tz);
				if (!(run > 0f) || float.IsInfinity(run))
				{
					continue;
				}
				plan.Corridors.Add(new Corridor { From = from, To = from + outward * run, Outward = outward });
			}
		}

		private static SceneCloudWorld.IslandKind PickKind(DeterministicRNG random)
		{
			float roll = random.NextFloat();
			return roll < 0.1f ? SceneCloudWorld.IslandKind.Large
				: roll < 0.35f ? SceneCloudWorld.IslandKind.Medium
				: roll < 0.7f ? SceneCloudWorld.IslandKind.Small
				: SceneCloudWorld.IslandKind.Islet;
		}

		private static float RadiusFor(SceneCloudWorld.IslandKind kind, float size, DeterministicRNG random)
		{
			// The big ones grow with the scene; the small ones are the size they are.
			float scale = Mathf.Clamp(size / 1500f, 0.6f, 2.2f);
			switch (kind)
			{
				case SceneCloudWorld.IslandKind.Large: return random.Range(55f, 110f) * scale;
				case SceneCloudWorld.IslandKind.Medium: return random.Range(30f, 55f) * Mathf.Sqrt(scale);
				case SceneCloudWorld.IslandKind.Small: return random.Range(15f, 30f);
				default: return random.Range(6f, 13f);
			}
		}

		private static Island Make(SceneCloudWorld.IslandKind kind, Vector2 at, float radius, float height, DeterministicRNG random)
		{
			var island = new Island
			{
				Kind = kind,
				Centre = at,
				Radius = radius,
				Height = height,
				Seed = (uint)random.Next(),
			};
			// Ragged in proportion: a big island's outline wanders in bays and headlands, an islet's is a lump.
			int harmonics = kind >= SceneCloudWorld.IslandKind.Large ? 6 : kind >= SceneCloudWorld.IslandKind.Small ? 4 : 3;
			island.Amplitude = new float[harmonics];
			island.Phase = new float[harmonics];
			for (int k = 0; k < harmonics; k++)
			{
				island.Amplitude[k] = random.Range(0.25f, 1f) * 0.16f / (1f + 0.6f * k);
				island.Phase[k] = random.Range(0f, Mathf.PI * 2f);
			}
			bool city = kind == SceneCloudWorld.IslandKind.City;
			// A city's ground is gentle; the wild islands crown and roll more, and the big ones carry a hill.
			island.Dome = radius * (city ? 0.03f : random.Range(0.05f, 0.12f));
			island.Roll = Mathf.Min(city ? 1.2f : 3.5f, radius * 0.05f);
			if (!city && kind >= SceneCloudWorld.IslandKind.Medium && random.NextFloat() < 0.6f)
			{
				float a = random.Range(0f, Mathf.PI * 2f);
				island.HillAt = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * random.Range(0.1f, 0.45f);
				island.HillReach = radius * random.Range(0.25f, 0.4f);
				island.HillHeight = island.HillReach * random.Range(0.3f, 0.7f);
			}
			// Hung below like an upturned mountain: deep under a small island, proportionally shallower under a big one.
			island.Keel = Mathf.Clamp(radius * random.Range(1.1f, 1.8f), 14f, 260f) * (city ? 0.8f : 1f);
			return island;
		}

		/// <summary>
		/// The bridges: every pair of islands a rope bridge can join (short enough, not too steep, nothing in the way),
		/// joined cheapest first into a forest, with a few more for loops.
		/// </summary>
		private static void PlanBridges(Plan plan, DeterministicRNG random)
		{
			var candidates = new List<(int a, int b, float gap)>();
			List<Island> islands = plan.Islands;
			for (int a = 0; a < islands.Count; a++)
			{
				for (int b = a + 1; b < islands.Count; b++)
				{
					if (!TryEnds(islands[a], islands[b], out Vector2 from, out Vector2 to))
					{
						continue;
					}
					float gap = Vector2.Distance(from, to);
					float rise = Mathf.Abs(islands[a].GroundAt(from.x, from.y) - islands[b].GroundAt(to.x, to.y));
					if (gap > LongestBridge || gap < 4f || rise > gap * BridgeGrade || Blocked(plan, a, b, from, to))
					{
						continue;
					}
					candidates.Add((a, b, gap));
				}
			}
			candidates.Sort((x, y) => x.gap.CompareTo(y.gap));
			var parent = new int[islands.Count];
			for (int i = 0; i < parent.Length; i++)
			{
				parent[i] = i;
			}
			int Root(int i)
			{
				while (parent[i] != i)
				{
					parent[i] = parent[parent[i]];
					i = parent[i];
				}
				return i;
			}
			var ends = new int[islands.Count];
			foreach ((int a, int b, float _) in candidates)
			{
				int ra = Root(a), rb = Root(b);
				bool join = ra != rb;
				// A loop now and then, so a cluster is not a single path, but never a crowd of ropes off one islet.
				bool loop = !join && random.NextFloat() < 0.18f && ends[a] < 3 && ends[b] < 3;
				if (!join && !loop)
				{
					continue;
				}
				TryEnds(islands[a], islands[b], out Vector2 from, out Vector2 to);
				if (CrossesABridge(plan, from, to))
				{
					continue;
				}
				if (join)
				{
					parent[ra] = rb;
				}
				plan.Bridges.Add(new Bridge { A = a, B = b, FromAt = from, ToAt = to });
				ends[a]++;
				ends[b]++;
			}
		}

		/// <summary>How far in from its outline a bridge's end stands, metres: its posts on firm ground.</summary>
		public const float BridgeInset = 2.5f;

		/// <summary>A bridge's two ends, each <see cref="BridgeInset"/> in from its island's outline on the line between the centres.</summary>
		private static bool TryEnds(Island a, Island b, out Vector2 from, out Vector2 to)
		{
			Vector2 d = b.Centre - a.Centre;
			float length = d.magnitude;
			from = to = default;
			if (length < 1e-3f)
			{
				return false;
			}
			Vector2 dir = d / length;
			float angleA = Mathf.Atan2(dir.y, dir.x), angleB = Mathf.Atan2(-dir.y, -dir.x);
			from = a.Centre + dir * (a.OutlineAt(angleA) - BridgeInset);
			to = b.Centre - dir * (b.OutlineAt(angleB) - BridgeInset);
			return Vector2.Dot(to - from, dir) > 0f;
		}

		/// <summary>True when a bridge already laid crosses this one.</summary>
		private static bool CrossesABridge(Plan plan, Vector2 from, Vector2 to)
		{
			foreach (Bridge bridge in plan.Bridges)
			{
				if (Crosses(from, to, bridge.FromAt, bridge.ToAt))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>True when a third island stands in the way of a bridge.</summary>
		private static bool Blocked(Plan plan, int a, int b, Vector2 from, Vector2 to)
		{
			for (int i = 0; i < plan.Islands.Count; i++)
			{
				if (i == a || i == b)
				{
					continue;
				}
				Island other = plan.Islands[i];
				if (DistanceToSegment(other.Centre, from, to) < other.Reach + 3f)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The landing stages: on every island big enough for one (the city's several), facing the open sky: the way out
		/// with the fewest islands and bridges near it.
		/// </summary>
		private static void PlanDocks(Plan plan, DeterministicRNG random)
		{
			for (int i = 0; i < plan.Islands.Count; i++)
			{
				Island island = plan.Islands[i];
				int wanted = island.Kind == SceneCloudWorld.IslandKind.City ? 3 : island.Kind == SceneCloudWorld.IslandKind.Large ? 1
					: island.Kind == SceneCloudWorld.IslandKind.Medium && random.NextFloat() < 0.4f ? 1 : 0;
				var taken = new List<float>();
				if (island.Kind == SceneCloudWorld.IslandKind.City)
				{
					// Facing the open sky kept for them, whatever else is about.
					foreach (Corridor corridor in plan.Corridors)
					{
						float angle = Mathf.Atan2(corridor.Outward.y, corridor.Outward.x);
						taken.Add(angle);
						plan.Docks.Add(new Dock { Island = i, Edge = island.Centre + corridor.Outward * island.OutlineAt(angle), Outward = corridor.Outward });
					}
				}
				for (int n = taken.Count; n < wanted; n++)
				{
					float bestAngle = 0f, bestScore = float.NegativeInfinity;
					for (int s = 0; s < 32; s++)
					{
						float angle = (s + random.NextFloat() * 0.5f) / 32f * Mathf.PI * 2f;
						float score = OpenSky(plan, i, angle);
						foreach (float other in taken)
						{
							float apart = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, other * Mathf.Rad2Deg));
							score -= apart < 70f ? 1000f : 0f;
						}
						if (score > bestScore)
						{
							bestScore = score;
							bestAngle = angle;
						}
					}
					if (bestScore < 0f)
					{
						break;
					}
					taken.Add(bestAngle);
					var outward = new Vector2(Mathf.Cos(bestAngle), Mathf.Sin(bestAngle));
					plan.Docks.Add(new Dock { Island = i, Edge = island.Centre + outward * island.OutlineAt(bestAngle), Outward = outward });
				}
			}
		}

		/// <summary>How clear the sky is off an island's edge toward an angle: the clear run (metres, up to 120), or negative when a bridge leaves there.</summary>
		private static float OpenSky(Plan plan, int index, float angle)
		{
			Island island = plan.Islands[index];
			var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
			Vector2 edge = island.Centre + dir * island.OutlineAt(angle);
			foreach (Bridge bridge in plan.Bridges)
			{
				if ((bridge.A == index && Vector2.Distance(bridge.FromAt, edge) < 18f) || (bridge.B == index && Vector2.Distance(bridge.ToAt, edge) < 18f))
				{
					return -1f;
				}
			}
			const float Reach = 120f;
			float clear = Reach;
			for (int i = 0; i < plan.Islands.Count; i++)
			{
				if (i == index)
				{
					continue;
				}
				Island other = plan.Islands[i];
				Vector2 to = other.Centre - edge;
				float along = Vector2.Dot(to, dir);
				if (along < -other.Reach)
				{
					continue;
				}
				float across = Mathf.Abs(to.x * dir.y - to.y * dir.x);
				if (across < other.Reach + 12f)
				{
					clear = Mathf.Min(clear, Mathf.Max(0f, along - other.Reach));
				}
			}
			// Out past the scene's edge is open sky too, but a stage that faces the edge from right beside it is no use.
			float toEdgeX = dir.x > 1e-4f ? (plan.Width * 0.5f - edge.x) / dir.x : dir.x < -1e-4f ? (-plan.Width * 0.5f - edge.x) / dir.x : float.PositiveInfinity;
			float toEdgeZ = dir.y > 1e-4f ? (plan.Depth * 0.5f - edge.y) / dir.y : dir.y < -1e-4f ? (-plan.Depth * 0.5f - edge.y) / dir.y : float.PositiveInfinity;
			clear = Mathf.Min(clear, Mathf.Min(toEdgeX, toEdgeZ) - 10f);
			return clear < 30f ? -1f : clear;
		}

		// ── Shaping ───────────────────────────────────────────────

		/// <summary>
		/// The islands' ground in <paramref name="field"/>, and which of its cells are island: true where solid, row by row
		/// from the south, (Width − 1) × (Depth − 1) cells. Under the void the heights are the nearest island's rim.
		/// </summary>
		public static bool[] Shape(Plan plan, SceneHeightField field)
		{
			int width = field.Width, depth = field.Depth;
			var owner = new int[width * depth];
			float[] metres = field.Metres;
			// A coarse grid of which islands reach each bucket, so a sample asks only those near it.
			const float Bucket = 64f;
			float halfW = field.Plan.WidthMetres * 0.5f, halfD = field.Plan.DepthMetres * 0.5f;
			int bx = Mathf.Max(1, Mathf.CeilToInt(field.Plan.WidthMetres / Bucket)), bz = Mathf.Max(1, Mathf.CeilToInt(field.Plan.DepthMetres / Bucket));
			var buckets = new List<int>[bx * bz];
			// Far enough past an island's outline that the void beside it takes its rim's height.
			const float Skirt = 24f;
			for (int i = 0; i < plan.Islands.Count; i++)
			{
				Island island = plan.Islands[i];
				float r = island.Reach + Skirt;
				int x0 = Mathf.Clamp(Mathf.FloorToInt((island.Centre.x - r + halfW) / Bucket), 0, bx - 1), x1 = Mathf.Clamp(Mathf.FloorToInt((island.Centre.x + r + halfW) / Bucket), 0, bx - 1);
				int z0 = Mathf.Clamp(Mathf.FloorToInt((island.Centre.y - r + halfD) / Bucket), 0, bz - 1), z1 = Mathf.Clamp(Mathf.FloorToInt((island.Centre.y + r + halfD) / Bucket), 0, bz - 1);
				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						(buckets[z * bx + x] ??= new List<int>()).Add(i);
					}
				}
			}
			float floor = plan.Floor;
			Parallel.For(0, depth, z =>
			{
				float north = field.NorthOf(z);
				int bzi = Mathf.Clamp(Mathf.FloorToInt((north + halfD) / Bucket), 0, bz - 1);
				for (int x = 0; x < width; x++)
				{
					float east = field.EastOf(x);
					int bxi = Mathf.Clamp(Mathf.FloorToInt((east + halfW) / Bucket), 0, bx - 1);
					List<int> near = buckets[bzi * bx + bxi];
					int best = -1;
					float nearest = float.PositiveInfinity;
					if (near != null)
					{
						foreach (int i in near)
						{
							float t = plan.Islands[i].Inside(east, north);
							if (t < nearest)
							{
								nearest = t;
								best = i;
							}
						}
					}
					int s = z * width + x;
					if (best < 0)
					{
						metres[s] = floor;
						owner[s] = -1;
						continue;
					}
					metres[s] = plan.Islands[best].GroundAt(east, north);
					owner[s] = nearest < 1f ? best : -1;
				}
			});

			int cellsX = width - 1, cellsZ = depth - 1;
			var solid = new bool[cellsX * cellsZ];
			for (int z = 0; z < cellsZ; z++)
			{
				for (int x = 0; x < cellsX; x++)
				{
					int o = owner[z * width + x];
					solid[z * cellsX + x] = o >= 0 && owner[z * width + x + 1] == o && owner[(z + 1) * width + x] == o && owner[(z + 1) * width + x + 1] == o;
				}
			}
			return solid;
		}

		// ── Geometry ──────────────────────────────────────────────

		public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
		{
			Vector2 ab = b - a;
			float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
			return Vector2.Distance(p, a + ab * t);
		}

		private static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
		{
			float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;
			float d1 = Cross(b - a, c - a), d2 = Cross(b - a, d - a), d3 = Cross(d - c, a - c), d4 = Cross(d - c, b - c);
			return d1 * d2 < 0f && d3 * d4 < 0f;
		}

		/// <summary>Smooth value noise in [0, 1] on a unit lattice, seeded.</summary>
		public static float ValueNoise(float x, float y, uint seed)
		{
			int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
			float fx = x - ix, fy = y - iy;
			fx = fx * fx * (3f - 2f * fx);
			fy = fy * fy * (3f - 2f * fy);
			float a = Lattice(ix, iy, seed), b = Lattice(ix + 1, iy, seed), c = Lattice(ix, iy + 1, seed), d = Lattice(ix + 1, iy + 1, seed);
			return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
		}

		/// <summary>Smooth value noise in [0, 1] in three dimensions, seeded.</summary>
		public static float ValueNoise(float x, float y, float z, uint seed)
		{
			int iz = Mathf.FloorToInt(z);
			float fz = z - iz;
			fz = fz * fz * (3f - 2f * fz);
			return Mathf.Lerp(ValueNoise(x, y, seed ^ (uint)(iz * 0x27D4EB2D)), ValueNoise(x, y, seed ^ (uint)((iz + 1) * 0x27D4EB2D)), fz);
		}

		private static float Lattice(int x, int y, uint seed)
		{
			unchecked
			{
				uint h = seed ^ (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u;
				h ^= h >> 15;
				h *= 0x2C1B3C6Du;
				h ^= h >> 12;
				h *= 0x297A2D39u;
				h ^= h >> 15;
				return (h & 0xFFFFFF) / 16777215f;
			}
		}
	}
}
#endif
