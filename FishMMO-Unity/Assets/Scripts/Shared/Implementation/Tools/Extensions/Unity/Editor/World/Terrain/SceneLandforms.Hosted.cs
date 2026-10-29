#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The planner's share of the last landform-preset pass (2026-10-25, phase I2g-d, decisions 80–96): entries standing on a host
	/// entry's sites (<see cref="BiomeLandformAnchor.OnTopOf"/>, <see cref="BiomeLandformAnchor.OnFlankOf"/>), the strike-slip shift
	/// of the lines carved before a trace, the obstacles a dune field's wakes lie behind, and the cycloid's chain of arcs.
	/// </summary>
	/// <remarks>
	/// All pure but for the ground they ask, and inert unless an entry asks for them: no older entry takes any of these paths.
	/// </remarks>
	public static partial class SceneLandforms
	{
		/// <summary>The host of an entry on a host: the nearest entry before it in the list that is not itself on one; null for none.</summary>
		private static string HostKind(IReadOnlyList<BiomeLandform> forms, int entry)
		{
			for (int i = entry - 1; i >= 0; i--)
			{
				if (!forms[i].OnHost)
				{
					return forms[i].Kind;
				}
			}
			return null;
		}

		/// <summary>True when a form on top of a line host follows the line itself (cyclic steps down a canyon, pits along a rift).</summary>
		private static bool FollowsLine(BiomeLandformForm form) => form == BiomeLandformForm.Treads || form == BiomeLandformForm.PitChain;

		/// <summary>True when a host is cut into the ground (its downhill runs toward its middle or line), false when it is raised.</summary>
		private static bool IsCutHost(in BiomeLandformSite host)
		{
			BiomeLandformFamily family = host.Form.Family;
			if (family == BiomeLandformFamily.Channels || family == BiomeLandformFamily.Depression || family == BiomeLandformFamily.Crater)
			{
				return true;
			}
			if (family == BiomeLandformFamily.Ridge)
			{
				return host.Form.Form == BiomeLandformForm.Graben || host.Segments != null && host.Segments.Count > 0 && host.Segments[0].Depth < 0f;
			}
			return host.Height < 0f;
		}

		/// <summary>
		/// Places an entry on each site its host entry placed (pure but for the ground), in the host's order: inside its top, round
		/// its flank, along its line or at its far end (<see cref="BiomeLandformAnchor.OnTopOf"/>). Members of one host keep apart
		/// from each other, not from the host.
		/// </summary>
		private static void PlaceOnHost(LandformPlanInput input, BiomeLandform form, string host, int id, int biomeIndex, int entry, List<BiomeLandformSite> sites)
		{
			int made = 0, count = sites.Count;
			var context = new BiomeLandforms.Context { SeaLevel = input.SeaLevel };
			for (int s = 0; s < count; s++)
			{
				BiomeLandformSite h = sites[s];
				if (h.BiomeID != id || !string.Equals(h.Form.Kind, host, StringComparison.Ordinal))
				{
					continue;
				}
				if (form.PerScene > 0 && made >= form.PerScene)
				{
					break;
				}
				uint seed = BiomeLandforms.Hash(h.Seed, (uint)entry, 0x0A7u);
				bool line = BiomeLandforms.IsLine(h.Form.Form) && h.Segments != null && h.Segments.Count > 0;
				if (line && (form.Anchor == BiomeLandformAnchor.OnTopOf && FollowsLine(form.Form) || form.Anchor == BiomeLandformAnchor.OnFlankOf && form.Form == BiomeLandformForm.Grooves))
				{
					if (FollowSite(input, form, id, h, seed, out BiomeLandformSite follow))
					{
						sites.Add(follow);
						made++;
					}
					continue;
				}
				float topShare = line ? Mathf.Clamp(h.Form.Top, 0.1f, 0.9f) : new BiomeLandforms.Shape(h, context).TopShare;
				int n = HostCount(form, h, line, topShare, seed);
				var own = new List<(Vector2 At, float Radius)>();
				for (int k = 0, tries = 0; k < n && tries < n * 4 && (form.PerScene <= 0 || made < form.PerScene); tries++)
				{
					uint memberSeed = BiomeLandforms.Hash(seed, (uint)tries, 0x0A8u);
					float size = float.NaN;
					if (!HostPlace(form, h, line, topShare, k, n, memberSeed, out Vector2 at, out float yaw, ref size))
					{
						continue;
					}
					if (input.Keep != null && input.Keep(at.x, at.y) >= 0.5f)
					{
						continue;
					}
					if (!TrySite(input, form, id, biomeIndex, null, at, HostGround(input, form, h, line, at), memberSeed, out BiomeLandformSite site, size, yaw))
					{
						continue;
					}
					float reach = SpacingRadius(site);
					bool clear = true;
					foreach ((Vector2 other, float radius) in own)
					{
						if (Vector2.Distance(other, site.Centre) < 0.85f * (reach + radius))
						{
							clear = false;
							break;
						}
					}
					if (!clear)
					{
						continue;
					}
					own.Add((site.Centre, reach));
					sites.Add(site);
					made++;
					k++;
				}
			}
		}

		private static float LineLength(in BiomeLandformSite site)
		{
			float total = 0f;
			foreach (BiomeLandformSegment seg in site.Segments)
			{
				total += Vector2.Distance(seg.A, seg.B);
			}
			return total;
		}

		/// <summary>How many an entry stands on one host: by its density over the host's top, flank or line, else half to all of its PerPatch.</summary>
		private static int HostCount(BiomeLandform form, in BiomeLandformSite h, bool line, float topShare, uint seed)
		{
			if (form.Anchor == BiomeLandformAnchor.OnTopOf && (line || form.FitShare > 0f))
			{
				// A line's far end, or one concentric with the host.
				return 1;
			}
			int n;
			if (form.Density > 0f)
			{
				float major = Mathf.Max(h.Radius, h.HalfLength), minor = h.Radius;
				float areaKm2 = line ? LineLength(h) * 2f * Mathf.Max(0.5f, h.Segments[0].Half) / 1e6f
					: form.Anchor == BiomeLandformAnchor.OnTopOf ? Mathf.PI * topShare * major * topShare * minor / 1e6f
					: Mathf.PI * major * minor * (1f - topShare * topShare) / 1e6f;
				float expected = form.Density * areaKm2;
				n = Mathf.FloorToInt(expected) + (BiomeLandforms.Unit(BiomeLandforms.Hash(seed, 0x0A9u)) < expected - Mathf.Floor(expected) ? 1 : 0);
			}
			else
			{
				int most = form.PerPatch;
				n = most <= 1 ? 1 : most / 2 + (int)(BiomeLandforms.Hash(seed, 0x0ABu) % (uint)(most - most / 2 + 1));
			}
			return form.PerPatch > 0 ? Mathf.Min(n, form.PerPatch) : n;
		}

		private static float YawTo(Vector2 dir) => Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

		/// <summary>Where member <paramref name="k"/> of <paramref name="n"/> stands on its host, and its heading when the entry takes the host's (NaN otherwise).</summary>
		private static bool HostPlace(BiomeLandform form, in BiomeLandformSite h, bool line, float topShare, int k, int n, uint seed, out Vector2 at, out float yaw, ref float size)
		{
			float U(uint salt) => BiomeLandforms.Unit(BiomeLandforms.Hash(seed, salt));
			bool hostDownhill = form.Align == BiomeLandformAlign.HostDownhill;
			yaw = float.NaN;
			at = h.Centre;
			var axis = new Vector2(Mathf.Cos(h.Yaw * Mathf.Deg2Rad), Mathf.Sin(h.Yaw * Mathf.Deg2Rad));
			var normal = new Vector2(-axis.y, axis.x);
			if (line)
			{
				if (form.Anchor == BiomeLandformAnchor.OnTopOf)
				{
					// The line's far end (a fan at a canyon's mouth), heading on along it.
					BiomeLandformSegment last = h.Segments[h.Segments.Count - 1];
					at = last.B;
					if (hostDownhill && (last.B - last.A).sqrMagnitude > 1e-6f)
					{
						yaw = YawTo((last.B - last.A).normalized);
					}
					return true;
				}
				float total = LineLength(h), along = (k + 0.5f + 0.6f * (U(1u) - 0.5f)) / Mathf.Max(1, n) * total;
				foreach (BiomeLandformSegment seg in h.Segments)
				{
					float span = Vector2.Distance(seg.A, seg.B);
					if (along > span)
					{
						along -= span;
						continue;
					}
					Vector2 dir = span > 1e-4f ? (seg.B - seg.A) / span : axis;
					var across = new Vector2(-dir.y, dir.x);
					float side = U(2u) < 0.5f ? 1f : -1f;
					at = Vector2.Lerp(seg.A, seg.B, span > 1e-4f ? along / span : 0f) + across * (side * seg.Half * Mathf.Lerp(topShare, 1f, 0.3f + 0.4f * U(3u)));
					if (hostDownhill)
					{
						yaw = YawTo(across * (IsCutHost(h) ? -side : side));
					}
					return true;
				}
				return false;
			}
			float major = Mathf.Max(h.Radius, h.HalfLength), minor = Mathf.Max(0.01f, h.Radius);
			if (form.Anchor == BiomeLandformAnchor.OnTopOf)
			{
				if (form.FitShare > 0f)
				{
					// Concentric with the host, its size that share of the host's (a flexural moat round a guyot).
					size = 2f * major * form.FitShare;
					return true;
				}
				float own = 0.5f * BiomeLandforms.Pick(form.Size, U(10u));
				float topMajor = Mathf.Max(0f, topShare * major - own), topMinor = Mathf.Max(0f, topShare * minor - own);
				if (major > 1.2f * minor)
				{
					// In a row along a long host's axis (vent mouths along a summit terrace).
					float t = (k + 0.5f) / Mathf.Max(1, n) - 0.5f;
					at = h.Centre + axis * (1.5f * t * topMajor) + normal * ((U(4u) - 0.5f) * 0.3f * topMinor);
				}
				else
				{
					float angle = U(5u) * Mathf.PI * 2f, d = Mathf.Sqrt(U(6u)) * Mathf.Min(topMajor, topMinor);
					at = h.Centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * d;
				}
				return true;
			}
			// Round the flank, evenly spaced from the host's own phase, a hashed way down it.
			float phase = BiomeLandforms.Unit(BiomeLandforms.Hash(h.Seed, 0x0AAu)) * Mathf.PI * 2f;
			float a = (k + 0.5f + 0.6f * (U(7u) - 0.5f)) / Mathf.Max(1, n) * Mathf.PI * 2f + phase;
			float rho = Mathf.Lerp(topShare, 1f, 0.3f + 0.4f * U(8u));
			Vector2 offset = axis * (Mathf.Cos(a) * major * rho) + normal * (Mathf.Sin(a) * minor * rho);
			at = h.Centre + offset;
			if (hostDownhill && offset.sqrMagnitude > 1e-6f)
			{
				yaw = YawTo(offset.normalized * (IsCutHost(h) ? -1f : 1f));
			}
			return true;
		}

		/// <summary>The ground a member on a host stands on as planned: a table's or platform's top, a pan's floor, a bowl's floor, a mound's summit; else the ground there.</summary>
		private static float HostGround(LandformPlanInput input, BiomeLandform form, in BiomeLandformSite h, bool line, Vector2 at)
		{
			if (form.Anchor != BiomeLandformAnchor.OnTopOf || line)
			{
				return input.Ground(at.x, at.y);
			}
			if (!float.IsNaN(h.Level))
			{
				return h.Level;
			}
			switch (h.Form.Family)
			{
				case BiomeLandformFamily.Crater: return h.Ground - h.Height;
				case BiomeLandformFamily.Mound: return input.Ground(at.x, at.y) + h.Height;
				default: return input.Ground(at.x, at.y);
			}
		}

		/// <summary>
		/// An entry that follows its line host (pure but for the ground): cyclic steps on its floor, pits along it, or gullies down
		/// both its walls, <see cref="BiomeLandform.Length"/> apart (each running from the wall's foot to the rim, tapering both ends).
		/// </summary>
		private static bool FollowSite(LandformPlanInput input, BiomeLandform form, int id, in BiomeLandformSite h, uint seed, out BiomeLandformSite site)
		{
			float U(uint salt) => BiomeLandforms.Unit(BiomeLandforms.Hash(seed, salt));
			float width = BiomeLandforms.Pick(form.Width, U(12u)), height = BiomeLandforms.Pick(form.Height, U(13u));
			float hostTop = Mathf.Clamp(h.Form.Top, 0.1f, 0.9f);
			var segments = new List<BiomeLandformSegment>();
			if (form.Form == BiomeLandformForm.Grooves)
			{
				float half = Mathf.Max(0.25f, 0.5f * width);
				float at = 0.5f * Mathf.Max(4f, BiomeLandforms.Pick(form.Length, U(20u)));
				int index = 0;
				float start = 0f;
				foreach (BiomeLandformSegment seg in h.Segments)
				{
					float span = Vector2.Distance(seg.A, seg.B);
					if (span < 1e-4f)
					{
						continue;
					}
					Vector2 dir = (seg.B - seg.A) / span, across = new Vector2(-dir.y, dir.x);
					while (at < start + span && segments.Count < MaximumSegments)
					{
						Vector2 p = Vector2.Lerp(seg.A, seg.B, (at - start) / span);
						for (int side = -1; side <= 1; side += 2)
						{
							Vector2 at0 = p + dir * ((BiomeLandforms.Unit(BiomeLandforms.Hash(seed, (uint)index, (uint)(side + 2))) - 0.5f) * 0.3f * half * 4f);
							Vector2 foot = at0 + across * (side * seg.Half * Mathf.Max(0.15f, hostTop + 0.05f)), rim = at0 + across * (side * seg.Half * 0.97f);
							segments.AddRange(Line(new List<Vector2> { foot, rim }, half, height, true));
						}
						at += Mathf.Max(4f, BiomeLandforms.Pick(form.Length, BiomeLandforms.Unit(BiomeLandforms.Hash(seed, (uint)index, 3u))));
						index++;
					}
					start += span;
				}
			}
			else
			{
				foreach (BiomeLandformSegment seg in h.Segments)
				{
					BiomeLandformSegment copy = seg;
					copy.Half = form.Form == BiomeLandformForm.Treads ? Mathf.Max(0.5f, seg.Half * Mathf.Max(0.3f, h.Form.Top)) : Mathf.Max(0.25f, 0.5f * width);
					copy.Depth = height;
					// Only the stretches whose reach stays inside the scene (pits wider than the rift they follow near its edge).
					if (Inside(input, copy.A, 3.2f * copy.Half + 1f) && Inside(input, copy.B, 3.2f * copy.Half + 1f))
					{
						segments.Add(copy);
					}
				}
			}
			site = new BiomeLandformSite
			{
				Form = form,
				BiomeID = id,
				Centre = h.Centre,
				Radius = Mathf.Max(0.25f, 0.5f * width),
				HalfLength = h.HalfLength,
				HalfWidth = Mathf.Max(0.1f, 0.5f * width),
				Height = height,
				Yaw = h.Yaw,
				Seed = seed,
				Level = float.NaN,
				Ground = h.Ground,
				Segments = segments,
			};
			if (segments.Count == 0)
			{
				return false;
			}
			site.Bounds = BiomeLandforms.BoundsOf(site);
			Vector4 bb = site.Bounds, scene = input.SceneBounds;
			return bb.x >= scene.x && bb.y >= scene.y && bb.z <= scene.z && bb.w <= scene.w;
		}

		/// <summary>
		/// The obstacles a dune field's wake lies behind (decision 91): every mound, crater or table of its biome placed before it
		/// inside its box and at least 10 m across its radius, each as a segment (A its centre, Half its radius, Depth its wake's
		/// length: its diameter times the field's Wake range by its hash); null for none.
		/// </summary>
		private static List<BiomeLandformSegment> WakeObstacles(BiomeLandform form, in BiomeLandformSite field, List<BiomeLandformSite> sites)
		{
			var obstacles = new List<BiomeLandformSegment>();
			Vector4 b = field.Bounds;
			foreach (BiomeLandformSite s in sites)
			{
				BiomeLandformFamily family = s.Form.Family;
				if (s.BiomeID != field.BiomeID || family != BiomeLandformFamily.Mound && family != BiomeLandformFamily.Crater && family != BiomeLandformFamily.Mesa)
				{
					continue;
				}
				if (s.Radius < 10f || s.Centre.x < b.x || s.Centre.x > b.z || s.Centre.y < b.y || s.Centre.y > b.w)
				{
					continue;
				}
				float length = 2f * s.Radius * BiomeLandforms.Pick(form.Wake, BiomeLandforms.Unit(BiomeLandforms.Hash(s.Seed, 0x3A4Eu)));
				obstacles.Add(new BiomeLandformSegment { A = s.Centre, B = s.Centre, Half = s.Radius, Depth = length });
				if (obstacles.Count >= 256)
				{
					break;
				}
			}
			return obstacles.Count > 0 ? obstacles : null;
		}

		/// <summary>
		/// A strike-slip trace's offset (decision 90): every line placed before it (any biome's) moved along the trace by its Shift
		/// on the trace's left side, all of it between the trace's ends and fading to nothing a quarter of its length past each.
		/// </summary>
		private static void ShiftLines(List<BiomeLandformSite> sites, int trace)
		{
			BiomeLandformSite t = sites[trace];
			if (!TraceOf(in t, out Vector2 start, out Vector2 d, out float length, out float offset))
			{
				return;
			}
			float fade = 0.25f * length;
			Vector2 Move(Vector2 p)
			{
				Vector2 rel = p - start;
				if (d.x * rel.y - d.y * rel.x <= 0f)
				{
					return p;
				}
				float along = Vector2.Dot(rel, d);
				float w = Mathf.SmoothStep(0f, 1f, (along + fade) / fade) * (1f - Mathf.SmoothStep(0f, 1f, (along - length) / fade));
				return p + d * (offset * w);
			}
			for (int i = 0; i < trace; i++)
			{
				BiomeLandformSite s = sites[i];
				if (!BiomeLandforms.IsLine(s.Form.Form) || s.Segments == null || s.Segments.Count == 0)
				{
					continue;
				}
				var moved = new List<BiomeLandformSegment>(s.Segments.Count);
				foreach (BiomeLandformSegment seg in s.Segments)
				{
					BiomeLandformSegment m = seg;
					m.A = Move(seg.A);
					m.B = Move(seg.B);
					moved.Add(m);
				}
				ReAlong(moved);
				s.Segments = moved;
				s.Centre = Move(s.Centre);
				s.Bounds = BiomeLandforms.BoundsOf(s);
				sites[i] = s;
			}
		}

		/// <summary>
		/// A strike-slip trace's line and slip (pure; what <see cref="ShiftLines"/> moves the lines by): from its first segment's start to
		/// its last's end, its unit direction and length, and the slip its seed picks from its Shift range. False for a site with no
		/// Shift, no segments or a trace shorter than a metre.
		/// </summary>
		public static bool TraceOf(in BiomeLandformSite t, out Vector2 start, out Vector2 direction, out float length, out float offset)
		{
			start = direction = default;
			length = offset = 0f;
			if (t.Form.Shift == Vector2.zero || t.Segments == null || t.Segments.Count == 0)
			{
				return false;
			}
			start = t.Segments[0].A;
			Vector2 d = t.Segments[t.Segments.Count - 1].B - start;
			length = d.magnitude;
			if (length < 1f)
			{
				return false;
			}
			direction = d / length;
			offset = BiomeLandforms.Pick(t.Form.Shift, BiomeLandforms.Unit(BiomeLandforms.Hash(t.Seed, 0x5F1u)));
			return true;
		}

		/// <summary>
		/// The strike-slip traces of a scene's landforms as the ways jog at them (2026-10-26, I2l-b, <see cref="PathJogs"/>): each site
		/// with a Shift, in the order placed. Empty for none (every scene until a round names a strike-slip preset).
		/// </summary>
		public static List<PathJogs.Trace> StrikeSlipTraces(IReadOnlyList<BiomeLandformSite> sites)
		{
			var traces = new List<PathJogs.Trace>();
			if (sites == null)
			{
				return traces;
			}
			foreach (BiomeLandformSite site in sites)
			{
				if (TraceOf(in site, out Vector2 start, out Vector2 direction, out float length, out float offset) && offset != 0f)
				{
					traces.Add(new PathJogs.Trace(start, direction, length, offset));
				}
			}
			return traces;
		}

		/// <summary>Measures a site's lines again after their points moved: each run (from a stretch starting at 0) its own along-distances and length.</summary>
		private static void ReAlong(List<BiomeLandformSegment> segments)
		{
			int runStart = 0;
			float along = 0f;
			for (int i = 0; i <= segments.Count; i++)
			{
				if (i == segments.Count || i > 0 && segments[i].AlongA == 0f)
				{
					for (int j = runStart; j < i; j++)
					{
						BiomeLandformSegment done = segments[j];
						done.LineLength = along;
						segments[j] = done;
					}
					runStart = i;
					along = 0f;
					if (i == segments.Count)
					{
						break;
					}
				}
				BiomeLandformSegment seg = segments[i];
				seg.AlongA = along;
				along += Vector2.Distance(seg.A, seg.B);
				seg.AlongB = along;
				segments[i] = seg;
			}
		}

		/// <summary>
		/// A cycloid's line (decision 90, pure): 4 … its Count arcs along its axis, each a chord about its size long (± 20 % by
		/// its hash), every one bulging the same way (its axis's left) by three tenths of its chord, so the cusps stand in one line.
		/// </summary>
		private static List<Vector2> CycloidLine(Vector2 at, Vector2 axis, in BiomeLandformSite site)
		{
			int most = Mathf.Max(4, site.Form.Count > 0 ? site.Form.Count : 6);
			int n = 4 + (int)(BiomeLandforms.Hash(site.Seed, 0xC7Cu) % (uint)(most - 3));
			float mean = Mathf.Max(20f, 2f * site.Radius);
			var chords = new float[n];
			float total = 0f;
			for (int k = 0; k < n; k++)
			{
				chords[k] = mean * (0.8f + 0.4f * BiomeLandforms.Unit(BiomeLandforms.Hash(site.Seed, (uint)k, 0xC7Du)));
				total += chords[k];
			}
			var normal = new Vector2(-axis.y, axis.x);
			Vector2 cusp = at - axis * (0.5f * total);
			var points = new List<Vector2> { cusp };
			for (int k = 0; k < n; k++)
			{
				float c = chords[k], sag = 0.3f * c;
				float r = (c * c * 0.25f + sag * sag) / (2f * sag);
				Vector2 centre = cusp + axis * (0.5f * c) - normal * (r - sag);
				Vector2 next = cusp + axis * c;
				float a0 = Mathf.Atan2(cusp.y - centre.y, cusp.x - centre.x) * Mathf.Rad2Deg;
				float sweep = Mathf.DeltaAngle(a0, Mathf.Atan2(next.y - centre.y, next.x - centre.x) * Mathf.Rad2Deg);
				int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(sweep) * Mathf.Deg2Rad * r / 15f), 4, 60);
				for (int i = 1; i <= steps; i++)
				{
					float a = (a0 + sweep * i / steps) * Mathf.Deg2Rad;
					points.Add(i == steps ? next : centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
				}
				cusp = next;
			}
			return points;
		}
	}
}
#endif
