#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The cave kit's glow art as meshes (<see cref="CaveGlowKind"/>, 2026-10-13): glowing crystal clusters and giant
	/// columns, luminous fungi (Mycena tufts, giant glowcaps, Panellus brackets), glow-worm snares and ore seams. Pure
	/// geometry, three levels each; names, hues and materials are ProceduralArtCatalogue.Cave.cs's.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Texture coordinates.</b> On everything that glows, u is a glow level (<see cref="Glow"/>: 0 dark, 1 brightest)
	/// read through the shared glow ramps, and v a mottle coordinate that runs round a part (so a cap's gills streak
	/// radially and a stalk's fibres run along it). On rock and ore (a crystal's matrix, a seam), the texture runs in metres.
	/// </para>
	/// <para>
	/// <b>Seating.</b> Every piece reaches past its mount's surface (a matrix below the floor, a seam's back and a bracket's
	/// root into the wall, a glow-worm's thread and larva up into the roof), so it sits on rock that is not flat without a
	/// gap showing.
	/// </para>
	/// <para>
	/// <b>Determinism.</b> Every shape is drawn before anything depends on the level (one stream a variant, parts sorted by
	/// size), so the levels share one form and differ only in tessellation and in how many of the smallest parts they keep.
	/// </para>
	/// </remarks>
	public static class CaveGlowMeshes
	{
		public const int Levels = 3;

		private const float Tau = Mathf.PI * 2f;

		private static readonly Color32 White = new Color32(255, 255, 255, 255);

		/// <summary>A level of a shape's variant, its tangents made.</summary>
		public static MeshBuilder Build(CaveGlowKind shape, int variant, int lod, int seed)
		{
			lod = Mathf.Clamp(lod, 0, Levels - 1);
			int s = ProceduralNoise.SeedFor($"CaveGlow/{shape}/{variant}", seed);
			MeshBuilder m;
			switch (shape)
			{
				case CaveGlowKind.Crystal: m = CrystalCluster(variant, lod, s); break;
				case CaveGlowKind.GiantCrystal: m = GiantCrystals(variant, lod, s); break;
				case CaveGlowKind.Fungus: m = FungusTuft(variant, lod, s); break;
				case CaveGlowKind.GiantFungus: m = GiantFungi(variant, lod, s); break;
				case CaveGlowKind.ShelfFungus: m = ShelfFungi(variant, lod, s); break;
				case CaveGlowKind.GlowWorms: m = GlowWorms(variant, lod, s); break;
				case CaveGlowKind.OreVein: m = OreSeam(variant, lod, s); break;
				default: throw new ArgumentOutOfRangeException(nameof(shape), $"{shape} wears another kind's meshes");
			}
			m.RecalculateTangents();
			return m;
		}

		/// <summary>How many submeshes a shape's meshes have: crystals 2 (matrix rock, crystal), seams 2 (quartz, ore), the rest 1.</summary>
		public static int Submeshes(CaveGlowKind shape) => shape == CaveGlowKind.Crystal || shape == CaveGlowKind.GiantCrystal || shape == CaveGlowKind.OreVein ? 2 : 1;

		/// <summary>A glow level's texture coordinate: kept inside the ramp, so the repeat never blends its two ends.</summary>
		public static float Glow(float level) => 0.03f + 0.94f * Mathf.Clamp01(level);

		// ── Maps ──────────────────────────────────────────────────────

		/// <summary>
		/// A glow ramp, tileable in v: along u the surface brightens (the albedo, from a dim 0.32 to 0.92) or begins to glow
		/// (the emission: none below a level of 0.08, rising to full), mottled by streaky noise, longer along u than across
		/// it, so a cap's gills and a crystal's faces glow unevenly as living tissue and flawed crystal do. The emission is
		/// written in sRGB, as the importer reads it, so its linear value is the ramp's.
		/// </summary>
		public static Color32[] GlowRamp(int size, int seed, bool emission)
		{
			var pixels = new Color32[size * size];
			int s = ProceduralNoise.SeedFor(emission ? "CaveGlow/Emission" : "CaveGlow/Albedo", seed);
			for (int y = 0; y < size; y++)
			{
				float v = (y + 0.5f) / size;
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					float mottle = ProceduralNoise.PeriodicFbm(u, v, 2, 12, 3, 0.55f, s);
					float value;
					if (emission)
					{
						float e = Mathf.Pow(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 1f, u)), 1.4f);
						value = Mathf.Pow(Mathf.Clamp01(e * (1f + 0.32f * mottle)), 1f / 2.2f);
					}
					else
					{
						value = Mathf.Clamp01((0.32f + 0.6f * u) * (1f + 0.16f * mottle));
					}
					byte b = (byte)Mathf.RoundToInt(value * 255f);
					pixels[y * size + x] = new Color32(b, b, b, 255);
				}
			}
			return pixels;
		}

		/// <summary>
		/// Ore grains, tileable: interlocking crystals (cellular cells) each its own brightness, darker along the boundaries
		/// between them, a little speckle within; the alpha is each grain's smoothness, so a seam glitters facet by facet
		/// under a lamp. Tinted per ore by its material.
		/// </summary>
		public static Color32[] OreGrains(int size, int seed)
		{
			var pixels = new Color32[size * size];
			int s = ProceduralNoise.SeedFor("CaveGlow/Ore", seed);
			for (int y = 0; y < size; y++)
			{
				float v = (y + 0.5f) / size;
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					ProceduralNoise.Cell cell = ProceduralNoise.PeriodicCellular(u, v, 12, 0.9f, s);
					float grain = 0.55f + 0.45f * ProceduralNoise.ToUnit(cell.Id);
					float boundary = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((cell.F2 - cell.F1) / 0.08f));
					float speck = 1f + 0.12f * ProceduralNoise.PeriodicFbm(u, v, 32, 2, 0.5f, s + 1);
					float value = Mathf.Clamp01(grain * Mathf.Lerp(0.42f, 1f, boundary) * speck);
					float smooth = Mathf.Lerp(0.3f, 1f, ProceduralNoise.ToUnit(ProceduralNoise.Mix(cell.Id ^ 0x9E3779B9u))) * Mathf.Lerp(0.5f, 1f, boundary);
					byte b = (byte)Mathf.RoundToInt(value * 255f);
					pixels[y * size + x] = new Color32(b, b, b, (byte)Mathf.RoundToInt(smooth * 255f));
				}
			}
			return pixels;
		}

		// ── Building blocks ───────────────────────────────────────────

		/// <summary>A point of a lathe's profile: radius, height and glow level; a crease starts a sharp edge.</summary>
		private struct LP
		{
			public float R, Y, Glow;
			public bool Crease;

			public LP(float r, float y, float glow, bool crease = false)
			{
				R = r;
				Y = y;
				Glow = glow;
				Crease = crease;
			}
		}

		/// <summary>The outward normal (in the radius–height plane) of the band from <paramref name="a"/> to <paramref name="b"/>.</summary>
		private static Vector2 BandNormal(LP a, LP b)
		{
			var n = new Vector2(b.Y - a.Y, -(b.R - a.R));
			return n.sqrMagnitude > 1e-14f ? n.normalized : Vector2.zero;
		}

		/// <summary>
		/// A solid of revolution about +y, placed by <paramref name="place"/>: the profile runs up the outside (an end on the
		/// axis closes to a point; one off it is left open, for a stalk to enter); a <paramref name="closed"/> profile is a
		/// loop wound bottom-out, up the outside, top-in, down the inside (a skirt). Smooth round the axis, and along the
		/// profile but at creases. v runs <paramref name="vTurns"/> times round.
		/// </summary>
		private static void Lathe(MeshBuilder m, int sub, IList<LP> p, int segments, Matrix4x4 place, float vTurns, float phase = 0f, bool closed = false)
		{
			int first = m.VertexCount;
			int n = p.Count, bands = closed ? n : n - 1;
			segments = Mathf.Max(3, segments);
			for (int i = 0; i < bands; i++)
			{
				LP a = p[i], b = p[(i + 1) % n];
				if (a.R < 1e-6f && b.R < 1e-6f)
				{
					continue;
				}
				Vector2 band = BandNormal(a, b);
				if (band == Vector2.zero)
				{
					continue;
				}
				Vector2 na = PointNormal(p, i, closed, band, true), nb = PointNormal(p, (i + 1) % n, closed, band, false);
				bool poleA = a.R < 1e-6f, poleB = b.R < 1e-6f;
				int ringA = m.VertexCount;
				for (int k = 0; k <= segments; k++)
				{
					float ang = phase + Tau * k / segments, c = Mathf.Cos(ang), s = Mathf.Sin(ang);
					float v = vTurns * (k + (poleA ? 0.5f : 0f)) / segments;
					m.AddVertex(new Vector3(a.R * c, a.Y, a.R * s), new Vector3(na.x * c, na.y, na.x * s).normalized, new Vector2(Glow(a.Glow), v), White);
				}
				int ringB = m.VertexCount;
				for (int k = 0; k <= segments; k++)
				{
					float ang = phase + Tau * k / segments, c = Mathf.Cos(ang), s = Mathf.Sin(ang);
					float v = vTurns * (k + (poleB ? 0.5f : 0f)) / segments;
					m.AddVertex(new Vector3(b.R * c, b.Y, b.R * s), new Vector3(nb.x * c, nb.y, nb.x * s).normalized, new Vector2(Glow(b.Glow), v), White);
				}
				for (int k = 0; k < segments; k++)
				{
					float mid = phase + Tau * (k + 0.5f) / segments;
					var facing = new Vector3(band.x * Mathf.Cos(mid), band.y, band.x * Mathf.Sin(mid));
					if (poleA)
					{
						m.AddTriangle(sub, ringA + k, ringB + k, ringB + k + 1, facing);
					}
					else if (poleB)
					{
						m.AddTriangle(sub, ringA + k, ringA + k + 1, ringB + k, facing);
					}
					else
					{
						m.AddQuad(sub, ringA + k, ringA + k + 1, ringB + k + 1, ringB + k, facing);
					}
				}
			}
			m.Transform(place, first);
		}

		/// <summary>A profile point's normal: the band's own at a crease, an open end or a pole (straight along the axis), else the mean of its two bands'.</summary>
		private static Vector2 PointNormal(IList<LP> p, int i, bool closed, Vector2 band, bool start)
		{
			int n = p.Count;
			LP at = p[i];
			if (at.R < 1e-6f)
			{
				// A pole: straight along the axis, the way the band leans.
				return new Vector2(0f, band.y >= 0f ? 1f : -1f);
			}
			if (at.Crease)
			{
				return band;
			}
			int prev = i - 1, next = i + 1;
			if (closed)
			{
				prev = (prev + n) % n;
				next %= n;
			}
			if (prev < 0 || next >= n)
			{
				return band;
			}
			Vector2 sum = BandNormal(p[prev], at) + BandNormal(at, p[next]);
			return sum.sqrMagnitude > 1e-12f ? sum.normalized : band;
		}

		/// <summary>
		/// A tube along a path (a stalk, a thread): radial normals, rings turned by parallel transport so it never twists;
		/// a flat cap where asked. Glow and radius per point; v runs round in metres.
		/// </summary>
		private static void Tube(MeshBuilder m, int sub, IList<Vector3> path, IList<float> radius, IList<float> glow, int sides, bool capStart, bool capEnd)
		{
			int n = path.Count;
			sides = Mathf.Max(3, sides);
			Vector3 t0 = (path[1] - path[0]).normalized;
			Vector3 side = Mathf.Abs(t0.y) < 0.9f ? Vector3.Cross(t0, Vector3.up).normalized : Vector3.Cross(t0, Vector3.right).normalized;
			var rings = new int[n];
			var tangents = new Vector3[n];
			for (int i = 0; i < n; i++)
			{
				Vector3 t = (path[Mathf.Min(i + 1, n - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
				tangents[i] = t;
				side = (side - t * Vector3.Dot(side, t)).normalized;
				Vector3 bin = Vector3.Cross(t, side);
				rings[i] = m.VertexCount;
				for (int k = 0; k <= sides; k++)
				{
					float a = Tau * k / sides;
					Vector3 dir = side * Mathf.Cos(a) + bin * Mathf.Sin(a);
					// v in metres round (doubled): a thread's few millimetres must not sweep the whole mottle, or its mip
					// level would blur its glow ramp into the mean.
					m.AddVertex(path[i] + dir * radius[i], dir, new Vector2(Glow(glow[i]), 2f * Tau * radius[i] * k / sides), White);
				}
			}
			for (int i = 0; i + 1 < n; i++)
			{
				for (int k = 0; k < sides; k++)
				{
					int a = rings[i] + k, b = rings[i] + k + 1, c = rings[i + 1] + k + 1, d = rings[i + 1] + k;
					Vector3 facing = (m.Normals[a] + m.Normals[b] + m.Normals[c] + m.Normals[d]);
					m.AddQuad(sub, a, b, c, d, facing);
				}
			}
			if (capStart)
			{
				Cap(m, sub, path[0], -tangents[0], rings[0], sides, glow[0], 2f * Tau * radius[0]);
			}
			if (capEnd)
			{
				Cap(m, sub, path[n - 1], tangents[n - 1], rings[n - 1], sides, glow[n - 1], 2f * Tau * radius[n - 1]);
			}
		}

		/// <summary>A flat cap over a tube's ring, facing <paramref name="normal"/>.</summary>
		private static void Cap(MeshBuilder m, int sub, Vector3 centre, Vector3 normal, int ring, int sides, float glow, float vRound)
		{
			int c = m.AddVertex(centre, normal, new Vector2(Glow(glow), 0.5f * vRound), White);
			int first = m.VertexCount;
			for (int k = 0; k < sides; k++)
			{
				m.AddVertex(m.Positions[ring + k], normal, new Vector2(Glow(glow), vRound * k / sides), White);
			}
			for (int k = 0; k < sides; k++)
			{
				m.AddTriangle(sub, c, first + k, first + (k + 1) % sides, normal);
			}
		}

		/// <summary>A small bead (an ellipsoid of few facets): a droplet, a knot of glow, a spot on a cap.</summary>
		private static void Bead(MeshBuilder m, int sub, Vector3 centre, Vector3 radii, float glow, int segments, int rings)
		{
			var profile = new List<LP>(rings + 1);
			for (int i = 0; i <= rings; i++)
			{
				float a = -0.5f * Mathf.PI + Mathf.PI * i / rings;
				profile.Add(new LP(i == 0 || i == rings ? 0f : Mathf.Cos(a), Mathf.Sin(a), glow));
			}
			// v round in metres, as a tube's (a droplet is a few millimetres round).
			Lathe(m, sub, profile, segments, Matrix4x4.TRS(centre, Quaternion.identity, radii), 2f * Tau * Mathf.Max(radii.x, radii.z));
		}

		/// <summary>
		/// A lump of rock (a crystal's matrix, an ore's nugget): an ellipsoid roughened by 3D noise, its normals from its
		/// faces, textured in metres. Built apart and appended, so its normals never blend with what it touches.
		/// </summary>
		private static MeshBuilder Lump(Vector3 radii, int segments, int rings, float rough, int seed)
		{
			var b = new MeshBuilder(1);
			segments = Mathf.Max(4, segments);
			rings = Mathf.Max(2, rings);
			Vector3 Point(float lat, float lon)
			{
				var dir = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
				float k = 1f + rough * ProceduralNoise.Fbm3(dir * 1.9f, 3, 0.5f, seed);
				return Vector3.Scale(dir, radii) * k;
			}
			Vector2 Uv(Vector3 p) => new Vector2(p.x + p.z * 0.7f, p.y + p.z * 0.3f);
			var grid = new int[rings + 1, segments + 1];
			for (int r = 0; r <= rings; r++)
			{
				float lat = -0.5f * Mathf.PI + Mathf.PI * r / rings;
				for (int k = 0; k <= segments; k++)
				{
					float lon = Tau * (k + (r == 0 || r == rings ? 0.5f : 0f)) / segments;
					Vector3 p = Point(lat, lon);
					grid[r, k] = b.AddVertex(p, Vector3.zero, Uv(p), White);
				}
			}
			for (int r = 0; r < rings; r++)
			{
				for (int k = 0; k < segments; k++)
				{
					int a = grid[r, k], c = grid[r, k + 1], d = grid[r + 1, k + 1], e = grid[r + 1, k];
					Vector3 facing = b.Positions[a] + b.Positions[c] + b.Positions[d] + b.Positions[e];
					if (r == 0)
					{
						b.AddTriangle(0, a, d, e, facing);
					}
					else if (r == rings - 1)
					{
						b.AddTriangle(0, a, c, e, facing);
					}
					else
					{
						b.AddQuad(0, a, c, d, e, facing);
					}
				}
			}
			b.RecalculateNormals(true);
			return b;
		}

		/// <summary>
		/// One crystal (flat faces, as real crystal faces are planes): a prism of <paramref name="sides"/> faces a little
		/// irregular, narrowing to its shoulder, ended by a pyramid whose apex is a little off the axis; its foot (capped)
		/// at the origin of <paramref name="place"/>, its axis +y. Brightest at its tip, dim at the foot sunk in its matrix.
		/// </summary>
		private static void CrystalPoint(MeshBuilder m, int sub, Matrix4x4 place, float radius, float body, float tip, int sides, DeterministicRNG rng,
			float glowFoot, float glowTip)
		{
			int first = m.VertexCount;
			float turn = rng.NextFloat() * Tau;
			float taper = rng.Range(0.82f, 0.97f);
			var foot = new Vector3[sides];
			for (int i = 0; i < sides; i++)
			{
				float a = turn + Tau * (i + rng.Range(-0.18f, 0.18f)) / sides;
				float r = radius * rng.Range(0.88f, 1.12f);
				foot[i] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
			}
			var apex = new Vector3(rng.Range(-0.12f, 0.12f) * radius, body + tip, rng.Range(-0.12f, 0.12f) * radius);
			float glowShoulder = Mathf.Lerp(glowFoot, glowTip, 0.72f);
			float v0 = rng.NextFloat();
			Vector3 Shoulder(int i) => new Vector3(foot[i].x * taper, body, foot[i].z * taper);
			for (int i = 0; i < sides; i++)
			{
				int j = (i + 1) % sides;
				Vector3 a = foot[i], b = foot[j], c = Shoulder(j), d = Shoulder(i);
				Vector3 n = Vector3.Cross(c - a, b - a).normalized;
				Vector3 outward = (a + b) * 0.5f;
				outward.y = 0f;
				if (Vector3.Dot(n, outward) < 0f)
				{
					n = -n;
				}
				float v = v0 + 0.17f * i;
				int ia = m.AddVertex(a, n, new Vector2(Glow(glowFoot), v), White);
				int ib = m.AddVertex(b, n, new Vector2(Glow(glowFoot), v + 0.17f), White);
				int ic = m.AddVertex(c, n, new Vector2(Glow(glowShoulder), v + 0.17f), White);
				int id = m.AddVertex(d, n, new Vector2(Glow(glowShoulder), v), White);
				m.AddQuad(sub, ia, ib, ic, id, n);
				// The termination's face over this side.
				Vector3 tn = Vector3.Cross(apex - d, c - d).normalized;
				Vector3 tout = (c + d) * 0.5f - new Vector3(apex.x, body, apex.z);
				tout.y = Mathf.Max(0.05f, tout.y) + 0.25f * tout.magnitude;
				if (Vector3.Dot(tn, tout) < 0f)
				{
					tn = -tn;
				}
				int td = m.AddVertex(d, tn, new Vector2(Glow(glowShoulder), v), White);
				int tc = m.AddVertex(c, tn, new Vector2(Glow(glowShoulder), v + 0.17f), White);
				int ta = m.AddVertex(apex, tn, new Vector2(Glow(glowTip), v + 0.085f), White);
				m.AddTriangle(sub, td, tc, ta, tn);
			}
			// The foot, capped (it is sunk in the matrix, but closed all the same).
			int centre = m.AddVertex(Vector3.zero, Vector3.down, new Vector2(Glow(glowFoot), v0), White);
			int ring = m.VertexCount;
			for (int i = 0; i < sides; i++)
			{
				m.AddVertex(foot[i], Vector3.down, new Vector2(Glow(glowFoot), v0 + 0.1f * i), White);
			}
			for (int i = 0; i < sides; i++)
			{
				m.AddTriangle(sub, centre, ring + i, ring + (i + 1) % sides, Vector3.down);
			}
			m.Transform(place, first);
		}

		/// <summary>The rotation taking +y onto <paramref name="dir"/>, then turned <paramref name="spin"/> degrees about it.</summary>
		private static Quaternion UpTo(Vector3 dir, float spin) => Quaternion.FromToRotation(Vector3.up, dir.normalized) * Quaternion.AngleAxis(spin, Vector3.up);

		/// <summary>A direction tilted <paramref name="degrees"/> from +y toward the azimuth <paramref name="azimuth"/> (radians).</summary>
		private static Vector3 Tilted(float degrees, float azimuth)
		{
			float t = degrees * Mathf.Deg2Rad;
			return new Vector3(Mathf.Sin(t) * Mathf.Cos(azimuth), Mathf.Cos(t), Mathf.Sin(t) * Mathf.Sin(azimuth));
		}

		// ── Crystals ──────────────────────────────────────────────────

		private struct CrystalPart
		{
			public Vector3 Foot, Dir;
			public float Length, Radius, Spin;
			public int Seed;
		}

		/// <summary>
		/// A cluster of crystal points grown from a lump of host rock: the longest nucleated nearest the middle and stand
		/// near upright, the rest lean further out the further out they stand, as a druse grows. v0 a druse of eleven
		/// (0.25–1 m), v1 five stout points (to 1.3 m), v2 a spray of sixteen slender needles.
		/// </summary>
		private static MeshBuilder CrystalCluster(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			float longest, shortest, girth, lean;
			Vector3 matrix;
			switch (variant)
			{
				case 1: count = 5; longest = 1.3f; shortest = 0.5f; girth = 0.18f; lean = 40f; matrix = new Vector3(0.42f, 0.22f, 0.36f); break;
				case 2: count = 16; longest = 1.1f; shortest = 0.3f; girth = 0.075f; lean = 62f; matrix = new Vector3(0.36f, 0.18f, 0.32f); break;
				default: count = 11; longest = 1f; shortest = 0.25f; girth = 0.14f; lean = 52f; matrix = new Vector3(0.46f, 0.22f, 0.4f); break;
			}
			var parts = new List<CrystalPart>(count);
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (float)(count - 1) : 0f;
				float length = Mathf.Lerp(longest, shortest, Mathf.Pow(rank, 0.75f)) * rng.Range(0.88f, 1.08f);
				float r = Mathf.Sqrt(Mathf.Lerp(0.02f, 1f, rank) * rng.Range(0.4f, 1f)) * 0.78f;
				float az = rng.NextFloat() * Tau;
				var foot = new Vector3(Mathf.Cos(az) * r * matrix.x, 0f, Mathf.Sin(az) * r * matrix.z);
				foot.y = matrix.y * Mathf.Sqrt(Mathf.Max(0f, 1f - r * r)) * 0.8f;
				Vector3 dir = Tilted(Mathf.Lerp(4f, lean, r) + rng.Range(-6f, 7f), az + rng.Range(-0.45f, 0.45f));
				parts.Add(new CrystalPart
				{
					Foot = foot - dir * length * 0.14f,
					Dir = dir,
					Length = length,
					Radius = length * girth * rng.Range(0.85f, 1.15f),
					Spin = rng.NextFloat() * 360f,
					Seed = seed + 7919 * (i + 1),
				});
			}
			int matrixSeed = seed + 5;
			var m = new MeshBuilder(2);
			m.Append(Lump(matrix, lod == 0 ? 14 : lod == 1 ? 10 : 7, lod == 0 ? 7 : lod == 1 ? 5 : 4, 0.22f, matrixSeed));
			AddCrystals(m, parts, lod, 0.22f, 1f);
			return m;
		}

		/// <summary>Adds the crystal points to submesh 1, the smaller half dropped at the last level.</summary>
		private static void AddCrystals(MeshBuilder m, List<CrystalPart> parts, int lod, float glowFoot, float glowTip)
		{
			parts.Sort((a, b) => b.Length.CompareTo(a.Length));
			int keep = lod == 2 ? Mathf.Max(1, (parts.Count + 1) / 2) : parts.Count;
			for (int i = 0; i < keep; i++)
			{
				CrystalPart c = parts[i];
				var crng = new DeterministicRNG(c.Seed);
				float tip = c.Length * crng.Range(0.16f, 0.24f);
				CrystalPoint(m, 1, Matrix4x4.TRS(c.Foot, UpTo(c.Dir, c.Spin), Vector3.one), c.Radius, c.Length - tip, tip, 6, crng, glowFoot, glowTip);
			}
		}

		/// <summary>
		/// Giant crystal columns (the fantasy of Naica's selenite beams, alight): v0 one column of 4.6 m leaning off a heap
		/// of host rock with lesser points at its foot, v1 three columns diverging from one root, 5.2, 3.9 and 3 m.
		/// </summary>
		private static MeshBuilder GiantCrystals(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			var parts = new List<CrystalPart>();
			var lumps = new List<(Vector3 at, Vector3 radii)>();
			float az = rng.NextFloat() * Tau;
			float[] giants = variant == 0 ? new[] { 4.6f } : new[] { 5.2f, 3.9f, 3f };
			for (int g = 0; g < giants.Length; g++)
			{
				float a = az + g * Tau / giants.Length + rng.Range(-0.3f, 0.3f);
				float tilt = variant == 0 ? rng.Range(18f, 26f) : (g == 0 ? rng.Range(6f, 12f) : rng.Range(22f, 34f));
				Vector3 dir = Tilted(tilt, a);
				var foot = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (g == 0 ? 0.1f : 0.35f);
				float length = giants[g];
				parts.Add(new CrystalPart { Foot = foot - dir * 0.5f, Dir = dir, Length = length, Radius = length * rng.Range(0.085f, 0.1f), Spin = rng.NextFloat() * 360f, Seed = seed + 101 * (g + 1) });
			}
			int small = variant == 0 ? 6 : 8;
			for (int i = 0; i < small; i++)
			{
				float a = rng.NextFloat() * Tau;
				float r = rng.Range(0.45f, 1.1f);
				Vector3 dir = Tilted(rng.Range(20f, 55f), a + rng.Range(-0.4f, 0.4f));
				float length = rng.Range(0.7f, 2.1f);
				var foot = new Vector3(Mathf.Cos(a) * r, rng.Range(0.05f, 0.25f), Mathf.Sin(a) * r);
				parts.Add(new CrystalPart { Foot = foot - dir * length * 0.12f, Dir = dir, Length = length, Radius = length * rng.Range(0.11f, 0.15f), Spin = rng.NextFloat() * 360f, Seed = seed + 997 * (i + 7) });
			}
			int heaps = variant == 0 ? 3 : 4;
			for (int i = 0; i < heaps; i++)
			{
				float a = az + Tau * i / heaps + rng.Range(-0.4f, 0.4f);
				float r = i == 0 ? 0f : rng.Range(0.5f, 0.85f);
				lumps.Add((new Vector3(Mathf.Cos(a) * r, -0.05f, Mathf.Sin(a) * r), new Vector3(rng.Range(0.6f, 0.95f), rng.Range(0.3f, 0.45f), rng.Range(0.55f, 0.85f))));
			}
			var m = new MeshBuilder(2);
			for (int i = 0; i < lumps.Count; i++)
			{
				MeshBuilder lump = Lump(lumps[i].radii, lod == 0 ? 14 : lod == 1 ? 10 : 7, lod == 0 ? 7 : lod == 1 ? 5 : 4, 0.25f, seed + 31 * (i + 1));
				lump.Transform(Matrix4x4.Translate(lumps[i].at));
				m.Append(lump);
			}
			AddCrystals(m, parts, lod, 0.3f, 1f);
			return m;
		}

		// ── Fungi ─────────────────────────────────────────────────────

		private struct Mushroom
		{
			public Vector3 Foot, Lean;
			public float Height, Stalk, Cap, CapHeight, Nod;
			public int Seed;
		}

		/// <summary>
		/// A bonnet cap (Mycena's): bell-shaped, a little umbonate, its gills under it brightest, its margin flared; the
		/// profile from where the stalk enters the gills, out under them to the rim, then up over the cap to its crown.
		/// </summary>
		private static List<LP> BonnetProfile(float stalk, float cap, float height, int lod)
		{
			var p = new List<LP> { new LP(stalk * 1.1f, -0.04f * height, 0.9f, true) };
			if (lod == 0) p.Add(new LP(0.45f * cap, -0.13f * height, 1f));
			p.Add(new LP(0.85f * cap, -0.28f * height, 0.96f));
			p.Add(new LP(cap, -0.42f * height, 0.86f, true));
			if (lod < 2) p.Add(new LP(0.97f * cap, -0.15f * height, 0.72f));
			p.Add(new LP(0.86f * cap, 0.25f * height, 0.6f));
			p.Add(new LP(0.6f * cap, 0.62f * height, 0.56f));
			if (lod == 0) p.Add(new LP(0.3f * cap, 0.88f * height, 0.6f));
			p.Add(new LP(0f, height, 0.68f));
			return p;
		}

		/// <summary>A stalked mushroom: its stalk a tube curving up from below its foot, its cap turned to the stalk's end.</summary>
		private static void AddMushroom(MeshBuilder m, in Mushroom f, int lod, List<LP> cap)
		{
			int stations = lod == 0 ? 5 : lod == 1 ? 3 : 2;
			var path = new List<Vector3>(stations);
			var radius = new List<float>(stations);
			var glow = new List<float>(stations);
			Vector3 p0 = f.Foot + Vector3.down * 0.04f;
			Vector3 p2 = f.Foot + Vector3.up * f.Height + f.Lean * f.Height;
			Vector3 p1 = f.Foot + Vector3.up * (f.Height * 0.55f) + f.Lean * (f.Height * 0.15f);
			for (int i = 0; i < stations; i++)
			{
				float t = i / (float)(stations - 1);
				path.Add((1f - t) * (1f - t) * p0 + 2f * (1f - t) * t * p1 + t * t * p2);
				radius.Add(f.Stalk * Mathf.Lerp(1.15f, 0.85f, t));
				glow.Add(Mathf.Lerp(0.15f, 0.48f, t));
			}
			Tube(m, 0, path, radius, glow, lod == 0 ? 6 : lod == 1 ? 4 : 3, true, false);
			Vector3 end = (p2 - p1).normalized;
			// The cap nods a little off the stalk's end, as a bonnet's does.
			Vector3 axis = (end + new Vector3(f.Lean.x, 0f, f.Lean.z) * f.Nod).normalized;
			Lathe(m, 0, cap, lod == 0 ? 10 : lod == 1 ? 7 : 5, Matrix4x4.TRS(p2, UpTo(axis, f.Seed % 360), Vector3.one), 3f);
		}

		/// <summary>
		/// A tuft of luminous bonnets (fantasy-sized Mycena): the tallest in the middle, the rest round them leaning out. v0
		/// a tight tuft of nine (0.1–0.25 m), v1 a patch of fifteen spread 0.9 m (0.08–0.3 m), v2 a clump of six tall ones
		/// (0.28–0.5 m) with broader caps. The smaller half drop out at the last level.
		/// </summary>
		private static MeshBuilder FungusTuft(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			float tallest, shortest, spread, capShare;
			switch (variant)
			{
				case 1: count = 15; tallest = 0.3f; shortest = 0.08f; spread = 0.45f; capShare = 0.22f; break;
				case 2: count = 6; tallest = 0.5f; shortest = 0.28f; spread = 0.16f; capShare = 0.26f; break;
				default: count = 9; tallest = 0.25f; shortest = 0.1f; spread = 0.12f; capShare = 0.22f; break;
			}
			var all = new List<Mushroom>(count);
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (float)(count - 1) : 0f;
				float h = Mathf.Lerp(tallest, shortest, rank) * rng.Range(0.85f, 1.1f);
				float r = spread * Mathf.Sqrt(Mathf.Lerp(0.03f, 1f, rank) * rng.Range(0.3f, 1f));
				float az = rng.NextFloat() * Tau;
				var foot = new Vector3(Mathf.Cos(az) * r, 0f, Mathf.Sin(az) * r);
				var lean = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az)) * (0.06f + 0.5f * r / Mathf.Max(0.05f, spread) * 0.25f + rng.Range(0f, 0.06f));
				float cap = h * capShare * rng.Range(0.8f, 1.15f);
				all.Add(new Mushroom
				{
					Foot = foot, Lean = lean, Height = h, Cap = cap, CapHeight = cap * rng.Range(0.75f, 1.05f),
					Stalk = cap * rng.Range(0.11f, 0.15f), Nod = rng.Range(0.1f, 0.5f), Seed = rng.Next(),
				});
			}
			int keep = lod == 2 ? Mathf.Max(1, (count + 1) / 2) : count;
			var m = new MeshBuilder(1);
			for (int i = 0; i < keep; i++)
			{
				Mushroom f = all[i];
				AddMushroom(m, in f, lod, BonnetProfile(f.Stalk, f.Cap, f.CapHeight, lod));
			}
			return m;
		}

		/// <summary>
		/// Giant glowcaps: a stout stalk swelling to a bulb at its foot (set into the floor), a ragged skirt below the cap, a
		/// broad umbonate cap whose gills glow brightest and whose top carries glowing warts. v0 one 2.7 m tall, v1 three
		/// leaning apart (2.2, 1.6, 1 m).
		/// </summary>
		private static MeshBuilder GiantFungi(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			float[] heights = variant == 0 ? new[] { 2.7f } : new[] { 2.2f, 1.6f, 1.0f };
			var m = new MeshBuilder(1);
			float az = rng.NextFloat() * Tau;
			for (int g = 0; g < heights.Length; g++)
			{
				float h = heights[g] * rng.Range(0.95f, 1.05f);
				float cap = h * (variant == 0 ? 0.43f : 0.41f) * rng.Range(0.95f, 1.05f);
				float stalk = h * 0.065f;
				float a = az + g * Tau / heights.Length + rng.Range(-0.3f, 0.3f);
				Vector3 foot = g == 0 && variant == 0 ? Vector3.zero : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (g == 0 ? 0.25f : 0.75f + 0.25f * g);
				Vector3 axis = Tilted(variant == 0 ? rng.Range(4f, 8f) : rng.Range(7f, 15f), a);
				Quaternion turn = UpTo(axis, rng.NextFloat() * 360f);
				float capAt = h - cap * 0.38f;
				int seg = lod == 0 ? 16 : lod == 1 ? 11 : 7;
				// The stalk, from a bulb sunk in the floor up into the cap.
				var stem = new List<LP> { new LP(0f, -0.25f * stalk * 4f, 0.2f) };
				stem.Add(new LP(stalk * 1.45f, -0.12f, 0.22f));
				if (lod < 2) stem.Add(new LP(stalk * 1.55f, 0.12f * h, 0.26f));
				stem.Add(new LP(stalk * 1.05f, 0.3f * h, 0.3f));
				if (lod == 0) stem.Add(new LP(stalk * 0.95f, 0.55f * h, 0.36f));
				stem.Add(new LP(stalk * 0.9f, capAt + 0.02f * h, 0.45f));
				Lathe(m, 0, stem, seg, Matrix4x4.TRS(foot, turn, Vector3.one), 6f);
				// The skirt (an annulus ring) hanging from the stalk under the cap.
				if (lod < 2)
				{
					float ya = capAt - 0.12f * h, rs = stalk * 0.95f;
					var skirt = new List<LP>
					{
						new LP(rs * 0.98f, ya - 0.05f * h, 0.5f), new LP(rs * 1.85f, ya - 0.11f * h, 0.62f, true),
						new LP(rs * 1.95f, ya - 0.09f * h, 0.62f, true), new LP(rs * 1.02f, ya, 0.5f, true),
					};
					Lathe(m, 0, skirt, seg, Matrix4x4.TRS(foot, turn, Vector3.one), 6f, 0f, true);
				}
				// The cap: gills glowing under it, an umbo on top.
				var capProfile = new List<LP> { new LP(stalk * 0.95f, -0.04f * cap, 0.92f, true) };
				if (lod < 2) capProfile.Add(new LP(0.45f * cap, -0.12f * cap, 1f));
				capProfile.Add(new LP(0.88f * cap, -0.2f * cap, 0.96f));
				capProfile.Add(new LP(cap, -0.26f * cap, 0.85f, true));
				capProfile.Add(new LP(0.98f * cap, -0.14f * cap, 0.7f));
				if (lod == 0) capProfile.Add(new LP(0.86f * cap, 0.12f * cap, 0.55f));
				capProfile.Add(new LP(0.6f * cap, 0.3f * cap, 0.5f));
				if (lod < 2) capProfile.Add(new LP(0.25f * cap, 0.4f * cap, 0.54f));
				capProfile.Add(new LP(0f, 0.43f * cap, 0.6f));
				Matrix4x4 capPlace = Matrix4x4.TRS(foot + turn * new Vector3(0f, capAt, 0f), turn, Vector3.one);
				Lathe(m, 0, capProfile, seg + 4, capPlace, 10f);
				// Glowing warts over the cap's top.
				if (lod < 2)
				{
					int warts = lod == 0 ? 14 : 7;
					for (int w = 0; w < warts; w++)
					{
						float wa = rng.NextFloat() * Tau, r = rng.Range(0.18f, 0.85f) * cap;
						float size = cap * rng.Range(0.035f, 0.065f);
						// On the cap's top (its finest profile, so every level puts them in the same place), a little sunk.
						Vector3 local = new Vector3(Mathf.Cos(wa) * r, CapTopAt(GlowcapTop, r / cap) * cap + size * 0.25f, Mathf.Sin(wa) * r);
						Bead(m, 0, capPlace.MultiplyPoint3x4(local), new Vector3(size, size * 0.6f, size), 1f, 5, 3);
					}
				}
			}
			return m;
		}

		/// <summary>The glowcap's top from its rim in to its crown, as (radius, height) shares of the cap's radius: its finest profile's.</summary>
		private static readonly Vector2[] GlowcapTop =
		{
			new Vector2(1f, -0.26f), new Vector2(0.98f, -0.14f), new Vector2(0.86f, 0.12f), new Vector2(0.6f, 0.3f), new Vector2(0.25f, 0.4f), new Vector2(0f, 0.43f),
		};

		/// <summary>The height of a cap's top at a share of its radius, from its profile (rim first, crown last).</summary>
		private static float CapTopAt(Vector2[] top, float share)
		{
			for (int i = 0; i + 1 < top.Length; i++)
			{
				if (share <= top[i].x && share >= top[i + 1].x)
				{
					float t = Mathf.InverseLerp(top[i].x, top[i + 1].x, share);
					return Mathf.Lerp(top[i].y, top[i + 1].y, t);
				}
			}
			return top[top.Length - 1].y;
		}

		private struct Bracket
		{
			public Vector3 At;
			public float Out, Half, Thick, Droop, Tilt, Lobe;
		}

		/// <summary>
		/// Tiers of bracket fungi on a wall (Panellus stipticus, fantasy-sized): kidney-shaped shelves standing out of the wall
		/// along −z, a little convex on top, drooping at the margin, their pores underneath glowing. v0 six in a cluster
		/// 0.4 m, v1 nine in a column of tiers 0.8 m tall, v2 thirteen over a patch 1 m across. Their roots run into the wall.
		/// </summary>
		private static MeshBuilder ShelfFungi(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			Vector2 patch;
			float big;
			switch (variant)
			{
				case 1: count = 9; patch = new Vector2(0.25f, 0.8f); big = 0.2f; break;
				case 2: count = 13; patch = new Vector2(1f, 0.6f); big = 0.24f; break;
				default: count = 6; patch = new Vector2(0.4f, 0.35f); big = 0.18f; break;
			}
			var all = new List<Bracket>(count);
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (float)(count - 1) : 0f;
				float size = big * Mathf.Lerp(1f, 0.4f, rank) * rng.Range(0.85f, 1.1f);
				all.Add(new Bracket
				{
					At = new Vector3(rng.Range(-0.5f, 0.5f) * patch.x, rng.Range(-0.5f, 0.5f) * patch.y, 0f),
					Out = size,
					Half = size * rng.Range(1f, 1.4f),
					Thick = size * rng.Range(0.18f, 0.26f),
					Droop = size * rng.Range(0.1f, 0.25f),
					Tilt = rng.Range(-8f, 12f),
					Lobe = rng.NextFloat() * Tau,
				});
			}
			int keep = lod == 2 ? Mathf.Max(1, count * 2 / 3) : count;
			var m = new MeshBuilder(1);
			for (int i = 0; i < keep; i++)
			{
				AddBracket(m, all[i], lod);
			}
			return m;
		}

		/// <summary>One bracket: top and pore surfaces over a fan from its root (in the wall), a rim strip where they meet and two side strips.</summary>
		private static void AddBracket(MeshBuilder m, in Bracket b, int lod)
		{
			int fan = lod == 0 ? 9 : lod == 1 ? 6 : 4, steps = lod == 0 ? 4 : lod == 1 ? 3 : 2;
			const float limit = 80f * Mathf.Deg2Rad, root = 0.15f;
			var anchor = new Vector3(0f, 0f, 0.12f);
			Bracket br = b;
			Vector3 Edge(float a)
			{
				float lobe = 1f + 0.08f * Mathf.Sin(3f * a + br.Lobe);
				return new Vector3(br.Half * Mathf.Sin(a) * lobe, 0f, -br.Out * Mathf.Cos(a) * lobe);
			}
			Vector3 On(float a, float t, bool top)
			{
				Vector3 p = anchor + (Edge(a) - anchor) * t;
				float s = (t - root) / (1f - root);
				float y = br.Thick * (1f - s * s) * (0.7f + 0.3f * Mathf.Cos(a)) - br.Droop * s * s;
				if (!top)
				{
					y -= br.Thick * 0.85f * (1f - s) + 0.006f;
				}
				p.y = y;
				return p;
			}
			Matrix4x4 place = Matrix4x4.TRS(b.At, Quaternion.Euler(b.Tilt, 0f, 0f), Vector3.one);
			for (int surface = 0; surface < 2; surface++)
			{
				bool top = surface == 0;
				var part = new MeshBuilder(1);
				var grid = new int[fan + 1, steps + 1];
				for (int i = 0; i <= fan; i++)
				{
					float a = -limit + 2f * limit * i / fan;
					for (int j = 0; j <= steps; j++)
					{
						float t = root + (1f - root) * j / steps;
						float glow = top ? Mathf.Lerp(0.38f, 0.58f, t) : Mathf.Lerp(0.8f, 1f, t);
						grid[i, j] = part.AddVertex(On(a, t, top), Vector3.zero, new Vector2(Glow(glow), 3f * i / fan), White);
					}
				}
				Vector3 facing = top ? Vector3.up : Vector3.down;
				for (int i = 0; i < fan; i++)
				{
					for (int j = 0; j < steps; j++)
					{
						part.AddQuad(0, grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1], facing);
					}
				}
				part.RecalculateNormals(true);
				part.Transform(place);
				m.Append(part);
			}
			// The rim, the two sides and the root's back: strips joining the two surfaces, each facing away from the shelf's
			// middle (between its surfaces, halfway out).
			Vector3 middle = (On(0f, 0.55f, true) + On(0f, 0.55f, false)) * 0.5f;
			void Strip(Func<int, Vector3> upper, Func<int, Vector3> lower, int count, float glow)
			{
				var part = new MeshBuilder(1);
				for (int i = 0; i < count; i++)
				{
					part.AddVertex(upper(i), Vector3.zero, new Vector2(Glow(glow), (float)i / count), White);
					part.AddVertex(lower(i), Vector3.zero, new Vector2(Glow(glow), (float)i / count), White);
				}
				for (int i = 0; i + 1 < count; i++)
				{
					int a = 2 * i;
					Vector3 centre = (part.Positions[a] + part.Positions[a + 1] + part.Positions[a + 2] + part.Positions[a + 3]) * 0.25f;
					part.AddQuad(0, a, a + 2, a + 3, a + 1, centre - middle);
				}
				part.RecalculateNormals(false);
				part.Transform(place);
				m.Append(part);
			}
			Strip(i => On(-limit + 2f * limit * i / fan, 1f, true), i => On(-limit + 2f * limit * i / fan, 1f, false), fan + 1, 0.8f);
			Strip(j => On(-limit, root + (1f - root) * j / steps, true), j => On(-limit, root + (1f - root) * j / steps, false), steps + 1, 0.55f);
			Strip(j => On(limit, root + (1f - root) * j / steps, true), j => On(limit, root + (1f - root) * j / steps, false), steps + 1, 0.55f);
			Strip(i => On(-limit + 2f * limit * i / fan, root, true), i => On(-limit + 2f * limit * i / fan, root, false), fan + 1, 0.3f);
		}

		// ── Glow-worms ────────────────────────────────────────────────

		/// <summary>Seating: how far a glow-worm's thread and larva reach up into the roof above the origin.</summary>
		public const float GlowWormRoot = 0.3f;

		/// <summary>
		/// A glow-worm colony on a roof (Arachnocampa): a few dozen to over a hundred silk snares hanging 0.1–0.6 m (most
		/// short), each beaded with droplets of mucus, a glowing larva at the top of each, and as many again whose snares
		/// are only a few centimetres; gathered in clumps over a patch of ragged outline, 2, 3 or 4 m across. The larvae are
		/// the light (glow 1); the threads are faint, the droplets catch it. Each larva is a short vertical rod reaching
		/// <see cref="GlowWormRoot"/> up into the roof, so on any roof within that it shows as a speck where it comes
		/// through the rock; the threads start up there too.
		/// </summary>
		private static MeshBuilder GlowWorms(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			float radius = variant == 0 ? 1f : variant == 1 ? 1.5f : 2f;
			int threads = Mathf.RoundToInt(Mathf.PI * radius * radius * 10f);
			int larvae = threads + threads / 2;
			var points = new List<(Vector3 at, float length, int seed)>(larvae);
			int guard = 0;
			while (points.Count < larvae && guard++ < larvae * 40)
			{
				float x = rng.Range(-radius, radius), z = rng.Range(-radius, radius);
				float a = Mathf.Atan2(z, x);
				float edge = radius * (0.8f + 0.2f * ProceduralNoise.Fbm3(new Vector3(Mathf.Cos(a) * 1.3f, 0f, Mathf.Sin(a) * 1.3f), 2, 0.5f, seed + 3));
				float r = Mathf.Sqrt(x * x + z * z);
				if (r > edge)
				{
					continue;
				}
				float clump = 0.5f + 0.5f * ProceduralNoise.Fbm3(new Vector3(x, 0f, z) * 1.6f, 3, 0.5f, seed + 5);
				float density = Mathf.Clamp01(clump * 1.4f - 0.15f) * (1f - 0.5f * (r / edge) * (r / edge));
				if (rng.NextFloat() > density)
				{
					continue;
				}
				bool snare = points.Count < threads;
				float u = rng.NextFloat();
				float length = snare ? 0.1f + 0.5f * u * u : 0.02f + 0.04f * u;
				points.Add((new Vector3(x, 0f, z), length, rng.Next()));
			}
			var m = new MeshBuilder(1);
			for (int i = 0; i < points.Count; i++)
			{
				(Vector3 at, float length, int pseed) = points[i];
				var prng = new DeterministicRNG(pseed);
				// The larva: a speck where it comes through the roof.
				var larva = new List<Vector3> { at + Vector3.up * GlowWormRoot, at + Vector3.down * 0.012f };
				Tube(m, 0, larva, new[] { 0.0045f, 0.0045f }, new[] { 1f, 1f }, lod == 0 ? 4 : 3, false, true);
				bool longThread = length > 0.1f;
				bool draw = lod == 0 || (lod == 1 ? (i % 5 != 0) : (longThread && i % 4 == 0));
				if (!draw)
				{
					continue;
				}
				var bottom = at + new Vector3(prng.Range(-0.01f, 0.01f), -length, prng.Range(-0.01f, 0.01f));
				float thread = lod == 0 ? 0.0022f : lod == 1 ? 0.003f : 0.004f;
				Tube(m, 0, new List<Vector3> { at + Vector3.up * GlowWormRoot * 0.8f, bottom }, new[] { thread, thread }, new[] { 0.3f, 0.4f }, 3, false, false);
				if (lod == 2)
				{
					continue;
				}
				// Droplets along the thread, a little bigger toward its end where they gather.
				int beads = Mathf.Max(1, Mathf.RoundToInt(length / 0.08f) + (lod == 0 ? 1 : -1));
				if (lod == 1)
				{
					beads = Mathf.Max(1, beads / 2);
				}
				for (int k = 0; k < beads; k++)
				{
					float t = (k + 0.3f + 0.4f * prng.NextFloat()) / beads;
					float size = Mathf.Lerp(0.0035f, 0.0065f, t) * (lod == 1 ? 1.3f : 1f) * prng.Range(0.8f, 1.2f);
					Vector3 c = Vector3.Lerp(at, bottom, t);
					Bead(m, 0, c, new Vector3(size, size * 1.25f, size), 0.75f, 4, 2);
				}
			}
			return m;
		}

		// ── Ore seams ─────────────────────────────────────────────────

		/// <summary>Seating: how far a seam's back runs into the wall behind the origin.</summary>
		public const float SeamRoot = 0.15f;

		/// <summary>
		/// An ore seam in a wall along x (the back toward +z, into the rock): a band of milky quartz (submesh 0) standing a
		/// centimetre or two proud, and in it the ore (submesh 1) standing 2–5 cm proud, pinching and swelling along a
		/// wandering line, with crystalline nuggets on it; both run back <see cref="SeamRoot"/> into the wall and their
		/// edges dip a centimetre in, so the seam comes out of the rock. v0 1.5 m, v1 2.5 m, v2 4 m, v3 3 m with a branch.
		/// Textured in metres.
		/// </summary>
		private static MeshBuilder OreSeam(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			float length = variant == 0 ? 1.5f : variant == 1 ? 2.5f : variant == 2 ? 4f : 3f;
			float width = rng.Range(0.12f, 0.2f) * (variant == 2 ? 1.2f : 1f);
			var m = new MeshBuilder(2);
			Seam(m, new Vector3(-length * 0.5f, 0f, 0f), 0f, length, width, rng.Range(-0.12f, 0.12f), lod, seed + 11, rng);
			if (variant == 3)
			{
				// A branch splaying off a third of the way along.
				float a = rng.Range(25f, 35f) * (rng.NextFloat() < 0.5f ? 1f : -1f);
				Seam(m, new Vector3(-length * 0.15f, 0f, 0f), a, 1.2f, width * 0.6f, 0f, lod, seed + 23, rng);
			}
			return m;
		}

		/// <summary>One seam: the quartz band and the ore in it, lofted along a wandering line from <paramref name="start"/> at <paramref name="angle"/> degrees off +x.</summary>
		private static void Seam(MeshBuilder m, Vector3 start, float angle, float length, float width, float slope, int lod, int seed, DeterministicRNG rng)
		{
			float step = lod == 0 ? 0.08f : lod == 1 ? 0.16f : 0.35f;
			int stations = Mathf.Max(3, Mathf.CeilToInt(length / step) + 1);
			float f1 = rng.Range(1.2f, 2.2f), f2 = rng.Range(3.5f, 5.5f), p1 = rng.NextFloat() * Tau, p2 = rng.NextFloat() * Tau;
			float a1 = rng.Range(0.05f, 0.12f), a2 = rng.Range(0.01f, 0.03f);
			Quaternion turn = Quaternion.AngleAxis(angle, Vector3.forward);
			var centre = new Vector3[stations];
			var halfWidth = new float[stations];
			var proud = new float[stations];
			for (int i = 0; i < stations; i++)
			{
				float x = length * i / (stations - 1);
				float y = a1 * Mathf.Sin(x * f1 + p1) + a2 * Mathf.Sin(x * f2 + p2) + slope * x;
				centre[i] = start + turn * new Vector3(x, y, 0f);
				float ends = Mathf.SmoothStep(0f, 1f, Mathf.Min(x, length - x) / Mathf.Min(0.35f, length * 0.2f));
				float swell = 0.55f + 0.45f * (0.5f + 0.5f * ProceduralNoise.Fbm3(new Vector3(x * 1.7f, 0f, 0f), 2, 0.5f, seed));
				halfWidth[i] = 0.5f * width * swell * Mathf.Lerp(0.25f, 1f, ends);
				proud[i] = (0.02f + 0.03f * (0.5f + 0.5f * ProceduralNoise.Fbm3(new Vector3(x * 2.3f, 5f, 0f), 2, 0.5f, seed + 1))) * Mathf.Lerp(0.4f, 1f, ends);
			}
			// The quartz band: wider and lower; then the ore over it.
			m.Append(Loft(centre, halfWidth, proud, 1.7f, 0.012f, 0.35f));
			m.Append(Loft(centre, halfWidth, proud, 1f, 0f, 1f), 1);
			if (lod == 2)
			{
				return;
			}
			// Nuggets: crystalline lumps standing out of the ore.
			int nuggets = Mathf.RoundToInt(length * (lod == 0 ? 7f : 3f));
			for (int k = 0; k < nuggets; k++)
			{
				int i = Mathf.Clamp(Mathf.RoundToInt(rng.NextFloat() * (stations - 1)), 1, stations - 2);
				Vector3 along = (centre[i + 1] - centre[i - 1]).normalized;
				var across = new Vector3(-along.y, along.x, 0f);
				float size = Mathf.Max(0.008f, halfWidth[i] * rng.Range(0.2f, 0.45f));
				Vector3 at = centre[i] + across * (halfWidth[i] * rng.Range(-0.6f, 0.6f)) + Vector3.back * (proud[i] * 0.75f);
				MeshBuilder lump = Lump(new Vector3(size, size * rng.Range(0.7f, 1f), size * 0.8f), lod == 0 ? 6 : 5, 3, 0.3f, seed + 97 * (k + 1));
				lump.Transform(Matrix4x4.Translate(at));
				m.Append(lump, 1);
			}
		}

		/// <summary>
		/// A seam's body lofted through its stations: each section a low arch across the line (its middle <paramref name="proud"/>
		/// out of the wall along −z, its edges a centimetre into it) closed by a back <see cref="SeamRoot"/> into the wall; the
		/// ends capped. Normals from its faces; texture in metres.
		/// </summary>
		private static MeshBuilder Loft(Vector3[] centre, float[] halfWidth, float[] proud, float widen, float add, float raise)
		{
			int n = centre.Length;
			float[] across = { -1f, -0.6f, -0.2f, 0.2f, 0.6f, 1f };
			int ring = across.Length + 2;
			var b = new MeshBuilder(1);
			var rings = new int[n];
			var mids = new Vector3[n];
			for (int i = 0; i < n; i++)
			{
				Vector3 along = (centre[Mathf.Min(i + 1, n - 1)] - centre[Mathf.Max(i - 1, 0)]).normalized;
				var side = new Vector3(-along.y, along.x, 0f);
				float w = halfWidth[i] * widen + add;
				float h = proud[i] * raise + add * 0.5f;
				rings[i] = b.VertexCount;
				Vector3 Put(Vector3 p) { b.AddVertex(p, Vector3.zero, new Vector2(p.x, p.y), White); return p; }
				Put(centre[i] - side * w + Vector3.forward * SeamRoot);
				foreach (float s in across)
				{
					float lift = Mathf.Abs(s) > 0.99f ? -0.01f : h * Mathf.Pow(1f - s * s, 0.6f);
					Put(centre[i] + side * (s * w) + Vector3.back * lift);
				}
				Put(centre[i] + side * w + Vector3.forward * SeamRoot);
				mids[i] = centre[i] + Vector3.forward * (SeamRoot * 0.5f);
			}
			for (int i = 0; i + 1 < n; i++)
			{
				for (int k = 0; k < ring; k++)
				{
					int a = rings[i] + k, c = rings[i] + (k + 1) % ring, d = rings[i + 1] + (k + 1) % ring, e = rings[i + 1] + k;
					Vector3 facing = (b.Positions[a] + b.Positions[c] + b.Positions[d] + b.Positions[e]) * 0.25f - (mids[i] + mids[i + 1]) * 0.5f;
					b.AddQuad(0, a, c, d, e, facing);
				}
			}
			for (int end = 0; end < 2; end++)
			{
				int i = end == 0 ? 0 : n - 1;
				Vector3 outward = end == 0 ? centre[0] - centre[1] : centre[n - 1] - centre[n - 2];
				int c = b.AddVertex(mids[i], Vector3.zero, new Vector2(mids[i].x, mids[i].y), White);
				for (int k = 0; k < ring; k++)
				{
					b.AddTriangle(0, c, rings[i] + k, rings[i] + (k + 1) % ring, outward);
				}
			}
			b.RecalculateNormals(false);
			return b;
		}
	}
}
#endif
