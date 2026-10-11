#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using Lathe = FishMMO.Shared.WorldDesign.RockFormations.Lathe;
using Field = FishMMO.Shared.WorldDesign.RockFormations.Field;
using Facet = FishMMO.Shared.WorldDesign.RockFormations.Facet;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Mineral and alien formations added by the vegetation expansion (2026-10-10): crystal clusters (prismatic,
	/// bladed, acicular, cubic, jagged, plates), vent cones (spatter cones, hornitos, fumaroles, geyser and ice vent
	/// cones, black smokers), rimstone terraces, crater rims, salt-polygon plates, termite mounds, stalagmites and
	/// flowstone, frost-capped knobs, ventifacts, sastrugi, penitentes, lobate flow fronts and glacier tables — the
	/// new formation kinds beside <see cref="RockFormations"/>' engines, which they reuse. Owned by the rock package;
	/// names come only from <see cref="RockArtNames"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The same contract as every formation.</b> Each builder returns an unfitted mesh of closed, outward-wound
	/// shells in metres near the shape's declared size; <see cref="RockFormations.Build"/> centres it, sinks it by
	/// <see cref="RockFormations.Burial"/> and fits it exactly. Every random draw that shapes the silhouette is made
	/// before anything depends on the resolution, from per-part streams, so the levels of detail share one form and
	/// differ only in sampling (and, for clusters, in how many of the smallest parts they keep).
	/// </para>
	/// <para>
	/// <b>Lathes for anything hollow-topped.</b> A crater, a vent's throat, a pool behind a rim cannot be cast from one
	/// point (a ray from inside the cone leaves through the crater floor before it reaches the inner wall), so they
	/// are lathes whose profile climbs the outer flank to the rim and turns down the inner wall to a floor cap. The
	/// lathe winds from its reference cylinder, so the inner wall faces into the crater as it should.
	/// </para>
	/// </remarks>
	public static class CrystalFormations
	{
		private const float Tau = RockFormations.Tau;

		private static float Smooth(float e0, float e1, float x) => RockFormations.Smooth(e0, e1, x);

		/// <summary>A lathe round <paramref name="centre"/> with an outline, at a tessellation.</summary>
		private static Lathe NewLathe(Vector3 centre, int segments, int capRings, Func<float, float> outline)
		{
			return new Lathe { Centre = centre, Segments = segments, CapRings = capRings, Outline = outline };
		}

		/// <summary>Leans a part: x and z shift by <paramref name="lean"/> metres per metre above <paramref name="baseY"/>, toward <paramref name="azimuth"/>.</summary>
		private static void Shear(MeshBuilder part, float lean, float azimuth, float baseY)
		{
			Matrix4x4 m = Matrix4x4.identity;
			m.m01 = lean * Mathf.Cos(azimuth);
			m.m21 = lean * Mathf.Sin(azimuth);
			part.Transform(Matrix4x4.Translate(new Vector3(0f, baseY, 0f)) * m * Matrix4x4.Translate(new Vector3(0f, -baseY, 0f)));
		}

		/// <summary>The rotation turning up onto <paramref name="dir"/>.</summary>
		private static Matrix4x4 UpTo(Vector3 dir)
		{
			dir.Normalize();
			Vector3 axis = Vector3.Cross(Vector3.up, dir);
			float s = axis.magnitude;
			if (s < 1e-5f)
			{
				return dir.y >= 0f ? Matrix4x4.identity : RockFormations.Rotation(Vector3.right, 180f);
			}
			return RockFormations.Rotation(axis / s, Mathf.Atan2(s, dir.y) * Mathf.Rad2Deg);
		}

		/// <summary>
		/// Segments round each of <paramref name="parts"/> lathes of <paramref name="rings"/> wall rings and
		/// <paramref name="capRings"/> cap rings: as many as most of the level's triangle budget allows, as
		/// <see cref="RockFormations.LatheDetail"/> does for beds, but counting the rings these profiles really have.
		/// </summary>
		private static int Segments(int res, int parts, int rings, int capRings)
		{
			int level = RockFormations.Level(res);
			int perSegment = 2 * (rings - 1) + 2 * (2 * capRings - 1);
			int budget = RockFormations.TriangleBudget[2 - level] * 4 / 5 / Mathf.Max(1, parts);
			return Mathf.Clamp(budget / Mathf.Max(1, perSegment), 8, 6 + 3 * res);
		}

		/// <summary>
		/// Scales a part-built mesh's UVs by the scale <see cref="RockFormations.Build"/> will fit it with (the geometric
		/// mean of its horizontal and vertical factors), so a texture tile still covers its metres on the fitted mesh
		/// when an assembly of parts came out a little larger or smaller than declared.
		/// </summary>
		private static void Prefit(MeshBuilder mesh, float size, float height)
		{
			Bounds b = mesh.Bounds;
			float sh = size / Mathf.Max(1e-4f, Mathf.Max(b.size.x, b.size.z));
			float sv = height / Mathf.Max(1e-4f, b.size.y);
			float k = Mathf.Sqrt(sh * sv);
			for (int i = 0; i < mesh.UVs.Count; i++)
			{
				mesh.UVs[i] *= k;
			}
		}

		// ── Crystal clusters ─────────────────────────────────────────

		private struct Crystal
		{
			public float Length;
			public int Index;
			public Matrix4x4 Place;
		}

		/// <summary>
		/// Crystals grown from a common matrix in the shape's habit: selenite blades with chisel tips, sprays of
		/// sulphur needles, interlocking pyrite cubes, six-sided ice columns, the steep spires of an etched halite crust,
		/// thin plates of hoar. Every crystal is a flat-faced convex polytope, as real crystal faces are planes.
		/// </summary>
		/// <remarks>
		/// Crystals grow outward from where they nucleated, so in a druse they lean away from its middle; the largest
		/// grew first and fastest and stand nearest the centre. The matrix (a lump of the same rock, or for spires and
		/// plates a thin crust) carries the footprint, and its tessellation is what the levels of detail change.
		/// </remarks>
		public static MeshBuilder CrystalCluster(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			CrystalHabit habit = shape.Habit;
			int n = Mathf.Max(1, shape.Count > 0 ? shape.Count : 8);
			float size = shape.Size, height = shape.Height;
			int level = RockFormations.Level(res);
			var mesh = new MeshBuilder(1);

			// The matrix, and how high its top stands at a distance from the middle.
			bool crust = habit == CrystalHabit.Jagged || habit == CrystalHabit.Plates;
			float matrixRadius, matrixTop;
			if (crust)
			{
				matrixRadius = size * 0.5f;
				matrixTop = height * (habit == CrystalHabit.Jagged ? 0.22f : 0.12f);
				RockFormations.LatheDetail(res, 1, false, out int segments, out int bevel, out int wall, out int caps);
				float aspect = Mathf.Lerp(RockFormations.Range(rng, st.Aspect), 1f, 0.4f);
				float yaw = rng.NextFloat() * Tau;
				float r0 = matrixRadius, top = matrixTop;
				int crustSeed = seed + 5;
				Lathe lathe = NewLathe(Vector3.zero, segments, Mathf.Max(caps, level + 1), phi =>
					RockFormations.Superellipse(r0, r0 * aspect, 2.4f, phi - yaw) * (1f + 0.12f * RockFormations.CircleNoise(phi, 2.5f, crustSeed)));
				RockFormations.BedRings(lathe, top, top * 0.1f, top * 0.45f, bevel, wall);
				lathe.TopRelief = p => top * 0.35f * ProceduralNoise.Fbm3(p * (5f / size), 3, 0.5f, crustSeed + 1);
				mesh.Append(RockFormations.BuildLathe(lathe));
			}
			else
			{
				matrixRadius = size * 0.28f;
				matrixTop = height * 0.16f;
				var lumpShape = new FormationShape { Name = shape.Name, Kind = FormationKind.Boulder, Size = size, Height = height };
				Field lump = RockFormations.BaseField(in type, in lumpShape, rng, seed + 7, matrixRadius, matrixTop);
				lump.Pits = default;
				lump.Flutes = default;
				lump.Clasts = default;
				mesh.Append(RockFormations.BuildField(lump, Mathf.Max(2, Mathf.RoundToInt(res * 0.7f))));
			}

			// Every crystal's length, direction and footing, drawn whatever the level.
			var crystals = new List<Crystal>(n);
			float longest = habit == CrystalHabit.Jagged ? height * 0.95f : habit == CrystalHabit.Plates ? height * 1.1f : habit == CrystalHabit.Cubic ? height * 0.55f : height * 0.95f;
			for (int i = 0; i < n; i++)
			{
				float rank = n > 1 ? i / (float)(n - 1) : 0f;
				float length = longest * Mathf.Lerp(1f, habit == CrystalHabit.Jagged ? 0.4f : 0.3f, Mathf.Pow(rank, 0.7f)) * rng.Range(0.85f, 1.1f);
				// The biggest nucleated nearest the middle; the rest spread outward.
				float spread = crust ? 0.82f : 0.75f;
				float r = matrixRadius * spread * Mathf.Sqrt(Mathf.Lerp(0.02f, 1f, rank) * rng.Range(0.5f, 1f));
				float az = rng.NextFloat() * Tau;
				var foot = new Vector3(Mathf.Cos(az) * r, 0f, Mathf.Sin(az) * r);
				float rel = Mathf.Clamp01(r / matrixRadius);
				foot.y = matrixTop * (crust ? 0.9f : Mathf.Sqrt(Mathf.Max(0f, 1f - rel * rel)) * 0.85f);
				float tilt;
				switch (habit)
				{
					case CrystalHabit.Bladed: tilt = Mathf.Lerp(4f, 50f, rel) + rng.Range(-6f, 8f); break;
					case CrystalHabit.Acicular: tilt = Mathf.Lerp(5f, 72f, rel) + rng.Range(-8f, 8f); break;
					case CrystalHabit.Jagged: tilt = rng.Range(0f, 10f); break;
					case CrystalHabit.Plates: tilt = rng.Range(25f, 80f); break;
					case CrystalHabit.Cubic: tilt = rng.Range(0f, 35f); break;
					default: tilt = Mathf.Lerp(3f, 40f, rel) + rng.Range(-5f, 6f); break;
				}
				// Lean away from the middle, a little off radial.
				Vector3 dir = RockFormations.Tilted(Mathf.Max(0f, tilt), az + rng.Range(-0.5f, 0.5f));
				float spin = rng.NextFloat() * 360f;
				float sink = length * (habit == CrystalHabit.Cubic ? rng.Range(0.15f, 0.45f) : 0.12f);
				Matrix4x4 place = Matrix4x4.Translate(foot - dir * sink) * UpTo(dir) * RockFormations.Rotation(Vector3.up, spin);
				crystals.Add(new Crystal { Length = length, Index = i, Place = place });
			}
			crystals.Sort((a, b) => a.Length != b.Length ? b.Length.CompareTo(a.Length) : a.Index.CompareTo(b.Index));

			// Every crystal at every level: they are flat-faced and cheap, and the silhouette is theirs. The levels
			// differ in the matrix's tessellation.
			for (int k = 0; k < crystals.Count; k++)
			{
				Crystal c = crystals[k];
				var crng = new DeterministicRNG(seed + 7919 * (c.Index + 1));
				float length = c.Length;
				MeshBuilder part = RockFormations.Polytope(() => CrystalFacets(crng, habit, length), size, 0, seed + c.Index, new Vector3(length * 0.15f, length * 0.5f, length * 0.15f));
				part.Transform(c.Place);
				mesh.Append(part);
			}
			Prefit(mesh, size, height);
			return mesh;
		}

		/// <summary>One crystal's planes in its own frame: its axis up, its foot at the origin (a sunk base below).</summary>
		private static List<Facet> CrystalFacets(DeterministicRNG rng, CrystalHabit habit, float length)
		{
			var facets = new List<Facet>();
			float turn = rng.NextFloat() * Tau;
			switch (habit)
			{
				case CrystalHabit.Bladed:
				{
					// Monoclinic selenite: a flattened prism, the broad faces a little skewed, ended by a chisel.
					float a = length * rng.Range(0.14f, 0.22f), b = a * rng.Range(0.18f, 0.28f);
					float skew = rng.Range(-0.12f, 0.12f);
					facets.Add(RockFormations.FacetOf(new Vector3(1f, 0f, skew), a));
					facets.Add(RockFormations.FacetOf(new Vector3(-1f, 0f, -skew), a));
					facets.Add(RockFormations.FacetOf(new Vector3(skew, 0f, 1f), b));
					facets.Add(RockFormations.FacetOf(new Vector3(-skew, 0f, -1f), b));
					float tau = rng.Range(48f, 66f) * Mathf.Deg2Rad;
					float off = rng.Range(-0.3f, 0.3f) * a;
					var apex = new Vector3(off, length, 0f);
					foreach (float side in new[] { 1f, -1f })
					{
						var nn = new Vector3(side * Mathf.Sin(tau), Mathf.Cos(tau), 0f);
						facets.Add(RockFormations.FacetOf(nn, Vector3.Dot(nn.normalized, apex)));
					}
					break;
				}
				case CrystalHabit.Acicular:
				{
					float r = length * rng.Range(0.035f, 0.06f);
					AddPrism(facets, rng, 6, r, turn);
					AddPyramid(facets, 6, r, length, rng.Range(74f, 80f), turn);
					break;
				}
				case CrystalHabit.Cubic:
				{
					// A cube centred above its foot by half its edge (the placement sinks it into the matrix).
					float h = length * 0.5f;
					var c = new Vector3(0f, h, 0f);
					foreach (Vector3 axis in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
					{
						facets.Add(RockFormations.FacetOf(axis, Vector3.Dot(axis, c) + h * rng.Range(0.95f, 1f)));
					}
					return facets;
				}
				case CrystalHabit.Jagged:
				{
					// An etched spire: steep faces converging on an off-centre tip, a chip or two off the top.
					int sides = 5 + rng.Next(3);
					var apex = new Vector3(rng.Range(-0.08f, 0.08f) * length, length, rng.Range(-0.08f, 0.08f) * length);
					for (int k = 0; k < sides; k++)
					{
						float az = turn + Tau * k / sides + rng.Range(-0.35f, 0.35f) * Tau / sides;
						Vector3 nn = RockFormations.Tilted(rng.Range(66f, 80f), az);
						facets.Add(RockFormations.FacetOf(nn, Vector3.Dot(nn, apex) + rng.Range(-0.01f, 0.04f) * length));
					}
					int chips = 1 + rng.Next(2);
					for (int k = 0; k < chips; k++)
					{
						Vector3 nn = RockFormations.Tilted(rng.Range(25f, 60f), rng.NextFloat() * Tau);
						facets.Add(RockFormations.FacetOf(nn, Vector3.Dot(nn, apex) - rng.Range(0.04f, 0.14f) * length));
					}
					break;
				}
				case CrystalHabit.Plates:
				{
					// A thin hexagonal plate on edge: its faces ±z, its six edges round the axis.
					float r = length * 0.5f, t = r * rng.Range(0.06f, 0.12f);
					var c = new Vector3(0f, r, 0f);
					facets.Add(RockFormations.FacetOf(Vector3.forward, t * 0.5f));
					facets.Add(RockFormations.FacetOf(Vector3.back, t * 0.5f));
					for (int k = 0; k < 6; k++)
					{
						float az = turn + Tau * k / 6f;
						var nn = new Vector3(Mathf.Cos(az), Mathf.Sin(az), 0f);
						facets.Add(RockFormations.FacetOf(nn, Vector3.Dot(nn, c) + r * rng.Range(0.82f, 1f)));
					}
					return facets;
				}
				default:
				{
					// A six-sided column: ice's flat basal face with its corners bevelled, or a shallow point.
					float r = length * rng.Range(0.12f, 0.19f);
					AddPrism(facets, rng, 6, r, turn);
					if (rng.NextFloat() < 0.6f)
					{
						facets.Add(RockFormations.FacetOf(Vector3.up, length));
						float tau = 68f * Mathf.Deg2Rad;
						for (int k = 0; k < 6; k++)
						{
							float az = turn + Tau * (k + 0.5f) / 6f;
							Vector3 nn = RockFormations.Tilted(68f, az);
							facets.Add(RockFormations.FacetOf(nn, Mathf.Cos(tau) * length + Mathf.Sin(tau) * r * 0.9f));
						}
					}
					else
					{
						AddPyramid(facets, 6, r, length, rng.Range(48f, 56f), turn);
					}
					break;
				}
			}
			// The sunk base.
			facets.Add(RockFormations.FacetOf(Vector3.down, length * 0.15f));
			return facets;
		}

		private static void AddPrism(List<Facet> facets, DeterministicRNG rng, int sides, float radius, float turn)
		{
			for (int k = 0; k < sides; k++)
			{
				float a = turn + Tau * k / sides;
				facets.Add(RockFormations.FacetOf(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), radius * rng.Range(0.93f, 1.03f)));
			}
		}

		/// <summary>A pyramid ending a prism at <paramref name="length"/>: faces tilted <paramref name="tiltDegrees"/> from the axis, through the apex.</summary>
		private static void AddPyramid(List<Facet> facets, int sides, float radius, float length, float tiltDegrees, float turn)
		{
			float tau = tiltDegrees * Mathf.Deg2Rad;
			for (int k = 0; k < sides; k++)
			{
				float az = turn + Tau * k / sides;
				Vector3 nn = RockFormations.Tilted(tiltDegrees, az);
				facets.Add(RockFormations.FacetOf(nn, Mathf.Cos(tau) * length));
			}
		}

		// ── Vent cones ───────────────────────────────────────────────

		/// <summary>
		/// A vent cone: the outer flank climbing to a rim, the inner wall falling to a crater floor with a throat in
		/// its middle. Low and wide (height under about 0.6 of the width) it is a spatter or geyser or fumarole cone
		/// with a broad crater; tall and narrow it is a hornito or a chimney with a small orifice and a flared foot.
		/// <see cref="FormationShape.Count"/> adds flanges: the ledges a black smoker grows where hot fluid ponds
		/// beneath an overhang and keeps depositing.
		/// </summary>
		public static MeshBuilder Cone(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			int flanges = Mathf.Max(0, shape.Count);
			RockFormations.LatheDetail(res, 1 + flanges, false, out _, out int bevel, out _, out int capRings);
			int level = RockFormations.Level(res);
			int outer = level == 2 ? 8 : level == 1 ? 5 : 3;
			int inner = level == 2 ? 3 : level == 1 ? 2 : 1;
			int lipRings = level == 2 ? 4 : level == 1 ? 3 : 2;
			int segments = Segments(res, 1 + flanges, outer + inner + lipRings + 1, capRings);
			float w = shape.Size * 0.5f, height = shape.Height;
			float tall = Smooth(0.6f, 1.8f, height / shape.Size);
			float aspect = Mathf.Lerp(RockFormations.Range(rng, st.Aspect), 1f, 0.55f);
			float yaw = rng.NextFloat() * Tau;
			float rimScale = Mathf.Lerp(rng.Range(0.5f, 0.6f), rng.Range(0.3f, 0.38f), tall);
			float throat = rimScale * Mathf.Lerp(rng.Range(0.58f, 0.7f), rng.Range(0.45f, 0.58f), tall);
			float floorScale = throat * rng.Range(0.42f, 0.55f);
			float depth = height * Mathf.Lerp(rng.Range(0.28f, 0.4f), rng.Range(0.12f, 0.2f), tall);
			float lump = st.Lumpiness;
			int outlineSeed = seed + 5;
			float leanAz = rng.NextFloat() * Tau;
			float lean = rng.Range(0.02f, 0.08f) * Mathf.Lerp(0.5f, 1f, tall);
			float knobCell = Mathf.Max(0.12f, 0.16f * shape.Size);

			Lathe lathe = NewLathe(Vector3.zero, segments, capRings, phi =>
				RockFormations.Superellipse(w, w * aspect, 2.2f, phi - yaw) * (1f + lump * 0.14f * RockFormations.CircleNoise(phi, 2f, outlineSeed)));
			float spacingPower = Mathf.Lerp(1f, 1.7f, tall);
			// The rim is a rounded lip, not a knife edge: a half-round of rings over the wall between the flank and
			// the throat (smoothed normals across a sharp crest would face the steep inner wall the wrong way).
			float lip = 0.45f * (rimScale - throat);
			float lipM = Mathf.Min(lip * w, 0.45f * depth);
			float lipCentre = rimScale - lip;
			for (int r = 0; r <= outer; r++)
			{
				float t = Mathf.Pow(r / (float)outer, spacingPower);
				// A cone steepens a little to its rim; a chimney stands straight above a flared foot.
				float cone = 1f - (1f - rimScale) * Mathf.Pow(t, 0.85f);
				float chimney = rimScale + (1f - rimScale) * Mathf.Pow(1f - t, 4f);
				lathe.AddRing(t * (height - lipM), Mathf.Lerp(cone, chimney, tall), 0f);
			}
			for (int k = 1; k <= lipRings; k++)
			{
				float a = Mathf.PI * k / lipRings;
				lathe.AddRing(height - lipM + lipM * Mathf.Sin(a), lipCentre + lip * Mathf.Cos(a), 0f, 1f - k / (float)lipRings);
			}
			float wallTop = height - lipM;
			for (int r = 1; r <= inner; r++)
			{
				float u = r / (float)inner;
				float y = wallTop - (depth - lipM) * Mathf.Pow(u, 0.75f);
				float s = r == inner ? floorScale : Mathf.Lerp(lipCentre - lip, floorScale, Mathf.Pow(u, 1.2f));
				lathe.AddRing(y, s, 0f, 0f);
			}
			float floorRadius = w * floorScale;
			lathe.WallOffset = (p, phi) =>
			{
				float u = Mathf.Clamp01(p.y / height);
				float radial = new Vector2(p.x, p.z).magnitude;
				float d = lump * 0.07f * radial * ProceduralNoise.Fbm3(p * (2.5f / w), 3, 0.5f, seed + 7);
				// Clots of spatter, nodules of geyserite: knobs a sixth of the width across.
				d += lump * 0.1f * radial * (RockFormations.CellBump(p / knobCell, seed + 23, 0.55f, 0.3f, 0.5f, 0.7f) - 0.3f);
				return d * (1f - 0.5f * Smooth(0.9f, 1f, u));
			};
			// The throat: a funnel sunk in the crater floor.
			lathe.TopRelief = p =>
			{
				float k = Mathf.Clamp01(1f - new Vector2(p.x, p.z).magnitude / Mathf.Max(1e-3f, floorRadius));
				return -depth * 0.45f * k * k;
			};
			var mesh = new MeshBuilder(1);
			mesh.Append(RockFormations.BuildLathe(lathe));

			for (int f = 0; f < flanges; f++)
			{
				var frng = new DeterministicRNG(seed + 401 * (f + 1));
				float t = Mathf.Lerp(0.28f, 0.82f, (f + 0.5f) / flanges) + frng.Range(-0.05f, 0.05f);
				float cone = 1f - (1f - rimScale) * Mathf.Pow(t, 0.85f);
				float chimney = rimScale + (1f - rimScale) * Mathf.Pow(1f - t, 4f);
				float at = w * Mathf.Lerp(cone, chimney, tall);
				float az = frng.NextFloat() * Tau;
				float reach = frng.Range(0.7f, 1.0f);
				float thick = height * frng.Range(0.02f, 0.035f);
				int flangeSeed = seed + 409 * (f + 1);
				Lathe shelf = NewLathe(new Vector3(0f, t * height - thick * 0.5f, 0f), Mathf.Max(8, segments * 2 / 3), 1, phi =>
				{
					float side = Mathf.Max(0f, Mathf.Cos(phi - az));
					return at * (0.85f + reach * side * side) * (1f + 0.15f * RockFormations.CircleNoise(phi, 3f, flangeSeed));
				});
				RockFormations.BedRings(shelf, thick, thick * 0.25f, thick * 0.4f, bevel, 0);
				mesh.Append(RockFormations.BuildLathe(shelf));
			}
			// Leaned whole, flanges with it, by a shear (a radial offset would slide a narrow top off its axis).
			Shear(mesh, lean, leanAz, 0f);
			return mesh;
		}

		// ── Rimmed terraces ──────────────────────────────────────────

		/// <summary>
		/// Pools stepping down a slope (toward +x), each a lobe whose wall rises to a rounded lip a little above the
		/// pool behind it. Water spreading thin over a lip degasses and cools fastest there, so the rim grows up
		/// faster than the floor and dams the pool: travertine and sinter terraces, cave gours, Dallol's salt terraces.
		/// Each pool overlaps the next, so an upper pool's front wall stands in the lower pool.
		/// </summary>
		public static MeshBuilder Terrace(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			int pools = Mathf.Max(2, shape.Count > 0 ? shape.Count : 4);
			RockFormations.LatheDetail(res, pools, false, out int segments, out _, out _, out int capRings);
			int level = RockFormations.Level(res);
			float length = shape.Size, height = shape.Height;
			float ground = RockFormations.Burial(FormationKind.Terrace) * height;
			float step = (height - ground) / pools;
			float lip = Mathf.Clamp(0.5f * step, 0.02f, 0.3f);
			float baseRadius = length / (pools + 1) * 1.15f;
			float lump = st.Lumpiness;
			var mesh = new MeshBuilder(1);
			for (int k = 0; k < pools; k++)
			{
				var prng = new DeterministicRNG(seed + 211 * (k + 1));
				float ax = baseRadius * prng.Range(0.88f, 1.12f);
				float az = ax * prng.Range(1.3f, 1.8f);
				float cx = -length * 0.5f + baseRadius + k * (length - 2f * baseRadius) / (pools - 1);
				float cz = prng.Range(-0.15f, 0.15f) * az;
				float crest = height - k * step;
				int lobeSeed = seed + 223 * (k + 1);
				float a = ax, b = az;
				Lathe lathe = NewLathe(new Vector3(cx, 0f, cz), segments, Mathf.Max(capRings, level + 1), phi =>
					// Scalloped all round, and bulging downslope where the overflow builds the front out.
					RockFormations.Superellipse(a, b, 2.4f, phi) * (1f + 0.1f * RockFormations.CircleNoise(phi, 3f, lobeSeed) + 0.12f * Mathf.Max(0f, Mathf.Cos(phi))));
				lathe.AddRing(0f, 1f, 0f);
				if (level > 0)
				{
					lathe.AddRing(crest - lip * 1.6f, 1f, 0f);
				}
				lathe.AddRing(crest - lip * 0.5f, 0.995f, 0f);
				lathe.AddRing(crest, 0.975f, 0f);
				if (level > 1)
				{
					lathe.AddRing(crest - lip * 0.45f, 0.94f, 0f, 0.5f);
				}
				lathe.AddRing(crest - lip, 0.91f, 0f, 0.3f);
				float wallLump = lump * 0.015f * length;
				lathe.WallOffset = (p, phi) => wallLump * ProceduralNoise.Fbm3(p * (3f / length), 3, 0.5f, lobeSeed + 7);
				float floorRelief = 0.06f * lip;
				lathe.TopRelief = p => floorRelief * ProceduralNoise.Fbm3(p * (4f / length), 2, 0.5f, lobeSeed + 9);
				mesh.Append(RockFormations.BuildLathe(lathe));
			}
			return mesh;
		}

		// ── Crater rims ──────────────────────────────────────────────

		/// <summary>
		/// A small crater: an apron of ejecta rising, steepening, to a rim crest about half way out, then the inner
		/// wall falling to a floor just below the ground, so the bowl's floor is the terrain itself. The crest wanders
		/// in height round the rim, and <see cref="FormationShape.Count"/> ejecta blocks of the rock lie on the crest and
		/// the upper apron, where the largest fragments of a fresh small crater land.
		/// </summary>
		public static MeshBuilder CraterRim(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			int blocks = Mathf.Max(0, shape.Count);
			RockFormations.LatheDetail(res, 1 + blocks / 3, false, out int segments, out _, out _, out int capRings);
			int level = RockFormations.Level(res);
			float radius = shape.Size * 0.5f, height = shape.Height;
			float ground = RockFormations.Burial(FormationKind.CraterRim) * height;
			float crest = rng.Range(0.5f, 0.58f);
			float floorAt = crest * rng.Range(0.42f, 0.52f);
			float aspect = Mathf.Lerp(RockFormations.Range(rng, st.Aspect), 1f, 0.7f);
			float yaw = rng.NextFloat() * Tau;
			int outlineSeed = seed + 5;
			Lathe lathe = NewLathe(Vector3.zero, segments, capRings, phi =>
				RockFormations.Superellipse(radius, radius * aspect, 2f, phi - yaw) * (1f + 0.06f * RockFormations.CircleNoise(phi, 2.5f, outlineSeed)));
			lathe.AddRing(0f, 1f, 0f);
			lathe.AddRing(ground, 1f, 0f);
			int apron = level == 2 ? 5 : level == 1 ? 3 : 1;
			for (int i = 1; i <= apron; i++)
			{
				float u = i / (float)(apron + 1);
				lathe.AddRing(ground + (height - ground) * Mathf.Pow(u, 2.2f), Mathf.Lerp(1f, crest, u), 0f);
			}
			lathe.AddRing(height, crest, 0f);
			int inner = level == 2 ? 3 : level == 1 ? 2 : 1;
			float floorY = ground - 0.04f * height;
			for (int j = 1; j <= inner; j++)
			{
				float v = j / (float)inner;
				lathe.AddRing(Mathf.Lerp(height, floorY, Mathf.Pow(v, 0.8f)), Mathf.Lerp(crest * 0.97f, floorAt, v), 0f);
			}
			float size = shape.Size;
			float wallLump = (0.15f + st.Lumpiness) * 0.006f * size;
			lathe.WallOffset = (p, phi) => wallLump * ProceduralNoise.Fbm3(p * (4f / size), 3, 0.5f, seed + 7);
			MeshBuilder rim = RockFormations.BuildLathe(lathe);
			// The crest wanders up and down round the rim: a vertical lift that peaks at the crest and fades to
			// nothing at the ground and the floor, so the profile never folds.
			float rise = (height - ground) * 0.22f;
			float crestY = height;
			for (int i = 0; i < rim.VertexCount; i++)
			{
				Vector3 p = rim.Positions[i];
				float weight = Mathf.Clamp01((p.y - ground) / (crestY - ground));
				weight = weight * weight * (3f - 2f * weight);
				p.y += rise * weight * RockFormations.CircleNoise(Mathf.Atan2(p.z, p.x), 3f, seed + 9);
				rim.Positions[i] = p;
			}
			rim.RecalculateNormals(true);
			var mesh = new MeshBuilder(1);
			mesh.Append(rim);

			FractureStyle fr = st.Fracture;
			if (fr.Dish <= 0f)
			{
				fr.Dish = 0.03f;
			}
			for (int k = 0; k < blocks; k++)
			{
				var brng = new DeterministicRNG(seed + 601 * (k + 1));
				float rr = radius * Mathf.Min(0.92f, crest * brng.Range(0.9f, 1.5f));
				float az = brng.NextFloat() * Tau;
				float bs = radius * brng.Range(0.07f, 0.13f) * (k == 0 ? 1.3f : 1f);
				var half = new Vector3(bs * 0.5f, bs * brng.Range(0.28f, 0.38f), bs * brng.Range(0.34f, 0.46f));
				float u = Mathf.Clamp01((1f - rr / radius) / (1f - crest));
				float y = ground + (height - ground) * Mathf.Pow(u, 2.2f);
				MeshBuilder block = RockFormations.Polytope(() => RockFormations.ConchoidalFacets(brng, half, 9, st.FacetDepth, in fr, true), bs / Mathf.Max(1f, 0.4f * res), 0, seed + k, half);
				block.Transform(Matrix4x4.Translate(new Vector3(Mathf.Cos(az) * rr, y + half.y * 0.25f, Mathf.Sin(az) * rr)) * RockFormations.Rotation(Vector3.up, brng.NextFloat() * 360f)
					* RockFormations.Rotation(RockFormations.HorizontalAxis(brng), brng.Range(0f, 20f)));
				mesh.Append(block);
			}
			return mesh;
		}

		// ── Salt polygons ────────────────────────────────────────────

		/// <summary>
		/// A patch of salt-crust polygons: a drying pan's crust cracks into cells two or three metres across, and
		/// salt crystallising in the cracks pushes their edges up into ridges. Each cell is a low tray whose rim
		/// rises to a crest on the cell's boundary and falls inward to a flat floor; neighbouring cells overlap by
		/// a ridge's width, so their crests meet along the boundary as one ridge with a flank on either side.
		/// </summary>
		public static MeshBuilder PolygonRidges(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int cells = Mathf.Max(3, shape.Count > 0 ? shape.Count : 5);
			RockFormations.LatheDetail(res, cells, false, out int segments, out _, out _, out int capRings);
			float size = shape.Size, height = shape.Height;
			float ground = RockFormations.Burial(FormationKind.PolygonRidges) * height;
			float floorY = ground + 0.05f * height;
			float ridge = Mathf.Clamp(0.06f * size, 0.12f, 0.45f);
			float spacing = size / Mathf.Sqrt(cells) * 0.95f;
			var sites = new List<Vector2>();
			var keys = new List<float>();
			for (int q = -4; q <= 4; q++)
			{
				for (int r = -4; r <= 4; r++)
				{
					if (Mathf.Abs(q + r) > 4)
					{
						continue;
					}
					var p = new Vector2(spacing * (q + r * 0.5f), spacing * r * 0.8660254f);
					p += new Vector2(rng.Range(-0.22f, 0.22f), rng.Range(-0.22f, 0.22f)) * spacing;
					sites.Add(p);
					keys.Add(p.sqrMagnitude);
				}
			}
			var order = new List<int>();
			for (int i = 0; i < sites.Count; i++)
			{
				order.Add(i);
			}
			order.Sort((x, y) => keys[x] != keys[y] ? keys[x].CompareTo(keys[y]) : x.CompareTo(y));
			var mesh = new MeshBuilder(1);
			for (int k = 0; k < Mathf.Min(cells, order.Count); k++)
			{
				Vector2 site = sites[order[k]];
				List<Vector2> cell = RockFormations.VoronoiCell(site, sites, -ridge, spacing * 2f);
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
				float crest = height * rng.Range(0.8f, 1f);
				int cellSeed = seed + 307 * (k + 1);
				List<Vector2> polygon = cell;
				Vector2 origin = centroid;
				Lathe lathe = NewLathe(new Vector3(centroid.x, 0f, centroid.y), segments, capRings, phi => RockFormations.RayToPolygon(origin, phi, polygon));
				lathe.AddRing(0f, 1f, 0f);
				lathe.AddRing(ground, 1f, 0f);
				lathe.AddRing(crest, 1f, ridge * 0.5f);
				lathe.AddRing(Mathf.Lerp(crest, floorY, 0.6f), 1f, ridge * 0.95f, 0.5f);
				lathe.AddRing(floorY, 1f, ridge * 1.3f, 0.3f);
				float wallLump = 0.02f * height;
				lathe.WallOffset = (p, phi) => wallLump * ProceduralNoise.Fbm3(p * (6f / size), 2, 0.5f, cellSeed);
				float blister = 0.04f * height;
				lathe.TopRelief = p => blister * ProceduralNoise.Fbm3(p * (3f / size), 2, 0.5f, cellSeed + 1);
				mesh.Append(RockFormations.BuildLathe(lathe));
			}
			return mesh;
		}

		// ── Termite mounds ───────────────────────────────────────────

		/// <summary>
		/// A cathedral mound: a tapering spire scored by deep buttress flutes, with two or three lesser turrets leaning
		/// off its flanks over the side shafts of the nest's ventilation, knobbly with the pellets it is built from.
		/// </summary>
		public static MeshBuilder Mound(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			int turrets = 2 + rng.Next(2);
			RockFormations.LatheDetail(res, 1 + turrets, false, out _, out _, out _, out int capRings);
			int level = RockFormations.Level(res);
			int rings = level == 2 ? 12 : level == 1 ? 8 : 5;
			int segments = Segments(res, 1 + turrets, rings, capRings);
			float w = shape.Size * 0.5f / 1.15f, height = shape.Height;
			float lump = st.Lumpiness;
			int flutes = Mathf.Max(5, st.Flutes.Count);
			float knob = Mathf.Max(0.08f, 0.07f * shape.Size);
			var mesh = new MeshBuilder(1);
			for (int t = 0; t <= turrets; t++)
			{
				var trng = new DeterministicRNG(seed + 503 * (t + 1));
				bool main = t == 0;
				float h = main ? height : height * trng.Range(0.4f, 0.72f);
				float r0 = main ? w * 0.62f : w * trng.Range(0.3f, 0.42f);
				float az = trng.NextFloat() * Tau;
				float off = main ? w * trng.Range(0f, 0.08f) : w * trng.Range(0.35f, 0.55f);
				float aspect = trng.Range(0.75f, 0.95f);
				float yaw = trng.NextFloat() * Tau;
				int partSeed = seed + 509 * (t + 1);
				float top = h, radius = r0;
				Lathe lathe = NewLathe(new Vector3(Mathf.Cos(az) * off, 0f, Mathf.Sin(az) * off), segments, capRings, phi =>
					RockFormations.Superellipse(radius, radius * aspect, 2.2f, phi - yaw) * (1f + lump * 0.2f * RockFormations.CircleNoise(phi, 2f, partSeed)));
				for (int r = 0; r < rings; r++)
				{
					float u = r / (float)(rings - 1) * 0.94f;
					float s = 0.14f + 0.86f * Mathf.Pow(1f - u, 1.1f) + 0.15f * Mathf.Pow(1f - u, 6f);
					s *= 1f + 0.08f * ProceduralNoise.Fbm3(new Vector3(u * 3f, 0.5f, t), 2, 0.5f, partSeed + 3);
					lathe.AddRing(u * top, s, 0f);
				}
				Vector3 centre = lathe.Centre;
				lathe.WallOffset = (p, phi) =>
				{
					float u = Mathf.Clamp01(p.y / top);
					Vector3 rel = p - centre;
					float rad = new Vector2(rel.x, rel.z).magnitude;
					// Buttresses: deep flutes between ribs, cut a share of the girth, deepest low down.
					float d = -0.22f * rad * Mathf.Lerp(1f, 0.5f, u) * RockFormations.Groove(phi, p, flutes, radius, partSeed + 11);
					d += lump * 0.05f * rad * (RockFormations.CellBump(p / knob, partSeed + 13, 0.6f, 0.3f, 0.5f, 0.6f) - 0.3f);
					return d + lump * 0.03f * top * ProceduralNoise.Fbm3(p * (2f / radius), 3, 0.5f, partSeed + 7);
				};
				float tip = radius * 0.3f;
				lathe.TopRelief = p => top * 0.06f * Mathf.Clamp01(1f - new Vector2(p.x - centre.x, p.z - centre.z).magnitude / Mathf.Max(1e-3f, tip));
				MeshBuilder part = RockFormations.BuildLathe(lathe);
				if (!main)
				{
					// Turrets lean out from the spire.
					part.Transform(Matrix4x4.Translate(-centre));
					Shear(part, trng.Range(0.06f, 0.16f), az, 0f);
					part.Transform(Matrix4x4.Translate(centre));
				}
				mesh.Append(part);
			}
			return mesh;
		}

		// ── Dripstone and flowstone ──────────────────────────────────

		/// <summary>
		/// Stalagmites on a shared flowstone foot. Calcite dripstone grows as candles, slowly tapering, banded with
		/// bulges where the drip rate changed, rounded at the top round a shallow splash cup. A lava tube's dribble
		/// spires (igneous) are beads of drips frozen one on another; ice stalagmites (metamorphic: ice) are smooth cones.
		/// </summary>
		public static MeshBuilder Stalagmites(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			int count = Mathf.Max(1, shape.Count > 0 ? shape.Count : 3);
			RockFormations.LatheDetail(res, count + 1, false, out _, out _, out _, out int capRings);
			int level = RockFormations.Level(res);
			int rings = level == 2 ? 12 : level == 1 ? 7 : 4;
			int segments = Segments(res, count + 1, rings, capRings);
			float w = shape.Size * 0.5f, height = shape.Height;
			bool beaded = type.Family == RockFamily.Igneous;
			bool conical = type.Family == RockFamily.Metamorphic;
			float lump = st.Lumpiness;
			var mesh = new MeshBuilder(1);

			// The foot: a low flowstone apron joining them.
			{
				float footH = height * 0.1f;
				float aspect = rng.Range(0.75f, 0.95f);
				float yaw = rng.NextFloat() * Tau;
				int footSeed = seed + 3;
				Lathe foot = NewLathe(Vector3.zero, segments, capRings, phi =>
					RockFormations.Superellipse(w * 0.95f, w * 0.95f * aspect, 2.2f, phi - yaw) * (1f + 0.12f * RockFormations.CircleNoise(phi, 3f, footSeed)));
				int footRings = level == 2 ? 4 : 3;
				for (int r = 0; r < footRings; r++)
				{
					float u = r / (float)(footRings - 1);
					foot.AddRing(u * footH, Mathf.Sqrt(Mathf.Max(0f, 1f - 0.75f * u * u)), 0f);
				}
				foot.TopRelief = p => footH * 0.4f * ProceduralNoise.Fbm3(p * (3f / w), 2, 0.5f, footSeed + 1);
				mesh.Append(RockFormations.BuildLathe(foot));
			}

			for (int k = 0; k < count; k++)
			{
				var srng = new DeterministicRNG(seed + 701 * (k + 1));
				bool main = k == 0;
				float h = main ? height : height * srng.Range(0.35f, 0.75f);
				float r0 = conical ? (main ? w * srng.Range(0.28f, 0.34f) : w * srng.Range(0.14f, 0.24f)) : (main ? w * srng.Range(0.2f, 0.26f) : w * srng.Range(0.11f, 0.17f));
				float az = srng.NextFloat() * Tau;
				float off = main ? w * srng.Range(0f, 0.15f) : w * srng.Range(0.35f, 0.62f);
				float leanAz = srng.NextFloat() * Tau;
				float lean = srng.Range(0f, 0.06f);
				float beadPhase = srng.NextFloat();
				int partSeed = seed + 709 * (k + 1);
				var centre = new Vector3(Mathf.Cos(az) * off, 0f, Mathf.Sin(az) * off);
				float radius = r0, top = h;
				Lathe lathe = NewLathe(centre, segments, capRings, phi => radius * (1f + lump * 0.25f * RockFormations.CircleNoise(phi, 2f, partSeed)));
				for (int r = 0; r < rings; r++)
				{
					float u = r / (float)(rings - 1) * 0.95f;
					float s;
					if (conical)
					{
						s = 1f - 0.85f * Mathf.Pow(u, 0.95f);
					}
					else
					{
						// A candle: barely tapering until the rounded top.
						s = 0.55f + 0.45f * (1f - Mathf.Pow(u, 1.6f));
						s *= 1f + (beaded ? 0.3f : 0.07f) * Mathf.Max(0f, Mathf.Sin((u * (beaded ? 5.5f : 3f) + beadPhase) * Mathf.PI * 2f));
					}
					// The foot flares where the drip water spreads.
					s += 0.35f * Mathf.Pow(1f - u, 8f);
					lathe.AddRing(u * top, s, 0f);
				}
				lathe.WallOffset = (p, phi) => lump * 0.03f * radius * ProceduralNoise.Fbm3(p * (3f / radius), 2, 0.5f, partSeed + 7);
				float capR = radius * (conical ? 0.15f : 0.45f);
				lathe.TopRelief = p =>
				{
					float d = new Vector2(p.x - centre.x, p.z - centre.z).magnitude / Mathf.Max(1e-3f, capR);
					// A rounded top, with the splash cup a drop leaves in calcite.
					return capR * (conical ? 1.6f : 0.8f) * Mathf.Clamp01(1f - d * d) - (conical ? 0f : capR * 0.25f * Mathf.Clamp01(1f - 4f * d * d));
				};
				MeshBuilder part = RockFormations.BuildLathe(lathe);
				part.Transform(Matrix4x4.Translate(-centre));
				Shear(part, lean, leanAz, 0f);
				part.Transform(Matrix4x4.Translate(centre));
				mesh.Append(part);
			}
			return mesh;
		}

		/// <summary>
		/// A flowstone mound: calcite laid by water running over it, so its flanks hang in draperies (ribs running
		/// down the fall line) and are rippled across by microgours; it slumps toward the side the water leaves by.
		/// </summary>
		public static MeshBuilder Flowstone(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			RockFormations.LatheDetail(res, 1, false, out int segments, out _, out _, out _);
			int level = RockFormations.Level(res);
			int rings = level == 2 ? 10 : level == 1 ? 6 : 4;
			int capRings = level == 2 ? 4 : level == 1 ? 2 : 1;
			float w = shape.Size * 0.5f, height = shape.Height;
			float aspect = Mathf.Lerp(RockFormations.Range(rng, st.Aspect), 1f, 0.3f);
			float yaw = rng.NextFloat() * Tau;
			float flowAz = rng.NextFloat() * Tau;
			float slump = rng.Range(0.08f, 0.16f) * w;
			int outlineSeed = seed + 5;
			Lathe lathe = NewLathe(Vector3.zero, segments, capRings, phi =>
				RockFormations.Superellipse(w, w * aspect, 2.2f, phi - yaw) * (1f + 0.12f * RockFormations.CircleNoise(phi, 3f, outlineSeed)));
			float shoulder = height * 0.85f;
			for (int r = 0; r < rings; r++)
			{
				float u = r / (float)(rings - 1);
				lathe.AddRing(u * shoulder, Mathf.Max(0.3f, Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(u, 1.8f) * 0.91f))), 0f);
			}
			float rib = height * 0.12f;
			lathe.WallOffset = (p, phi) =>
			{
				float u = Mathf.Clamp01(p.y / shoulder);
				float radial = new Vector2(p.x, p.z).magnitude;
				float d = -0.07f * radial * (1f - 0.5f * u) * RockFormations.Groove(phi, p, 16, w * 0.6f, seed + 11);
				d += 0.012f * w * Mathf.Sin(p.y / rib * Tau);
				return d + slump * (1f - u) * Mathf.Cos(phi - flowAz) + 0.02f * w * ProceduralNoise.Fbm3(p * (2f / w), 3, 0.5f, seed + 7);
			};
			float domeH = height - shoulder;
			float domeR = w * 0.3f;
			lathe.TopRelief = p => domeH * Mathf.Clamp01(1f - new Vector2(p.x, p.z).sqrMagnitude / (domeR * domeR * 4f)) + 0.02f * height * ProceduralNoise.Fbm3(p * (3f / w), 2, 0.5f, seed + 9);
			return RockFormations.BuildLathe(lathe);
		}

		// ── Knobs ────────────────────────────────────────────────────

		/// <summary>
		/// A conical knob from the implicit field (the type's lumps, facets and pits on a tapering superellipsoid). With
		/// a <see cref="FormationShape.Cap"/>, a second body in submesh 1 caps its top: the same field grown a few
		/// centimetres outward and cut off below a rough line, so the cap is a crust over the knob's upper part with a
		/// patchy lower edge — the frost that refreezes on the cold tops of Callisto's knobs.
		/// </summary>
		public static MeshBuilder Knob(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			float hw = shape.Size * 0.5f, hh = shape.Height * 0.5f;
			Field field = RockFormations.BaseField(in type, in shape, rng, seed, hw, hh);
			field.TaperTop = rng.Range(0.12f, 0.24f);
			field.Exponent = Mathf.Min(field.Exponent, 2.3f);
			field.FloorY = -hh * 0.8f;
			int cube = RockFormations.CubeRes(res, in type.Style);
			bool capped = !string.IsNullOrEmpty(shape.Cap);
			MeshBuilder body = RockFormations.BuildField(field, cube);
			if (!capped)
			{
				return body;
			}
			float edge = hh * rng.Range(0.2f, 0.42f);
			Field cap = field.Clone();
			float crust = 0.006f * (shape.Size + shape.Height);
			cap.Half += Vector3.one * crust;
			cap.Cuts.Add(new RockFormations.Cut { N = Vector3.down, D = -edge, Soft = 0.02f * hh, Rough = 0.08f * hh });
			cap.Origin = new Vector3(0f, (edge + hh) * 0.5f, 0f);
			MeshBuilder capMesh = RockFormations.BuildField(cap, Mathf.Max(2, Mathf.RoundToInt(cube * 0.6f)));
			var mesh = new MeshBuilder(2);
			mesh.Append(body, 0);
			mesh.Append(capMesh, 1);
			return mesh;
		}

		// ── Ventifacts ───────────────────────────────────────────────

		/// <summary>
		/// A ventifact: a stone sand-blasted by the prevailing winds into a few flat, polished faces that meet at sharp
		/// keels — three for a dreikanter, the commonest, sometimes four — over a rounded body the wind never reached.
		/// </summary>
		public static MeshBuilder Ventifact(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockGeometryStyle st = type.Style;
			int level = RockFormations.Level(res);
			float aspect = RockFormations.Range(rng, st.Aspect);
			var half = new Vector3(shape.Size * 0.5f, shape.Height * 0.5f, shape.Size * 0.5f * aspect);
			int faces = rng.NextFloat() < 0.7f ? 3 : 4;
			float az0 = rng.NextFloat() * Tau;
			float spacing = shape.Size / (1.1f * Mathf.Max(1, res));
			// The polished faces are faintly scooped: dished rings at the finer levels, flat at the coarsest.
			int rings = level;
			return RockFormations.Polytope(() =>
			{
				var facets = new List<Facet> { RockFormations.FacetOf(Vector3.down, half.y * 0.85f) };
				for (int k = 0; k < faces; k++)
				{
					float az = az0 + Tau * k / faces + rng.Range(-0.25f, 0.25f);
					Vector3 nn = RockFormations.Tilted(rng.Range(38f, 58f), az);
					facets.Add(RockFormations.FacetOf(nn, RockFormations.Support(half, nn) * rng.Range(0.62f, 0.72f), 0.015f, 0, 0f, 0.002f));
				}
				// The unworn body: barely-cutting planes round an ellipsoid.
				Matrix4x4 spin = RockFormations.Rotation(Vector3.up, rng.NextFloat() * 360f);
				const int Body = 10;
				for (int i = 0; i < Body; i++)
				{
					float y = 1f - 2f * (i + 0.5f) / Body;
					float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
					float a = i * 2.39996323f;
					Vector3 nn = spin.MultiplyVector(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
					if (nn.y < -0.5f)
					{
						continue;
					}
					facets.Add(RockFormations.FacetOf(nn, RockFormations.Support(half, nn) * rng.Range(0.95f, 1f), 0f, 0, 0f, 0.004f));
				}
				return facets;
			}, spacing, rings, seed, half);
		}

		// ── Sastrugi ─────────────────────────────────────────────────

		/// <summary>
		/// Sastrugi: snow the wind erodes into ridges along its own direction (here −x is upwind), each with a steep,
		/// undercut prow facing the wind and a long tapering tail. Each ridge is a closed sweep of cross-sections
		/// along x: a rounded crest over a flat buried base, the prow end leaning forward over its foot.
		/// </summary>
		public static MeshBuilder Sastrugi(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count = Mathf.Max(1, shape.Count > 0 ? shape.Count : 4);
			int level = RockFormations.Level(res);
			int stations = Mathf.Max(4, Mathf.RoundToInt(1.4f * res));
			int around = Mathf.Max(4, res);
			float length = shape.Size, height = shape.Height;
			float ground = RockFormations.Burial(FormationKind.Sastrugi) * height;
			float width = length * 0.55f;
			var mesh = new MeshBuilder(1);
			for (int k = 0; k < count; k++)
			{
				var krng = new DeterministicRNG(seed + 131 * (k + 1));
				float len = length * (k == 0 ? krng.Range(0.9f, 1f) : krng.Range(0.45f, 0.85f));
				float x0 = krng.Range(-0.5f, 0.5f) * (length - len);
				float z0 = Mathf.Lerp(-width * 0.5f, width * 0.5f, (k + 0.5f) / count) + krng.Range(-0.12f, 0.12f) * width / count;
				float h = (height - ground) * (k == 0 ? 1f : krng.Range(0.4f, 0.9f));
				float wk = h * krng.Range(3f, 4.5f);
				float undercut = h * krng.Range(0.15f, 0.35f);
				float wander = 0.05f * len * krng.Range(-1f, 1f);
				float wavelength = krng.Range(0.6f, 1.4f);
				Sweep(mesh, stations, around, i =>
				{
					float u = i / (float)stations;
					float hp = h * (0.85f + 0.15f * Smooth(0f, 0.1f, u)) * Mathf.Max(0.07f, Mathf.Pow(1f - u, 0.9f));
					float wp = wk * (0.75f + 0.25f * Smooth(0f, 0.15f, u)) * Mathf.Lerp(1f, 0.35f, u);
					return new SweepStation
					{
						X = x0 - len * 0.5f + u * len,
						Z = z0 + wander * Mathf.Sin(u * Mathf.PI * wavelength),
						Height = hp,
						Width = wp,
						Lean = -undercut * (1f - Smooth(0f, 0.12f, u)),
					};
				}, ground);
			}
			mesh.RecalculateNormals(true);
			return mesh;
		}

		private struct SweepStation
		{
			public float X, Z, Height, Width;
			/// <summary>How far the crest reaches along x ahead of its base, metres (negative leans toward −x).</summary>
			public float Lean;
		}

		/// <summary>
		/// A closed ridge along x: stations bottom-flat at y = 0, sides up to <paramref name="ground"/>, a crest arc of
		/// <paramref name="around"/> segments over it; the two end sections are capped by fans. Wound outward from
		/// each station's own axis point, which lies inside every section.
		/// </summary>
		private static void Sweep(MeshBuilder mesh, int stations, int around, Func<int, SweepStation> at, float ground)
		{
			int m = around + 3;
			int first = mesh.VertexCount;
			var axis = new Vector3[stations + 1];
			float inv = 1f / RockMeshes.TextureMetres;
			for (int i = 0; i <= stations; i++)
			{
				SweepStation s = at(i);
				var ring = new Vector3[m];
				for (int j = 0; j <= around; j++)
				{
					float theta = Mathf.PI * j / around;
					// A sharp wind-cut crest over long flanks, not a round log.
					float lift = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(theta)), 1.4f);
					float y = ground + s.Height * lift;
					ring[j] = new Vector3(s.X + s.Lean * lift, y, s.Z + s.Width * 0.5f * Mathf.Cos(theta));
				}
				ring[around + 1] = new Vector3(s.X, 0f, s.Z - s.Width * 0.5f);
				ring[around + 2] = new Vector3(s.X, 0f, s.Z + s.Width * 0.5f);
				axis[i] = new Vector3(s.X + s.Lean * 0.3f, ground * 0.5f + s.Height * 0.3f, s.Z);
				float v = 0f;
				for (int j = 0; j < m; j++)
				{
					if (j > 0)
					{
						v += (ring[j] - ring[j - 1]).magnitude;
					}
					mesh.AddVertex(ring[j], Vector3.zero, new Vector2(ring[j].x, v) * inv, RockFormations.White);
				}
			}
			for (int i = 0; i < stations; i++)
			{
				for (int j = 0; j < m; j++)
				{
					int a = first + i * m + j, b = first + i * m + (j + 1) % m;
					int c = b + m, d = a + m;
					Vector3 mid = (mesh.Positions[a] + mesh.Positions[b] + mesh.Positions[c] + mesh.Positions[d]) * 0.25f;
					Vector3 facing = mid - Vector3.Lerp(axis[i], axis[i + 1], 0.5f);
					facing.x = 0f;
					mesh.AddQuad(0, a, b, c, d, facing);
				}
			}
			foreach (int i in new[] { 0, stations })
			{
				// The end faces get their own vertices, mapped flat across (z, y).
				int ringStart = mesh.VertexCount;
				Vector3 c = Vector3.zero;
				for (int j = 0; j < m; j++)
				{
					Vector3 q = mesh.Positions[first + i * m + j];
					c += q;
					mesh.AddVertex(q, Vector3.zero, new Vector2(q.z, q.y) * inv, RockFormations.White);
				}
				c /= m;
				int hub = mesh.AddVertex(c, Vector3.zero, new Vector2(c.z, c.y) * inv, RockFormations.White);
				Vector3 facing = i == 0 ? Vector3.left : Vector3.right;
				for (int j = 0; j < m; j++)
				{
					mesh.AddTriangle(0, ringStart + j, ringStart + (j + 1) % m, hub, facing);
				}
			}
		}

		// ── Penitentes ───────────────────────────────────────────────

		/// <summary>
		/// Penitentes: where the sun is high and the air dry and cold, snow sublimates instead of melting; hollows
		/// deepen faster than ridges (they trap the light), and the ridges left between them become tall thin blades in
		/// rows running east–west, leaning toward the noon sun. Here the rows run along x and the blades lean toward +z,
		/// all from a shared foot of the same snow.
		/// </summary>
		public static MeshBuilder Penitentes(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			int count = Mathf.Max(1, shape.Count > 0 ? shape.Count : 9);
			RockFormations.LatheDetail(res, count + 1, false, out int segments, out _, out _, out int capRings);
			int level = RockFormations.Level(res);
			int rings = level == 2 ? 8 : level == 1 ? 5 : 3;
			float size = shape.Size, height = shape.Height;
			float ground = RockFormations.Burial(FormationKind.Penitentes) * height;
			float lean = Mathf.Tan(rng.Range(12f, 24f) * Mathf.Deg2Rad);
			var mesh = new MeshBuilder(1);

			float footTop = ground + 0.05f * height;
			float footAspect = rng.Range(0.65f, 0.8f);
			int footSeed = seed + 3;
			Lathe foot = NewLathe(Vector3.zero, segments, capRings, phi =>
				RockFormations.Superellipse(size * 0.5f, size * 0.5f * footAspect, 2.6f, phi) * (1f + 0.08f * RockFormations.CircleNoise(phi, 3f, footSeed)));
			foot.AddRing(0f, 1f, 0f);
			foot.AddRing(footTop, 0.96f, 0f);
			foot.TopRelief = p => -0.025f * height * (ProceduralNoise.Fbm3(p * (4f / size), 2, 0.5f, footSeed + 1) + 0.5f);
			mesh.Append(RockFormations.BuildLathe(foot));

			int rows = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(count / 1.6f)));
			int cols = Mathf.CeilToInt(count / (float)rows);
			for (int i = 0; i < count; i++)
			{
				var brng = new DeterministicRNG(seed + 809 * (i + 1));
				int row = i % rows, col = i / rows;
				float x = Mathf.Lerp(-0.38f, 0.38f, cols > 1 ? col / (float)(cols - 1) : 0.5f) * size + brng.Range(-0.05f, 0.05f) * size;
				float z = Mathf.Lerp(-0.28f, 0.28f, rows > 1 ? row / (float)(rows - 1) : 0.5f) * size * footAspect + brng.Range(-0.04f, 0.04f) * size;
				float h = (height - ground) * (i == 0 ? 1f : brng.Range(0.55f, 0.95f));
				float a = h * brng.Range(0.16f, 0.24f), b = a * brng.Range(0.35f, 0.5f);
				int bladeSeed = seed + 811 * (i + 1);
				float top = ground + h;
				var centre = new Vector3(x, ground * 0.5f, z);
				Lathe blade = NewLathe(centre, Mathf.Max(8, segments / 2), 1, phi =>
					RockFormations.Superellipse(a, b, 2.2f, phi) * (1f + 0.1f * RockFormations.CircleNoise(phi, 2.5f, bladeSeed)));
				for (int r = 0; r < rings; r++)
				{
					float u = r / (float)(rings - 1);
					blade.AddRing(u * (top - centre.y), 0.07f + 0.93f * Mathf.Pow(1f - u, 0.85f), 0f);
				}
				blade.WallOffset = (p, phi) => -0.06f * a * (RockFormations.CellBump(p / Mathf.Max(0.05f, a * 0.8f), bladeSeed + 5, 0.7f, 0.35f, 0.5f, 0.8f));
				float tipR = a * 0.07f;
				blade.TopRelief = p => h * 0.04f * Mathf.Clamp01(1f - new Vector2(p.x - centre.x, p.z - centre.z).magnitude / Mathf.Max(1e-3f, tipR));
				MeshBuilder part = RockFormations.BuildLathe(blade);
				Shear(part, lean * brng.Range(0.85f, 1.15f), Mathf.PI * 0.5f + brng.Range(-0.12f, 0.12f), ground);
				mesh.Append(part);
			}
			return mesh;
		}

		// ── Lobate flow fronts ───────────────────────────────────────

		/// <summary>
		/// A lobe of a viscous flow (cryolava on an icy moon): a low tongue with a steep rounded front, bulging toward
		/// +x where it advanced, its top crossed by pressure ridges that run parallel to the front, as the crust is
		/// shoved and buckled by the flow behind it.
		/// </summary>
		public static MeshBuilder Lobe(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockFormations.LatheDetail(res, 1, false, out int segments, out _, out _, out _);
			int level = RockFormations.Level(res);
			int capRings = level == 2 ? 7 : level == 1 ? 4 : 2;
			float a = shape.Size * 0.5f, height = shape.Height;
			float b = a * rng.Range(0.55f, 0.75f);
			float ground = RockFormations.Burial(FormationKind.Lobe) * height;
			int outlineSeed = seed + 5;
			Func<float, float> outline = phi =>
				RockFormations.Superellipse(a, b, 2.3f, phi) * (1f + 0.1f * RockFormations.CircleNoise(phi, 3f, outlineSeed) + 0.12f * Mathf.Max(0f, Mathf.Cos(phi)));
			Lathe lathe = NewLathe(Vector3.zero, segments, capRings, outline);
			float rise = height - ground;
			lathe.AddRing(0f, 1f, 0f);
			lathe.AddRing(ground, 1f, 0f);
			if (level > 0)
			{
				lathe.AddRing(ground + 0.4f * rise, 0.995f, 0f);
			}
			lathe.AddRing(ground + 0.75f * rise, 0.97f, 0f);
			if (level > 1)
			{
				lathe.AddRing(ground + 0.93f * rise, 0.93f, 0f);
			}
			lathe.AddRing(height, 0.86f, 0f);
			int ridges = 3 + rng.Next(2);
			float phase = rng.NextFloat() * Tau;
			lathe.WallOffset = (p, phi) => 0.01f * a * ProceduralNoise.Fbm3(p * (3f / a), 3, 0.5f, seed + 7);
			lathe.TopRelief = p =>
			{
				float phi = Mathf.Atan2(p.z, p.x);
				float rn = new Vector2(p.x, p.z).magnitude / Mathf.Max(1e-3f, outline(phi) * 0.86f);
				float d = 0.07f * rise * Mathf.Sin(rn * Tau * ridges + phase) * Smooth(0.15f, 0.55f, rn);
				return d + 0.06f * rise * (1f - rn * rn) + 0.03f * rise * ProceduralNoise.Fbm3(p * (3f / a), 2, 0.5f, seed + 9);
			};
			return RockFormations.BuildLathe(lathe);
		}

		// ── Glacier tables ───────────────────────────────────────────

		/// <summary>
		/// A glacier table: a boulder shades the ice beneath it while the glacier around melts down, until it stands on
		/// a pedestal of ice — narrow-waisted, fluted by meltwater, thinner on the sunny side, so the table tips toward
		/// the sun before it falls. The pedestal is submesh 0 (the type, ice); the boulder submesh 1, cast from the
		/// <see cref="FormationShape.Cap"/> rock's own field (a slab-like corestone, flat underneath where it rests).
		/// </summary>
		public static MeshBuilder Table(in RockType type, in FormationShape shape, int res, int seed)
		{
			var rng = new DeterministicRNG(seed);
			RockFormations.LatheDetail(res, 2, false, out int segments, out _, out _, out int capRings);
			int level = RockFormations.Level(res);
			int rings = level == 2 ? 10 : level == 1 ? 6 : 4;
			RockType rock = !string.IsNullOrEmpty(shape.Cap) && RockTypes.TryGet(shape.Cap, out RockType capType) ? capType : type;
			float size = shape.Size, height = shape.Height;
			float boulderW = size * rng.Range(0.85f, 0.95f);
			float boulderH = height * rng.Range(0.28f, 0.36f);
			float pedestal = height - boulderH * 0.85f;
			float footR = size * 0.45f;
			float neckR = boulderW * rng.Range(0.16f, 0.22f);
			float topR = boulderW * rng.Range(0.24f, 0.3f);
			float sunAz = rng.NextFloat() * Tau;
			float aspect = rng.Range(0.8f, 1f);
			float yaw = rng.NextFloat() * Tau;
			Lathe lathe = NewLathe(Vector3.zero, segments, capRings, phi => RockFormations.Superellipse(1f, aspect, 2f, phi - yaw));
			for (int r = 0; r < rings; r++)
			{
				float t = r / (float)(rings - 1);
				float radius = neckR + (footR - neckR) * Mathf.Pow(1f - t, 3f) + (topR - neckR) * Smooth(0.7f, 1f, t);
				lathe.AddRing(t * pedestal, radius, 0f);
			}
			lathe.WallOffset = (p, phi) =>
			{
				float u = Mathf.Clamp01(p.y / pedestal);
				float radial = new Vector2(p.x, p.z).magnitude;
				// Meltwater flutes down the waist, and the sunny side wasted back.
				float d = -0.1f * radial * Smooth(0.08f, 0.4f, u) * (1f - Smooth(0.85f, 1f, u)) * RockFormations.Groove(phi, p, 11, neckR * 2f, seed + 11);
				return d - 0.18f * radial * u * (1f - Smooth(0.85f, 1f, u)) * Mathf.Max(0f, Mathf.Cos(phi - sunAz));
			};
			var mesh = new MeshBuilder(2);
			mesh.Append(RockFormations.BuildLathe(lathe), 0);

			var slab = new FormationShape { Name = shape.Name, Kind = FormationKind.Boulder, Size = boulderW, Height = boulderH, Exponent = 3f };
			Field field = RockFormations.BaseField(in rock, in slab, rng, seed + 41, boulderW * 0.5f, boulderH * 0.5f);
			field.Cuts.Add(new RockFormations.Cut { N = Vector3.down, D = boulderH * 0.5f * 0.7f, Soft = 0.06f * boulderH });
			field.FloorY = float.NegativeInfinity;
			MeshBuilder boulder = RockFormations.BuildField(field, Mathf.Max(2, Mathf.RoundToInt(res * 0.9f)));
			// Tipped toward the sun, and set on the pedestal's top.
			Vector3 tipAxis = Vector3.Cross(Vector3.up, new Vector3(Mathf.Cos(sunAz), 0f, Mathf.Sin(sunAz)));
			boulder.Transform(RockFormations.Rotation(tipAxis, rng.Range(4f, 12f)) * RockFormations.Rotation(Vector3.up, rng.NextFloat() * 360f));
			float sink = 0.04f * boulderH;
			boulder.Transform(Matrix4x4.Translate(new Vector3(0f, pedestal - boulder.Bounds.min.y - sink, 0f)));
			mesh.Append(boulder, 1);
			return mesh;
		}
	}
}
#endif
