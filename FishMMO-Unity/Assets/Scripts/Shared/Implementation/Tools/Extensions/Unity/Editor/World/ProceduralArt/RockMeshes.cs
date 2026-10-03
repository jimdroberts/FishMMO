#if UNITY_EDITOR
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The proportions and character of one rock shape.</summary>
	public struct RockShape
	{
		public string Name;
		/// <summary>Overall size in metres (the longest horizontal extent, roughly).</summary>
		public float Size;
		/// <summary>Stretch per axis before noise: flat slabs, tall spires, round boulders.</summary>
		public Vector3 Proportions;
		/// <summary>Relative depth of the lumpy noise, 0..1.</summary>
		public float Lumpiness;
		/// <summary>How many cleavage planes cut flat faces into it.</summary>
		public int Facets;
		/// <summary>How far the planes cut in, 0 barely … 1 deep.</summary>
		public float FacetDepth;
	}

	/// <summary>
	/// Rocks: a cube-sphere displaced by 3D noise and cut by cleavage planes, at any resolution
	/// from the same shape function, so every level of detail has the same silhouette.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why not WorldEditor's rock generator.</b> Audited and rejected rather than ported: it
	/// produced no UVs at all (its weld keyed on position plus UV, and its rocks had none), so no
	/// texture or normal map could be applied; its convex hull wound about half the faces it added
	/// inward; its "flat shaded" option silently produced smooth normals; its noise was
	/// <c>Mathf.PerlinNoise</c> offset by <c>seed × 1000</c>, which collapses to banding at large
	/// seeds; and every intermediate mesh but the last leaked. The icosphere-subdivision idea is
	/// sound, but an icosphere has no seam-free UV layout, which is why this uses a cube-sphere:
	/// six square charts, each a clean UV grid.
	/// </para>
	/// <para>
	/// <b>A radius function, so no cracks.</b> Every vertex's position is
	/// <c>direction × radius(direction)</c>. Vertices duplicated along the cube's edges (one per
	/// chart, for their UVs) are computed from bit-identical directions, so they land on the same
	/// point and the welded normal pass shades across them. Displacing along each split vertex's
	/// own normal — what WorldEditor's noise deformer did — opens a crack at every seam.
	/// </para>
	/// <para>
	/// <b>Sits on the ground.</b> The underside is flattened and the pivot placed a fifth of the way
	/// up, so on a slope a rock is bedded in rather than balanced on a point.
	/// </para>
	/// </remarks>
	public static class RockMeshes
	{
		/// <summary>Metres of rock surface one tile of its texture covers.</summary>
		public const float TextureMetres = 2f;

		private static readonly Vector3[] FaceNormal = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
		private static readonly Vector3[] FaceU = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.left, Vector3.right };
		private static readonly Vector3[] FaceV = { Vector3.up, Vector3.up, Vector3.forward, Vector3.back, Vector3.up, Vector3.up };

		/// <summary>One rock at a resolution (grid cells along each cube face's edge); 6·2·res² triangles.</summary>
		public static MeshBuilder Build(in RockShape shape, int resolution, int seed)
		{
			int res = Mathf.Max(1, resolution);
			int s = ProceduralNoise.SeedFor(shape.Name, seed);
			RockShape sh = shape;
			Vector4[] planes = Planes(in sh, s);

			var mesh = new MeshBuilder(1);
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
						Vector3 cube = CubePoint(f, a, b);
						Vector3 p = Surface(cube.normalized, in sh, planes, s);
						float chart = sh.Size * 1.6f / TextureMetres;
						mesh.AddVertex(p, Vector3.zero, new Vector2((float)i / res * chart, (float)j / res * chart), new Color32(255, 255, 255, 0));
					}
				}
				Vector3 outward = FaceNormal[f];
				for (int j = 0; j < res; j++)
				{
					for (int i = 0; i < res; i++)
					{
						int v00 = first + j * (res + 1) + i;
						int v10 = v00 + 1, v01 = v00 + res + 1, v11 = v01 + 1;
						// Face each triangle away from the rock's centre, judged per triangle: the
						// cleavage planes can tilt a cell far from its cube face's own direction.
						Vector3 centre = (mesh.Positions[v00] + mesh.Positions[v11]) * 0.5f;
						Vector3 facing = centre.sqrMagnitude > 1e-8f ? centre : outward;
						mesh.AddTriangle(0, v00, v10, v11, facing);
						mesh.AddTriangle(0, v00, v11, v01, facing);
					}
				}
			}

			// Bed it in: flatten the underside and put the pivot a fifth of the way up.
			Bounds raw = mesh.Bounds;
			float floor = raw.min.y + raw.size.y * 0.12f;
			float pivot = raw.min.y + raw.size.y * 0.2f;
			for (int i = 0; i < mesh.VertexCount; i++)
			{
				Vector3 p = mesh.Positions[i];
				if (p.y < floor)
				{
					p.y = floor - (floor - p.y) * 0.25f;
				}
				p.y -= pivot;
				mesh.Positions[i] = p;
			}
			mesh.RecalculateNormals(true);
			mesh.RecalculateTangents();
			return mesh;
		}

		/// <summary>Several small rocks in one mesh: a pebble scatter for the detail channel.</summary>
		public static MeshBuilder BuildCluster(in RockShape shape, int count, int resolution, float spread, int seed)
		{
			var result = new MeshBuilder(1);
			var rng = new DeterministicRNG(ProceduralNoise.SeedFor(shape.Name + "/cluster", seed));
			for (int k = 0; k < count; k++)
			{
				RockShape one = shape;
				one.Name = shape.Name + "/" + k;
				one.Size = shape.Size * rng.Range(0.6f, 1.2f);
				MeshBuilder rock = Build(in one, resolution, seed);
				float angle = rng.NextFloat() * Mathf.PI * 2f;
				float radius = k == 0 ? 0f : spread * rng.Range(0.4f, 1f);
				Matrix4x4 m = Matrix4x4.TRS(new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius),
					Quaternion.Euler(0f, rng.NextFloat() * 360f, 0f), Vector3.one);
				rock.Transform(m);
				result.Append(rock);
			}
			return result;
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

		/// <summary>Random cleavage planes (xyz unit normal, w distance), deterministic per rock.</summary>
		private static Vector4[] Planes(in RockShape shape, int seed)
		{
			var rng = new DeterministicRNG(seed ^ 0x5bd1e995);
			var planes = new Vector4[Mathf.Max(0, shape.Facets)];
			for (int i = 0; i < planes.Length; i++)
			{
				// Mostly sideways and upward: real boulders are cleaved on their exposed faces, and a
				// plane straight down would only flatten what the ground hides anyway.
				float y = rng.Range(-0.2f, 0.9f);
				float a = rng.NextFloat() * Mathf.PI * 2f;
				float h = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
				var n = new Vector3(Mathf.Cos(a) * h, y, Mathf.Sin(a) * h);
				float d = Mathf.Lerp(0.95f, 0.6f, Mathf.Clamp01(shape.FacetDepth)) * rng.Range(0.9f, 1.05f);
				planes[i] = new Vector4(n.x, n.y, n.z, d);
			}
			return planes;
		}

		private static Vector3 Surface(Vector3 dir, in RockShape shape, Vector4[] planes, int seed)
		{
			// Stretch the direction field first, then measure: proportions belong to the shape, the
			// noise to its surface, and the noise should not be stretched along with it.
			float lump = ProceduralNoise.Fbm3(dir * 1.6f, 4, 0.5f, seed) * 0.22f * shape.Lumpiness;
			float ridge = (ProceduralNoise.Ridged3(dir * 3.1f, 3, 0.5f, seed + 17) - 0.5f) * 0.08f * shape.Lumpiness;
			float r = 1f + lump + ridge;
			// Cleavage: where a plane cuts in closer than the noise surface, the plane wins, by a
			// soft minimum so the edge between two faces is chipped rather than razor sharp.
			for (int i = 0; i < planes.Length; i++)
			{
				var n = new Vector3(planes[i].x, planes[i].y, planes[i].z);
				float along = Vector3.Dot(dir, n);
				if (along <= 1e-3f)
				{
					continue;
				}
				float limit = planes[i].w / along;
				r = SoftMin(r, limit, 0.06f);
			}
			Vector3 p = dir * (r * 0.5f * shape.Size);
			return Vector3.Scale(p, shape.Proportions);
		}

		private static float SoftMin(float a, float b, float k)
		{
			float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
			return Mathf.Lerp(b, a, h) - k * h * (1f - h);
		}
	}
}
#endif
