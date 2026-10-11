#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Rock geometry that breaks the way each rock type breaks: rounded corestones and tors,
	/// bedded blocks with ledges, cleaved slab stacks, conchoidal chunks, karst pinnacles and
	/// pavements, fairy chimneys, basalt columns and scree heaps. Pure functions of a type, a
	/// shape, a resolution, a variant and a seed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Three engines, every one closed and crack-free by construction.</b>
	/// <list type="bullet">
	/// <item><b>Implicit field, ray-cast from a centre</b> (boulders, tors, split rocks, caps,
	/// rounded fragments). <see cref="RockMeshes"/>' cube-sphere with a radius function,
	/// generalised: the radius along each direction is where a signed field — a superellipsoid,
	/// displaced by lumps, foliation ribs, exfoliation sheets, pits, flutes and clasts, then cut by
	/// fracture planes — first turns positive, found by marching and bisection. Vertices shared by
	/// two charts are cast along bit-identical directions, so they land on the same point; the
	/// surface is star-shaped about the ray origin, so it is one closed sheet whatever the field
	/// does. Features are functions of the 3D point, not the direction, so a band is a plane
	/// through the rock and a flute runs truly downhill.</item>
	/// <item><b>Lathe</b> (beds, clints, pinnacles, chimney stems). A stack of rings round a
	/// vertical axis, each ring a plan outline scaled and inset, closed by two capped fans. Beds
	/// are built one lathe per bed and stacked with a sliver of overlap, which is the only way to
	/// get a crisp ledge: a single radius function sampled on a regular grid steps a ledge into a
	/// staircase of triangles at every level of detail but the finest. Each bed is closed; the
	/// stack is a set of closed shells.</item>
	/// <item><b>Convex polytope</b> (faceted chunks, slabs, columns, angular fragments). Each face
	/// is its plane's square clipped by every other plane, in double precision; corners are welded
	/// across faces, and the result is accepted only if every directed edge has exactly one
	/// reverse twin and V − E + F = 2. A plane set that fails (four planes nearly through one
	/// point) is redrawn from the same random stream, so the outcome depends only on the seed.
	/// Edges are subdivided once, from the lower-numbered corner, and both faces read the same
	/// points, so a dished face never opens a crack against its flat neighbour. Faces are dished
	/// (conchoidal scars, cross-joint cups) by displacing concentric rings that run from the
	/// boundary, where the displacement is zero, to an off-centre point of impact.</item>
	/// </list>
	/// </para>
	/// <para>
	/// <b>Winding from a reference, not from the result.</b> <see cref="RockMeshes"/> decides each
	/// triangle's facing from where it ended up, which a deep pit can fool. Here the order is
	/// decided on the undeformed reference (the unit sphere, the unit cylinder, the flat face) where
	/// outward is unambiguous, and kept when the vertices move, so the surface stays consistently
	/// oriented; <see cref="MeshBuilder.Validate"/> then catches any triangle the deformation
	/// folded over, instead of the winding hiding it.
	/// </para>
	/// <para>
	/// <b>Same silhouette at every level.</b> All structure — beds, plates, columns, planes,
	/// fragments, their sizes and places — is drawn from per-part random streams before anything
	/// depends on the resolution, and a polytope's acceptance does not depend on how finely it is
	/// later tessellated. The resolution only changes sampling: rays per chart, segments per ring,
	/// edge subdivisions and dish rings. Each level is then fitted to the shape's declared size, so
	/// a coarse level's sampled-in corners do not make it shrink when it swaps in.
	/// </para>
	/// <para>
	/// <b>Bedded pivot, metres UVs.</b> Every formation is centred on its pivot and sunk so a fixed
	/// fraction of its height is below it (most of a talus block above ground, most of a pavement
	/// clint below). Lathes and polytopes carry UVs in metres over
	/// <see cref="RockMeshes.TextureMetres"/>, with v up the rock so a strata texture's bands lie
	/// along the beds; implicit charts carry the cube-sphere's grid UVs at about the same density.
	/// </para>
	/// </remarks>
	public static class RockFormations
	{
		/// <summary>Most triangles a formation may have at each of the boulder levels of detail.</summary>
		public static readonly int[] TriangleBudget = { 4000, 1600, 800 };

		/// <summary>
		/// A lighter set of resolutions for scattered fields, in place of
		/// <see cref="ProceduralArtCatalogue.BoulderResolution"/>: LOD0 at 7 instead of 10 roughly
		/// halves a rounded boulder (3888 → 2028 triangles for the pitted, clasted and foliated
		/// types, 2352 → 1200 for the rest, the legacy boulder's own count) and drops beds and faceted
		/// rocks to their middle tessellation. Same silhouettes; fewer pits, ribs and ledge bevels
		/// are resolved up close.
		/// </summary>
		public static readonly int[] LightResolution = { 7, 4, 2 };

		/// <summary>The fraction of its height every formation of a kind is sunk below its pivot.</summary>
		public static float Burial(FormationKind kind)
		{
			switch (kind)
			{
				case FormationKind.Boulder: return 0.15f;
				case FormationKind.Tor: return 0.05f;
				case FormationKind.Perched: return 0.05f;
				case FormationKind.Split: return 0.15f;
				case FormationKind.Bedded: return 0.1f;
				case FormationKind.Ledges: return 0.08f;
				case FormationKind.Pedestal: return 0.05f;
				case FormationKind.Pavement: return 0.35f;
				case FormationKind.Pinnacle: return 0.06f;
				case FormationKind.Chimney: return 0.04f;
				case FormationKind.Faceted: return 0.12f;
				case FormationKind.SlabStack: return 0.1f;
				case FormationKind.Standing: return 0.15f;
				case FormationKind.Columns: return 0.05f;
				case FormationKind.FallenColumns: return 0.08f;
				case FormationKind.Scree: return 0.08f;
				case FormationKind.CrystalCluster: return 0.15f;
				case FormationKind.Cone: return 0.06f;
				case FormationKind.Terrace: return 0.12f;
				case FormationKind.CraterRim: return 0.2f;
				case FormationKind.PolygonRidges: return 0.45f;
				case FormationKind.Mound: return 0.05f;
				case FormationKind.Stalagmite: return 0.05f;
				case FormationKind.Flowstone: return 0.12f;
				case FormationKind.Knob: return 0.06f;
				case FormationKind.Ventifact: return 0.15f;
				case FormationKind.Sastrugi: return 0.25f;
				case FormationKind.Penitentes: return 0.08f;
				case FormationKind.Lobe: return 0.2f;
				case FormationKind.Table: return 0.08f;
				case FormationKind.RubbleRidge: return 0.12f;
				default: return 0.12f;
			}
		}

		internal static readonly Color32 White = new Color32(255, 255, 255, 0);
		internal const float Tau = Mathf.PI * 2f;

		/// <summary>The seed a variant of a shape of a type is built from.</summary>
		public static int VariantSeed(string type, string shape, int variant, int seed)
		{
			return ProceduralNoise.SeedFor(type + "/" + shape + "/" + variant, seed);
		}

		/// <summary>One variant of one shape of a rock type, at a resolution from <see cref="ProceduralArtCatalogue.BoulderResolution"/>.</summary>
		public static MeshBuilder Build(in RockType type, in FormationShape shape, int resolution, int variant, int seed)
		{
			switch (shape.Kind)
			{
				case FormationKind.Columns:
				case FormationKind.FallenColumns:
					return BuildColumns(in type, in shape, resolution, variant, seed);
				case FormationKind.Scree:
					return BuildScree(in type, in shape, resolution, variant, seed);
			}
			int res = Mathf.Max(1, resolution);
			int s = VariantSeed(type.Name, shape.Name, variant, seed);
			MeshBuilder mesh;
			switch (shape.Kind)
			{
				case FormationKind.Tor: mesh = Tor(in type, in shape, res, s, false); break;
				case FormationKind.Perched: mesh = Tor(in type, in shape, res, s, true); break;
				case FormationKind.Split: mesh = Split(in type, in shape, res, s); break;
				case FormationKind.Bedded:
				case FormationKind.Ledges:
				case FormationKind.Pedestal: mesh = Bedded(in type, in shape, res, s); break;
				case FormationKind.Pavement: mesh = Pavement(in type, in shape, res, s); break;
				case FormationKind.Pinnacle: mesh = Pinnacle(in type, in shape, res, s); break;
				case FormationKind.Chimney: mesh = Chimney(in type, in shape, res, s); break;
				case FormationKind.Faceted: mesh = Faceted(in type, in shape, res, s); break;
				case FormationKind.SlabStack: mesh = SlabStack(in type, in shape, res, s, false); break;
				case FormationKind.Standing: mesh = SlabStack(in type, in shape, res, s, true); break;
				case FormationKind.CrystalCluster: mesh = CrystalFormations.CrystalCluster(in type, in shape, res, s); break;
				case FormationKind.Cone: mesh = CrystalFormations.Cone(in type, in shape, res, s); break;
				case FormationKind.Terrace: mesh = CrystalFormations.Terrace(in type, in shape, res, s); break;
				case FormationKind.CraterRim: mesh = CrystalFormations.CraterRim(in type, in shape, res, s); break;
				case FormationKind.PolygonRidges: mesh = CrystalFormations.PolygonRidges(in type, in shape, res, s); break;
				case FormationKind.Mound: mesh = CrystalFormations.Mound(in type, in shape, res, s); break;
				case FormationKind.Stalagmite: mesh = CrystalFormations.Stalagmites(in type, in shape, res, s); break;
				case FormationKind.Flowstone: mesh = CrystalFormations.Flowstone(in type, in shape, res, s); break;
				case FormationKind.Knob: mesh = CrystalFormations.Knob(in type, in shape, res, s); break;
				case FormationKind.Ventifact: mesh = CrystalFormations.Ventifact(in type, in shape, res, s); break;
				case FormationKind.Sastrugi: mesh = CrystalFormations.Sastrugi(in type, in shape, res, s); break;
				case FormationKind.Penitentes: mesh = CrystalFormations.Penitentes(in type, in shape, res, s); break;
				case FormationKind.Lobe: mesh = CrystalFormations.Lobe(in type, in shape, res, s); break;
				case FormationKind.Table: mesh = CrystalFormations.Table(in type, in shape, res, s); break;
				case FormationKind.RubbleRidge: mesh = IceMeshes.BuildGroundedRidge(shape.Size, shape.Height * (1f - Burial(shape.Kind)), shape.Height * Burial(shape.Kind), shape.Count, res, s); break;
				default: mesh = Boulder(in type, in shape, res, s); break;
			}
			return Finish(mesh, in shape, Burial(shape.Kind));
		}

		/// <summary>
		/// Columnar jointing: a cluster of five- to seven-sided prisms of varied height with broken,
		/// tilted, cupped tops (<see cref="FormationKind.Columns"/>), or broken columns lying where
		/// they fell (<see cref="FormationKind.FallenColumns"/>).
		/// </summary>
		/// <remarks>
		/// The cross-sections are the Voronoi cells of a jittered hexagonal lattice, which is what
		/// real columns approximate: contraction cracks meet at about 120°, so most columns are
		/// hexagons and the defects are pentagons and heptagons. Each column is the intersection of
		/// the half-spaces beyond its neighbours' bisectors (moved apart by the joint gap), the
		/// ground, and one or two top planes; the lattice runs two rings beyond the kept columns, so
		/// every kept cell is bounded by neighbours and none is cut by an artificial boundary.
		/// </remarks>
		public static MeshBuilder BuildColumns(in RockType type, in FormationShape shape, int resolution, int variant, int seed)
		{
			int res = Mathf.Max(1, resolution);
			int s = VariantSeed(type.Name, shape.Name, variant, seed);
			MeshBuilder mesh = shape.Kind == FormationKind.FallenColumns ? FallenColumns(in type, in shape, res, s) : Columns(in type, in shape, res, s);
			return Finish(mesh, in shape, Burial(shape.Kind));
		}

		/// <summary>
		/// A talus heap: fragments of the type's own fracture habit (angular blocks, platy chips or
		/// rounded lumps), graded so the largest roll to the foot and the smallest rest near the
		/// top, over a low core mound that keeps the heap from reading hollow.
		/// </summary>
		public static MeshBuilder BuildScree(in RockType type, in FormationShape shape, int resolution, int variant, int seed)
		{
			int res = Mathf.Max(1, resolution);
			int s = VariantSeed(type.Name, shape.Name, variant, seed);
			return Finish(Scree(in type, in shape, res, s), in shape, Burial(FormationKind.Scree));
		}

		// ── Finishing ────────────────────────────────────────────────

		/// <summary>Centres it, sinks it, fits it to the declared size and recomputes tangents.</summary>
		internal static MeshBuilder Finish(MeshBuilder mesh, in FormationShape shape, float burial)
		{
			Bounds b = mesh.Bounds;
			float height = Mathf.Max(1e-4f, b.size.y);
			float horizontal = Mathf.Max(1e-4f, Mathf.Max(b.size.x, b.size.z));
			var shift = new Vector3(-b.center.x, -(b.min.y + burial * height), -b.center.z);
			float sh = shape.Size / horizontal, sv = shape.Height / height;
			mesh.Transform(Matrix4x4.Scale(new Vector3(sh, sv, sh)) * Matrix4x4.Translate(shift));
			mesh.RecalculateTangents();
			return mesh;
		}

		// ── Small helpers ────────────────────────────────────────────

		internal static int Level(int res) => res >= 8 ? 2 : res >= 4 ? 1 : 0;

		/// <summary>
		/// Cube-sphere cells per chart edge for a lone rock: a little finer than the legacy
		/// boulders, and finer again where the style has relief the mesh must carry (pits, clasts,
		/// foliation ribs), which would otherwise alias away between vertices.
		/// </summary>
		internal static int CubeRes(int res, in RockGeometryStyle st)
		{
			bool detailed = (st.Pits.Spacing > 0f && st.Pits.Depth > 0f) || st.Clasts.Size > 0f || st.Foliation.Relief > 0f;
			return Mathf.Max(2, Mathf.RoundToInt(res * (detailed ? 1.8f : 1.4f)));
		}

		/// <summary>A real smoothstep (Unity's <c>Mathf.SmoothStep</c> interpolates between its first two arguments instead).</summary>
		internal static float Smooth(float e0, float e1, float x)
		{
			float t = Mathf.Clamp01((x - e0) / (e1 - e0));
			return t * t * (3f - 2f * t);
		}

		internal static float SMin(float a, float b, float k)
		{
			if (k <= 1e-6f)
			{
				return Mathf.Min(a, b);
			}
			float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
			return Mathf.Lerp(b, a, h) - k * h * (1f - h);
		}

		internal static float SMax(float a, float b, float k) => -SMin(-a, -b, k);

		internal static float Frac(float x) => x - Mathf.Floor(x);

		internal static float Range(DeterministicRNG rng, Vector2 range) => rng.Range(range.x, Mathf.Max(range.x, range.y));

		/// <summary>A rotation by the right-hand rule about <paramref name="axis"/>: v' = v cos θ + (k × v) sin θ + k (k·v)(1 − cos θ).</summary>
		internal static Matrix4x4 Rotation(Vector3 axis, float degrees)
		{
			axis.Normalize();
			float a = degrees * Mathf.Deg2Rad;
			float c = Mathf.Cos(a), s = Mathf.Sin(a), t = 1f - c;
			float x = axis.x, y = axis.y, z = axis.z;
			Matrix4x4 m = Matrix4x4.identity;
			m.m00 = t * x * x + c; m.m01 = t * x * y - s * z; m.m02 = t * x * z + s * y;
			m.m10 = t * x * y + s * z; m.m11 = t * y * y + c; m.m12 = t * y * z - s * x;
			m.m20 = t * x * z - s * y; m.m21 = t * y * z + s * x; m.m22 = t * z * z + c;
			return m;
		}

		/// <summary>A unit direction tilted <paramref name="tiltDegrees"/> from up toward azimuth <paramref name="azimuth"/> (radians).</summary>
		internal static Vector3 Tilted(float tiltDegrees, float azimuth)
		{
			float t = tiltDegrees * Mathf.Deg2Rad;
			return new Vector3(Mathf.Sin(t) * Mathf.Cos(azimuth), Mathf.Cos(t), Mathf.Sin(t) * Mathf.Sin(azimuth));
		}

		internal static Vector3 RandomAxis(DeterministicRNG rng)
		{
			float y = rng.Range(-1f, 1f);
			float a = rng.NextFloat() * Tau;
			float h = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
			return new Vector3(Mathf.Cos(a) * h, y, Mathf.Sin(a) * h);
		}

		internal static Vector3 HorizontalAxis(DeterministicRNG rng)
		{
			float a = rng.NextFloat() * Tau;
			return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
		}

		/// <summary>The radius of the superellipse |x/a|^e + |z/b|^e = 1 toward angle θ.</summary>
		internal static float Superellipse(float a, float b, float e, float theta)
		{
			float c = Mathf.Abs(Mathf.Cos(theta)) / a, s = Mathf.Abs(Mathf.Sin(theta)) / b;
			float sum = Mathf.Pow(c, e) + Mathf.Pow(s, e);
			return sum > 1e-12f ? Mathf.Pow(sum, -1f / e) : Mathf.Max(a, b);
		}

		/// <summary>Smooth noise round a circle, periodic in the angle: a plan outline's wobble.</summary>
		internal static float CircleNoise(float phi, float frequency, int seed)
		{
			return ProceduralNoise.Fbm3(new Vector3(Mathf.Cos(phi) * frequency, Mathf.Sin(phi) * frequency, 0.37f), 3, 0.5f, seed);
		}

		/// <summary>The support distance of an ellipsoid of semi-axes <paramref name="half"/> along <paramref name="n"/>.</summary>
		internal static float Support(Vector3 half, Vector3 n)
		{
			float x = half.x * n.x, y = half.y * n.y, z = half.z * n.z;
			return Mathf.Sqrt(x * x + y * y + z * z);
		}

		/// <summary>
		/// The tallest of the domes (1 − x²)^power over the 27 nearest cells of jittered cellular
		/// noise, x being the distance to a cell's point over that cell's radius; cells hold a dome
		/// with probability <paramref name="fill"/>. Taking the maximum over every cell in reach,
		/// rather than only the nearest, keeps the field continuous where two domes overlap.
		/// </summary>
		internal static float CellBump(Vector3 p, int seed, float fill, float rMin, float rMax, float power)
		{
			int cx = Mathf.FloorToInt(p.x), cy = Mathf.FloorToInt(p.y), cz = Mathf.FloorToInt(p.z);
			float best = 0f;
			for (int dz = -1; dz <= 1; dz++)
			{
				for (int dy = -1; dy <= 1; dy++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						int x = cx + dx, y = cy + dy, z = cz + dz;
						uint h = ProceduralNoise.Hash(x, y, z, seed);
						if (ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x2c1b3c6du)) >= fill)
						{
							continue;
						}
						float fx = x + ProceduralNoise.ToUnit(h);
						float fy = y + ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x68e31da4u));
						float fz = z + ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x1b873593u));
						float r = Mathf.Lerp(rMin, rMax, ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x297a2d39u)));
						float ex = p.x - fx, ey = p.y - fy, ez = p.z - fz;
						float d2 = (ex * ex + ey * ey + ez * ez) / (r * r);
						if (d2 >= 1f)
						{
							continue;
						}
						float v = Mathf.Pow(1f - d2, power);
						if (v > best)
						{
							best = v;
						}
					}
				}
			}
			return best;
		}

		/// <summary>A vertical solution flute, 0 on the sharp ridges between grooves and 1 at a groove's floor.</summary>
		internal static float Groove(float phi, Vector3 p, int count, float warpScale, int seed)
		{
			float warp = ProceduralNoise.Fbm3(p / Mathf.Max(0.05f, warpScale), 2, 0.5f, seed) * 0.5f;
			float x = Frac(phi * count / Tau + warp);
			return Mathf.Sqrt(Mathf.Sin(Mathf.PI * x));
		}

		/// <summary>
		/// Appends a triangle wound so it faces <paramref name="facing"/> on the reference positions,
		/// whatever the real positions have since become. The same rule as
		/// <see cref="MeshBuilder.AddTriangle"/>, judged on the undeformed shape.
		/// </summary>
		internal static void AddOriented(MeshBuilder mesh, List<Vector3> reference, int a, int b, int c, Vector3 facing)
		{
			Vector3 n = Vector3.Cross(reference[b] - reference[a], reference[c] - reference[a]);
			List<int> list = mesh.Submeshes[0];
			if (Vector3.Dot(n, facing) >= 0f)
			{
				list.Add(a); list.Add(b); list.Add(c);
			}
			else
			{
				list.Add(a); list.Add(c); list.Add(b);
			}
		}

		// ── Engine 1: implicit field, ray-cast from a centre ─────────

		internal struct Cut
		{
			public Vector3 N;
			public float D, Soft, Rough;
		}

		/// <summary>
		/// A rock's signed field in metres along a ray from <see cref="Origin"/>: negative inside.
		/// A superellipsoid round <see cref="Centre"/>, displaced, then cut.
		/// </summary>
		internal sealed class Field
		{
			public Vector3 Centre, Origin;
			public Vector3 Half = Vector3.one;
			public float Exponent = 2f;
			/// <summary>Horizontal scale at the top relative to the bottom (a spire or a cap).</summary>
			public float TaperTop = 1f;
			public float Lump, LumpScale = 1f;
			public int Seed;
			public List<Cut> Cuts = new List<Cut>();
			public float FloorY = float.NegativeInfinity, FloorSoft;
			public ExfoliationStyle Exfoliation;
			public FoliationStyle Foliation;
			public Vector3 FoliationNormal = Vector3.up, FoldDirection = Vector3.right;
			public PitStyle Pits;
			public FluteStyle Flutes;
			public ClastStyle Clasts;

			public Field Clone()
			{
				var f = (Field)MemberwiseClone();
				f.Cuts = new List<Cut>(Cuts);
				return f;
			}

			public float MaxHalf => Mathf.Max(Half.x, Mathf.Max(Half.y, Half.z));
			public float MeanHalf => (Half.x + Half.y + Half.z) / 3f;

			public float Evaluate(Vector3 p)
			{
				Vector3 q = p - Centre;
				if (TaperTop != 1f)
				{
					float ty = Mathf.Clamp01((q.y + Half.y) / (2f * Half.y));
					float sc = Mathf.Lerp(1f, TaperTop, ty);
					q.x /= sc;
					q.z /= sc;
				}
				float e = Exponent;
				float sn = Mathf.Pow(Mathf.Pow(Mathf.Abs(q.x) / Half.x, e) + Mathf.Pow(Mathf.Abs(q.y) / Half.y, e) + Mathf.Pow(Mathf.Abs(q.z) / Half.z, e), 1f / e);
				float len = q.magnitude;
				// Along-ray distance to the superellipsoid: |q| minus its radius in q's direction.
				float f = sn > 1e-6f ? len - len / sn : -Mathf.Min(Half.x, Mathf.Min(Half.y, Half.z));
				f -= Displacement(p, q);
				for (int i = 0; i < Cuts.Count; i++)
				{
					Cut c = Cuts[i];
					float g = Vector3.Dot(c.N, p) - c.D;
					if (c.Rough > 0f)
					{
						g += c.Rough * ProceduralNoise.Fbm3(p * (3f / LumpScale), 3, 0.5f, Seed + 91 + i);
					}
					f = SMax(f, g, c.Soft);
				}
				if (FloorY > float.NegativeInfinity)
				{
					f = SMax(f, FloorY - p.y, FloorSoft);
				}
				return f;
			}

			private float Displacement(Vector3 p, Vector3 q)
			{
				float d = 0f;
				Vector3 rel = p - Origin;
				float rl = rel.magnitude;
				Vector3 dir = rl > 1e-6f ? rel / rl : Vector3.up;
				if (Lump > 0f)
				{
					d += ProceduralNoise.Fbm3(p / LumpScale, 4, 0.5f, Seed) * Lump;
					d += (ProceduralNoise.Ridged3(p * (2.2f / LumpScale), 3, 0.5f, Seed + 17) - 0.5f) * Lump * 0.4f;
				}
				if (Exfoliation.Thickness > 0f)
				{
					// Sheets peel in patches: where the patch noise is below the threshold the outer
					// sheet has gone, and below a lower threshold the one under it too. The narrow
					// smoothstep leaves each sheet's broken edge as a low step.
					float patch = ProceduralNoise.Fbm3(p / (LumpScale * 0.6f), 3, 0.5f, Seed + 41);
					float th = Mathf.Lerp(-0.3f, 0.3f, Exfoliation.Peel);
					d -= Exfoliation.Thickness * (1f - Smooth(th - 0.025f, th + 0.025f, patch));
					d -= Exfoliation.Thickness * (1f - Smooth(th - 0.225f, th - 0.175f, patch));
				}
				if (Foliation.Relief > 0f)
				{
					d += Bands(p);
				}
				if (Pits.Spacing > 0f && Pits.Depth > 0f)
				{
					float up = Smooth(0.15f, 0.65f, dir.y);
					float side = 1f - Smooth(0.5f, 0.9f, Mathf.Abs(dir.y));
					float w = Mathf.Lerp(1f, up, Pits.Tops) * Mathf.Lerp(1f, side, Pits.Sides);
					if (w > 0f)
					{
						float rMax = Mathf.Clamp(Pits.Radius, 0.05f, 0.5f);
						// A cavity deeper than it is wide gets a flared bowl: a radius function
						// cannot overhang, and a steep wall sampled by rays folds over.
						float steep = Pits.Depth / Mathf.Max(1e-3f, rMax * Pits.Spacing);
						d -= w * Pits.Depth * CellBump(p / Pits.Spacing, Seed + 67, Pits.Fill, rMax * 0.6f, rMax, steep > 0.8f ? 1.1f : 0.6f);
					}
				}
				if (Flutes.Count > 0 && Flutes.Depth > 0f)
				{
					float horiz = Smooth(0.3f, 0.65f, Mathf.Sqrt(Mathf.Max(0f, 1f - dir.y * dir.y)));
					float below = (Half.y - q.y) / (2f * Half.y);
					float reach = 1f - Smooth(Flutes.Reach * 0.5f, Flutes.Reach, below);
					if (horiz > 0f && reach > 0f)
					{
						d -= Flutes.Depth * reach * horiz * Groove(Mathf.Atan2(q.z, q.x), p, Flutes.Count, LumpScale * 0.7f, Seed + 61);
					}
				}
				if (Clasts.Size > 0f)
				{
					// Cobbles stand proud of a matrix worn back between them.
					float bump = CellBump(p / Clasts.Size, Seed + 71, Clasts.Fill, 0.32f, 0.5f, 0.5f);
					d += Clasts.Protrusion * Clasts.Size * 0.5f * (bump - 0.3f);
				}
				return d;
			}

			/// <summary>
			/// Distance through the layering at p, metres: along the foliation normal, bent by the
			/// folds and the crenulation, and stretched and squeezed so bands vary in thickness. The
			/// ribs are cut from it and the texture's v is laid along it, so a texture band and a
			/// rib follow the same folds at the same spacing.
			/// </summary>
			public float Layer(Vector3 p)
			{
				FoliationStyle fo = Foliation;
				float along = Vector3.Dot(p, FoldDirection);
				float h = Vector3.Dot(p, FoliationNormal);
				if (fo.Fold > 0f && fo.FoldWavelength > 0f)
				{
					h += fo.Fold * Mathf.Sin(along * Tau / fo.FoldWavelength);
					h += fo.Fold * 0.5f * ProceduralNoise.Fbm3(p / fo.FoldWavelength, 2, 0.5f, Seed + 53);
				}
				if (fo.Crenulation > 0f)
				{
					h += fo.Crenulation * fo.Wavelength * 0.45f * Mathf.Sin(along * Tau / (fo.Wavelength * 1.6f));
				}
				float wavelength = Mathf.Max(0.01f, fo.Wavelength);
				float phase = h / wavelength;
				// Irregular thickness: a slow warp along the layering itself, gentle enough (slope
				// under 1) that the coordinate never runs backwards.
				phase += 0.35f * ProceduralNoise.Gradient3(new Vector3(phase * 0.37f, 0.5f, 0.5f), Seed + 57);
				return phase * wavelength;
			}

			/// <summary>Foliation ribs: resistant bands standing proud along the folded layering, each band its own hardness.</summary>
			private float Bands(Vector3 p)
			{
				FoliationStyle fo = Foliation;
				float phase = Layer(p) / Mathf.Max(0.01f, fo.Wavelength);
				int band = Mathf.FloorToInt(phase);
				float f = phase - band;
				float hard = ProceduralNoise.ToUnit(ProceduralNoise.Hash(band, 0, Seed + 59));
				// A steep face on one side of each rib and a long slope on the other, as layers of
				// different hardness weather.
				float rib = Smooth(0f, 0.22f, f) * (1f - Smooth(0.42f, 0.78f, f));
				return fo.Relief * ((0.35f + 0.65f * hard) * rib - 0.35f);
			}

			/// <summary>Distance from the origin to the surface along <paramref name="dir"/>: march to the first exit, then bisect.</summary>
			public float Radius(Vector3 dir, float reach)
			{
				const int Steps = 28;
				const int Bisections = 16;
				float step = reach / Steps;
				float lo = 0f, hi = reach;
				bool found = false;
				for (int i = 1; i <= Steps; i++)
				{
					float t = step * i;
					if (Evaluate(Origin + dir * t) > 0f)
					{
						lo = t - step;
						hi = t;
						found = true;
						break;
					}
				}
				if (!found)
				{
					return reach;
				}
				for (int i = 0; i < Bisections; i++)
				{
					float mid = (lo + hi) * 0.5f;
					if (Evaluate(Origin + dir * mid) > 0f)
					{
						hi = mid;
					}
					else
					{
						lo = mid;
					}
				}
				return (lo + hi) * 0.5f;
			}
		}

		internal static Vector3 CubePoint(int face, float a, float b)
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

		internal static readonly Vector3[] ChartAxis = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

		/// <summary>
		/// The field's surface as a cube-sphere of six charts, smooth-shaded across their seams.
		/// </summary>
		/// <remarks>
		/// Each chart's UVs are in metres, with v along the rock's layering (the foliation normal,
		/// or up) as near as the chart allows. The
		/// cube grid's own (i, j) would turn a strata texture's bands into squares on top of the
		/// rock and chevrons down its sides — the construction showing through.
		/// </remarks>
		internal static MeshBuilder BuildField(Field field, int res)
		{
			res = Mathf.Max(1, res);
			float reach = 1.8f * field.MaxHalf + (field.Origin - field.Centre).magnitude + 2f * field.Lump;
			Vector3 layering = field.Foliation.Relief > 0f ? field.FoliationNormal : Vector3.up;
			var part = new MeshBuilder(1);
			var reference = new List<Vector3>();
			for (int f = 0; f < 6; f++)
			{
				int first = part.VertexCount;
				Vector3 axis = ChartAxis[f];
				Vector3 vAxis = layering - axis * Vector3.Dot(layering, axis);
				if (vAxis.sqrMagnitude < 0.05f)
				{
					vAxis = Vector3.forward - axis * axis.z;
				}
				vAxis.Normalize();
				Vector3 uAxis = Vector3.Cross(vAxis, axis);
				// On the charts round the layering, v is exactly the distance along it, the same
				// function on every chart, so a band crosses a chart seam unbroken; the two charts
				// facing along it keep the planar projection. A foliated rock lays v along its folded
				// layer coordinate on every chart instead, so texture bands follow the ribs.
				bool foliated = field.Foliation.Relief > 0f;
				bool side = Mathf.Abs(Vector3.Dot(axis, layering)) < 0.7f;
				Vector3 vMeasure = side ? layering : vAxis;
				float inv = 1f / RockMeshes.TextureMetres;
				for (int j = 0; j <= res; j++)
				{
					for (int i = 0; i <= res; i++)
					{
						// (2i − res) / res, not 2i / res − 1: the latter is not exactly antisymmetric in
						// float (for res = 9, 2·3/9 − 1 and −(2·6/9 − 1) differ in the last bit), so two
						// charts' copies of a seam vertex would be cast along directions an ulp apart.
						// Division by res is sign-symmetric, so this is bit-identical from both charts.
						float a = (float)(2 * i - res) / res;
						float b = (float)(2 * j - res) / res;
						Vector3 dir = CubePoint(f, a, b).normalized;
						float t = field.Radius(dir, reach);
						Vector3 p = field.Origin + dir * t;
						float v = foliated ? field.Layer(p) : Vector3.Dot(p, vMeasure);
						part.AddVertex(p, Vector3.zero, new Vector2(Vector3.Dot(p, uAxis), v) * inv, White);
						reference.Add(dir);
					}
				}
				for (int j = 0; j < res; j++)
				{
					for (int i = 0; i < res; i++)
					{
						int v00 = first + j * (res + 1) + i;
						int v10 = v00 + 1, v01 = v00 + res + 1, v11 = v01 + 1;
						Vector3 facing = reference[v00] + reference[v11];
						AddOriented(part, reference, v00, v10, v11, facing);
						AddOriented(part, reference, v00, v11, v01, facing);
					}
				}
			}
			part.RecalculateNormals(true);
			return part;
		}

		/// <summary>The field every lone rock starts from: the style's proportions, lumps, cleavage and surface features.</summary>
		internal static Field BaseField(in RockType type, in FormationShape shape, DeterministicRNG rng, int seed, float halfWidth, float halfHeight)
		{
			RockGeometryStyle st = type.Style;
			float aspect = Range(rng, st.Aspect);
			float elongation = shape.Elongation > 0f ? shape.Elongation : 1f;
			var f = new Field { Seed = seed };
			f.Half = new Vector3(halfWidth, halfHeight, Mathf.Max(0.05f * halfWidth, halfWidth * aspect / elongation));
			f.Exponent = shape.Exponent > 0f ? shape.Exponent : Range(rng, st.Roundness);
			float mean = f.MeanHalf;
			f.Lump = st.Lumpiness * 0.12f * mean;
			f.LumpScale = mean * 0.9f;
			float soft = Mathf.Lerp(0.18f, 0.015f, st.Sharpness) * mean;
			for (int i = 0; i < st.Facets; i++)
			{
				// Mostly sideways and upward: real boulders are cleaved on their exposed faces.
				float y = rng.Range(-0.2f, 0.9f);
				float a = rng.NextFloat() * Tau;
				float h = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
				var n = new Vector3(Mathf.Cos(a) * h, y, Mathf.Sin(a) * h);
				float d = Support(f.Half, n) * Mathf.Lerp(0.95f, 0.62f, st.FacetDepth) * rng.Range(0.9f, 1.05f);
				f.Cuts.Add(new Cut { N = n, D = d, Soft = soft });
			}
			f.FloorY = -f.Half.y * 0.72f;
			f.FloorSoft = 0.12f * mean;
			f.Exfoliation = st.Exfoliation;
			f.Pits = st.Pits;
			f.Flutes = st.Flutes;
			f.Clasts = st.Clasts;
			f.Foliation = st.Foliation;
			if (st.Foliation.Relief > 0f)
			{
				float az = rng.NextFloat() * Tau;
				f.FoliationNormal = Tilted(Range(rng, st.Foliation.Dip), az);
				Vector3 dipDir = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
				Vector3 fold = dipDir - f.FoliationNormal * Vector3.Dot(dipDir, f.FoliationNormal);
				f.FoldDirection = fold.sqrMagnitude > 1e-6f ? fold.normalized : Vector3.right;
			}
			return f;
		}

		internal static MeshBuilder Boulder(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			Field field = BaseField(in type, in shape, rng, seed, shape.Size * 0.5f, shape.Height * 0.5f);
			return BuildField(field, CubeRes(res, in type.Style));
		}

		/// <summary>
		/// Corestones stacked on their sheeting joints: each block a rounded box flattened top and
		/// bottom, smaller and a little offset and turned on the one below. Perched: a big rounder
		/// boulder resting on a low wide one by a small contact, a logan stone.
		/// </summary>
		internal static MeshBuilder Tor(in RockType type, in FormationShape shape, int res, int seed, bool perched)
		{
			var rng = new DeterministicRNG(seed);
			int n = perched ? 2 : Mathf.Max(2, shape.Count > 0 ? shape.Count : 3);
			var heights = new float[n];
			float sum = 0f;
			for (int i = 0; i < n; i++)
			{
				float share = perched ? (i == 0 ? 0.42f : 1f) : Mathf.Lerp(1.1f, 0.85f, i / (float)(n - 1));
				heights[i] = share * rng.Range(0.8f, 1.15f);
				sum += heights[i];
			}
			var result = new MeshBuilder(1);
			float baseYaw = rng.NextFloat() * 360f;
			Vector2 offset = Vector2.zero;
			for (int i = 0; i < n; i++)
			{
				float h = heights[i] / sum * shape.Height;
				float widthShare = perched ? (i == 0 ? 1f : 0.66f) : Mathf.Lerp(1f, 0.6f, i / (float)(n - 1));
				float w = shape.Size * 0.5f * widthShare * rng.Range(0.88f, 1f);
				bool rocking = perched && i == 1;
				FormationShape blockShape = shape;
				blockShape.Exponent = rocking ? 2.15f : perched ? 3.2f : shape.Exponent;
				Field f = BaseField(in type, in blockShape, rng, seed + 101 * (i + 1), w, h * 0.5f);
				float hh = h * 0.5f;
				float flatTop = hh * 0.8f, flatBottom = hh * (rocking ? 1.2f : 0.8f);
				f.Cuts.Add(new Cut { N = Vector3.up, D = flatTop, Soft = 0.12f * hh });
				f.Cuts.Add(new Cut { N = Vector3.down, D = flatBottom, Soft = 0.12f * hh });
				f.FloorY = float.NegativeInfinity;
				if (i < n - 1)
				{
					// Weathering pits (gnammas) hold water only on the summit block.
					f.Pits.Depth = 0f;
				}
				MeshBuilder part = BuildField(f, Mathf.Max(2, res));
				if (i > 0)
				{
					float reachOut = rocking ? 0.22f : 0.14f;
					offset += new Vector2(rng.Range(-reachOut, reachOut), rng.Range(-reachOut, reachOut)) * w;
				}
				part.Transform(Rotation(Vector3.up, baseYaw + rng.Range(-35f, 35f)) * Rotation(HorizontalAxis(rng), rng.Range(0f, 4f)));
				// Rest it on what is really below: the highest point of the stack under its middle,
				// not the nominal flat top, which lumps and rounding never quite reach.
				float support = 0f;
				if (i > 0)
				{
					support = float.MinValue;
					float r2 = 0.3f * w * 0.3f * w;
					foreach (Vector3 p in result.Positions)
					{
						float dx = p.x - offset.x, dz = p.z - offset.y;
						if (dx * dx + dz * dz < r2 && p.y > support)
						{
							support = p.y;
						}
					}
					if (support == float.MinValue)
					{
						support = result.Bounds.max.y;
					}
				}
				float sink = (rocking ? 0.03f : 0.05f) * h;
				part.Transform(Matrix4x4.Translate(new Vector3(offset.x, support - part.Bounds.min.y - sink, offset.y)));
				result.Append(part);
			}
			return result;
		}

		/// <summary>A corestone split by one vertical joint, both halves cast from the same field so they still match across the gap, leaning apart.</summary>
		internal static MeshBuilder Split(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			Field proto = BaseField(in type, in shape, rng, seed, shape.Size * 0.46f, shape.Height * 0.5f);
			float yaw = rng.Range(-15f, 15f) * Mathf.Deg2Rad;
			var n = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
			float gap = 0.035f * shape.Size;
			float lean = rng.Range(3f, 7f);
			Vector3 axis = Vector3.Cross(Vector3.up, n);
			var result = new MeshBuilder(1);
			for (int side = -1; side <= 1; side += 2)
			{
				Field f = proto.Clone();
				// Keep side·(n·p) ≥ gap/2: the joint face, a little rough.
				f.Cuts.Add(new Cut { N = -side * n, D = -gap * 0.5f, Soft = 0.02f * proto.MeanHalf, Rough = 0.012f * shape.Size });
				f.Origin = side * n * proto.Half.x * 0.45f;
				MeshBuilder part = BuildField(f, Mathf.Max(2, res));
				// Lean apart about the foot of the joint face: rotating up about up × n turns it toward n.
				Vector3 pivot = side * n * gap * 0.5f + Vector3.down * proto.Half.y * 0.72f;
				Matrix4x4 m = Matrix4x4.Translate(pivot + side * n * gap * 0.6f) * Rotation(axis, side * lean) * Matrix4x4.Translate(-pivot);
				part.Transform(m);
				result.Append(part);
			}
			return result;
		}

		// ── Engine 2: lathe ──────────────────────────────────────────

		internal struct Ring
		{
			public float Y, Scale, Inset, Wall;
		}

		/// <summary>A closed surface of revolution with a star-shaped plan outline, rings bottom to top.</summary>
		internal sealed class Lathe
		{
			public Vector3 Centre;
			public int Segments = 16;
			public int CapRings = 1;
			public readonly List<Ring> Rings = new List<Ring>();
			/// <summary>Plan radius toward angle φ at scale 1, metres.</summary>
			public Func<float, float> Outline;
			/// <summary>Extra radius at a ring point (its unoffset position, φ), metres; negative carves.</summary>
			public Func<Vector3, float, float> WallOffset;
			/// <summary>Vertical offset of the top cap's interior (up positive), metres.</summary>
			public Func<Vector3, float> TopRelief;
			/// <summary>Vertical offset of the bottom cap's interior (up positive), metres.</summary>
			public Func<Vector3, float> BottomRelief;

			public void AddRing(float y, float scale, float inset, float wall = 1f)
			{
				Rings.Add(new Ring { Y = y, Scale = scale, Inset = inset, Wall = wall });
			}
		}

		internal static MeshBuilder BuildLathe(Lathe lathe)
		{
			int n = Mathf.Max(3, lathe.Segments);
			int k = lathe.Rings.Count;
			var grid = new Vector3[k, n];
			var mean = new float[k];
			for (int r = 0; r < k; r++)
			{
				Ring ring = lathe.Rings[r];
				for (int i = 0; i < n; i++)
				{
					float phi = Tau * i / n;
					float c = Mathf.Cos(phi), s = Mathf.Sin(phi);
					// An inset never eats more than most of the outline, or a narrow bed's bevel folds over its axis.
					float plan = lathe.Outline(phi) * ring.Scale;
					float radius = Mathf.Max(plan * 0.3f, plan - ring.Inset);
					if (lathe.WallOffset != null && ring.Wall != 0f)
					{
						Vector3 at = lathe.Centre + new Vector3(c * radius, ring.Y, s * radius);
						radius += ring.Wall * lathe.WallOffset(at, phi);
					}
					radius = Mathf.Max(0.01f, radius);
					grid[r, i] = lathe.Centre + new Vector3(c * radius, ring.Y, s * radius);
					mean[r] += radius / n;
				}
			}

			var part = new MeshBuilder(1);
			var reference = new List<Vector3>();
			float meanRadius = 0f;
			for (int r = 0; r < k; r++)
			{
				meanRadius += mean[r] / k;
			}
			// The wall chart: u round the rock in metres, v up its profile in metres.
			float uStep = Tau * meanRadius / n / RockMeshes.TextureMetres;
			float v = 0f;
			int wallFirst = part.VertexCount;
			for (int r = 0; r < k; r++)
			{
				if (r > 0)
				{
					float dy = lathe.Rings[r].Y - lathe.Rings[r - 1].Y, dr = mean[r] - mean[r - 1];
					v += Mathf.Sqrt(dy * dy + dr * dr) / RockMeshes.TextureMetres;
				}
				for (int i = 0; i <= n; i++)
				{
					float phi = Tau * i / n;
					part.AddVertex(grid[r, i % n], Vector3.zero, new Vector2(i * uStep, v), White);
					reference.Add(new Vector3(Mathf.Cos(phi), r, Mathf.Sin(phi)));
				}
			}
			for (int r = 0; r + 1 < k; r++)
			{
				for (int i = 0; i < n; i++)
				{
					int a = wallFirst + r * (n + 1) + i;
					int b = a + 1, d = a + n + 1, c = d + 1;
					float mid = Tau * (i + 0.5f) / n;
					var facing = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
					AddOriented(part, reference, a, b, c, facing);
					AddOriented(part, reference, a, c, d, facing);
				}
			}
			AddCap(part, reference, lathe, grid, k - 1, n, 1f);
			AddCap(part, reference, lathe, grid, 0, n, -1f);
			part.RecalculateNormals(true);
			return part;
		}

		/// <summary>A capped fan over one rim ring: concentric rings shrinking toward the axis, planar UVs in metres.</summary>
		internal static void AddCap(MeshBuilder part, List<Vector3> reference, Lathe lathe, Vector3[,] grid, int rim, int n, float side)
		{
			float y = lathe.Rings[rim].Y;
			Vector3 centre = lathe.Centre + new Vector3(0f, y, 0f);
			Func<Vector3, float> relief = side > 0f ? lathe.TopRelief : lathe.BottomRelief;
			int rings = Mathf.Max(1, lathe.CapRings);
			int first = part.VertexCount;
			for (int c = 0; c < rings; c++)
			{
				float s = 1f - (float)c / rings;
				for (int i = 0; i < n; i++)
				{
					Vector3 p = c == 0 ? grid[rim, i] : centre + (grid[rim, i] - centre) * s;
					if (c > 0 && relief != null)
					{
						float s2 = s * s;
						p.y += relief(p) * (1f - s2 * s2);
					}
					part.AddVertex(p, Vector3.zero, new Vector2(p.x, p.z) / RockMeshes.TextureMetres, White);
					float phi = Tau * i / n;
					reference.Add(new Vector3(Mathf.Cos(phi) * s, 0f, Mathf.Sin(phi) * s));
				}
			}
			Vector3 tip = centre;
			if (relief != null)
			{
				tip.y += relief(tip);
			}
			int apex = part.AddVertex(tip, Vector3.zero, new Vector2(tip.x, tip.z) / RockMeshes.TextureMetres, White);
			reference.Add(Vector3.zero);
			Vector3 facing = Vector3.up * side;
			for (int c = 0; c + 1 < rings; c++)
			{
				for (int i = 0; i < n; i++)
				{
					int a = first + c * n + i;
					int b = first + c * n + (i + 1) % n;
					int bb = b + n, aa = a + n;
					AddOriented(part, reference, a, b, bb, facing);
					AddOriented(part, reference, a, bb, aa, facing);
				}
			}
			int last = first + (rings - 1) * n;
			for (int i = 0; i < n; i++)
			{
				AddOriented(part, reference, last + i, last + (i + 1) % n, apex, facing);
			}
		}

		/// <summary>
		/// Lathe tessellation at a resolution for a formation of <paramref name="parts"/> lathes:
		/// bevel steps, extra wall rings and cap rings by level, and as many segments round as the
		/// level's triangle budget allows (most of it), so a ten-bed shale ledge and a lone
		/// pinnacle both land inside it.
		/// </summary>
		/// <param name="pitted">The tops carry solution pans: more cap rings, so a pan is more than one vertex.</param>
		internal static void LatheDetail(int res, int parts, bool pitted, out int segments, out int bevelSteps, out int wallRings, out int capRings)
		{
			int level = Level(res);
			bevelSteps = level == 2 ? 2 : 1;
			wallRings = 0;
			capRings = pitted ? (level == 2 ? 6 : level == 1 ? 3 : 1) : (level == 2 ? 3 : level == 1 ? 2 : 1);
			if (level == 1 && parts >= 6)
			{
				// A tall stack shows only its top cap and ledge rims: spend the budget on segments
				// round, which hold the outline's corners, rather than on buried caps.
				capRings = 1;
			}
			int rings = 2 * (bevelSteps + 1) + wallRings;
			int perSegment = 2 * (rings - 1) + 2 * (2 * capRings - 1);
			int budget = TriangleBudget[2 - level] * 4 / 5 / Mathf.Max(1, parts);
			segments = Mathf.Clamp(budget / perSegment, 8, 6 + 3 * res);
		}

		internal static bool PittedTops(in RockGeometryStyle st) => st.Pits.Spacing > 0f && st.Pits.Depth > 0f && st.Pits.Tops > 0f;

		/// <summary>One bed's profile: a rounded bottom edge, a wall, a rounded top edge; insets from the outline in metres.</summary>
		internal static void BedRings(Lathe lathe, float thickness, float roundBottom, float roundTop, int bevelSteps, int wallRings)
		{
			float limit = thickness * 0.85f;
			if (roundBottom + roundTop > limit)
			{
				float k = limit / (roundBottom + roundTop);
				roundBottom *= k;
				roundTop *= k;
			}
			roundBottom = Mathf.Max(roundBottom, thickness * 0.02f);
			roundTop = Mathf.Max(roundTop, thickness * 0.02f);
			for (int s = 0; s <= bevelSteps; s++)
			{
				float a = s / (float)bevelSteps * Mathf.PI * 0.5f;
				lathe.AddRing(roundBottom * (1f - Mathf.Cos(a)), 1f, roundBottom * (1f - Mathf.Sin(a)));
			}
			float y0 = roundBottom, y1 = thickness - roundTop;
			for (int w = 1; w <= wallRings; w++)
			{
				lathe.AddRing(Mathf.Lerp(y0, y1, w / (float)(wallRings + 1)), 1f, 0f);
			}
			for (int s = bevelSteps; s >= 0; s--)
			{
				float a = s / (float)bevelSteps * Mathf.PI * 0.5f;
				lathe.AddRing(thickness - roundTop * (1f - Mathf.Cos(a)), 1f, roundTop * (1f - Mathf.Sin(a)));
			}
		}

		/// <summary>
		/// Beds of differing hardness, one closed lathe each, stacked with a sliver of overlap. Soft
		/// beds are set back and rounded, hard ones stand proud with crisp edges and overhang the
		/// soft bed below. Ledges also set each bed back on one face, a staircase; a pedestal sets
		/// its middle beds far back under a hard cap. Dip tilts the whole stack.
		/// </summary>
		internal static MeshBuilder Bedded(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			BeddingStyle bd = st.Bedding;
			FormationKind kind = shape.Kind;
			int beds = shape.Count > 0 ? shape.Count : Mathf.RoundToInt(Range(rng, bd.Beds));
			beds = Mathf.Max(kind == FormationKind.Pedestal ? 3 : 1, beds);
			LatheDetail(res, beds, PittedTops(in st), out int segments, out int bevelSteps, out int wallRings, out int capRings);
			float size = shape.Size;
			float aspect = Range(rng, st.Aspect);
			float w = size * 0.5f, depth = w * aspect;
			float e = Range(rng, st.Roundness);
			float yaw = rng.NextFloat() * Tau;
			int joints = Mathf.Max(0, bd.Joints);
			var jointAz = new float[joints];
			var jointCut = new float[joints];
			for (int j = 0; j < joints; j++)
			{
				jointAz[j] = yaw + j * Mathf.PI * 0.5f + rng.Range(-0.15f, 0.15f);
				jointCut[j] = rng.Range(0.82f, 0.95f);
			}
			float front = rng.NextFloat() * Tau;
			float lumpiness = st.Lumpiness;
			int outlineSeed = seed + 5;
			Func<float, float> baseOutline = phi =>
			{
				float r = Superellipse(w, depth, e, phi - yaw);
				for (int j = 0; j < jointAz.Length; j++)
				{
					float c = Mathf.Cos(phi - jointAz[j]);
					if (c > 0.05f)
					{
						float dj = Superellipse(w, depth, e, jointAz[j] - yaw) * jointCut[j];
						r = SMin(r, dj / c, 0.04f * w);
					}
				}
				return r * (1f + lumpiness * 0.08f * CircleNoise(phi, 2.5f, outlineSeed));
			};

			var thickness = new float[beds];
			var hardness = new float[beds];
			float total = 0f;
			for (int b = 0; b < beds; b++)
			{
				hardness[b] = rng.NextFloat();
				thickness[b] = rng.Range(0.6f, 1.4f);
				if (kind == FormationKind.Pedestal)
				{
					bool cap = b == beds - 1;
					hardness[b] = cap ? 1f : b == 0 ? 0.5f : 0.1f;
					thickness[b] *= cap ? 1.8f : 1f;
				}
				total += thickness[b];
			}
			// Beds are one mass: each sits a sliver into the one below, its foot square, so the
			// bedding plane reads as a shallow weathered notch, never as a gap between cushions.
			float overlap = 0.02f * shape.Height;
			float scale = (shape.Height + overlap * (beds - 1)) / total;

			var mesh = new MeshBuilder(1);
			float y = 0f;
			for (int b = 0; b < beds; b++)
			{
				float t = thickness[b] * scale;
				float h = hardness[b];
				// A soft bed weathers back by a fraction of its own thickness (Contrast), a hard one not at all.
				float recess = t * bd.Contrast * (1f - h);
				// Fractions of the outline: how far this bed retreats on the exposed face (ledges)
				// and all round (a pedestal's neck).
				float setback = 0f, neck = 1f;
				if (kind == FormationKind.Ledges)
				{
					setback = beds > 1 ? 0.55f * b / (beds - 1) : 0f;
				}
				else if (kind == FormationKind.Pedestal)
				{
					// Wind-blown sand scours hardest near the ground: the neck is thinnest low down.
					float u = b / (float)(beds - 1);
					neck = b == beds - 1 ? 1f : b == 0 ? 0.62f : Mathf.Lerp(0.36f, 0.5f, u);
					recess *= 0.3f;
				}
				float crumble = bd.Crumble * size;
				int bedSeed = seed + 1000 + b * 17;
				float frontAz = front;
				float bedSetback = setback;
				float bedNeck = neck;
				float bedRecess = recess;
				float bedLayer = 0.37f + b * 0.3f;
				Func<float, float> outline = phi =>
				{
					float scaleHere = bedNeck * (1f - bedSetback * Smooth(-0.3f, 0.9f, Mathf.Cos(phi - frontAz)));
					// The wobble is one field sampled bed by bed, so each bed's outline follows the
					// one below with small offsets and the outcrop keeps one silhouette.
					float wobble = ProceduralNoise.Fbm3(new Vector3(Mathf.Cos(phi) * 5f, Mathf.Sin(phi) * 5f, bedLayer), 3, 0.5f, outlineSeed + 1);
					float r = baseOutline(phi) * scaleHere - bedRecess + crumble * wobble;
					return Mathf.Max(0.05f * w, r);
				};
				// Weathering rounds a soft bed's top edge a little and leaves a hard bed's crisp: the
				// ledge is the hard bed's slightly rounded lip over the soft bed's shallow recess.
				// Every bed's foot is square, fused onto the bed below.
				float soft = (1f - h) * (1f - h);
				float roundTop = t * Mathf.Lerp(0.05f, 0.2f, bd.Rounding * soft);
				float roundBottom = t * 0.03f;
				if (kind == FormationKind.Pedestal && b < beds - 1)
				{
					// The neck is one scoured column; its beds show as faint lines, not as discs.
					roundTop = roundBottom = t * 0.05f;
				}
				var lathe = new Lathe { Centre = new Vector3(0f, y, 0f), Segments = segments, CapRings = capRings, Outline = outline };
				BedRings(lathe, t, roundBottom, roundTop, bevelSteps, wallRings);
				float bedTop = y + t;
				lathe.WallOffset = BedWall(in st, size, bedTop, t, bedSeed);
				lathe.TopRelief = BedTop(in st, size, bedSeed);
				lathe.BottomRelief = p => 0.004f * size * ProceduralNoise.Fbm3(p * (4f / size), 2, 0.5f, bedSeed + 3);
				mesh.Append(BuildLathe(lathe));
				y += t - overlap;
			}

			float dip = shape.Dip >= 0f ? shape.Dip : Range(rng, bd.Dip);
			if (dip > 0.01f)
			{
				mesh.Transform(Rotation(HorizontalAxis(rng), dip));
			}
			return mesh;
		}

		/// <summary>A bed's wall: weathering relief, and solution flutes running down from its top edge.</summary>
		internal static Func<Vector3, float, float> BedWall(in RockGeometryStyle st, float size, float top, float thickness, int seed)
		{
			float lump = st.Lumpiness * 0.02f * size;
			FluteStyle fl = st.Flutes;
			float reachMetres = Mathf.Max(0.05f, fl.Reach * thickness * 1.5f);
			return (p, phi) =>
			{
				float d = lump * ProceduralNoise.Fbm3(p * (3f / size), 3, 0.5f, seed + 7);
				if (fl.Count > 0 && fl.Depth > 0f)
				{
					float reach = 1f - Smooth(reachMetres * 0.4f, reachMetres, top - p.y);
					if (reach > 0f)
					{
						d -= fl.Depth * reach * Groove(phi, p, fl.Count, size * 0.4f, seed + 11);
					}
				}
				return d;
			};
		}

		/// <summary>A bed's top: gentle relief, and solution pans where the style pits upward faces.</summary>
		internal static Func<Vector3, float> BedTop(in RockGeometryStyle st, float size, int seed)
		{
			PitStyle pits = st.Pits;
			float lump = st.Lumpiness * 0.012f * size;
			return p =>
			{
				float d = lump * ProceduralNoise.Fbm3(p * (3f / size), 3, 0.5f, seed + 13);
				if (pits.Spacing > 0f && pits.Depth > 0f && pits.Tops > 0f)
				{
					float rMax = Mathf.Clamp(pits.Radius, 0.05f, 0.5f);
					d -= pits.Depth * pits.Tops * CellBump(p / pits.Spacing, seed + 17, pits.Fill, rMax * 0.6f, rMax, 0.5f);
				}
				return d;
			};
		}

		/// <summary>Distance from <paramref name="c"/> to a convex polygon's boundary toward angle φ.</summary>
		internal static float RayToPolygon(Vector2 c, float phi, List<Vector2> polygon)
		{
			var d = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));
			float best = float.MaxValue;
			for (int i = 0; i < polygon.Count; i++)
			{
				Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
				Vector2 e = b - a;
				float den = d.x * e.y - d.y * e.x;
				if (Mathf.Abs(den) < 1e-9f)
				{
					continue;
				}
				Vector2 ac = a - c;
				float t = (ac.x * e.y - ac.y * e.x) / den;
				float u = (ac.x * d.y - ac.y * d.x) / den;
				if (t > 0f && u >= -1e-4f && u <= 1f + 1e-4f && t < best)
				{
					best = t;
				}
			}
			return best == float.MaxValue ? 0.05f : best;
		}

		/// <summary>The cell of a site among others, each bisector moved back by half the gap: a convex polygon, counter-clockwise.</summary>
		internal static List<Vector2> VoronoiCell(Vector2 site, List<Vector2> sites, float gap, float extent)
		{
			var poly = new List<Vector2>
			{
				site + new Vector2(-extent, -extent), site + new Vector2(extent, -extent),
				site + new Vector2(extent, extent), site + new Vector2(-extent, extent),
			};
			foreach (Vector2 other in sites)
			{
				Vector2 delta = other - site;
				float len = delta.magnitude;
				if (len < 1e-5f || len > extent * 2f)
				{
					continue;
				}
				Vector2 n = delta / len;
				float d = Vector2.Dot(n, (site + other) * 0.5f) - gap * 0.5f;
				var next = new List<Vector2>(poly.Count + 1);
				for (int i = 0; i < poly.Count; i++)
				{
					Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
					float da = Vector2.Dot(n, a) - d, db = Vector2.Dot(n, b) - d;
					if (da <= 0f)
					{
						next.Add(a);
					}
					if ((da <= 0f) != (db <= 0f))
					{
						next.Add(a + (b - a) * (da / (da - db)));
					}
				}
				poly = next;
				if (poly.Count < 3)
				{
					break;
				}
			}
			return poly;
		}

		/// <summary>
		/// Limestone pavement: clints, the blocks between widened joints, each a low lathe with
		/// solution-rounded edges and a top pocked with pans; the grikes between them are the gaps.
		/// </summary>
		internal static MeshBuilder Pavement(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			int count = Mathf.Max(2, shape.Count > 0 ? shape.Count : 6);
			LatheDetail(res, count, PittedTops(in st), out int segments, out int bevelSteps, out int wallRings, out int capRings);
			int cols = Mathf.CeilToInt(Mathf.Sqrt(count * 1.5f));
			int rows = Mathf.CeilToInt(count / (float)cols);
			float aspect = Range(rng, st.Aspect);
			float sx = shape.Size / cols, sz = shape.Size * aspect / rows;
			var sites = new List<Vector2>();
			var keep = new List<int>();
			for (int r = -1; r <= rows; r++)
			{
				for (int c = -1; c <= cols; c++)
				{
					var p = new Vector2((c + 0.5f) * sx - shape.Size * 0.5f, (r + 0.5f) * sz - shape.Size * aspect * 0.5f);
					p += new Vector2(rng.Range(-0.18f, 0.18f) * sx, rng.Range(-0.18f, 0.18f) * sz);
					if (r >= 0 && r < rows && c >= 0 && c < cols && keep.Count < count)
					{
						keep.Add(sites.Count);
					}
					sites.Add(p);
				}
			}
			float gap = Mathf.Clamp(0.04f * shape.Size, 0.06f, 0.14f);
			float extent = Mathf.Max(sx, sz) * 2f;
			var mesh = new MeshBuilder(1);
			for (int k = 0; k < keep.Count; k++)
			{
				Vector2 site = sites[keep[k]];
				List<Vector2> cell = VoronoiCell(site, sites, gap, extent);
				if (cell.Count < 3)
				{
					continue;
				}
				Vector2 centroid = Vector2.zero;
				foreach (Vector2 p in cell)
				{
					centroid += p;
				}
				centroid /= cell.Count;
				float t = shape.Height * rng.Range(0.75f, 1f);
				int clintSeed = seed + 2000 + k * 13;
				List<Vector2> polygon = cell;
				Vector2 origin = centroid;
				var lathe = new Lathe
				{
					Centre = new Vector3(centroid.x, 0f, centroid.y),
					Segments = segments,
					CapRings = capRings,
					Outline = phi => RayToPolygon(origin, phi, polygon),
				};
				BedRings(lathe, t, t * 0.06f, t * 0.25f, bevelSteps, wallRings);
				lathe.WallOffset = BedWall(in st, shape.Size, t, t, clintSeed);
				lathe.TopRelief = BedTop(in st, shape.Size, clintSeed);
				mesh.Append(BuildLathe(lathe));
			}
			return mesh;
		}

		/// <summary>A karst pinnacle: a tapering lathe scored top to bottom by deep solution flutes, to a sharp tip.</summary>
		internal static MeshBuilder Pinnacle(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			LatheDetail(res, 1, false, out int segments, out _, out _, out int capRings);
			int level = Level(res);
			int rings = level == 2 ? 12 : level == 1 ? 8 : 5;
			float aspect = Range(rng, st.Aspect);
			float w = shape.Size * 0.5f / 1.15f;
			float depth = w * Mathf.Lerp(aspect, 1f, 0.4f);
			float yaw = rng.NextFloat() * Tau;
			int outlineSeed = seed + 5;
			float lump = st.Lumpiness;
			int flutes = Mathf.Max(7, st.Flutes.Count / 2 + 3);
			float height = shape.Height;
			float leanAz = rng.NextFloat() * Tau;
			float lean = rng.Range(0.05f, 0.2f) * w;
			float tipTop = 0.93f;
			var lathe = new Lathe
			{
				Centre = Vector3.zero,
				Segments = segments,
				CapRings = capRings,
				Outline = phi => Superellipse(w, depth, 2.4f, phi - yaw) * (1f + lump * 0.25f * CircleNoise(phi, 2f, outlineSeed)),
			};
			for (int r = 0; r < rings; r++)
			{
				float t = r / (float)(rings - 1) * tipTop;
				float s = 0.1f + 0.9f * Mathf.Pow(1f - t, 0.85f) + 0.15f * Mathf.Pow(1f - t, 8f);
				// Bulges and waists where harder and softer layers dissolve at different rates.
				s *= 1f + 0.12f * ProceduralNoise.Fbm3(new Vector3(t * 4f, 0.5f, 0.5f), 2, 0.5f, seed + 3);
				lathe.AddRing(t * height, s, 0f);
			}
			lathe.WallOffset = (p, phi) =>
			{
				float u = Mathf.Clamp01(p.y / height);
				float radial = new Vector2(p.x, p.z).magnitude;
				// Flutes cut a fixed share of the local girth, so the spire is scored to its tip.
				float d = -0.24f * radial * Mathf.Lerp(0.45f, 1f, u) * Groove(phi, p, flutes, w, seed + 11);
				d += lean * u * Mathf.Cos(phi - leanAz);
				return d + lump * 0.02f * height * ProceduralNoise.Fbm3(p * (2f / w), 3, 0.5f, seed + 7);
			};
			float topRadius = 0.25f * w;
			lathe.TopRelief = p => height * 0.07f * Mathf.Clamp01(1f - new Vector2(p.x, p.z).magnitude / topRadius);
			return BuildLathe(lathe);
		}

		/// <summary>
		/// A fairy chimney: a flared cone of soft tuff, scored by rain runnels and banded by ash
		/// layers, under a capstone of harder rock that shields it.
		/// </summary>
		internal static MeshBuilder Chimney(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			LatheDetail(res, 1, false, out int segments, out _, out _, out int capRings);
			int level = Level(res);
			int rings = level == 2 ? 13 : level == 1 ? 8 : 5;
			float aspect = Mathf.Lerp(Range(rng, st.Aspect), 1f, 0.5f);
			float w = shape.Size * 0.5f / 1.2f;
			float yaw = rng.NextFloat() * Tau;
			float stem = shape.Height * 0.8f;
			float leanAz = rng.NextFloat() * Tau;
			float lean = rng.Range(0.03f, 0.12f) * w;
			float totalHeight = shape.Height;
			int outlineSeed = seed + 5;
			float lump = st.Lumpiness;
			FluteStyle fl = st.Flutes;
			FoliationStyle fo = st.Foliation;
			PitStyle pits = st.Pits;
			var lathe = new Lathe
			{
				Centre = Vector3.zero,
				Segments = segments,
				CapRings = capRings,
				Outline = phi => Superellipse(w, w * aspect, 2.2f, phi - yaw) * (1f + lump * 0.12f * CircleNoise(phi, 2f, outlineSeed)),
			};
			for (int r = 0; r < rings; r++)
			{
				float t = r / (float)(rings - 1);
				float s = 0.36f + 0.64f * Mathf.Pow(1f - t, 1.3f) + 0.2f * Mathf.Pow(1f - t, 8f) + 0.06f * Smooth(0.85f, 1f, t);
				lathe.AddRing(t * stem, s, 0f);
			}
			lathe.WallOffset = (p, phi) =>
			{
				float u = Mathf.Clamp01(p.y / stem);
				float radial = new Vector2(p.x, p.z).magnitude;
				float local = Mathf.Clamp01(radial / (0.4f * w));
				float d = lean * u * Mathf.Cos(phi - leanAz);
				if (fl.Count > 0)
				{
					// Rain runnels: deepest on the flared foot where the water gathers.
					d -= Mathf.Max(fl.Depth, 0.09f * radial) * Mathf.Lerp(1f, 0.6f, u) * Groove(phi, p, fl.Count, w * 0.8f, seed + 11);
				}
				if (fo.Relief > 0f && fo.Wavelength > 0f)
				{
					float f = Frac(p.y / fo.Wavelength + 0.3f * ProceduralNoise.Fbm3(p / w, 2, 0.5f, seed + 19));
					d += fo.Relief * (Smooth(0f, 0.2f, f) * (1f - Smooth(0.5f, 0.85f, f)) - 0.4f);
				}
				if (pits.Spacing > 0f && pits.Depth > 0f)
				{
					float rMax = Mathf.Clamp(pits.Radius, 0.05f, 0.5f);
					d -= pits.Depth * 0.6f * (1f - u) * local * CellBump(p / pits.Spacing, seed + 23, pits.Fill, rMax * 0.6f, rMax, 0.6f);
				}
				return d + lump * 0.015f * totalHeight * ProceduralNoise.Fbm3(p * (2f / w), 3, 0.5f, seed + 7);
			};
			MeshBuilder mesh = BuildLathe(lathe);

			// The capstone: a flat, blocky slab of harder rock, far wider than the neck it shields.
			var capShape = new FormationShape { Name = shape.Name, Kind = FormationKind.Boulder, Size = shape.Size, Height = shape.Height, Exponent = 3f };
			float capHalf = w * 1.15f, capHeight = shape.Height * 0.16f;
			Field cap = BaseField(in type, in capShape, rng, seed + 41, capHalf, capHeight * 0.5f);
			cap.Pits = default;
			cap.Flutes = default;
			cap.Foliation = default;
			cap.Exfoliation = default;
			cap.Lump = 0.12f * cap.MeanHalf;
			cap.FloorY = -cap.Half.y * 0.6f;
			MeshBuilder capMesh = BuildField(cap, Mathf.Max(2, Mathf.RoundToInt(res * 0.9f)));
			Matrix4x4 m = Matrix4x4.Translate(new Vector3(rng.Range(-0.05f, 0.05f) * w, stem + cap.Half.y * 0.6f - capHeight * 0.15f + lean, rng.Range(-0.05f, 0.05f) * w))
				* Rotation(HorizontalAxis(rng), rng.Range(2f, 9f));
			capMesh.Transform(m);
			mesh.Append(capMesh);
			return mesh;
		}

		// ── Engine 3: convex polytope ────────────────────────────────

		/// <summary>One plane of a polytope (keep n·x ≤ d) and how its face is shaped.</summary>
		internal struct Facet
		{
			public Vector3 N;
			public float D;
			/// <summary>Depth of the scoop as a fraction of the face's width; negative domes it.</summary>
			public float Dish;
			public int Ripples;
			/// <summary>Ripple height as a fraction of the face's width.</summary>
			public float RippleRelief;
			/// <summary>Noise relief, metres, zero at the face's edges.</summary>
			public float Roughness;
		}

		internal static Facet FacetOf(Vector3 n, float d, float dish = 0f, int ripples = 0, float rippleRelief = 0f, float roughness = 0f)
		{
			return new Facet { N = n.normalized, D = d, Dish = dish, Ripples = ripples, RippleRelief = rippleRelief, Roughness = roughness };
		}

		/// <summary>Double-precision vector for clipping, so corners computed from different faces agree to far below the weld tolerance.</summary>
		internal readonly struct D3
		{
			public readonly double X, Y, Z;

			public D3(double x, double y, double z)
			{
				X = x;
				Y = y;
				Z = z;
			}

			public D3(Vector3 v) : this(v.x, v.y, v.z) { }

			public static D3 operator +(D3 a, D3 b) => new D3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
			public static D3 operator -(D3 a, D3 b) => new D3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
			public static D3 operator *(D3 a, double s) => new D3(a.X * s, a.Y * s, a.Z * s);
			public double Dot(D3 b) => X * b.X + Y * b.Y + Z * b.Z;
			public static D3 Cross(D3 a, D3 b) => new D3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
			public double Length => Math.Sqrt(Dot(this));
			public D3 Normalized => this * (1.0 / Math.Max(1e-300, Length));
			public Vector3 ToVector3() => new Vector3((float)X, (float)Y, (float)Z);
		}

		internal static List<D3> Clip(List<D3> poly, D3 n, double d)
		{
			var result = new List<D3>(poly.Count + 1);
			for (int i = 0; i < poly.Count; i++)
			{
				D3 a = poly[i], b = poly[(i + 1) % poly.Count];
				double da = n.Dot(a) - d, db = n.Dot(b) - d;
				if (da <= 0.0)
				{
					result.Add(a);
				}
				if ((da <= 0.0) != (db <= 0.0))
				{
					result.Add(a + (b - a) * (da / (da - db)));
				}
			}
			return result;
		}

		internal static long EdgeKey(int a, int b) => ((long)a << 32) | (uint)b;

		/// <summary>
		/// The convex solid the planes bound, tessellated: flat faces as fans, dished faces as rings
		/// to an off-centre point of impact. False when the planes do not close a clean solid.
		/// </summary>
		/// <param name="spacing">Edge subdivision length where a dished face needs the points.</param>
		/// <param name="rings">Concentric rings on a dished face; zero lays every face flat.</param>
		internal static bool TryPolytope(List<Facet> facets, float spacing, int rings, int seed, out MeshBuilder part)
		{
			part = null;
			int count = facets.Count;
			if (count < 4)
			{
				return false;
			}
			double extent = 0.01;
			var normals = new D3[count];
			for (int i = 0; i < count; i++)
			{
				normals[i] = new D3(facets[i].N).Normalized;
				extent = Math.Max(extent, Math.Abs(facets[i].D));
			}
			double square = extent * 8.0;

			// 1. Each face: its plane's square, clipped by every other plane.
			var polygons = new List<List<D3>>();
			var faceOf = new List<int>();
			double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
			for (int i = 0; i < count; i++)
			{
				D3 n = normals[i];
				D3 helper = Math.Abs(n.Y) < 0.9 ? new D3(0, 1, 0) : new D3(1, 0, 0);
				D3 u = D3.Cross(helper, n).Normalized;
				D3 v = D3.Cross(n, u);
				D3 c = n * facets[i].D;
				// Counter-clockwise about n, since u × v = n; clipping keeps the order.
				var poly = new List<D3> { c - u * square - v * square, c + u * square - v * square, c + u * square + v * square, c - u * square + v * square };
				for (int j = 0; j < count && poly.Count >= 3; j++)
				{
					if (j != i)
					{
						poly = Clip(poly, normals[j], facets[j].D);
					}
				}
				if (poly.Count < 3)
				{
					continue;
				}
				polygons.Add(poly);
				faceOf.Add(i);
				foreach (D3 p in poly)
				{
					minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
					minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
					minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
				}
			}
			if (polygons.Count < 4)
			{
				return false;
			}

			// 2. Weld corners across faces. The tolerance is a fraction of the solid's own size:
			// an edge shorter than that is a sliver whose triangles would be too small to carry a
			// normal, so its ends merge into one corner on every face that has it.
			double size = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
			if (size > square)
			{
				// Unbounded: a face reached the clipping square.
				return false;
			}
			double tol = Math.Max(size * 0.008, 1e-4), tol2 = tol * tol;
			var loops = new List<List<int>>();
			var owner = new List<int>();
			var reps = new List<D3>();
			for (int f = 0; f < polygons.Count; f++)
			{
				var loop = new List<int>(polygons[f].Count);
				foreach (D3 p in polygons[f])
				{
					int id = -1;
					for (int r = 0; r < reps.Count; r++)
					{
						D3 d = reps[r] - p;
						if (d.Dot(d) < tol2)
						{
							id = r;
							break;
						}
					}
					if (id < 0)
					{
						id = reps.Count;
						reps.Add(p);
					}
					if (loop.Count == 0 || loop[loop.Count - 1] != id)
					{
						loop.Add(id);
					}
				}
				while (loop.Count > 1 && loop[0] == loop[loop.Count - 1])
				{
					loop.RemoveAt(loop.Count - 1);
				}
				if (loop.Count >= 3)
				{
					loops.Add(loop);
					owner.Add(faceOf[f]);
				}
			}
			if (loops.Count < 4)
			{
				return false;
			}

			// 3. Closed and manifold: every directed edge once, with its twin; Euler characteristic 2.
			var directed = new Dictionary<long, int>();
			for (int l = 0; l < loops.Count; l++)
			{
				List<int> loop = loops[l];
				for (int e = 0; e < loop.Count; e++)
				{
					long key = EdgeKey(loop[e], loop[(e + 1) % loop.Count]);
					if (directed.ContainsKey(key))
					{
						return false;
					}
					directed[key] = l;
				}
			}
			foreach (long key in directed.Keys)
			{
				int a = (int)(key >> 32), b = (int)(key & 0xffffffffL);
				if (!directed.ContainsKey(EdgeKey(b, a)))
				{
					return false;
				}
			}
			if (reps.Count - directed.Count / 2 + loops.Count != 2)
			{
				return false;
			}

			// 4. Subdivide edges once, canonically, where a dished face needs them.
			var corner = new Vector3[reps.Count];
			for (int r = 0; r < reps.Count; r++)
			{
				corner[r] = reps[r].ToVector3();
			}
			var displaced = new bool[loops.Count];
			for (int l = 0; l < loops.Count; l++)
			{
				Facet f = facets[owner[l]];
				displaced[l] = rings > 0 && (Mathf.Abs(f.Dish) > 0f || (f.Ripples > 0 && f.RippleRelief > 0f) || f.Roughness > 0f);
			}
			var edgePoints = new Dictionary<long, Vector3[]>();
			part = new MeshBuilder(1);
			var reference = new List<Vector3>();
			var boundary = new List<Vector3>();
			for (int l = 0; l < loops.Count; l++)
			{
				List<int> loop = loops[l];
				Facet f = facets[owner[l]];
				Vector3 n = normals[owner[l]].ToVector3();
				boundary.Clear();
				for (int e = 0; e < loop.Count; e++)
				{
					int a = loop[e], b = loop[(e + 1) % loop.Count];
					boundary.Add(corner[a]);
					int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
					long key = EdgeKey(lo, hi);
					if (!edgePoints.TryGetValue(key, out Vector3[] pts))
					{
						bool sub = displaced[l] || displaced[directed[EdgeKey(b, a)]];
						D3 pa = reps[lo], pb = reps[hi];
						double len = (pb - pa).Length;
						int segs = sub ? Math.Max(1, (int)Math.Ceiling(len / Math.Max(1e-4, spacing))) : 1;
						pts = new Vector3[segs - 1];
						for (int s = 1; s < segs; s++)
						{
							pts[s - 1] = (pa + (pb - pa) * (s / (double)segs)).ToVector3();
						}
						edgePoints[key] = pts;
					}
					if (a == lo)
					{
						boundary.AddRange(pts);
					}
					else
					{
						for (int s = pts.Length - 1; s >= 0; s--)
						{
							boundary.Add(pts[s]);
						}
					}
				}
				AddPolytopeFace(part, reference, boundary, boundary.Count == loop.Count, n, in f, displaced[l] ? rings : 0, seed + l * 7919);
			}
			part.RecalculateNormals(false);
			return true;
		}

		/// <summary>One face: a fan when flat; when dished, rings from the boundary (undisplaced) to the point of impact.</summary>
		/// <param name="corners">The boundary is only the face's corners, no edge subdivisions.</param>
		internal static void AddPolytopeFace(MeshBuilder part, List<Vector3> reference, List<Vector3> boundary, bool corners, Vector3 n, in Facet f, int rings, int seed)
		{
			// UVs in metres on the face's plane, v as near to up as the face allows, so strata lie level.
			Vector3 vAxis = Vector3.up - n * n.y;
			if (vAxis.sqrMagnitude < 0.01f)
			{
				vAxis = Vector3.forward - n * n.z;
			}
			vAxis.Normalize();
			Vector3 uAxis = Vector3.Cross(vAxis, n);
			float inv = 1f / RockMeshes.TextureMetres;
			int m = boundary.Count;
			Vector3 centroid = Vector3.zero;
			foreach (Vector3 p in boundary)
			{
				centroid += p;
			}
			centroid /= m;

			if (rings <= 0)
			{
				int first = part.VertexCount;
				foreach (Vector3 p in boundary)
				{
					part.AddVertex(p, Vector3.zero, new Vector2(Vector3.Dot(p, uAxis), Vector3.Dot(p, vAxis)) * inv, White);
					reference.Add(p);
				}
				if (m == 3 || (corners && ConvexFan(boundary, n)))
				{
					// Only true corners on the boundary and every fan triangle faces out: a fan
					// from the first corner, two triangles fewer than from the centroid.
					for (int i = 1; i + 1 < m; i++)
					{
						AddOriented(part, reference, first, first + i, first + i + 1, n);
					}
					return;
				}
				int hub = part.AddVertex(centroid, Vector3.zero, new Vector2(Vector3.Dot(centroid, uAxis), Vector3.Dot(centroid, vAxis)) * inv, White);
				reference.Add(centroid);
				for (int i = 0; i < m; i++)
				{
					AddOriented(part, reference, first + i, first + (i + 1) % m, hub, n);
				}
				return;
			}

			float area = 0f;
			for (int i = 0; i < m; i++)
			{
				area += Vector3.Dot(Vector3.Cross(boundary[i] - centroid, boundary[(i + 1) % m] - centroid), n);
			}
			float width = Mathf.Sqrt(Mathf.Abs(area) * 0.5f);
			uint h = ProceduralNoise.Mix((uint)seed);
			Vector3 focus = Vector3.Lerp(centroid, boundary[(int)(h % (uint)m)], 0.2f + 0.35f * ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x9e3779b9u)));
			float dish = f.Dish * width;
			float ripple = f.RippleRelief * width;
			int start = part.VertexCount;
			for (int j = 0; j < rings; j++)
			{
				float t = j / (float)rings;
				for (int i = 0; i < m; i++)
				{
					Vector3 flat = j == 0 ? boundary[i] : Vector3.Lerp(boundary[i], focus, t);
					Vector3 p = j == 0 ? flat : flat - n * Depth(t, flat, dish, f.Ripples, ripple, f.Roughness, width, seed);
					part.AddVertex(p, Vector3.zero, new Vector2(Vector3.Dot(flat, uAxis), Vector3.Dot(flat, vAxis)) * inv, White);
					reference.Add(flat);
				}
			}
			Vector3 apex = focus - n * Depth(1f, focus, dish, f.Ripples, ripple, f.Roughness, width, seed);
			int top = part.AddVertex(apex, Vector3.zero, new Vector2(Vector3.Dot(focus, uAxis), Vector3.Dot(focus, vAxis)) * inv, White);
			reference.Add(focus);
			for (int j = 0; j + 1 < rings; j++)
			{
				for (int i = 0; i < m; i++)
				{
					int a = start + j * m + i, b = start + j * m + (i + 1) % m;
					int bb = b + m, aa = a + m;
					AddOriented(part, reference, a, b, bb, n);
					AddOriented(part, reference, a, bb, aa, n);
				}
			}
			int last = start + (rings - 1) * m;
			for (int i = 0; i < m; i++)
			{
				AddOriented(part, reference, last + i, last + (i + 1) % m, top, n);
			}
		}

		/// <summary>True when a fan from the first corner gives triangles that all face along n with real area (welding can nudge a corner a hair off convex).</summary>
		internal static bool ConvexFan(List<Vector3> boundary, Vector3 n)
		{
			float scale = (boundary[1] - boundary[0]).sqrMagnitude;
			for (int i = 1; i + 1 < boundary.Count; i++)
			{
				float a = Vector3.Dot(Vector3.Cross(boundary[i] - boundary[0], boundary[i + 1] - boundary[0]), n);
				if (a <= 1e-3f * scale)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// How far below its plane a dished face lies at ring parameter t (0 at the edge, 1 at the
		/// point of impact): a scoop deepest at the impact, concentric ripples round it (the
		/// "rings" of a conchoidal fracture), and roughness faded in from the edge.
		/// </summary>
		internal static float Depth(float t, Vector3 p, float dish, int ripples, float ripple, float roughness, float width, int seed)
		{
			float s = 1f - t;
			float d = dish * (1f - s * s);
			if (ripples > 0)
			{
				d += ripple * Mathf.Sin(Mathf.PI * ripples * t) * (1f - 0.6f * t);
			}
			if (roughness > 0f)
			{
				// Bounded by the face's width, so a small face's rough rings cannot fold over its edge.
				float r = Mathf.Min(roughness, 0.05f * width);
				d -= r * ProceduralNoise.Fbm3(p * (4f / Mathf.Max(0.05f, width)), 3, 0.5f, seed) * Smooth(0f, 0.6f, t);
			}
			return d;
		}

		/// <summary>A box, which always closes: the fallback if every redraw of a plane set failed.</summary>
		internal static MeshBuilder BoxFallback(Vector3 half)
		{
			var facets = new List<Facet>
			{
				FacetOf(Vector3.right, half.x), FacetOf(Vector3.left, half.x),
				FacetOf(Vector3.up, half.y), FacetOf(Vector3.down, half.y),
				FacetOf(Vector3.forward, half.z), FacetOf(Vector3.back, half.z),
			};
			TryPolytope(facets, 1f, 0, 0, out MeshBuilder part);
			return part;
		}

		/// <summary>Planes tangent to an ellipsoid in well-spread directions, some cut deeper: a conchoidally broken lump.</summary>
		internal static List<Facet> ConchoidalFacets(DeterministicRNG rng, Vector3 half, int count, float facetDepth, in FractureStyle fr, bool flatBase)
		{
			var facets = new List<Facet>();
			Matrix4x4 spin = Rotation(RandomAxis(rng), rng.NextFloat() * 360f);
			for (int i = 0; i < count; i++)
			{
				// A Fibonacci sphere, turned at random and jittered, keeps faces spread with no gaps.
				float y = 1f - 2f * (i + 0.5f) / count;
				float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
				float a = i * 2.39996323f;
				Vector3 n = spin.MultiplyVector(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
				n = (n + RandomAxis(rng) * 0.3f).normalized;
				if (flatBase && n.y < -0.55f)
				{
					continue;
				}
				float d = Support(half, n) * Mathf.Lerp(1f, 0.7f, facetDepth * rng.Range(0.3f, 1f));
				facets.Add(FacetOf(n, d, fr.Dish * rng.Range(0.6f, 1.4f), fr.Ripples, fr.RippleRelief * rng.Range(0.5f, 1.2f), fr.Roughness));
			}
			if (flatBase)
			{
				facets.Add(FacetOf(Vector3.down, half.y * 0.8f));
			}
			return facets;
		}

		/// <summary>Three near-orthogonal joint sets make a block; a few chips take its corners and edges.</summary>
		internal static List<Facet> JointedFacets(DeterministicRNG rng, Vector3 half, int count, float facetDepth, in FractureStyle fr, float elongation)
		{
			var facets = new List<Facet>();
			Matrix4x4 frame = Rotation(Vector3.up, rng.NextFloat() * 360f) * Rotation(HorizontalAxis(rng), rng.Range(0f, 8f));
			Vector3 x = frame.MultiplyVector(Vector3.right), y = frame.MultiplyVector(Vector3.up), z = frame.MultiplyVector(Vector3.forward);
			facets.Add(FacetOf(x, half.x * rng.Range(0.85f, 1f), fr.Dish * rng.Range(0.5f, 1.2f), 0, 0f, fr.Roughness));
			facets.Add(FacetOf(-x, half.x * rng.Range(0.85f, 1f), fr.Dish * rng.Range(0.5f, 1.2f), 0, 0f, fr.Roughness));
			facets.Add(FacetOf(z, half.z * rng.Range(0.85f, 1f), fr.Dish * rng.Range(0.5f, 1.2f), 0, 0f, fr.Roughness));
			facets.Add(FacetOf(-z, half.z * rng.Range(0.85f, 1f), fr.Dish * rng.Range(0.5f, 1.2f), 0, 0f, fr.Roughness));
			facets.Add(FacetOf(y, half.y * rng.Range(0.85f, 1f), fr.Dish * rng.Range(0.5f, 1.2f), 0, 0f, fr.Roughness));
			facets.Add(FacetOf(Vector3.down, half.y * 0.85f));
			if (elongation > 1.2f)
			{
				// A wedge: one end sheared off by a steep plane.
				Vector3 wedge = (x + y * rng.Range(0.6f, 1.1f)).normalized;
				facets.Add(FacetOf(wedge, Vector3.Dot(wedge, x * half.x * 0.55f + y * half.y * 0.2f), fr.Dish * 1.3f, fr.Ripples, fr.RippleRelief, fr.Roughness));
			}
			for (int i = 6; i < Mathf.Max(8, count); i++)
			{
				// Chips: conchoidal scars where a corner or an edge was knocked off.
				Vector3 n = RandomAxis(rng);
				n.y = Mathf.Abs(n.y) * 0.85f;
				n.Normalize();
				float boxSupport = Mathf.Abs(Vector3.Dot(n, x)) * half.x + Mathf.Abs(Vector3.Dot(n, y)) * half.y + Mathf.Abs(Vector3.Dot(n, z)) * half.z;
				float d = boxSupport * Mathf.Lerp(0.9f, 0.6f, facetDepth * rng.Range(0.4f, 1f));
				facets.Add(FacetOf(n, d, fr.Dish * 1.5f * rng.Range(0.7f, 1.3f), fr.Ripples, fr.RippleRelief, fr.Roughness));
			}
			return facets;
		}

		/// <summary>A polygonal prism along y of the given radius and length, its ends broken or cupped.</summary>
		internal static List<Facet> PrismFacets(DeterministicRNG rng, int sides, float radius, float length, in ColumnStyle cs, float topBroken)
		{
			var facets = new List<Facet>();
			float turn = rng.NextFloat() * Tau;
			for (int k = 0; k < sides; k++)
			{
				float a = turn + Tau * k / sides + rng.Range(-0.12f, 0.12f);
				facets.Add(FacetOf(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), radius * rng.Range(0.92f, 1.05f)));
			}
			for (int end = -1; end <= 1; end += 2)
			{
				float tilt = Range(rng, cs.TopTilt);
				Vector3 n = Tilted(tilt, rng.NextFloat() * Tau);
				n.y *= end;
				bool broken = rng.NextFloat() < topBroken;
				float dish = broken ? 0.06f : (rng.NextFloat() < 0.7f ? 1f : -0.7f) * cs.Cup / Mathf.Max(0.05f, radius * 2f);
				facets.Add(FacetOf(n, length * 0.5f * rng.Range(0.9f, 1f), dish, broken ? 2 : 0, broken ? 0.008f : 0f, broken ? 0.008f : 0.003f));
			}
			return facets;
		}

		/// <summary>A cleaved plate: two parallel cleavage faces and a ring of broken edge faces at various bevels.</summary>
		internal static List<Facet> PlateFacets(DeterministicRNG rng, float a, float b, float thickness, in CleavageStyle cl, float roughness)
		{
			var facets = new List<Facet>
			{
				FacetOf(Vector3.up, thickness * 0.5f, 0f, 0, 0f, roughness),
				FacetOf(Vector3.down, thickness * 0.5f, 0f, 0, 0f, roughness),
			};
			int edges = Mathf.Max(5, cl.EdgeFacets) + rng.Next(3);
			float turn = rng.NextFloat() * Tau;
			for (int k = 0; k < edges; k++)
			{
				float theta = turn + Tau * k / edges + rng.Range(-0.3f, 0.3f) * Tau / edges;
				float bevel = rng.Range(-1f, 1f) * cl.EdgeBevel * 0.7f;
				float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
				var n = new Vector3(c * Mathf.Cos(bevel), Mathf.Sin(bevel), s * Mathf.Cos(bevel));
				float reach = Mathf.Sqrt(a * c * a * c + b * s * b * s) * rng.Range(0.82f, 1f);
				facets.Add(FacetOf(n, reach * Mathf.Cos(bevel) + thickness * 0.5f * Mathf.Abs(Mathf.Sin(bevel))));
			}
			return facets;
		}

		/// <summary>
		/// A slab to stand on end: square sides and a square-cut foot (+z, which the stand-up
		/// rotation turns downward), and a top (−z) broken by two or three chips, as a slate post
		/// is split from the bed and its top knocked off.
		/// </summary>
		internal static List<Facet> StandingFacets(DeterministicRNG rng, float a, float b, float thickness, float roughness)
		{
			var facets = new List<Facet>
			{
				FacetOf(Vector3.up, thickness * 0.5f, 0f, 0, 0f, roughness),
				FacetOf(Vector3.down, thickness * 0.5f, 0f, 0, 0f, roughness),
				FacetOf(new Vector3(1f, 0f, rng.Range(-0.06f, 0.06f)), a * rng.Range(0.9f, 1f)),
				FacetOf(new Vector3(-1f, 0f, rng.Range(-0.06f, 0.06f)), a * rng.Range(0.9f, 1f)),
				FacetOf(Vector3.forward, b),
				FacetOf(new Vector3(rng.Range(-0.25f, 0.25f), 0f, -1f), b * rng.Range(0.88f, 1f)),
			};
			int chips = 2 + rng.Next(2);
			for (int k = 0; k < chips; k++)
			{
				float side = k % 2 == 0 ? 1f : -1f;
				var n = new Vector3(side * rng.Range(0.5f, 1.2f), rng.Range(-0.3f, 0.3f), -1f).normalized;
				float corner = Mathf.Abs(n.x) * a + Mathf.Abs(n.z) * b + Mathf.Abs(n.y) * thickness * 0.5f;
				facets.Add(FacetOf(n, corner * rng.Range(0.72f, 0.88f)));
			}
			return facets;
		}

		/// <summary>Builds a polytope from a plane-set generator, redrawing until it closes cleanly; a box if it never does.</summary>
		internal static MeshBuilder Polytope(Func<List<Facet>> draw, float spacing, int rings, int seed, Vector3 fallbackHalf)
		{
			for (int attempt = 0; attempt < 24; attempt++)
			{
				if (TryPolytope(draw(), spacing, rings, seed + attempt, out MeshBuilder part))
				{
					return part;
				}
			}
			return BoxFallback(fallbackHalf);
		}

		internal static int DishRings(int level) => level == 2 ? 4 : level == 1 ? 2 : 0;

		/// <summary>
		/// A faceted rock: conchoidal (planes falling every way, deeply scooped and rippled, edges
		/// left razor sharp), jointed (a block of three joint sets with chipped corners), or, for
		/// a columnar rock, a broken length of column lying on its side.
		/// </summary>
		internal static MeshBuilder Faceted(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			FractureStyle fr = st.Fracture;
			int level = Level(res);
			float spacing = shape.Size / (1.1f * res);
			int rings = DishRings(level);
			float elongation = shape.Elongation > 0f ? shape.Elongation : 1f;
			float aspect = Range(rng, st.Aspect);
			var half = new Vector3(shape.Size * 0.5f, shape.Height * 0.5f, shape.Size * 0.5f * aspect / elongation);
			int facetCount = Mathf.Max(6, st.Facets) + rng.Next(3);
			float facetDepth = st.FacetDepth;

			if (st.Columns.Diameter.y > 0f)
			{
				ColumnStyle cs = st.Columns;
				int sides = SidesFor(rng);
				float radius = shape.Height * 0.5f;
				float length = shape.Size * 0.95f;
				MeshBuilder chunk = Polytope(() => PrismFacets(rng, sides, radius, length, in cs, 0.6f), spacing, rings, seed, new Vector3(radius, length * 0.5f, radius));
				chunk.Transform(Rotation(Vector3.up, rng.NextFloat() * 360f) * Rotation(Vector3.forward, 90f + rng.Range(-8f, 8f)));
				return chunk;
			}
			if (fr.Jointed)
			{
				return Polytope(() => JointedFacets(rng, half, facetCount, facetDepth, in fr, elongation), spacing, rings, seed, half);
			}
			return Polytope(() => ConchoidalFacets(rng, half, facetCount, facetDepth, in fr, true), spacing, rings, seed, half);
		}

		/// <summary>Columns are mostly hexagonal, with pentagons and heptagons as the defects.</summary>
		internal static int SidesFor(DeterministicRNG rng)
		{
			float u = rng.NextFloat();
			return u < 0.55f ? 6 : u < 0.8f ? 5 : 7;
		}

		/// <summary>
		/// Plates split along cleavage (slate) or fissility (shale), stacked and shingled, each a
		/// little smaller and pushed a little further over, then tilted together; or one thick
		/// plate stood on end.
		/// </summary>
		internal static MeshBuilder SlabStack(in RockType type, in FormationShape shape, int res, int seed, bool standing)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			CleavageStyle cl = st.Cleavage;
			if (cl.Thickness.y <= 0f)
			{
				cl.Thickness = new Vector2(0.06f, 0.12f);
			}
			int level = Level(res);
			float spacing = shape.Size / (0.8f * res);
			int rings = level == 2 ? 2 : level == 1 ? 1 : 0;
			int count = standing ? 1 : Mathf.Max(1, shape.Count > 0 ? shape.Count : 4);
			float tilt = standing ? 90f - rng.Range(4f, 14f) : (shape.Dip >= 0f ? shape.Dip : Range(rng, cl.Tilt));
			float aspect = Range(rng, st.Aspect);
			float a = shape.Size * 0.5f;
			float b = standing ? shape.Height * 0.5f : a * aspect;
			float roughness = Mathf.Max(st.Fracture.Roughness, 0.002f);
			float stepAz = rng.NextFloat() * Tau;
			var step = new Vector2(Mathf.Cos(stepAz), Mathf.Sin(stepAz)) * a * 0.12f;
			var result = new MeshBuilder(1);
			float lift = 0f;
			Vector2 shift = Vector2.zero;
			for (int j = 0; j < count; j++)
			{
				float thick = Range(rng, cl.Thickness) * (standing ? 1.6f : 1f);
				float scale = standing ? 1f : Mathf.Lerp(1f, 0.55f, j / (float)count) * rng.Range(0.85f, 1.05f);
				float pa = a * scale, pb = b * scale;
				MeshBuilder plate = standing
					? Polytope(() => StandingFacets(rng, pa, pb, thick, roughness), spacing, rings, seed + j * 131, new Vector3(pa, thick * 0.5f, pb))
					: Polytope(() => PlateFacets(rng, pa, pb, thick, in cl, roughness), spacing, rings, seed + j * 131, new Vector3(pa, thick * 0.5f, pb));
				plate.Transform(Matrix4x4.Translate(new Vector3(shift.x, lift + thick * 0.5f, shift.y)) * Rotation(Vector3.up, rng.Range(-12f, 12f)));
				result.Append(plate);
				lift += thick - 0.003f;
				shift += step + new Vector2(rng.Range(-1f, 1f), rng.Range(-1f, 1f)) * a * 0.06f;
			}
			result.Transform(Rotation(Vector3.up, rng.NextFloat() * 360f) * Rotation(Vector3.right, tilt));
			return result;
		}

		/// <summary>A column cluster: Voronoi prisms of a jittered hexagonal lattice, tops tilted, broken and cupped.</summary>
		internal static MeshBuilder Columns(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			ColumnStyle cs = type.Style.Columns;
			if (cs.Diameter.y <= 0f)
			{
				cs.Diameter = new Vector2(0.45f, 0.6f);
				cs.TopTilt = new Vector2(4f, 20f);
			}
			int level = Level(res);
			float diameter = Range(rng, cs.Diameter);
			int count = Mathf.Clamp(shape.Count > 0 ? shape.Count : 9, 1, 37);
			float aspect = Range(rng, type.Style.Aspect);
			bool causeway = shape.Height < shape.Size * 0.5f;

			// A jittered hexagonal lattice five rings out; the kept columns are the nearest to the
			// centre (in a metric stretched to the style's aspect), the rest only bound them.
			var sites = new List<Vector2>();
			var keys = new List<float>();
			for (int q = -5; q <= 5; q++)
			{
				for (int r = -5; r <= 5; r++)
				{
					if (Mathf.Abs(q + r) > 5)
					{
						continue;
					}
					var p = new Vector2(diameter * (q + r * 0.5f), diameter * r * 0.8660254f);
					p += new Vector2(rng.Range(-0.2f, 0.2f), rng.Range(-0.2f, 0.2f)) * diameter;
					sites.Add(p);
					keys.Add(p.x * p.x + p.y * p.y / (aspect * aspect));
				}
			}
			var order = new List<int>();
			for (int i = 0; i < sites.Count; i++)
			{
				order.Add(i);
			}
			order.Sort((x, y) => keys[x] != keys[y] ? keys[x].CompareTo(keys[y]) : x.CompareTo(y));
			count = Mathf.Min(count, order.Count);
			float keepRadius = Mathf.Sqrt(keys[order[count - 1]]) + 1e-4f;

			float spacing = diameter / (0.5f * res);
			int rings = level == 2 ? 3 : level == 1 ? 1 : 0;
			var mesh = new MeshBuilder(1);
			for (int k = 0; k < count; k++)
			{
				int index = order[k];
				Vector2 c = sites[index];
				float rr = Mathf.Sqrt(keys[index]) / keepRadius;
				float height;
				if (causeway)
				{
					float field = ProceduralNoise.Fbm3(new Vector3(c.x, 0f, c.y) / (diameter * 2.2f), 2, 0.5f, seed + 3);
					height = shape.Height * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(0.5f + 1.4f * field)) * rng.Range(0.85f, 1f);
				}
				else
				{
					height = shape.Height * (0.45f + 0.55f * (1f - 0.8f * rr * rr) * rng.Range(0.7f, 1f));
				}
				var neighbours = new List<Facet>();
				foreach (Vector2 t in sites)
				{
					Vector2 delta = t - c;
					float len = delta.magnitude;
					if (len < 1e-5f || len > diameter * 2.3f)
					{
						continue;
					}
					Vector2 dir = delta / len;
					neighbours.Add(FacetOf(new Vector3(dir.x, 0f, dir.y), Vector2.Dot(dir, (c + t) * 0.5f) - cs.Gap * 0.5f));
				}
				float h = height;
				Vector3 centre = new Vector3(c.x, 0f, c.y);
				MeshBuilder column = Polytope(() =>
				{
					var facets = new List<Facet>(neighbours) { FacetOf(Vector3.down, 0f) };
					float azimuth = rng.NextFloat() * Tau;
					Vector3 n = Tilted(Range(rng, cs.TopTilt), azimuth);
					float cup = (rng.NextFloat() < 0.7f ? 1f : -0.7f) * cs.Cup / diameter;
					facets.Add(FacetOf(n, Vector3.Dot(n, centre + Vector3.up * h), cup, 0, 0f, 0.003f));
					if (rng.NextFloat() < cs.Broken)
					{
						// A broken wedge: a steep conchoidal break across one side of the top.
						float az2 = azimuth + Mathf.PI + rng.Range(-0.7f, 0.7f);
						Vector3 n2 = Tilted(rng.Range(32f, 55f), az2);
						Vector3 at = centre + new Vector3(Mathf.Cos(az2), 0f, Mathf.Sin(az2)) * diameter * 0.18f + Vector3.up * (h - diameter * rng.Range(0.05f, 0.3f));
						facets.Add(FacetOf(n2, Vector3.Dot(n2, at), 0.06f, 2, 0.006f, 0.008f));
					}
					h *= 0.985f;
					return facets;
				}, spacing, rings, seed + 31 * k, new Vector3(diameter * 0.5f, height * 0.5f, diameter * 0.5f));
				mesh.Append(column);
			}
			// The whole cluster leans a little together, as columns stand normal to a cooling surface.
			mesh.Transform(Rotation(HorizontalAxis(rng), rng.Range(0f, 6f)) * Rotation(Vector3.up, rng.NextFloat() * 360f));
			return mesh;
		}

		/// <summary>Broken lengths of column lying on their sides, a layer on the ground and a few resting in the grooves on top.</summary>
		internal static MeshBuilder FallenColumns(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			ColumnStyle cs = type.Style.Columns;
			if (cs.Diameter.y <= 0f)
			{
				cs.Diameter = new Vector2(0.45f, 0.6f);
				cs.TopTilt = new Vector2(4f, 20f);
			}
			int level = Level(res);
			int count = Mathf.Max(1, shape.Count > 0 ? shape.Count : 4);
			int lower = Mathf.CeilToInt(count * 0.6f);
			float diameter = Range(rng, cs.Diameter);
			float spacing = diameter / (0.5f * res);
			int rings = level == 2 ? 3 : level == 1 ? 1 : 0;
			float baseYaw = rng.NextFloat() * 360f;
			var mesh = new MeshBuilder(1);
			for (int k = 0; k < count; k++)
			{
				bool upper = k >= lower;
				int layerIndex = upper ? k - lower : k;
				int layerCount = upper ? count - lower : lower;
				int sides = SidesFor(rng);
				float length = shape.Size * rng.Range(0.55f, 0.9f);
				float radius = diameter * 0.5f;
				MeshBuilder column = Polytope(() => PrismFacets(rng, sides, radius, length, in cs, 0.5f), spacing, rings, seed + 31 * k, new Vector3(radius, length * 0.5f, radius));
				float z = (layerIndex - (layerCount - 1) * 0.5f) * diameter * 0.92f;
				float y = radius * 0.9f + (upper ? diameter * 0.78f : 0f);
				float x = rng.Range(-0.15f, 0.15f) * length;
				column.Transform(Matrix4x4.Translate(new Vector3(x, y, z))
					* Rotation(Vector3.up, rng.Range(-12f, 12f))
					* Rotation(Vector3.forward, 90f + rng.Range(-4f, 4f))
					* Rotation(Vector3.up, rng.NextFloat() * 360f));
				mesh.Append(column);
			}
			mesh.Transform(Rotation(Vector3.up, baseYaw));
			return mesh;
		}

		// ── Fragments and scree ──────────────────────────────────────

		/// <summary>One loose fragment of the type, centred on the origin: angular, platy or rounded as the rock breaks.</summary>
		internal static MeshBuilder Fragment(in RockType type, float size, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			int level = Level(res);
			int fragmentRings = level == 2 ? 2 : level == 1 ? 1 : 0;
			if (st.Fragments == FragmentForm.Rounded)
			{
				var f = new Field
				{
					Seed = seed,
					Half = new Vector3(size * 0.5f, size * rng.Range(0.28f, 0.36f), size * rng.Range(0.36f, 0.45f)),
					Exponent = rng.Range(2f, 2.6f),
				};
				f.Lump = (0.3f + st.Lumpiness) * 0.12f * f.MeanHalf;
				f.LumpScale = f.MeanHalf;
				f.Pits = st.Pits;
				f.Pits.Depth *= Mathf.Clamp01(size / 0.6f);
				return BuildField(f, Mathf.Max(2, (res + 1) / 2));
			}
			float spacing = size / Mathf.Max(1f, 0.3f * res);
			int rings = fragmentRings;
			if (st.Fragments == FragmentForm.Platy)
			{
				CleavageStyle cl = st.Cleavage;
				float thick = cl.Thickness.y > 0f ? Range(rng, cl.Thickness) : size * 0.15f;
				thick = Mathf.Min(thick, size * 0.3f);
				float pa = size * 0.5f, pb = pa * rng.Range(0.55f, 0.85f);
				float roughness = Mathf.Max(st.Fracture.Roughness, 0.002f);
				return Polytope(() => PlateFacets(rng, pa, pb, thick, in cl, roughness), spacing, rings, seed, new Vector3(pa, thick * 0.5f, pb));
			}
			FractureStyle fr = st.Fracture;
			if (fr.Dish <= 0f)
			{
				fr.Dish = 0.03f;
			}
			var half = new Vector3(size * 0.5f, size * rng.Range(0.24f, 0.36f), size * rng.Range(0.3f, 0.45f));
			int count = 8 + rng.Next(3);
			return Polytope(() => ConchoidalFacets(rng, half, count, 0.6f, in fr, false), spacing, rings, seed, half);
		}

		/// <summary>
		/// Platy talus: chips dropped one by one from the outside of the heap inward, each landing
		/// on whatever is already beneath it and tipped outward down the slope (imbricated, as
		/// plates come to rest on a talus cone). No smooth core: the heap is chips all through.
		/// </summary>
		internal static MeshBuilder PlatyPile(in RockType type, int count, float radius, float aspect, int res, int seed, DeterministicRNG rng)
		{
			var mesh = new MeshBuilder(1);
			count = Mathf.Max(count, 12);
			float squash = Mathf.Lerp(aspect, 1f, 0.5f);
			for (int k = 0; k < count; k++)
			{
				float u = k / (float)(count - 1);
				// Outer chips first and largest; the inner ones land on them and build the heap up.
				float size = radius * 0.5f * Mathf.Lerp(1f, 0.55f, u) * rng.Range(0.85f, 1.1f);
				float r = radius * 0.82f * Mathf.Pow(1f - u, 0.6f) * rng.Range(0.8f, 1.05f);
				float theta = k * 2.39996323f + rng.Range(-0.3f, 0.3f);
				var radial = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
				float x = radial.x * r, z = radial.z * r * squash;
				MeshBuilder chip = Fragment(in type, size, res, seed + 7919 * (k + 1));
				// Tipped outward by the slope it lands on (steeper toward the foot), turned at random.
				float tip = Mathf.Lerp(8f, 30f, r / radius) + rng.Range(-6f, 6f);
				chip.Transform(Rotation(Vector3.Cross(Vector3.up, radial), tip) * Rotation(Vector3.up, rng.NextFloat() * 360f));
				// Land it on the highest point already under its footprint. A flat chip's vertices are
				// at its rim, so the search covers most of the new chip, not just its middle.
				float support = 0f;
				float reach2 = size * 0.45f * size * 0.45f;
				foreach (Vector3 p in mesh.Positions)
				{
					float dx = p.x - x, dz = p.z - z;
					if (dx * dx + dz * dz < reach2 && p.y > support)
					{
						support = p.y;
					}
				}
				// No higher than the angle of repose allows (about 32°): a chip that would perch above
				// the cone slides down into the heap, which is what keeps it a heap and not a tower.
				support = Mathf.Min(support, Mathf.Max(0f, radius * 0.95f - r) * 0.62f);
				float bottom = chip.Bounds.min.y;
				chip.Transform(Matrix4x4.Translate(new Vector3(x, support - bottom - size * 0.03f, z)));
				mesh.Append(chip);
			}
			return mesh;
		}

		internal static MeshBuilder Scree(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			int count = Mathf.Max(3, shape.Count > 0 ? shape.Count : 14);
			float radius = shape.Size * 0.5f;
			float aspect = Range(rng, st.Aspect);
			var mesh = new MeshBuilder(1);

			if (st.Fragments == FragmentForm.Platy)
			{
				return PlatyPile(in type, count, radius, aspect, res, seed, rng);
			}

			// The core: a low lumpy mound under the fragments.
			var core = new Field
			{
				Seed = seed + 1,
				Half = new Vector3(radius * 0.62f, shape.Height * 0.5f, radius * 0.62f * aspect),
				Exponent = 2.2f,
			};
			core.Lump = 0.12f * core.MeanHalf;
			core.LumpScale = core.MeanHalf * 0.6f;
			core.FloorY = -core.Half.y * 0.2f;
			core.FloorSoft = 0.05f * core.MeanHalf;
			mesh.Append(BuildField(core, Mathf.Max(2, (res + 1) / 2)));

			for (int k = 0; k < count; k++)
			{
				float u = count > 1 ? k / (float)(count - 1) : 0f;
				// Sorted by size: the biggest blocks roll furthest, to the foot of the heap.
				float size = radius * (st.Fragments == FragmentForm.Platy ? 0.42f : 0.6f) * Mathf.Lerp(1f, 0.45f, Mathf.Pow(u, 0.7f)) * rng.Range(0.85f, 1.1f);
				float r = radius * Mathf.Lerp(0.8f, 0.08f, Mathf.Pow(u, 0.75f)) * rng.Range(0.9f, 1.05f);
				float theta = k * 2.39996323f + rng.Range(-0.3f, 0.3f);
				float x = Mathf.Cos(theta) * r, z = Mathf.Sin(theta) * r * Mathf.Lerp(aspect, 1f, 0.5f);
				float rel = new Vector2(x / core.Half.x, z / core.Half.z).magnitude;
				float ground = rel < 1f ? core.Half.y * Mathf.Sqrt(1f - rel * rel) : 0f;
				int fragmentSeed = seed + 7919 * (k + 1);
				MeshBuilder fragment = Fragment(in type, size, res, fragmentSeed);
				Matrix4x4 turn = st.Fragments == FragmentForm.Platy
					? Rotation(HorizontalAxis(rng), rng.Range(-18f, 18f)) * Rotation(Vector3.up, rng.NextFloat() * 360f)
					: Rotation(RandomAxis(rng), rng.NextFloat() * 360f);
				fragment.Transform(Matrix4x4.Translate(new Vector3(x, Mathf.Max(0f, ground) + size * 0.12f, z)) * turn);
				mesh.Append(fragment);
			}
			return mesh;
		}
	}
}
#endif
