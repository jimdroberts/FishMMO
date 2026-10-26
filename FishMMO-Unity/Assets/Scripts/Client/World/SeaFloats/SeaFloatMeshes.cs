using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// The sea floats' one mesh (2026-10-25, phase I2p): a submesh per <see cref="BiomeSeaFloatKind"/>, in the enum's order, built
	/// once from fixed seeds (the same on every machine). Each kind is in metres of a one-metre unit (the instance's scale makes it
	/// its size), y up from the waterline: what floats low lies mostly under it. Vertex colour = albedo (sRGB), alpha 1.
	/// </summary>
	public static class SeaFloatMeshes
	{
		private sealed class Builder
		{
			public readonly List<Vector3> Positions = new List<Vector3>();
			public readonly List<Vector3> Normals = new List<Vector3>();
			public readonly List<Color32> Colours = new List<Color32>();
			public readonly List<int>[] Indices = new List<int>[BiomeSeaFloats.KindCount];
			public int Sub;
			private uint seed = 0x5EAF1047u;

			public Builder()
			{
				for (int i = 0; i < Indices.Length; i++)
				{
					Indices[i] = new List<int>();
				}
			}

			public float Next()
			{
				seed = BiomeSeaFloats.Mix(seed + 0x9E3779B9u);
				return BiomeSeaFloats.Unit(seed);
			}

			public float Range(float a, float b) => Mathf.Lerp(a, b, Next());

			private int Vertex(Vector3 p, Vector3 n, Color32 c)
			{
				Positions.Add(p);
				Normals.Add(n);
				Colours.Add(c);
				return Positions.Count - 1;
			}

			/// <summary>A quad (both faces drawn: the shader culls nothing).</summary>
			public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 colour)
			{
				Vector3 n = Vector3.Cross(b - a, d - a).normalized;
				if (n.y < 0f)
				{
					n = -n;
				}
				int i0 = Vertex(a, n, colour), i1 = Vertex(b, n, colour), i2 = Vertex(c, n, colour), i3 = Vertex(d, n, colour);
				List<int> t = Indices[Sub];
				t.Add(i0); t.Add(i2); t.Add(i1);
				t.Add(i0); t.Add(i3); t.Add(i2);
			}

			public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color32 colour)
			{
				Vector3 n = Vector3.Cross(b - a, c - a).normalized;
				int i0 = Vertex(a, n, colour), i1 = Vertex(b, n, colour), i2 = Vertex(c, n, colour);
				List<int> t = Indices[Sub];
				t.Add(i0); t.Add(i1); t.Add(i2);
			}

			/// <summary>A box turned about the vertical.</summary>
			public void Box(Vector3 centre, Vector3 size, float yaw, Color32 colour)
			{
				Quaternion r = Quaternion.Euler(0f, yaw, 0f);
				Vector3 h = size * 0.5f;
				Vector3 P(float x, float y, float z) => centre + r * new Vector3(x * h.x, y * h.y, z * h.z);
				Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), colour);
				Quad(P(-1, -1, -1), P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), colour);
				Quad(P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), P(1, -1, 1), colour);
				Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), colour);
				Quad(P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), colour);
				Quad(P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), colour);
			}

			/// <summary>A cylinder along a horizontal direction (its yaw), smooth-sided, capped.</summary>
			public void Cylinder(Vector3 centre, float length, float radius, int sides, float yaw, Color32 side, Color32 cap)
			{
				Quaternion r = Quaternion.Euler(0f, yaw, 0f);
				Vector3 axis = r * Vector3.right;
				List<int> t = Indices[Sub];
				int first = Positions.Count;
				for (int s = 0; s <= sides; s++)
				{
					float a = s / (float)sides * Mathf.PI * 2f;
					Vector3 radial = r * new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
					Vertex(centre - axis * (length * 0.5f) + radial * radius, radial, side);
					Vertex(centre + axis * (length * 0.5f) + radial * radius, radial, side);
				}
				for (int s = 0; s < sides; s++)
				{
					int a = first + s * 2;
					t.Add(a); t.Add(a + 2); t.Add(a + 1);
					t.Add(a + 1); t.Add(a + 2); t.Add(a + 3);
				}
				for (int end = -1; end <= 1; end += 2)
				{
					Vector3 middle = centre + axis * (length * 0.5f * end);
					int m = Vertex(middle, axis * end, cap);
					int ring = Positions.Count;
					for (int s = 0; s <= sides; s++)
					{
						float a = s / (float)sides * Mathf.PI * 2f;
						Vertex(middle + r * new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a)) * radius, axis * end, cap);
					}
					for (int s = 0; s < sides; s++)
					{
						t.Add(m); t.Add(ring + s); t.Add(ring + s + 1);
					}
				}
			}

			/// <summary>A flat disc (an ellipse with <paramref name="aspect"/>) with a thickness: top, rim and bottom.</summary>
			public void Disc(Vector3 centre, float radius, float aspect, float top, float bottom, int sides, Color32 upper, Color32 rim, float rimLift = 0f)
			{
				List<int> t = Indices[Sub];
				int m = Vertex(centre + Vector3.up * top, Vector3.up, upper);
				int ring = Positions.Count;
				for (int s = 0; s <= sides; s++)
				{
					float a = s / (float)sides * Mathf.PI * 2f;
					float wobble = 1f + 0.08f * Mathf.Sin(a * 3f + radius * 7f);
					Vertex(centre + new Vector3(Mathf.Cos(a) * radius * wobble, top + rimLift, Mathf.Sin(a) * radius * aspect * wobble), Vector3.up, rimLift > 0f ? rim : upper);
				}
				for (int s = 0; s < sides; s++)
				{
					t.Add(m); t.Add(ring + s + 1); t.Add(ring + s);
				}
				int sideRing = Positions.Count;
				for (int s = 0; s <= sides; s++)
				{
					float a = s / (float)sides * Mathf.PI * 2f;
					float wobble = 1f + 0.08f * Mathf.Sin(a * 3f + radius * 7f);
					var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * aspect).normalized;
					Vector3 edge = centre + new Vector3(Mathf.Cos(a) * radius * wobble, 0f, Mathf.Sin(a) * radius * aspect * wobble);
					Vertex(edge + Vector3.up * (top + rimLift), outward, rim);
					Vertex(edge + Vector3.up * bottom, outward, rim);
				}
				for (int s = 0; s < sides; s++)
				{
					int a = sideRing + s * 2;
					t.Add(a); t.Add(a + 1); t.Add(a + 2);
					t.Add(a + 1); t.Add(a + 3); t.Add(a + 2);
				}
			}

			/// <summary>A lumpy rounded body (an octahedron subdivided once, each point pushed out by its own amount), squashed in y.</summary>
			public void Lump(Vector3 centre, float radius, float above, float below, float roughness, Color32 top, Color32 under)
			{
				List<int> t = Indices[Sub];
				var corners = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
				int[,] faces = { { 2, 4, 0 }, { 2, 0, 5 }, { 2, 5, 1 }, { 2, 1, 4 }, { 3, 0, 4 }, { 3, 5, 0 }, { 3, 1, 5 }, { 3, 4, 1 } };
				var push = new Dictionary<Vector3, float>();
				float Push(Vector3 d)
				{
					if (!push.TryGetValue(d, out float p))
					{
						p = 1f + (Next() - 0.5f) * 2f * roughness;
						push[d] = p;
					}
					return p;
				}
				Vector3 Place(Vector3 d)
				{
					d.Normalize();
					float p = Push(d) * radius;
					return centre + new Vector3(d.x * p, d.y * (d.y >= 0f ? above : below) / Mathf.Max(1e-3f, radius) * p, d.z * p);
				}
				for (int f = 0; f < 8; f++)
				{
					Vector3 a = corners[faces[f, 0]], b = corners[faces[f, 1]], c = corners[faces[f, 2]];
					Vector3 ab = (a + b).normalized, bc = (b + c).normalized, ca = (c + a).normalized;
					foreach ((Vector3 p, Vector3 q, Vector3 r) in new[] { (a, ab, ca), (ab, b, bc), (ca, bc, c), (ab, bc, ca) })
					{
						Vector3 pp = Place(p), pq = Place(q), pr = Place(r);
						Color32 colour = (pp.y + pq.y + pr.y) / 3f > centre.y ? top : under;
						Triangle(pp, pq, pr, colour);
					}
				}
			}
		}

		private static Color32 Hex(int rgb) => new Color32((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF), 255);

		/// <summary>Builds the mesh: submesh i is <see cref="BiomeSeaFloatKind"/> i. The caller owns it.</summary>
		public static Mesh Build()
		{
			var b = new Builder();

			// Sargassum: a clump of golden-brown blades round a matted middle, studded with its little round floats.
			b.Sub = (int)BiomeSeaFloatKind.Sargassum;
			b.Disc(Vector3.zero, 0.26f, 0.8f, 0.015f, -0.03f, 9, Hex(0x8a6a22), Hex(0x6a5018));
			for (int i = 0; i < 12; i++)
			{
				float a = i / 12f * Mathf.PI * 2f + b.Range(-0.2f, 0.2f);
				float length = b.Range(0.25f, 0.5f), width = b.Range(0.04f, 0.07f), y = b.Range(0.005f, 0.035f);
				var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				var side = new Vector3(-dir.z, 0f, dir.x) * width;
				Vector3 root = dir * 0.12f + Vector3.up * y, tip = dir * (0.12f + length) + Vector3.up * (y - 0.02f);
				b.Quad(root - side, root + side, tip + side * 0.4f, tip - side * 0.4f, Hex(i % 3 == 0 ? 0xa88a32 : 0x9a7a2a));
			}
			for (int i = 0; i < 14; i++)
			{
				float a = b.Range(0f, Mathf.PI * 2f), r = b.Range(0.08f, 0.45f);
				b.Box(new Vector3(Mathf.Cos(a) * r, 0.03f, Mathf.Sin(a) * r), Vector3.one * b.Range(0.025f, 0.04f), b.Range(0f, 90f), Hex(0xb89a3a));
			}

			// By-the-wind sailor: a deep-blue oval float carrying a stiff clear sail set across it.
			b.Sub = (int)BiomeSeaFloatKind.Velella;
			b.Disc(Vector3.zero, 0.5f, 0.68f, 0.05f, -0.04f, 12, Hex(0x2a48b0), Hex(0x1c3488));
			b.Triangle(new Vector3(-0.4f, 0.05f, -0.1f), new Vector3(0.4f, 0.05f, 0.1f), new Vector3(0.12f, 0.5f, 0.03f), Hex(0xc8d4ec));

			// A drift log: bark brown, its ends paler where they broke, one end bearded with goose barnacles.
			b.Sub = (int)BiomeSeaFloatKind.GooseLog;
			b.Cylinder(new Vector3(0f, -0.025f, 0f), 1f, 0.07f, 9, 0f, Hex(0x5a4632), Hex(0x9a8462));
			b.Cylinder(new Vector3(-0.2f, 0.0f, 0.05f), 0.22f, 0.02f, 5, 35f, Hex(0x4a3828), Hex(0x8a7458));
			for (int i = 0; i < 18; i++)
			{
				float x = b.Range(0.32f, 0.5f), a = b.Range(-1.4f, 1.4f);
				var at = new Vector3(x, -0.025f + Mathf.Sin(a) * 0.075f, Mathf.Cos(a) * 0.075f);
				b.Box(at, new Vector3(0.022f, 0.035f, 0.022f), b.Range(0f, 90f), Hex(i % 4 == 0 ? 0x3a3430 : 0xe4dfd2));
			}

			// A propagule raft: green pencil-like seedlings lying every which way, browning at their tips.
			b.Sub = (int)BiomeSeaFloatKind.PropaguleRaft;
			for (int i = 0; i < 16; i++)
			{
				float a = b.Range(0f, Mathf.PI * 2f), r = Mathf.Sqrt(b.Next()) * 0.42f;
				b.Cylinder(new Vector3(Mathf.Cos(a) * r, 0.006f, Mathf.Sin(a) * r), b.Range(0.22f, 0.34f), 0.012f, 5, b.Range(0f, 180f),
					Hex(i % 3 == 0 ? 0x6a6a2a : 0x4a7a2a), Hex(0x6a4a2a));
			}

			// A lost corkline: its head rope sagging in an arc over thirty metres, a cork every metre and a half.
			b.Sub = (int)BiomeSeaFloatKind.Corkline;
			const int segments = 24;
			Vector3 RopeAt(float s) => new Vector3((s - 0.5f) * 30f, 0f, 2.2f * Mathf.Sin(s * Mathf.PI) + 0.6f * Mathf.Sin(s * Mathf.PI * 3f));
			for (int i = 0; i < segments; i++)
			{
				Vector3 a = RopeAt(i / (float)segments), c = RopeAt((i + 1) / (float)segments);
				Vector3 along = c - a;
				b.Box((a + c) * 0.5f, new Vector3(along.magnitude, 0.02f, 0.02f), -Mathf.Atan2(along.z, along.x) * Mathf.Rad2Deg, Hex(0x8a7a5a));
			}
			for (int i = 0; i <= 20; i++)
			{
				float s = i / 20f;
				Vector3 a = RopeAt(Mathf.Max(0f, s - 0.01f)), c = RopeAt(Mathf.Min(1f, s + 0.01f));
				b.Cylinder(RopeAt(s) + Vector3.up * 0.01f, 0.14f, 0.06f, 7, -Mathf.Atan2(c.z - a.z, c.x - a.x) * Mathf.Rad2Deg, Hex(0xa87a48), Hex(0xc49a64));
			}

			// A growler: a lump of old glacier ice, most of it under the water, blue where it is dense.
			b.Sub = (int)BiomeSeaFloatKind.Growler;
			b.Lump(Vector3.zero, 0.5f, 0.14f, 0.42f, 0.28f, Hex(0xdcecf4), Hex(0x8cc0d8));

			// A pumice raft: grey froth-stone pebbles packed together.
			b.Sub = (int)BiomeSeaFloatKind.PumiceRaft;
			for (int i = 0; i < 12; i++)
			{
				float a = b.Range(0f, Mathf.PI * 2f), r = Mathf.Sqrt(b.Next()) * 0.42f, size = b.Range(0.05f, 0.12f);
				b.Lump(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), size, size * 0.5f, size * 0.6f, 0.3f, Hex(i % 3 == 0 ? 0xa8a498 : 0xbcb8ac), Hex(0x8a8678));
			}

			// Pancake ice: a round plate of new ice, its rim raised where it jostles its neighbours.
			b.Sub = (int)BiomeSeaFloatKind.PancakeIce;
			b.Disc(Vector3.zero, 0.5f, 0.92f, 0.03f, -0.07f, 16, Hex(0xe2eaee), Hex(0xf2f6f8), 0.035f);

			// Brash ice: a few angular chunks of broken floe.
			b.Sub = (int)BiomeSeaFloatKind.BrashIce;
			for (int i = 0; i < 5; i++)
			{
				float a = b.Range(0f, Mathf.PI * 2f), r = b.Range(0f, 0.3f), size = b.Range(0.15f, 0.38f);
				b.Box(new Vector3(Mathf.Cos(a) * r, size * 0.5f * 0.2f - size * 0.35f, Mathf.Sin(a) * r), new Vector3(size, size * 0.6f, size * b.Range(0.6f, 1f)), b.Range(0f, 90f), Hex(i % 2 == 0 ? 0xdcecf2 : 0xc8e0ea));
			}

			// A kelp raft (2026-10-25, I2i-b; built last, so every kind above keeps its seeds): a matted tangle of bull-kelp, whip
			// stipes trailing out of it, their round bulbs riding high, blades streaming from the bulbs.
			b.Sub = (int)BiomeSeaFloatKind.KelpRaft;
			b.Disc(Vector3.zero, 0.3f, 0.7f, 0.02f, -0.05f, 10, Hex(0x5a4a1e), Hex(0x4a3c18));
			for (int i = 0; i < 9; i++)
			{
				float a = i / 9f * Mathf.PI * 2f + b.Range(-0.25f, 0.25f), length = b.Range(0.3f, 0.5f);
				var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				b.Cylinder(dir * (0.15f + 0.5f * length) + Vector3.up * 0.005f, length, 0.012f, 5, -a * Mathf.Rad2Deg, Hex(0x6a5424), Hex(0x5a4a1e));
				Vector3 bulb = dir * (0.15f + length);
				b.Lump(bulb, 0.035f, 0.025f, 0.02f, 0.15f, Hex(0x7a6428), Hex(0x5a4a1e));
				var side = new Vector3(-dir.z, 0f, dir.x);
				for (int k = -1; k <= 1; k += 2)
				{
					Vector3 root = bulb + side * (0.02f * k) + Vector3.up * 0.01f, tip = root + (dir + side * (0.35f * k)).normalized * b.Range(0.15f, 0.3f);
					b.Quad(root - side * 0.025f, root + side * 0.025f, tip + side * 0.012f, tip - side * 0.012f, Hex(k > 0 ? 0x6a5a22 : 0x5e4e1e));
				}
			}

			var mesh = new Mesh { name = "Sea floats", hideFlags = HideFlags.DontSave };
			if (b.Positions.Count > 65000)
			{
				mesh.indexFormat = IndexFormat.UInt32;
			}
			mesh.SetVertices(b.Positions);
			mesh.SetNormals(b.Normals);
			mesh.SetColors(b.Colours);
			mesh.subMeshCount = BiomeSeaFloats.KindCount;
			for (int i = 0; i < BiomeSeaFloats.KindCount; i++)
			{
				mesh.SetTriangles(b.Indices[i], i, false);
			}
			mesh.RecalculateBounds();
			mesh.UploadMeshData(false);
			return mesh;
		}
	}
}
