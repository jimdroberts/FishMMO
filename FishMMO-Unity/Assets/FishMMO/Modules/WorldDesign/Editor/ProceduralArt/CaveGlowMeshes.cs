#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The cave kit's glow art as meshes (<see cref="CaveGlowKind"/>, 2026-10-13): glowing crystal clusters and giant
	/// columns, luminous fungi (Mycena tufts, giant glowcaps, Panellus brackets), glow-worm snares and ore seams; since
	/// 2026-10-20 also mushroom trees to 15 m and glowcaps of 3–5 m, puffballs and morels, tall bracket tiers, glowroot
	/// curtains, glow-vines and the tree roots that hang through a thin roof. Pure
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
	public static partial class CaveGlowMeshes
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
				case CaveGlowKind.TowerFungus: m = TowerFungi(variant, lod, s); break;
				case CaveGlowKind.MidFungus: m = MidFungi(variant, lod, s); break;
				case CaveGlowKind.Puffballs: m = Puffballs(variant, lod, s); break;
				case CaveGlowKind.ShelfTiers: m = ShelfTiers(variant, lod, s); break;
				case CaveGlowKind.GlowRoots: m = GlowRoots(variant, lod, s); break;
				case CaveGlowKind.HangingRoots: m = HangingRoots(variant, lod, s); break;
				case CaveGlowKind.GlowVine: m = GlowVine(variant, lod, s); break;
				case CaveGlowKind.MagmaCrack: m = MagmaCracks(variant, lod, s); break;
				case CaveGlowKind.MagmaSeep: m = MagmaSeeps(variant, lod, s); break;
				case CaveGlowKind.Lavacicle: m = Lavacicles(variant, lod, s); break;
				case CaveGlowKind.LavaCrust: m = LavaCrusts(variant, lod, s); break;
				// 2026-10-20 (caves livened, hanging): CaveGlowMeshes.Hanging.cs.
				case CaveGlowKind.GlowSilk: m = GlowSilk(variant, lod, s); break;
				case CaveGlowKind.TapRoots: m = TapRoots(variant, lod, s); break;
				case CaveGlowKind.GuanoHeap: m = GuanoHeap(variant, lod, s); break;
				case CaveGlowKind.IceFall: m = IceFalls(variant, lod, s); break;
				case CaveGlowKind.IcicleCurtain: m = IcicleCurtains(variant, lod, s); break;
				case CaveGlowKind.FrozenPool: m = FrozenPools(variant, lod, s); break;
				case CaveGlowKind.Hoarfrost: m = Hoarfrosts(variant, lod, s); break;
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

		// ── Towering fungi (2026-10-20) ───────────────────────────────

		/// <summary>The shape of a mushroom tree's cap.</summary>
		private enum CapStyle
		{
			/// <summary>A broad convex dome with a low umbo (a fly agaric's, grown old).</summary>
			Dome,
			/// <summary>A wide, nearly flat parasol with a little umbo (Macrolepiota's).</summary>
			Parasol,
			/// <summary>A tall conical bell (a bonnet's, grown to a tree).</summary>
			Bell,
		}

		/// <summary>One mushroom tree of a piece: where it stands and how it is made.</summary>
		private struct FungusTree
		{
			public Vector3 Foot;
			/// <summary>From its foot to its cap's crown, metres.</summary>
			public float Height;
			/// <summary>How far its stalk leans (degrees) and toward which heading (radians).</summary>
			public float Lean, Azimuth;
			/// <summary>The cap's radius and the stalk's, as shares of its height.</summary>
			public float CapShare, StalkShare;
			public CapStyle Style;
			/// <summary>Bracket tiers up its stalk.</summary>
			public int Shelves;
			/// <summary>Glowing warts on its cap's top (half of them at the middle level).</summary>
			public int Warts;
			/// <summary>A drooping veil of glowing strands under its cap; buttress roots at its foot; mycelium threads out over the floor.</summary>
			public bool Veil, Roots, Mycelium;
		}

		private static FungusTree Tree(Vector3 foot, float height, float lean, float azimuth, float cap, float stalk, CapStyle style,
			int shelves, int warts, bool veil, bool roots, bool mycelium) => new FungusTree
		{
			Foot = foot, Height = height, Lean = lean, Azimuth = azimuth, CapShare = cap, StalkShare = stalk, Style = style,
			Shelves = shelves, Warts = warts, Veil = veil, Roots = roots, Mycelium = mycelium,
		};

		/// <summary>A point on the floor <paramref name="distance"/> metres out along heading <paramref name="azimuth"/> (radians).</summary>
		private static Vector3 Out(float azimuth, float distance) => new Vector3(Mathf.Cos(azimuth), 0f, Mathf.Sin(azimuth)) * distance;

		/// <summary>
		/// A cap's underside from the stalk out to the rim, and its top from the rim in to its crown, as (radius, height) shares
		/// of its radius (the cap's origin where the stalk enters it). The dome's top is the glowcap's.
		/// </summary>
		private static void CapShape(CapStyle style, out Vector2[] under, out Vector2[] top)
		{
			switch (style)
			{
				case CapStyle.Parasol:
					under = new[] { new Vector2(0f, -0.03f), new Vector2(0.5f, -0.06f), new Vector2(0.9f, -0.07f), new Vector2(1f, -0.05f) };
					top = new[] { new Vector2(1f, -0.05f), new Vector2(0.97f, 0.02f), new Vector2(0.75f, 0.08f), new Vector2(0.4f, 0.14f), new Vector2(0.15f, 0.2f), new Vector2(0f, 0.22f) };
					break;
				case CapStyle.Bell:
					under = new[] { new Vector2(0f, -0.05f), new Vector2(0.45f, -0.18f), new Vector2(0.85f, -0.32f), new Vector2(1f, -0.42f) };
					top = new[] { new Vector2(1f, -0.42f), new Vector2(0.96f, -0.2f), new Vector2(0.85f, 0.15f), new Vector2(0.62f, 0.45f), new Vector2(0.32f, 0.68f), new Vector2(0f, 0.8f) };
					break;
				default:
					under = new[] { new Vector2(0f, -0.04f), new Vector2(0.45f, -0.12f), new Vector2(0.88f, -0.2f), new Vector2(1f, -0.26f) };
					top = GlowcapTop;
					break;
			}
		}

		/// <summary>A profile's height at a share of the radius, its points in either order of radius.</summary>
		private static float ProfileAt(Vector2[] points, float share)
		{
			for (int i = 0; i + 1 < points.Length; i++)
			{
				float a = points[i].x, b = points[i + 1].x;
				if (share >= Mathf.Min(a, b) && share <= Mathf.Max(a, b))
				{
					return Mathf.Lerp(points[i].y, points[i + 1].y, Mathf.Abs(b - a) < 1e-6f ? 0f : (share - a) / (b - a));
				}
			}
			return Mathf.Abs(share - points[0].x) < Mathf.Abs(share - points[points.Length - 1].x) ? points[0].y : points[points.Length - 1].y;
		}

		private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) => (1f - t) * (1f - t) * a + 2f * (1f - t) * t * b + t * t * c;

		private static Vector3 BezierTangent(Vector3 a, Vector3 b, Vector3 c, float t) => 2f * (1f - t) * (b - a) + 2f * t * (c - b);

		/// <summary>A mushroom tree's stalk radius a share <paramref name="t"/> of the way up it: swollen at the foot, a little bellied, slimmer under the cap.</summary>
		private static float StalkRadius(float stalk, float t) => stalk * (1.3f - 0.45f * t + 0.1f * Mathf.Sin(Mathf.PI * t));

		/// <summary>
		/// One mushroom tree (fantasy, after the giant fungi of the deep in legend and the Prototaxites of the Devonian): a stout
		/// fibrous stalk curving up from a flared, buttress-rooted foot set into the floor (its mycelium threading out over the
		/// rock round it), a ring hanging under its cap, tiers of bracket fungi up its stalk, a broad cap whose radial gills
		/// glow brightest and whose top carries glowing warts, and under it a drooping veil of glowing strands beaded at their
		/// tips. Every draw is made before any depends on the level, so the levels are one form.
		/// </summary>
		private static void AddFungusTree(MeshBuilder m, in FungusTree f, int lod, DeterministicRNG rng)
		{
			var roots = new Vector3[8];
			for (int i = 0; i < roots.Length; i++)
			{
				roots[i] = new Vector3(rng.Range(-0.25f, 0.25f), rng.Range(0.8f, 1.25f), rng.Range(0.7f, 1.2f));
			}
			var threads = new Vector3[10];
			for (int i = 0; i < threads.Length; i++)
			{
				threads[i] = new Vector3(rng.NextFloat() * Tau, rng.Range(0.12f, 0.3f), rng.NextFloat() * Tau);
			}
			var shelves = new Vector4[4];
			for (int i = 0; i < shelves.Length; i++)
			{
				shelves[i] = new Vector4(rng.Range(0.22f, 0.7f), rng.NextFloat() * Tau, rng.Range(0.75f, 1.15f), rng.Range(-6f, 10f));
			}
			var warts = new Vector3[24];
			for (int i = 0; i < warts.Length; i++)
			{
				warts[i] = new Vector3(rng.NextFloat() * Tau, rng.Range(0.15f, 0.86f), rng.Range(0.03f, 0.06f));
			}
			var veil = new Vector3[20];
			for (int i = 0; i < veil.Length; i++)
			{
				veil[i] = new Vector3(rng.NextFloat() * Tau, rng.Range(0.3f, 0.62f), rng.Range(0.08f, 0.22f));
			}
			float spin = rng.NextFloat() * 360f, gillPhase = rng.NextFloat() * Tau;

			float h = f.Height, c = h * f.CapShare, s = h * f.StalkShare;
			CapShape(f.Style, out Vector2[] under, out Vector2[] top);
			float length = h - top[top.Length - 1].y * c;
			Vector3 lean = Tilted(f.Lean, f.Azimuth);
			var flat = new Vector3(lean.x, 0f, lean.z);
			Vector3 p0 = f.Foot + Vector3.down * 0.3f, p1 = f.Foot + Vector3.up * (0.55f * length) + flat * (0.12f * length), p2 = f.Foot + lean * length;
			int sides = lod == 0 ? 14 : lod == 1 ? 9 : 6;

			// The stalk: fibrous (its mottle streaks along it), dim at the foot, brightening toward the cap.
			int stations = lod == 0 ? 9 : lod == 1 ? 5 : 3;
			var path = new List<Vector3>(stations);
			var radius = new List<float>(stations);
			var glow = new List<float>(stations);
			for (int i = 0; i < stations; i++)
			{
				float t = i / (float)(stations - 1);
				path.Add(Bezier(p0, p1, p2, t));
				radius.Add(StalkRadius(s, t));
				glow.Add(Mathf.Lerp(0.12f, 0.42f, t));
			}
			Tube(m, 0, path, radius, glow, sides, true, false);
			float stalkTop = radius[radius.Count - 1];

			// The flared foot, set into the floor.
			var flare = new List<LP> { new LP(s * 2.4f, -0.45f, 0.1f), new LP(s * 2.1f, -0.02f, 0.12f) };
			if (lod < 2) flare.Add(new LP(s * 1.65f, 0.035f * h, 0.14f));
			flare.Add(new LP(s * 1.22f, 0.09f * h, 0.17f));
			Lathe(m, 0, flare, sides, Matrix4x4.TRS(f.Foot, Quaternion.identity, Vector3.one), 6f);

			// Buttress roots from the foot down into the floor, as a strangler fig's; the mycelium threading out over the rock.
			if (f.Roots && lod < 2)
			{
				int n = lod == 0 ? 7 : 5, rs = lod == 0 ? 4 : 3;
				for (int i = 0; i < n; i++)
				{
					float az = f.Azimuth + Tau * i / n + roots[i].x;
					var dir = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
					float reach = (s * 2.2f + 0.12f * h) * roots[i].y;
					Vector3 a = f.Foot + Vector3.up * (0.07f * h * roots[i].z) + dir * (s * 0.9f);
					Vector3 b = f.Foot + Vector3.up * (0.015f * h) + dir * (s * 1.9f + 0.04f * h);
					Vector3 e = f.Foot + Vector3.down * 0.3f + dir * reach;
					var rp = new List<Vector3>(rs);
					var rr = new List<float>(rs);
					var rg = new List<float>(rs);
					for (int k = 0; k < rs; k++)
					{
						float t = k / (float)(rs - 1);
						rp.Add(Bezier(a, b, e, t));
						rr.Add(s * Mathf.Lerp(0.42f, 0.1f, t));
						rg.Add(Mathf.Lerp(0.16f, 0.26f, t));
					}
					Tube(m, 0, rp, rr, rg, lod == 0 ? 6 : 4, true, true);
				}
			}
			if (f.Mycelium && lod == 0)
			{
				foreach (Vector3 th in threads)
				{
					var dir = new Vector3(Mathf.Cos(th.x), 0f, Mathf.Sin(th.x));
					var side = new Vector3(-dir.z, 0f, dir.x);
					float run = th.y * h;
					var tp = new List<Vector3>(5);
					var tr = new List<float>(5);
					var tg = new List<float>(5);
					for (int k = 0; k < 5; k++)
					{
						float t = k / 4f;
						tp.Add(f.Foot + dir * (s * 2.1f + run * t) + side * (0.12f * run * Mathf.Sin(th.z + 3f * t)) + Vector3.down * (0.012f + 0.03f * t));
						tr.Add(h * Mathf.Lerp(0.005f, 0.002f, t));
						tg.Add(Mathf.Lerp(0.8f, 0.6f, t));
					}
					Tube(m, 0, tp, tr, tg, 3, false, true);
				}
			}

			// The ring (annulus) hanging from the stalk under the cap.
			if (lod < 2)
			{
				const float at = 0.84f;
				Vector3 q = Bezier(p0, p1, p2, at), axis = BezierTangent(p0, p1, p2, at);
				float r = StalkRadius(s, at);
				var skirt = new List<LP>
				{
					new LP(r * 0.98f, -0.045f * h, 0.5f), new LP(r * 1.9f, -0.09f * h, 0.64f, true),
					new LP(r * 2f, -0.075f * h, 0.64f, true), new LP(r * 1.02f, 0f, 0.5f, true),
				};
				Lathe(m, 0, skirt, sides, Matrix4x4.TRS(q, UpTo(axis, 0f), Vector3.one), 6f, 0f, true);
			}

			// The cap, turned to the stalk's end: gills glowing under it, the rim crisp, the top dimmer to its crown.
			Matrix4x4 capPlace = Matrix4x4.TRS(p2, UpTo(BezierTangent(p0, p1, p2, 1f), spin), Vector3.one);
			var cap = new List<LP> { new LP(stalkTop * 0.95f, under[0].y * c, 0.92f, true) };
			if (lod < 2) cap.Add(new LP(under[1].x * c, under[1].y * c, 1f));
			cap.Add(new LP(under[2].x * c, under[2].y * c, 0.96f));
			cap.Add(new LP(c, under[3].y * c, 0.85f, true));
			cap.Add(new LP(top[1].x * c, top[1].y * c, 0.7f));
			if (lod == 0) cap.Add(new LP(top[2].x * c, top[2].y * c, 0.55f));
			cap.Add(new LP(top[3].x * c, top[3].y * c, 0.5f));
			if (lod < 2) cap.Add(new LP(top[4].x * c, top[4].y * c, 0.54f));
			cap.Add(new LP(0f, top[5].y * c, 0.6f));
			Lathe(m, 0, cap, lod == 0 ? 24 : lod == 1 ? 14 : 8, capPlace, 10f);

			if (lod == 2)
			{
				return;
			}

			// The gills: thin radial plates under the cap, brightest at their edges (two-faced, so seen from either side).
			int gills = (lod == 0 ? 36 : 18) * Mathf.Clamp(Mathf.RoundToInt(h / 4f), 1, 3) / 3;
			gills = Mathf.Max(lod == 0 ? 12 : 8, gills);
			int segs = lod == 0 ? 3 : 2;
			float depth = (f.Style == CapStyle.Bell ? 0.06f : 0.075f) * c;
			var plates = new MeshBuilder(1);
			for (int k = 0; k < gills; k++)
			{
				float az = gillPhase + Tau * k / gills;
				var d = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
				var across = new Vector3(-d.z, 0f, d.x);
				float r0 = stalkTop * 1.05f, r1 = 0.92f * c;
				for (int face = -1; face <= 1; face += 2)
				{
					Vector3 n = across * face;
					int first = plates.VertexCount;
					for (int j = 0; j <= segs; j++)
					{
						float u = j / (float)segs, r = Mathf.Lerp(r0, r1, u);
						Vector3 upper = d * r + Vector3.up * (ProfileAt(under, r / c) * c + 0.01f * c);
						Vector3 lower = upper + Vector3.down * (depth * (0.3f + 0.7f * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.1f, 0.9f, u))) + 0.01f * c);
						plates.AddVertex(upper, n, new Vector2(Glow(0.9f), 0.37f * k + 0.2f * u), White);
						plates.AddVertex(lower, n, new Vector2(Glow(1f), 0.37f * k + 0.2f * u), White);
					}
					for (int j = 0; j < segs; j++)
					{
						int a = first + 2 * j;
						plates.AddQuad(0, a, a + 2, a + 3, a + 1, n);
					}
				}
			}
			plates.Transform(capPlace);
			m.Append(plates);

			// Glowing warts over the cap's top (on its finest profile, so both levels put them in the same place), a little sunk.
			int wartCount = Mathf.Min(warts.Length, lod == 0 ? f.Warts : f.Warts / 2);
			for (int w = 0; w < wartCount; w++)
			{
				float r = warts[w].y * c, size = warts[w].z * c;
				var local = new Vector3(Mathf.Cos(warts[w].x) * r, ProfileAt(top, warts[w].y) * c + size * 0.25f, Mathf.Sin(warts[w].x) * r);
				Bead(m, 0, capPlace.MultiplyPoint3x4(local), new Vector3(size, size * 0.6f, size), 1f, lod == 0 ? 5 : 4, 3);
			}

			// The veil: strands drooping from under the cap, each beaded with a glowing drop at its tip.
			if (f.Veil)
			{
				int strands = lod == 0 ? 18 : 9;
				for (int k = 0; k < strands; k++)
				{
					Vector3 v = veil[k];
					var from = new Vector3(Mathf.Cos(v.x) * v.y * c, ProfileAt(under, v.y) * c + 0.01f * c, Mathf.Sin(v.x) * v.y * c);
					Vector3 start = capPlace.MultiplyPoint3x4(from);
					var outward = new Vector3(Mathf.Cos(v.x), 0f, Mathf.Sin(v.x));
					float hang = v.z * h;
					int vs = lod == 0 ? 4 : 3;
					var vp = new List<Vector3>(vs);
					var vr = new List<float>(vs);
					var vg = new List<float>(vs);
					for (int j = 0; j < vs; j++)
					{
						float t = j / (float)(vs - 1);
						vp.Add(start + Vector3.down * (hang * t) + outward * (0.05f * hang * t * t));
						vr.Add(h * Mathf.Lerp(0.007f, 0.003f, t));
						vg.Add(Mathf.Lerp(0.62f, 0.95f, t));
					}
					Tube(m, 0, vp, vr, vg, lod == 0 ? 4 : 3, false, true);
					if (lod == 0)
					{
						float drop = 0.008f * h;
						Bead(m, 0, vp[vs - 1] + Vector3.down * drop, new Vector3(drop, drop * 1.5f, drop), 1f, 4, 3);
					}
				}
			}

			// Tiers of bracket fungi up the stalk, their roots in it.
			for (int i = 0; i < Mathf.Min(f.Shelves, shelves.Length); i++)
			{
				Vector4 sh = shelves[i];
				Vector3 q = Bezier(p0, p1, p2, sh.x);
				var outward = new Vector3(Mathf.Cos(sh.y), 0f, Mathf.Sin(sh.y));
				float size = 0.075f * h * sh.z;
				var bracket = new Bracket { At = Vector3.zero, Out = size, Half = size * 1.25f, Thick = size * 0.22f, Droop = size * 0.16f, Tilt = sh.w, Lobe = sh.y };
				var part = new MeshBuilder(1);
				AddBracket(part, in bracket, lod);
				part.Transform(Matrix4x4.TRS(q + outward * (StalkRadius(s, sh.x) * 0.8f), Quaternion.LookRotation(-outward, Vector3.up), Vector3.one));
				m.Append(part);
			}
		}

		/// <summary>
		/// Towering glowcaps, mushroom trees of the deep (2026-10-20: "Largest mushrooms should get up to 15m tall"): v0 one
		/// 12 m dome on a stalk tiered with brackets, its veil hanging; v1 a stand of three (11, 7.5 and 5 m), the lesser
		/// leaning out from under the greatest; v2 a 10 m parasol leaning 11°, four tiers of brackets up it; v3 a 14 m slender
		/// bell, veiled, with a 4.5 m one at its foot. The dressing scales a piece so its tallest stands 5–15 m
		/// (PointOfInterestCaveDressing CaveFungi).
		/// </summary>
		private static MeshBuilder TowerFungi(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			float az = rng.NextFloat() * Tau;
			FungusTree[] trees;
			switch (variant)
			{
				case 1:
					trees = new[]
					{
						Tree(Vector3.zero, 11f, 5f, az, 0.4f, 0.05f, CapStyle.Dome, 2, 22, true, true, true),
						Tree(Out(az + 2.2f, 3.4f), 7.5f, 9f, az + 2.2f, 0.4f, 0.05f, CapStyle.Dome, 1, 12, false, true, false),
						Tree(Out(az - 2f, 2.9f), 5f, 12f, az - 2f, 0.44f, 0.055f, CapStyle.Parasol, 0, 8, false, true, false),
					};
					break;
				case 2:
					trees = new[] { Tree(Vector3.zero, 10f, 11f, az, 0.46f, 0.048f, CapStyle.Parasol, 4, 14, false, true, true) };
					break;
				case 3:
					trees = new[]
					{
						Tree(Vector3.zero, 14f, 3f, az, 0.3f, 0.04f, CapStyle.Bell, 2, 16, true, true, false),
						Tree(Out(az + 2.6f, 2.6f), 4.5f, 10f, az + 2.6f, 0.32f, 0.05f, CapStyle.Bell, 0, 8, false, true, false),
					};
					break;
				default:
					trees = new[] { Tree(Vector3.zero, 12f, 4f, az, 0.42f, 0.05f, CapStyle.Dome, 3, 22, true, true, true) };
					break;
			}
			var m = new MeshBuilder(1);
			foreach (FungusTree t in trees)
			{
				AddFungusTree(m, in t, lod, rng);
			}
			return m;
		}

		/// <summary>
		/// Glowcaps of the middle size, 3–5 m (2026-10-20), as the mushroom trees are made: v0 one 4.4 m dome with a tier of
		/// brackets, v1 a pair of parasols (4.2 and 3 m), v2 a clump of three bells (4, 3.3 and 2.6 m).
		/// </summary>
		private static MeshBuilder MidFungi(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			float az = rng.NextFloat() * Tau;
			FungusTree[] trees;
			switch (variant)
			{
				case 1:
					trees = new[]
					{
						Tree(Vector3.zero, 4.2f, 6f, az, 0.46f, 0.06f, CapStyle.Parasol, 0, 10, false, true, false),
						Tree(Out(az + 2.4f, 1.7f), 3f, 12f, az + 2.4f, 0.44f, 0.065f, CapStyle.Parasol, 0, 8, false, false, false),
					};
					break;
				case 2:
					trees = new[]
					{
						Tree(Vector3.zero, 4f, 5f, az, 0.32f, 0.055f, CapStyle.Bell, 1, 8, false, false, false),
						Tree(Out(az + 2.1f, 1.25f), 3.3f, 11f, az + 2.1f, 0.32f, 0.06f, CapStyle.Bell, 0, 6, false, false, false),
						Tree(Out(az - 2.2f, 1.1f), 2.6f, 14f, az - 2.2f, 0.34f, 0.065f, CapStyle.Bell, 0, 5, false, false, false),
					};
					break;
				default:
					trees = new[] { Tree(Vector3.zero, 4.4f, 5f, az, 0.42f, 0.06f, CapStyle.Dome, 1, 14, true, true, false) };
					break;
			}
			var m = new MeshBuilder(1);
			foreach (FungusTree t in trees)
			{
				AddFungusTree(m, in t, lod, rng);
			}
			return m;
		}

		/// <summary>
		/// Puffballs and morels of the cave floor (2026-10-20), fantasy-sized: v0 a cluster of seven puffballs (Calvatia), 0.25–0.9
		/// m across, to 0.6 m tall, glowing warts on their tops; v1 six morels, 0.3–0.8 m, their honeycombed heads pitted with glowing cells; v2 a
		/// giant puffball burst open at its crown, its spores drifting glowing over it, three small ones at its foot.
		/// </summary>
		private static MeshBuilder Puffballs(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			var m = new MeshBuilder(1);
			int seg = lod == 0 ? 12 : lod == 1 ? 8 : 6;
			if (variant == 1)
			{
				for (int i = 0; i < 6; i++)
				{
					float rank = i / 5f, h = Mathf.Lerp(0.8f, 0.3f, rank) * rng.Range(0.9f, 1.1f);
					float az = rng.NextFloat() * Tau, at = i == 0 ? 0f : rng.Range(0.15f, 0.45f);
					float lean = rng.Range(3f, 12f), spin = rng.NextFloat() * 360f;
					int pitSeed = rng.Next();
					if (lod == 2 && i >= 4)
					{
						continue;
					}
					Quaternion turn = UpTo(Tilted(lean, az), spin);
					var place = Matrix4x4.TRS(Out(az, at), turn, Vector3.one);
					float stem = 0.12f * h, head = 0.2f * h;
					// The hollow white stem, then the conical head on it.
					var stalk = new List<LP> { new LP(stem * 1.2f, -0.05f, 0.25f), new LP(stem, 0.4f * h, 0.32f), new LP(stem * 0.9f, 0.45f * h, 0.35f) };
					Lathe(m, 0, stalk, seg, place, 3f);
					var cone = new List<LP> { new LP(stem * 0.85f, 0.4f * h, 0.45f, true), new LP(head, 0.44f * h, 0.5f, true) };
					if (lod < 2) cone.Add(new LP(head * 0.92f, 0.7f * h, 0.52f));
					cone.Add(new LP(head * 0.55f, 0.9f * h, 0.55f));
					cone.Add(new LP(0f, h, 0.6f));
					Lathe(m, 0, cone, seg, place, 4f);
					if (lod == 2)
					{
						continue;
					}
					// The pits, glowing, in rows round the head.
					var prng = new DeterministicRNG(pitSeed);
					int rows = lod == 0 ? 4 : 2, round = lod == 0 ? 7 : 5;
					for (int row = 0; row < rows; row++)
					{
						float t = (row + 0.5f) / rows, y = Mathf.Lerp(0.5f, 0.86f, t) * h;
						float r = Mathf.Lerp(head * 0.95f, head * 0.62f, t);
						for (int k = 0; k < round; k++)
						{
							float a = Tau * (k + 0.5f * (row & 1) + prng.Range(-0.15f, 0.15f)) / round;
							float pit = head * 0.16f;
							Bead(m, 0, place.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * r * 0.97f, y, Mathf.Sin(a) * r * 0.97f)), new Vector3(pit, pit * 1.3f, pit), 1f, 4, 2);
						}
					}
				}
				return m;
			}
			int count = variant == 2 ? 4 : 7;
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (count - 1f) : 0f;
				float r = (variant == 2 && i == 0 ? 0.62f : Mathf.Lerp(0.42f, 0.13f, rank)) * rng.Range(0.88f, 1.1f);
				if (variant == 2 && i > 0)
				{
					r *= 0.45f;
				}
				float az = rng.NextFloat() * Tau, at = i == 0 ? 0f : rng.Range(0.4f, 0.9f) + r;
				float squash = rng.Range(0.75f, 0.95f);
				int wartSeed = rng.Next();
				if (lod == 2 && i >= count - 2)
				{
					continue;
				}
				Vector3 foot = Out(az, at);
				bool burst = variant == 2 && i == 0;
				var ball = new List<LP> { new LP(0f, -0.08f * r, 0.2f), new LP(0.5f * r, -0.02f * r, 0.24f) };
				if (lod < 2) ball.Add(new LP(0.86f * r, 0.3f * r * squash, 0.38f));
				ball.Add(new LP(r, 0.75f * r * squash, 0.5f));
				if (lod < 2) ball.Add(new LP(0.88f * r, 1.2f * r * squash, 0.6f));
				if (burst)
				{
					// Torn open at the crown: a ragged lip turning in to the spore mass, glowing inside.
					ball.Add(new LP(0.62f * r, 1.42f * r * squash, 0.7f, true));
					ball.Add(new LP(0.5f * r, 1.32f * r * squash, 0.95f, true));
					ball.Add(new LP(0f, 1.2f * r * squash, 1f));
				}
				else
				{
					ball.Add(new LP(0.5f * r, 1.5f * r * squash, 0.68f));
					ball.Add(new LP(0f, 1.6f * r * squash, 0.72f));
				}
				Lathe(m, 0, ball, seg, Matrix4x4.TRS(foot, Quaternion.identity, Vector3.one), 3f);
				if (lod == 2 || burst)
				{
					continue;
				}
				var prng = new DeterministicRNG(wartSeed);
				int warts = lod == 0 ? 9 : 4;
				for (int w = 0; w < warts; w++)
				{
					float a = prng.NextFloat() * Tau, lat = prng.Range(0.25f, 1f);
					float size = r * prng.Range(0.05f, 0.09f);
					float rr = Mathf.Lerp(r, 0.35f * r, lat), y = Mathf.Lerp(0.8f, 1.52f, lat) * r * squash;
					Bead(m, 0, foot + new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr), new Vector3(size, size * 0.6f, size), 1f, 4, 2);
				}
			}
			if (variant == 2 && lod < 2)
			{
				// The spores drifting over the burst one: specks of light, thinning upward.
				int motes = lod == 0 ? 26 : 12;
				for (int k = 0; k < motes; k++)
				{
					float t = rng.NextFloat(), a = rng.NextFloat() * Tau, rr = rng.Range(0f, 0.35f + 0.6f * t);
					float size = rng.Range(0.012f, 0.028f) * (1f - 0.5f * t);
					Bead(m, 0, new Vector3(Mathf.Cos(a) * rr, 1.05f + 1.2f * t, Mathf.Sin(a) * rr), Vector3.one * size, 1f, 4, 2);
				}
			}
			return m;
		}

		/// <summary>
		/// Tall tiers of great bracket fungi on a wall (2026-10-20; Ganoderma and Laetiporus grown fantasy-large): overlapping
		/// shelves standing out along −z, their pores glowing underneath, their roots in the wall. v0 a column of ten 1.8 m tall,
		/// v1 a staircase of twelve climbing 2.8 m, v2 a broad fan of fourteen over 2.4 by 1.6 m.
		/// </summary>
		private static MeshBuilder ShelfTiers(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			Vector2 patch;
			float big;
			switch (variant)
			{
				case 1: count = 12; patch = new Vector2(1.2f, 2.8f); big = 0.5f; break;
				case 2: count = 14; patch = new Vector2(2.4f, 1.6f); big = 0.55f; break;
				default: count = 10; patch = new Vector2(0.7f, 1.8f); big = 0.45f; break;
			}
			var all = new List<Bracket>(count);
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (float)(count - 1) : 0f;
				float size = big * Mathf.Lerp(1f, 0.45f, rank) * rng.Range(0.85f, 1.1f);
				float y = rng.Range(-0.5f, 0.5f), x = rng.Range(-0.5f, 0.5f);
				if (variant == 1)
				{
					// The staircase: up and across together, as the tiers climb a seep.
					x = Mathf.Lerp(-0.5f, 0.5f, (y + 0.5f)) + rng.Range(-0.15f, 0.15f);
				}
				all.Add(new Bracket
				{
					At = new Vector3(x * patch.x, (y + 0.5f) * patch.y, 0f),
					Out = size,
					Half = size * rng.Range(1.1f, 1.5f),
					Thick = size * rng.Range(0.16f, 0.22f),
					Droop = size * rng.Range(0.08f, 0.18f),
					Tilt = rng.Range(-6f, 10f),
					Lobe = rng.NextFloat() * Tau,
				});
			}
			int keep = lod == 2 ? Mathf.Max(1, count / 2) : lod == 1 ? count * 3 / 4 : count;
			var m = new MeshBuilder(1);
			for (int i = 0; i < keep; i++)
			{
				AddBracket(m, all[i], lod);
			}
			return m;
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

		// ── Roots and glow-vines (2026-10-20) ─────────────────────────

		/// <summary>Seating: how far a hanging root's top reaches up into the roof above the origin.</summary>
		public const float RootSeat = 0.22f;

		/// <summary>One hanging strand: where its top is (on the roof), how long it hangs, how thick it starts, its own seed.</summary>
		private struct Strand
		{
			public Vector3 Top;
			public float Length, Radius;
			public int Seed;
		}

		/// <summary>
		/// Strands hanging from a roof patch <paramref name="size"/> across (x, z), longest first: thick where they leave the rock
		/// (reaching <see cref="RootSeat"/> up into it), tapering to their tips.
		/// </summary>
		private static List<Strand> Strands(DeterministicRNG rng, int count, Vector2 size, float shortest, float longest, float thick, float thin)
		{
			var all = new List<Strand>(count);
			for (int i = 0; i < count; i++)
			{
				float u = rng.NextFloat();
				all.Add(new Strand
				{
					Top = new Vector3(rng.Range(-0.5f, 0.5f) * size.x, 0f, rng.Range(-0.5f, 0.5f) * size.y),
					Length = Mathf.Lerp(shortest, longest, u * u),
					Radius = rng.Range(thin, thick),
					Seed = rng.Next(),
				});
			}
			all.Sort((a, b) => b.Length.CompareTo(a.Length));
			return all;
		}

		/// <summary>
		/// A hanging strand's path from up in the roof down, wandering as roots do (a random walk biased down, kinking more the
		/// thinner it is), <paramref name="stations"/> points, its radius tapering to <paramref name="tip"/> of its start.
		/// </summary>
		private static void HangingPath(in Strand s, int stations, float wander, float tip, float glowTop, float glowTip,
			List<Vector3> path, List<float> radius, List<float> glow)
		{
			var prng = new DeterministicRNG(s.Seed);
			float phase = prng.NextFloat() * Tau, phase2 = prng.NextFloat() * Tau;
			Vector3 drift = new Vector3(prng.Range(-1f, 1f), 0f, prng.Range(-1f, 1f)) * wander;
			path.Clear();
			radius.Clear();
			glow.Clear();
			for (int i = 0; i < stations; i++)
			{
				float t = i / (float)(stations - 1);
				float d = s.Length * t;
				var sway = new Vector3(Mathf.Sin(phase + 4.1f * t), 0f, Mathf.Sin(phase2 + 3.3f * t)) * (wander * 0.35f * t);
				path.Add(s.Top + Vector3.up * (RootSeat * (1f - t) * (i == 0 ? 1f : 0.2f)) + Vector3.down * d + drift * (t * t) + sway);
				radius.Add(s.Radius * Mathf.Lerp(1f, tip, t));
				glow.Add(Mathf.Lerp(glowTop, glowTip, t * t));
			}
		}

		/// <summary>
		/// A glowroot curtain (fantasy, 2026-10-20: the deep dark's bioluminescent roots): strands hanging from a roof patch,
		/// faint where they leave the rock and brightening down them, beaded with glowing nodules and ending in a bright
		/// bulb; a few thick runners along the roof hold them. v0 a curtain 2 m across to 2.2 m long, v1 a deep one 3 m across
		/// to 3.5 m, v2 a fringe along a 2.6 m line to 1.4 m. Fewer strands at each level, the longest kept.
		/// </summary>
		private static MeshBuilder GlowRoots(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			Vector2 size;
			float shortest, longest;
			switch (variant)
			{
				case 1: count = 34; size = new Vector2(3f, 1.2f); shortest = 0.8f; longest = 3.5f; break;
				case 2: count = 18; size = new Vector2(2.6f, 0.3f); shortest = 0.4f; longest = 1.4f; break;
				default: count = 24; size = new Vector2(2f, 0.8f); shortest = 0.6f; longest = 2.2f; break;
			}
			List<Strand> strands = Strands(rng, count, size, shortest, longest, 0.022f, 0.012f);
			var runners = new List<(float x0, float z0, float x1, float z1)>();
			for (int i = 0; i < 4; i++)
			{
				runners.Add((rng.Range(-0.6f, -0.3f) * size.x, rng.Range(-0.5f, 0.5f) * size.y, rng.Range(0.3f, 0.6f) * size.x, rng.Range(-0.5f, 0.5f) * size.y));
			}
			var m = new MeshBuilder(1);
			int keep = lod == 0 ? count : lod == 1 ? count * 2 / 3 : Mathf.Max(3, count / 3);
			int stations = lod == 0 ? 6 : lod == 1 ? 4 : 3, sides = lod == 0 ? 4 : 3;
			var path = new List<Vector3>();
			var radius = new List<float>();
			var glow = new List<float>();
			for (int i = 0; i < keep; i++)
			{
				Strand s = strands[i];
				HangingPath(in s, stations, 0.12f, 0.35f, 0.3f, 0.9f, path, radius, glow);
				Tube(m, 0, path, radius, glow, sides, false, true);
				Vector3 tip = path[path.Count - 1];
				float bulb = s.Radius * 1.7f;
				Bead(m, 0, tip + Vector3.down * bulb, new Vector3(bulb, bulb * 1.6f, bulb), 1f, lod == 2 ? 4 : 5, lod == 2 ? 2 : 3);
				if (lod == 2)
				{
					continue;
				}
				// Nodules down the strand, brighter lower.
				var prng = new DeterministicRNG(s.Seed ^ 0x5bd1);
				int nodules = Mathf.Clamp(Mathf.RoundToInt(s.Length / 0.7f), 1, 4);
				for (int k = 0; k < nodules; k++)
				{
					float t = (k + 0.4f + 0.4f * prng.NextFloat()) / (nodules + 0.5f);
					if (lod == 1 && (k & 1) == 1)
					{
						continue;
					}
					int at = Mathf.Clamp(Mathf.RoundToInt(t * (path.Count - 1)), 1, path.Count - 1);
					Vector3 p = Vector3.Lerp(path[at - 1], path[at], 0.5f);
					float r = s.Radius * prng.Range(1.3f, 1.7f);
					Bead(m, 0, p, new Vector3(r, r * 1.4f, r), Mathf.Lerp(0.75f, 1f, t), 4, 2);
				}
			}
			if (lod < 2)
			{
				// The runners along the roof, half sunk in it.
				foreach ((float x0, float z0, float x1, float z1) in runners)
				{
					var rp = new List<Vector3> { new Vector3(x0, 0.05f, z0), new Vector3((x0 + x1) * 0.5f, -0.03f, (z0 + z1) * 0.5f), new Vector3(x1, 0.06f, z1) };
					Tube(m, 0, rp, new[] { 0.035f, 0.04f, 0.03f }, new[] { 0.25f, 0.3f, 0.25f }, lod == 0 ? 5 : 4, true, true);
				}
			}
			return m;
		}

		/// <summary>
		/// Tree roots come down through a cave's thin roof near its mouth (2026-10-20), as they hang in shallow caves under a
		/// wood: a few thick roots kinking down from the rock, forking, thinning to a fringe of hair roots at their ends, and
		/// runners along the roof. Not lit: the root material only (pale at the growing tips). v0 a few thick roots over 1.5 m,
		/// to 1.6 m long; v1 a dense hairy curtain 2.5 m across, to 2.4 m; v2 one great root knot and its many fine rootlets.
		/// </summary>
		private static MeshBuilder HangingRoots(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			Vector2 size;
			float shortest, longest, thick, thin;
			switch (variant)
			{
				case 1: count = 22; size = new Vector2(2.5f, 1f); shortest = 0.5f; longest = 2.4f; thick = 0.045f; thin = 0.012f; break;
				case 2: count = 14; size = new Vector2(1.2f, 1.2f); shortest = 0.4f; longest = 1.8f; thick = 0.08f; thin = 0.015f; break;
				default: count = 8; size = new Vector2(1.5f, 0.8f); shortest = 0.6f; longest = 1.6f; thick = 0.06f; thin = 0.02f; break;
			}
			List<Strand> strands = Strands(rng, count, size, shortest, longest, thick, thin);
			var m = new MeshBuilder(1);
			int keep = lod == 0 ? count : lod == 1 ? count * 2 / 3 : Mathf.Max(3, count / 3);
			int stations = lod == 0 ? 6 : lod == 1 ? 4 : 3, sides = lod == 0 ? 5 : lod == 1 ? 4 : 3;
			var path = new List<Vector3>();
			var radius = new List<float>();
			var glow = new List<float>();
			var fork = new List<Vector3>();
			var forkRadius = new List<float>();
			var forkGlow = new List<float>();
			for (int i = 0; i < keep; i++)
			{
				Strand s = strands[i];
				HangingPath(in s, stations, 0.3f, 0.25f, 0.3f, 0.62f, path, radius, glow);
				Tube(m, 0, path, radius, glow, sides, false, true);
				if (lod == 2)
				{
					continue;
				}
				var prng = new DeterministicRNG(s.Seed ^ 0x2c49);
				// A fork from part way down, thinner, wandering off its own way.
				if (s.Length > 0.8f && prng.NextFloat() < 0.7f)
				{
					int from = Mathf.Clamp(stations / 2 - 1, 1, stations - 2);
					var branch = new Strand { Top = path[from], Length = s.Length * prng.Range(0.35f, 0.6f), Radius = radius[from] * 0.6f, Seed = prng.Next() };
					HangingPath(in branch, lod == 0 ? 4 : 3, 0.25f, 0.3f, glow[from], 0.65f, fork, forkRadius, forkGlow);
					fork[0] = path[from];
					Tube(m, 0, fork, forkRadius, forkGlow, Mathf.Max(3, sides - 1), false, true);
				}
				// The hair roots at its end.
				if (lod == 0)
				{
					Vector3 end = path[path.Count - 1];
					for (int k = 0; k < 3; k++)
					{
						var d = new Vector3(prng.Range(-1f, 1f), -1.6f, prng.Range(-1f, 1f)).normalized;
						float hair = prng.Range(0.12f, 0.3f);
						Tube(m, 0, new List<Vector3> { end, end + d * hair }, new[] { radius[radius.Count - 1] * 0.7f, 0.0025f }, new[] { 0.62f, 0.7f }, 3, false, false);
					}
				}
			}
			if (lod < 2)
			{
				// The runners along the roof, half sunk.
				for (int k = 0; k < 3; k++)
				{
					Vector3 a = strands[k % strands.Count].Top, b = strands[(k + 1) % strands.Count].Top;
					var rp = new List<Vector3> { a + Vector3.up * 0.06f, (a + b) * 0.5f + Vector3.down * 0.02f, b + Vector3.up * 0.06f };
					if ((b - a).sqrMagnitude < 0.04f)
					{
						continue;
					}
					Tube(m, 0, rp, new[] { thick * 0.9f, thick, thick * 0.9f }, new[] { 0.28f, 0.3f, 0.28f }, sides, true, true);
				}
			}
			return m;
		}

		/// <summary>
		/// A glow-vine on a wet wall (fantasy, 2026-10-20): creeping stems laid on the rock (along the wall's face, a little into
		/// it), meandering up and across it, set with small rounded leaves that glow, brightest at the growing tips. v0 a patch
		/// 1.5 m across and tall, v1 a sheet 2.5 by 2 m, v2 a cascade hanging 2.4 m down from its top (origin at the top).
		/// </summary>
		private static MeshBuilder GlowVine(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			Vector2 size;
			int stems;
			bool down = variant == 2;
			switch (variant)
			{
				case 1: size = new Vector2(2.5f, 2f); stems = 7; break;
				case 2: size = new Vector2(1.6f, 2.4f); stems = 6; break;
				default: size = new Vector2(1.5f, 1.5f); stems = 5; break;
			}
			var plans = new List<(float x, float lengthShare, float phase, int seed)>(stems);
			for (int i = 0; i < stems; i++)
			{
				plans.Add((rng.Range(-0.45f, 0.45f) * size.x, rng.Range(0.55f, 1f), rng.NextFloat() * Tau, rng.Next()));
			}
			var m = new MeshBuilder(1);
			int stations = lod == 0 ? 9 : lod == 1 ? 6 : 4;
			int keep = lod == 2 ? Mathf.Max(2, stems / 2) : stems;
			for (int i = 0; i < keep; i++)
			{
				(float x, float share, float phase, int pseed) = plans[i];
				var prng = new DeterministicRNG(pseed);
				float length = share * size.y;
				var path = new List<Vector3>(stations);
				var radius = new List<float>(stations);
				var glow = new List<float>(stations);
				for (int k = 0; k < stations; k++)
				{
					float t = k / (float)(stations - 1);
					float y = down ? -length * t : length * t;
					float sx = x + 0.18f * size.x * Mathf.Sin(phase + 3.4f * t) * t;
					path.Add(new Vector3(sx, y, -0.006f));
					radius.Add(Mathf.Lerp(0.016f, 0.007f, t));
					glow.Add(Mathf.Lerp(0.4f, 0.9f, t));
				}
				Tube(m, 0, path, radius, glow, 3, true, true);
				if (lod == 2)
				{
					continue;
				}
				// The leaves: flat ovals lying on the rock beside the stem, glowing more toward its tip.
				int leaves = Mathf.RoundToInt(length / (lod == 0 ? 0.11f : 0.22f));
				for (int k = 0; k < leaves; k++)
				{
					float t = (k + prng.Range(0.1f, 0.9f)) / Mathf.Max(1, leaves);
					int at = Mathf.Clamp(Mathf.FloorToInt(t * (stations - 1)), 0, stations - 2);
					float f = t * (stations - 1) - at;
					Vector3 p = Vector3.Lerp(path[at], path[at + 1], f);
					float side = (k & 1) == 0 ? 1f : -1f;
					float leaf = prng.Range(0.03f, 0.05f) * Mathf.Lerp(1f, 0.7f, t);
					p += new Vector3(side * leaf * 0.9f, prng.Range(-0.5f, 0.5f) * leaf, -0.016f);
					Bead(m, 0, p, new Vector3(leaf, leaf * 0.8f, 0.007f), Mathf.Lerp(0.7f, 1f, t), lod == 0 ? 5 : 4, 2);
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

		// ── Cave crystals (2026-10-16, I6c) ───────────────────────────
		//
		// The cave-crystal catalogue's own habits (ProceduralArtCatalogue.CaveMinerals): every kind not already a rock formation's
		// crystal, a mesh set a habit shared by the kinds that grow in it, as the glow art's hues share their meshes. Two submeshes:
		// the host rock (0, the kit's fieldstone) and the crystal (1, the kind's own material, u a glow level from foot to tip as
		// the glow art's crystals). Floor-mounted like the glow art's crystals (origin at the foot, +y up, seated a little below).

		/// <summary>A level of a cave crystal habit's variant, its tangents made (variants past the catalogue's wrap round).</summary>
		public static MeshBuilder BuildMineral(CaveMineralHabit habit, int variant, int lod, int seed)
		{
			lod = Mathf.Clamp(lod, 0, Levels - 1);
			int n = ProceduralArtCatalogue.CaveMineralVariants;
			variant = ((variant % n) + n) % n;
			int s = ProceduralNoise.SeedFor($"CaveMineral/{habit}/{variant}", seed);
			MeshBuilder m;
			switch (habit)
			{
				case CaveMineralHabit.Druse: m = Druse(variant, lod, s); break;
				case CaveMineralHabit.Prisms: m = Prisms(variant, lod, s); break;
				case CaveMineralHabit.Geode: m = Geode(variant, lod, s); break;
				case CaveMineralHabit.Cubes: m = Cubes(variant, lod, s); break;
				case CaveMineralHabit.Shards: m = Shards(variant, lod, s); break;
				case CaveMineralHabit.Nodules: m = Nodules(variant, lod, s); break;
				case CaveMineralHabit.Blades: m = Blades(variant, lod, s); break;
				// The drafts' habits (2026-10-26, NEW-CRYSTALS): CaveGlowMeshes.Habits.cs.
				default: m = BuildHabit(habit, variant, lod, s); break;
			}
			m.RecalculateTangents();
			return m;
		}

		/// <summary>A matrix lump's tessellation at a level: its segments round and rings up.</summary>
		private static void LumpDetail(int lod, out int segments, out int rings)
		{
			segments = lod == 0 ? 14 : lod == 1 ? 10 : 7;
			rings = lod == 0 ? 7 : lod == 1 ? 5 : 4;
		}

		/// <summary>
		/// Points grown from the top of an ellipsoidal matrix of <paramref name="matrix"/> radii: the longest nearest the middle
		/// and near upright, the rest leaning out the further out they stand (as <see cref="CrystalCluster"/> grows them), each
		/// <paramref name="girth"/> of its length round, between <paramref name="longest"/> and <paramref name="shortest"/>.
		/// </summary>
		private static List<CrystalPart> Grown(DeterministicRNG rng, int count, float longest, float shortest, float girth, float lean, Vector3 matrix, float spread, int seed)
		{
			var parts = new List<CrystalPart>(count);
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (float)(count - 1) : 0f;
				float length = Mathf.Lerp(longest, shortest, Mathf.Pow(rank, 0.75f)) * rng.Range(0.88f, 1.08f);
				float r = Mathf.Sqrt(Mathf.Lerp(0.02f, 1f, rank) * rng.Range(0.4f, 1f)) * spread;
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
			return parts;
		}

		/// <summary>A matrix lump of rock on submesh 0.</summary>
		private static void Matrix(MeshBuilder m, Vector3 radii, float rough, int lod, int seed)
		{
			LumpDetail(lod, out int segments, out int rings);
			m.Append(Lump(radii, segments, rings, rough, seed));
		}

		/// <summary>
		/// Clear quartz's druse: a crust of many small slender points carpeting a low lump of rock, leaning outward. v0 24 points
		/// to 0.42 m, v1 16 to 0.32 m, v2 32 to 0.48 m, v3 26 along an elongated crust.
		/// </summary>
		private static MeshBuilder Druse(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			float longest, shortest;
			Vector3 matrix;
			switch (variant)
			{
				case 1: count = 16; longest = 0.32f; shortest = 0.1f; matrix = new Vector3(0.36f, 0.1f, 0.3f); break;
				case 2: count = 32; longest = 0.48f; shortest = 0.1f; matrix = new Vector3(0.6f, 0.14f, 0.5f); break;
				case 3: count = 26; longest = 0.38f; shortest = 0.1f; matrix = new Vector3(0.7f, 0.1f, 0.28f); break;
				default: count = 24; longest = 0.42f; shortest = 0.12f; matrix = new Vector3(0.5f, 0.12f, 0.42f); break;
			}
			List<CrystalPart> parts = Grown(rng, count, longest, shortest, 0.12f, 55f, matrix, 0.9f, seed);
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.2f, lod, seed + 5);
			AddCrystals(m, parts, lod, 0.25f, 1f);
			return m;
		}

		/// <summary>
		/// Smoky quartz's pocket: a few stout prisms with pyramid tips out of a lump of pegmatite. v0 three to 0.9 m, v1 one
		/// great prism of 1.1 m and two small, v2 five to 0.75 m, v3 a twin pair crossed.
		/// </summary>
		private static MeshBuilder Prisms(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			Vector3 matrix = variant == 1 ? new Vector3(0.4f, 0.24f, 0.36f) : new Vector3(0.45f, 0.25f, 0.4f);
			var parts = new List<CrystalPart>();
			float[] lengths;
			float girth = 0.22f, lean = 30f;
			switch (variant)
			{
				case 1: lengths = new[] { 1.1f, 0.38f, 0.32f }; girth = 0.24f; break;
				case 2: lengths = new[] { 0.75f, 0.62f, 0.55f, 0.48f, 0.4f }; girth = 0.2f; lean = 45f; break;
				case 3: lengths = new[] { 0.85f, 0.8f }; girth = 0.2f; break;
				default: lengths = new[] { 0.9f, 0.7f, 0.55f }; break;
			}
			float az0 = rng.NextFloat() * Tau;
			for (int i = 0; i < lengths.Length; i++)
			{
				float az = variant == 3 ? az0 + i * Mathf.PI : az0 + i * Tau / lengths.Length + rng.Range(-0.4f, 0.4f);
				float r = i == 0 && variant != 3 ? 0.08f : rng.Range(0.25f, 0.6f);
				float tilt = variant == 3 ? rng.Range(24f, 32f) : i == 0 ? rng.Range(3f, 10f) : lean * rng.Range(0.6f, 1.1f);
				Vector3 dir = Tilted(tilt, az + rng.Range(-0.2f, 0.2f));
				var foot = new Vector3(Mathf.Cos(az) * r * matrix.x, matrix.y * 0.7f, Mathf.Sin(az) * r * matrix.z);
				parts.Add(new CrystalPart { Foot = foot - dir * lengths[i] * 0.12f, Dir = dir, Length = lengths[i], Radius = lengths[i] * girth * rng.Range(0.9f, 1.1f),
					Spin = rng.NextFloat() * 360f, Seed = seed + 613 * (i + 1) });
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.26f, lod, seed + 5);
			AddCrystals(m, parts, lod, 0.2f, 1f);
			return m;
		}

		/// <summary>
		/// A geode broken open (amethyst's, wyrdstone's): a bowl of rock rind, its hollow lined with stubby points growing in
		/// toward its middle, laid tilted so its open face looks out (+z) and up. v0 0.42 m across with 46 points, v1 0.3 m with
		/// 30, v2 0.55 m with 60, v3 an elongated 0.38 m with 40.
		/// </summary>
		private static MeshBuilder Geode(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			float radius, rind = 0.07f, stretch = 1f;
			int count;
			switch (variant)
			{
				case 1: radius = 0.3f; count = 30; rind = 0.055f; break;
				case 2: radius = 0.55f; count = 60; rind = 0.085f; break;
				case 3: radius = 0.38f; count = 40; stretch = 1.3f; break;
				default: radius = 0.42f; count = 46; break;
			}
			float inner = radius - rind;
			var centre = new Vector3(0f, radius, 0f);
			var parts = new List<CrystalPart>(count);
			for (int i = 0; i < count; i++)
			{
				// Over the hollow's wall, from near its floor to just under the rim, every way round.
				float t = Mathf.Acos(Mathf.Lerp(Mathf.Cos(8f * Mathf.Deg2Rad), Mathf.Cos(78f * Mathf.Deg2Rad), rng.NextFloat()));
				float phi = rng.NextFloat() * Tau;
				var at = new Vector3(inner * Mathf.Sin(t) * Mathf.Cos(phi), radius - inner * Mathf.Cos(t), inner * Mathf.Sin(t) * Mathf.Sin(phi));
				Vector3 dir = (centre - at).normalized;
				dir = (dir + new Vector3(rng.Range(-0.25f, 0.25f), rng.Range(-0.25f, 0.25f), rng.Range(-0.25f, 0.25f))).normalized;
				float length = inner * rng.Range(0.18f, 0.42f);
				parts.Add(new CrystalPart { Foot = at - dir * length * 0.15f, Dir = dir, Length = length, Radius = length * rng.Range(0.28f, 0.38f), Spin = rng.NextFloat() * 360f, Seed = seed + 389 * (i + 1) });
			}
			float tilt = rng.Range(46f, 58f), turn = rng.Range(-20f, 20f);
			var m = new MeshBuilder(2);
			// The rind: up the outside from its pole to the rim, across the rim, down the inside to the hollow's floor.
			int steps = lod == 0 ? 7 : lod == 1 ? 5 : 3;
			var profile = new List<LP>(2 * steps + 3);
			for (int k = 0; k <= steps; k++)
			{
				float a = 0.5f * Mathf.PI * k / steps;
				profile.Add(new LP(radius * Mathf.Sin(a), radius * (1f - Mathf.Cos(a)), 0.3f, k == steps));
			}
			for (int k = steps; k >= 0; k--)
			{
				float a = 0.5f * Mathf.PI * k / steps;
				profile.Add(new LP(inner * Mathf.Sin(a), radius - inner * Mathf.Cos(a), 0.3f, k == steps));
			}
			int rindFirst = m.VertexCount;
			Lathe(m, 0, profile, lod == 0 ? 18 : lod == 1 ? 12 : 8, Matrix4x4.Scale(new Vector3(stretch, 1f, 1f)), 2f);
			// The rind is rock: textured in metres, as a matrix lump is.
			for (int i = rindFirst; i < m.VertexCount; i++)
			{
				Vector3 q = m.Positions[i];
				m.UVs[i] = new Vector2(q.x + q.z * 0.7f, q.y + q.z * 0.3f);
			}
			var stretched = new List<CrystalPart>(parts.Count);
			foreach (CrystalPart p in parts)
			{
				CrystalPart q = p;
				q.Foot = new Vector3(p.Foot.x * stretch, p.Foot.y, p.Foot.z);
				stretched.Add(q);
			}
			AddCrystals(m, stretched, lod, 0.35f, 1f);
			// Tilted so the hollow looks out and up, then sat a little into the floor.
			m.Transform(Matrix4x4.Rotate(Quaternion.Euler(tilt, turn, 0f)));
			Bounds b = m.Bounds;
			m.Transform(Matrix4x4.Translate(new Vector3(-b.center.x, -b.min.y - 0.06f, -b.center.z)));
			return m;
		}

		/// <summary>
		/// One flat-faced cube (a crystal's), its faces' normals its own, u a glow level rising with its height: in
		/// <paramref name="place"/>'s frame, <paramref name="half"/> its half-extents.
		/// </summary>
		private static void Cuboid(MeshBuilder m, int sub, Matrix4x4 place, Vector3 half, float glowLow, float glowHigh, float v0)
		{
			int first = m.VertexCount;
			Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
			for (int f = 0; f < 6; f++)
			{
				Vector3 n = axes[f];
				Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
				Vector3 w = Vector3.Cross(n, u);
				var corners = new Vector3[4];
				for (int c = 0; c < 4; c++)
				{
					float su = c == 0 || c == 3 ? -1f : 1f, sw = c < 2 ? -1f : 1f;
					corners[c] = Vector3.Scale(n + u * su + w * sw, half);
				}
				int a = m.VertexCount;
				for (int c = 0; c < 4; c++)
				{
					float level = Mathf.Lerp(glowLow, glowHigh, Mathf.InverseLerp(-half.y, half.y, corners[c].y));
					m.AddVertex(corners[c], n, new Vector2(Glow(level), v0 + 0.13f * f + (c == 1 || c == 2 ? 0.11f : 0f)), White);
				}
				m.AddQuad(sub, a, a + 1, a + 2, a + 3, n);
			}
			m.Transform(place, first);
		}

		/// <summary>
		/// Fluorite's interlocking cubes on a lump of rock: some square on it, some stood on an edge or a corner. v0 seven to
		/// 0.32 m, v1 four great ones to 0.42 m, v2 twelve small, v3 six stepped up one side.
		/// </summary>
		private static MeshBuilder Cubes(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			float largest, smallest;
			Vector3 matrix = new Vector3(0.42f, 0.2f, 0.38f);
			switch (variant)
			{
				case 1: count = 4; largest = 0.42f; smallest = 0.26f; matrix = new Vector3(0.46f, 0.22f, 0.42f); break;
				case 2: count = 12; largest = 0.22f; smallest = 0.08f; break;
				case 3: count = 6; largest = 0.3f; smallest = 0.14f; matrix = new Vector3(0.5f, 0.2f, 0.32f); break;
				default: count = 7; largest = 0.32f; smallest = 0.12f; break;
			}
			var cubes = new List<(Matrix4x4 place, float edge, float v)>(count);
			for (int i = 0; i < count; i++)
			{
				float rank = count > 1 ? i / (float)(count - 1) : 0f;
				float edge = Mathf.Lerp(largest, smallest, rank) * rng.Range(0.9f, 1.1f);
				float az = rng.NextFloat() * Tau, r = variant == 3 ? rank * 0.8f : Mathf.Sqrt(rng.NextFloat()) * 0.7f;
				float roll = rng.NextFloat();
				// Square on the rock, on an edge (45° about x), or on a corner (the body diagonal up).
				Quaternion stand = roll < 0.5f ? Quaternion.Euler(rng.Range(-12f, 12f), 0f, rng.Range(-12f, 12f))
					: roll < 0.8f ? Quaternion.Euler(45f, 0f, rng.Range(-10f, 10f)) : Quaternion.Euler(45f, 0f, 35.26f);
				Quaternion turn = Quaternion.AngleAxis(rng.NextFloat() * 360f, Vector3.up) * stand;
				float x = Mathf.Cos(az) * r * matrix.x, z = variant == 3 ? Mathf.Lerp(-matrix.z, matrix.z, rank) * 0.7f : Mathf.Sin(az) * r * matrix.z;
				if (variant == 3)
				{
					x = rng.Range(-0.3f, 0.3f) * matrix.x;
				}
				float top = matrix.y * Mathf.Sqrt(Mathf.Max(0f, 1f - r * r)) * 0.8f + (variant == 3 ? rank * 0.12f : 0f);
				var centre = new Vector3(x, top + edge * 0.25f, z);
				cubes.Add((Matrix4x4.TRS(centre, turn, Vector3.one), edge, rng.NextFloat()));
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.22f, lod, seed + 5);
			int keep = lod == 2 ? Mathf.Max(1, (cubes.Count + 1) / 2) : cubes.Count;
			for (int i = 0; i < keep; i++)
			{
				float h = cubes[i].edge * 0.5f;
				Cuboid(m, 1, cubes[i].place, new Vector3(h, h, h), 0.3f, 1f, cubes[i].v);
			}
			return m;
		}

		/// <summary>
		/// Obsidian's shards: thin glassy blades (flattened four-sided points) jutting every way out of a rough mass. v0 seven to
		/// 0.9 m, v1 ten, v2 four great ones to 1.2 m, v3 twelve small splinters.
		/// </summary>
		private static MeshBuilder Shards(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			float longest, shortest;
			switch (variant)
			{
				case 1: count = 10; longest = 0.75f; shortest = 0.25f; break;
				case 2: count = 4; longest = 1.2f; shortest = 0.6f; break;
				case 3: count = 12; longest = 0.45f; shortest = 0.15f; break;
				default: count = 7; longest = 0.9f; shortest = 0.3f; break;
			}
			var matrix = new Vector3(0.42f, 0.24f, 0.38f);
			List<CrystalPart> parts = Grown(rng, count, longest, shortest, 0.2f, 70f, matrix, 0.85f, seed);
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.32f, lod, seed + 5);
			FlatPoints(m, parts, lod, 0.28f, 0.2f, 1f);
			return m;
		}

		/// <summary>
		/// Sunstone's blades: broad tabular crystals with chisel tips bursting from a lump of scoria. v0 eight to 0.9 m, v1 five
		/// great ones to 1 m, v2 twelve, v3 six fanned in one plane.
		/// </summary>
		private static MeshBuilder Blades(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count;
			float longest, shortest;
			switch (variant)
			{
				case 1: count = 5; longest = 1f; shortest = 0.5f; break;
				case 2: count = 12; longest = 0.7f; shortest = 0.25f; break;
				case 3: count = 6; longest = 0.85f; shortest = 0.4f; break;
				default: count = 8; longest = 0.9f; shortest = 0.35f; break;
			}
			var matrix = new Vector3(0.4f, 0.22f, 0.36f);
			List<CrystalPart> parts = Grown(rng, count, longest, shortest, 0.3f, 58f, matrix, 0.75f, seed);
			if (variant == 3)
			{
				// A fan: every blade in the plane of the first, leaning either way in it.
				float plane = rng.NextFloat() * Tau;
				for (int i = 0; i < parts.Count; i++)
				{
					CrystalPart p = parts[i];
					float lean = Mathf.Lerp(-55f, 55f, parts.Count > 1 ? i / (parts.Count - 1f) : 0.5f);
					p.Dir = Tilted(Mathf.Abs(lean), plane + (lean < 0f ? Mathf.PI : 0f));
					p.Foot = new Vector3(Mathf.Cos(plane), 0f, Mathf.Sin(plane)) * (lean / 55f * 0.18f) + Vector3.up * (matrix.y * 0.6f);
					p.Spin = plane * Mathf.Rad2Deg;
					parts[i] = p;
				}
			}
			var m = new MeshBuilder(2);
			Matrix(m, matrix, 0.4f, lod, seed + 5);
			FlatPoints(m, parts, lod, 0.32f, 0.25f, 1f);
			return m;
		}

		/// <summary>
		/// Points flattened across their own z to <paramref name="flat"/> of their breadth (four-sided: a blade or a shard), the
		/// smaller half dropped at the last level.
		/// </summary>
		private static void FlatPoints(MeshBuilder m, List<CrystalPart> parts, int lod, float flat, float glowFoot, float glowTip)
		{
			parts.Sort((a, b) => b.Length.CompareTo(a.Length));
			int keep = lod == 2 ? Mathf.Max(1, (parts.Count + 1) / 2) : parts.Count;
			for (int i = 0; i < keep; i++)
			{
				CrystalPart c = parts[i];
				var crng = new DeterministicRNG(c.Seed);
				float tip = c.Length * crng.Range(0.18f, 0.3f);
				CrystalPoint(m, 1, Matrix4x4.TRS(c.Foot, UpTo(c.Dir, c.Spin), new Vector3(1f, 1f, flat)), c.Radius, c.Length - tip, tip, 4, crng, glowFoot, glowTip);
			}
		}

		/// <summary>
		/// Amber's nodules: rounded drops and lumps of resin sunk to more than half their height in a low bed of rock. v0 nine
		/// to 0.2 m, v1 five large to 0.26 m, v2 fourteen small, v3 seven and three tall drips.
		/// </summary>
		private static MeshBuilder Nodules(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count, drips = 0;
			float largest, smallest;
			switch (variant)
			{
				case 1: count = 5; largest = 0.26f; smallest = 0.12f; break;
				case 2: count = 14; largest = 0.15f; smallest = 0.04f; break;
				case 3: count = 7; largest = 0.18f; smallest = 0.07f; drips = 3; break;
				default: count = 9; largest = 0.2f; smallest = 0.06f; break;
			}
			var bed = new Vector3(0.55f, 0.14f, 0.5f);
			var beads = new List<(Vector3 centre, Vector3 radii)>(count + drips);
			for (int i = 0; i < count + drips; i++)
			{
				bool drip = i >= count;
				float rank = count > 1 ? Mathf.Min(1f, i / (float)(count - 1)) : 0f;
				float r = Mathf.Lerp(largest, smallest, rank) * rng.Range(0.85f, 1.15f);
				float az = rng.NextFloat() * Tau, at = Mathf.Sqrt(rng.NextFloat()) * 0.75f;
				Vector3 radii = drip ? new Vector3(r * 0.7f, r * 2.2f, r * 0.7f) : new Vector3(r, r * rng.Range(0.65f, 0.85f), r * rng.Range(0.8f, 1f));
				float top = bed.y * Mathf.Sqrt(Mathf.Max(0f, 1f - at * at)) * 0.85f;
				beads.Add((new Vector3(Mathf.Cos(az) * at * bed.x, top + radii.y * 0.3f, Mathf.Sin(az) * at * bed.z), radii));
			}
			var m = new MeshBuilder(2);
			Matrix(m, bed, 0.18f, lod, seed + 5);
			int segments = lod == 0 ? 10 : lod == 1 ? 8 : 6, rings = lod == 0 ? 6 : lod == 1 ? 5 : 4;
			foreach ((Vector3 centre, Vector3 radii) in beads)
			{
				Bead(m, 1, centre, radii, 0.8f, segments, rings);
			}
			return m;
		}

		// ── Cave lava (2026-10-20) ────────────────────────────────────
		//
		// What a lava tube that still runs shows round its lava ("glowing floor/wall cracks, magma seeping from walls, glowing
		// lavacicles"): fissures in a black crust glowing in their depths, runnels of magma down a wall, lava stalactites with
		// dripping glowing tips, and ropy pahoehoe crust cooled at the lava's edge. One submesh each, in the Magma hues (glassy
		// black crust; the glow its own blackbody light through the shared glow ramp, u a glow level as everywhere here).

		/// <summary>How far a lava piece's edges sink under the surface it lies on (floor, wall), metres: seated on uneven rock without a gap.</summary>
		public const float LavaSeat = 0.04f;

		/// <summary>How tall each magma seep variant stands over its foot (its source crack's height), metres: what the placer fits to a wall.</summary>
		public static readonly float[] MagmaSeepHeights = { 1.2f, 2.2f, 1.6f };

		/// <summary>The longest drop of each lavacicle variant under its roof at scale 1, metres.</summary>
		public static readonly float[] LavacicleDrops = { 0.6f, 0.9f, 0.4f };

		/// <summary>How far each magma crack variant reaches from its origin at scale 1, metres (along x; the star every way).</summary>
		public static readonly float[] MagmaCrackReach = { 0.85f, 1.25f, 0.7f, 1.65f };

		/// <summary>Each lava crust variant's length along its flow (x) and width across it at scale 1, metres.</summary>
		public static readonly Vector2[] LavaCrustSizes = { new Vector2(1.6f, 1f), new Vector2(2.4f, 1.6f), new Vector2(3f, 0.9f) };

		/// <summary>One point of a strip's section across (<see cref="Ribbon"/>).</summary>
		private struct RibbonPoint
		{
			/// <summary>Across, in the station's half-widths (−1 .. 1, increasing).</summary>
			public float Across;
			/// <summary>Out of the surface, in the station's lift; ignored where <see cref="Sunk"/>.</summary>
			public float Lift;
			public float Glow;
			/// <summary>Sunk <see cref="LavaSeat"/> under the surface: an edge that seats the strip on uneven rock.</summary>
			public bool Sunk;

			public RibbonPoint(float across, float lift, float glow, bool sunk = false)
			{
				Across = across;
				Lift = lift;
				Glow = glow;
				Sunk = sunk;
			}
		}

		/// <summary>A fissure's section, finest first: sunk edges, the crust's raised lips, its inner walls glowing deeper down, the molten floor.</summary>
		private static readonly RibbonPoint[][] CrackSections =
		{
			new[]
			{
				new RibbonPoint(-1f, 0f, 0f, true), new RibbonPoint(-0.72f, 0.7f, 0f), new RibbonPoint(-0.5f, 1f, 0f), new RibbonPoint(-0.36f, 0.75f, 0.12f),
				new RibbonPoint(-0.25f, 0.45f, 0.55f), new RibbonPoint(-0.12f, 0.3f, 1f), new RibbonPoint(0.12f, 0.3f, 1f), new RibbonPoint(0.25f, 0.45f, 0.55f),
				new RibbonPoint(0.36f, 0.75f, 0.12f), new RibbonPoint(0.5f, 1f, 0f), new RibbonPoint(0.72f, 0.7f, 0f), new RibbonPoint(1f, 0f, 0f, true),
			},
			new[]
			{
				new RibbonPoint(-1f, 0f, 0f, true), new RibbonPoint(-0.5f, 1f, 0f), new RibbonPoint(-0.28f, 0.5f, 0.35f), new RibbonPoint(-0.1f, 0.3f, 1f),
				new RibbonPoint(0.1f, 0.3f, 1f), new RibbonPoint(0.28f, 0.5f, 0.35f), new RibbonPoint(0.5f, 1f, 0f), new RibbonPoint(1f, 0f, 0f, true),
			},
			new[]
			{
				new RibbonPoint(-1f, 0f, 0f, true), new RibbonPoint(-0.45f, 1f, 0f), new RibbonPoint(-0.12f, 0.35f, 1f), new RibbonPoint(0.12f, 0.35f, 1f),
				new RibbonPoint(0.45f, 1f, 0f), new RibbonPoint(1f, 0f, 0f, true),
			},
		};

		/// <summary>A runnel's section, finest first: a bead of molten rock, a dull crusted bank either side, brightest down its middle.</summary>
		private static readonly RibbonPoint[][] RunnelSections =
		{
			new[]
			{
				new RibbonPoint(-1f, 0f, 0.05f, true), new RibbonPoint(-0.7f, 0.55f, 0.15f), new RibbonPoint(-0.35f, 0.9f, 0.6f), new RibbonPoint(0f, 1f, 1f),
				new RibbonPoint(0.35f, 0.9f, 0.6f), new RibbonPoint(0.7f, 0.55f, 0.15f), new RibbonPoint(1f, 0f, 0.05f, true),
			},
			new[]
			{
				new RibbonPoint(-1f, 0f, 0.05f, true), new RibbonPoint(-0.5f, 0.8f, 0.35f), new RibbonPoint(0f, 1f, 1f), new RibbonPoint(0.5f, 0.8f, 0.35f),
				new RibbonPoint(1f, 0f, 0.05f, true),
			},
			new[] { new RibbonPoint(-1f, 0f, 0.1f, true), new RibbonPoint(0f, 1f, 1f), new RibbonPoint(1f, 0f, 0.1f, true) },
		};

		/// <summary>
		/// A strip lofted through stations (each a centre, the way out of the surface there, a half-width and a lift), its section
		/// a height field across (<see cref="RibbonPoint"/>), its ends closed by a fan to a point sunk just past them. Built apart,
		/// normals from its faces, then appended: it lies on the rock as a skin, every face turned out of the surface. v runs in
		/// metres along it. <paramref name="hard"/>: each band across its own vertices, so a crust's lips keep their edges (and a
		/// narrow crack's steep walls never bend the normals of the slopes beside them).
		/// </summary>
		private static void Ribbon(MeshBuilder m, IList<Vector3> centre, IList<Vector3> outward, IList<float> half, IList<float> lift, RibbonPoint[] section, bool hard = false)
		{
			int n = centre.Count, k = section.Length;
			if (n < 2)
			{
				return;
			}
			var points = new Vector3[n, k];
			var uvs = new Vector2[n, k];
			float along = 0f;
			for (int i = 0; i < n; i++)
			{
				if (i > 0)
				{
					along += Vector3.Distance(centre[i], centre[i - 1]);
				}
				Vector3 tangent = (centre[Mathf.Min(i + 1, n - 1)] - centre[Mathf.Max(i - 1, 0)]).normalized;
				Vector3 side = Vector3.Cross(outward[i], tangent).normalized;
				for (int j = 0; j < k; j++)
				{
					RibbonPoint p = section[j];
					float up = p.Sunk ? -LavaSeat : p.Lift * lift[i];
					points[i, j] = centre[i] + side * (p.Across * half[i]) + outward[i] * up;
					uvs[i, j] = new Vector2(Glow(p.Glow), along * 1.7f + p.Across * 0.15f);
				}
			}
			var b = new MeshBuilder(1);
			// Smooth: one vertex a point. Hard: every band (j to j + 1) its own copy of its two columns.
			var shared = new int[n, k];
			var bands = new int[n, k - 1, 2];
			for (int i = 0; i < n; i++)
			{
				if (!hard)
				{
					for (int j = 0; j < k; j++)
					{
						shared[i, j] = b.AddVertex(points[i, j], Vector3.zero, uvs[i, j], White);
					}
				}
				for (int j = 0; j + 1 < k; j++)
				{
					for (int e = 0; e < 2; e++)
					{
						bands[i, j, e] = hard ? b.AddVertex(points[i, j + e], Vector3.zero, uvs[i, j + e], White) : shared[i, j + e];
					}
				}
			}
			for (int i = 0; i + 1 < n; i++)
			{
				Vector3 facing = outward[i] + outward[i + 1];
				for (int j = 0; j + 1 < k; j++)
				{
					b.AddQuad(0, bands[i, j, 0], bands[i, j, 1], bands[i + 1, j, 1], bands[i + 1, j, 0], facing);
				}
			}
			// The ends: a fan to a point sunk just past each, on its own copy of the end's row, so the cap's slopes never bend
			// the strip's normals.
			for (int end = 0; end < 2; end++)
			{
				int i = end == 0 ? 0 : n - 1;
				Vector3 beyond = (end == 0 ? centre[0] - centre[1] : centre[n - 1] - centre[n - 2]).normalized;
				Vector3 tip = centre[i] + beyond * Mathf.Max(0.01f, half[i] * 0.6f) - outward[i] * LavaSeat;
				int t = b.AddVertex(tip, Vector3.zero, new Vector2(Glow(0.05f), along * 1.7f * end), White);
				int ring = b.VertexCount;
				for (int j = 0; j < k; j++)
				{
					b.AddVertex(points[i, j], Vector3.zero, uvs[i, j], White);
				}
				for (int j = 0; j + 1 < k; j++)
				{
					b.AddTriangle(0, t, ring + j, ring + j + 1, outward[i] + beyond * 0.5f);
				}
			}
			b.RecalculateNormals(false);
			m.Append(b);
		}

		/// <summary>The plane a fissure lies in: two directions in it and the way out of it (a floor's: x, z, up; a wall's: x, y, −z).</summary>
		private struct LavaPlane
		{
			public Vector3 U, V, Out;

			public static LavaPlane Floor => new LavaPlane { U = Vector3.right, V = Vector3.forward, Out = Vector3.up };
			public static LavaPlane Wall => new LavaPlane { U = Vector3.right, V = Vector3.up, Out = Vector3.back };
		}

		/// <summary>One fissure's shape, drawn before any level is built: where it starts and heads (radians in its plane), how long and wide, its lips' height, its noise.</summary>
		private struct FissureShape
		{
			public Vector2 Start;
			public float Heading, Length, Half, Lip;
			public int Seed;
		}

		/// <summary>A point of a fissure's centre line in its plane, <paramref name="s"/> metres along it: wandering as a crack runs, a slow bend and a jag.</summary>
		private static Vector2 FissureAt(in FissureShape f, float s)
		{
			float wander = 0.06f * ProceduralNoise.Fbm3(new Vector3(s * 0.8f, 1.3f, 0f), 2, 0.5f, f.Seed)
				+ 0.012f * ProceduralNoise.Fbm3(new Vector3(s * 2.5f, 4.7f, 2f), 2, 0.5f, f.Seed + 1);
			var dir = new Vector2(Mathf.Cos(f.Heading), Mathf.Sin(f.Heading));
			var across = new Vector2(-dir.y, dir.x);
			return f.Start + dir * s + across * wander;
		}

		/// <summary>A fissure lofted in its plane: its half-width swelling and pinching along it, tapering to its ends.</summary>
		private static void Fissure(MeshBuilder m, in FissureShape f, LavaPlane plane, int lod)
		{
			float step = lod == 0 ? 0.05f : lod == 1 ? 0.1f : 0.2f;
			int n = Mathf.Max(3, Mathf.CeilToInt(f.Length / step) + 1);
			var centre = new List<Vector3>(n);
			var outward = new List<Vector3>(n);
			var half = new List<float>(n);
			var lift = new List<float>(n);
			float taper = Mathf.Min(0.25f, 0.3f * f.Length);
			for (int i = 0; i < n; i++)
			{
				float s = f.Length * i / (n - 1);
				Vector2 p = FissureAt(in f, s);
				centre.Add(plane.U * p.x + plane.V * p.y);
				outward.Add(plane.Out);
				float ends = Mathf.SmoothStep(0f, 1f, Mathf.Min(s, f.Length - s) / taper);
				float swell = 0.75f + 0.5f * ProceduralNoise.Fbm3(new Vector3(s * 1.9f, 7.1f, 0f), 2, 0.5f, f.Seed + 2);
				float h = f.Half * swell * Mathf.Lerp(0.3f, 1f, ends);
				half.Add(h);
				// The lips as high as the crack is wide allows: a narrow crack's walls no steeper than a wide one's.
				lift.Add(f.Lip * (0.85f + 0.3f * ProceduralNoise.Fbm3(new Vector3(s * 2.3f, 3.3f, 5f), 2, 0.5f, f.Seed + 3)) * Mathf.Clamp(h / 0.12f, 0.3f, 1f));
			}
			Ribbon(m, centre, outward, half, lift, CrackSections[Mathf.Clamp(lod, 0, CrackSections.Length - 1)], hard: true);
		}

		/// <summary>A fissure starting off another, <paramref name="share"/> of the way along it, heading <paramref name="turn"/> radians off it.</summary>
		private static FissureShape Branch(in FissureShape from, float share, float turn, float length, float half, DeterministicRNG rng)
		{
			return new FissureShape
			{
				Start = FissureAt(in from, share * from.Length),
				Heading = from.Heading + turn,
				Length = length,
				Half = half,
				Lip = rng.Range(0.055f, 0.08f),
				Seed = rng.Next(),
			};
		}

		/// <summary>
		/// A fissure in a crust of cooling lava, glowing molten in its depths, flat on the rock (its lips 6–8 cm proud), along x:
		/// v0 1.6 m with a short branch, v1 a fork 2.4 m, v2 a star of three arms 1.2 m across, v3 a long crack 3.2 m with two
		/// side cracks. Every shape drawn before the level is built.
		/// </summary>
		private static MeshBuilder MagmaCracks(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			var cracks = new List<FissureShape>();
			FissureShape Main(Vector2 start, float heading, float length, float half) => new FissureShape
			{
				Start = start, Heading = heading, Length = length, Half = half, Lip = rng.Range(0.06f, 0.08f), Seed = rng.Next(),
			};
			float Side() => rng.NextFloat() < 0.5f ? -1f : 1f;
			switch (variant)
			{
				case 1:
				{
					FissureShape trunk = Main(new Vector2(-1.2f, 0f), 0f, 1.25f, 0.13f);
					cracks.Add(trunk);
					cracks.Add(Branch(in trunk, 1f, rng.Range(0.35f, 0.5f), rng.Range(1.1f, 1.3f), 0.1f, rng));
					cracks.Add(Branch(in trunk, 1f, -rng.Range(0.35f, 0.5f), rng.Range(1.1f, 1.3f), 0.1f, rng));
					break;
				}
				case 2:
				{
					float a0 = rng.Range(0f, Tau);
					for (int i = 0; i < 3; i++)
					{
						cracks.Add(Main(Vector2.zero, a0 + i * 2.094f + rng.Range(-0.25f, 0.25f), rng.Range(0.5f, 0.65f), 0.12f));
					}
					break;
				}
				case 3:
				{
					FissureShape trunk = Main(new Vector2(-1.6f, 0f), 0f, 3.2f, 0.14f);
					cracks.Add(trunk);
					cracks.Add(Branch(in trunk, 0.3f, Side() * rng.Range(0.6f, 0.9f), rng.Range(0.5f, 0.7f), 0.07f, rng));
					cracks.Add(Branch(in trunk, 0.72f, Side() * rng.Range(0.6f, 0.9f), rng.Range(0.5f, 0.7f), 0.07f, rng));
					break;
				}
				default:
				{
					FissureShape trunk = Main(new Vector2(-0.8f, 0f), 0f, 1.6f, 0.12f);
					cracks.Add(trunk);
					cracks.Add(Branch(in trunk, 0.42f, Side() * rng.Range(0.5f, 0.8f), rng.Range(0.55f, 0.7f), 0.07f, rng));
					break;
				}
			}
			var m = new MeshBuilder(1);
			foreach (FissureShape f in cracks)
			{
				Fissure(m, in f, LavaPlane.Floor, lod);
			}
			return m;
		}

		/// <summary>One runnel of a seep: where it leaves its crack (x, y on the wall), where it meets the floor (x), how far its foot spreads out, its widths and lift, its noise.</summary>
		private struct Runnel
		{
			public float TopX, TopY, FootX, Spread, Width, FootWidth, Lift;
			public int Seed;
		}

		/// <summary>The bend at a runnel's foot, metres: where it turns from the wall onto the floor.</summary>
		private const float RunnelBend = 0.12f;

		/// <summary>
		/// A runnel's centre, the way out of the rock and its half-width and lift at <paramref name="t"/> (0 its top on the wall,
		/// 1 the end of its foot's lobe on the floor): down the wall in a gravity-led meander, round a quarter bend onto the floor,
		/// then out in a widening lobe that rounds off at its end.
		/// </summary>
		private static void RunnelAt(in Runnel r, float t, out Vector3 centre, out Vector3 outward, out float half, out float lift)
		{
			float wall = r.TopY - RunnelBend, bend = 0.5f * Mathf.PI * RunnelBend, total = wall + bend + r.Spread;
			float s = t * total;
			float meander = 0.035f * ProceduralNoise.Fbm3(new Vector3(s * 1.6f, 2.1f, 0f), 2, 0.5f, r.Seed);
			float x = Mathf.Lerp(r.TopX, r.FootX, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s / Mathf.Max(0.01f, wall)))) + meander;
			float swell = 0.85f + 0.3f * ProceduralNoise.Fbm3(new Vector3(s * 2.7f, 5.5f, 1f), 2, 0.5f, r.Seed + 1);
			if (s <= wall)
			{
				centre = new Vector3(x, r.TopY - s, 0f);
				outward = Vector3.back;
				float top = Mathf.SmoothStep(0f, 1f, s / 0.15f);
				half = Mathf.Lerp(r.Width, r.FootWidth * 0.6f, s / Mathf.Max(0.01f, wall)) * swell * Mathf.Lerp(0.5f, 1f, top);
				lift = r.Lift * Mathf.Lerp(0.6f, 1f, top);
				return;
			}
			if (s <= wall + bend)
			{
				float a = (s - wall) / RunnelBend;
				centre = new Vector3(x, RunnelBend * (1f - Mathf.Sin(a)), -RunnelBend * (1f - Mathf.Cos(a)));
				outward = new Vector3(0f, Mathf.Sin(a), -Mathf.Cos(a));
				half = r.FootWidth * 0.6f * swell;
				lift = r.Lift;
				return;
			}
			float u = (s - wall - bend) / Mathf.Max(0.01f, r.Spread);
			centre = new Vector3(x, 0f, -RunnelBend - (s - wall - bend));
			outward = Vector3.up;
			// The lobe widens, then rounds off to its end.
			float round = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Max(0f, (u - 0.55f) / 0.45f), 2f)));
			half = Mathf.Lerp(r.FootWidth * 0.6f, r.FootWidth, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, u / 0.55f))) * Mathf.Max(0.15f, round) * swell;
			lift = r.Lift * Mathf.Lerp(1.15f, 0.7f, u);
		}

		/// <summary>
		/// Magma seeping out of a crack in a wall (fantasy, as hot lava would in a tube still running): runnels of molten rock down
		/// to the wall's foot, each spreading in a small lobe out over the floor there, drops hanging off their bulges. Wall
		/// mount, origin at the wall's foot: v0 one runnel 1.2 m, v1 one 2.2 m splitting in two, v2 three from a crack 1.6 m
		/// across, 1.6 m up (<see cref="MagmaSeepHeights"/>).
		/// </summary>
		private static MeshBuilder MagmaSeeps(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			float height = MagmaSeepHeights[Mathf.Clamp(variant, 0, MagmaSeepHeights.Length - 1)];
			var runnels = new List<Runnel>();
			FissureShape source;
			Runnel Make(float topX, float topY, float footX) => new Runnel
			{
				TopX = topX, TopY = topY, FootX = footX, Spread = rng.Range(0.35f, 0.55f), Width = rng.Range(0.025f, 0.035f),
				FootWidth = rng.Range(0.16f, 0.24f), Lift = rng.Range(0.025f, 0.04f), Seed = rng.Next(),
			};
			switch (variant)
			{
				case 1:
					source = new FissureShape { Start = new Vector2(-0.22f, height), Heading = rng.Range(-0.15f, 0.15f), Length = 0.45f, Half = 0.07f, Lip = 0.05f, Seed = rng.Next() };
					runnels.Add(Make(0f, height, rng.Range(-0.15f, 0.05f)));
					runnels.Add(Make(rng.Range(-0.02f, 0.04f), rng.Range(0.95f, 1.1f), rng.Range(0.25f, 0.4f)));
					break;
				case 2:
					source = new FissureShape { Start = new Vector2(-0.8f, height), Heading = rng.Range(-0.08f, 0.08f), Length = 1.6f, Half = 0.08f, Lip = 0.055f, Seed = rng.Next() };
					foreach (float x in new[] { -0.5f, 0.05f, 0.55f })
					{
						float top = FissureAt(in source, x + 0.8f).y;
						runnels.Add(Make(x, top, x + rng.Range(-0.12f, 0.12f)));
					}
					break;
				default:
					source = new FissureShape { Start = new Vector2(-0.17f, height), Heading = rng.Range(-0.2f, 0.2f), Length = 0.35f, Half = 0.06f, Lip = 0.05f, Seed = rng.Next() };
					runnels.Add(Make(0f, height, rng.Range(-0.1f, 0.1f)));
					break;
			}
			// Drops hanging off the runnels' bulges on the wall: drawn whatever the level, built at the finest.
			var drops = new List<(int runnel, float t, float size)>();
			for (int i = 0; i < runnels.Count; i++)
			{
				int count = rng.Range(1, 4);
				for (int k = 0; k < count; k++)
				{
					drops.Add((i, rng.Range(0.15f, 0.7f), rng.Range(0.014f, 0.024f)));
				}
			}
			var m = new MeshBuilder(1);
			Fissure(m, in source, LavaPlane.Wall, lod);
			int stations = lod == 0 ? 36 : lod == 1 ? 20 : 10;
			RibbonPoint[] section = RunnelSections[Mathf.Clamp(lod, 0, RunnelSections.Length - 1)];
			foreach (Runnel r in runnels)
			{
				var centre = new List<Vector3>(stations);
				var outward = new List<Vector3>(stations);
				var half = new List<float>(stations);
				var lift = new List<float>(stations);
				for (int i = 0; i < stations; i++)
				{
					RunnelAt(in r, i / (stations - 1f), out Vector3 c, out Vector3 o, out float h, out float l);
					centre.Add(c);
					outward.Add(o);
					half.Add(h);
					lift.Add(l);
				}
				Ribbon(m, centre, outward, half, lift, section);
			}
			if (lod == 0)
			{
				foreach ((int runnel, float t, float size) in drops)
				{
					Runnel r = runnels[runnel];
					float wallShare = (r.TopY - RunnelBend) / (r.TopY - RunnelBend + 0.5f * Mathf.PI * RunnelBend + r.Spread);
					RunnelAt(in r, t * wallShare, out Vector3 c, out Vector3 o, out _, out float l);
					Bead(m, 0, c + o * (l + size * 0.6f) + Vector3.down * size, new Vector3(size, size * 1.5f, size), 1f, 6, 4);
				}
			}
			return m;
		}

		/// <summary>One lava stalactite: where it leaves the roof (x, z), how far it hangs, how thick it starts, its noise; or a stout cone (a shark's tooth).</summary>
		private struct LavaDrip
		{
			public Vector2 At;
			public float Length, Radius;
			public bool Tooth;
			public int Seed;
		}

		/// <summary>
		/// Lava stalactites (lavacicles) over molten lava, hung from a crusted roof patch: dark where they leave the rock,
		/// brightening down to their dripping tips, still glowing. Roof mount: v0 seven to 0.6 m, v1 a fringe of twelve along
		/// 1.6 m to 0.9 m, v2 five stout shark's teeth to 0.4 m (<see cref="LavacicleDrops"/>). The shorter ones go first at the
		/// coarser levels.
		/// </summary>
		private static MeshBuilder Lavacicles(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			float longest = LavacicleDrops[Mathf.Clamp(variant, 0, LavacicleDrops.Length - 1)];
			int count;
			Vector2 patch;
			switch (variant)
			{
				case 1: count = 12; patch = new Vector2(0.85f, 0.22f); break;
				case 2: count = 5; patch = new Vector2(0.4f, 0.32f); break;
				default: count = 7; patch = new Vector2(0.5f, 0.38f); break;
			}
			var drips = new List<LavaDrip>(count);
			for (int i = 0; i < count; i++)
			{
				float u = rng.NextFloat();
				Vector2 at = variant == 1
					? new Vector2(rng.Range(-0.9f, 0.9f) * patch.x, rng.Range(-0.4f, 0.4f) * patch.y)
					: Random2(rng) * 0.75f;
				if (variant != 1)
				{
					at = Vector2.Scale(at, patch);
				}
				drips.Add(new LavaDrip
				{
					At = at,
					Length = i == 0 ? longest : Mathf.Lerp(0.25f * longest, longest, u * u),
					Radius = variant == 2 ? rng.Range(0.05f, 0.09f) : rng.Range(0.022f, 0.045f),
					Tooth = variant == 2,
					Seed = rng.Next(),
				});
			}
			drips.Sort((a, b) => b.Length.CompareTo(a.Length));
			int kept = lod == 0 ? count : Mathf.Max(1, Mathf.RoundToInt(count * (lod == 1 ? 0.7f : 0.4f)));
			var m = new MeshBuilder(1);
			// The crusted patch the drips hang from: a low dome of dark crust, its rim up in the rock.
			var crust = new List<LP> { new LP(1f, -0.12f, 0.02f), new LP(0.9f, 0f, 0.04f), new LP(0.55f, 0.035f, 0.06f), new LP(0f, 0.045f, 0.08f) };
			Lathe(m, 0, crust, lod == 0 ? 14 : lod == 1 ? 10 : 7, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(180f, 0f, 0f), new Vector3(patch.x, 1f, patch.y)), 2f);
			int sides = lod == 0 ? 7 : lod == 1 ? 5 : 4;
			int stations = lod == 0 ? 7 : lod == 1 ? 5 : 3;
			for (int i = 0; i < kept; i++)
			{
				LavaDrip d = drips[i];
				var top = new Vector3(d.At.x, 0f, d.At.y);
				if (d.Tooth)
				{
					var cone = new List<LP>
					{
						new LP(d.Radius, -0.1f, 0.03f), new LP(d.Radius, 0f, 0.05f), new LP(0.6f * d.Radius, 0.5f * d.Length, 0.35f),
						new LP(0.25f * d.Radius, 0.88f * d.Length, 0.75f), new LP(0f, d.Length, 1f),
					};
					Lathe(m, 0, cone, sides + 1, Matrix4x4.TRS(top, Quaternion.Euler(180f, 0f, 0f), Vector3.one), 1f);
					continue;
				}
				var prng = new DeterministicRNG(d.Seed);
				Vector3 drift = new Vector3(prng.Range(-1f, 1f), 0f, prng.Range(-1f, 1f)) * (0.06f * d.Length);
				float phase = prng.NextFloat() * Tau;
				var path = new List<Vector3>(stations);
				var radius = new List<float>(stations);
				var glow = new List<float>(stations);
				for (int k = 0; k < stations; k++)
				{
					float t = k / (stations - 1f);
					var wiggle = new Vector3(Mathf.Sin(phase + 7f * t), 0f, Mathf.Cos(phase * 1.3f + 5f * t)) * (0.012f * t);
					path.Add(top + Vector3.up * (0.12f * (1f - t)) + Vector3.down * (d.Length * t) + drift * (t * t) + wiggle);
					radius.Add(d.Radius * Mathf.Lerp(1f, 0.35f, t));
					glow.Add(Mathf.Lerp(0.04f, 0.85f, t * t));
				}
				bool bead = lod < 2;
				Tube(m, 0, path, radius, glow, sides, false, !bead);
				if (bead)
				{
					float r = d.Radius * 0.55f;
					Bead(m, 0, path[stations - 1] + Vector3.down * (0.6f * r), new Vector3(r, r * 1.35f, r), 1f, sides, lod == 0 ? 4 : 3);
				}
			}
			return m;
		}

		/// <summary>A point in the unit disc (area-uniform).</summary>
		private static Vector2 Random2(DeterministicRNG rng)
		{
			float a = rng.NextFloat() * Tau, r = Mathf.Sqrt(rng.NextFloat());
			return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
		}

		/// <summary>
		/// A low lobe of ropy pahoehoe crust, cooled black at molten lava's edge: its rope folds arc toward its front (the way it
		/// flowed, +x), its toes still glowing in their creases, its rim sunk into the rock. At most 12 cm tall at scale 1:
		/// v0 1.6 by 1 m, v1 a broad sheet 2.4 by 1.6 m, v2 a long tongue 3 by 0.9 m. The ropes are the finest level's alone.
		/// </summary>
		private static MeshBuilder LavaCrusts(int variant, int lod, int seed)
		{
			var rng = new DeterministicRNG(seed);
			Vector2 size = LavaCrustSizes[Mathf.Clamp(variant, 0, LavaCrustSizes.Length - 1)];
			float length = size.x, width = size.y, height = variant == 1 ? 0.12f : variant == 2 ? 0.09f : 0.1f;
			float ropePitch = rng.Range(0.11f, 0.15f), ropeBend = rng.Range(0.8f, 1.4f), toes = rng.Range(0.1f, 0.2f);
			int noise = rng.Next();
			int rows = lod == 0 ? Mathf.Clamp(Mathf.CeilToInt(length / ropePitch * 4f), 24, 72) : lod == 1 ? 16 : 8;
			int cols = lod == 0 ? 16 : lod == 1 ? 10 : 6;
			float ropes = lod == 0 ? 1f : 0f;
			var b = new MeshBuilder(1);
			var grid = new int[rows + 1, cols + 1];
			for (int i = 0; i <= rows; i++)
			{
				float u = i / (float)rows;
				// Narrow at its back where it left the flow, broad toward its rounded front.
				float w = 0.5f * width * (0.3f + 0.7f * Mathf.Sin(0.5f * Mathf.PI * Mathf.Min(1f, u * 1.2f)));
				for (int j = 0; j <= cols; j++)
				{
					float v = -1f + 2f * j / cols;
					float x = length * (u - 0.5f) + toes * length * Mathf.Pow(u, 4f) * (Mathf.Sqrt(Mathf.Max(0f, 1f - v * v)) - 1f);
					float z = v * w;
					bool rim = i == 0 || i == rows || j == 0 || j == cols;
					float rope = Mathf.Sin(Tau * ((u * length - ropeBend * 0.12f * v * v) / ropePitch));
					float trough = 0.5f - 0.5f * rope;
					float dome = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Abs(v), 2.2f))) * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, u / 0.25f)) * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, (1f - u) / 0.1f));
					float lump = 0.12f * ProceduralNoise.Fbm3(new Vector3(x * 1.7f, 0f, z * 1.7f), 2, 0.5f, noise);
					float y = rim ? -LavaSeat : height * dome * (0.82f + lump + 0.18f * ropes * rope);
					float g = Mathf.Clamp01(0.75f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.78f, 0.97f, u)) * (0.55f + 0.45f * (ropes > 0f ? trough : 0.5f))
						+ 0.25f * (ropes > 0f ? trough : 0.5f) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.9f, u)));
					grid[i, j] = b.AddVertex(new Vector3(x, y, z), Vector3.zero, new Vector2(Glow(g), x * 1.3f + z * 0.4f), White);
				}
			}
			for (int i = 0; i < rows; i++)
			{
				for (int j = 0; j < cols; j++)
				{
					b.AddQuad(0, grid[i, j], grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j], Vector3.up);
				}
			}
			b.RecalculateNormals(true);
			var m = new MeshBuilder(1);
			m.Append(b);
			return m;
		}
	}
}
#endif
