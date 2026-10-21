#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What hangs from a cave's roof and what piles under it (2026-10-20, caves livened): glow-worm silk fringes along a damp
	/// roof joint, long tap roots that come down through a thin roof to the floor, and the guano heaps under a bat roost.
	/// Built as the rest of the glow art is (<see cref="Build"/>), three levels each; their kinds, hues and materials are
	/// ProceduralArtCatalogue.Cave.cs's (<see cref="CaveGlowKind.GlowSilk"/>, <see cref="CaveGlowKind.TapRoots"/>,
	/// <see cref="CaveGlowKind.GuanoHeap"/>).
	/// </summary>
	public static partial class CaveGlowMeshes
	{
		// ── Glow-worm silk ────────────────────────────────────────────

		/// <summary>The silk fringes' joint lengths along x (v0, v1) and a drip point's spread (v2), metres.</summary>
		public static readonly float[] GlowSilkSpans = { 2.4f, 3.6f, 1.2f };

		/// <summary>
		/// Glow-worm silk along a roof joint (2026-10-20): where water seeps through a crack in a damp roof the larvae line it,
		/// each hanging its snare straight down in the still air, beaded with sticky droplets evenly spaced as the mucus beads
		/// along a thread. The colony's snares (<see cref="GlowWorms"/>) are a few hand-spans long; these are the long ones of a
		/// fantasy cave's oldest colonies, 0.25–1.6 m, so a fringe reads from across a passage as lines of light. The larva at
		/// each top is the brightest (glow 1), the droplets catch it (brighter lower down, where they gather), the thread is
		/// faint. v0 a fringe along a 2.4 m joint, v1 a broad band along 3.6 m (two joints side by side), v2 a cluster under a
		/// drip point 1.2 m across, longest in its middle like a chandelier. Along x; the dressing turns x along the joint.
		/// Each larva is a rod reaching <see cref="GlowWormRoot"/> up into the roof, so it shows as a speck on any roof within that.
		/// </summary>
		private static MeshBuilder GlowSilk(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			float shortest, longest, width;
			switch (variant)
			{
				case 1: count = 52; shortest = 0.3f; longest = 1.4f; width = 0.5f; break;
				case 2: count = 28; shortest = 0.4f; longest = 1.6f; width = GlowSilkSpans[2]; break;
				default: count = 36; shortest = 0.25f; longest = 1f; width = 0.14f; break;
			}
			float span = GlowSilkSpans[Mathf.Clamp(variant, 0, GlowSilkSpans.Length - 1)];
			var threads = new List<(Vector3 top, float length, int seed)>(count);
			int guard = 0;
			while (threads.Count < count && guard++ < count * 40)
			{
				Vector3 top;
				float share;
				if (variant == 2)
				{
					// Round a drip point: thick in the middle, thinning out; the middle's the longest.
					float a = rng.Range(0f, Tau), r = 0.5f * span * Mathf.Sqrt(rng.NextFloat());
					top = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
					share = 1f - r / (0.5f * span);
				}
				else
				{
					// Along the joint in clumps with gaps (where the seep is), a band of the joint's width across it; on v1 two joints.
					float x = rng.Range(-0.5f, 0.5f) * span;
					float joint = variant == 1 ? (rng.NextFloat() < 0.5f ? -0.5f : 0.5f) * width : 0f;
					float z = joint + rng.Range(-0.5f, 0.5f) * (variant == 1 ? 0.18f : width);
					float clump = 0.5f + 0.5f * ProceduralNoise.Fbm3(new Vector3(x * 1.4f, 0f, joint * 3f), 3, 0.5f, seed + 7);
					if (rng.NextFloat() > Mathf.Clamp01(clump * 1.6f - 0.2f))
					{
						continue;
					}
					top = new Vector3(x, 0f, z);
					share = rng.NextFloat();
				}
				float u = rng.NextFloat();
				float length = variant == 2 ? Mathf.Lerp(shortest, longest, Mathf.Clamp01(0.3f * u + 0.8f * share))
					: Mathf.Lerp(shortest, longest, u * u);
				threads.Add((top, length, rng.Next()));
			}
			// Longest first, so a coarser level keeps the ones that read from afar.
			threads.Sort((a, b) => b.length.CompareTo(a.length));
			var m = new MeshBuilder(1);
			int keep = lod == 0 ? threads.Count : lod == 1 ? threads.Count * 2 / 3 : Mathf.Max(4, threads.Count / 3);
			float thread = lod == 0 ? 0.0018f : lod == 1 ? 0.0026f : 0.0035f;
			for (int i = 0; i < keep && i < threads.Count; i++)
			{
				(Vector3 at, float length, int tseed) = threads[i];
				var prng = new DeterministicRNG(tseed);
				// The larva: a speck where it lies against the roof, its rod seating it.
				Tube(m, 0, new List<Vector3> { at + Vector3.up * GlowWormRoot, at + Vector3.down * 0.014f }, new[] { 0.005f, 0.005f }, new[] { 1f, 1f }, lod == 0 ? 4 : 3, false, true);
				// The thread hangs plumb in the still air, a hair's sway off it at its end.
				Vector3 bottom = at + new Vector3(prng.Range(-0.008f, 0.008f), -length, prng.Range(-0.008f, 0.008f));
				Tube(m, 0, new List<Vector3> { at + Vector3.up * GlowWormRoot * 0.8f, bottom }, new[] { thread, thread }, new[] { 0.32f, 0.42f }, 3, false, false);
				// The droplets, evenly spaced as mucus beads on a thread; fewer and bigger on a coarser level (what a pixel shows).
				float pitch = lod == 0 ? 0.06f : lod == 1 ? 0.12f : 0.24f;
				int beads = Mathf.Max(1, Mathf.FloorToInt(length / pitch));
				float grow = lod == 0 ? 1f : lod == 1 ? 1.4f : 1.9f;
				float offset = prng.Range(0.2f, 0.8f);
				for (int k = 0; k < beads; k++)
				{
					float t = Mathf.Clamp01((k + offset) / beads);
					float size = Mathf.Lerp(0.0028f, 0.0045f, t) * grow * prng.Range(0.85f, 1.15f);
					Bead(m, 0, Vector3.Lerp(at, bottom, t), new Vector3(size, size * 1.3f, size), Mathf.Lerp(0.8f, 0.97f, t), 3, 2);
				}
			}
			return m;
		}

		// ── Tap roots ─────────────────────────────────────────────────

		/// <summary>How far a tap root hangs at scale 1, metres: the dressing stretches it in y to the floor under it.</summary>
		public const float TapRootLength = 4f;

		/// <summary>
		/// Tree roots that have come down through a thin roof all the way to the floor (2026-10-20), as roots do in a shallow cave
		/// under a wood: they follow the joints down, hang free across the void, and where they reach the floor spread over it in
		/// rootlets feeling for water. Hung from the roof at the origin, <see cref="TapRootLength"/> long (the dressing scales
		/// y so their feet stand on the floor; their horizontal rootlets are unchanged by it). Not lit: the root material (pale
		/// at the growing tips). v0 three thick tap roots kinking down, forking; v1 a curtain of a dozen thin roots, half of them
		/// reaching the floor and the rest ending in hair; v2 a column of five roots twisted round each other like a rope, fanned
		/// out at the roof and splayed at the foot.
		/// </summary>
		private static MeshBuilder TapRoots(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			var m = new MeshBuilder(1);
			int stations = lod == 0 ? 11 : lod == 1 ? 7 : 5, sides = lod == 0 ? 5 : lod == 1 ? 4 : 3;
			var path = new List<Vector3>();
			var radius = new List<float>();
			var glow = new List<float>();
			if (variant == 2)
			{
				RootColumn(m, rng, lod, stations * 2 - 1, sides);
				return m;
			}
			int count = variant == 1 ? 12 : 3;
			Vector2 size = variant == 1 ? new Vector2(2f, 0.6f) : new Vector2(1f, 0.6f);
			float thick = variant == 1 ? 0.03f : 0.08f, thin = variant == 1 ? 0.015f : 0.05f;
			var roots = new List<Strand>(count);
			for (int i = 0; i < count; i++)
			{
				// The first half of a curtain reach the floor; the rest stop short in a tuft of hair.
				bool reaches = variant == 0 || i % 2 == 0;
				roots.Add(new Strand
				{
					Top = new Vector3(rng.Range(-0.5f, 0.5f) * size.x, 0f, rng.Range(-0.5f, 0.5f) * size.y),
					Length = reaches ? TapRootLength : TapRootLength * rng.Range(0.35f, 0.85f),
					Radius = rng.Range(thin, thick),
					Seed = rng.Next(),
				});
			}
			roots.Sort((a, b) => b.Radius.CompareTo(a.Radius));
			int keep = lod == 0 ? count : lod == 1 ? Mathf.Max(3, count * 2 / 3) : Mathf.Max(2, count / 3);
			var fork = new List<Vector3>();
			var forkRadius = new List<float>();
			var forkGlow = new List<float>();
			for (int i = 0; i < keep; i++)
			{
				Strand s = roots[i];
				// Roots hang straighter than the near-mouth tangle (their weight pulls them plumb over metres).
				HangingPath(in s, stations, 0.18f, s.Length >= TapRootLength - 1e-3f ? 0.55f : 0.2f, 0.3f, 0.55f, path, radius, glow);
				if (s.Length >= TapRootLength - 1e-3f)
				{
					// Its foot exactly on the floor's line, whatever the sway put it at.
					Vector3 end = path[path.Count - 1];
					path[path.Count - 1] = new Vector3(end.x, -TapRootLength, end.z);
				}
				Tube(m, 0, path, radius, glow, sides, false, true);
				var prng = new DeterministicRNG(s.Seed ^ 0x6e2b);
				if (lod < 2 && s.Radius > 0.025f)
				{
					// A fork or two off it, thinner, ending in the air.
					int forks = prng.Range(1, 3);
					for (int f = 0; f < forks; f++)
					{
						int from = Mathf.Clamp(prng.Range(1, stations - 2), 1, stations - 2);
						var branch = new Strand { Top = path[from], Length = prng.Range(0.4f, 1.1f), Radius = radius[from] * 0.45f, Seed = prng.Next() };
						HangingPath(in branch, lod == 0 ? 4 : 3, 0.3f, 0.2f, glow[from], 0.6f, fork, forkRadius, forkGlow);
						fork[0] = path[from];
						Tube(m, 0, fork, forkRadius, forkGlow, Mathf.Max(3, sides - 1), false, true);
					}
				}
				Vector3 foot = path[path.Count - 1];
				if (s.Length >= TapRootLength - 1e-3f)
				{
					// Where it meets the floor it spreads over it in rootlets, a little into it (the floor is not flat).
					if (lod < 2)
					{
						FloorRootlets(m, prng, foot, radius[radius.Count - 1], lod == 0 ? 4 : 2, Mathf.Max(3, sides - 1));
					}
				}
				else if (lod == 0)
				{
					// A tuft of hair roots at a root's end in the air.
					for (int k = 0; k < 3; k++)
					{
						var d = new Vector3(prng.Range(-1f, 1f), -1.6f, prng.Range(-1f, 1f)).normalized;
						Tube(m, 0, new List<Vector3> { foot, foot + d * prng.Range(0.1f, 0.25f) }, new[] { radius[radius.Count - 1] * 0.7f, 0.0025f }, new[] { 0.55f, 0.62f }, 3, false, false);
					}
				}
			}
			if (lod < 2)
			{
				// Runners along the roof between the tops, half sunk in it.
				for (int k = 0; k + 1 < Mathf.Min(count, 4); k++)
				{
					Vector3 a = roots[k].Top, b = roots[k + 1].Top;
					if ((b - a).sqrMagnitude < 0.04f)
					{
						continue;
					}
					var rp = new List<Vector3> { a + Vector3.up * 0.07f, (a + b) * 0.5f + Vector3.down * 0.02f, b + Vector3.up * 0.07f };
					Tube(m, 0, rp, new[] { thick * 0.8f, thick * 0.9f, thick * 0.8f }, new[] { 0.28f, 0.3f, 0.28f }, sides, true, true);
				}
			}
			return m;
		}

		/// <summary>
		/// Rootlets spread over the floor from a root's foot: each runs out a little way, kinking, sinking a few centimetres into
		/// the floor at its end so it never stands off a floor that falls away.
		/// </summary>
		private static void FloorRootlets(MeshBuilder m, DeterministicRNG rng, Vector3 foot, float footRadius, int count, int sides)
		{
			float turn = rng.Range(0f, Tau);
			for (int k = 0; k < count; k++)
			{
				float a = turn + Tau * (k + rng.Range(-0.25f, 0.25f)) / count, reach = rng.Range(0.3f, 0.8f);
				var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				var side = new Vector3(-dir.z, 0f, dir.x);
				var p = new List<Vector3>
				{
					foot + Vector3.up * 0.04f,
					foot + dir * (0.35f * reach) + side * rng.Range(-0.06f, 0.06f) + Vector3.down * 0.005f,
					foot + dir * (0.7f * reach) + side * rng.Range(-0.1f, 0.1f) + Vector3.down * 0.02f,
					foot + dir * reach + side * rng.Range(-0.12f, 0.12f) + Vector3.down * 0.05f,
				};
				float r0 = Mathf.Max(0.006f, footRadius * 0.8f);
				Tube(m, 0, p, new[] { r0, r0 * 0.6f, r0 * 0.4f, 0.003f }, new[] { 0.5f, 0.55f, 0.6f, 0.62f }, sides, false, false);
			}
		}

		/// <summary>
		/// Five roots twisted round one another down <see cref="TapRootLength"/> like a rope (where they followed one joint for
		/// metres and grew together), fanned out over the roof at the top and splayed over the floor at the foot.
		/// </summary>
		private static void RootColumn(MeshBuilder m, DeterministicRNG rng, int lod, int stations, int sides)
		{
			const int strands = 5;
			float twist = rng.Range(1.2f, 2f) * (rng.NextFloat() < 0.5f ? -1f : 1f), lean = rng.Range(-0.15f, 0.15f);
			int keep = lod == 2 ? 3 : strands;
			for (int k = 0; k < keep; k++)
			{
				float phase = Tau * k / strands + rng.Range(-0.2f, 0.2f), r = rng.Range(0.03f, 0.045f);
				var path = new List<Vector3>(stations);
				var radius = new List<float>(stations);
				var glow = new List<float>(stations);
				for (int i = 0; i < stations; i++)
				{
					float t = i / (float)(stations - 1);
					// The bundle's own radius: fanned at the roof, tight down its length, splayed at the foot.
					float spread = 0.07f + 0.35f * Mathf.Pow(1f - Mathf.Clamp01(t / 0.12f), 2f) + 0.18f * Mathf.Pow(Mathf.Clamp01((t - 0.9f) / 0.1f), 2f);
					float a = phase + Tau * twist * t;
					float y = i == 0 ? RootSeat : -TapRootLength * t;
					path.Add(new Vector3(Mathf.Cos(a) * spread + lean * t, y, Mathf.Sin(a) * spread));
					radius.Add(r * Mathf.Lerp(1.1f, 0.8f, t));
					glow.Add(Mathf.Lerp(0.3f, 0.5f, t));
				}
				Tube(m, 0, path, radius, glow, sides, false, true);
				if (lod < 2)
				{
					FloorRootlets(m, new DeterministicRNG(rng.Next()), path[path.Count - 1], r, lod == 0 ? 2 : 1, Mathf.Max(3, sides - 1));
				}
			}
		}

		// ── Guano ─────────────────────────────────────────────────────

		/// <summary>The heaps' radii at scale 1 (v0, v1, v2), metres.</summary>
		public static readonly float[] GuanoRadii = { 0.7f, 1.3f, 1.0f };

		/// <summary>
		/// A heap of bat guano under a roost (2026-10-20): droppings pile where a colony hangs year after year, a low mound with a
		/// ragged edge, steepening into cones under the spots most hung from (guano stalagmites), the fresh top darker and moist,
		/// the old crust round its foot pale and dry. A floor piece, origin at its foot's middle; its skirt runs a little under
		/// the floor so it seats on uneven rock. The glow coordinate is only its brightness through the ramp (it does not glow): low
		/// on the fresh top, high on the dry crust. v0 a heap 0.7 m round, 0.3 m high, peaked; v1 a broad low spread 1.3 m round
		/// with four small cones; v2 an old crusted heap 1 m round, 0.4 m high, terraced as its crust slumped, with two cones.
		/// </summary>
		private static MeshBuilder GuanoHeap(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			float radius = GuanoRadii[Mathf.Clamp(variant, 0, GuanoRadii.Length - 1)];
			float height = variant == 1 ? 0.12f : variant == 2 ? 0.4f : 0.3f;
			int coneCount = variant == 1 ? 4 : variant == 2 ? 2 : 1;
			var cones = new List<(Vector2 at, float r, float h)>(coneCount);
			for (int i = 0; i < coneCount; i++)
			{
				float a = rng.Range(0f, Tau), d = variant == 0 ? 0f : rng.Range(0.15f, 0.55f) * radius;
				cones.Add((new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d, rng.Range(0.12f, 0.22f) * (variant == 1 ? 1.4f : 1f), variant == 1 ? rng.Range(0.08f, 0.18f) : rng.Range(0.1f, 0.2f)));
			}
			int noiseSeed = rng.Next();
			int rings = lod == 0 ? 12 : lod == 1 ? 8 : 5, segments = lod == 0 ? 28 : lod == 1 ? 16 : 10;
			const float Skirt = 0.08f;
			var m = new MeshBuilder(1);
			// The height of the heap over its foot at a point in plan (0 at its edge and beyond).
			float Height(float x, float z, out float edgeShare)
			{
				float a = Mathf.Atan2(z, x);
				float edge = radius * (0.85f + 0.15f * ProceduralNoise.Fbm3(new Vector3(Mathf.Cos(a) * 1.5f, 0f, Mathf.Sin(a) * 1.5f), 3, 0.5f, noiseSeed));
				float r = Mathf.Sqrt(x * x + z * z) / Mathf.Max(0.01f, edge);
				edgeShare = Mathf.Clamp01(r);
				if (r >= 1f)
				{
					return 0f;
				}
				// A mound's profile: rounded at the top, its foot running out thin.
				float h = height * Mathf.Pow(1f - r * r, 1.6f);
				if (variant == 2)
				{
					// The old crust slumped in steps.
					float steps = h / height * 3f;
					h = height / 3f * (Mathf.Floor(steps) + Mathf.SmoothStep(0f, 1f, steps - Mathf.Floor(steps)));
				}
				foreach ((Vector2 at, float cr, float ch) in cones)
				{
					float d = Vector2.Distance(new Vector2(x, z), at) / cr;
					if (d < 1f)
					{
						h += ch * Mathf.Pow(1f - d, 1.5f);
					}
				}
				h *= 1f + 0.18f * ProceduralNoise.Fbm3(new Vector3(x * 6f, 0f, z * 6f), 2, 0.5f, noiseSeed + 1);
				return h * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - r) * 5f));
			}
			// Its brightness through the ramp: dark and fresh on top, paler toward the dry crust at its foot, mottled.
			float Level(float x, float z, float h, float edgeShare)
			{
				float mottle = ProceduralNoise.Fbm3(new Vector3(x * 9f, 1f, z * 9f), 2, 0.5f, noiseSeed + 2);
				return Mathf.Clamp01(0.3f + 0.45f * edgeShare - 0.25f * Mathf.Clamp01(h / Mathf.Max(0.01f, height)) + 0.12f * mottle);
			}
			int centre = m.AddVertex(new Vector3(0f, Height(0f, 0f, out _), 0f), Vector3.up, new Vector2(Glow(Level(0f, 0f, height, 0f)), 0f), White);
			var ringStart = new int[rings + 1];
			for (int i = 1; i <= rings; i++)
			{
				// Rings packed toward the foot, where the slope turns; the last is the skirt under the floor.
				float t = i / (float)rings;
				bool skirt = i == rings;
				float rr = skirt ? radius * 1.12f : radius * 1.02f * Mathf.Sqrt(t * (2f - t));
				ringStart[i] = m.VertexCount;
				for (int k = 0; k <= segments; k++)
				{
					float a = Tau * k / segments;
					float x = Mathf.Cos(a) * rr, z = Mathf.Sin(a) * rr;
					float h = skirt ? -Skirt : Height(x, z, out _);
					float level = skirt ? 0.75f : Level(x, z, h, Mathf.Clamp01(rr / radius));
					// v in metres round, so the ramp's mottle streaks round the heap as its crust does.
					m.AddVertex(new Vector3(x, h, z), Vector3.up, new Vector2(Glow(level), Tau * rr * k / segments), White);
				}
			}
			for (int k = 0; k < segments; k++)
			{
				m.AddTriangle(0, centre, ringStart[1] + k, ringStart[1] + k + 1, Vector3.up);
			}
			for (int i = 1; i < rings; i++)
			{
				for (int k = 0; k < segments; k++)
				{
					m.AddQuad(0, ringStart[i] + k, ringStart[i] + k + 1, ringStart[i + 1] + k + 1, ringStart[i + 1] + k, Vector3.up);
				}
			}
			m.RecalculateNormals(true);
			return m;
		}
	}
}
#endif
