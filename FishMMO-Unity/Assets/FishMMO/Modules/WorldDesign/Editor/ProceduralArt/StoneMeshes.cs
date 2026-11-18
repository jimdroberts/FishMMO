#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// One stone's recipe: a base body and the marks its rock leaves on it. Every length but <see cref="Size"/> is in
	/// the stone's normalised space, where the base body is about one unit from its centre in every direction, so the
	/// same recipe reads the same on a pebble and on a boulder.
	/// </summary>
	/// <remarks>Built through the chained methods below (each returns a changed copy), as <see cref="StoneStyles"/> does.</remarks>
	public struct StoneShape
	{
		/// <summary>The seed's key: unique per material, slot and piece, so no two stones share their randomness.</summary>
		public string Name;
		/// <summary>Full extents in metres: x its length, y its height, z its breadth.</summary>
		public Vector3 Size;
		/// <summary>Superellipsoid exponents across (x, z) and up (y): 2 an ellipsoid, 3–4 a rounded block, 6+ a box.</summary>
		public float Square, SquareV;
		/// <summary>Three or more: a polygonal prism of that many sides instead (columnar jointing); 0 none.</summary>
		public int Sides;
		/// <summary>How unequal the prism's sides are, 0 regular … 0.3 a lava flow's ragged columns.</summary>
		public float SideJitter;
		/// <summary>Flat fracture or joint planes cut into it, how deep (0 grazing … 1 deep), and the band of heights their normals take (−1 down … 1 up).</summary>
		public int Facets;
		public float FacetDepth, FacetLow, FacetHigh;
		/// <summary>How soft every cut edge is (a soft minimum's width): 0.01 razor, 0.06 chipped, 0.2 well rounded.</summary>
		public float Edge;
		/// <summary>Conchoidal scars: shell-shaped hollows where a blow took a flake off, how deep, and the shell's radius.</summary>
		public int Scoops;
		public float ScoopDepth, ScoopRadius;
		/// <summary>Knobs standing proud (nodule lobes, a conglomerate's clasts, corallites): their angular width (radians) and height.</summary>
		public int Knobs;
		public float KnobWidth, KnobHeight;
		/// <summary>Pits sunk in (vesicles, solution pans, tafoni): their angular width (radians) and depth.</summary>
		public int Pits;
		public float PitWidth, PitDepth;
		/// <summary>Branches from its centre (a coral head's): their length and radius, and how far up they start (0 sideways … 1 straight up).</summary>
		public int Arms;
		public float ArmLength, ArmWidth, ArmRise;
		/// <summary>Grooves across it (bedding, cleavage, foliation, onion skins): how many over its height, how deep, and their dip in degrees.</summary>
		public float Beds, BedDepth, BedDip;
		/// <summary>Bends the grooves along noise contours (a brain coral's meanders); 0 straight.</summary>
		public float Meander;
		/// <summary>Grooves running down from its top (karren, the rills rain dissolves into limestone), how many round it and how deep.</summary>
		public int Flutes;
		public float FluteDepth;
		/// <summary>Noise: broad lumps, fracture ridges and fine grain.</summary>
		public float Lump, Ridge, Grain;
		/// <summary>Narrower toward its top (0 none … 0.6 a cone), and its top pushed sideways (a share of its length).</summary>
		public float Taper, Lean;
		/// <summary>The body's size within the extents (1 fills them; a coral head's small base under its branches is less).</summary>
		public float Core;

		/// <summary>A rounded lump of the given extents in metres, with a little noise: the start of every recipe.</summary>
		public static StoneShape Of(string name, float x, float y, float z) => new StoneShape
		{
			Name = name, Size = new Vector3(x, y, z), Square = 2f, SquareV = 2f, Edge = 0.06f, FacetLow = -0.2f, FacetHigh = 0.9f,
			ScoopRadius = 0.8f, Lump = 0.3f, Ridge = 0.1f, Core = 1f,
		};

		/// <summary>The same recipe at other extents (a slot's or a piece's size).</summary>
		public StoneShape Sized(float x, float y, float z) { StoneShape s = this; s.Size = new Vector3(x, y, z); return s; }
		public StoneShape Named(string name) { StoneShape s = this; s.Name = name; return s; }
		public StoneShape Boxy(float across, float up) { StoneShape s = this; s.Square = across; s.SquareV = up; return s; }
		public StoneShape Prism(int sides, float jitter) { StoneShape s = this; s.Sides = sides; s.SideJitter = jitter; return s; }
		public StoneShape Cut(int planes, float depth, float edge, float low = -0.2f, float high = 0.9f) { StoneShape s = this; s.Facets = planes; s.FacetDepth = depth; s.Edge = edge; s.FacetLow = low; s.FacetHigh = high; return s; }
		public StoneShape Soft(float edge) { StoneShape s = this; s.Edge = edge; return s; }
		public StoneShape Conchoidal(int scars, float depth, float radius = 0.8f) { StoneShape s = this; s.Scoops = scars; s.ScoopDepth = depth; s.ScoopRadius = radius; return s; }
		public StoneShape Knobbly(int knobs, float width, float height) { StoneShape s = this; s.Knobs = knobs; s.KnobWidth = width; s.KnobHeight = height; return s; }
		public StoneShape Pitted(int pits, float width, float depth) { StoneShape s = this; s.Pits = pits; s.PitWidth = width; s.PitDepth = depth; return s; }
		public StoneShape Branched(int arms, float length, float width, float rise) { StoneShape s = this; s.Arms = arms; s.ArmLength = length; s.ArmWidth = width; s.ArmRise = rise; return s; }
		public StoneShape Bedded(float beds, float depth, float dip = 0f) { StoneShape s = this; s.Beds = beds; s.BedDepth = depth; s.BedDip = dip; return s; }
		public StoneShape Meandering(float warp) { StoneShape s = this; s.Meander = warp; return s; }
		public StoneShape Fluted(int flutes, float depth) { StoneShape s = this; s.Flutes = flutes; s.FluteDepth = depth; return s; }
		public StoneShape Noise(float lump, float ridge, float grain = 0f) { StoneShape s = this; s.Lump = lump; s.Ridge = ridge; s.Grain = grain; return s; }
		public StoneShape Tapered(float taper, float lean = 0f) { StoneShape s = this; s.Taper = taper; s.Lean = lean; return s; }
		public StoneShape Cored(float core) { StoneShape s = this; s.Core = core; return s; }
	}

	/// <summary>One stone of a detail mesh's group, and the grid resolution it is built at (6·2·res² triangles).</summary>
	public struct StonePiece
	{
		public StoneShape Shape;
		public int Resolution;

		public StonePiece(StoneShape shape, int resolution)
		{
			Shape = shape;
			Resolution = resolution;
		}
	}

	/// <summary>What a stone's mesh measures as: the figures the tests hold each stone material to.</summary>
	public struct StoneMeasure
	{
		/// <summary>Height over the geometric mean of length and breadth: a slate flake ≈ 0.2, a corestone ≈ 0.75.</summary>
		public float Flatness;
		/// <summary>Length over breadth.</summary>
		public float Elongation;
		/// <summary>The share of edge length where neighbouring faces meet at 30° or more: sharp arrises.</summary>
		public float Angularity;
		/// <summary>The share of edge length folded inward by 8° or more: pits, flutes, scars, the necks of knobs.</summary>
		public float Concavity;
	}

	/// <summary>
	/// Stones: boulders, small rocks and pebbles, each material with shapes of its own (2026-10-16, decision 6 of the
	/// biome suite: "distinct mesh shapes per stone material"). Before this, every material's boulders, rocks and
	/// pebbles were one shared mesh per slot with only the texture changed (<see cref="RockMeshes"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A radius function on a cube-sphere</b>, as <see cref="RockMeshes"/>: every vertex is a direction times the
	/// stone's radius in it, so every level of detail has the same silhouette, a shared chart edge lands on the same point
	/// from both charts, and the mesh is closed whatever the recipe. What is new is the radius: a superellipsoid or a
	/// polygonal prism as the body, then the marks a rock's breaking leaves — fracture planes, conchoidal scars, knobs,
	/// pits, branches, bedding grooves, karren flutes — each a term of the radius, so they combine freely.
	/// </para>
	/// <para>
	/// <b>Wound by the sphere.</b> A star-shaped surface is the sphere pushed in and out along its rays, a map that keeps
	/// orientation, so each triangle takes the winding its cell has on the unit cube. Judging it from the displaced
	/// triangle (<see cref="RockMeshes"/>' way) guesses wrong on the steep walls of a branch or a pit.
	/// </para>
	/// <para>
	/// <b>UVs by cube projection</b> of the finished stone onto its chart's own axes, in metres over
	/// <see cref="RockMeshes.TextureMetres"/>, so a pebble and a boulder show their rock's texture at the same scale and
	/// the side charts run v up the stone, along which a strata texture lays its beds.
	/// </para>
	/// <para>
	/// <b>Pure C#</b> (no Matrix4x4, Quaternion or Mesh), so the tests can build every stone outside the editor.
	/// </para>
	/// </remarks>
	public static class StoneMeshes
	{
		private static readonly Vector3[] FaceNormal = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
		private static readonly Vector3[] FaceU = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.left, Vector3.right };
		private static readonly Vector3[] FaceV = { Vector3.up, Vector3.up, Vector3.forward, Vector3.back, Vector3.up, Vector3.up };

		/// <summary>Triangles a stone has at a resolution: six charts of res² quads.</summary>
		public static int TrianglesAt(int resolution) => 12 * resolution * resolution;

		/// <summary>One stone at a resolution (grid cells along each cube face's edge), bedded with its pivot a fifth of the way up.</summary>
		public static MeshBuilder Build(in StoneShape shape, int resolution, int seed)
		{
			int res = Mathf.Max(1, resolution);
			var marks = new Marks(in shape, ProceduralNoise.SeedFor(shape.Name, seed));
			Vector3 half = shape.Size * 0.5f;

			var mesh = new MeshBuilder(1);
			List<int> triangles = mesh.Submeshes[0];
			var white = new Color32(255, 255, 255, 0);
			for (int f = 0; f < 6; f++)
			{
				int first = mesh.VertexCount;
				for (int j = 0; j <= res; j++)
				{
					for (int i = 0; i <= res; i++)
					{
						// An exact integer numerator over res, so every chart computes a shared edge point bit-identically.
						float a = (float)(2 * i - res) / res;
						float b = (float)(2 * j - res) / res;
						Vector3 dir = CubePoint(f, a, b).normalized;
						Vector3 p = Deform(Vector3.Scale(dir * marks.Radius(dir), half), in shape, half);
						mesh.AddVertex(p, Vector3.zero, Vector2.zero, white);
					}
				}
				// The winding the cell has on the cube, outward there, and kept outward by the radial map.
				Vector3 c00 = CubePoint(f, -1f, -1f), c10 = CubePoint(f, 1f, -1f), c11 = CubePoint(f, 1f, 1f);
				bool forward = Vector3.Dot(Vector3.Cross(c10 - c00, c11 - c00), FaceNormal[f]) >= 0f;
				for (int j = 0; j < res; j++)
				{
					for (int i = 0; i < res; i++)
					{
						int v00 = first + j * (res + 1) + i;
						int v10 = v00 + 1, v01 = v00 + res + 1, v11 = v01 + 1;
						if (forward)
						{
							triangles.Add(v00); triangles.Add(v10); triangles.Add(v11);
							triangles.Add(v00); triangles.Add(v11); triangles.Add(v01);
						}
						else
						{
							triangles.Add(v00); triangles.Add(v11); triangles.Add(v10);
							triangles.Add(v00); triangles.Add(v01); triangles.Add(v11);
						}
					}
				}
			}

			// Bed it in: flatten the underside and put the pivot a fifth of the way up, as RockMeshes does. A boxy stone's
			// underside is flat already; squashing it too would push its bottom rim out past its side as a lip.
			Bounds raw = mesh.Bounds;
			float floor = raw.min.y + raw.size.y * 0.12f;
			float pivot = raw.min.y + raw.size.y * 0.2f;
			float squash = shape.Sides >= 3 ? 1f : Mathf.Lerp(0.25f, 1f, Mathf.InverseLerp(2.5f, 5f, shape.SquareV));
			for (int i = 0; i < mesh.VertexCount; i++)
			{
				Vector3 p = mesh.Positions[i];
				if (p.y < floor)
				{
					p.y = floor - (floor - p.y) * squash;
				}
				p.y -= pivot;
				mesh.Positions[i] = p;
			}
			Relax(mesh);
			// Cube projection of the finished stone, each chart on its own axes.
			float inv = 1f / RockMeshes.TextureMetres;
			int perFace = (res + 1) * (res + 1);
			for (int i = 0; i < mesh.VertexCount; i++)
			{
				int f = i / perFace;
				Vector3 p = mesh.Positions[i];
				mesh.UVs[i] = new Vector2(Vector3.Dot(p, FaceU[f]) * inv, Vector3.Dot(p, FaceV[f]) * inv);
			}
			mesh.RecalculateNormals(true);
			mesh.RecalculateTangents();
			return mesh;
		}

		/// <summary>The most smoothing passes <see cref="Relax"/> makes; folds clear in one to three.</summary>
		public const int MaxRelaxPasses = 12;

		/// <summary>
		/// Smooths away the folds a coarse grid makes of a crease finer than its cells: wherever a triangle faces against
		/// its corners' normals (a groove one cell wide, a knob's neck, a plane meeting a scar at a grazing angle), its
		/// corners move halfway to the mean of their neighbours, and again until none does. Only those corners move, so
		/// a sound stone is untouched; a corner and its copies on the other charts move together, so the mesh stays
		/// closed. Deterministic: the same stone takes the same passes.
		/// </summary>
		private static void Relax(MeshBuilder mesh)
		{
			int count = mesh.VertexCount;
			var ids = new Dictionary<(float, float, float), int>();
			var weld = new int[count];
			var corners = new List<Vector3>();
			for (int i = 0; i < count; i++)
			{
				Vector3 p = mesh.Positions[i];
				var key = (p.x, p.y, p.z);
				if (!ids.TryGetValue(key, out int id))
				{
					id = corners.Count;
					ids[key] = id;
					corners.Add(p);
				}
				weld[i] = id;
			}
			List<int> list = mesh.Submeshes[0];
			var neighbours = new List<int>[corners.Count];
			for (int k = 0; k < neighbours.Length; k++)
			{
				neighbours[k] = new List<int>(6);
			}
			for (int t = 0; t + 2 < list.Count; t += 3)
			{
				for (int e = 0; e < 3; e++)
				{
					int a = weld[list[t + e]], b = weld[list[t + (e + 1) % 3]];
					if (a != b && !neighbours[a].Contains(b))
					{
						neighbours[a].Add(b);
						neighbours[b].Add(a);
					}
				}
			}
			var bad = new HashSet<int>();
			var order = new List<int>();
			for (int pass = 0; pass < MaxRelaxPasses; pass++)
			{
				mesh.RecalculateNormals(true);
				bad.Clear();
				order.Clear();
				for (int t = 0; t + 2 < list.Count; t += 3)
				{
					int a = list[t], b = list[t + 1], c = list[t + 2];
					Vector3 face = Vector3.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
					if (face.sqrMagnitude >= 1e-14f && Vector3.Dot(face, mesh.Normals[a] + mesh.Normals[b] + mesh.Normals[c]) > 0f)
					{
						continue;
					}
					foreach (int v in new[] { weld[a], weld[b], weld[c] })
					{
						if (bad.Add(v))
						{
							order.Add(v);
						}
					}
				}
				if (order.Count == 0)
				{
					return;
				}
				// Every new place from the old ones, in the order found, so the pass is the same on every machine.
				var moved = new Vector3[order.Count];
				for (int k = 0; k < order.Count; k++)
				{
					Vector3 mean = Vector3.zero;
					foreach (int n in neighbours[order[k]])
					{
						mean += corners[n];
					}
					mean /= Mathf.Max(1, neighbours[order[k]].Count);
					moved[k] = (corners[order[k]] + mean) * 0.5f;
				}
				for (int k = 0; k < order.Count; k++)
				{
					corners[order[k]] = moved[k];
				}
				for (int i = 0; i < count; i++)
				{
					mesh.Positions[i] = corners[weld[i]];
				}
			}
		}

		/// <summary>
		/// A detail mesh's group of stones: the first in the middle, the rest round it on a golden-angle spiral, each a
		/// little bigger or smaller and turned its own way, all on the ground.
		/// </summary>
		public static MeshBuilder BuildGroup(IReadOnlyList<StonePiece> pieces, int seed)
		{
			var result = new MeshBuilder(1);
			if (pieces == null || pieces.Count == 0)
			{
				return result;
			}
			var rng = new DeterministicRNG(ProceduralNoise.SeedFor(pieces[0].Shape.Name + "/group", seed));
			float firstRadius = 0f;
			for (int k = 0; k < pieces.Count; k++)
			{
				StoneShape one = pieces[k].Shape;
				float size = k == 0 ? 1f : rng.Range(0.8f, 1.15f);
				one.Size *= size;
				MeshBuilder stone = Build(in one, pieces[k].Resolution, seed);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				float footRadius = 0.5f * Mathf.Max(one.Size.x, one.Size.z);
				Vector3 offset = Vector3.zero;
				if (k == 0)
				{
					firstRadius = footRadius;
				}
				else
				{
					// Beside the first, just touching or a hand's breadth off: a scatter, never a heap.
					float angle = k * 2.3999632f + rng.Range(-0.4f, 0.4f);
					float distance = (firstRadius + footRadius) * rng.Range(0.85f, 1.35f);
					offset = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
				}
				Place(stone, yaw, offset);
				result.Append(stone);
			}
			result.RecalculateNormals(true);
			result.RecalculateTangents();
			return result;
		}

		/// <summary>Turns a stone about +Y (as Unity's AngleAxis does) and moves it, written out so no native call is made.</summary>
		private static void Place(MeshBuilder stone, float yaw, Vector3 offset)
		{
			float c = Mathf.Cos(yaw), s = Mathf.Sin(yaw);
			for (int i = 0; i < stone.VertexCount; i++)
			{
				Vector3 p = stone.Positions[i];
				stone.Positions[i] = new Vector3(p.x * c + p.z * s, p.y, -p.x * s + p.z * c) + offset;
			}
		}

		private static Vector3 CubePoint(int face, float a, float b)
		{
			// Written per component, so a point on a shared edge is bit-identical from both charts.
			switch (face)
			{
				case 0: return new Vector3(1f, b, a);
				case 1: return new Vector3(-1f, b, -a);
				case 2: return new Vector3(a, 1f, b);
				case 3: return new Vector3(a, -1f, -b);
				case 4: return new Vector3(-a, b, 1f);
				default: return new Vector3(a, b, -1f);
			}
		}

		/// <summary>
		/// The whole-stone bends after scaling: a taper toward the top and a lean. Both move a point within its own
		/// height and keep the stone's orientation (the horizontal scale stays positive), so the winding holds.
		/// </summary>
		private static Vector3 Deform(Vector3 p, in StoneShape shape, Vector3 half)
		{
			if (shape.Taper == 0f && shape.Lean == 0f)
			{
				return p;
			}
			float up = Mathf.Clamp01((p.y / Mathf.Max(1e-4f, half.y) + 1f) * 0.5f);
			float scale = 1f - Mathf.Clamp(shape.Taper, 0f, 0.8f) * up;
			return new Vector3(p.x * scale + shape.Lean * half.x * up * up, p.y, p.z * scale);
		}

		/// <summary>A soft minimum: the smaller of two, its corner rounded over <paramref name="k"/>.</summary>
		private static float SoftMin(float a, float b, float k)
		{
			if (k <= 1e-5f)
			{
				return Mathf.Min(a, b);
			}
			float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
			return Mathf.Lerp(b, a, h) - k * h * (1f - h);
		}

		private static float SoftMax(float a, float b, float k) => -SoftMin(-a, -b, k);

		/// <summary>A random unit direction whose height lies in [low, high]: features on the flanks and top, not underneath.</summary>
		private static Vector3 Direction(DeterministicRNG rng, float low, float high)
		{
			float y = rng.Range(Mathf.Max(-1f, low), Mathf.Min(1f, high));
			float a = rng.NextFloat() * Mathf.PI * 2f;
			float h = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
			return new Vector3(Mathf.Cos(a) * h, y, Mathf.Sin(a) * h);
		}

		/// <summary>
		/// A recipe's marks, drawn once from its seed in a fixed order (prism sides, planes, scars, knobs, pits, branches,
		/// beds), and the radius they give a direction.
		/// </summary>
		private sealed class Marks
		{
			private readonly StoneShape s;
			private readonly int seed;
			private readonly Vector2[] sideNormals;
			private readonly float[] sideDistances;
			private readonly Vector4[] planes;
			private readonly Vector4[] scoops;
			private readonly Vector4[] knobs;
			private readonly Vector4[] pits;
			private readonly Vector4[] arms;
			private readonly Vector3 bedAxis;
			private readonly float bedPhase;

			public Marks(in StoneShape shape, int seed)
			{
				s = shape;
				this.seed = seed;
				var rng = new DeterministicRNG(seed ^ 0x2f6b1c35);

				int sides = shape.Sides >= 3 ? shape.Sides : 0;
				sideNormals = new Vector2[sides];
				sideDistances = new float[sides];
				float turn = rng.NextFloat() * Mathf.PI * 2f;
				for (int k = 0; k < sides; k++)
				{
					float a = turn + (k + rng.Range(-0.5f, 0.5f) * shape.SideJitter) * Mathf.PI * 2f / sides;
					sideNormals[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
					sideDistances[k] = 1f + rng.Range(-1f, 1f) * shape.SideJitter * 0.5f;
				}

				planes = new Vector4[Mathf.Max(0, shape.Facets)];
				for (int k = 0; k < planes.Length; k++)
				{
					Vector3 n = Direction(rng, shape.FacetLow, shape.FacetHigh);
					float d = Mathf.Lerp(0.97f, 0.55f, Mathf.Clamp01(shape.FacetDepth)) * rng.Range(0.9f, 1.05f);
					planes[k] = new Vector4(n.x, n.y, n.z, d);
				}

				// A scar is a sphere taken out of the stone: its centre out along a direction, so that it cuts ScoopDepth in.
				scoops = new Vector4[Mathf.Max(0, shape.Scoops)];
				for (int k = 0; k < scoops.Length; k++)
				{
					Vector3 n = Direction(rng, -0.1f, 0.85f);
					float radius = shape.ScoopRadius * rng.Range(0.75f, 1.2f);
					float depth = shape.ScoopDepth * rng.Range(0.6f, 1.1f);
					Vector3 c = n * (1f + radius - depth);
					scoops[k] = new Vector4(c.x, c.y, c.z, radius);
				}

				knobs = Bumps(rng, shape.Knobs, -0.35f, 1f, shape.KnobWidth, shape.KnobHeight);
				pits = Bumps(rng, shape.Pits, -0.25f, 1f, shape.PitWidth, shape.PitDepth);

				arms = new Vector4[Mathf.Max(0, shape.Arms)];
				float armTurn = rng.NextFloat() * Mathf.PI * 2f;
				for (int k = 0; k < arms.Length; k++)
				{
					float a = armTurn + (k + rng.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / arms.Length;
					// The first stands straight up; the rest fan out from the rise to near vertical.
					float y = k == 0 ? 1f : rng.Range(Mathf.Clamp(shape.ArmRise, 0f, 0.95f), 0.95f);
					float h = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
					arms[k] = new Vector4(Mathf.Cos(a) * h, y, Mathf.Sin(a) * h, shape.ArmLength * rng.Range(0.75f, 1.1f));
				}

				// The beds' normal: up, tilted by the dip toward a random bearing.
				float bearing = rng.NextFloat() * Mathf.PI * 2f;
				float dip = shape.BedDip * Mathf.Deg2Rad;
				bedAxis = new Vector3(Mathf.Sin(dip) * Mathf.Cos(bearing), Mathf.Cos(dip), Mathf.Sin(dip) * Mathf.Sin(bearing));
				bedPhase = rng.NextFloat();
			}

			private static Vector4[] Bumps(DeterministicRNG rng, int count, float low, float high, float width, float height)
			{
				var bumps = new Vector4[Mathf.Max(0, count)];
				for (int k = 0; k < bumps.Length; k++)
				{
					Vector3 n = Direction(rng, low, high);
					// w: the cosine of the bump's angular radius, so a direction is inside it when dot > w.
					float w = Mathf.Cos(Mathf.Clamp(width * rng.Range(0.7f, 1.3f), 0.02f, 1.4f));
					bumps[k] = new Vector4(n.x, n.y, n.z, w);
				}
				return bumps;
			}

			/// <summary>How far the stone's surface is along a unit direction, in normalised space.</summary>
			public float Radius(Vector3 d)
			{
				float r = Body(d) * (s.Core > 0f ? s.Core : 1f);

				// Broad lumps, fracture ridges and grain, on the direction, so they never stretch with the stone.
				r *= 1f + s.Lump * 0.22f * ProceduralNoise.Fbm3(d * 1.6f, 4, 0.5f, seed)
					+ s.Ridge * 0.08f * (ProceduralNoise.Ridged3(d * 3.1f, 3, 0.5f, seed + 17) - 0.5f)
					+ s.Grain * 0.03f * ProceduralNoise.Fbm3(d * 7f, 2, 0.5f, seed + 29);

				for (int k = 0; k < knobs.Length; k++)
				{
					float t = Inside(d, knobs[k]);
					if (t > 0f)
					{
						// A dome: steep at its foot, round on top.
						r += s.KnobHeight * t * (2f - t);
					}
				}
				for (int k = 0; k < pits.Length; k++)
				{
					float t = Inside(d, pits[k]);
					if (t > 0f)
					{
						// A bowl: soft lip, flat floor.
						r -= s.PitDepth * t * t * (3f - 2f * t);
					}
				}
				for (int k = 0; k < arms.Length; k++)
				{
					var axis = new Vector3(arms[k].x, arms[k].y, arms[k].z);
					float cos = Vector3.Dot(d, axis);
					if (cos <= 0.05f)
					{
						continue;
					}
					float sin = Mathf.Sqrt(Mathf.Max(1e-6f, 1f - cos * cos));
					// A cylinder from the centre along the axis: its end cap or its wall, whichever the ray meets first.
					float arm = Mathf.Min(arms[k].w / cos, s.ArmWidth / sin);
					r = SoftMax(r, arm, 0.12f);
				}

				if (s.Beds > 0f && s.BedDepth > 0f)
				{
					float c = Vector3.Dot(d * r, bedAxis) * s.Beds * 0.5f + bedPhase;
					if (s.Meander > 0f)
					{
						c += s.Meander * s.Beds * 0.25f * ProceduralNoise.Fbm3(d * 2.2f, 3, 0.5f, seed + 41);
					}
					float g = 0.5f + 0.5f * Mathf.Cos(c * Mathf.PI * 2f);
					g *= g; g *= g; // ^4: grooves between broader beds, no narrower than a boulder's grid can draw
					r *= 1f - s.BedDepth * g;
				}

				if (s.Flutes > 0 && s.FluteDepth > 0f)
				{
					// Up the flanks, and gone again at the crown, where every flute would meet in a point.
					float flank = Mathf.Sqrt(d.x * d.x + d.z * d.z);
					float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.2f, 0.65f, d.y)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.45f, flank));
					if (w > 0f)
					{
						float az = Mathf.Atan2(d.z, d.x);
						// A plain wave round the stone: a sharper groove is narrower than a boulder's grid and folds it.
						float g = 0.5f + 0.5f * Mathf.Cos(s.Flutes * az + 2.2f * ProceduralNoise.Fbm3(d * 2f, 2, 0.5f, seed + 53));
						r *= 1f - s.FluteDepth * g * w;
					}
				}

				// Cuts last: a plane or a scar wins wherever it reaches in further than the rest.
				for (int k = 0; k < planes.Length; k++)
				{
					float along = d.x * planes[k].x + d.y * planes[k].y + d.z * planes[k].z;
					if (along > 1e-3f)
					{
						r = SoftMin(r, planes[k].w / along, s.Edge);
					}
				}
				for (int k = 0; k < scoops.Length; k++)
				{
					var c = new Vector3(scoops[k].x, scoops[k].y, scoops[k].z);
					float b = Vector3.Dot(d, c);
					float disc = b * b - (c.sqrMagnitude - scoops[k].w * scoops[k].w);
					if (b > 0f && disc > 0f)
					{
						float entry = b - Mathf.Sqrt(disc);
						if (entry > 0f)
						{
							r = SoftMin(r, entry, s.Edge * 0.5f);
						}
					}
				}
				return Mathf.Max(r, 0.15f);
			}

			/// <summary>0 at a bump's edge … 1 at its centre; 0 outside it.</summary>
			private static float Inside(Vector3 d, Vector4 bump)
			{
				float dot = d.x * bump.x + d.y * bump.y + d.z * bump.z;
				return dot > bump.w ? Mathf.Clamp01((dot - bump.w) / Mathf.Max(1e-4f, 1f - bump.w)) : 0f;
			}

			/// <summary>The body: a polygonal prism, or a superellipsoid.</summary>
			private float Body(Vector3 d)
			{
				if (sideNormals.Length > 0)
				{
					float r = Mathf.Abs(d.y) > 1e-4f ? 1f / Mathf.Abs(d.y) : 1e4f;
					for (int k = 0; k < sideNormals.Length; k++)
					{
						float along = d.x * sideNormals[k].x + d.z * sideNormals[k].y;
						if (along > 1e-4f)
						{
							r = SoftMin(r, sideDistances[k] / along, s.Edge);
						}
					}
					return Mathf.Min(r, 4f);
				}
				float e = Mathf.Max(1.2f, s.Square), ev = Mathf.Max(1.2f, s.SquareV);
				float across = Mathf.Pow(Mathf.Pow(Mathf.Abs(d.x), e) + Mathf.Pow(Mathf.Abs(d.z), e), 1f / e);
				float f = Mathf.Pow(across, ev) + Mathf.Pow(Mathf.Abs(d.y), ev);
				return Mathf.Pow(Mathf.Max(1e-6f, f), -1f / ev);
			}
		}

		// ── Measures ──────────────────────────────────────────────────

		/// <summary>The figures of one stone mesh (<see cref="StoneMeasure"/>), from its welded edges.</summary>
		public static StoneMeasure Measure(MeshBuilder mesh)
		{
			Bounds b = mesh.Bounds;
			float length = Mathf.Max(b.size.x, b.size.z), breadth = Mathf.Max(1e-5f, Mathf.Min(b.size.x, b.size.z));
			var measure = new StoneMeasure
			{
				Flatness = b.size.y / Mathf.Max(1e-5f, Mathf.Sqrt(b.size.x * b.size.z)),
				Elongation = length / breadth,
			};

			// Weld by position (the cube-sphere splits its chart edges), then pair each edge's two faces.
			var ids = new Dictionary<(long, long, long), int>();
			var weld = new int[mesh.VertexCount];
			for (int i = 0; i < mesh.VertexCount; i++)
			{
				Vector3 p = mesh.Positions[i];
				var key = ((long)Mathf.Round(p.x * 1e5f), (long)Mathf.Round(p.y * 1e5f), (long)Mathf.Round(p.z * 1e5f));
				if (!ids.TryGetValue(key, out int id))
				{
					id = ids.Count;
					ids[key] = id;
				}
				weld[i] = id;
			}
			var edges = new Dictionary<long, (int face, int opposite)>();
			var faceNormals = new List<Vector3>();
			var faceCorners = new List<Vector3>();
			float total = 0f, sharp = 0f, folded = 0f;
			List<int> list = mesh.Submeshes[0];
			for (int t = 0; t + 2 < list.Count; t += 3)
			{
				int face = t / 3;
				Vector3 a = mesh.Positions[list[t]], bb = mesh.Positions[list[t + 1]], c = mesh.Positions[list[t + 2]];
				Vector3 n = Vector3.Cross(bb - a, c - a);
				faceNormals.Add(n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.zero);
				faceCorners.Add(a);
				for (int e = 0; e < 3; e++)
				{
					int i0 = list[t + e], i1 = list[t + (e + 1) % 3], i2 = list[t + (e + 2) % 3];
					int w0 = weld[i0], w1 = weld[i1];
					if (w0 == w1)
					{
						continue;
					}
					long key = w0 < w1 ? ((long)w0 << 32) | (uint)w1 : ((long)w1 << 32) | (uint)w0;
					if (!edges.TryGetValue(key, out var other))
					{
						edges[key] = (face, i2);
						continue;
					}
					edges.Remove(key);
					Vector3 n0 = faceNormals[other.face], n1 = faceNormals[face];
					if (n0 == Vector3.zero || n1 == Vector3.zero)
					{
						continue;
					}
					float len = Vector3.Distance(mesh.Positions[i0], mesh.Positions[i1]);
					float angle = Vector3.Angle(n0, n1);
					total += len;
					if (angle >= 30f)
					{
						sharp += len;
					}
					// Folded inward when this face's far corner lies above the other face's plane.
					bool concave = Vector3.Dot(mesh.Positions[i2] - faceCorners[other.face], n0) > 0f;
					if (concave && angle >= 8f)
					{
						folded += len;
					}
				}
			}
			if (total > 0f)
			{
				measure.Angularity = sharp / total;
				measure.Concavity = folded / total;
			}
			return measure;
		}
	}
}
#endif
