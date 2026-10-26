#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	// ── Cave crystal habits (2026-10-26, NEW-CRYSTALS, decision 34) ────
	//
	// The habits the biome drafts name for their cave crystals beyond the first seven: grape clusters, twins, helictites, massive
	// columns, coralloid sprays, foliated books, fibrous sheaves, rosettes, octahedra, bipyramids, dendrites, pearls, floss, cups,
	// barrels, stars, droplets, rhombohedra, plates, needle sprays, leaves, dodecahedra, trapezohedra, trapiche sections and nodules
	// in a host. Each is built as the first seven are (CaveGlowMeshes.BuildMineral): two submeshes, the host rock (0) and the crystal
	// (1, u a glow level), floor-mounted at its foot, its form drawn before anything depends on the level, the smaller half of its
	// parts dropped at the last level. Real crystal faces are planes, so most are flat-faced solids (Solid, Column); drops, pearls,
	// grapes and filaments are smooth.
	public static partial class CaveGlowMeshes
	{
		/// <summary>A level of one of the drafts' habits (<see cref="BuildMineral"/> sends every habit past the first seven here).</summary>
		private static MeshBuilder BuildHabit(CaveMineralHabit habit, int variant, int lod, int seed)
		{
			switch (habit)
			{
				case CaveMineralHabit.Botryoidal: return Botryoidal(variant, lod, seed);
				case CaveMineralHabit.Twinned: return Twinned(variant, lod, seed);
				case CaveMineralHabit.Helix: return Helictites(variant, lod, seed);
				case CaveMineralHabit.Massive: return Massive(variant, lod, seed);
				case CaveMineralHabit.Coralloid: return Coralloid(variant, lod, seed);
				case CaveMineralHabit.Foliated: return Foliated(variant, lod, seed);
				case CaveMineralHabit.Fibrous: return Fibrous(variant, lod, seed);
				case CaveMineralHabit.Rosette: return Rosettes(variant, lod, seed);
				case CaveMineralHabit.Octahedral: return Polyhedra(variant, lod, seed, Octahedron, new[] { 6, 3, 11, 5 }, new[] { 0.24f, 0.34f, 0.14f, 0.2f }, new[] { 0.1f, 0.22f, 0.05f, 0.1f });
				case CaveMineralHabit.Bipyramid: return Bipyramids(variant, lod, seed);
				case CaveMineralHabit.Dendrite: return Dendrites(variant, lod, seed);
				case CaveMineralHabit.Pearls: return Pearls(variant, lod, seed);
				case CaveMineralHabit.Floss: return Floss(variant, lod, seed);
				case CaveMineralHabit.Cupped: return Cups(variant, lod, seed);
				case CaveMineralHabit.Barrel: return Barrels(variant, lod, seed);
				case CaveMineralHabit.Stellate: return Stars(variant, lod, seed);
				case CaveMineralHabit.Droplet: return Droplets(variant, lod, seed);
				case CaveMineralHabit.Rhomb: return Polyhedra(variant, lod, seed, Rhombohedron, new[] { 5, 2, 9, 4 }, new[] { 0.28f, 0.36f, 0.16f, 0.26f }, new[] { 0.12f, 0.3f, 0.06f, 0.14f });
				case CaveMineralHabit.Tabular: return Plates(variant, lod, seed);
				case CaveMineralHabit.Radiating: return Sprays(variant, lod, seed);
				case CaveMineralHabit.Leaf: return Leaves(variant, lod, seed);
				case CaveMineralHabit.Dodecahedral: return Polyhedra(variant, lod, seed, RhombicDodecahedron, new[] { 7, 4, 12, 6 }, new[] { 0.16f, 0.3f, 0.09f, 0.18f }, new[] { 0.07f, 0.08f, 0.04f, 0.08f });
				case CaveMineralHabit.Trapezohedral: return Polyhedra(variant, lod, seed, Icositetrahedron, new[] { 6, 3, 11, 8 }, new[] { 0.15f, 0.24f, 0.09f, 0.13f }, new[] { 0.06f, 0.14f, 0.035f, 0.05f });
				case CaveMineralHabit.Trapiche: return TrapicheCrystals(variant, lod, seed);
				case CaveMineralHabit.Matrix: return HostNodules(variant, lod, seed);
				default: return Blades(variant, lod, seed);
			}
		}

		// ── Solids ────────────────────────────────────────────────────

		/// <summary>
		/// A flat-faced solid: each face (corner indices round its edge, either way) a plane of its own normal, turned away from the
		/// solid's centre (so the solid must be convex about it, or each face's own), fanned into triangles; u a glow level rising
		/// with its height in its own frame, then placed by <paramref name="place"/>.
		/// </summary>
		private static void Solid(MeshBuilder m, int sub, Matrix4x4 place, IList<Vector3> corners, IList<int[]> faces, float glowLow, float glowHigh, float v0)
		{
			int first = m.VertexCount;
			Vector3 centre = Vector3.zero;
			float low = float.MaxValue, high = float.MinValue;
			foreach (Vector3 p in corners)
			{
				centre += p;
				low = Mathf.Min(low, p.y);
				high = Mathf.Max(high, p.y);
			}
			centre /= corners.Count;
			for (int f = 0; f < faces.Count; f++)
			{
				int[] face = faces[f];
				// Newell's normal: exact for a plane, the best fit for a face a little off one.
				Vector3 n = Vector3.zero, mid = Vector3.zero;
				for (int i = 0; i < face.Length; i++)
				{
					Vector3 a = corners[face[i]], b = corners[face[(i + 1) % face.Length]];
					n.x += (a.y - b.y) * (a.z + b.z);
					n.y += (a.z - b.z) * (a.x + b.x);
					n.z += (a.x - b.x) * (a.y + b.y);
					mid += a;
				}
				if (n.sqrMagnitude < 1e-16f)
				{
					continue;
				}
				n.Normalize();
				mid /= face.Length;
				if (Vector3.Dot(n, mid - centre) < 0f)
				{
					n = -n;
				}
				int a0 = m.VertexCount;
				for (int i = 0; i < face.Length; i++)
				{
					Vector3 p = corners[face[i]];
					float level = Mathf.Lerp(glowLow, glowHigh, high > low ? (p.y - low) / (high - low) : 1f);
					m.AddVertex(p, n, new Vector2(Glow(level), v0 + 0.13f * f + ((i & 1) == 0 ? 0f : 0.11f)), White);
				}
				for (int i = 1; i + 1 < face.Length; i++)
				{
					m.AddTriangle(sub, a0, a0 + i, a0 + i + 1, n);
				}
			}
			m.Transform(place, first);
		}

		/// <summary>
		/// A many-sided column about +y: rings of <paramref name="sides"/> corners at the same angles (each ring a scaled copy of the
		/// first, so every side is a plane), <paramref name="rings"/> as (scale, height); closed by a point <paramref name="apexTop"/>
		/// above the top ring and <paramref name="apexBottom"/> below the bottom one (0: a flat end). Corners jittered in radius by
		/// up to <paramref name="jitter"/>; a pyramid's point a little off the axis.
		/// </summary>
		private static void Column(MeshBuilder m, int sub, Matrix4x4 place, int sides, float radius, IList<Vector2> rings, float apexTop, float apexBottom,
			float jitter, DeterministicRNG rng, float glowLow, float glowHigh)
		{
			float turn = rng.NextFloat() * Tau;
			var corner = new Vector2[sides];
			for (int i = 0; i < sides; i++)
			{
				float a = turn + Tau * i / sides;
				float r = radius * (1f + rng.Range(-jitter, jitter));
				corner[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
			}
			var points = new List<Vector3>(sides * rings.Count + 2);
			var faces = new List<int[]>();
			for (int j = 0; j < rings.Count; j++)
			{
				for (int i = 0; i < sides; i++)
				{
					points.Add(new Vector3(corner[i].x * rings[j].x, rings[j].y, corner[i].y * rings[j].x));
				}
			}
			for (int j = 0; j + 1 < rings.Count; j++)
			{
				for (int i = 0; i < sides; i++)
				{
					int k = (i + 1) % sides;
					faces.Add(new[] { j * sides + i, j * sides + k, (j + 1) * sides + k, (j + 1) * sides + i });
				}
			}
			int top = (rings.Count - 1) * sides;
			float off = radius * 0.08f;
			if (apexTop > 0f)
			{
				int apex = points.Count;
				points.Add(new Vector3(rng.Range(-off, off), rings[rings.Count - 1].y + apexTop, rng.Range(-off, off)));
				for (int i = 0; i < sides; i++)
				{
					faces.Add(new[] { top + i, top + (i + 1) % sides, apex });
				}
			}
			else
			{
				var cap = new int[sides];
				for (int i = 0; i < sides; i++)
				{
					cap[i] = top + i;
				}
				faces.Add(cap);
			}
			if (apexBottom > 0f)
			{
				int apex = points.Count;
				points.Add(new Vector3(rng.Range(-off, off), rings[0].y - apexBottom, rng.Range(-off, off)));
				for (int i = 0; i < sides; i++)
				{
					faces.Add(new[] { i, (i + 1) % sides, apex });
				}
			}
			else
			{
				var cap = new int[sides];
				for (int i = 0; i < sides; i++)
				{
					cap[i] = i;
				}
				faces.Add(cap);
			}
			Solid(m, sub, place, points, faces, glowLow, glowHigh, rng.NextFloat());
		}

		/// <summary>A convex polygon in the x–z plane, extruded from <paramref name="y0"/> up to <paramref name="y1"/>.</summary>
		private static void Extruded(MeshBuilder m, int sub, Matrix4x4 place, IList<Vector2> polygon, float y0, float y1, float glowLow, float glowHigh, float v0)
		{
			int n = polygon.Count;
			var points = new List<Vector3>(2 * n);
			foreach (Vector2 p in polygon)
			{
				points.Add(new Vector3(p.x, y0, p.y));
			}
			foreach (Vector2 p in polygon)
			{
				points.Add(new Vector3(p.x, y1, p.y));
			}
			var faces = new List<int[]>(n + 2);
			var bottom = new int[n];
			var top = new int[n];
			for (int i = 0; i < n; i++)
			{
				int k = (i + 1) % n;
				faces.Add(new[] { i, k, n + k, n + i });
				bottom[i] = i;
				top[i] = n + i;
			}
			faces.Add(bottom);
			faces.Add(top);
			Solid(m, sub, place, points, faces, glowLow, glowHigh, v0);
		}

		/// <summary>A solid's corners and faces, unit size about its centre (<see cref="Polyhedra"/>).</summary>
		private delegate void Shape(DeterministicRNG rng, int variant, out Vector3[] corners, out int[][] faces);

		/// <summary>An octahedron (two square pyramids base to base); v3's stretched along its axis.</summary>
		private static void Octahedron(DeterministicRNG rng, int variant, out Vector3[] corners, out int[][] faces)
		{
			float tall = variant == 3 ? 1.45f : 1f;
			corners = new[] { new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(0f, tall, 0f), new Vector3(0f, -tall, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f) };
			faces = new[]
			{
				new[] { 0, 2, 4 }, new[] { 4, 2, 1 }, new[] { 1, 2, 5 }, new[] { 5, 2, 0 },
				new[] { 0, 4, 3 }, new[] { 4, 1, 3 }, new[] { 1, 5, 3 }, new[] { 5, 0, 3 },
			};
		}

		/// <summary>
		/// A rhombohedron: three equal edges at one angle to each other (75° as calcite's cleavage; v3's 62°, sharper), its six
		/// faces rhombi. Corners about its centre, edge 1.
		/// </summary>
		private static void Rhombohedron(DeterministicRNG rng, int variant, out Vector3[] corners, out int[][] faces)
		{
			float theta = (variant == 3 ? 62f : 75f + rng.Range(-3f, 3f)) * Mathf.Deg2Rad;
			float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
			var e1 = new Vector3(1f, 0f, 0f);
			var e2 = new Vector3(c, s, 0f);
			float y3 = (c - c * c) / s;
			var e3 = new Vector3(c, y3, Mathf.Sqrt(Mathf.Max(0.01f, 1f - c * c - y3 * y3)));
			Vector3 half = (e1 + e2 + e3) * 0.5f;
			corners = new Vector3[8];
			for (int i = 0; i < 8; i++)
			{
				corners[i] = ((i & 1) != 0 ? e1 : Vector3.zero) + ((i & 2) != 0 ? e2 : Vector3.zero) + ((i & 4) != 0 ? e3 : Vector3.zero) - half;
			}
			// One face for each corner index bit held at 0 and at 1.
			faces = new[]
			{
				new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 },
				new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 },
				new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 },
			};
		}

		/// <summary>A rhombic dodecahedron (garnet's): twelve rhombi, the cube's eight corners and six points out along its axes.</summary>
		private static void RhombicDodecahedron(DeterministicRNG rng, int variant, out Vector3[] corners, out int[][] faces)
		{
			var list = new List<Vector3>(14);
			for (int i = 0; i < 8; i++)
			{
				list.Add(new Vector3((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f));
			}
			for (int axis = 0; axis < 3; axis++)
			{
				for (int sign = -1; sign <= 1; sign += 2)
				{
					var p = Vector3.zero;
					p[axis] = sign;
					list.Add(p);
				}
			}
			var faceList = new List<int[]>(12);
			for (int a = 0; a < 3; a++)
			{
				for (int b = a + 1; b < 3; b++)
				{
					int c = 3 - a - b;
					for (int sa = 0; sa < 2; sa++)
					{
						for (int sb = 0; sb < 2; sb++)
						{
							// The axis points at a and b, and the two cube corners between them (c either way).
							int pa = 8 + 2 * a + sa, pb = 8 + 2 * b + sb;
							int lo = (sa << a) | (sb << b), hi = lo | (1 << c);
							faceList.Add(new[] { pa, lo, pb, hi });
						}
					}
				}
			}
			corners = list.ToArray();
			faces = faceList.ToArray();
		}

		/// <summary>
		/// A deltoidal icositetrahedron (leucite's trapezohedron): twenty-four kites, each round a point out along an axis, the two
		/// edge points beside it and the cube corner between them.
		/// </summary>
		private static void Icositetrahedron(DeterministicRNG rng, int variant, out Vector3[] corners, out int[][] faces)
		{
			const float edge = 0.70710678f, cube = 0.54691816f;
			var list = new List<Vector3>(26);
			var index = new Dictionary<Vector3Int, int>();
			int At(Vector3Int key)
			{
				if (!index.TryGetValue(key, out int i))
				{
					int nonZero = (key.x != 0 ? 1 : 0) + (key.y != 0 ? 1 : 0) + (key.z != 0 ? 1 : 0);
					float scale = nonZero == 1 ? 1f : nonZero == 2 ? edge : cube;
					i = list.Count;
					index[key] = i;
					list.Add(new Vector3(key.x, key.y, key.z) * scale);
				}
				return i;
			}
			var faceList = new List<int[]>(24);
			for (int axis = 0; axis < 3; axis++)
			{
				int j = (axis + 1) % 3, k = (axis + 2) % 3;
				for (int s = -1; s <= 1; s += 2)
				{
					for (int sj = -1; sj <= 1; sj += 2)
					{
						for (int sk = -1; sk <= 1; sk += 2)
						{
							var a = Vector3Int.zero;
							a[axis] = s;
							Vector3Int ej = a, ek = a;
							ej[j] = sj;
							ek[k] = sk;
							Vector3Int c = ej;
							c[k] = sk;
							faceList.Add(new[] { At(a), At(ej), At(c), At(ek) });
						}
					}
				}
			}
			corners = list.ToArray();
			faces = faceList.ToArray();
		}

		/// <summary>
		/// A cluster of one solid on a lump of rock: <paramref name="counts"/>[v] of them from <paramref name="largest"/>[v] down to
		/// <paramref name="smallest"/>[v] across, each turned every way and sunk a third of its size in the rock.
		/// </summary>
		private static MeshBuilder Polyhedra(int variant, int lod, int seed, Shape shape, int[] counts, float[] largest, float[] smallest)
		{
			var rng = new DeterministicRNG(seed);
			int v = Mathf.Clamp(variant, 0, counts.Length - 1);
			int count = counts[v];
			Vector3 matrix = v == 1 ? new Vector3(0.44f, 0.22f, 0.4f) : v == 3 ? new Vector3(0.55f, 0.18f, 0.32f) : new Vector3(0.42f, 0.2f, 0.38f);
			var parts = new List<(Matrix4x4 place, Vector3[] corners, int[][] faces, float v)>(count);
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (count - 1f) : 0f;
				float size = Mathf.Lerp(largest[v], smallest[v], rank) * rng.Range(0.9f, 1.1f);
				float az = rng.NextFloat() * Tau, r = i == 0 ? 0.1f : Mathf.Sqrt(rng.NextFloat()) * 0.78f;
				float top = matrix.y * Mathf.Sqrt(Mathf.Max(0f, 1f - r * r)) * 0.85f;
				var at = new Vector3(Mathf.Cos(az) * r * matrix.x, top + size * 0.18f, Mathf.Sin(az) * r * matrix.z);
				Quaternion turn = Quaternion.Euler(rng.Range(0f, 360f), rng.Range(0f, 360f), rng.Range(0f, 360f));
				shape(rng, v, out Vector3[] corners, out int[][] faces);
				parts.Add((Matrix4x4.TRS(at, turn, Vector3.one * (size * 0.5f)), corners, faces, rng.NextFloat()));
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.22f, lod, seed + 5);
			int keep = lod == 2 ? Mathf.Max(1, (parts.Count + 1) / 2) : parts.Count;
			for (int i = 0; i < keep; i++)
			{
				Solid(m, 1, parts[i].place, parts[i].corners, parts[i].faces, 0.35f, 1f, parts[i].v);
			}
			return m;
		}

		/// <summary>A point on the top of an ellipsoidal matrix of <paramref name="matrix"/> radii, <paramref name="r"/> of the way out.</summary>
		private static Vector3 OnMatrix(Vector3 matrix, float azimuth, float r, float sink = 0.8f)
		{
			r = Mathf.Clamp01(r);
			return new Vector3(Mathf.Cos(azimuth) * r * matrix.x, matrix.y * Mathf.Sqrt(Mathf.Max(0f, 1f - r * r)) * sink, Mathf.Sin(azimuth) * r * matrix.z);
		}

		/// <summary>One prism point (<see cref="CrystalPoint"/>) from its foot along <paramref name="dir"/>, flattened across <paramref name="across"/> by <paramref name="flat"/>.</summary>
		private static void Point(MeshBuilder m, Vector3 foot, Vector3 dir, Vector3 across, float flat, float length, float radius, float tipShare, int sides, int seed, float glowFoot, float glowTip)
		{
			var rng = new DeterministicRNG(seed);
			float tip = length * tipShare;
			Vector3 side = Vector3.ProjectOnPlane(across, dir);
			Quaternion rotation = side.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(side.normalized, dir) : UpTo(dir, 0f);
			CrystalPoint(m, 1, Matrix4x4.TRS(foot, rotation, new Vector3(1f, 1f, flat)), radius, length - tip, tip, sides, rng, glowFoot, glowTip);
		}

		// ── The habits ────────────────────────────────────────────────

		/// <summary>Grape clusters: bunches of domes, the largest at each bunch's heart, crusting a low lump. v0 18 in 3 bunches, v1 10 large, v2 26 small, v3 an elongated crust.</summary>
		private static MeshBuilder Botryoidal(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count, bunches;
			float largest, smallest;
			var bed = new Vector3(0.5f, 0.16f, 0.44f);
			switch (variant)
			{
				case 1: count = 10; bunches = 2; largest = 0.2f; smallest = 0.1f; break;
				case 2: count = 26; bunches = 5; largest = 0.11f; smallest = 0.04f; break;
				case 3: count = 18; bunches = 3; largest = 0.13f; smallest = 0.05f; bed = new Vector3(0.7f, 0.12f, 0.3f); break;
				default: count = 18; bunches = 3; largest = 0.15f; smallest = 0.05f; break;
			}
			var centres = new Vector2[bunches];
			for (int b = 0; b < bunches; b++)
			{
				centres[b] = new Vector2(rng.Range(-0.5f, 0.5f), rng.Range(-0.5f, 0.5f));
			}
			var beads = new List<(Vector3 at, float r)>(count);
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (count - 1f) : 0f;
				float r = Mathf.Lerp(largest, smallest, rank) * rng.Range(0.85f, 1.15f);
				Vector2 c = centres[i % bunches];
				float az = rng.NextFloat() * Tau, d = Mathf.Sqrt(rng.NextFloat()) * 0.35f * (0.4f + rank);
				float x = Mathf.Clamp(c.x + Mathf.Cos(az) * d, -0.85f, 0.85f), z = Mathf.Clamp(c.y + Mathf.Sin(az) * d, -0.85f, 0.85f);
				float out1 = Mathf.Min(1f, Mathf.Sqrt(x * x + z * z));
				float top = bed.y * Mathf.Sqrt(Mathf.Max(0f, 1f - out1 * out1)) * 0.85f;
				beads.Add((new Vector3(x * bed.x, top + (1f - rank) * largest * 0.5f, z * bed.z), r));
			}
			var m = new MeshBuilder(2);
			Matrix(m, bed, 0.2f, lod, seed + 5);
			int segments = lod == 0 ? 9 : lod == 1 ? 7 : 6, rings = lod == 0 ? 5 : 4;
			int keep = lod == 2 ? Mathf.Max(1, (beads.Count + 1) / 2) : beads.Count;
			for (int i = 0; i < keep; i++)
			{
				Bead(m, 1, beads[i].at, new Vector3(beads[i].r, beads[i].r * 0.8f, beads[i].r), 0.75f, segments, rings);
			}
			return m;
		}

		/// <summary>
		/// Twins: flattened prisms paired in a V at 84.5° (Japan-law quartz, sphene's wedges). v0 three pairs, v1 one great pair, v2
		/// five small, v3 three crosses of square prisms through one another at their middles (staurolite).
		/// </summary>
		private static MeshBuilder Twinned(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			var matrix = new Vector3(0.42f, 0.22f, 0.38f);
			int pairs;
			float longest, shortest, flat = 0.4f;
			bool cross = variant == 3;
			switch (variant)
			{
				case 1: pairs = 1; longest = 0.72f; shortest = 0.72f; break;
				case 2: pairs = 5; longest = 0.38f; shortest = 0.18f; break;
				case 3: pairs = 3; longest = 0.5f; shortest = 0.3f; flat = 1f; break;
				default: pairs = 3; longest = 0.52f; shortest = 0.26f; break;
			}
			var twins = new List<(Vector3 foot, Vector3 a, Vector3 b, Vector3 across, float length, int seed)>(pairs);
			for (int i = 0; i < pairs; i++)
			{
				float rank = pairs > 1 ? i / (pairs - 1f) : 0f;
				float length = Mathf.Lerp(longest, shortest, rank) * rng.Range(0.9f, 1.08f);
				Vector3 foot = OnMatrix(matrix, rng.NextFloat() * Tau, i == 0 ? 0.08f : rng.Range(0.3f, 0.7f), 0.75f);
				float plane = rng.NextFloat() * Tau, lean = rng.Range(-12f, 12f);
				var across = new Vector3(-Mathf.Sin(plane), 0f, Mathf.Cos(plane));
				float half = cross ? 45f : 42.25f;
				twins.Add((foot, Tilted(half + lean, plane), Tilted(half - lean, plane + Mathf.PI), across, length, seed + 431 * (i + 1)));
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.24f, lod, seed + 5);
			int keep = lod == 2 ? Mathf.Max(1, (twins.Count + 1) / 2) : twins.Count;
			int sides = cross ? 4 : 6;
			for (int i = 0; i < keep; i++)
			{
				var t = twins[i];
				float radius = t.length * (cross ? 0.1f : 0.2f);
				if (cross)
				{
					// Each arm of the cross is half a prism, from the cross's middle out, stood on its two lower arms.
					Vector3 centre = t.foot + Vector3.up * t.length * 0.3f;
					foreach (Vector3 d in new[] { t.a, -t.a, t.b, -t.b })
					{
						Point(m, centre - d * radius * 0.5f, d, t.across, flat, t.length * 0.5f, radius, 0.12f, sides, t.seed + (d.y > 0f ? 1 : 2) + (Vector3.Dot(d, t.a) > 0.5f ? 4 : 8), 0.3f, 1f);
					}
					continue;
				}
				Point(m, t.foot - t.a * t.length * 0.1f, t.a, t.across, flat, t.length, radius, 0.22f, sides, t.seed + 1, 0.25f, 1f);
				Point(m, t.foot - t.b * t.length * 0.1f, t.b, t.across, flat, t.length * 0.94f, radius, 0.22f, sides, t.seed + 2, 0.25f, 1f);
			}
			return m;
		}

		/// <summary>
		/// Helictites: wire-thin filaments curling every way from a lump, against gravity, tapering to clear tips. v0 22 to 0.32 m,
		/// v1 12 long tight coils, v2 28 short, v3 16 along an elongated lump.
		/// </summary>
		private static MeshBuilder Helictites(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			float longest, shortest, curl;
			var matrix = new Vector3(0.36f, 0.2f, 0.32f);
			switch (variant)
			{
				case 1: count = 12; longest = 0.4f; shortest = 0.16f; curl = 0.45f; break;
				case 2: count = 28; longest = 0.22f; shortest = 0.07f; curl = 0.3f; break;
				case 3: count = 16; longest = 0.3f; shortest = 0.1f; curl = 0.3f; matrix = new Vector3(0.55f, 0.16f, 0.26f); break;
				default: count = 22; longest = 0.32f; shortest = 0.1f; curl = 0.28f; break;
			}
			const int Points = 10;
			var wires = new List<(Vector3[] path, float radius)>(count);
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (count - 1f) : 0f;
				float length = Mathf.Lerp(longest, shortest, rank) * rng.Range(0.85f, 1.1f);
				float az = rng.NextFloat() * Tau;
				Vector3 at = OnMatrix(matrix, az, Mathf.Sqrt(rng.NextFloat()) * 0.85f);
				Vector3 dir = Tilted(rng.Range(15f, 80f), az + rng.Range(-0.6f, 0.6f));
				Vector3 axis = new Vector3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f)).normalized;
				if (axis.sqrMagnitude < 0.5f)
				{
					axis = Vector3.right;
				}
				float turn = curl * rng.Range(0.6f, 1.4f) * Mathf.Rad2Deg;
				var path = new Vector3[Points];
				path[0] = at - dir * 0.02f;
				float step = length / (Points - 1);
				for (int k = 1; k < Points; k++)
				{
					path[k] = path[k - 1] + dir * step;
					dir = Quaternion.AngleAxis(turn, axis) * dir;
					axis = (axis + new Vector3(rng.Range(-0.3f, 0.3f), rng.Range(-0.3f, 0.3f), rng.Range(-0.3f, 0.3f))).normalized;
					if (dir.y < -0.25f)
					{
						dir.y = -0.25f;
						dir.Normalize();
					}
				}
				wires.Add((path, rng.Range(0.007f, 0.012f)));
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.2f, lod, seed + 5);
			int keep = lod == 2 ? Mathf.Max(1, (wires.Count + 1) / 2) : wires.Count;
			int sides = lod == 0 ? 4 : 3, stride = lod == 0 ? 1 : lod == 1 ? 1 : 2;
			for (int i = 0; i < keep; i++)
			{
				Thread(m, wires[i].path, stride, wires[i].radius, wires[i].radius * 0.45f, 0.3f, 1f, sides);
			}
			return m;
		}

		/// <summary>A tapering tube along a path's every <paramref name="stride"/>-th point (and its last), capped both ends, on the crystal submesh.</summary>
		private static void Thread(MeshBuilder m, Vector3[] path, int stride, float footRadius, float tipRadius, float glowFoot, float glowTip, int sides)
		{
			var points = new List<Vector3>();
			for (int k = 0; k < path.Length; k += stride)
			{
				points.Add(path[k]);
			}
			if ((path.Length - 1) % stride != 0)
			{
				points.Add(path[path.Length - 1]);
			}
			var radius = new float[points.Count];
			var glow = new float[points.Count];
			for (int k = 0; k < points.Count; k++)
			{
				float t = k / (points.Count - 1f);
				radius[k] = Mathf.Lerp(footRadius, tipRadius, t);
				glow[k] = Mathf.Lerp(glowFoot, glowTip, t);
			}
			Tube(m, 1, points, radius, glow, sides, true, true);
		}

		/// <summary>Massive: squat irregular columns of five to seven sides with low pointed tops, fused in a lump. v0 three, v1 five, v2 two great, v3 seven stepped.</summary>
		private static MeshBuilder Massive(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			float largest, smallest;
			var matrix = new Vector3(0.42f, 0.18f, 0.38f);
			switch (variant)
			{
				case 1: count = 5; largest = 0.26f; smallest = 0.14f; break;
				case 2: count = 2; largest = 0.36f; smallest = 0.28f; matrix = new Vector3(0.48f, 0.2f, 0.42f); break;
				case 3: count = 7; largest = 0.2f; smallest = 0.1f; matrix = new Vector3(0.55f, 0.16f, 0.34f); break;
				default: count = 3; largest = 0.3f; smallest = 0.2f; break;
			}
			var blocks = new List<(Matrix4x4 place, int sides, float radius, float height, int seed)>(count);
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (count - 1f) : 0f;
				float radius = Mathf.Lerp(largest, smallest, rank) * rng.Range(0.9f, 1.1f);
				float height = radius * rng.Range(1.2f, 2.1f);
				Vector3 foot = OnMatrix(matrix, rng.NextFloat() * Tau, i == 0 ? 0.05f : variant == 3 ? rank * 0.85f : rng.Range(0.25f, 0.7f), 0.6f);
				Quaternion lean = Quaternion.AngleAxis(rng.Range(0f, 22f), new Vector3(rng.Range(-1f, 1f), 0f, rng.Range(-1f, 1f)).normalized + Vector3.forward * 0.001f);
				blocks.Add((Matrix4x4.TRS(foot + Vector3.down * radius * 0.4f, lean, Vector3.one), 5 + rng.Range(0, 3), radius, height, seed + 911 * (i + 1)));
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.18f, lod, seed + 5);
			int keep = lod == 2 ? Mathf.Max(1, (blocks.Count + 1) / 2) : blocks.Count;
			for (int i = 0; i < keep; i++)
			{
				var b = blocks[i];
				var rng2 = new DeterministicRNG(b.seed);
				Column(m, 1, b.place, b.sides, b.radius, new[] { new Vector2(1f, 0f), new Vector2(0.9f, b.height) }, b.radius * 0.35f, 0f, 0.14f, rng2, 0.3f, 1f);
			}
			return m;
		}

		/// <summary>
		/// Coralloid: knobbly sprigs branching twice or thrice from a lump, a knob at every tip (flos-ferri). v0 six sprigs, v1 four
		/// thicker branching deeper, v2 nine small, v3 five low and spreading.
		/// </summary>
		private static MeshBuilder Coralloid(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int sprigs, depth = 1;
			float length, radius, rise = 30f;
			var matrix = new Vector3(0.4f, 0.18f, 0.36f);
			switch (variant)
			{
				case 1: sprigs = 4; length = 0.2f; radius = 0.032f; depth = 2; break;
				case 2: sprigs = 9; length = 0.13f; radius = 0.02f; break;
				case 3: sprigs = 5; length = 0.2f; radius = 0.024f; rise = 60f; matrix = new Vector3(0.55f, 0.14f, 0.36f); break;
				default: sprigs = 6; length = 0.18f; radius = 0.026f; break;
			}
			var branches = new List<(Vector3[] path, float foot, float tip, bool knob)>();
			void Grow(Vector3 from, Vector3 dir, float len, float r, int level)
			{
				var path = new Vector3[5];
				path[0] = from;
				Vector3 d = dir;
				for (int k = 1; k < path.Length; k++)
				{
					d = (d + new Vector3(rng.Range(-0.25f, 0.25f), rng.Range(-0.05f, 0.2f), rng.Range(-0.25f, 0.25f))).normalized;
					path[k] = path[k - 1] + d * (len / (path.Length - 1));
				}
				bool leaf = level >= depth;
				branches.Add((path, r, r * 0.75f, leaf));
				if (leaf)
				{
					return;
				}
				int children = variant == 1 ? 2 : 2 + rng.Range(0, 2);
				for (int c = 0; c < children; c++)
				{
					Vector3 bend = Vector3.Cross(d, new Vector3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f))).normalized;
					if (bend.sqrMagnitude < 0.5f)
					{
						bend = Vector3.Cross(d, Vector3.right).normalized;
					}
					Vector3 next = Quaternion.AngleAxis(rng.Range(25f, 48f), bend) * d;
					if (next.y < 0.1f)
					{
						next.y = 0.1f;
						next.Normalize();
					}
					Grow(path[path.Length - 1] - d * r * 0.5f, next, len * 0.72f, r * 0.72f, level + 1);
				}
			}
			for (int s = 0; s < sprigs; s++)
			{
				float az = rng.NextFloat() * Tau;
				Vector3 foot = OnMatrix(matrix, az, s == 0 ? 0.05f : rng.Range(0.2f, 0.75f));
				Grow(foot - Vector3.up * 0.03f, Tilted(rng.Range(0f, rise), az), length * rng.Range(0.85f, 1.15f), radius, 0);
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.2f, lod, seed + 5);
			int sides = lod == 0 ? 5 : lod == 1 ? 4 : 3, stride = lod == 2 ? 2 : 1;
			int segments = lod == 0 ? 6 : 5, rings = lod == 0 ? 4 : 3;
			foreach (var b in branches)
			{
				Thread(m, b.path, stride, b.foot, b.tip, 0.35f, 0.85f, sides);
				if (b.knob)
				{
					float k = b.tip * 1.7f;
					Bead(m, 1, b.path[b.path.Length - 1], new Vector3(k, k, k), 0.95f, segments, rings);
				}
			}
			return m;
		}

		/// <summary>
		/// Foliated: books of thin hexagonal plates stacked a little askew along each book's lean, the plates mostly on edge
		/// (orpiment, specularite). v0 four books, v1 two great, v2 seven small, v3 five fanned from one root.
		/// </summary>
		private static MeshBuilder Foliated(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int books, plates;
			float largest, smallest;
			var matrix = new Vector3(0.42f, 0.2f, 0.38f);
			switch (variant)
			{
				case 1: books = 2; plates = 12; largest = 0.3f; smallest = 0.24f; break;
				case 2: books = 7; plates = 7; largest = 0.15f; smallest = 0.08f; break;
				case 3: books = 5; plates = 8; largest = 0.2f; smallest = 0.14f; break;
				default: books = 4; plates = 9; largest = 0.22f; smallest = 0.13f; break;
			}
			var stack = new List<(Matrix4x4 place, float radius, float thick, int seed, int book)>();
			float fan = rng.NextFloat() * Tau;
			for (int b = 0; b < books; b++)
			{
				float rank = books > 1 ? b / (books - 1f) : 0f;
				float radius = Mathf.Lerp(largest, smallest, rank) * rng.Range(0.9f, 1.1f);
				float az = variant == 3 ? fan + (rank - 0.5f) * 2.2f : rng.NextFloat() * Tau;
				Vector3 foot = OnMatrix(matrix, az, variant == 3 ? 0.15f : b == 0 ? 0.05f : rng.Range(0.25f, 0.7f), 0.7f);
				// The book's axis (its plates' normal) leans well over, so its plates stand on edge.
				Vector3 axis = Tilted(rng.Range(55f, 85f), az + rng.Range(-0.5f, 0.5f));
				Quaternion frame = Quaternion.FromToRotation(Vector3.up, axis);
				float thick = radius * rng.Range(0.05f, 0.08f);
				Vector3 centre = foot + Vector3.up * radius * 0.55f;
				for (int p = 0; p < plates; p++)
				{
					float along = (p - (plates - 1) * 0.5f) * thick * 1.15f;
					Quaternion askew = Quaternion.Euler(rng.Range(-4f, 4f), rng.Range(-12f, 12f), rng.Range(-4f, 4f));
					stack.Add((Matrix4x4.TRS(centre + axis * along, frame * askew, Vector3.one), radius * rng.Range(0.88f, 1.05f), thick * 0.5f, seed + 37 * stack.Count + 3, b));
				}
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.22f, lod, seed + 5);
			int keepBooks = lod == 2 ? Mathf.Max(1, (books + 1) / 2) : books;
			foreach (var s in stack)
			{
				if (s.book >= keepBooks)
				{
					continue;
				}
				var r = new DeterministicRNG(s.seed);
				Column(m, 1, s.place, 6, s.radius, new[] { new Vector2(1f, -s.thick), new Vector2(1f, s.thick) }, 0f, 0f, 0.1f, r, 0.35f, 1f);
			}
			return m;
		}

		/// <summary>
		/// Fibrous: sheaves of near-parallel needles fanning from a waist both ways, the lower half in the rock (satin spar, stilbite's
		/// bow-ties). v0 one sheaf of 40, v1 two of 24, v2 a vein of 48 short needles side by side, v3 three small sheaves.
		/// </summary>
		private static MeshBuilder Fibrous(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int sheaves, needles;
			float longest, spread = 22f;
			var matrix = new Vector3(0.42f, 0.2f, 0.38f);
			switch (variant)
			{
				case 1: sheaves = 2; needles = 24; longest = 0.5f; break;
				case 2: sheaves = 1; needles = 48; longest = 0.26f; spread = 6f; matrix = new Vector3(0.6f, 0.16f, 0.3f); break;
				case 3: sheaves = 3; needles = 16; longest = 0.36f; break;
				default: sheaves = 1; needles = 40; longest = 0.7f; break;
			}
			var parts = new List<(Vector3 foot, Vector3 dir, Vector3 across, float length, float radius, int seed)>();
			for (int s = 0; s < sheaves; s++)
			{
				float az = rng.NextFloat() * Tau;
				Vector3 waist = OnMatrix(matrix, az, s == 0 ? 0.05f : rng.Range(0.35f, 0.65f)) + Vector3.up * 0.04f;
				float plane = rng.NextFloat() * Tau;
				var inPlane = new Vector3(Mathf.Cos(plane), 0f, Mathf.Sin(plane));
				var across = new Vector3(-inPlane.z, 0f, inPlane.x);
				Vector3 lean = Tilted(rng.Range(0f, 15f), rng.NextFloat() * Tau);
				for (int i = 0; i < needles; i++)
				{
					float u = needles > 1 ? i / (needles - 1f) * 2f - 1f : 0f;
					Vector3 dir = (Quaternion.AngleAxis(u * spread, across) * Quaternion.AngleAxis(rng.Range(-spread, spread) * 0.35f, inPlane) * lean).normalized;
					float length = longest * rng.Range(0.75f, 1f);
					Vector3 offset = variant == 2 ? inPlane * u * 0.45f + across * rng.Range(-0.02f, 0.02f) : inPlane * rng.Range(-0.02f, 0.02f) + across * rng.Range(-0.015f, 0.015f);
					parts.Add((waist + offset - dir * length * 0.42f, dir, across, length, length * rng.Range(0.012f, 0.02f), seed + 53 * parts.Count + 7));
				}
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.2f, lod, seed + 5);
			int keep = lod == 2 ? Mathf.Max(1, (parts.Count + 1) / 2) : parts.Count;
			for (int i = 0; i < keep; i++)
			{
				var p = parts[i];
				Point(m, p.foot, p.dir, p.across, 1f, p.length, p.radius, 0.08f, 4, p.seed, 0.25f, 1f);
			}
			return m;
		}

		/// <summary>
		/// Rosettes: thin rounded petals set every way about a centre, each through it (a desert rose, a barite rose, curved
		/// rose-crystal). v0 one rose of 14, v1 one of 8 large petals, v2 three roses of 7, v3 two of 10.
		/// </summary>
		private static MeshBuilder Rosettes(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int roses, petals;
			float petal;
			var matrix = new Vector3(0.42f, 0.18f, 0.38f);
			switch (variant)
			{
				case 1: roses = 1; petals = 8; petal = 0.24f; break;
				case 2: roses = 3; petals = 7; petal = 0.12f; matrix = new Vector3(0.5f, 0.16f, 0.44f); break;
				case 3: roses = 2; petals = 10; petal = 0.15f; break;
				default: roses = 1; petals = 14; petal = 0.17f; break;
			}
			var parts = new List<(Matrix4x4 place, float radius, int seed)>();
			for (int r = 0; r < roses; r++)
			{
				float az = rng.NextFloat() * Tau;
				Vector3 centre = OnMatrix(matrix, az, r == 0 ? 0.05f : rng.Range(0.4f, 0.7f)) + Vector3.up * petal * 0.75f;
				for (int p = 0; p < petals; p++)
				{
					// The petal's way out from the rose's heart, and its plane's normal square to it.
					Vector3 outward = new Vector3(rng.Range(-1f, 1f), rng.Range(-0.35f, 1f), rng.Range(-1f, 1f));
					if (outward.sqrMagnitude < 0.05f)
					{
						outward = Vector3.up;
					}
					outward.Normalize();
					Vector3 normal = Vector3.Cross(outward, new Vector3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f)));
					if (normal.sqrMagnitude < 1e-4f)
					{
						normal = Vector3.Cross(outward, Vector3.right);
					}
					float size = petal * rng.Range(0.8f, 1.1f);
					parts.Add((Matrix4x4.TRS(centre + outward * size * 0.5f, Quaternion.FromToRotation(Vector3.up, normal.normalized), Vector3.one), size, seed + 71 * parts.Count + 1));
				}
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.2f, lod, seed + 5);
			int sides = lod == 0 ? 8 : lod == 1 ? 7 : 6;
			int keep = lod == 2 ? Mathf.Max(1, (parts.Count + 1) / 2) : parts.Count;
			for (int i = 0; i < keep; i++)
			{
				var r = new DeterministicRNG(parts[i].seed);
				float thick = parts[i].radius * 0.06f;
				Column(m, 1, parts[i].place, sides, parts[i].radius * 0.5f, new[] { new Vector2(1f, -thick), new Vector2(1f, thick) }, 0f, 0f, 0.12f, r, 0.4f, 1f);
			}
			return m;
		}

		/// <summary>
		/// Bipyramids: hexagonal double points with a short waist (zircon, sulphur, farrstone). v0 six, v1 three large, v2 twelve
		/// small, v3 five long four-sided spindles (dogtooth spearheads, wyrmglass).
		/// </summary>
		private static MeshBuilder Bipyramids(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count, sides = 6;
			float largest, smallest, point = 1.5f, waist = 0.3f;
			var matrix = new Vector3(0.42f, 0.2f, 0.38f);
			switch (variant)
			{
				case 1: count = 3; largest = 0.18f; smallest = 0.12f; break;
				case 2: count = 12; largest = 0.08f; smallest = 0.035f; break;
				case 3: count = 5; largest = 0.09f; smallest = 0.06f; sides = 4; point = 3.4f; waist = 0f; break;
				default: count = 6; largest = 0.12f; smallest = 0.06f; break;
			}
			var parts = new List<(Matrix4x4 place, float radius, int seed)>(count);
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (count - 1f) : 0f;
				float radius = Mathf.Lerp(largest, smallest, rank) * rng.Range(0.9f, 1.1f);
				float az = rng.NextFloat() * Tau;
				Vector3 foot = OnMatrix(matrix, az, i == 0 ? 0.05f : Mathf.Sqrt(rng.NextFloat()) * 0.75f);
				Quaternion stand = Quaternion.FromToRotation(Vector3.up, Tilted(i == 0 ? rng.Range(0f, 15f) : rng.Range(10f, 75f), az + rng.Range(-0.5f, 0.5f)));
				parts.Add((Matrix4x4.TRS(foot + stand * Vector3.up * radius * point * 0.45f, stand, Vector3.one), radius, seed + 97 * (i + 1)));
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.22f, lod, seed + 5);
			int keep = lod == 2 ? Mathf.Max(1, (parts.Count + 1) / 2) : parts.Count;
			for (int i = 0; i < keep; i++)
			{
				var r = new DeterministicRNG(parts[i].seed);
				float rad = parts[i].radius, w = rad * waist;
				Vector2[] rings = w > 0f ? new[] { new Vector2(1f, -w), new Vector2(1f, w) } : new[] { new Vector2(1f, 0f) };
				Column(m, 1, parts[i].place, sides, rad, rings, rad * point, rad * point, 0.04f, r, 0.3f, 1f);
			}
			return m;
		}

		/// <summary>
		/// Dendrites: six-armed plates of fern-branched bars fanned off the rock (sublimation hoar, lead feathers). v0 three plates,
		/// v1 two great, v2 five small, v3 four single feathered fronds. The last level keeps only the main arms.
		/// </summary>
		private static MeshBuilder Dendrites(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int plates, branches = 3;
			float largest, smallest;
			bool fronds = variant == 3;
			var matrix = new Vector3(0.4f, 0.2f, 0.36f);
			switch (variant)
			{
				case 1: plates = 2; largest = 0.42f; smallest = 0.36f; break;
				case 2: plates = 5; largest = 0.2f; smallest = 0.14f; branches = 2; break;
				case 3: plates = 4; largest = 0.5f; smallest = 0.32f; branches = 5; break;
				default: plates = 3; largest = 0.3f; smallest = 0.22f; break;
			}
			var bars = new List<(Matrix4x4 place, Vector3 half, bool main, float v)>();
			for (int p = 0; p < plates; p++)
			{
				float rank = plates > 1 ? p / (plates - 1f) : 0f;
				float size = Mathf.Lerp(largest, smallest, rank) * rng.Range(0.9f, 1.1f);
				float az = rng.NextFloat() * Tau;
				Vector3 foot = OnMatrix(matrix, az, p == 0 ? 0.05f : rng.Range(0.3f, 0.7f));
				// The plate's normal leans well over, so the plate stands up off the rock.
				Vector3 normal = Tilted(rng.Range(50f, 80f), az + rng.Range(-0.6f, 0.6f));
				Quaternion frame = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.AngleAxis(rng.Range(0f, 60f), Vector3.up);
				float thick = 0.004f, width = size * 0.04f;
				// A plate's centre stands its radius off the rock; a frond rises from its foot.
				Vector3 centre = fronds ? foot - Vector3.up * 0.03f : foot + Vector3.up * size * 0.55f;
				int arms = fronds ? 1 : 6;
				for (int a = 0; a < arms; a++)
				{
					float angle = fronds ? 0f : 60f * a;
					Quaternion arm = frame * Quaternion.AngleAxis(angle, Vector3.up);
					if (fronds)
					{
						// A frond's spine runs up from the rock (its plane upright), not out of a plate.
						arm = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.up, normal).normalized + Vector3.up * 0.001f, normal) * Quaternion.AngleAxis(-90f, Vector3.up);
					}
					Vector3 along = arm * Vector3.right;
					bars.Add((Matrix4x4.TRS(centre + along * size * 0.5f, arm, Vector3.one), new Vector3(size * 0.5f, thick, width), true, rng.NextFloat()));
					for (int b = 0; b < branches; b++)
					{
						float f = (b + 1f) / (branches + 1f);
						float len = (1f - f) * size * (fronds ? 0.4f : 0.45f);
						for (int side = -1; side <= 1; side += 2)
						{
							Quaternion twig = arm * Quaternion.AngleAxis(side * 60f, Vector3.up);
							Vector3 start = centre + along * size * f;
							bars.Add((Matrix4x4.TRS(start + twig * Vector3.right * len * 0.5f, twig, Vector3.one), new Vector3(len * 0.5f, thick, width * 0.7f), false, rng.NextFloat()));
						}
					}
				}
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.2f, lod, seed + 5);
			foreach (var b in bars)
			{
				if (lod == 2 && !b.main)
				{
					continue;
				}
				Cuboid(m, 1, b.place, b.half, 0.45f, 1f, b.v);
			}
			return m;
		}

		/// <summary>
		/// Pearls: polished spheres nested in a shallow rimstone cup, a few spilled over its rim. v0 nine, v1 four large, v2 fourteen
		/// small, v3 an elongated cup of eight with three outside.
		/// </summary>
		private static MeshBuilder Pearls(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count, outside = 0;
			float largest, smallest, radius = 0.38f, stretch = 1f;
			switch (variant)
			{
				case 1: count = 4; largest = 0.09f; smallest = 0.07f; radius = 0.34f; break;
				case 2: count = 14; largest = 0.045f; smallest = 0.025f; radius = 0.32f; break;
				case 3: count = 8; outside = 3; largest = 0.06f; smallest = 0.035f; stretch = 1.45f; break;
				default: count = 9; largest = 0.07f; smallest = 0.035f; break;
			}
			const float Rim = 0.2f, Floor = 0.05f, Wall = 0.04f;
			float inner = radius - Wall;
			var pearls = new List<(Vector3 at, float r)>(count + outside);
			for (int i = 0; i < count + outside; i++)
			{
				bool spilled = i >= count;
				float rank = count > 1 ? Mathf.Min(1f, i / (count - 1f)) : 0f;
				float r = Mathf.Lerp(largest, smallest, rank) * rng.Range(0.9f, 1.1f);
				float az = rng.NextFloat() * Tau;
				float d = spilled ? radius + r * 1.2f : Mathf.Sqrt(rng.NextFloat()) * (inner - r) * 0.8f;
				float y = spilled ? r * 0.85f : Floor + Mathf.Lerp(0f, Rim - Floor, Mathf.Pow(d / inner, 2f)) * 0.7f + r * 0.9f;
				pearls.Add((new Vector3(Mathf.Cos(az) * d * stretch, y, Mathf.Sin(az) * d), r));
			}
			var m = new MeshBuilder(2);
			// The cup: up its outside from the pole to the rim, across it, down its inside to the floor (rock, textured in metres).
			int steps = lod == 0 ? 5 : lod == 1 ? 4 : 3;
			var profile = new List<LP>(2 * steps + 4);
			for (int k = 0; k <= steps; k++)
			{
				float t = k / (float)steps;
				profile.Add(new LP(radius * Mathf.Sin(t * 0.5f * Mathf.PI), Rim * (1f - Mathf.Cos(t * 0.5f * Mathf.PI)), 0.3f, k == steps));
			}
			for (int k = steps; k >= 0; k--)
			{
				float t = k / (float)steps;
				profile.Add(new LP(inner * Mathf.Sin(t * 0.5f * Mathf.PI), Floor + (Rim - Floor) * (1f - Mathf.Cos(t * 0.5f * Mathf.PI)), 0.3f, k == steps));
			}
			int cupFirst = m.VertexCount;
			Lathe(m, 0, profile, lod == 0 ? 16 : lod == 1 ? 12 : 8, Matrix4x4.Scale(new Vector3(stretch, 1f, 1f)), 2f);
			for (int i = cupFirst; i < m.VertexCount; i++)
			{
				Vector3 q = m.Positions[i];
				m.UVs[i] = new Vector2(q.x + q.z * 0.7f, q.y + q.z * 0.3f);
			}
			m.Transform(Matrix4x4.Translate(new Vector3(0f, -0.03f, 0f)), cupFirst);
			int segments = lod == 0 ? 10 : lod == 1 ? 8 : 6, rings = lod == 0 ? 6 : lod == 1 ? 5 : 4;
			int keep = lod == 2 ? Mathf.Max(1, (pearls.Count + 1) / 2) : pearls.Count;
			for (int i = 0; i < keep; i++)
			{
				Vector3 at = pearls[i].at - new Vector3(0f, 0.03f, 0f);
				Bead(m, 1, at, Vector3.one * pearls[i].r, 0.8f, segments, rings);
			}
			return m;
		}

		/// <summary>
		/// Floss: hair-thin tufts and curls over a crust of efflorescence (cave cotton, thenardite, Pele's hair). v0 five tufts of
		/// eight, v1 three of ten long curls, v2 eight of six, v3 one great cotton tuft of thirty.
		/// </summary>
		private static MeshBuilder Floss(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int tufts, hairs;
			float longest, curl;
			var matrix = new Vector3(0.4f, 0.2f, 0.36f);
			switch (variant)
			{
				case 1: tufts = 3; hairs = 10; longest = 0.28f; curl = 0.35f; break;
				case 2: tufts = 8; hairs = 6; longest = 0.12f; curl = 0.2f; break;
				case 3: tufts = 1; hairs = 30; longest = 0.22f; curl = 0.25f; break;
				default: tufts = 5; hairs = 8; longest = 0.18f; curl = 0.22f; break;
			}
			const int Points = 8;
			var strands = new List<Vector3[]>();
			var crust = new List<(Vector3 at, float r)>();
			for (int t = 0; t < tufts; t++)
			{
				float az = rng.NextFloat() * Tau;
				Vector3 root = OnMatrix(matrix, az, t == 0 ? 0.05f : rng.Range(0.3f, 0.75f));
				crust.Add((root, rng.Range(0.06f, 0.11f)));
				for (int h = 0; h < hairs; h++)
				{
					float length = longest * rng.Range(0.6f, 1f);
					Vector3 dir = Tilted(rng.Range(5f, 70f), rng.NextFloat() * Tau);
					Vector3 axis = new Vector3(rng.Range(-1f, 1f), rng.Range(-0.3f, 0.3f), rng.Range(-1f, 1f)).normalized;
					if (axis.sqrMagnitude < 0.5f)
					{
						axis = Vector3.forward;
					}
					float turn = curl * rng.Range(0.5f, 1.5f) * Mathf.Rad2Deg;
					var path = new Vector3[Points];
					path[0] = root + new Vector3(rng.Range(-0.03f, 0.03f), -0.01f, rng.Range(-0.03f, 0.03f));
					for (int k = 1; k < Points; k++)
					{
						path[k] = path[k - 1] + dir * (length / (Points - 1));
						dir = Quaternion.AngleAxis(turn, axis) * dir;
						if (dir.y < -0.3f)
						{
							dir.y = -0.3f;
							dir.Normalize();
						}
					}
					strands.Add(path);
				}
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.2f, lod, seed + 5);
			int keep = lod == 2 ? Mathf.Max(1, (strands.Count + 1) / 2) : strands.Count;
			int stride = lod == 0 ? 1 : 2;
			for (int i = 0; i < keep; i++)
			{
				Thread(m, strands[i], stride, 0.0035f, 0.0015f, 0.45f, 0.9f, 3);
			}
			int segments = lod == 0 ? 7 : 5;
			foreach ((Vector3 at, float r) in crust)
			{
				Bead(m, 1, at, new Vector3(r, 0.025f, r), 0.5f, segments, 3);
			}
			return m;
		}

		/// <summary>
		/// Hollow hexagonal cups, flat-faced: outer walls narrowing to the foot, a rim, inner walls and a floor (depth hoar, bellspar).
		/// Its foot at the origin of <paramref name="place"/>, open up +y.
		/// </summary>
		private static void HexCup(MeshBuilder m, Matrix4x4 place, float footRadius, float rimRadius, float height, float wall, DeterministicRNG rng, float glowLow, float glowHigh)
		{
			const int Sides = 6;
			int first = m.VertexCount;
			float turn = rng.NextFloat() * Tau;
			var dirs = new Vector3[Sides];
			for (int i = 0; i < Sides; i++)
			{
				float a = turn + Tau * i / Sides;
				dirs[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
			}
			Vector3 Ring(int i, float r, float y) => dirs[i % Sides] * r + Vector3.up * y;
			float v0 = rng.NextFloat();
			void Face(Vector3[] pts, Vector3 facing, int index)
			{
				Vector3 n = Vector3.Cross(pts[1] - pts[0], pts[pts.Length - 1] - pts[0]);
				if (n.sqrMagnitude < 1e-16f)
				{
					return;
				}
				n.Normalize();
				if (Vector3.Dot(n, facing) < 0f)
				{
					n = -n;
				}
				int a0 = m.VertexCount;
				for (int i = 0; i < pts.Length; i++)
				{
					float level = Mathf.Lerp(glowLow, glowHigh, Mathf.Clamp01(pts[i].y / height));
					m.AddVertex(pts[i], n, new Vector2(Glow(level), v0 + 0.13f * index + ((i & 1) == 0 ? 0f : 0.11f)), White);
				}
				for (int i = 1; i + 1 < pts.Length; i++)
				{
					m.AddTriangle(1, a0, a0 + i, a0 + i + 1, n);
				}
			}
			float innerFoot = Mathf.Max(footRadius * 0.4f, footRadius - wall), innerRim = rimRadius - wall;
			int f = 0;
			for (int i = 0; i < Sides; i++)
			{
				Vector3 mid = (dirs[i] + dirs[(i + 1) % Sides]) * 0.5f;
				Face(new[] { Ring(i, footRadius, 0f), Ring(i + 1, footRadius, 0f), Ring(i + 1, rimRadius, height), Ring(i, rimRadius, height) }, mid, f++);
				Face(new[] { Ring(i, rimRadius, height), Ring(i + 1, rimRadius, height), Ring(i + 1, innerRim, height), Ring(i, innerRim, height) }, Vector3.up, f++);
				Face(new[] { Ring(i, innerRim, height), Ring(i + 1, innerRim, height), Ring(i + 1, innerFoot, wall), Ring(i, innerFoot, wall) }, -mid, f++);
			}
			var floor = new Vector3[Sides];
			var foot = new Vector3[Sides];
			for (int i = 0; i < Sides; i++)
			{
				floor[i] = Ring(i, innerFoot, wall);
				foot[i] = Ring(i, footRadius, 0f);
			}
			Face(floor, Vector3.up, f++);
			Face(foot, Vector3.down, f);
			m.Transform(place, first);
		}

		/// <summary>Cups: hollow hexagonal cups tilted open up and out. v0 seven, v1 four large, v2 twelve small, v3 three chains of three, each cup sat in the last.</summary>
		private static MeshBuilder Cups(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count, chain = 1;
			float largest, smallest;
			var matrix = new Vector3(0.42f, 0.2f, 0.38f);
			switch (variant)
			{
				case 1: count = 4; largest = 0.2f; smallest = 0.14f; break;
				case 2: count = 12; largest = 0.09f; smallest = 0.05f; break;
				case 3: count = 3; chain = 3; largest = 0.11f; smallest = 0.08f; break;
				default: count = 7; largest = 0.13f; smallest = 0.07f; break;
			}
			var cups = new List<(Matrix4x4 place, float rim, float height, int seed)>();
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (count - 1f) : 0f;
				float rim = Mathf.Lerp(largest, smallest, rank) * rng.Range(0.9f, 1.1f);
				float az = rng.NextFloat() * Tau;
				Vector3 at = OnMatrix(matrix, az, i == 0 ? 0.05f : Mathf.Sqrt(rng.NextFloat()) * 0.75f) - Vector3.up * rim * 0.2f;
				Quaternion tilt = Quaternion.FromToRotation(Vector3.up, Tilted(i == 0 ? rng.Range(0f, 15f) : rng.Range(15f, 60f), az + rng.Range(-0.5f, 0.5f)));
				for (int c = 0; c < chain; c++)
				{
					float height = rim * rng.Range(1.1f, 1.5f);
					cups.Add((Matrix4x4.TRS(at, tilt, Vector3.one), rim, height, seed + 59 * cups.Count + 9));
					// The next cup of a chain sits in this one's mouth, a little smaller and turned.
					at += tilt * Vector3.up * height * 0.7f;
					tilt = tilt * Quaternion.Euler(rng.Range(-15f, 15f), rng.Range(0f, 60f), rng.Range(-15f, 15f));
					rim *= 0.85f;
				}
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.22f, lod, seed + 5);
			int keep = lod == 2 ? Mathf.Max(1, (cups.Count + 1) / 2) : cups.Count;
			for (int i = 0; i < keep; i++)
			{
				var c = cups[i];
				HexCup(m, c.place, c.rim * 0.55f, c.rim, c.height, c.rim * 0.12f, new DeterministicRNG(c.seed), 0.35f, 1f);
			}
			return m;
		}

		/// <summary>Barrels: hexagonal crystals swelling at the middle to flat ends (sapphire). v0 four, v1 two great, v2 eight small, v3 five lying in a heap.</summary>
		private static MeshBuilder Barrels(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			float largest, smallest;
			var matrix = new Vector3(0.42f, 0.2f, 0.38f);
			switch (variant)
			{
				case 1: count = 2; largest = 0.15f; smallest = 0.12f; break;
				case 2: count = 8; largest = 0.07f; smallest = 0.04f; break;
				case 3: count = 5; largest = 0.1f; smallest = 0.07f; break;
				default: count = 4; largest = 0.11f; smallest = 0.07f; break;
			}
			var parts = new List<(Matrix4x4 place, float radius, float height, int seed)>(count);
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (count - 1f) : 0f;
				float radius = Mathf.Lerp(largest, smallest, rank) * rng.Range(0.9f, 1.1f);
				float height = radius * rng.Range(3f, 4f);
				float az = rng.NextFloat() * Tau;
				Vector3 foot = OnMatrix(matrix, az, i == 0 ? 0.05f : Mathf.Sqrt(rng.NextFloat()) * 0.75f);
				float tilt = variant == 3 ? rng.Range(70f, 88f) : i == 0 ? rng.Range(0f, 12f) : rng.Range(10f, 60f);
				Quaternion stand = Quaternion.FromToRotation(Vector3.up, Tilted(tilt, az + rng.Range(-0.6f, 0.6f)));
				// A lying barrel rests on its side with its middle on the rock; a standing one is sunk a little at its foot.
				Vector3 at = variant == 3 ? foot - stand * Vector3.up * height * 0.5f + Vector3.up * radius * 0.6f : foot - stand * Vector3.up * height * 0.12f;
				parts.Add((Matrix4x4.TRS(at, stand, Vector3.one), radius, height, seed + 83 * (i + 1)));
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.22f, lod, seed + 5);
			int keep = lod == 2 ? Mathf.Max(1, (parts.Count + 1) / 2) : parts.Count;
			for (int i = 0; i < keep; i++)
			{
				var p = parts[i];
				var r = new DeterministicRNG(p.seed);
				float h = p.height;
				Vector2[] rings = lod == 2
					? new[] { new Vector2(0.6f, 0f), new Vector2(1f, 0.5f * h), new Vector2(0.6f, h) }
					: new[] { new Vector2(0.6f, 0f), new Vector2(0.9f, 0.22f * h), new Vector2(1f, 0.5f * h), new Vector2(0.9f, 0.78f * h), new Vector2(0.6f, h) };
				Column(m, 1, p.place, 6, p.radius, rings, 0f, 0f, 0.03f, r, 0.3f, 1f);
			}
			return m;
		}

		/// <summary>
		/// Stars: points radiating every way from raised centres (sixling stars, ikaite, septentrion). v0 one star of 12 rays, v1
		/// three small of 8, v2 one urchin of 22, v3 two flat seven-rayed stars.
		/// </summary>
		private static MeshBuilder Stars(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int stars, rays;
			float length;
			bool flat = variant == 3;
			var matrix = new Vector3(0.38f, 0.2f, 0.34f);
			switch (variant)
			{
				case 1: stars = 3; rays = 8; length = 0.18f; matrix = new Vector3(0.46f, 0.18f, 0.4f); break;
				case 2: stars = 1; rays = 22; length = 0.3f; break;
				case 3: stars = 2; rays = 7; length = 0.26f; break;
				default: stars = 1; rays = 12; length = 0.35f; break;
			}
			var parts = new List<(Vector3 foot, Vector3 dir, float length, int seed)>();
			for (int s = 0; s < stars; s++)
			{
				float az = rng.NextFloat() * Tau;
				Vector3 centre = OnMatrix(matrix, az, s == 0 ? 0.05f : rng.Range(0.35f, 0.7f)) + Vector3.up * length * 0.5f;
				Quaternion turn = Quaternion.Euler(rng.Range(0f, 360f), rng.Range(0f, 360f), rng.Range(0f, 360f));
				Vector3 normal = Tilted(rng.Range(20f, 70f), az);
				Vector3 inPlane = Vector3.Cross(normal, Vector3.up).normalized;
				for (int r = 0; r < rays; r++)
				{
					Vector3 dir;
					if (flat)
					{
						dir = Quaternion.AngleAxis(360f * r / rays + rng.Range(-6f, 6f), normal) * inPlane;
					}
					else
					{
						// Rays spread evenly over the sphere (a golden spiral), turned at random.
						float y = 1f - 2f * (r + 0.5f) / rays, ring = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)), a = r * 2.39996323f;
						dir = turn * new Vector3(Mathf.Cos(a) * ring, y, Mathf.Sin(a) * ring);
					}
					if (dir.y < -0.6f)
					{
						dir.y = -0.6f;
					}
					dir.Normalize();
					float len = length * rng.Range(0.8f, 1.05f);
					parts.Add((centre - dir * len * 0.05f, dir, len, seed + 61 * parts.Count + 3));
				}
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.2f, lod, seed + 5);
			int keep = lod == 2 ? Mathf.Max(1, (parts.Count + 1) / 2) : parts.Count;
			int sides = lod == 2 ? 4 : 6;
			for (int i = 0; i < keep; i++)
			{
				var p = parts[i];
				Point(m, p.foot, p.dir, Vector3.Cross(p.dir, Vector3.up), 1f, p.length, p.length * 0.09f, 0.25f, sides, p.seed, 0.35f, 1f);
			}
			return m;
		}

		/// <summary>
		/// Droplets: smooth teardrops with pointed tails, standing and lying on a lump (tektite splash-forms, tear-glass). v0 seven,
		/// v1 four large, v2 twelve small, v3 six long-tailed lying over.
		/// </summary>
		private static MeshBuilder Droplets(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			float largest, smallest, tail = 1.6f, lie = 35f;
			var matrix = new Vector3(0.4f, 0.18f, 0.36f);
			switch (variant)
			{
				case 1: count = 4; largest = 0.15f; smallest = 0.1f; break;
				case 2: count = 12; largest = 0.07f; smallest = 0.035f; break;
				case 3: count = 6; largest = 0.09f; smallest = 0.06f; tail = 3f; lie = 85f; break;
				default: count = 7; largest = 0.11f; smallest = 0.05f; break;
			}
			var drops = new List<(Matrix4x4 place, float r)>(count);
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (count - 1f) : 0f;
				float r = Mathf.Lerp(largest, smallest, rank) * rng.Range(0.9f, 1.1f);
				float az = rng.NextFloat() * Tau;
				Vector3 foot = OnMatrix(matrix, az, i == 0 ? 0.05f : Mathf.Sqrt(rng.NextFloat()) * 0.78f);
				Quaternion stand = Quaternion.FromToRotation(Vector3.up, Tilted(rng.Range(0f, lie), az + rng.Range(-0.8f, 0.8f)));
				drops.Add((Matrix4x4.TRS(foot - Vector3.up * r * 0.4f, stand, Vector3.one), r));
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.2f, lod, seed + 5);
			int bulge = lod == 0 ? 5 : lod == 1 ? 4 : 3, segments = lod == 0 ? 10 : lod == 1 ? 8 : 6;
			int keep = lod == 2 ? Mathf.Max(1, (drops.Count + 1) / 2) : drops.Count;
			for (int i = 0; i < keep; i++)
			{
				float r = drops[i].r;
				// Up from the bottom pole round the bulb to 30° above its middle, then straight to the tail's point.
				var profile = new List<LP>(bulge + 2);
				for (int k = 0; k <= bulge; k++)
				{
					float a = -0.5f * Mathf.PI + (0.5f * Mathf.PI + Mathf.PI / 6f) * k / bulge;
					profile.Add(new LP(k == 0 ? 0f : r * Mathf.Cos(a), r + r * Mathf.Sin(a), Mathf.Lerp(0.4f, 0.75f, k / (float)bulge)));
				}
				profile.Add(new LP(0f, r * 1.5f + r * tail, 1f));
				Lathe(m, 1, profile, segments, drops[i].place, 2f * Tau * r);
			}
			return m;
		}

		/// <summary>
		/// Tabular: thin flat plates stood on edge and intergrown (barite, ilmenite, hydrohalite, celestine). v0 six hexagonal, v1 four
		/// oblong, v2 ten small, v3 three great crossing plates.
		/// </summary>
		private static MeshBuilder Plates(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count, sides = 6;
			float largest, smallest, oblong = 1f;
			var matrix = new Vector3(0.42f, 0.2f, 0.38f);
			switch (variant)
			{
				case 1: count = 4; largest = 0.22f; smallest = 0.16f; sides = 4; oblong = 1.7f; break;
				case 2: count = 10; largest = 0.12f; smallest = 0.07f; break;
				case 3: count = 3; largest = 0.3f; smallest = 0.27f; break;
				default: count = 6; largest = 0.2f; smallest = 0.11f; break;
			}
			var parts = new List<(Matrix4x4 place, float radius, int seed)>(count);
			float cross = rng.NextFloat() * 180f;
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (count - 1f) : 0f;
				float radius = Mathf.Lerp(largest, smallest, rank) * rng.Range(0.9f, 1.1f);
				float az = rng.NextFloat() * Tau;
				Vector3 foot = OnMatrix(matrix, az, variant == 3 ? 0.05f : i == 0 ? 0.05f : Mathf.Sqrt(rng.NextFloat()) * 0.75f);
				// The plate's normal near level, so it stands on edge (v3's crossing at even turns about one upright).
				Vector3 normal = variant == 3 ? Tilted(90f - rng.Range(0f, 12f), (cross + 60f * i) * Mathf.Deg2Rad) : Tilted(rng.Range(50f, 90f), rng.NextFloat() * Tau);
				Quaternion frame = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.AngleAxis(rng.Range(0f, 360f), Vector3.up);
				parts.Add((Matrix4x4.TRS(foot + Vector3.up * radius * 0.45f, frame, new Vector3(oblong, 1f, 1f)), radius, seed + 67 * (i + 1)));
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.22f, lod, seed + 5);
			int keep = lod == 2 ? Mathf.Max(1, (parts.Count + 1) / 2) : parts.Count;
			for (int i = 0; i < keep; i++)
			{
				var r = new DeterministicRNG(parts[i].seed);
				float thick = parts[i].radius * 0.09f;
				Column(m, 1, parts[i].place, sides, parts[i].radius, new[] { new Vector2(1f, -thick), new Vector2(1f, thick) }, 0f, 0f, 0.06f, r, 0.4f, 1f);
			}
			return m;
		}

		/// <summary>
		/// Radiating: sprays of fine needles from a point (wollastonite, phillipsite, aragonite, stibnite). v0 one spray of 30, v1
		/// two of 18, v2 a flat fan of 24, v3 four small of 10.
		/// </summary>
		private static MeshBuilder Sprays(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int sprays, needles;
			float length;
			var matrix = new Vector3(0.4f, 0.2f, 0.36f);
			switch (variant)
			{
				case 1: sprays = 2; needles = 18; length = 0.36f; break;
				case 2: sprays = 1; needles = 24; length = 0.5f; break;
				case 3: sprays = 4; needles = 10; length = 0.22f; matrix = new Vector3(0.48f, 0.18f, 0.42f); break;
				default: sprays = 1; needles = 30; length = 0.45f; break;
			}
			var parts = new List<(Vector3 foot, Vector3 dir, float length, int seed)>();
			for (int s = 0; s < sprays; s++)
			{
				float az = rng.NextFloat() * Tau;
				Vector3 root = OnMatrix(matrix, az, s == 0 ? 0.05f : rng.Range(0.35f, 0.7f)) - Vector3.up * 0.02f;
				float plane = rng.NextFloat() * Tau;
				for (int i = 0; i < needles; i++)
				{
					// The fan's needles lean either way in its plane (a negative tilt leans the other way round); a spray's fill a cap of 72°.
					Vector3 dir = variant == 2
						? Tilted(Mathf.Lerp(-70f, 70f, needles > 1 ? i / (needles - 1f) : 0.5f) + rng.Range(-3f, 3f), plane)
						: Tilted(Mathf.Acos(Mathf.Lerp(1f, Mathf.Cos(72f * Mathf.Deg2Rad), rng.NextFloat())) * Mathf.Rad2Deg, rng.NextFloat() * Tau);
					float len = length * rng.Range(0.75f, 1.05f);
					parts.Add((root, dir, len, seed + 47 * parts.Count + 5));
				}
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.2f, lod, seed + 5);
			int keep = lod == 2 ? Mathf.Max(1, (parts.Count + 1) / 2) : parts.Count;
			for (int i = 0; i < keep; i++)
			{
				var p = parts[i];
				Point(m, p.foot, p.dir, Vector3.Cross(p.dir, Vector3.up), 1f, p.length, p.length * 0.028f, 0.1f, 4, p.seed, 0.3f, 1f);
			}
			return m;
		}

		/// <summary>
		/// Leaves: thin curved lance-shaped plates fanned from a root, each bowed back along its length (leafglass, sibylleaf). v0 a
		/// fan of seven, v1 four large, v2 eleven small, v3 two fans of five.
		/// </summary>
		private static MeshBuilder Leaves(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int fans, leaves;
			float length;
			var matrix = new Vector3(0.38f, 0.18f, 0.34f);
			switch (variant)
			{
				case 1: fans = 1; leaves = 4; length = 0.55f; break;
				case 2: fans = 1; leaves = 11; length = 0.26f; break;
				case 3: fans = 2; leaves = 5; length = 0.34f; matrix = new Vector3(0.48f, 0.16f, 0.4f); break;
				default: fans = 1; leaves = 7; length = 0.4f; break;
			}
			var parts = new List<(Vector3 foot, Vector3 dir, Vector3 outward, float length, float bend, int seed)>();
			for (int f = 0; f < fans; f++)
			{
				float az = rng.NextFloat() * Tau;
				Vector3 root = OnMatrix(matrix, az, f == 0 ? 0.05f : rng.Range(0.4f, 0.7f)) - Vector3.up * 0.03f;
				float turn = rng.NextFloat() * Tau;
				for (int i = 0; i < leaves; i++)
				{
					float a = turn + Tau * i / leaves + rng.Range(-0.25f, 0.25f);
					var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
					Vector3 dir = Tilted(rng.Range(15f, 55f), a);
					parts.Add((root, dir, outward, length * rng.Range(0.75f, 1.05f), rng.Range(0.15f, 0.3f), seed + 89 * parts.Count + 11));
				}
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.2f, lod, seed + 5);
			int sides = lod == 0 ? 8 : lod == 1 ? 6 : 5;
			int keep = lod == 2 ? Mathf.Max(1, (parts.Count + 1) / 2) : parts.Count;
			for (int i = 0; i < keep; i++)
			{
				var p = parts[i];
				float L = p.length, w = L * 0.14f;
				// Built upright in its own frame (broad across x, thin across z), bowed back along z, then laid on its way out.
				var leaf = new MeshBuilder(1);
				Column(leaf, 0, Matrix4x4.Scale(new Vector3(1f, 1f, 0.12f)), sides, w,
					new[] { new Vector2(0.3f, 0f), new Vector2(0.85f, 0.18f * L), new Vector2(1f, 0.4f * L), new Vector2(0.8f, 0.64f * L), new Vector2(0.45f, 0.85f * L) },
					0.15f * L, 0f, 0.05f, new DeterministicRNG(p.seed), 0.35f, 1f);
				// The bow z += bend·L·t² (t = y/L): each normal through the bow's inverse transpose, n.y −= 2·bend·t·n.z.
				for (int k = 0; k < leaf.VertexCount; k++)
				{
					Vector3 q = leaf.Positions[k], n = leaf.Normals[k];
					float t = q.y / L;
					leaf.Positions[k] = new Vector3(q.x, q.y, q.z + p.bend * L * t * t);
					leaf.Normals[k] = new Vector3(n.x, n.y - 2f * p.bend * t * n.z, n.z).normalized;
				}
				Vector3 side = Vector3.ProjectOnPlane(p.outward, p.dir);
				Quaternion rotation = side.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(side.normalized, p.dir) : UpTo(p.dir, 0f);
				leaf.Transform(Matrix4x4.TRS(p.foot, rotation, Vector3.one));
				m.Append(leaf, 1);
			}
			return m;
		}

		/// <summary>
		/// Trapiche: hexagonal prisms each of six crystal sectors between six dark spokes round a dark core (rock), so its section
		/// shows the six-rayed star. v0 four, v1 two large, v2 seven small, v3 five lying, their sections turned out.
		/// </summary>
		private static MeshBuilder TrapicheCrystals(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			float largest, smallest;
			var matrix = new Vector3(0.42f, 0.2f, 0.38f);
			switch (variant)
			{
				case 1: count = 2; largest = 0.16f; smallest = 0.13f; break;
				case 2: count = 7; largest = 0.07f; smallest = 0.045f; break;
				case 3: count = 5; largest = 0.09f; smallest = 0.07f; break;
				default: count = 4; largest = 0.11f; smallest = 0.08f; break;
			}
			var parts = new List<(Matrix4x4 place, float radius, float height, float turn, float v)>(count);
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (count - 1f) : 0f;
				float radius = Mathf.Lerp(largest, smallest, rank) * rng.Range(0.9f, 1.1f);
				float height = radius * rng.Range(1.8f, 2.6f);
				float az = rng.NextFloat() * Tau;
				Vector3 foot = OnMatrix(matrix, az, i == 0 ? 0.05f : Mathf.Sqrt(rng.NextFloat()) * 0.75f);
				float tilt = variant == 3 ? rng.Range(70f, 85f) : i == 0 ? rng.Range(0f, 10f) : rng.Range(8f, 35f);
				Quaternion stand = Quaternion.FromToRotation(Vector3.up, Tilted(tilt, az + rng.Range(-0.5f, 0.5f)));
				Vector3 at = variant == 3 ? foot - stand * Vector3.up * height * 0.5f + Vector3.up * radius * 0.5f : foot - stand * Vector3.up * height * 0.15f;
				parts.Add((Matrix4x4.TRS(at, stand, Vector3.one), radius, height, rng.NextFloat() * Tau, rng.NextFloat()));
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.22f, lod, seed + 5);
			int keep = lod == 2 ? Mathf.Max(1, (parts.Count + 1) / 2) : parts.Count;
			for (int i = 0; i < keep; i++)
			{
				var p = parts[i];
				float R = p.radius, core = R * 0.2f, spoke = R * 0.035f, h = p.height;
				var c = new Vector2[6];
				for (int k = 0; k < 6; k++)
				{
					float a = p.turn + Tau * k / 6f;
					c[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
				}
				var coreRing = new Vector2[6];
				for (int k = 0; k < 6; k++)
				{
					int n = (k + 1) % 6;
					coreRing[k] = c[k] * core;
					// A sector of crystal between two spokes.
					Extruded(m, 1, p.place, new[] { c[k] * core, c[n] * core, c[n] * R, c[k] * R }, 0f, h, 0.3f, 1f, p.v + 0.07f * k);
					// The spoke along the corner's line, a hair proud of the faces so the star shows on the section and the sides.
					Vector2 perp = new Vector2(-c[k].y, c[k].x) * spoke;
					Extruded(m, 0, p.place, new[] { c[k] * core * 0.9f + perp, c[k] * R * 1.01f + perp, c[k] * R * 1.01f - perp, c[k] * core * 0.9f - perp }, -0.002f, h + 0.003f, 0f, 0f, p.v);
				}
				Extruded(m, 0, p.place, coreRing, -0.002f, h + 0.003f, 0f, 0f, p.v);
			}
			return m;
		}

		/// <summary>
		/// Matrix: nodules sunk all but their caps in a big host lump and thin veins crossing its top (turquoise's spider-web, lapis,
		/// a gold seam). v0 six nodules and eight veins, v1 three great and four, v2 twelve small and fourteen, v3 a seam slab right
		/// through the lump and four nodules.
		/// </summary>
		private static MeshBuilder HostNodules(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int nodules, veins;
			float largest, smallest;
			var host = new Vector3(0.5f, 0.26f, 0.44f);
			switch (variant)
			{
				case 1: nodules = 3; veins = 4; largest = 0.14f; smallest = 0.1f; break;
				case 2: nodules = 12; veins = 14; largest = 0.07f; smallest = 0.035f; break;
				case 3: nodules = 4; veins = 0; largest = 0.09f; smallest = 0.06f; host = new Vector3(0.55f, 0.24f, 0.4f); break;
				default: nodules = 6; veins = 8; largest = 0.1f; smallest = 0.05f; break;
			}
			Vector3 Normal(Vector3 p) => new Vector3(p.x / (host.x * host.x), p.y / (host.y * host.y), p.z / (host.z * host.z)).normalized;
			var beads = new List<(Vector3 at, Vector3 radii, Quaternion turn)>(nodules);
			for (int i = 0; i < nodules; i++)
			{
				float rank = nodules > 1 ? i / (nodules - 1f) : 0f;
				float r = Mathf.Lerp(largest, smallest, rank) * rng.Range(0.9f, 1.1f);
				Vector3 at = OnMatrix(host, rng.NextFloat() * Tau, Mathf.Sqrt(rng.NextFloat()) * 0.8f, 0.92f);
				beads.Add((at - Normal(at) * r * 0.45f, new Vector3(r, r * 0.7f, r * rng.Range(0.8f, 1f)), Quaternion.FromToRotation(Vector3.up, Normal(at))));
			}
			var bars = new List<(Matrix4x4 place, Vector3 half, float v)>();
			for (int i = 0; i < veins; i++)
			{
				Vector3 at = OnMatrix(host, rng.NextFloat() * Tau, Mathf.Sqrt(rng.NextFloat()) * 0.7f, 0.9f);
				Quaternion lie = Quaternion.FromToRotation(Vector3.up, Normal(at)) * Quaternion.AngleAxis(rng.Range(0f, 360f), Vector3.up);
				bars.Add((Matrix4x4.TRS(at, lie, Vector3.one), new Vector3(rng.Range(0.08f, 0.2f), 0.012f, rng.Range(0.006f, 0.01f)), rng.NextFloat()));
			}
			if (variant == 3)
			{
				// The seam: a slab of crystal right through the host, showing on its top and both its sides.
				Quaternion dip = Quaternion.Euler(rng.Range(-15f, 15f), rng.Range(0f, 360f), rng.Range(70f, 85f));
				bars.Add((Matrix4x4.TRS(new Vector3(0f, host.y * 0.3f, 0f), dip, Vector3.one), new Vector3(host.y * 1.1f, 0.03f, host.x * 0.85f), rng.NextFloat()));
			}
			var m = new MeshBuilder(2);
			Matrix(m, host, 0.12f, lod, seed + 5);
			int segments = lod == 0 ? 9 : lod == 1 ? 7 : 6, rings = lod == 0 ? 5 : 4;
			int keep = lod == 2 ? Mathf.Max(1, (beads.Count + 1) / 2) : beads.Count;
			for (int i = 0; i < keep; i++)
			{
				int first = m.VertexCount;
				Bead(m, 1, Vector3.zero, beads[i].radii, 0.75f, segments, rings);
				m.Transform(Matrix4x4.TRS(beads[i].at, beads[i].turn, Vector3.one), first);
			}
			int keepBars = lod == 2 ? (bars.Count + 1) / 2 : bars.Count;
			for (int i = 0; i < bars.Count; i++)
			{
				// The seam (v3's last) is kept at every level; veins thin out at the last.
				if (i >= keepBars && !(variant == 3 && i == bars.Count - 1))
				{
					continue;
				}
				Cuboid(m, 1, bars[i].place, bars[i].half, 0.5f, 0.9f, bars[i].v);
			}
			return m;
		}
	}
}
#endif
