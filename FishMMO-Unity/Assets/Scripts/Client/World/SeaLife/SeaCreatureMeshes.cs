using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The sea creatures' meshes, built at runtime from a few numbers: one unit long, nose toward +z, back up
	/// +y, centred on the origin (a jelly's bell is one unit across, its crown up; a crab one unit wide with its
	/// legs). The vertex layout is FishSeaLife.hlsl's: colour (linear, alpha the opacity), TEXCOORD0 x along the
	/// body and y out from its midline, TEXCOORD1 which appendage and how far out along it.
	/// </summary>
	/// <remarks>
	/// Small: a baitfish is about 200 vertices and a whale 300, because a school is drawn by the hundred.
	/// Fins and tentacles are single sheets; the shader draws both sides and turns the normal to the eye.
	/// Every number is fixed, so every player's fish are the same shape.
	/// </remarks>
	public static class SeaCreatureMeshes
	{
		/// <summary>The mesh for a kind (its shape and colours), built fresh.</summary>
		public static Mesh Build(SeaCreatureKind kind)
		{
			var b = new Builder();
			Color back = kind.Back.linear, belly = kind.Belly.linear;
			switch (kind.Shape)
			{
				case SeaCreatureShape.ReefFish:
					Fish(b, back, belly, height: 0.5f, width: 0.12f, fork: 0.15f, dorsal: 0.14f, dorsalFrom: 0.12f, dorsalTo: 0.72f, pointed: false, flukes: false, tail: 0.95f);
					break;
				case SeaCreatureShape.Shark:
					Fish(b, back, belly, height: 0.17f, width: 0.15f, fork: 0.6f, dorsal: 0.55f, dorsalFrom: 0.32f, dorsalTo: 0.5f, pointed: true, flukes: false, tail: 1.4f);
					if (kind.HammerHead)
					{
						HammerHead(b, back);
					}
					break;
				case SeaCreatureShape.Mola:
					Mola(b, back, belly);
					break;
				case SeaCreatureShape.Billfish:
					Fish(b, back, belly, height: 0.13f, width: 0.09f, fork: 0.9f, dorsal: 0.9f, dorsalFrom: 0.15f, dorsalTo: 0.6f, pointed: true, flukes: false, tail: 1.6f);
					Limb(b, new[] { new Vector3(0f, 0.004f, 0.47f), new Vector3(0f, 0f, 0.62f), new Vector3(0f, -0.002f, 0.8f) }, 0.014f, back, 0);
					break;
				case SeaCreatureShape.Nautilus:
					Nautilus(b, back, belly);
					break;
				case SeaCreatureShape.Dugong:
					Fish(b, back, belly, height: 0.22f, width: 0.26f, fork: 0.3f, dorsal: 0f, dorsalFrom: 0.5f, dorsalTo: 0.55f, pointed: false, flukes: true, tail: 1.3f);
					break;
				case SeaCreatureShape.Sawfish:
					Fish(b, back, belly, height: 0.11f, width: 0.2f, fork: 0.5f, dorsal: 0.35f, dorsalFrom: 0.35f, dorsalTo: 0.48f, pointed: true, flukes: false, tail: 1.2f);
					Saw(b, Color.Lerp(back, belly, 0.4f));
					break;
				case SeaCreatureShape.Mudskipper:
					Fish(b, back, belly, height: 0.2f, width: 0.15f, fork: 0.05f, dorsal: 0.6f, dorsalFrom: 0.22f, dorsalTo: 0.45f, pointed: false, flukes: false, tail: 0.9f);
					for (int s = -1; s <= 1; s += 2)
					{
						Ellipsoid(b, new Vector3(s * 0.035f, 0.09f, 0.38f), new Vector3(0.03f, 0.03f, 0.03f), 0.03f, 4, 6, (lat, lon) => new Color(0.03f, 0.03f, 0.035f), Vector2.zero);
					}
					break;
				case SeaCreatureShape.Whale:
					Fish(b, back, belly, height: 0.19f, width: 0.2f, fork: 0.35f, dorsal: 0.12f, dorsalFrom: 0.62f, dorsalTo: 0.72f, pointed: false, flukes: true, tail: 1.5f);
					break;
				case SeaCreatureShape.Ray:
					Ray(b, back, belly);
					break;
				case SeaCreatureShape.Turtle:
					Turtle(b, back, belly);
					break;
				case SeaCreatureShape.Jelly:
					Jelly(b, back, belly);
					break;
				case SeaCreatureShape.Crab:
					Crab(b, back, belly);
					break;
				case SeaCreatureShape.Eel:
					Eel(b, back, belly);
					break;
				default:
					Fish(b, back, belly, height: 0.22f, width: 0.11f, fork: 0.5f, dorsal: 0.2f, dorsalFrom: 0.3f, dorsalTo: 0.5f, pointed: false, flukes: false, tail: 1f);
					break;
			}
			Mesh mesh = b.ToMesh(kind.Name);
			// The shader swings tails and wings by up to about a third of a length: room for it.
			Bounds bounds = mesh.bounds;
			bounds.Expand(0.6f);
			mesh.bounds = bounds;
			mesh.UploadMeshData(true);
			return mesh;
		}

		// ── The open sea's shapes (2026-10-25, I2p) ───────────────────

		/// <summary>A hammerhead's cephalofoil: a wide flat blade across the nose, its eyes at the tips.</summary>
		private static void HammerHead(Builder b, Color back)
		{
			Ellipsoid(b, new Vector3(0f, 0.005f, 0.44f), new Vector3(0.17f, 0.02f, 0.045f), 0.016f, 4, 12, (lat, lon) => back * (lat > 0f ? 1f : 1.15f), Vector2.zero);
			for (int s = -1; s <= 1; s += 2)
			{
				Ellipsoid(b, new Vector3(s * 0.168f, 0.008f, 0.44f), new Vector3(0.012f, 0.012f, 0.012f), 0.012f, 3, 5, (lat, lon) => new Color(0.02f, 0.02f, 0.025f), Vector2.zero);
			}
		}

		/// <summary>
		/// An ocean sunfish: the fish builder's body made a tall disc cut short behind (its clavus a low frill), with a tall dorsal
		/// fin and a matching anal fin beneath, both far back, which the shader (mode 7) sculls side to side against each other.
		/// </summary>
		private static void Mola(Builder b, Color back, Color belly)
		{
			Fish(b, back, belly, height: 0.85f, width: 0.14f, fork: 0f, dorsal: 0.6f, dorsalFrom: 0.55f, dorsalTo: 0.7f, pointed: false, flukes: false, tail: 0.4f);
			Color fin = Color.Lerp(back, belly, 0.3f);
			int a0 = b.Add(new Vector3(0f, -0.27f, -0.05f), belly, new Vector2(0.55f, 0f), Vector2.zero);
			int a1 = b.Add(new Vector3(0f, -0.25f, -0.2f), belly, new Vector2(0.7f, 0f), Vector2.zero);
			int a2 = b.Add(new Vector3(0f, -0.8f, -0.17f), fin, new Vector2(0.67f, 0f), Vector2.zero);
			b.Tri(a0, a1, a2);
		}

		/// <summary>
		/// A chambered nautilus: a coiled shell, cream barred with flame-brown over its top, its hood and ring of fine tentacles out in
		/// front (appendage 1, which the shader's mode 8 trails behind each jet), a dark eye on either side.
		/// </summary>
		private static void Nautilus(Builder b, Color back, Color belly)
		{
			var cream = new Color(0.86f, 0.78f, 0.64f);
			Ellipsoid(b, new Vector3(0f, 0.05f, -0.05f), new Vector3(0.17f, 0.4f, 0.42f), 0.4f, 10, 14,
				(lat, lon) => lat > -0.2f && Mathf.Sin(lon * 7f + lat * 3f) > 0.25f ? back : Color.Lerp(cream, belly, 0.3f), Vector2.zero);
			for (int i = 0; i < 18; i++)
			{
				float a = i / 18f * Mathf.PI * 2f;
				var root = new Vector3(Mathf.Cos(a) * 0.07f, -0.08f + Mathf.Sin(a) * 0.06f, 0.33f);
				var tip = root + new Vector3(Mathf.Cos(a) * 0.05f, -0.06f + Mathf.Sin(a) * 0.03f, 0.27f);
				Limb(b, new[] { root, Vector3.Lerp(root, tip, 0.5f) + Vector3.down * 0.02f, tip }, 0.012f, Color.Lerp(belly, cream, 0.5f), 1);
			}
			for (int s = -1; s <= 1; s += 2)
			{
				Ellipsoid(b, new Vector3(s * 0.15f, -0.02f, 0.3f), new Vector3(0.025f, 0.025f, 0.025f), 0.025f, 3, 6, (lat, lon) => new Color(0.03f, 0.03f, 0.03f), Vector2.zero);
			}
		}

		/// <summary>A sawfish's rostrum: a long flat blade from the nose, toothed along both edges.</summary>
		private static void Saw(Builder b, Color colour)
		{
			const float from = 0.47f, to = 0.8f;
			int r0 = b.Add(new Vector3(-0.028f, 0f, from), colour, new Vector2(0.5f - from, 0.3f), Vector2.zero);
			int r1 = b.Add(new Vector3(0.028f, 0f, from), colour, new Vector2(0.5f - from, 0.3f), Vector2.zero);
			int t0 = b.Add(new Vector3(-0.018f, 0f, to), colour, new Vector2(0.5f - to, 0.2f), Vector2.zero);
			int t1 = b.Add(new Vector3(0.018f, 0f, to), colour, new Vector2(0.5f - to, 0.2f), Vector2.zero);
			b.Quad(r0, t0, t1, r1);
			Color tooth = Color.Lerp(colour, Color.white, 0.5f);
			for (int i = 0; i < 10; i++)
			{
				float z = Mathf.Lerp(from + 0.02f, to - 0.01f, i / 9f);
				float half = Mathf.Lerp(0.028f, 0.018f, (z - from) / (to - from));
				for (int s = -1; s <= 1; s += 2)
				{
					int p0 = b.Add(new Vector3(s * half, 0f, z - 0.008f), tooth, new Vector2(0.5f - z, 0.3f), Vector2.zero);
					int p1 = b.Add(new Vector3(s * half, 0f, z + 0.008f), tooth, new Vector2(0.5f - z, 0.3f), Vector2.zero);
					int p2 = b.Add(new Vector3(s * (half + 0.016f), 0f, z), tooth, new Vector2(0.5f - z, 0.3f), Vector2.zero);
					b.Tri(p0, p1, p2);
				}
			}
		}

		// ── Eels ──────────────────────────────────────────────────────

		/// <summary>
		/// An eel (2026-10-25): a long round body lofted from rings, blunt-headed, tapering to a pointed tail, with one ribbon fin
		/// running from behind the head over the back, round the tail and forward under the belly; dark eyes on the head. Every
		/// part rides the body wave (FishSeaLife.hlsl mode 6 ripples the whole length).
		/// </summary>
		private static void Eel(Builder b, Color back, Color belly)
		{
			const int stations = 22;
			const int sides = 8;
			const float girth = 0.075f;
			float Fullness(float a)
			{
				float head = Mathf.Sqrt(Mathf.Clamp01(a / 0.06f));
				float taper = 1f - 0.9f * Mathf.Pow(Mathf.Clamp01((a - 0.35f) / 0.65f), 1.4f);
				return Mathf.Max(0.03f, head * taper);
			}
			float Z(float a) => 0.5f - a;
			Color Shade(float up) => Color.Lerp(belly, back, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(up * 0.9f + 0.6f)));

			int first = b.Count;
			for (int i = 0; i <= stations; i++)
			{
				float a = i / (float)stations;
				float f = Fullness(a);
				for (int k = 0; k <= sides; k++)
				{
					float t = k * Mathf.PI * 2f / sides;
					float cos = Mathf.Cos(t), sin = Mathf.Sin(t);
					var p = new Vector3(cos * girth * 0.45f * f, sin * girth * 0.55f * f, Z(a));
					Color c = Shade(sin);
					if (i == 1 && Mathf.Abs(cos) > 0.7f && sin > 0.1f)
					{
						c = new Color(0.02f, 0.02f, 0.025f);
					}
					b.Add(p, c, new Vector2(a, Mathf.Abs(cos)), Vector2.zero);
				}
			}
			for (int i = 0; i < stations; i++)
			{
				for (int k = 0; k < sides; k++)
				{
					int a0 = first + i * (sides + 1) + k;
					int a1 = a0 + sides + 1;
					b.Quad(a0, a0 + 1, a1 + 1, a1);
				}
			}
			// The snout's cap.
			int nose = b.Add(new Vector3(0f, 0f, Z(0f) + 0.004f), Shade(0.2f), new Vector2(0f, 0f), Vector2.zero);
			for (int k = 0; k < sides; k++)
			{
				b.Tri(nose, first + k + 1, first + k);
			}

			// The ribbon fin: a strip over the back from a = 0.18 and under the belly from a = 0.5, meeting at the tail.
			Color fin = Color.Lerp(back, belly, 0.2f);
			void Ribbon(float from, float sign, float height)
			{
				const int steps = 12;
				int previousRoot = -1, previousTip = -1;
				for (int s = 0; s <= steps; s++)
				{
					float a = Mathf.Lerp(from, 1f, s / (float)steps);
					float edge = girth * 0.55f * Fullness(a) * sign;
					float rise = height * Mathf.Sin(Mathf.Clamp01((a - from) / (1f - from)) * Mathf.PI * 0.5f + 0.35f) * sign;
					int root = b.Add(new Vector3(0f, edge, Z(a)), fin, new Vector2(a, 0f), Vector2.zero);
					int tip = b.Add(new Vector3(0f, edge + rise, Z(a)), fin, new Vector2(a, 1f), Vector2.zero);
					if (previousRoot >= 0)
					{
						b.Quad(previousRoot, previousTip, tip, root);
					}
					previousRoot = root;
					previousTip = tip;
				}
			}
			Ribbon(0.18f, 1f, girth * 0.45f);
			Ribbon(0.5f, -1f, girth * 0.35f);
		}

		// ── Fish, sharks, whales ──────────────────────────────────────

		/// <summary>
		/// A fish's body lofted from rings along its length (round-nosed or pointed), narrowing to the tail's
		/// stalk; a forked tail (a whale's flukes lie flat), a dorsal fin, pectoral fins, an anal fin and an
		/// eye. Countershaded: the back's colour over the belly's.
		/// </summary>
		private static void Fish(Builder b, Color back, Color belly, float height, float width, float fork, float dorsal, float dorsalFrom, float dorsalTo,
			bool pointed, bool flukes, float tail)
		{
			const int stations = 14;
			const int sides = 10;
			const float stalk = 0.87f;
			float Fullness(float a)
			{
				float nose = pointed ? Mathf.Pow(Mathf.Clamp01(a / 0.22f), 0.85f) : Mathf.Sqrt(Mathf.Clamp01(a / 0.12f));
				float taper = 1f - 0.85f * Mathf.Pow(Mathf.Clamp01((a - 0.25f) / (stalk - 0.25f)), 1.6f);
				return Mathf.Max(0.02f, nose * taper);
			}
			float Z(float a) => 0.5f - a;
			Color Shade(float up) => Color.Lerp(belly, back, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(up * 0.9f + 0.55f)));

			// The body.
			int first = b.Count;
			for (int i = 0; i <= stations; i++)
			{
				float a = stalk * i / stations;
				float f = Fullness(a);
				for (int k = 0; k <= sides; k++)
				{
					float t = k * Mathf.PI * 2f / sides;
					float cos = Mathf.Cos(t), sin = Mathf.Sin(t);
					// A whale's belly is flatter than its back; a shark's and a fish's are even.
					float yScale = flukes && sin < 0f ? 0.8f : 1f;
					var p = new Vector3(cos * width * 0.5f * f, sin * height * 0.5f * f * yScale, Z(a));
					Color c = Shade(sin);
					// The eye: a dark spot on each flank just behind the nose.
					if (i == 1 && Mathf.Abs(cos) > 0.75f && sin > 0.05f && !flukes)
					{
						c = new Color(0.02f, 0.02f, 0.025f);
					}
					b.Add(p, c, new Vector2(a, Mathf.Abs(cos)), Vector2.zero);
				}
			}
			for (int i = 0; i < stations; i++)
			{
				for (int k = 0; k < sides; k++)
				{
					int a0 = first + i * (sides + 1) + k;
					int a1 = a0 + sides + 1;
					b.Quad(a0, a0 + 1, a1 + 1, a1);
				}
			}
			// Cap the stalk.
			int capCentre = b.Add(new Vector3(0f, 0f, Z(stalk)), Shade(0f), new Vector2(stalk, 0f), Vector2.zero);
			int lastRing = first + stations * (sides + 1);
			for (int k = 0; k < sides; k++)
			{
				b.Tri(capCentre, lastRing + k, lastRing + k + 1);
			}

			// The tail: two lobes from the stalk to their tips, the fork between them.
			float stalkHalf = Mathf.Max(width, height) * 0.5f * Fullness(stalk);
			float span = Mathf.Max(height, width) * 0.55f * tail;
			Color fin = Color.Lerp(back, belly, 0.25f);
			Vector3 Lobe(float across, float along) => flukes ? new Vector3(across, 0f, along) : new Vector3(0f, across, along);
			int rootA = b.Add(Lobe(stalkHalf, Z(stalk)), fin, new Vector2(stalk, 0f), Vector2.zero);
			int rootB = b.Add(Lobe(-stalkHalf, Z(stalk)), fin, new Vector2(stalk, 0f), Vector2.zero);
			// A shark's upper lobe is the longer.
			int tipA = b.Add(Lobe(span * (pointed ? 1.1f : 1f), Z(1f) - (pointed ? 0.05f : 0f)), fin, new Vector2(1f, 1f), Vector2.zero);
			int tipB = b.Add(Lobe(-span * (pointed ? 0.55f : 1f), Z(1f) + (pointed ? 0.04f : 0f)), fin, new Vector2(1f, 1f), Vector2.zero);
			int notch = b.Add(Lobe(0f, Z(stalk + (1f - stalk) * Mathf.Lerp(1f, 0.25f, fork))), fin, new Vector2(1f, 0f), Vector2.zero);
			b.Tri(rootA, tipA, notch);
			b.Tri(rootB, notch, tipB);
			b.Tri(rootA, notch, rootB);

			// The dorsal fin, swept back; a long low one for a reef fish.
			{
				float midA = Mathf.Lerp(dorsalFrom, dorsalTo, 0.75f);
				int d0 = b.Add(new Vector3(0f, height * 0.5f * Fullness(dorsalFrom) * 0.95f, Z(dorsalFrom)), back, new Vector2(dorsalFrom, 0f), Vector2.zero);
				int d1 = b.Add(new Vector3(0f, height * 0.5f * Fullness(dorsalTo) * 0.95f, Z(dorsalTo)), back, new Vector2(dorsalTo, 0f), Vector2.zero);
				int d2 = b.Add(new Vector3(0f, height * 0.5f * Fullness(midA) + dorsal * Mathf.Max(height, 0.2f), Z(midA + 0.08f)), back * 0.85f, new Vector2(midA + 0.08f, 0f), Vector2.zero);
				b.Tri(d0, d2, d1);
			}
			// The anal fin (not a whale's).
			if (!flukes)
			{
				float from = pointed ? 0.68f : 0.6f, to = 0.78f;
				int n0 = b.Add(new Vector3(0f, -height * 0.5f * Fullness(from) * 0.95f, Z(from)), belly, new Vector2(from, 0f), Vector2.zero);
				int n1 = b.Add(new Vector3(0f, -height * 0.5f * Fullness(to) * 0.95f, Z(to)), belly, new Vector2(to, 0f), Vector2.zero);
				int n2 = b.Add(new Vector3(0f, -height * 0.5f * Fullness(from) - height * 0.25f, Z(to + 0.03f)), fin, new Vector2(to, 0f), Vector2.zero);
				b.Tri(n0, n2, n1);
			}
			// Pectoral fins, a pair; a whale's are long.
			for (int s = -1; s <= 1; s += 2)
			{
				float at = flukes ? 0.25f : 0.22f;
				float reach = flukes ? 0.3f : pointed ? 0.2f : 0.1f;
				float root = width * 0.5f * Fullness(at) * 0.9f;
				int p0 = b.Add(new Vector3(s * root, -height * 0.15f, Z(at)), fin, new Vector2(at, 0.5f), Vector2.zero);
				int p1 = b.Add(new Vector3(s * root, -height * 0.15f, Z(at + 0.08f)), fin, new Vector2(at + 0.08f, 0.5f), Vector2.zero);
				int p2 = b.Add(new Vector3(s * (root + reach), -height * 0.35f, Z(at + 0.14f)), fin * 0.9f, new Vector2(at + 0.14f, 1f), Vector2.zero);
				b.Tri(p0, p1, p2);
			}
		}

		// ── Rays ──────────────────────────────────────────────────────

		/// <summary>
		/// A manta: a diamond of wing one unit across, thick at the body and thin at the tips, dark above and pale
		/// beneath with paler shoulders, two cephalic fins at the front and a thin whip of a tail.
		/// </summary>
		private static void Ray(Builder b, Color back, Color belly)
		{
			const int columns = 16, rows = 6;
			float Front(float s) => 0.22f - 0.26f * Mathf.Pow(s, 1.4f);
			float Back(float s) => -0.3f + 0.24f * Mathf.Pow(s, 0.7f);
			for (int face = 0; face < 2; face++)
			{
				bool top = face == 0;
				int first = b.Count;
				for (int i = 0; i <= columns; i++)
				{
					float x = -0.5f + (float)i / columns;
					float s = Mathf.Abs(x) * 2f;
					for (int j = 0; j <= rows; j++)
					{
						float c = (float)j / rows;
						float z = Mathf.Lerp(Front(s), Back(s), c);
						float thick = 0.07f * Mathf.Pow(1f - s, 1.5f) * Mathf.Sin(Mathf.PI * c);
						var p = new Vector3(x, top ? thick : -thick * 0.35f, z);
						// The shoulders pale on a manta's back.
						float shoulder = top ? Mathf.Clamp01(1f - Mathf.Abs(s - 0.35f) / 0.2f) * Mathf.Clamp01(1f - Mathf.Abs(c - 0.25f) / 0.25f) : 0f;
						Color colour = top ? Color.Lerp(back, Color.Lerp(back, belly, 0.6f), shoulder * 0.7f) : belly;
						b.Add(p, colour, new Vector2((0.22f - z) / 0.52f, s), Vector2.zero);
					}
				}
				for (int i = 0; i < columns; i++)
				{
					for (int j = 0; j < rows; j++)
					{
						int a0 = first + i * (rows + 1) + j;
						int a1 = a0 + rows + 1;
						b.Quad(a0, a0 + 1, a1 + 1, a1);
					}
				}
			}
			// Cephalic fins: two lobes curling forward and down from the mouth.
			for (int s = -1; s <= 1; s += 2)
			{
				int c0 = b.Add(new Vector3(s * 0.05f, 0f, 0.21f), back, new Vector2(0f, 0.1f), Vector2.zero);
				int c1 = b.Add(new Vector3(s * 0.1f, 0f, 0.19f), back, new Vector2(0f, 0.2f), Vector2.zero);
				int c2 = b.Add(new Vector3(s * 0.08f, -0.04f, 0.3f), back * 0.8f, new Vector2(0f, 0.15f), Vector2.zero);
				b.Tri(c0, c1, c2);
			}
			// The tail: two crossed thin strips trailing behind.
			for (int k = 0; k < 2; k++)
			{
				Vector3 across = k == 0 ? new Vector3(0.008f, 0f, 0f) : new Vector3(0f, 0.008f, 0f);
				int t0 = b.Add(new Vector3(0f, 0f, -0.28f) - across, back, new Vector2(1f, 0f), new Vector2(1f, 0f));
				int t1 = b.Add(new Vector3(0f, 0f, -0.28f) + across, back, new Vector2(1f, 0f), new Vector2(1f, 0f));
				int t2 = b.Add(new Vector3(0f, 0f, -0.75f), back, new Vector2(1.5f, 0f), new Vector2(1f, 1f));
				b.Tri(t0, t1, t2);
			}
		}

		// ── Turtles ───────────────────────────────────────────────────

		/// <summary>
		/// A sea turtle: a domed shell over a flat plastron, its scutes in a mottle of light and dark, a blunt
		/// head, long front flippers that beat like wings and two small rear paddles.
		/// </summary>
		private static void Turtle(Builder b, Color back, Color belly)
		{
			// Shell and plastron, as one squashed ellipsoid: domed above, shallow below.
			Ellipsoid(b, new Vector3(0f, 0f, -0.02f), new Vector3(0.3f, 0.15f, 0.4f), 0.055f, 9, 16,
				(lat, lon) =>
				{
					if (lat < 0f)
					{
						return belly;
					}
					// The scutes: a hexagonal-ish mottle from the angle and the height.
					float plate = Mathf.Abs(Mathf.Sin(lon * 5f) * Mathf.Sin(lat * 7f + 0.5f));
					return back * (0.75f + 0.4f * plate);
				}, Vector2.zero);
			// The head.
			Ellipsoid(b, new Vector3(0f, 0.01f, 0.43f), new Vector3(0.07f, 0.055f, 0.085f), 0.055f, 5, 8, (lat, lon) => Color.Lerp(belly, back, 0.55f), Vector2.zero);
			Color skin = Color.Lerp(belly, back, 0.4f);
			// Flippers: front long, rear short; each a curved paddle.
			for (int s = -1; s <= 1; s += 2)
			{
				Paddle(b, new Vector3(s * 0.22f, 0f, 0.2f), new Vector3(s * 0.62f, -0.02f, -0.02f), new Vector3(s * 0.45f, 0f, 0.18f), 0.11f, 0.04f, skin, s < 0 ? 1f : 2f);
				Paddle(b, new Vector3(s * 0.16f, 0f, -0.3f), new Vector3(s * 0.3f, -0.01f, -0.46f), new Vector3(s * 0.26f, 0f, -0.36f), 0.08f, 0.04f, skin, s < 0 ? 3f : 4f);
			}
		}

		/// <summary>A flat paddle from a root to a tip along a quadratic curve, tapering, tagged as an appendage.</summary>
		private static void Paddle(Builder b, Vector3 root, Vector3 tip, Vector3 control, float rootWidth, float tipWidth, Color colour, float appendage)
		{
			const int stations = 5;
			int first = b.Count;
			for (int i = 0; i <= stations; i++)
			{
				float t = (float)i / stations;
				Vector3 p = (1f - t) * (1f - t) * root + 2f * t * (1f - t) * control + t * t * tip;
				Vector3 d = 2f * (1f - t) * (control - root) + 2f * t * (tip - control);
				Vector3 side = Vector3.Cross(d.normalized, Vector3.up).normalized;
				float w = Mathf.Lerp(rootWidth, tipWidth, t) * 0.5f * (1f + 0.3f * Mathf.Sin(Mathf.PI * t));
				var uv1 = new Vector2(appendage, t);
				float along = 0.5f - p.z;
				b.Add(p - side * w, colour * (0.9f + 0.1f * t), new Vector2(along, 0f), uv1);
				b.Add(p + side * w, colour * (0.9f + 0.1f * t), new Vector2(along, 0f), uv1);
			}
			for (int i = 0; i < stations; i++)
			{
				int a = first + i * 2;
				b.Quad(a, a + 1, a + 3, a + 2);
			}
		}

		// ── Jellies ───────────────────────────────────────────────────

		/// <summary>
		/// A jellyfish: a bell one unit across, crown up, clearer at the crown and denser at the rim, trailing
		/// a fringe of long fine tentacles and four ruffled oral arms from its middle.
		/// </summary>
		private static void Jelly(Builder b, Color back, Color belly)
		{
			const int rings = 7, sides = 16;
			const float bell = 1.75f; // radians from the crown to the rim: a little past a hemisphere
			int first = b.Count;
			for (int i = 0; i <= rings; i++)
			{
				float phi = bell * i / rings;
				float r = 0.5f * Mathf.Sin(Mathf.Min(phi, Mathf.PI * 0.5f)) * (phi > Mathf.PI * 0.5f ? 1f - 0.15f * (phi - Mathf.PI * 0.5f) : 1f);
				float y = 0.32f * Mathf.Cos(phi);
				for (int k = 0; k <= sides; k++)
				{
					float t = k * Mathf.PI * 2f / sides;
					// Scalloped at the rim.
					float scallop = 1f + (i == rings ? 0.04f * Mathf.Cos(t * 8f) : 0f);
					var colour = new Color(back.r, back.g, back.b, Mathf.Lerp(0.35f, 0.75f, (float)i / rings));
					b.Add(new Vector3(Mathf.Cos(t) * r * scallop, y, Mathf.Sin(t) * r * scallop), colour, new Vector2((float)i / rings, (float)k / sides), Vector2.zero);
				}
			}
			for (int i = 0; i < rings; i++)
			{
				for (int k = 0; k < sides; k++)
				{
					int a0 = first + i * (sides + 1) + k;
					int a1 = a0 + sides + 1;
					b.Quad(a0, a0 + 1, a1 + 1, a1);
				}
			}
			float rimY = 0.32f * Mathf.Cos(bell);
			float rimR = 0.5f * (1f - 0.15f * (bell - Mathf.PI * 0.5f));
			// The tentacles: fine ribbons hanging from the rim.
			const int tentacles = 12;
			for (int n = 0; n < tentacles; n++)
			{
				float t = (n + 0.5f) * Mathf.PI * 2f / tentacles;
				var radial = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
				var tangent = new Vector3(-radial.z, 0f, radial.x);
				Ribbon(b, radial * rimR * 0.95f + Vector3.up * rimY, 1.6f + 0.4f * Mathf.Sin(n * 2.3f), 0.018f, 0.004f, tangent,
					new Color(belly.r, belly.g, belly.b, 0.5f), (float)n / tentacles);
			}
			// The oral arms: four broad, ruffled ribbons from the middle.
			for (int n = 0; n < 4; n++)
			{
				float t = (n + 0.25f) * Mathf.PI * 0.5f;
				var radial = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
				Ribbon(b, radial * 0.05f + Vector3.up * (rimY + 0.05f), 0.8f, 0.09f, 0.03f, radial, new Color(back.r * 1.1f, back.g, back.b * 1.05f, 0.7f), (float)n / 4f, ruffle: 0.04f);
			}
		}

		/// <summary>A ribbon hanging straight down from a point (the shader sways it), tagged as appendage 1.</summary>
		private static void Ribbon(Builder b, Vector3 top, float length, float rootWidth, float tipWidth, Vector3 across, Color colour, float angle01, float ruffle = 0f)
		{
			const int stations = 8;
			int first = b.Count;
			for (int i = 0; i <= stations; i++)
			{
				float t = (float)i / stations;
				float w = Mathf.Lerp(rootWidth, tipWidth, t) * 0.5f;
				Vector3 wave = across * (ruffle * Mathf.Sin(t * 14f));
				Vector3 p = top + Vector3.down * (length * t) + wave;
				var uv1 = new Vector2(1f, t);
				var c = new Color(colour.r, colour.g, colour.b, colour.a * (1f - 0.6f * t));
				b.Add(p - across * w, c, new Vector2(1f, angle01), uv1);
				b.Add(p + across * w, c, new Vector2(1f, angle01), uv1);
			}
			for (int i = 0; i < stations; i++)
			{
				int a = first + i * 2;
				b.Quad(a, a + 1, a + 3, a + 2);
			}
		}

		// ── Crabs ─────────────────────────────────────────────────────

		/// <summary>A crab: a broad shell on eight jointed legs, a pair of claws held forward.</summary>
		private static void Crab(Builder b, Color back, Color belly)
		{
			Ellipsoid(b, new Vector3(0f, 0.14f, 0f), new Vector3(0.27f, 0.09f, 0.2f), 0.045f, 6, 12,
				(lat, lon) => lat < 0f ? belly : back * (0.9f + 0.15f * Mathf.Cos(lon * 3f)), Vector2.zero);
			Color leg = Color.Lerp(back, belly, 0.35f);
			float[] rows = { 0.1f, 0.03f, -0.05f, -0.12f };
			int index = 1;
			for (int s = -1; s <= 1; s += 2)
			{
				foreach (float z in rows)
				{
					var path = new[]
					{
						new Vector3(s * 0.22f, 0.13f, z),
						new Vector3(s * 0.4f, 0.22f, z * 1.15f),
						new Vector3(s * 0.52f, 0.01f, z * 1.35f),
					};
					Limb(b, path, 0.018f, leg, index);
					index++;
				}
				// The claw: an arm forward, and the pincer.
				var arm = new[]
				{
					new Vector3(s * 0.16f, 0.12f, 0.15f),
					new Vector3(s * 0.24f, 0.13f, 0.27f),
				};
				Limb(b, arm, 0.025f, leg, 9);
				Ellipsoid(b, new Vector3(s * 0.25f, 0.12f, 0.35f), new Vector3(0.06f, 0.045f, 0.09f), 0.045f, 4, 8, (lat, lon) => back * 1.05f, new Vector2(9f, 1f));
			}
		}

		/// <summary>A jointed limb: a thin three-sided tube along a path, tagged with its appendage and reach.</summary>
		private static void Limb(Builder b, Vector3[] path, float radius, Color colour, int appendage)
		{
			const int sides = 3;
			int first = b.Count;
			for (int i = 0; i < path.Length; i++)
			{
				float t = (float)i / (path.Length - 1);
				Vector3 along = i < path.Length - 1 ? path[i + 1] - path[i] : path[i] - path[i - 1];
				Vector3 n1 = Vector3.Cross(along.normalized, Vector3.up).normalized;
				if (n1.sqrMagnitude < 1e-6f)
				{
					n1 = Vector3.right;
				}
				Vector3 n2 = Vector3.Cross(along.normalized, n1).normalized;
				float r = radius * (1f - 0.5f * t);
				for (int k = 0; k < sides; k++)
				{
					float a = k * Mathf.PI * 2f / sides;
					b.Add(path[i] + (n1 * Mathf.Cos(a) + n2 * Mathf.Sin(a)) * r, colour, new Vector2(0.5f - path[i].z, Mathf.Abs(path[i].x) * 2f), new Vector2(appendage, t));
				}
			}
			for (int i = 0; i < path.Length - 1; i++)
			{
				for (int k = 0; k < sides; k++)
				{
					int a0 = first + i * sides + k;
					int a1 = first + i * sides + (k + 1) % sides;
					b.Quad(a0, a1, a1 + sides, a0 + sides);
				}
			}
		}

		// ── Shared ────────────────────────────────────────────────────

		/// <summary>
		/// An ellipsoid with its own radius below the equator (<paramref name="lowerY"/>): a shell domed above and
		/// flat beneath. Colour from (latitude -π/2 .. π/2, longitude 0 .. 2π).
		/// </summary>
		private static void Ellipsoid(Builder b, Vector3 centre, Vector3 radii, float lowerY, int rings, int sides, System.Func<float, float, Color> colour, Vector2 uv1)
		{
			int first = b.Count;
			for (int i = 0; i <= rings; i++)
			{
				float lat = -Mathf.PI * 0.5f + Mathf.PI * i / rings;
				float y = Mathf.Sin(lat) * (lat < 0f ? lowerY : radii.y);
				float r = Mathf.Cos(lat);
				for (int k = 0; k <= sides; k++)
				{
					float lon = k * Mathf.PI * 2f / sides;
					var p = centre + new Vector3(Mathf.Sin(lon) * r * radii.x, y, Mathf.Cos(lon) * r * radii.z);
					b.Add(p, colour(lat, lon), new Vector2(Mathf.Clamp01(0.5f - p.z), Mathf.Abs(p.x) * 2f), uv1);
				}
			}
			for (int i = 0; i < rings; i++)
			{
				for (int k = 0; k < sides; k++)
				{
					int a0 = first + i * (sides + 1) + k;
					int a1 = a0 + sides + 1;
					b.Quad(a0, a0 + 1, a1 + 1, a1);
				}
			}
		}

		private sealed class Builder
		{
			private readonly List<Vector3> positions = new List<Vector3>();
			private readonly List<Color> colours = new List<Color>();
			private readonly List<Vector2> uv0 = new List<Vector2>();
			private readonly List<Vector2> uv1 = new List<Vector2>();
			private readonly List<int> triangles = new List<int>();

			public int Count => positions.Count;

			public int Add(Vector3 position, Color colour, Vector2 body, Vector2 part)
			{
				positions.Add(position);
				colours.Add(colour);
				uv0.Add(body);
				uv1.Add(part);
				return positions.Count - 1;
			}

			public void Tri(int a, int b, int c)
			{
				triangles.Add(a);
				triangles.Add(b);
				triangles.Add(c);
			}

			public void Quad(int a, int b, int c, int d)
			{
				Tri(a, b, c);
				Tri(a, c, d);
			}

			public Mesh ToMesh(string name)
			{
				var mesh = new Mesh { name = "Sea life: " + name, hideFlags = HideFlags.DontSave };
				mesh.SetVertices(positions);
				mesh.SetColors(colours);
				mesh.SetUVs(0, uv0);
				mesh.SetUVs(1, uv1);
				mesh.SetTriangles(triangles, 0);
				mesh.RecalculateNormals();
				mesh.RecalculateBounds();
				return mesh;
			}
		}
	}
}
