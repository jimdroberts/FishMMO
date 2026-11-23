using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Builds a species' procedural body (<see cref="WildlifeBodySpec"/>): one flat-shaded low-poly mesh, shared by every
	/// animal of the species, animated entirely in the vertex shader (FishMMO/Wildlife).
	/// </summary>
	/// <remarks>
	/// <para>The animal faces +Z, stands on y = 0 and is centred on x = 0.</para>
	/// <para>
	/// The vertex contract: colour rgb is the albedo (back, belly, accent, horn); UV1 xyz is the pivot of the vertex's part
	/// in mesh space and w its part (<see cref="Part"/>). The shader swings each leg about its hip, the head group about the
	/// neck base and the tail about its root.
	/// </para>
	/// </remarks>
	public static class WildlifeBodyMesh
	{
		/// <summary>Body parts, as the shader reads them from UV1.w.</summary>
		public enum Part
		{
			Body = 0,
			FrontLeft = 1,
			FrontRight = 2,
			HindLeft = 3,
			HindRight = 4,
			Head = 5,
			Tail = 6,
			LeftWing = 7,
			RightWing = 8,
		}

		private static readonly Color HornColour = new Color(0.78f, 0.72f, 0.6f);
		private static readonly Dictionary<WildlifeTemplate, Mesh> cache = new Dictionary<WildlifeTemplate, Mesh>();

		/// <summary>The species' shared mesh, built on first use.</summary>
		public static Mesh For(WildlifeTemplate species)
		{
			if (species == null)
			{
				return null;
			}
			if (cache.TryGetValue(species, out Mesh mesh) && mesh != null)
			{
				return mesh;
			}
			mesh = Build(species.Body, species.name);
			// Shared by every animal of the species and never edited: the CPU copy can go.
			mesh.UploadMeshData(true);
			cache[species] = mesh;
			return mesh;
		}

		/// <summary>Builds a body mesh from <paramref name="spec"/>.</summary>
		public static Mesh Build(WildlifeBodySpec spec, string name = "Wildlife")
		{
			var b = new Builder();
			float legTop = spec.LegLength + spec.Depth * 0.35f;
			float bodyY = spec.LegLength + spec.Depth * 0.5f;
			float halfL = spec.Length * 0.5f;

			// Torso: an eight-sided prism, the chest a little deeper than the rump.
			b.Prism(new Vector3(0f, bodyY, -halfL), new Vector3(0f, bodyY + spec.Depth * 0.04f, halfL),
				spec.Width * 0.5f * 0.92f, spec.Depth * 0.5f * 0.9f, spec.Width * 0.5f, spec.Depth * 0.5f,
				spec.Back, spec.Belly, Part.Body, new Vector3(0f, bodyY, 0f));

			// Legs: tapered, a dark hoof or paw at the foot. A winged animal's arms are its wings: hind legs only.
			float legX = Mathf.Max(spec.Width * 0.5f - spec.LegThickness * 0.6f, spec.LegThickness);
			float legZ = halfL - spec.LegThickness * 1.6f;
			if (spec.WingSpan > 0f)
			{
				Wing(b, spec, bodyY, halfL, -1, Part.LeftWing);
				Wing(b, spec, bodyY, halfL, 1, Part.RightWing);
			}
			else
			{
				Leg(b, spec, new Vector3(-legX, 0f, legZ), legTop, Part.FrontLeft);
				Leg(b, spec, new Vector3(legX, 0f, legZ), legTop, Part.FrontRight);
			}
			Leg(b, spec, new Vector3(-legX, 0f, -legZ), legTop, Part.HindLeft);
			Leg(b, spec, new Vector3(legX, 0f, -legZ), legTop, Part.HindRight);

			// Neck and head, one group swinging about the neck base.
			float raise = spec.NeckRaise * Mathf.Deg2Rad;
			Vector3 neckBase = new Vector3(0f, spec.LegLength + spec.Depth * 0.72f, halfL - spec.Depth * 0.15f);
			Vector3 neckDir = new Vector3(0f, Mathf.Sin(raise), Mathf.Cos(raise));
			Vector3 neckEnd = neckBase + neckDir * spec.NeckLength;
			float neckW = spec.HeadSize * 0.55f;
			b.Segment(neckBase, neckEnd, neckW * 1.25f, spec.Depth * 0.32f, neckW, spec.HeadSize * 0.5f, spec.Back, spec.Belly, Part.Head, neckBase);

			Vector3 headFwd = new Vector3(0f, -0.25f, 1f).normalized;
			Vector3 headBack = neckEnd - headFwd * spec.HeadSize * 0.25f;
			Vector3 muzzleStart = headBack + headFwd * spec.HeadLength * 0.62f;
			Vector3 headEnd = headBack + headFwd * spec.HeadLength;
			b.Segment(headBack, muzzleStart, spec.HeadSize * 0.5f, spec.HeadSize * 0.55f, spec.HeadSize * 0.4f, spec.HeadSize * 0.42f, spec.Back, spec.Belly, Part.Head, neckBase);
			b.Segment(muzzleStart, headEnd, spec.HeadSize * 0.36f, spec.HeadSize * 0.36f, spec.HeadSize * 0.26f, spec.HeadSize * 0.26f, spec.Accent, spec.Accent, Part.Head, neckBase);

			// Ears, angled out and back.
			Vector3 earRoot = headBack + Vector3.up * spec.HeadSize * 0.45f;
			for (int side = -1; side <= 1; side += 2)
			{
				Vector3 root = earRoot + Vector3.right * side * spec.HeadSize * 0.35f;
				Vector3 tip = root + new Vector3(side * 0.6f, 0.75f, -0.4f).normalized * spec.EarSize;
				b.Segment(root, tip, spec.EarSize * 0.22f, spec.EarSize * 0.08f, spec.EarSize * 0.04f, spec.EarSize * 0.03f, spec.Back, spec.Accent, Part.Head, neckBase);
			}

			Horns(b, spec, headBack, headFwd, headEnd, neckBase);

			// Tail, from the top of the rump.
			if (spec.TailLength > 0f)
			{
				float tr = spec.TailRaise * Mathf.Deg2Rad;
				Vector3 tailRoot = new Vector3(0f, spec.LegLength + spec.Depth * 0.8f, -halfL + 0.02f);
				Vector3 tailDir = new Vector3(0f, -Mathf.Cos(tr), -Mathf.Sin(tr));
				float tw = Mathf.Max(spec.TailLength * 0.14f, 0.03f);
				b.Segment(tailRoot, tailRoot + tailDir * spec.TailLength, tw, tw, tw * 0.55f, tw * 0.55f, spec.Back, spec.Belly, Part.Tail, tailRoot);
			}

			Mesh mesh = b.ToMesh(name + " Body");
			return mesh;
		}

		private static void Leg(Builder b, WildlifeBodySpec spec, Vector3 foot, float top, Part part)
		{
			float t = spec.LegThickness;
			Vector3 hip = new Vector3(foot.x, top, foot.z);
			Vector3 hoofTop = new Vector3(foot.x, Mathf.Min(spec.LegLength * 0.14f, 0.12f), foot.z);
			Vector3 pivot = new Vector3(foot.x, spec.LegLength + spec.Depth * 0.3f, foot.z);
			b.Segment(hip, hoofTop, t * 0.85f, t * 1.15f, t * 0.42f, t * 0.5f, spec.Back, spec.Belly, part, pivot);
			b.Segment(hoofTop, foot, t * 0.48f, t * 0.55f, t * 0.55f, t * 0.62f, spec.Accent, spec.Accent, part, pivot);
		}

		/// <summary>
		/// One wing, from the shoulder out along <paramref name="side"/> (-1 left, +1 right): the arm and three finger bones in the
		/// accent colour, and the membrane between them (both faces, the back colour over, the belly colour under), swinging as one
		/// part about the shoulder.
		/// </summary>
		private static void Wing(Builder b, WildlifeBodySpec spec, float bodyY, float halfL, int side, Part part)
		{
			float span = spec.WingSpan * 0.5f - spec.Width * 0.5f;
			float bone = Mathf.Max(spec.LegThickness * 0.6f, 0.015f);
			Vector3 shoulder = new Vector3(side * spec.Width * 0.45f, bodyY + spec.Depth * 0.3f, halfL * 0.45f);
			Vector3 wrist = shoulder + new Vector3(side * span * 0.45f, span * 0.08f, span * 0.12f);
			Vector3 lead = shoulder + new Vector3(side * span, span * 0.02f, -span * 0.05f);
			Vector3 middle = wrist + new Vector3(side * span * 0.42f, -span * 0.05f, -span * 0.38f);
			Vector3 trail = wrist + new Vector3(side * span * 0.2f, -span * 0.08f, -span * 0.62f);
			Vector3 hip = new Vector3(side * spec.Width * 0.4f, bodyY, -halfL * 0.75f);

			b.Segment(shoulder, wrist, bone, bone, bone * 0.8f, bone * 0.8f, spec.Accent, spec.Accent, part, shoulder);
			b.Segment(wrist, lead, bone * 0.7f, bone * 0.7f, bone * 0.3f, bone * 0.3f, spec.Accent, spec.Accent, part, shoulder);
			b.Segment(wrist, middle, bone * 0.6f, bone * 0.6f, bone * 0.25f, bone * 0.25f, spec.Accent, spec.Accent, part, shoulder);
			b.Segment(wrist, trail, bone * 0.6f, bone * 0.6f, bone * 0.25f, bone * 0.25f, spec.Accent, spec.Accent, part, shoulder);

			// The membrane: shoulder to the leading finger, between the fingers, and from the last finger back to the hip.
			b.Sheet(shoulder, wrist, lead, spec.Back, spec.Belly, part, shoulder);
			b.Sheet(wrist, middle, lead, spec.Back, spec.Belly, part, shoulder);
			b.Sheet(wrist, trail, middle, spec.Back, spec.Belly, part, shoulder);
			b.Sheet(shoulder, hip, wrist, spec.Back, spec.Belly, part, shoulder);
			b.Sheet(wrist, hip, trail, spec.Back, spec.Belly, part, shoulder);
		}

		private static void Horns(Builder b, WildlifeBodySpec spec, Vector3 headBack, Vector3 headFwd, Vector3 headEnd, Vector3 pivot)
		{
			float size = spec.HornSize;
			if (spec.Horns == WildlifeHorns.None || size <= 0f)
			{
				return;
			}
			if (spec.Horns == WildlifeHorns.Tusks)
			{
				for (int side = -1; side <= 1; side += 2)
				{
					Vector3 root = headEnd - headFwd * spec.HeadLength * 0.25f + Vector3.right * side * spec.HeadSize * 0.22f - Vector3.up * spec.HeadSize * 0.1f;
					Vector3 tip = root + new Vector3(side * 0.35f, 0.8f, 0.45f).normalized * size;
					b.Segment(root, tip, size * 0.16f, size * 0.16f, size * 0.03f, size * 0.03f, HornColour, HornColour, Part.Head, pivot);
				}
				return;
			}
			if (spec.Horns == WildlifeHorns.Horns)
			{
				// Two short horns curving out, up and forward off the crown (a cow, a dragon, the mounts).
				for (int side = -1; side <= 1; side += 2)
				{
					Vector3 root = headBack + Vector3.up * spec.HeadSize * 0.45f + Vector3.right * side * spec.HeadSize * 0.32f;
					Vector3 mid = root + new Vector3(side * 0.85f, 0.45f, -0.25f).normalized * size * 0.55f;
					Vector3 tip = mid + new Vector3(side * 0.2f, 0.75f, 0.6f).normalized * size * 0.5f;
					b.Segment(root, mid, size * 0.18f, size * 0.18f, size * 0.12f, size * 0.12f, HornColour, HornColour, Part.Head, pivot);
					b.Segment(mid, tip, size * 0.12f, size * 0.12f, size * 0.02f, size * 0.02f, HornColour, HornColour, Part.Head, pivot);
				}
				return;
			}
			float t = size * (spec.Horns == WildlifeHorns.BroadAntlers ? 0.07f : 0.05f);
			for (int side = -1; side <= 1; side += 2)
			{
				Vector3 root = headBack + Vector3.up * spec.HeadSize * 0.5f + Vector3.right * side * spec.HeadSize * 0.25f;
				Vector3 beamDir = new Vector3(side * 0.55f, 0.85f, -0.25f).normalized;
				Vector3 mid = root + beamDir * size * 0.55f;
				Vector3 tip = mid + new Vector3(side * 0.25f, 0.9f, 0.2f).normalized * size * 0.5f;
				b.Segment(root, mid, t, t, t * 0.8f, t * 0.8f, HornColour, HornColour, Part.Head, pivot);
				b.Segment(mid, tip, t * 0.8f, t * 0.8f, t * 0.35f, t * 0.35f, HornColour, HornColour, Part.Head, pivot);
				// Tines forward off the beam.
				for (int i = 0; i < 2; ++i)
				{
					Vector3 at = Vector3.Lerp(root, tip, 0.35f + 0.3f * i);
					Vector3 tine = at + new Vector3(side * 0.1f, 0.55f, 0.85f).normalized * size * 0.28f;
					b.Segment(at, tine, t * 0.6f, t * 0.6f, t * 0.2f, t * 0.2f, HornColour, HornColour, Part.Head, pivot);
				}
				if (spec.Horns == WildlifeHorns.BroadAntlers)
				{
					// A flat palm at the top of the beam.
					Vector3 palm = Vector3.Lerp(mid, tip, 0.6f);
					b.Segment(palm - beamDir * size * 0.12f, palm + new Vector3(side, 0.3f, 0f).normalized * size * 0.35f,
						size * 0.16f, t * 0.4f, size * 0.12f, t * 0.3f, HornColour, HornColour, Part.Head, pivot);
				}
			}
		}

		/// <summary>Accumulates flat-shaded faces with the part contract.</summary>
		private sealed class Builder
		{
			private readonly List<Vector3> positions = new List<Vector3>(512);
			private readonly List<Vector3> normals = new List<Vector3>(512);
			private readonly List<Color> colours = new List<Color>(512);
			private readonly List<Vector4> parts = new List<Vector4>(512);
			private readonly List<int> triangles = new List<int>(1024);

			/// <summary>
			/// A rectangular frustum from <paramref name="a"/> to <paramref name="b"/>: half width and half height at each end.
			/// Faces looking down take the under colour.
			/// </summary>
			public void Segment(Vector3 a, Vector3 bEnd, float wa, float ha, float wb, float hb, Color top, Color under, Part part, Vector3 pivot)
			{
				Vector3 axis = bEnd - a;
				if (axis.sqrMagnitude < 1e-8f)
				{
					return;
				}
				axis.Normalize();
				Vector3 side = Vector3.Cross(Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up, axis).normalized;
				Vector3 up = Vector3.Cross(axis, side).normalized;
				Vector3[] ring0 = { a + (-side * wa) + (-up * ha), a + (side * wa) + (-up * ha), a + (side * wa) + (up * ha), a + (-side * wa) + (up * ha) };
				Vector3[] ring1 = { bEnd + (-side * wb) + (-up * hb), bEnd + (side * wb) + (-up * hb), bEnd + (side * wb) + (up * hb), bEnd + (-side * wb) + (up * hb) };
				Rings(ring0, ring1, top, under, part, pivot);
			}

			/// <summary>An eight-sided prism along a to b: radii at each end (x across, y up).</summary>
			public void Prism(Vector3 a, Vector3 bEnd, float rxa, float rya, float rxb, float ryb, Color top, Color under, Part part, Vector3 pivot)
			{
				const int sides = 8;
				var ring0 = new Vector3[sides];
				var ring1 = new Vector3[sides];
				for (int i = 0; i < sides; ++i)
				{
					float angle = (i + 0.5f) * Mathf.PI * 2f / sides;
					float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
					ring0[i] = a + new Vector3(c * rxa, s * rya, 0f);
					ring1[i] = bEnd + new Vector3(c * rxb, s * ryb, 0f);
				}
				Rings(ring0, ring1, top, under, part, pivot);
			}

			/// <summary>A flat triangle seen from both sides: <paramref name="over"/> on the face looking up, <paramref name="under"/> below.</summary>
			public void Sheet(Vector3 a, Vector3 bPoint, Vector3 c, Color over, Color under, Part part, Vector3 pivot)
			{
				Vector3 n = Vector3.Cross(bPoint - a, c - a);
				if (n.sqrMagnitude < 1e-12f)
				{
					return;
				}
				Vector3 up = n.y >= 0f ? n.normalized : -n.normalized;
				Vector3 centre = (a + bPoint + c) / 3f;
				// Each face wound away from a point just behind it, so each is seen from its own side.
				Triangle(a, bPoint, c, centre - up * 0.01f, over, under, part, pivot);
				Triangle(a, c, bPoint, centre + up * 0.01f, over, under, part, pivot);
			}

			private void Rings(Vector3[] r0, Vector3[] r1, Color top, Color under, Part part, Vector3 pivot)
			{
				int n = r0.Length;
				Vector3 c0 = Vector3.zero, c1 = Vector3.zero;
				for (int i = 0; i < n; ++i)
				{
					c0 += r0[i];
					c1 += r1[i];
				}
				c0 /= n;
				c1 /= n;
				for (int i = 0; i < n; ++i)
				{
					int j = (i + 1) % n;
					Quad(r0[i], r1[i], r1[j], r0[j], (c0 + c1) * 0.5f, top, under, part, pivot);
				}
				// Caps.
				for (int i = 1; i < n - 1; ++i)
				{
					Triangle(r0[0], r0[i + 1], r0[i], c1, top, under, part, pivot);
					Triangle(r1[0], r1[i], r1[i + 1], c0, top, under, part, pivot);
				}
			}

			private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside, Color top, Color under, Part part, Vector3 pivot)
			{
				Triangle(a, b, c, inside, top, under, part, pivot);
				Triangle(a, c, d, inside, top, under, part, pivot);
			}

			/// <summary>One flat triangle, wound to face away from <paramref name="inside"/>.</summary>
			private void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 inside, Color top, Color under, Part part, Vector3 pivot)
			{
				Vector3 n = Vector3.Cross(b - a, c - a);
				if (n.sqrMagnitude < 1e-12f)
				{
					return;
				}
				n.Normalize();
				if (Vector3.Dot(n, (a + b + c) / 3f - inside) < 0f)
				{
					(b, c) = (c, b);
					n = -n;
				}
				Color colour = n.y < -0.35f ? under : top;
				var pv = new Vector4(pivot.x, pivot.y, pivot.z, (float)part);
				int i0 = positions.Count;
				positions.Add(a); positions.Add(b); positions.Add(c);
				for (int k = 0; k < 3; ++k)
				{
					normals.Add(n);
					colours.Add(colour);
					parts.Add(pv);
				}
				triangles.Add(i0); triangles.Add(i0 + 1); triangles.Add(i0 + 2);
			}

			public Mesh ToMesh(string name)
			{
				var mesh = new Mesh { name = name };
				mesh.SetVertices(positions);
				mesh.SetNormals(normals);
				mesh.SetColors(colours);
				mesh.SetUVs(1, parts);
				mesh.SetTriangles(triangles, 0);
				mesh.RecalculateBounds();
				// Room for the swing, the lunge and the fall onto a side, so the culler never clips a pose.
				Bounds bounds = mesh.bounds;
				bounds.Expand(Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
				mesh.bounds = bounds;
				return mesh;
			}
		}
	}
}
