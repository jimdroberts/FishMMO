#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// An ice cave's own ice (2026-10-20, caves livened): frozen waterfalls down its walls and drops, icicle curtains along its
	/// roof's edges, frozen pools level on its floors and hoarfrost feathering the walls near them. Glow art like the rest of the
	/// kit (<see cref="CaveGlowKind.IceFall"/>, <see cref="CaveGlowKind.IcicleCurtain"/>, <see cref="CaveGlowKind.FrozenPool"/>,
	/// <see cref="CaveGlowKind.Hoarfrost"/>): the glow ramp's u is how bright and how deep the ice glows, faintly blue in the
	/// dark (an ice cave's glow-ice, CaveHabitat), its material the "Ice" set's (<see cref="ProceduralArtCatalogue.IceHues"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>How ice grows in a cave.</b> Meltwater seeping from a crack or over a lip freezes as it runs, layer on layer, into fused
	/// columns that bulge where it pooled and build a cone at the foot (an ice fall); water dripping off the edge of a roof step
	/// freezes into a fringe of rippled icicles, longest where the most runs off; a pool freezes from its edge in, the bubbles
	/// trapped in rings, its surface dead level and glassy; and moist air meeting the frozen rock grows feathery hoar crystals on
	/// the walls round all of it.
	/// </para>
	/// <para>
	/// <b>Mounts.</b> A fall is a wall piece (origin on the wall at the face's foot, +z into the rock, standing out along −z, up +y,
	/// <see cref="IceFallHeight"/> tall at scale 1, scaled in y to its face); its lip and back reach into the rock, its foot's cone
	/// into the floor. A curtain hangs from its roof (origin on the roof, −y), along x, its longest icicle the variant's
	/// <see cref="IcicleDrops"/>. A pool lies on the floor with its surface <see cref="FrozenPoolRise"/> over the origin, at most 1 across x and z at scale 1
	/// (scaled in x and z to the pool, never in y), its rim bevelled <see cref="FrozenPoolBevel"/> down and its skirt
	/// <see cref="FrozenPoolSkirt"/> into the rock under it, so it meets a basin's side or a floor without a step or a gap. Hoarfrost
	/// is a wall patch, every crystal rooted in the rock.
	/// </para>
	/// </remarks>
	public static partial class CaveGlowMeshes
	{
		/// <summary>An ice fall's height at scale 1, metres: the placer scales y by its face's height over this.</summary>
		public const float IceFallHeight = 4f;
		/// <summary>An ice fall's width along x at scale 1 by variant, metres.</summary>
		public static readonly float[] IceFallWidths = { 1.4f, 3f, 2.6f };
		/// <summary>How far an ice fall's foot cone reaches out from its wall at scale 1 by variant (along −z), metres.</summary>
		public static readonly float[] IceFallAprons = { 0.95f, 1.05f, 1.3f };
		/// <summary>An icicle curtain's longest icicle at scale 1 by variant, metres.</summary>
		public static readonly float[] IcicleDrops = { 1f, 1.6f, 2.2f };
		/// <summary>An icicle curtain's length along x at scale 1 by variant, metres.</summary>
		public static readonly float[] IcicleSpans = { 2f, 2.4f, 1.4f };
		/// <summary>How far a frozen pool's surface stands over its origin, metres (never scaled): the placer sets the origin this far under the ice's surface.</summary>
		public const float FrozenPoolRise = 0.06f;
		/// <summary>How far a frozen pool's rim drops from its surface to where it meets the rock, metres (never scaled).</summary>
		public const float FrozenPoolBevel = 0.1f;
		/// <summary>How far a frozen pool's skirt reaches down under its rim into the rock, metres.</summary>
		public const float FrozenPoolSkirt = 0.6f;
		/// <summary>The share of a frozen pool's outline its flat surface covers before the bevel begins.</summary>
		public const float FrozenPoolFlat = 0.93f;

		// ── Ice falls ─────────────────────────────────────────────────

		/// <summary>
		/// A frozen waterfall: fused columns of ice down a wall, each bulging where the water pooled as it froze (every 0.6–0.9 m),
		/// standing further out toward the foot, where they merge into a cone of ice spread over the floor; a rounded lip of ice
		/// where the water came over at the top. v0 a stout fall of four columns 1.4 m wide; v1 a broad curtain of nine, 3 m wide,
		/// some ending short in icicle points; v2 two streams 2.6 m apart at the top meeting in one great cone.
		/// </summary>
		private static MeshBuilder IceFalls(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			variant = Mathf.Clamp(variant, 0, IceFallWidths.Length - 1);
			float height = IceFallHeight, width = IceFallWidths[variant], apron = IceFallAprons[variant];
			int columns = variant == 0 ? 4 : variant == 1 ? 9 : 7;
			// Every column is drawn before any level is: the levels differ only in tessellation and how many short ones they keep.
			var cols = new List<(float x, float radius, float end, float phase, float bulge, float lean)>(columns);
			for (int i = 0; i < columns; i++)
			{
				float t = columns == 1 ? 0.5f : i / (columns - 1f);
				float x;
				if (variant == 2)
				{
					// Two streams, each its own tight group, converging toward the cone.
					bool left = i < columns / 2 + 1;
					x = (left ? -0.36f : 0.36f) * width + rng.Range(-0.12f, 0.12f) * width;
				}
				else
				{
					x = (t - 0.5f) * width * 0.82f + rng.Range(-0.05f, 0.05f) * width;
				}
				float radius = rng.Range(0.13f, 0.22f) * (variant == 0 ? 1.35f : 1f);
				// A share of a broad curtain's columns stop short, ending in a point (the water failed before it reached the floor).
				float end = variant == 1 && i % 3 == 1 ? rng.Range(0.45f, 0.75f) : 1f;
				cols.Add((x, radius, end, rng.Range(0f, Tau), rng.Range(0.12f, 0.24f), rng.Range(-0.04f, 0.04f)));
			}
			float lipPhase = rng.Range(0f, Tau);
			int stations = lod == 0 ? 18 : lod == 1 ? 11 : 6;
			int sides = lod == 0 ? 10 : lod == 1 ? 7 : 5;
			var m = new MeshBuilder(1);
			for (int i = 0; i < cols.Count; i++)
			{
				(float x, float radius, float end, float phase, float bulge, float lean) c = cols[i];
				if (lod == 2 && c.end < 1f)
				{
					continue;
				}
				var path = new List<Vector3>(stations + 1);
				var radii = new List<float>(stations + 1);
				var glow = new List<float>(stations + 1);
				bool shortOne = c.end < 1f;
				for (int k = 0; k <= stations; k++)
				{
					float t = c.end * k / stations;
					// From up in the lip (into the rock) down to inside the cone at the foot.
					float y = height + 0.12f - (height + 0.02f) * t;
					float flow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.2f));
					// Bulges where the water pooled as it froze, a little irregular: the cauliflower surface of a frozen fall.
					float swell = 1f + c.bulge * Mathf.Sin(c.phase + y * 8.6f) + 0.5f * c.bulge * Mathf.Sin(1.7f * c.phase + y * 15f);
					float r = c.radius * (0.7f + 0.45f * t) * swell;
					if (shortOne)
					{
						// An icicle's point where it stops.
						float tip = Mathf.InverseLerp(0.7f * c.end, c.end, t);
						r *= Mathf.Lerp(1f, 0.08f, tip * tip);
					}
					// Out of the rock at the lip, standing further out toward the foot (the ice builds out as it thickens); the
					// column's back stays in the rock.
					float z = Mathf.Min(0.2f - (0.2f + 0.55f * r + 0.14f * t * t) * flow, 0.17f - r);
					float xs = c.x * Mathf.Lerp(1f, variant == 2 ? 0.45f : 0.85f, t * t) + 0.06f * Mathf.Sin(c.phase * 1.3f + 4f * t) + c.lean * height * t;
					path.Add(new Vector3(xs, y, z));
					radii.Add(r);
					// Clear ice glows deepest where it is thickest: a little brighter in the bulges and toward the foot.
					glow.Add(Mathf.Clamp01(0.26f + 0.5f * (swell - 1f) + 0.18f * t));
				}
				Tube(m, 0, path, radii, glow, sides, false, true);
			}
			// The lip of ice where the water came over: a rounded roll along the top, its back in the rock.
			{
				int n = lod == 0 ? 12 : lod == 1 ? 7 : 4;
				var path = new List<Vector3>(n + 1);
				var radii = new List<float>(n + 1);
				var glow = new List<float>(n + 1);
				float half = 0.5f * width * (variant == 2 ? 1.05f : 0.92f);
				for (int k = 0; k <= n; k++)
				{
					float t = k / (float)n;
					path.Add(new Vector3(Mathf.Lerp(-half, half, t), height + 0.05f + 0.04f * Mathf.Sin(lipPhase + 9f * t), -0.02f));
					radii.Add(0.13f * (0.75f + 0.5f * Mathf.Sin(Mathf.PI * t)));
					glow.Add(0.22f);
				}
				Tube(m, 0, path, radii, glow, lod == 0 ? 8 : 5, true, true);
			}
			// The cone at the foot: the ice that ran down built out over the floor, its back in the wall, its rim into the floor.
			int seg = lod == 0 ? 18 : lod == 1 ? 12 : 8;
			float coneWide = 0.5f * width * (variant == 1 ? 0.9f : 1.05f);
			var cone = new List<LP>
			{
				new LP(1f, -0.25f, 0.2f), new LP(0.97f, 0f, 0.22f), new LP(0.78f, 0.22f, 0.28f, false),
				new LP(0.5f, 0.42f, 0.34f), new LP(0.22f, 0.55f, 0.38f), new LP(0f, 0.6f, 0.4f),
			};
			float coneHigh = variant == 2 ? 0.9f : 0.6f;
			// An oval from 0.15 m inside the wall out to the apron's reach.
			float coneDeep = 0.5f * (apron + 0.15f);
			Lathe(m, 0, cone, seg, Matrix4x4.TRS(new Vector3(0f, 0f, 0.15f - coneDeep), Quaternion.identity, new Vector3(coneWide, coneHigh, coneDeep)), 2f);
			if (lod < 2)
			{
				// A lesser lobe to one side, so the cone is not a perfect mound.
				float side = rng.Range(0f, 1f) < 0.5f ? -1f : 1f;
				Lathe(m, 0, cone, seg - 4, Matrix4x4.TRS(new Vector3(side * 0.45f * coneWide, 0f, -0.45f * apron), Quaternion.Euler(0f, 30f, 0f), new Vector3(0.5f * coneWide, 0.55f * coneHigh, 0.4f * apron)), 2f);
			}
			return m;
		}

		// ── Icicle curtains ───────────────────────────────────────────

		/// <summary>
		/// A fringe of icicles along a roof's edge, rippled as they grew drop by drop, a drop at each tip, hung from a ridge of ice
		/// along the edge: v0 fourteen along 2 m to 1 m long; v1 a dense curtain of twenty-two along 2.4 m to 1.6 m, a frozen
		/// drapery between their roots; v2 six great icicles along 1.4 m to 2.2 m. Longest toward the middle, where most water ran
		/// off.
		/// </summary>
		private static MeshBuilder IcicleCurtains(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			variant = Mathf.Clamp(variant, 0, IcicleDrops.Length - 1);
			float longest = IcicleDrops[variant], span = IcicleSpans[variant], half = 0.5f * span;
			int count = variant == 0 ? 14 : variant == 1 ? 22 : 6;
			var drops = new List<(float x, float z, float length, float radius, float phase)>(count);
			for (int i = 0; i < count; i++)
			{
				float x = Mathf.Lerp(-half, half, (i + rng.Range(0.15f, 0.85f)) / count);
				float centre = 1f - Mathf.Abs(x) / half;
				float u = rng.NextFloat();
				float length = longest * Mathf.Lerp(0.18f, 1f, Mathf.Pow(u, 0.8f)) * Mathf.Lerp(0.55f, 1f, centre);
				float radius = (variant == 2 ? rng.Range(0.07f, 0.12f) : rng.Range(0.02f, 0.045f)) * Mathf.Lerp(0.8f, 1.2f, length / longest);
				drops.Add((x, rng.Range(-0.08f, 0.08f), length, radius, rng.Range(0f, Tau)));
			}
			// The longest exactly the variant's, so the placer's measure is the art's.
			int top = 0;
			for (int i = 1; i < drops.Count; i++)
			{
				if (drops[i].length > drops[top].length)
				{
					top = i;
				}
			}
			drops[top] = (drops[top].x, drops[top].z, longest, drops[top].radius, drops[top].phase);
			drops.Sort((a, b) => b.length.CompareTo(a.length));
			int kept = lod == 0 ? count : Mathf.Max(1, Mathf.RoundToInt(count * (lod == 1 ? 0.65f : 0.35f)));
			var m = new MeshBuilder(1);
			// The ridge of ice along the edge the icicles hang from: a low flattened mound, its rim up in the rock.
			var ridge = new List<LP> { new LP(1f, -0.1f, 0.18f), new LP(0.92f, 0f, 0.2f), new LP(0.55f, 0.06f, 0.24f), new LP(0f, 0.075f, 0.26f) };
			Lathe(m, 0, ridge, lod == 0 ? 16 : lod == 1 ? 10 : 6, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(180f, 0f, 0f), new Vector3(half + 0.1f, 1f, 0.16f)), 2f);
			int sides = lod == 0 ? 8 : lod == 1 ? 6 : 4;
			int rings = lod == 0 ? 10 : lod == 1 ? 6 : 3;
			for (int i = 0; i < kept; i++)
			{
				(float x, float z, float length, float radius, float phase) d = drops[i];
				// Up the outside from the tip (a pole) to the root in the rock: rippled every few centimetres as it grew, swelling a
				// little where a drop hung longest.
				var profile = new List<LP>(rings + 2) { new LP(0f, 0f, 0.78f) };
				for (int k = 1; k <= rings; k++)
				{
					float t = k / (float)rings;
					float ripple = lod == 0 ? 1f + 0.09f * Mathf.Sin(d.phase + t * d.length * 38f) : 1f;
					float r = d.radius * Mathf.Pow(t, 0.85f) * ripple;
					profile.Add(new LP(r, t * d.length, Mathf.Lerp(0.72f, 0.22f, t)));
				}
				profile.Add(new LP(d.radius * 1.15f, d.length + 0.07f, 0.2f));
				// Turned over, so the root is at the roof (y 0 → +0.07 into it) and the tip hangs at −length.
				Lathe(m, 0, profile, sides, Matrix4x4.TRS(new Vector3(d.x, -d.length, d.z), Quaternion.identity, Vector3.one), 1f, d.phase);
				if (lod == 0)
				{
					// The drop at the tip.
					float r = Mathf.Max(0.006f, 0.35f * d.radius);
					Bead(m, 0, new Vector3(d.x, -d.length - 0.6f * r, d.z), new Vector3(r, 1.3f * r, r), 0.85f, 6, 3);
				}
			}
			if (variant == 1 && lod < 2)
			{
				// A frozen drapery between the roots: a folded sheet 0.3 m deep, both faces, wavy along x.
				int nx = lod == 0 ? 28 : 14, ny = lod == 0 ? 4 : 2;
				float depth = 0.32f, thick = 0.018f, phase = rng.Range(0f, Tau);
				foreach (float face in new[] { -1f, 1f })
				{
					int first = m.VertexCount;
					for (int j = 0; j <= ny; j++)
					{
						float v = j / (float)ny;
						for (int i = 0; i <= nx; i++)
						{
							float t = i / (float)nx;
							float x = Mathf.Lerp(-half * 0.9f, half * 0.9f, t);
							// The fold's wave, deepening toward the free edge; its hem scalloped where drops ran down it.
							float wave = 0.05f * Mathf.Sin(phase + x * 9f) * (0.4f + v);
							float hem = v * depth * (0.8f + 0.2f * Mathf.Sin(phase * 2f + x * 23f));
							var p = new Vector3(x, 0.04f - hem, wave + face * thick * (1f - 0.6f * v));
							var n = new Vector3(-0.45f * Mathf.Cos(phase + x * 9f) * (0.4f + v), 0f, face).normalized;
							m.AddVertex(p, n, new Vector2(Glow(Mathf.Lerp(0.24f, 0.5f, v)), 2f * t), White);
						}
					}
					for (int j = 0; j < ny; j++)
					{
						for (int i = 0; i < nx; i++)
						{
							int a = first + j * (nx + 1) + i;
							m.AddQuad(0, a, a + 1, a + nx + 2, a + nx + 1, new Vector3(0f, 0f, face));
						}
					}
				}
			}
			return m;
		}

		// ── Frozen pools ──────────────────────────────────────────────

		/// <summary>
		/// The outline of a frozen pool's variant: its radius in a direction (radians from +x toward +z), the widest point at 1 on
		/// x or z. v0 a lobed round pool, v1 a long pool 0.6 as wide, v2 two pools frozen into one.
		/// </summary>
		public static float FrozenPoolOutline(int variant, float angle, int seed)
		{
			// A fixed set of lobes a variant (drawn once), normalised by the widest extent found round the outline.
			(float a, float b, float c) phase = ((seed & 1023) * 0.0061f, ((seed >> 10) & 1023) * 0.0059f, ((seed >> 20) & 1023) * 0.0053f);
			float raw = RawOutline(variant, angle, phase);
			float widest = 0f;
			for (int i = 0; i < 96; i++)
			{
				float a = Tau * i / 96f;
				float r = RawOutline(variant, a, phase);
				widest = Mathf.Max(widest, Mathf.Max(Mathf.Abs(r * Mathf.Cos(a)), Mathf.Abs(r * Mathf.Sin(a))));
			}
			return raw / Mathf.Max(1e-3f, widest);
		}

		private static float RawOutline(int variant, float angle, (float a, float b, float c) phase)
		{
			float lobes = 1f + 0.07f * Mathf.Sin(3f * angle + phase.a) + 0.05f * Mathf.Sin(5f * angle + phase.b) + 0.025f * Mathf.Sin(9f * angle + phase.c);
			float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
			switch (variant)
			{
				case 1:
				{
					// An ellipse 1 by 0.6, lobed.
					float e = 1f / Mathf.Sqrt(c * c + s * s / (0.6f * 0.6f));
					return e * lobes;
				}
				case 2:
				{
					// Two discs that both hold the middle: the outline is the further of the two rays' exits.
					float r1 = RayExit(c, s, new Vector2(-0.42f, 0f), 0.6f), r2 = RayExit(c, s, new Vector2(0.45f, 0.06f), 0.52f);
					return Mathf.Max(r1, r2) * (1f + 0.6f * (lobes - 1f));
				}
				default:
					return 0.92f * lobes * (1f / Mathf.Sqrt(c * c + s * s / (0.86f * 0.86f)));
			}
		}

		/// <summary>How far a ray from the origin along (c, s) runs before it leaves a disc that holds the origin.</summary>
		private static float RayExit(float c, float s, Vector2 centre, float radius)
		{
			// |t·d − centre|² = r² → t² − 2t(d·centre) + |centre|² − r² = 0, the larger root.
			float b = c * centre.x + s * centre.y, k = centre.sqrMagnitude - radius * radius;
			return b + Mathf.Sqrt(Mathf.Max(0f, b * b - k));
		}

		/// <summary>
		/// A frozen pool: a dead-level sheet of clear ice, dark and glassy in the middle, the bubbles it trapped as it froze from its
		/// edge inward lying in pale rings, frosted white at its rim; the rim bevelled <see cref="FrozenPoolBevel"/> down to the rock
		/// and a skirt <see cref="FrozenPoolSkirt"/> under it, so no step and no gap shows. Its surface is y <see cref="FrozenPoolRise"/>;
		/// widest 1 on x or z.
		/// </summary>
		private static MeshBuilder FrozenPools(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			variant = Mathf.Clamp(variant, 0, 2);
			int outlineSeed = rng.Next();
			float bandPhase = rng.Range(0f, Tau), bandRate = rng.Range(6f, 9f), streak = rng.Range(0f, Tau);
			int segments = lod == 0 ? 48 : lod == 1 ? 28 : 14;
			int rings = lod == 0 ? 9 : lod == 1 ? 5 : 2;
			var outline = new float[segments + 1];
			for (int k = 0; k <= segments; k++)
			{
				outline[k] = FrozenPoolOutline(variant, Tau * (k % segments) / segments, outlineSeed);
			}
			var m = new MeshBuilder(1);
			// The surface: a fan of rings out to the bevel, every vertex on y 0.
			int centre = m.AddVertex(Vector3.zero, Vector3.up, new Vector2(Glow(0.1f), 0f), White);
			var ringStart = new int[rings + 1];
			for (int j = 1; j <= rings; j++)
			{
				float f = FrozenPoolFlat * j / rings;
				ringStart[j] = m.VertexCount;
				for (int k = 0; k <= segments; k++)
				{
					float a = Tau * k / segments;
					float r = outline[k] * f;
					// Clear and dark in the middle; pale bubble rings where the freezing front paused; frosted toward the rim.
					float bands = Mathf.SmoothStep(0f, 1f, 0.5f + 0.5f * Mathf.Sin(bandPhase + f * bandRate * Tau * 0.5f + 0.6f * Mathf.Sin(3f * a + streak)));
					float level = Mathf.Lerp(0.08f, 0.5f, f * f) + 0.16f * bands * f;
					m.AddVertex(new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a)), Vector3.up, new Vector2(Glow(level), 3f * k / segments), White);
				}
			}
			for (int k = 0; k < segments; k++)
			{
				m.AddTriangle(0, centre, ringStart[1] + k, ringStart[1] + k + 1, Vector3.up);
			}
			for (int j = 1; j < rings; j++)
			{
				for (int k = 0; k < segments; k++)
				{
					int a = ringStart[j] + k, b = ringStart[j + 1] + k;
					m.AddQuad(0, a, a + 1, b + 1, b, Vector3.up);
				}
			}
			// The bevel to the rim (frosted white) and the skirt down into the rock, each its own ring of vertices (a crease at each).
			int bevelTop = m.VertexCount;
			for (int k = 0; k <= segments; k++)
			{
				float a = Tau * k / segments, r = outline[k] * FrozenPoolFlat;
				var n = new Vector3(0.5f * Mathf.Cos(a), 1f, 0.5f * Mathf.Sin(a)).normalized;
				m.AddVertex(new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a)), n, new Vector2(Glow(0.62f), 3f * k / segments), White);
			}
			int bevelFoot = m.VertexCount;
			for (int k = 0; k <= segments; k++)
			{
				float a = Tau * k / segments, r = outline[k];
				var n = new Vector3(0.5f * Mathf.Cos(a), 1f, 0.5f * Mathf.Sin(a)).normalized;
				m.AddVertex(new Vector3(r * Mathf.Cos(a), -FrozenPoolBevel, r * Mathf.Sin(a)), n, new Vector2(Glow(0.7f), 3f * k / segments), White);
			}
			int skirtTop = m.VertexCount;
			for (int k = 0; k <= segments; k++)
			{
				float a = Tau * k / segments, r = outline[k];
				var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				m.AddVertex(new Vector3(r * Mathf.Cos(a), -FrozenPoolBevel, r * Mathf.Sin(a)), n, new Vector2(Glow(0.5f), 3f * k / segments), White);
			}
			int skirtFoot = m.VertexCount;
			for (int k = 0; k <= segments; k++)
			{
				float a = Tau * k / segments, r = outline[k] * 0.9f;
				var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				m.AddVertex(new Vector3(r * Mathf.Cos(a), -FrozenPoolSkirt, r * Mathf.Sin(a)), n, new Vector2(Glow(0.3f), 3f * k / segments), White);
			}
			// The surface's last ring and the bevel's top ring share their positions (the bevel keeps its own normals: a crease).
			for (int k = 0; k < segments; k++)
			{
				float mid = Tau * (k + 0.5f) / segments;
				var outward = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
				m.AddQuad(0, bevelTop + k, bevelTop + k + 1, bevelFoot + k + 1, bevelFoot + k, outward + Vector3.up);
				m.AddQuad(0, skirtTop + k, skirtTop + k + 1, skirtFoot + k + 1, skirtFoot + k, outward);
			}
			// Built about its surface, then raised to stand over its origin.
			m.Transform(Matrix4x4.Translate(Vector3.up * FrozenPoolRise));
			return m;
		}

		// ── Hoarfrost ─────────────────────────────────────────────────

		/// <summary>The patch of hoarfrost a variant covers on its wall at scale 1 (x across, y up), metres.</summary>
		public static readonly Vector2[] HoarfrostPatches = { new Vector2(1f, 0.8f), new Vector2(0.7f, 1.5f), new Vector2(1.9f, 0.7f) };

		/// <summary>
		/// Hoarfrost on a wall: feathery plates of hoar crystal standing 2–8 cm out of the rock, thickest in the patch's middle and
		/// thinning to its ragged edge, each a six-sided leaf rooted in the rock, catching any light as it turns. v0 a patch 1 by
		/// 0.8 m, v1 a streak 0.7 by 1.5 m (frost up a seep), v2 a band 1.9 by 0.7 m (frost along a crack).
		/// </summary>
		private static MeshBuilder Hoarfrosts(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			variant = Mathf.Clamp(variant, 0, HoarfrostPatches.Length - 1);
			Vector2 patch = HoarfrostPatches[variant];
			int count = Mathf.RoundToInt(170f * patch.x * patch.y);
			var plates = new List<(Vector2 at, float length, float width, float spin, float tilt, float glow)>(count);
			for (int i = 0; i < count; i++)
			{
				// Denser and longer toward the middle: two draws averaged lean the spread inward.
				var at = new Vector2(0.25f * (rng.Range(-1f, 1f) + rng.Range(-1f, 1f)) * patch.x, 0.25f * (rng.Range(-1f, 1f) + rng.Range(-1f, 1f)) * patch.y);
				float middle = 1f - Mathf.Clamp01(Mathf.Max(Mathf.Abs(at.x) / (0.5f * patch.x), Mathf.Abs(at.y) / (0.5f * patch.y)));
				float length = Mathf.Lerp(0.02f, 0.08f, middle) * rng.Range(0.7f, 1.2f);
				plates.Add((at, length, length * rng.Range(0.45f, 0.75f), rng.Range(0f, Mathf.PI), rng.Range(-0.5f, 0.5f), rng.Range(0.3f, 0.6f)));
			}
			plates.Sort((a, b) => b.length.CompareTo(a.length));
			int kept = lod == 0 ? count : Mathf.Max(4, Mathf.RoundToInt(count * (lod == 1 ? 0.45f : 0.15f)));
			var m = new MeshBuilder(1);
			for (int i = 0; i < kept; i++)
			{
				(Vector2 at, float length, float width, float spin, float tilt, float glow) p = plates[i];
				// The plate stands out of the wall (−z), turned about that axis by its spin and leaning a little off it.
				Vector3 outward = (Vector3.back + new Vector3(p.tilt * Mathf.Cos(p.spin), p.tilt * Mathf.Sin(p.spin), 0f)).normalized;
				var across = new Vector3(Mathf.Cos(p.spin), Mathf.Sin(p.spin), 0f);
				across = (across - outward * Vector3.Dot(across, outward)).normalized;
				Vector3 normal = Vector3.Cross(outward, across).normalized;
				var root = new Vector3(p.at.x, p.at.y, 0.03f);
				// A six-sided leaf: its root in the rock, widest a third of the way out, a point at its tip.
				Vector3[] rim =
				{
					root, root + outward * (0.3f * p.length) + across * (0.5f * p.width), root + outward * (0.75f * p.length) + across * (0.3f * p.width),
					root + outward * (p.length + 0.03f), root + outward * (0.75f * p.length) - across * (0.3f * p.width), root + outward * (0.3f * p.length) - across * (0.5f * p.width),
				};
				foreach (float face in new[] { 1f, -1f })
				{
					int first = m.VertexCount;
					for (int k = 0; k < rim.Length; k++)
					{
						float level = Mathf.Lerp(0.3f, p.glow + 0.15f, k == 3 ? 1f : k == 0 ? 0f : 0.5f);
						m.AddVertex(rim[k], normal * face, new Vector2(Glow(level), 0.1f * k), White);
					}
					for (int k = 1; k + 1 < rim.Length; k++)
					{
						m.AddTriangle(0, first, first + k, first + k + 1, normal * face);
					}
				}
			}
			return m;
		}
	}

	public static partial class ProceduralArtCatalogue
	{
		private static CaveGlowHue[] iceHues, rimeHues;

		/// <summary>
		/// An ice cave's ice (2026-10-20, caves livened): Clear, the blue-white of ice near a mouth; Glacial, the deep blue of old
		/// ice far in. Glassy, faintly glowing in the dark as the cave's glow-ice does; a blue light where the placer gives one.
		/// Built when first asked (no static initialiser, so the families' table may read it in any order).
		/// </summary>
		public static CaveGlowHue[] IceHues => iceHues ??= new[]
		{
			new CaveGlowHue { Name = "Clear", Albedo = new Color(0.76f, 0.88f, 0.97f), Emission = new Color(0.08f, 0.24f, 0.5f), Smoothness = 0.9f, Light = new Color(0.55f, 0.8f, 1f), EmitterHue = 1 },
			new CaveGlowHue { Name = "Glacial", Albedo = new Color(0.52f, 0.74f, 0.95f), Emission = new Color(0.12f, 0.42f, 0.95f), Smoothness = 0.93f, Light = new Color(0.4f, 0.7f, 1f), EmitterHue = 1 },
		};

		/// <summary>Hoarfrost's white: sparkling, hardly glowing (its hue's light is blue, but its family asks for none).</summary>
		public static CaveGlowHue[] RimeHues => rimeHues ??= new[]
		{
			new CaveGlowHue { Name = "Rime", Albedo = new Color(0.93f, 0.96f, 1f), Emission = new Color(0.05f, 0.12f, 0.22f), Smoothness = 0.8f, Light = new Color(0.6f, 0.8f, 1f), EmitterHue = 1 },
		};
	}
}
#endif
