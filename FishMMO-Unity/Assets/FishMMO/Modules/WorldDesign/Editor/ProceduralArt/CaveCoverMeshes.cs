#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The run-time cave cover's art (2026-10-20): one small clump a <see cref="CaveCoverType"/> at each of three levels of detail
	/// — moss cushions, glow moss, a spread of pebbles or grit, a clump of mushrooms or glowcaps, bone fragments, a guano or
	/// sulphur crust, cave pearls in their cup, a mud sheen, root litter, frost needles, basalt grit, ember glints, lichen,
	/// glowing specks, a row of bracket fungi, mineral glints, a seep's wet streak, soda straws, hanging roots, glow-worm
	/// threads, a mine's timber offcuts, iron bits, ore crumbs and a candle stub — all in ONE mesh, a submesh a type and level
	/// (<see cref="CaveCoverTypes.SubmeshOf"/>), which the client's cover renderer (FishCaveCover.compute) draws straight off.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Frames.</b> A <see cref="CaveCoverMount.Surface"/> or <see cref="CaveCoverMount.Downhill"/> clump is built with its
	/// +Y out of the rock (and a Downhill one with +Z down the slope); an <see cref="CaveCoverMount.Upright"/> one with +Y up,
	/// and a roof's (soda straws, roots, threads) hangs below its origin. The origin is on the rock; the renderer sinks each a
	/// type's <see cref="CaveCoverTypeInfo.Sink"/>.
	/// </para>
	/// <para>
	/// <b>No textures.</b> Colour is the vertex colour (sRGB) — rgb the albedo, alpha 255 for an ordinary part and less for a
	/// glowing one (the shader glows by 1 − alpha in the cave's colour of the type's glow slot) — and UV0.x the height up the
	/// item, 0 at its foot (the shader darkens the foot). Everything is tiny, so a level is a handful of triangles: level 0 is
	/// the clump in full, level 1 fewer items and sides, level 2 its two or three largest items as a few faces.
	/// </para>
	/// </remarks>
	public static class CaveCoverMeshes
	{
		/// <summary>The payload mesh holding every type and level.</summary>
		public const string SetMeshName = "CaveCover_Set";

		/// <summary>Every type at every level in one builder, a submesh each in <see cref="CaveCoverTypes.SubmeshOf"/> order.</summary>
		public static MeshBuilder BuildSet(int seed)
		{
			var set = new MeshBuilder(CaveCoverTypes.Count * CaveCoverTypes.Levels);
			for (int t = 0; t < CaveCoverTypes.Count; t++)
			{
				for (int level = 0; level < CaveCoverTypes.Levels; level++)
				{
					MeshBuilder one = Build((CaveCoverType)t, level, seed);
					set.Append(one, CaveCoverTypes.SubmeshOf((CaveCoverType)t, level));
				}
			}
			set.RecalculateTangents();
			return set;
		}

		/// <summary>One type at one level (0 full, 2 the farthest).</summary>
		public static MeshBuilder Build(CaveCoverType type, int level, int seed)
		{
			var b = new MeshBuilder(1);
			level = Mathf.Clamp(level, 0, CaveCoverTypes.Levels - 1);
			var random = new DeterministicRNG(unchecked(seed * 31 + ((int)type + 1) * 7919));
			switch (type)
			{
				case CaveCoverType.MossCushion: MossCushion(b, random, level); break;
				case CaveCoverType.GlowMoss: GlowMoss(b, random, level); break;
				case CaveCoverType.Gravel: Stones(b, random, level, 7, 10, 0.02f, 0.06f, 0.3f, Gravel, true); break;
				case CaveCoverType.Grit: Stones(b, random, level, 10, 16, 0.008f, 0.02f, 0.25f, GritTones, false); break;
				case CaveCoverType.Mushrooms: Mushrooms(b, random, level, 3, 7, 0.04f, 0.12f, 0.02f, 0.05f, false); break;
				case CaveCoverType.GlowCaps: Mushrooms(b, random, level, 5, 9, 0.02f, 0.06f, 0.008f, 0.02f, true); break;
				case CaveCoverType.Bones: Bones(b, random, level); break;
				case CaveCoverType.Guano: Crust(b, random, level, 0.15f, 0.3f, GuanoTones, 3); break;
				case CaveCoverType.CavePearls: CavePearls(b, random, level); break;
				case CaveCoverType.MudSheen: Crust(b, random, level, 0.3f, 0.5f, MudTones, 0); break;
				case CaveCoverType.RootLitter: RootLitter(b, random, level); break;
				case CaveCoverType.FrostCrystals: Needles(b, random, level, 5, 9, 0.02f, 0.08f, FrostTones, 200); break;
				case CaveCoverType.BasaltGrit: Stones(b, random, level, 10, 16, 0.01f, 0.035f, 0.3f, BasaltTones, false); break;
				case CaveCoverType.SulphurCrust: Crust(b, random, level, 0.1f, 0.25f, SulphurTones, 4); break;
				case CaveCoverType.EmberGlints: Embers(b, random, level); break;
				case CaveCoverType.Lichen: Lichen(b, random, level); break;
				case CaveCoverType.GlowSpecks: Specks(b, random, level); break;
				case CaveCoverType.BracketFungus: Brackets(b, random, level); break;
				case CaveCoverType.MineralGlints: Needles(b, random, level, 4, 8, 0.01f, 0.03f, GlintTones, 150); break;
				case CaveCoverType.DripSheen: Seep(b, random, level); break;
				case CaveCoverType.SodaStraws: SodaStraws(b, random, level); break;
				case CaveCoverType.HangingRoots: HangingRoots(b, random, level); break;
				case CaveCoverType.GlowThreads: GlowThreads(b, random, level); break;
				case CaveCoverType.TimberOffcuts: Timber(b, random, level); break;
				case CaveCoverType.IronBits: Iron(b, random, level); break;
				case CaveCoverType.OreCrumbs: Stones(b, random, level, 6, 10, 0.008f, 0.025f, 0.22f, OreTones, false); break;
				case CaveCoverType.CandleStubs: Candles(b, random, level); break;
			}
			if (b.VertexCount == 0)
			{
				// Never an empty level: a speck the size of a grain, so every submesh has something to draw.
				Chip(b, new Vector3(0f, 0.002f, 0f), 0.004f, random, Hex(0x707070));
			}
			return b;
		}

		// ── Palettes (sRGB) ──

		private static readonly int[] Gravel = { 0x8a8478, 0x6e6a62, 0x9c9282, 0x5c574f, 0xa89c88 };
		private static readonly int[] GritTones = { 0x9a9282, 0x7c766c, 0xb0a690, 0x67625a };
		private static readonly int[] GuanoTones = { 0xb5ad98, 0x8c8270, 0x7a6f5c, 0xc4bca6 };
		private static readonly int[] MudTones = { 0x3d3226, 0x463a2c, 0x332a20 };
		private static readonly int[] FrostTones = { 0xe8f2ff, 0xd0e4f6, 0xf4f8ff };
		private static readonly int[] BasaltTones = { 0x2a2a2c, 0x353234, 0x222224, 0x3e3a38 };
		private static readonly int[] SulphurTones = { 0xd8c040, 0xe8d870, 0xe8e2cf, 0xc8a838 };
		private static readonly int[] GlintTones = { 0xe8e4f0, 0xd8e8f0, 0xf0e8d8 };
		private static readonly int[] OreTones = { 0xb8a070, 0x9aa0a8, 0x8a4a2a, 0xc0a050, 0x6a6a70 };
		private static readonly int[] MossTones = { 0x4a6b2a, 0x5c7a32, 0x6f8a3a, 0x3f5c26, 0x7a8f3a };
		private static readonly int[] WoodTones = { 0x7a5a3a, 0x6a4c30, 0x8c6c48, 0x5c4028 };

		private static Color32 Hex(int rgb, byte alpha = 255) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, alpha);

		private static Color32 Pick(DeterministicRNG random, int[] tones, byte alpha = 255) => Hex(tones[random.Range(0, tones.Length)], alpha);

		/// <summary>How many of a clump's items a level keeps: all, about 60 %, the two or three largest.</summary>
		private static int Keep(int count, int level) => level == 0 ? count : level == 1 ? Mathf.Max(1, Mathf.CeilToInt(count * 0.6f)) : Mathf.Min(count, Mathf.Max(1, count / 3));

		/// <summary>A point in a disc of a radius, denser to the middle (a clump, not a ring).</summary>
		private static Vector2 InDisc(DeterministicRNG random, float radius)
		{
			float a = random.NextFloat() * Mathf.PI * 2f;
			float r = radius * Mathf.Sqrt(random.NextFloat()) * (0.4f + 0.6f * random.NextFloat());
			return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
		}

		/// <summary>Items sized at random, then sorted largest first, so a level that keeps n keeps the n largest.</summary>
		private static List<(Vector2 at, float size, int salt)> Items(DeterministicRNG random, int min, int max, float sizeMin, float sizeMax, float radius)
		{
			int n = random.Range(min, max + 1);
			var items = new List<(Vector2, float, int)>(n);
			for (int i = 0; i < n; i++)
			{
				items.Add((InDisc(random, radius), Mathf.Lerp(sizeMin, sizeMax, random.NextFloat() * random.NextFloat()), random.Next()));
			}
			items.Sort((x, y) => y.Item2.CompareTo(x.Item2));
			return items;
		}

		// ── Primitives ──

		private static int Vertex(MeshBuilder b, Vector3 p, Vector3 n, Color32 c, float height)
		{
			// Normalised by hand: a grain-sized face's normal is shorter than Vector3.normalized's epsilon, which returns zero.
			float length = n.magnitude;
			return b.AddVertex(p, length > 1e-20f ? n / length : Vector3.up, new Vector2(Mathf.Clamp01(height), 0f), c);
		}

		/// <summary>A dome (the top half of an ellipsoid) on the plane y = foot.y: open underneath, as it sits on the rock.</summary>
		private static void Dome(MeshBuilder b, Vector3 foot, Vector3 radii, int sides, int rings, Color32 side, Color32 top)
		{
			sides = Mathf.Max(3, sides);
			rings = Mathf.Max(1, rings);
			int first = b.VertexCount;
			for (int r = 0; r < rings; r++)
			{
				float phi = (float)r / rings * Mathf.PI * 0.5f;
				float cy = Mathf.Cos(phi), sy = Mathf.Sin(phi);
				for (int s = 0; s < sides; s++)
				{
					float th = (float)s / sides * Mathf.PI * 2f;
					var unit = new Vector3(Mathf.Cos(th) * cy, sy, Mathf.Sin(th) * cy);
					Vector3 p = foot + Vector3.Scale(unit, radii);
					Vector3 n = new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z);
					Vertex(b, p, n, Color32.Lerp(side, top, (float)r / rings), sy);
				}
			}
			int apex = Vertex(b, foot + new Vector3(0f, radii.y, 0f), Vector3.up, top, 1f);
			for (int r = 0; r + 1 < rings; r++)
			{
				for (int s = 0; s < sides; s++)
				{
					int a = first + r * sides + s, c = first + r * sides + (s + 1) % sides;
					int a2 = a + sides, c2 = c + sides;
					Vector3 out0 = b.Positions[a] + b.Positions[c2] - 2f * foot;
					b.AddQuad(0, a, c, c2, a2, out0);
				}
			}
			int last = first + (rings - 1) * sides;
			for (int s = 0; s < sides; s++)
			{
				int a = last + s, c = last + (s + 1) % sides;
				b.AddTriangle(0, a, c, apex, b.Positions[a] + b.Positions[c] - 2f * foot);
			}
		}

		/// <summary>A faceted chip (a jittered octahedron, its faces flat-shaded), half sunk at its foot.</summary>
		private static void Chip(MeshBuilder b, Vector3 centre, float size, DeterministicRNG random, Color32 colour, bool flat = true)
		{
			var corners = new Vector3[6];
			Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
			for (int i = 0; i < 6; i++)
			{
				float k = size * (0.6f + 0.6f * random.NextFloat());
				if (flat && i >= 2 && i <= 3)
				{
					k *= 0.55f;
				}
				corners[i] = centre + axes[i] * k + new Vector3(random.NextFloat() - 0.5f, random.NextFloat() - 0.5f, random.NextFloat() - 0.5f) * size * 0.3f;
			}
			int[,] faces = { { 0, 2, 4 }, { 4, 2, 1 }, { 1, 2, 5 }, { 5, 2, 0 }, { 0, 4, 3 }, { 4, 1, 3 }, { 1, 5, 3 }, { 5, 0, 3 } };
			for (int f = 0; f < 8; f++)
			{
				Vector3 p0 = corners[faces[f, 0]], p1 = corners[faces[f, 1]], p2 = corners[faces[f, 2]];
				Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
				Vector3 outward = (p0 + p1 + p2) / 3f - centre;
				if (Vector3.Dot(n, outward) < 0f)
				{
					n = -n;
				}
				float h0 = Mathf.Clamp01(p0.y / Mathf.Max(1e-3f, size * 2f)), h1 = Mathf.Clamp01(p1.y / Mathf.Max(1e-3f, size * 2f)), h2 = Mathf.Clamp01(p2.y / Mathf.Max(1e-3f, size * 2f));
				int a = Vertex(b, p0, n, colour, h0), c = Vertex(b, p1, n, colour, h1), d = Vertex(b, p2, n, colour, h2);
				b.AddTriangle(0, a, c, d, outward);
			}
		}

		/// <summary>A tube from <paramref name="a"/> to <paramref name="z"/>, tapering, its far end capped by a point.</summary>
		private static void Tube(MeshBuilder b, Vector3 a, Vector3 z, float r0, float r1, int sides, Color32 c0, Color32 c1, float h0 = 0f, float h1 = 1f)
		{
			sides = Mathf.Max(3, sides);
			Vector3 axis = z - a;
			if (axis.sqrMagnitude < 1e-10f)
			{
				return;
			}
			Vector3 dir = axis.normalized;
			Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
			Vector3 v = Vector3.Cross(dir, u);
			int first = b.VertexCount;
			for (int ring = 0; ring < 2; ring++)
			{
				Vector3 centre = ring == 0 ? a : z;
				float r = ring == 0 ? r0 : r1;
				for (int s = 0; s < sides; s++)
				{
					float th = (float)s / sides * Mathf.PI * 2f;
					Vector3 radial = u * Mathf.Cos(th) + v * Mathf.Sin(th);
					Vertex(b, centre + radial * r, radial, ring == 0 ? c0 : c1, ring == 0 ? h0 : h1);
				}
			}
			for (int s = 0; s < sides; s++)
			{
				int p = first + s, q = first + (s + 1) % sides;
				Vector3 radial = b.Positions[p] + b.Positions[q] - 2f * a;
				radial -= dir * Vector3.Dot(radial, dir);
				b.AddQuad(0, p, q, q + sides, p + sides, radial);
			}
			int tip = Vertex(b, z + dir * r1 * 0.8f, dir, c1, h1);
			for (int s = 0; s < sides; s++)
			{
				int p = first + sides + s, q = first + sides + (s + 1) % sides;
				b.AddTriangle(0, p, q, tip, dir + (b.Positions[p] + b.Positions[q] - 2f * z));
			}
		}

		/// <summary>A box, flat-shaded.</summary>
		private static void Box(MeshBuilder b, Vector3 centre, Vector3 half, Quaternion rotation, Color32 colour, float height = 0.5f)
		{
			Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
			foreach (Vector3 n in normals)
			{
				Vector3 t = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
				Vector3 s = Vector3.Cross(n, t);
				Vector3 Corner(float i, float j) => centre + rotation * Vector3.Scale(n + t * i + s * j, half);
				Vector3 wn = rotation * n;
				int a = Vertex(b, Corner(-1f, -1f), wn, colour, height), c = Vertex(b, Corner(1f, -1f), wn, colour, height);
				int d = Vertex(b, Corner(1f, 1f), wn, colour, height), e = Vertex(b, Corner(-1f, 1f), wn, colour, height);
				b.AddQuad(0, a, c, d, e, wn);
			}
		}

		/// <summary>A pointed crystal or needle: a pyramid of <paramref name="sides"/> from a base ring to its tip.</summary>
		private static void Spike(MeshBuilder b, Vector3 foot, Vector3 tip, float radius, int sides, Color32 baseColour, Color32 tipColour)
		{
			sides = Mathf.Max(3, sides);
			Vector3 dir = (tip - foot).normalized;
			Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
			Vector3 v = Vector3.Cross(dir, u);
			for (int s = 0; s < sides; s++)
			{
				float t0 = (float)s / sides * Mathf.PI * 2f, t1 = (float)(s + 1) / sides * Mathf.PI * 2f;
				Vector3 p0 = foot + (u * Mathf.Cos(t0) + v * Mathf.Sin(t0)) * radius;
				Vector3 p1 = foot + (u * Mathf.Cos(t1) + v * Mathf.Sin(t1)) * radius;
				Vector3 n = Vector3.Cross(p1 - p0, tip - p0);
				Vector3 outward = (p0 + p1) * 0.5f - foot;
				if (Vector3.Dot(n, outward) < 0f)
				{
					n = -n;
				}
				int a = Vertex(b, p0, n, baseColour, 0f), c = Vertex(b, p1, n, baseColour, 0f), d = Vertex(b, tip, n, tipColour, 1f);
				b.AddTriangle(0, a, c, d, outward);
			}
		}

		/// <summary>A flat, ragged patch lying on the rock a little above it, facing +Y (crusts, sheens, lichen, leaves).</summary>
		private static void Patch(MeshBuilder b, Vector3 centre, float radius, float stretch, float angle, int sides, DeterministicRNG random, Color32 middle, Color32 edge, float lift)
		{
			sides = Mathf.Max(3, sides);
			Quaternion turn = Quaternion.AngleAxis(angle, Vector3.up);
			int mid = Vertex(b, centre + new Vector3(0f, lift * 1.5f, 0f), Vector3.up, middle, 0.5f);
			int first = b.VertexCount;
			for (int s = 0; s < sides; s++)
			{
				float th = (float)s / sides * Mathf.PI * 2f;
				float r = radius * (0.7f + 0.45f * random.NextFloat());
				Vector3 p = centre + turn * new Vector3(Mathf.Cos(th) * r * stretch, lift, Mathf.Sin(th) * r);
				Vertex(b, p, Vector3.up, edge, 0f);
			}
			for (int s = 0; s < sides; s++)
			{
				b.AddTriangle(0, mid, first + s, first + (s + 1) % sides, Vector3.up);
			}
		}

		// ── Floors ──

		private static void MossCushion(MeshBuilder b, DeterministicRNG random, int level)
		{
			var items = Items(random, 3, 5, 0.06f, 0.16f, 0.25f);
			int keep = Keep(items.Count, level);
			for (int i = 0; i < keep; i++)
			{
				(Vector2 at, float size, int salt) = items[i];
				var r = new DeterministicRNG(salt);
				Color32 c = Pick(r, MossTones);
				Color32 top = Color32.Lerp(c, Hex(0x9aab4a), 0.35f);
				int sides = level == 0 ? 8 : level == 1 ? 6 : 4;
				int rings = level == 0 ? 3 : level == 1 ? 2 : 1;
				Dome(b, new Vector3(at.x, -0.01f, at.y), new Vector3(size, size * 0.38f, size * (0.8f + 0.3f * r.NextFloat())), sides, rings, c, top);
			}
		}

		private static void GlowMoss(MeshBuilder b, DeterministicRNG random, int level)
		{
			// A low mat, and tufts standing out of it whose tips glow.
			if (level < 2)
			{
				Patch(b, Vector3.zero, 0.16f, 1.2f, random.NextFloat() * 360f, level == 0 ? 9 : 6, random, Hex(0x34502a), Hex(0x2a4024), 0.004f);
			}
			var items = Items(random, 7, 11, 0.025f, 0.06f, 0.18f);
			int keep = Keep(items.Count, level);
			for (int i = 0; i < keep; i++)
			{
				(Vector2 at, float size, int salt) = items[i];
				var r = new DeterministicRNG(salt);
				Vector3 foot = new Vector3(at.x, 0f, at.y);
				Vector3 lean = new Vector3(r.NextFloat() - 0.5f, 0f, r.NextFloat() - 0.5f) * size * 0.6f;
				Spike(b, foot, foot + Vector3.up * size + lean, size * 0.22f, level == 0 ? 4 : 3, Hex(0x3a5a2c), Hex(0xa8e0a0, 30));
			}
		}

		private static void Stones(MeshBuilder b, DeterministicRNG random, int level, int min, int max, float sizeMin, float sizeMax, float radius, int[] tones, bool rounded)
		{
			var items = Items(random, min, max, sizeMin, sizeMax, radius);
			int keep = Keep(items.Count, level);
			for (int i = 0; i < keep; i++)
			{
				(Vector2 at, float size, int salt) = items[i];
				var r = new DeterministicRNG(salt);
				Color32 c = Pick(r, tones);
				if (rounded && level == 0)
				{
					Dome(b, new Vector3(at.x, -size * 0.25f, at.y), new Vector3(size, size * 0.6f, size * (0.7f + 0.3f * r.NextFloat())), 6, 2, c, Color32.Lerp(c, Hex(0xd0c8b8), 0.12f));
				}
				else
				{
					Chip(b, new Vector3(at.x, size * 0.15f, at.y), size, r, c);
				}
			}
		}

		private static void Mushrooms(MeshBuilder b, DeterministicRNG random, int level, int min, int max, float heightMin, float heightMax, float capMin, float capMax, bool glow)
		{
			var items = Items(random, min, max, 0f, 1f, glow ? 0.1f : 0.12f);
			int keep = Keep(items.Count, level);
			int[] caps = glow ? new[] { 0xd8f0e0, 0xe0e8f8, 0xf0ecd0 } : new[] { 0xa88a60, 0x8c6a48, 0xc0a070, 0x7a5a3c, 0xd0b890 };
			for (int i = 0; i < keep; i++)
			{
				(Vector2 at, float size, int salt) = items[i];
				var r = new DeterministicRNG(salt);
				float height = Mathf.Lerp(heightMin, heightMax, size);
				float cap = Mathf.Lerp(capMin, capMax, size) * (0.8f + 0.4f * r.NextFloat());
				Vector3 foot = new Vector3(at.x, -0.005f, at.y);
				Vector3 top = foot + new Vector3((r.NextFloat() - 0.5f) * height * 0.3f, height, (r.NextFloat() - 0.5f) * height * 0.3f);
				Color32 stalk = glow ? Hex(0xd8e4d8, 180) : Hex(0xe0d4b8);
				Tube(b, foot, top, cap * 0.22f, cap * 0.16f, level == 0 ? 5 : 3, stalk, stalk, 0f, 0.8f);
				Color32 capColour = Hex(caps[r.Range(0, caps.Length)], glow ? (byte)20 : (byte)255);
				Color32 rim = glow ? Hex(0xb0f0c8, 60) : Color32.Lerp(capColour, Hex(0x3a2a1c), 0.3f);
				Dome(b, top - new Vector3(0f, cap * 0.15f, 0f), new Vector3(cap, cap * 0.55f, cap), level == 0 ? 7 : level == 1 ? 5 : 4, level == 0 ? 2 : 1, rim, capColour);
			}
		}

		private static void Bones(MeshBuilder b, DeterministicRNG random, int level)
		{
			Color32 bone = Hex(0xd8cfb5), stained = Hex(0xb0a080);
			int n = random.Range(2, 5);
			int keep = level == 2 ? 1 : level == 1 ? Mathf.Min(n, 2) : n;
			for (int i = 0; i < keep; i++)
			{
				float length = Mathf.Lerp(0.12f, 0.3f, random.NextFloat());
				float r = Mathf.Lerp(0.012f, 0.02f, random.NextFloat());
				Vector2 at = InDisc(random, 0.18f);
				float yaw = random.NextFloat() * Mathf.PI;
				Vector3 d = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * length * 0.5f;
				Vector3 c = new Vector3(at.x, r * 0.6f, at.y);
				Tube(b, c - d, c + d, r, r * 0.9f, level == 0 ? 5 : 3, stained, bone, 0.3f, 0.5f);
				if (level == 0)
				{
					Dome(b, c - d - new Vector3(0f, r, 0f), new Vector3(r * 1.6f, r * 1.8f, r * 1.6f), 5, 2, stained, bone);
				}
			}
			// A skull's broken dome, now and then.
			if (level < 2 && random.NextFloat() < 0.5f)
			{
				Vector2 at = InDisc(random, 0.12f);
				Dome(b, new Vector3(at.x, -0.01f, at.y), new Vector3(0.06f, 0.05f, 0.075f), level == 0 ? 8 : 5, level == 0 ? 3 : 2, stained, bone);
			}
		}

		private static void Crust(MeshBuilder b, DeterministicRNG random, int level, float radiusMin, float radiusMax, int[] tones, int lumps)
		{
			int patches = level == 2 ? 1 : 2;
			for (int i = 0; i < patches; i++)
			{
				Vector2 at = i == 0 ? Vector2.zero : InDisc(random, radiusMin);
				float radius = Mathf.Lerp(radiusMin, radiusMax, random.NextFloat()) * (i == 0 ? 1f : 0.6f);
				Color32 middle = Pick(random, tones), edge = Pick(random, tones);
				Patch(b, new Vector3(at.x, 0f, at.y), radius, 0.7f + 0.6f * random.NextFloat(), random.NextFloat() * 360f, level == 0 ? 12 : level == 1 ? 7 : 5, random, middle, edge, 0.004f + 0.002f * i);
			}
			int keep = level == 0 ? lumps : level == 1 ? lumps / 2 : 0;
			for (int i = 0; i < keep; i++)
			{
				Vector2 at = InDisc(random, radiusMin);
				Chip(b, new Vector3(at.x, 0.004f, at.y), Mathf.Lerp(0.008f, 0.02f, random.NextFloat()), random, Pick(random, tones));
			}
		}

		private static void CavePearls(MeshBuilder b, DeterministicRNG random, int level)
		{
			// The pool's shallow cup, then the pearls in it.
			Patch(b, Vector3.zero, 0.11f, 1f, random.NextFloat() * 360f, level == 0 ? 10 : 6, random, Hex(0x8c8270), Hex(0xa89c88), 0.003f);
			var items = Items(random, 3, 6, 0.008f, 0.02f, 0.07f);
			int keep = Keep(items.Count, level);
			for (int i = 0; i < keep; i++)
			{
				(Vector2 at, float size, int salt) = items[i];
				Color32 pearl = Hex(new DeterministicRNG(salt).NextFloat() < 0.5f ? 0xf0e8d8 : 0xe4dcc8);
				Dome(b, new Vector3(at.x, -size * 0.3f, at.y), new Vector3(size, size * 1.3f, size), level == 0 ? 6 : 4, level == 0 ? 3 : 1, pearl, pearl);
			}
		}

		private static void RootLitter(MeshBuilder b, DeterministicRNG random, int level)
		{
			int twigs = Keep(random.Range(4, 8), level), leaves = Keep(random.Range(4, 9), level);
			for (int i = 0; i < twigs; i++)
			{
				Vector2 at = InDisc(random, 0.2f);
				float yaw = random.NextFloat() * Mathf.PI * 2f, length = Mathf.Lerp(0.08f, 0.25f, random.NextFloat());
				float r = Mathf.Lerp(0.004f, 0.008f, random.NextFloat());
				Vector3 d = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * length * 0.5f;
				Vector3 c = new Vector3(at.x, r, at.y);
				Color32 bark = Pick(random, WoodTones);
				Tube(b, c - d, c + d, r, r * 0.6f, 3, bark, bark, 0.2f, 0.4f);
			}
			int[] dead = { 0x8a6a3a, 0x6c4e2a, 0xa07c48, 0x5a4428 };
			for (int i = 0; i < leaves; i++)
			{
				Vector2 at = InDisc(random, 0.22f);
				Patch(b, new Vector3(at.x, 0f, at.y), Mathf.Lerp(0.015f, 0.03f, random.NextFloat()), 1.8f, random.NextFloat() * 360f, 4, random, Pick(random, dead), Pick(random, dead), 0.003f + 0.001f * i);
			}
		}

		private static void Needles(MeshBuilder b, DeterministicRNG random, int level, int min, int max, float heightMin, float heightMax, int[] tones, byte tipGlow)
		{
			var items = Items(random, min, max, heightMin, heightMax, 0.12f);
			int keep = Keep(items.Count, level);
			for (int i = 0; i < keep; i++)
			{
				(Vector2 at, float size, int salt) = items[i];
				var r = new DeterministicRNG(salt);
				Vector3 foot = new Vector3(at.x, -0.003f, at.y);
				Vector3 lean = new Vector3(at.x, 0f, at.y).normalized * size * 0.5f + new Vector3(r.NextFloat() - 0.5f, 0f, r.NextFloat() - 0.5f) * size * 0.4f;
				Color32 c = Pick(r, tones);
				Spike(b, foot, foot + Vector3.up * size + lean, size * 0.2f, level == 0 ? 4 : 3, c, Hex((c.r << 16) | (c.g << 8) | c.b, tipGlow));
			}
		}

		private static void Embers(MeshBuilder b, DeterministicRNG random, int level)
		{
			var items = Items(random, 3, 6, 0.008f, 0.02f, 0.15f);
			int keep = Keep(items.Count, level);
			for (int i = 0; i < keep; i++)
			{
				(Vector2 at, float size, int salt) = items[i];
				var r = new DeterministicRNG(salt);
				// A clinker crumb whose cracks glow: most of it glowing, a dark crust over some faces.
				Chip(b, new Vector3(at.x, 0f, at.y), size, r, r.NextFloat() < 0.6f ? Hex(0xff8030, 0) : Hex(0x3a2420, 160));
			}
		}

		// ── Walls ──

		private static void Lichen(MeshBuilder b, DeterministicRNG random, int level)
		{
			int[] tones = { 0x9aa58a, 0x8a9a7a, 0xa8ae90, 0xc8a040, 0x7c8c6a };
			int patches = level == 0 ? 4 : level == 1 ? 3 : 1;
			for (int i = 0; i < patches; i++)
			{
				Vector2 at = i == 0 ? Vector2.zero : InDisc(random, 0.18f);
				Color32 c = Pick(random, tones);
				Patch(b, new Vector3(at.x, 0f, at.y), Mathf.Lerp(0.05f, 0.14f, random.NextFloat()), 0.8f + 0.4f * random.NextFloat(), random.NextFloat() * 360f,
					level == 0 ? 10 : 6, random, c, Color32.Lerp(c, Hex(0x5a6450), 0.3f), 0.002f + 0.0015f * i);
			}
		}

		private static void Specks(MeshBuilder b, DeterministicRNG random, int level)
		{
			var items = Items(random, 8, 14, 0.004f, 0.011f, 0.2f);
			int keep = Keep(items.Count, level);
			for (int i = 0; i < keep; i++)
			{
				(Vector2 at, float size, _) = items[i];
				Dome(b, new Vector3(at.x, -0.001f, at.y), new Vector3(size, size * 0.6f, size), level == 0 ? 5 : 3, 1, Hex(0xc8f0d8, 40), Hex(0xe8fff0, 0));
			}
		}

		private static void Brackets(MeshBuilder b, DeterministicRNG random, int level)
		{
			// Frame: +Y out of the wall, +Z down it. Each shelf is a half disc standing out of the wall, level in the world (its
			// plane across Y and X), its top tan and its pores underneath glowing.
			int n = random.Range(3, 6);
			int keep = Keep(n, level);
			for (int i = 0; i < keep; i++)
			{
				float radius = Mathf.Lerp(0.03f, 0.07f, random.NextFloat());
				float x = (random.NextFloat() - 0.5f) * 0.18f;
				float z = (i - n * 0.5f) * 0.05f + (random.NextFloat() - 0.5f) * 0.02f;
				int sides = level == 0 ? 7 : level == 1 ? 5 : 3;
				float thick = radius * 0.22f;
				Color32 top = Hex(random.NextFloat() < 0.5f ? 0x8a6a48 : 0xa08458), under = Hex(0xd0f0e0, 40);
				for (int face = 0; face < 2; face++)
				{
					float zz = z + (face == 0 ? -thick * 0.5f : thick * 0.5f);
					Vector3 normal = face == 0 ? Vector3.back : Vector3.forward;
					int centre = Vertex(b, new Vector3(x, 0f, zz), normal, face == 0 ? top : under, 0.5f);
					int first = b.VertexCount;
					for (int s = 0; s <= sides; s++)
					{
						float th = (float)s / sides * Mathf.PI;
						Vertex(b, new Vector3(x + Mathf.Cos(th) * radius * 1.2f, Mathf.Sin(th) * radius, zz), normal, face == 0 ? top : under, 1f);
					}
					for (int s = 0; s < sides; s++)
					{
						b.AddTriangle(0, centre, first + s, first + s + 1, normal);
					}
				}
			}
		}

		private static void Seep(MeshBuilder b, DeterministicRNG random, int level)
		{
			// A wet streak down the wall (+Z down it), widest where it seeps out, wavering.
			float length = Mathf.Lerp(0.4f, 0.9f, random.NextFloat());
			float width = Mathf.Lerp(0.04f, 0.1f, random.NextFloat());
			int steps = level == 0 ? 8 : level == 1 ? 4 : 2;
			Color32 wet = Hex(0x2f2a24), thin = Hex(0x3a342c);
			float phase = random.NextFloat() * 6.28f;
			int first = b.VertexCount;
			for (int s = 0; s <= steps; s++)
			{
				float t = (float)s / steps;
				float x = Mathf.Sin(phase + t * 5f) * 0.02f;
				float w = Mathf.Lerp(width, width * 0.25f, t);
				Vertex(b, new Vector3(x - w, 0.002f, t * length), Vector3.up, s == 0 ? wet : Color32.Lerp(wet, thin, t), 0.5f);
				Vertex(b, new Vector3(x + w, 0.002f, t * length), Vector3.up, s == 0 ? wet : Color32.Lerp(wet, thin, t), 0.5f);
			}
			for (int s = 0; s < steps; s++)
			{
				int a = first + 2 * s;
				b.AddQuad(0, a, a + 1, a + 3, a + 2, Vector3.up);
			}
		}

		// ── Roofs (hanging below the origin) ──

		private static void SodaStraws(MeshBuilder b, DeterministicRNG random, int level)
		{
			var items = Items(random, 5, 9, 0.04f, 0.3f, 0.12f);
			int keep = Keep(items.Count, level);
			for (int i = 0; i < keep; i++)
			{
				(Vector2 at, float size, int salt) = items[i];
				var r = new DeterministicRNG(salt);
				float radius = Mathf.Lerp(0.003f, 0.005f, r.NextFloat());
				Vector3 root = new Vector3(at.x, 0.01f, at.y), tip = new Vector3(at.x, -size, at.y);
				Tube(b, root, tip, radius * 1.4f, radius, level == 0 ? 4 : 3, Hex(0xd8ccb0), Hex(0xeee4cc), 1f, 0f);
				if (level == 0)
				{
					// The drop forming at its tip.
					Dome(b, tip - new Vector3(0f, radius * 0.6f, 0f), new Vector3(radius * 1.1f, -radius * 1.4f, radius * 1.1f), 4, 1, Hex(0xe8f0f0), Hex(0xf4fbff));
				}
			}
		}

		private static void HangingRoots(MeshBuilder b, DeterministicRNG random, int level)
		{
			var items = Items(random, 3, 6, 0.2f, 0.7f, 0.15f);
			int keep = Keep(items.Count, level);
			int segments = level == 0 ? 4 : level == 1 ? 2 : 1;
			for (int i = 0; i < keep; i++)
			{
				(Vector2 at, float length, int salt) = items[i];
				var r = new DeterministicRNG(salt);
				Color32 root = Pick(r, WoodTones), tipColour = Hex(0x9a8060);
				Vector3 p = new Vector3(at.x, 0.02f, at.y);
				float radius = Mathf.Lerp(0.005f, 0.009f, r.NextFloat());
				for (int s = 0; s < segments; s++)
				{
					float t1 = (float)(s + 1) / segments;
					Vector3 next = new Vector3(at.x + (r.NextFloat() - 0.5f) * 0.06f * t1, -length * t1, at.y + (r.NextFloat() - 0.5f) * 0.06f * t1);
					Tube(b, p, next, Mathf.Lerp(radius, radius * 0.3f, (float)s / segments), Mathf.Lerp(radius, radius * 0.3f, t1), 3, root, Color32.Lerp(root, tipColour, t1), 1f - (float)s / segments, 1f - t1);
					if (level == 0 && s == 1)
					{
						// A rootlet off it.
						Vector3 side = p + new Vector3((r.NextFloat() - 0.5f) * 0.1f, -length * 0.25f, (r.NextFloat() - 0.5f) * 0.1f);
						Tube(b, p, side, radius * 0.4f, radius * 0.15f, 3, root, tipColour, 0.6f, 0.3f);
					}
					p = next;
				}
			}
		}

		private static void GlowThreads(MeshBuilder b, DeterministicRNG random, int level)
		{
			var items = Items(random, 5, 10, 0.08f, 0.4f, 0.15f);
			int keep = Keep(items.Count, level);
			Color32 silk = Hex(0xd8e8ec, 200), bead = Hex(0xd0f4ff, 0);
			for (int i = 0; i < keep; i++)
			{
				(Vector2 at, float length, int salt) = items[i];
				var r = new DeterministicRNG(salt);
				Vector3 top = new Vector3(at.x, 0f, at.y), end = new Vector3(at.x, -length, at.y);
				if (level < 2)
				{
					Tube(b, top, end, 0.0012f, 0.0008f, 3, silk, silk, 1f, 0f);
				}
				int beads = level == 0 ? r.Range(3, 7) : level == 1 ? 2 : 1;
				for (int k = 0; k < beads; k++)
				{
					float t = (k + 0.5f + 0.3f * (r.NextFloat() - 0.5f)) / beads;
					float size = Mathf.Lerp(0.004f, 0.0025f, t);
					Chip(b, Vector3.Lerp(top, end, t), size, r, bead, false);
				}
				if (level == 0)
				{
					// The larva glowing at the top of its thread.
					Dome(b, top - new Vector3(0f, 0.004f, 0f), new Vector3(0.006f, -0.005f, 0.012f), 4, 1, Hex(0xb0f0ff, 0), Hex(0xe0ffff, 0));
				}
			}
		}

		// ── Mines ──

		private static void Timber(MeshBuilder b, DeterministicRNG random, int level)
		{
			int n = Keep(random.Range(2, 5), level);
			for (int i = 0; i < n; i++)
			{
				Vector2 at = InDisc(random, 0.2f);
				float length = Mathf.Lerp(0.08f, 0.3f, random.NextFloat());
				var half = new Vector3(length * 0.5f, Mathf.Lerp(0.008f, 0.015f, random.NextFloat()), Mathf.Lerp(0.01f, 0.025f, random.NextFloat()));
				Quaternion turn = Quaternion.Euler(random.NextFloat() * 8f - 4f, random.NextFloat() * 360f, random.NextFloat() * 10f - 5f);
				Box(b, new Vector3(at.x, half.y * 0.8f, at.y), half, turn, Pick(random, WoodTones), 0.4f);
			}
			if (level == 0)
			{
				// Splinters.
				for (int i = 0; i < 4; i++)
				{
					Vector2 at = InDisc(random, 0.22f);
					Vector3 d = Quaternion.Euler(0f, random.NextFloat() * 360f, 0f) * Vector3.right * Mathf.Lerp(0.02f, 0.05f, random.NextFloat());
					Vector3 c = new Vector3(at.x, 0.003f, at.y);
					Spike(b, c - d, c + d, 0.003f, 3, Pick(random, WoodTones), Hex(0xb09070));
				}
			}
		}

		private static void Iron(MeshBuilder b, DeterministicRNG random, int level)
		{
			int[] rust = { 0x5a3a2a, 0x3a3a3c, 0x6a4430, 0x48403c };
			int n = Keep(random.Range(3, 6), level);
			for (int i = 0; i < n; i++)
			{
				Vector2 at = InDisc(random, 0.18f);
				Color32 c = Pick(random, rust);
				Quaternion turn = Quaternion.Euler(0f, random.NextFloat() * 360f, 0f);
				float roll = random.NextFloat();
				if (roll < 0.55f)
				{
					// A nail lying on its side, with its head.
					float length = Mathf.Lerp(0.05f, 0.09f, random.NextFloat());
					Box(b, new Vector3(at.x, 0.003f, at.y), new Vector3(length * 0.5f, 0.002f, 0.002f), turn, c, 0.2f);
					if (level == 0)
					{
						Box(b, new Vector3(at.x, 0.004f, at.y) + turn * new Vector3(length * 0.5f, 0f, 0f), new Vector3(0.0015f, 0.004f, 0.004f), turn, c, 0.3f);
					}
				}
				else if (roll < 0.8f)
				{
					// A spike.
					Vector3 d = turn * Vector3.right * Mathf.Lerp(0.06f, 0.12f, random.NextFloat());
					Vector3 m = new Vector3(at.x, 0.005f, at.y);
					Spike(b, m - d * 0.5f, m + d * 0.5f, 0.006f, 4, c, Hex(0x4a4446));
				}
				else
				{
					// A broken pick or chisel head.
					Box(b, new Vector3(at.x, 0.012f, at.y), new Vector3(0.04f, 0.011f, 0.009f), turn, c, 0.4f);
				}
			}
		}

		private static void Candles(MeshBuilder b, DeterministicRNG random, int level)
		{
			int n = level == 0 ? random.Range(1, 3) : 1;
			Color32 wax = Hex(0xe8dcb8), drip = Hex(0xd8c8a0);
			for (int i = 0; i < n; i++)
			{
				Vector2 at = i == 0 ? Vector2.zero : InDisc(random, 0.06f);
				float r = Mathf.Lerp(0.012f, 0.018f, random.NextFloat()), h = Mathf.Lerp(0.02f, 0.06f, random.NextFloat());
				var foot = new Vector3(at.x, 0f, at.y);
				Tube(b, foot, foot + Vector3.up * h, r, r * 0.95f, level == 0 ? 7 : 4, drip, wax, 0f, 0.9f);
				if (level == 0)
				{
					Patch(b, foot, r * 2.2f, 1.1f, random.NextFloat() * 360f, 7, random, drip, drip, 0.002f);
					Box(b, foot + Vector3.up * (h + 0.004f), new Vector3(0.0008f, 0.004f, 0.0008f), Quaternion.identity, Hex(0x1c1814), 1f);
				}
				// The flame: a small teardrop of light.
				Vector3 flameFoot = foot + Vector3.up * (h + 0.003f);
				Spike(b, flameFoot, flameFoot + Vector3.up * 0.022f, 0.0045f, level == 0 ? 4 : 3, Hex(0xffc060, 0), Hex(0xfff0c0, 0));
				Spike(b, flameFoot + Vector3.up * 0.0005f, flameFoot - Vector3.up * 0.004f, 0.0045f, 3, Hex(0xffc060, 0), Hex(0xffa040, 0));
			}
		}
	}
}
#endif
