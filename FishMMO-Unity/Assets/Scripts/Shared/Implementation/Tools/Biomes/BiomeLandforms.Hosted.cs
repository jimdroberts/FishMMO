using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	public static partial class BiomeLandforms
	{
		/// <summary>
		/// The shapes and tuners added by the last landform-preset pass (2026-10-25, phase I2g-d, decisions 80–96): a breached rim
		/// (craters, calderas, cones), treads down a table's flank and benches down a caldera's walls, a table's flank angle, the
		/// canyon's head and its cyclic steps, the tessera weave, forked linear dunes and the dune-free wake behind an obstacle.
		/// </summary>
		/// <remarks>
		/// Every one keeps the contract of the rest: a pure function of the site and the point, zero outside the site's bounds and
		/// meeting the untouched ground there without a step. Each is inert unless its entry sets it, so every older entry carves
		/// exactly as before.
		/// </remarks>
		public sealed partial class Shape
		{
			/// <summary>
			/// A share <paramref name="w"/> (0 … 1) drawn as <paramref name="levels"/> flat treads with steep risers between: 0 stays
			/// 0 and 1 stays 1, so a stepped flank meets its top and its foot as the smooth one did.
			/// </summary>
			private static float Stepped(float w, int levels)
			{
				if (levels <= 1)
				{
					return w;
				}
				float level = Mathf.Clamp01(w) * levels;
				float k = MathF.Floor(level);
				return Mathf.Min(1f, (k + S01((level - k - 0.7f) / 0.3f)) / levels);
			}

			/// <summary>
			/// A table's flat top as a share of its radius: its Top (0.75 when unset), or, with a <see cref="BiomeLandform.Flank"/>
			/// angle and a level, whatever leaves its flank that steep (2026-10-25: a guyot's 14–22°, a pancake dome's 30°).
			/// </summary>
			private float MesaTop()
			{
				BiomeLandform f = Site.Form;
				float top = Mathf.Clamp(f.Top <= 0f ? 0.75f : f.Top, 0.05f, 0.97f);
				if (f.Flank > 0f && f.Form == BiomeLandformForm.Table && !float.IsNaN(Site.Level))
				{
					float run = MathF.Abs(Site.Level - Site.Ground) / MathF.Tan(Mathf.Clamp(f.Flank, 5f, 85f) * Mathf.Deg2Rad);
					top = Mathf.Clamp(1f - run / radius, 0.05f, 0.97f);
				}
				return top;
			}

			/// <summary>
			/// A crater's or caldera's change <paramref name="delta"/> with its rim breached (<see cref="BiomeLandform.Breach"/>): down
			/// its axis (+) or against it (−), a flat-floored channel its HalfWidth across (0.15 of its radius when unset) cut through
			/// the rim to that share of its depth and running out to 1.3 radii, fading to nothing by 1.6 (inside its 2-radius box).
			/// </summary>
			private float Breached(float delta, float dx, float dz, float r)
			{
				float b = Site.Form.Breach;
				if (b == 0f || r >= 1.6f)
				{
					return delta;
				}
				float u = dx * cos + dz * sin, v = -dx * sin + dz * cos;
				if (b < 0f)
				{
					u = -u;
				}
				float halfW = Site.HalfWidth > 0.5f ? Site.HalfWidth : 0.15f * radius;
				float n = (1f - S01((MathF.Abs(v) - halfW) / halfW)) * S01(u / (0.2f * radius)) * (1f - S01((r - 1.3f) / 0.3f));
				if (n <= 0f)
				{
					return delta;
				}
				float cut = -Site.Height * Mathf.Min(MathF.Abs(b), 1.5f) * (1f - S01((r - 0.85f) / 0.75f));
				return delta - n * Mathf.Max(0f, delta - cut);
			}

			/// <summary>
			/// A cone's (or dome's) crown breached down one flank (a horseshoe cinder cone): a channel from its crater floor (or a
			/// sixth of the way down when it has none) falling to the ground half way down its flank and on to its foot, its share <see cref="BiomeLandform.Breach"/>.
			/// </summary>
			private float MoundBreach(float x, float z, float q, float current, float target)
			{
				BiomeLandform f = Site.Form;
				Local(x, z, out float u, out float v);
				if (f.Breach < 0f)
				{
					u = -u;
				}
				float crown = f.Crown > 0f ? Mathf.Clamp(f.Crown, 0.05f, 0.9f) : 0.15f;
				float halfW = Site.HalfWidth > 0.5f ? Site.HalfWidth : Mathf.Max(0.5f, 0.8f * crown * radius);
				float n = (1f - S01((MathF.Abs(v) - halfW) / halfW)) * S01(u / (0.1f * radius));
				if (n <= 0f)
				{
					return target;
				}
				float floor = Site.Height * MoundProfile(crown) - (f.Crown > 0f ? f.CrownDepth : 0f);
				float cut = current + floor * (1f - S01((q - crown) / (0.5f * (1f - crown))));
				return target - n * Mathf.Min(1f, MathF.Abs(f.Breach)) * Mathf.Max(0f, target - cut);
			}

			/// <summary>
			/// The share of its radius that is its top or floor, as an entry standing on it reads it (2026-10-25, I2g-d): a table's
			/// flat top, a tower's cap, a mound's or bowl's Top, a crater's floor; a half for any other form.
			/// </summary>
			public float TopShare
			{
				get
				{
					BiomeLandform f = Site.Form;
					switch (FamilyOf(f.Form))
					{
						case BiomeLandformFamily.Mesa: return f.Form == BiomeLandformForm.Table ? MesaTop() : 0.9f;
						case BiomeLandformFamily.Mound: return f.Form == BiomeLandformForm.Tower ? TowerShoulder : Mathf.Clamp(f.Top > 0f ? f.Top : 0.3f, 0.05f, 0.95f);
						case BiomeLandformFamily.Crater:
							return f.Form == BiomeLandformForm.Caldera ? Mathf.Clamp(f.Top, 0.05f, 0.9f) : f.Form == BiomeLandformForm.MultiRing ? 0.4f : Mathf.Max(0.05f, CraterTop());
						case BiomeLandformFamily.Depression: return Mathf.Clamp(f.Top > 0f ? f.Top : 0.5f, 0.05f, 0.95f);
						default: return 0.5f;
					}
				}
			}

			/// <summary>True when a channel's lines taper at both ends (a spiral's, a ventless rille's, a canyon's head, any line on a host).</summary>
			private bool TapersBoth
			{
				get
				{
					BiomeLandform f = Site.Form;
					return f.Form == BiomeLandformForm.Spiral || f.Form == BiomeLandformForm.Rille && f.CrownDepth <= 0f || f.Form == BiomeLandformForm.Canyon || f.OnHost;
				}
			}

			// ── Channels: the canyon's head and its cyclic steps ─────

			/// <summary>A canyon's head: its floor and walls over the first two widths of its line, fading over the next two.</summary>
			private float CanyonHead(List<LineHit> hits)
			{
				float best = 0f;
				foreach (LineHit hit in hits)
				{
					float half = Mathf.Max(0.05f, Site.Segments[hit.Segment].Half);
					float t = hit.Distance / half;
					if (t < 1f)
					{
						best = Mathf.Max(best, (1f - S01((t - 0.8f) / 0.2f)) * (1f - S01((hit.Along - 2f * half) / (2f * half))));
					}
				}
				return best;
			}

			/// <summary>
			/// Cyclic steps along a stored line (decision 92), metres: sediment-wave crescents its Width apart, bowed down the line,
			/// on the floor its segments' half width spans; <paramref name="floorShare"/> that floor, <paramref name="crest"/> the crests.
			/// </summary>
			private float TreadsHeight(List<LineHit> hits, out float floorShare, out float crest)
			{
				floorShare = 0f;
				crest = 0f;
				int nearest = -1;
				for (int i = 0; i < hits.Count; i++)
				{
					if (nearest < 0 || hits[i].Distance < hits[nearest].Distance)
					{
						nearest = i;
					}
				}
				if (nearest < 0)
				{
					return 0f;
				}
				LineHit hit = hits[nearest];
				BiomeLandformSegment seg = Site.Segments[hit.Segment];
				float half = Mathf.Max(0.5f, seg.Half);
				float t = hit.Distance / half;
				if (t >= 1f)
				{
					return 0f;
				}
				floorShare = (1f - S01((t - 0.6f) / 0.4f)) * Taper(seg, hit.Along, half, true);
				float lambda = Mathf.Max(4f, Site.HalfWidth * 2f);
				float frac = Frac((hit.Along - 0.35f * half * (1f - t * t)) / lambda + Unit(Site.Seed));
				float p = frac < 0.65f ? S01(frac / 0.65f) : 1f - S01((frac - 0.65f) / 0.35f);
				crest = floorShare * Bump((frac - 0.65f) / 0.12f);
				return Site.Height * (p - 0.5f) * floorShare;
			}

			// ── Waves: the tessera weave ─────────────────────────────

			/// <summary>
			/// A tessera weave's height at a point (decision 85), metres: fins along the field's strike crossed at 70° by flat-floored
			/// troughs; <paramref name="crest"/> the fins left standing, <paramref name="trough"/> the troughs.
			/// </summary>
			private float WeaveHeight(float x, float z, float lambda, out float crest, out float trough)
			{
				BiomeLandform f = Site.Form;
				Local(x, z, out float u, out float v);
				float d = MathF.Abs(Frac(v / lambda + 0.15f * Noise(u / (lambda * 6f), 0f, Site.Seed)) - 0.5f) * lambda;
				float half = Mathf.Max(0.5f, 0.18f * lambda);
				float fin = 1f - S01((d - 0.6f * half) / (0.4f * half));
				const float cross = 70f * Mathf.Deg2Rad;
				float v2 = -u * MathF.Sin(cross) + v * MathF.Cos(cross);
				float spacing = f.Moat > 0f ? f.Moat * (0.7f + 0.6f * Unit(Hash(Site.Seed, 0x7Eu))) : 3f * lambda;
				float th = 0.5f * (f.Top > 0f ? f.Top : 0.25f) * spacing;
				float u2 = u * MathF.Cos(cross) + v * MathF.Sin(cross);
				float d2 = MathF.Abs(Frac(v2 / spacing + 0.1f * Noise(u2 / (spacing * 4f), 0.5f, Site.Seed ^ 0x7Fu)) - 0.5f) * spacing;
				trough = 1f - S01((d2 - 0.6f * th) / (0.4f * th));
				crest = fin * (1f - trough);
				return Site.Height * crest - f.MoatDepth * trough;
			}

			// ── Dunes: forks and wakes (decision 91) ──────────────────

			/// <summary>
			/// A linear dune field's crest share at (u along the wind, vv across it) with Y-junctions: each pair of neighbouring crests
			/// (every other corridor) runs in cells of its own length (<see cref="BiomeLandform.Forks"/>), and in about half of them
			/// the two crests close on their corridor and merge downwind of a junction (the Y opening upwind), the merged dune ending
			/// before the cell's end where the pair takes over again.
			/// </summary>
			private float ForkedLinear(float u, float vv, float lambda, float width)
			{
				BiomeLandform f = Site.Form;
				float pair = 2f * lambda;
				int m = Mathf.FloorToInt(vv / pair);
				float local = vv - m * pair;
				float length = Mathf.Max(4f * lambda, Pick(f.Forks, Unit(Hash(Site.Seed, (uint)m, 0xF0u))));
				float shift = Unit(Hash(Site.Seed, (uint)m, 0xF2u));
				float cu = u / length + shift;
				int c = Mathf.FloorToInt(cu);
				float inCell = (cu - c) * length;
				float twin = Mathf.Max(Bump(MathF.Abs(local - 0.5f * lambda) / width), Bump(MathF.Abs(local - 1.5f * lambda) / width));
				uint h = Hash(Site.Seed, (uint)m, (uint)c, 0xF1u);
				if (Unit(h) >= 0.5f)
				{
					return twin;
				}
				float ramp = 0.12f * length;
				float junction = (0.25f + 0.3f * Unit(Hash(h, 1u))) * length;
				float merge = S01((inCell - junction) / ramp);
				float end = S01((inCell - (length - 1.5f * ramp)) / ramp);
				float offset = 0.5f * lambda * (1f - merge);
				float forked = Mathf.Max(Bump(MathF.Abs(local - (lambda - offset)) / width), Bump(MathF.Abs(local - (lambda + offset)) / width));
				return forked + (twin - forked) * end;
			}

			/// <summary>The obstacles a dune field leaves wakes behind (x, z, radius, wake length), from its stored segments.</summary>
			private Vector4[] wakes;

			private void PrepareWakes()
			{
				if (Site.Segments == null || Site.Segments.Count == 0)
				{
					return;
				}
				wakes = new Vector4[Site.Segments.Count];
				for (int i = 0; i < wakes.Length; i++)
				{
					BiomeLandformSegment seg = Site.Segments[i];
					wakes[i] = new Vector4(seg.A.x, seg.A.y, Mathf.Max(1f, seg.Half), Mathf.Max(2f * seg.Half, seg.Depth));
				}
			}

			/// <summary>How much of a point lies in a wake (0 … 1): a teardrop downwind of each obstacle, its radius wide, its length long.</summary>
			private float WakeAt(float x, float z)
			{
				if (wakes == null)
				{
					return 0f;
				}
				float best = 0f;
				foreach (Vector4 o in wakes)
				{
					float dx = x - o.x, dz = z - o.y;
					float du = dx * cos + dz * sin, dv = -dx * sin + dz * cos;
					float r = o.z, length = o.w;
					if (du < -1.2f * r || du > length)
					{
						continue;
					}
					float w = du < 0f ? r * MathF.Sqrt(Mathf.Max(0f, 1f - du / r * (du / r))) : r * Mathf.Max(0f, 1f - du / length);
					float m = (1f - S01((MathF.Abs(dv) - w) / (0.4f * r))) * (1f - S01((du - 0.8f * length) / (0.2f * length))) * S01((du + 1.2f * r) / (0.4f * r));
					best = Mathf.Max(best, m);
				}
				return best;
			}
		}
	}
}
