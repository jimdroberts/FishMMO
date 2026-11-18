using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	public static partial class BiomeLandforms
	{
		/// <summary>
		/// Raises every closed low of a spill fill only <paramref name="share"/> of the way from its floor to its spill (2026-10-26,
		/// phase I2g-f: a Cayley plain's fill to three tenths of its crater's rim): the lows are the lattice nodes the flood raised,
		/// joined 4-way; each low's level is its lowest node plus that share of its rise to the spill, never under a node's own
		/// ground (pure but for the arrays).
		/// </summary>
		private static void PartFill(float[] level, float[] filled, int cw, int cd, float share)
		{
			var label = new int[level.Length];
			var queue = new Queue<int>();
			var members = new List<int>();
			int next = 0;
			for (int start = 0; start < level.Length; start++)
			{
				if (label[start] != 0 || filled[start] - level[start] <= 0.01f)
				{
					continue;
				}
				next++;
				label[start] = next;
				queue.Enqueue(start);
				members.Clear();
				float floor = float.MaxValue, spill = float.MinValue;
				while (queue.Count > 0)
				{
					int k = queue.Dequeue();
					members.Add(k);
					floor = Mathf.Min(floor, level[k]);
					spill = Mathf.Max(spill, filled[k]);
					int i = k % cw, j = k / cw;
					for (int n = 0; n < 4; n++)
					{
						int ni = i + (n == 0 ? 1 : n == 1 ? -1 : 0), nj = j + (n == 2 ? 1 : n == 3 ? -1 : 0);
						if (ni < 0 || nj < 0 || ni >= cw || nj >= cd)
						{
							continue;
						}
						int nk = nj * cw + ni;
						if (label[nk] == 0 && filled[nk] - level[nk] > 0.01f)
						{
							label[nk] = next;
							queue.Enqueue(nk);
						}
					}
				}
				float to = floor + share * (spill - floor);
				foreach (int k in members)
				{
					filled[k] = Mathf.Max(level[k], Mathf.Min(filled[k], to));
				}
			}
		}

		/// <summary>
		/// The landform follow-ups' shapes (2026-10-26, phase I2g-f): a halo crater's dark parabola, a rille's roofed stretches, a
		/// chaos raft's tilted top, a pancake dome's radial grooves, an arachnoid's web of radial fractures.
		/// </summary>
		/// <remarks>
		/// Each keeps the contract of the rest (pure, zero outside the site's bounds, meeting the ground at its edge) and is inert
		/// unless its entry sets the field that asks for it.
		/// </remarks>
		public sealed partial class Shape
		{
			/// <summary>
			/// A halo crater's dark parabola (decision 89): in the site's axis frame (its axis the wind's), the ground inside a
			/// parabola whose vertex stands 1.4 radii upwind of the centre and whose arms open down the axis, out to its
			/// <see cref="BiomeLandform.RayReach"/> radii (2 when unset), densest near the crater; not the crater itself.
			/// </summary>
			private float HaloMask(float dx, float dz, float r)
			{
				BiomeLandform f = Site.Form;
				if (f.Form != BiomeLandformForm.Impact)
				{
					return 0f;
				}
				float reach = f.RayReach > 0f ? Mathf.Clamp(f.RayReach, 2f, 12f) : 2f;
				float u = (dx * cos + dz * sin) / radius, v = (-dx * sin + dz * cos) / radius;
				float s = u + 1.4f - v * v / (0.6f * reach);
				float inside = S01(s / 0.6f);
				float outward = 1f - S01((r - 0.8f * reach) / (0.2f * reach));
				float fade = 1f - 0.6f * Mathf.Clamp01((r - 1f) / reach);
				return inside * outward * fade * S01((r - 1.05f) / 0.3f);
			}

			/// <summary>
			/// How much a rille's line is roofed at <paramref name="along"/> metres from its head (<see cref="BiomeLandform.Roofed"/>):
			/// stretches about eight half widths apart, that share of each pitch roofed, their ends eased; never over its first three
			/// half widths (the vent hollow and the open trench below it).
			/// </summary>
			private float RoofAt(float along, float half)
			{
				float share = Mathf.Clamp(Site.Form.Roofed, 0f, 0.9f);
				if (share <= 0f)
				{
					return 0f;
				}
				half = Mathf.Max(0.5f, half);
				float pitch = 8f * half;
				float warp = 0.3f * (Noise(along / (3f * pitch), 0.5f, Site.Seed ^ 0x2F0Fu) - 0.5f);
				float frac = Frac(along / pitch + warp + Unit(Hash(Site.Seed, 0x2F1u)));
				float edge = Mathf.Min(0.08f, 0.25f * share);
				float roof = S01(frac / edge) * (1f - S01((frac - share) / edge));
				return roof * S01((along - 3f * half) / half);
			}

			/// <summary>
			/// The middle of each roofed stretch of a rille's line (<see cref="RoofAt"/> above 0.95), world xz with the stretch's half
			/// width: the pads its tube's roof offers (a skylight's lip, a natural bridge). Empty for any other site.
			/// </summary>
			public List<(Vector2 Centre, float Radius)> RoofedStretches()
			{
				var stretches = new List<(Vector2, float)>();
				if (Site.Form.Form != BiomeLandformForm.Rille || Site.Form.Roofed <= 0f || Site.Segments == null || Site.Segments.Count == 0)
				{
					return stretches;
				}
				float half = Mathf.Max(0.5f, Site.Segments[0].Half);
				float step = 0.25f * half;
				float runStart = float.NaN;
				int s = 0;
				float length = Site.Segments[Site.Segments.Count - 1].AlongB;
				for (float at = 0f; at <= length + step && stretches.Count < 512; at += step)
				{
					bool roofed = at <= length && RoofAt(at, half) > 0.95f;
					if (roofed && float.IsNaN(runStart))
					{
						runStart = at;
					}
					else if (!roofed && !float.IsNaN(runStart))
					{
						float middle = 0.5f * (runStart + at - step);
						while (s < Site.Segments.Count - 1 && Site.Segments[s].AlongB < middle)
						{
							s++;
						}
						BiomeLandformSegment seg = Site.Segments[s];
						float span = Mathf.Max(1e-4f, seg.AlongB - seg.AlongA);
						stretches.Add((Vector2.Lerp(seg.A, seg.B, Mathf.Clamp01((middle - seg.AlongA) / span)), Mathf.Max(1f, 0.9f * seg.Half)));
						runStart = float.NaN;
					}
				}
				return stretches;
			}

			/// <summary>A tilted table's rise at a point over its centre's level (<see cref="BiomeLandform.Tilt"/>): its own azimuth and share of the tilt by its hash.</summary>
			private float TableTilt(float x, float z)
			{
				float a = Unit(Hash(Site.Seed, 0x7117u)) * TwoPi;
				float degrees = Mathf.Clamp(Site.Form.Tilt, 0f, 30f) * (0.3f + 0.7f * Unit(Hash(Site.Seed, 0x7118u)));
				float d = (x - Site.Centre.x) * MathF.Cos(a) + (z - Site.Centre.y) * MathF.Sin(a);
				return MathF.Tan(degrees * Mathf.Deg2Rad) * d;
			}

			/// <summary>
			/// A mound's radial grooves at a point (<see cref="BiomeLandform.Radials"/> of them), as the share of its profile
			/// <paramref name="p"/> they take: a quarter at a groove's line, only on the flank (none at the top or the foot).
			/// </summary>
			private float MoundRadial(float x, float z, float p)
			{
				Local(x, z, out float u, out float v);
				float rr = MathF.Sqrt(u * u + v * v);
				if (rr < 1e-3f)
				{
					return 0f;
				}
				float c = u / rr, s = v / rr;
				float theta = MathF.Atan2(v, u);
				float wobble = 0.5f * (Noise(2f + 2f * c, 2f + 2f * s, Site.Seed ^ 0x6A01u) - 0.5f);
				float groove = MathF.Pow(MathF.Abs(MathF.Cos(0.5f * Site.Form.Radials * theta + wobble)), 40f);
				return 0.25f * groove * 4f * p * (1f - p);
			}

			/// <summary>
			/// An arachnoid's web of radial fractures (<see cref="BiomeLandform.Radials"/> of them, each its own heading by the hash),
			/// cut a third of the ring's height deep and a tenth of its half width wide, from a third of its radius out to just past
			/// its outer foot (inside its box).
			/// </summary>
			private float RingWeb(float dx, float dz, float rr)
			{
				BiomeLandform f = Site.Form;
				float ring = Site.Radius, half = Mathf.Max(0.5f, Site.HalfWidth);
				float outer = ring + 0.6f * half;
				if (rr < 0.3f * ring || rr > outer + 0.4f * half)
				{
					return 0f;
				}
				int n = Mathf.Clamp(f.Radials, 1, 64);
				float theta = MathF.Atan2(dz, dx);
				float width = Mathf.Max(0.5f, 0.1f * half);
				float best = 0f;
				for (int k = 0; k < n; k++)
				{
					float a = (k + 0.6f * (Unit(Hash(Site.Seed, (uint)k, 0x3EBu)) - 0.5f)) * (TwoPi / n);
					float delta = Mathf.DeltaAngle(theta * Mathf.Rad2Deg, a * Mathf.Rad2Deg) * Mathf.Deg2Rad;
					if (MathF.Abs(delta) > 0.5f * MathF.PI)
					{
						continue;
					}
					best = Mathf.Max(best, Bump(rr * MathF.Sin(MathF.Abs(delta)) / width));
				}
				float span = S01((rr - 0.3f * ring) / (0.1f * ring)) * (1f - S01((rr - outer) / (0.4f * half)));
				return 0.33f * Mathf.Abs(Site.Height) * best * span;
			}
		}
	}
}
