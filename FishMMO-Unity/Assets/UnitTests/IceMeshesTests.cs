using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;
using FishMMO.Water;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The ice generator: every shape at every level is a valid, finite, closed mesh; floating ice
	/// displaces its own weight (firn-capped bergs) by its own measured volume, every level shares
	/// the finest level's hull, no berg flares into a shelf; icebergs keep the International Ice
	/// Patrol's size classes and their class's proportions and float upright; the same seed gives the
	/// same mesh; and the ice surfaces synthesise.
	/// </summary>
	[TestFixture]
	public class IceMeshesTests
	{
		private const int Seed = 20261002;

		private static IEnumerable<(string name, MeshBuilder mesh, int expectedTriangles)> EveryMesh()
		{
			for (int lod = 0; lod < IceMeshes.Resolution.Length; lod++)
			{
				int res = IceMeshes.Resolution[lod];
				int tris = 12 * res * res;
				foreach (IcebergShape s in IceMeshes.Icebergs)
				{
					yield return ($"Iceberg {s.Name} LOD{lod}", IceMeshes.BuildIceberg(in s, res, Seed).Mesh, tris);
				}
				foreach (IceBoulderShape s in IceMeshes.Boulders)
				{
					yield return ($"IceBoulder {s.Name} LOD{lod}", IceMeshes.BuildBoulder(in s, res, Seed), tris);
				}
				foreach (SeracShape s in IceMeshes.Seracs)
				{
					yield return ($"Serac {s.Name} LOD{lod}", IceMeshes.BuildSerac(in s, res, Seed), tris);
				}
				foreach (SeaIceShape s in IceMeshes.SeaIce)
				{
					yield return ($"SeaIce {s.Name} LOD{lod}", IceMeshes.BuildSeaIce(in s, res, Seed).Mesh, tris);
				}
				foreach (PressureRidgeShape s in IceMeshes.Ridges)
				{
					yield return ($"Ridge {s.Name} LOD{lod}", IceMeshes.BuildPressureRidge(in s, lod, Seed), -1);
				}
			}
		}

		// ── Every mesh ────────────────────────────────────────────────

		[Test]
		public void EveryIceShapeAtEveryLevel_ValidatesClean()
		{
			foreach (var (name, mesh, _) in EveryMesh())
			{
				List<string> problems = mesh.Validate(true);
				Assert.That(problems, Is.Empty, $"{name}: {(problems.Count > 0 ? problems[0] : "")}");
			}
		}

		[Test]
		public void EveryIceShapeAtEveryLevel_IsFinite()
		{
			foreach (var (name, mesh, _) in EveryMesh())
			{
				for (int i = 0; i < mesh.VertexCount; i++)
				{
					Vector3 p = mesh.Positions[i];
					Vector4 t = mesh.Tangents[i];
					Vector2 uv = mesh.UVs[i];
					Assert.That(float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z), $"{name} vertex {i} position");
					Assert.That(float.IsFinite(t.x) && float.IsFinite(t.y) && float.IsFinite(t.z), $"{name} vertex {i} tangent");
					Assert.That(float.IsFinite(uv.x) && float.IsFinite(uv.y), $"{name} vertex {i} uv");
				}
			}
		}

		[Test]
		public void EveryIceShapeAtEveryLevel_IsClosed()
		{
			foreach (var (name, mesh, _) in EveryMesh())
			{
				Assert.That(OpenEdge(mesh), Is.Null, name);
			}
		}

		[Test]
		public void ChartMeshes_HaveTheBouldersTriangleBudgets()
		{
			foreach (var (name, mesh, expected) in EveryMesh())
			{
				if (expected > 0)
				{
					Assert.That(mesh.TriangleCount, Is.EqualTo(expected), name);
				}
				else
				{
					Assert.That(mesh.TriangleCount, Is.LessThanOrEqualTo(1200), name);
				}
			}
			for (int lod = 0; lod < IceMeshes.Resolution.Length; lod++)
			{
				var rock = new RockShape { Name = "Round", Size = 1.6f, Proportions = new Vector3(1f, 0.75f, 0.9f), Lumpiness = 0.8f, Facets = 5, FacetDepth = 0.4f };
				MeshBuilder ice = IceMeshes.BuildBoulder(in IceMeshes.Boulders[0], IceMeshes.Resolution[lod], Seed);
				Assert.That(ice.TriangleCount, Is.EqualTo(RockMeshes.Build(in rock, IceMeshes.Resolution[lod], Seed).TriangleCount), $"LOD{lod}");
			}
		}

		/// <summary>Null when every welded directed edge is matched exactly once by its reverse: closed and consistently wound.</summary>
		private static string OpenEdge(MeshBuilder m)
		{
			var weld = new Dictionary<(long, long, long), int>();
			var id = new int[m.VertexCount];
			for (int i = 0; i < m.VertexCount; i++)
			{
				Vector3 p = m.Positions[i];
				var key = ((long)Math.Round(p.x * 1e4), (long)Math.Round(p.y * 1e4), (long)Math.Round(p.z * 1e4));
				if (!weld.TryGetValue(key, out int w))
				{
					w = weld.Count;
					weld[key] = w;
				}
				id[i] = w;
			}
			var edges = new Dictionary<(int, int), int>();
			foreach (List<int> s in m.Submeshes)
			{
				for (int t = 0; t < s.Count; t += 3)
				{
					for (int k = 0; k < 3; k++)
					{
						int a = id[s[t + k]], b = id[s[t + (k + 1) % 3]];
						if (a == b)
						{
							return $"triangle {t / 3} collapses when welded";
						}
						edges.TryGetValue((a, b), out int c);
						edges[(a, b)] = c + 1;
					}
				}
			}
			foreach (KeyValuePair<(int, int), int> e in edges)
			{
				if (e.Value != 1)
				{
					return $"an edge is used {e.Value} times in one direction";
				}
				if (!edges.TryGetValue((e.Key.Item2, e.Key.Item1), out int r) || r != 1)
				{
					return "an edge has no matching reverse";
				}
			}
			return null;
		}

		// ── Floating ──────────────────────────────────────────────────

		[Test]
		public void Hydrostatics_OfABox_AreExact()
		{
			// 10 × 4 (x × z) waterplane, 10 high, centred 3 m under the waterline: 8 m of it below.
			var box = new MeshBuilder(1);
			Vector3 size = new Vector3(10f, 10f, 4f), centre = new Vector3(0f, -3f, 0f);
			foreach (Vector3 f in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
			{
				Vector3 u = Mathf.Abs(f.y) > 0.5f ? Vector3.right : Vector3.up;
				Vector3 v = Vector3.Cross(f, u);
				int first = box.VertexCount;
				for (int k = 0; k < 4; k++)
				{
					float su = (k == 1 || k == 2) ? 1f : -1f, sv = k >= 2 ? 1f : -1f;
					box.AddVertex(centre + Vector3.Scale(f + u * su + v * sv, size * 0.5f), Vector3.zero, Vector2.zero, new Color32(255, 255, 255, 0));
				}
				box.AddQuad(0, first, first + 1, first + 2, first + 3, f);
			}
			IceHydrostatics h = IceMeshes.Measure(box);
			Assert.That(h.Volume, Is.EqualTo(400f).Within(1e-3f));
			Assert.That(h.VolumeBelow, Is.EqualTo(320f).Within(1e-3f));
			Assert.That(h.WaterplaneArea, Is.EqualTo(40f).Within(1e-4f));
			Assert.That(h.WaterplaneInertiaRoll, Is.EqualTo(10f * 64f / 12f).Within(1e-3f));
			Assert.That(h.WaterplaneInertiaPitch, Is.EqualTo(4f * 1000f / 12f).Within(1e-2f));
			Assert.That(h.CentreOfBuoyancy.y, Is.EqualTo(-4f).Within(1e-4f));
			Assert.That(h.Centroid.y, Is.EqualTo(-3f).Within(1e-4f));
			Assert.That(h.Gyration.x, Is.EqualTo(Mathf.Sqrt((100f + 16f) / 12f)).Within(1e-3f));
		}

		/// <summary>How far each level may float off its own weight: LOD0 is solved, the coarser levels reuse its hull.</summary>
		private static readonly float[] ArchimedesTolerance = { 0.015f, 0.06f, 0.25f };

		[Test]
		public void EveryIceberg_DisplacesItsOwnWeight_ExactlyAtLOD0()
		{
			foreach (IcebergShape s in IceMeshes.Icebergs)
			{
				for (int lod = 0; lod < IceMeshes.Resolution.Length; lod++)
				{
					FloatingIce ice = IceMeshes.BuildIceberg(in s, IceMeshes.Resolution[lod], Seed);
					// Measured again from the closed mesh itself, not taken from the builder's own report.
					IceHydrostatics h = IceMeshes.Measure(ice.Mesh);
					float ratio = IceMeshes.SeaWaterDensity * h.VolumeBelow / ice.Mass;
					Assert.That(ratio, Is.EqualTo(1f).Within(ArchimedesTolerance[lod]), $"{s.Name} LOD{lod}: water displaced over mass");
					Assert.That(h.VolumeAbove + h.VolumeBelow, Is.EqualTo(h.Volume).Within(h.Volume * 1e-4f), $"{s.Name} LOD{lod}");
					Assert.That(ice.Waterline, Is.EqualTo(0f));
					Assert.That(ice.Mesh.Bounds.min.y, Is.LessThan(0f), $"{s.Name}: nothing under water");
					Assert.That(ice.Mesh.Bounds.max.y, Is.GreaterThan(0f), $"{s.Name}: nothing above water");
				}
			}
		}

		[Test]
		public void EverySeaIcePiece_FloatsAtSeaIcesDensityRatio()
		{
			float share = IceMeshes.SeaIceDensity / IceMeshes.SeaWaterDensity;
			foreach (SeaIceShape s in IceMeshes.SeaIce)
			{
				for (int lod = 0; lod < IceMeshes.Resolution.Length; lod++)
				{
					IceHydrostatics h = IceMeshes.Measure(IceMeshes.BuildSeaIce(in s, IceMeshes.Resolution[lod], Seed).Mesh);
					Assert.That(h.VolumeBelow / h.Volume, Is.EqualTo(share).Within(share * ArchimedesTolerance[lod]), $"{s.Name} LOD{lod}");
				}
			}
		}

		[Test]
		public void EveryLevel_SharesTheFinestLevelsSilhouette()
		{
			foreach (IcebergShape s in IceMeshes.Icebergs)
			{
				FloatingIce fine = IceMeshes.BuildIceberg(in s, IceMeshes.Resolution[0], Seed);
				for (int lod = 1; lod < IceMeshes.Resolution.Length; lod++)
				{
					FloatingIce coarse = IceMeshes.BuildIceberg(in s, IceMeshes.Resolution[lod], Seed);
					Assert.That(coarse.Freeboard, Is.EqualTo(fine.Freeboard).Within(fine.Freeboard * 0.06f), $"{s.Name} LOD{lod} freeboard");
					Assert.That(coarse.Draught, Is.EqualTo(fine.Draught).Within(fine.Draught * 0.12f), $"{s.Name} LOD{lod} draught");
					Assert.That(coarse.Length, Is.EqualTo(fine.Length).Within(fine.Length * 0.1f), $"{s.Name} LOD{lod} length");
				}
			}
		}

		[Test]
		public void FirnDensity_RisesFromItsSurfaceToGlacialIce()
		{
			Assert.That(IceMeshes.FirnDensity(0f), Is.EqualTo(IceMeshes.FirnSurfaceDensity).Within(1e-3f));
			Assert.That(IceMeshes.FirnDensity(200f), Is.EqualTo(IceMeshes.GlacialIceDensity).Within(0.1f));
			float previous = 0f;
			for (float d = 0f; d <= 120f; d += 5f)
			{
				float rho = IceMeshes.FirnDensity(d);
				Assert.That(rho, Is.GreaterThan(previous), $"{d} m");
				previous = rho;
			}
			// The cap lightens a berg and lowers its centre of mass below the uniform body's centroid.
			FloatingIce tabular = Build(IcebergClass.Tabular, IcebergSize.Medium);
			Assert.That(tabular.Density, Is.LessThan(IceMeshes.GlacialIceDensity).And.GreaterThan(IceMeshes.FirnSurfaceDensity));
			Assert.That(tabular.CentreOfMass.y, Is.LessThan(tabular.Hydrostatics.Centroid.y));
			Assert.That(tabular.Body.GravityHeight, Is.EqualTo(tabular.CentreOfMass.y).Within(1e-4f));
		}

		[Test]
		public void EveryIceberg_IsBarelyBroaderBelowThanAbove_WithNoShelfAtTheWaterline()
		{
			foreach (IcebergShape s in IceMeshes.Icebergs)
			{
				if (s.Class == IcebergClass.Irregular)
				{
					continue; // A melt-rounded lump is widest below its waterline by its shape, not by a foot.
				}
				FloatingIce ice = IceMeshes.BuildIceberg(in s, IceMeshes.Resolution[0], Seed);
				Assert.That(ice.Breadth, Is.LessThanOrEqualTo(1.36f), $"{s.Name}: underwater breadth");
				// No shelf: in the top tenth of the draught the hull is hardly wider than the wall above water.
				float above = 0f, justBelow = 0f;
				foreach (Vector3 p in ice.Mesh.Positions)
				{
					float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
					if (p.y >= 0f && p.y <= 0.3f * ice.Freeboard) above = Mathf.Max(above, r);
					if (p.y < 0f && p.y >= -0.1f * ice.Draught) justBelow = Mathf.Max(justBelow, r);
				}
				Assert.That(justBelow, Is.LessThanOrEqualTo(above * 1.1f), $"{s.Name}: a shelf just under the water");
			}
		}

		[Test]
		public void TheDraught_FollowsTheVolume_NotAFixedNinth()
		{
			// A pinnacle is mostly air above water, a tabular berg a prism: their draughts differ by times.
			FloatingIce tabular = Build(IcebergClass.Tabular, IcebergSize.Medium);
			FloatingIce pinnacle = Build(IcebergClass.Pinnacle, IcebergSize.Medium);
			float tabularRatio = tabular.Draught / tabular.Freeboard, pinnacleRatio = pinnacle.Draught / pinnacle.Freeboard;
			Assert.That(tabularRatio, Is.GreaterThan(3f));
			Assert.That(pinnacleRatio, Is.LessThan(2f));
			Assert.That(tabularRatio / pinnacleRatio, Is.GreaterThan(2f));
		}

		[Test]
		public void EveryIceberg_FloatsUpright()
		{
			// The finest level's body is what WaterFloater is given; it must be stable as built.
			foreach (IcebergShape s in IceMeshes.Icebergs)
			{
				FloatingIce ice = IceMeshes.BuildIceberg(in s, IceMeshes.Resolution[0], Seed);
				Assert.That(ice.Body.MetacentricHeightRoll, Is.GreaterThan(0f), $"{s.Name} roll");
				Assert.That(ice.Body.MetacentricHeightPitch, Is.GreaterThan(0f), $"{s.Name} pitch");
				Assert.That(ice.Body.Mass, Is.EqualTo(ice.Mass).Within(1f), $"{s.Name}: the body carries the integrated mass");
			}
		}

		// ── The patrol's classes ──────────────────────────────────────

		private static FloatingIce Build(IcebergClass c, IcebergSize size)
		{
			foreach (IcebergShape s in IceMeshes.Icebergs)
			{
				if (s.Class == c && s.Size == size)
				{
					return IceMeshes.BuildIceberg(in s, IceMeshes.Resolution[0], Seed);
				}
			}
			Assert.Fail($"no {c} {size} in the catalogue");
			return null;
		}

		[Test]
		public void EveryIceberg_FitsItsIIPSizeClass()
		{
			foreach (IcebergShape s in IceMeshes.Icebergs)
			{
				FloatingIce ice = IceMeshes.BuildIceberg(in s, IceMeshes.Resolution[0], Seed);
				IcebergSizeRange r = IceMeshes.SizeRange(s.Size);
				Assert.That(r.Contains(ice.Freeboard, ice.Length), $"{s.Name}: {ice.Freeboard:F1} m high, {ice.Length:F1} m long is not {s.Size}");
			}
		}

		[Test]
		public void EveryIIPShapeClassAndSize_IsInTheCatalogue()
		{
			var classes = new HashSet<IcebergClass>();
			var sizes = new HashSet<IcebergSize>();
			foreach (IcebergShape s in IceMeshes.Icebergs)
			{
				classes.Add(s.Class);
				sizes.Add(s.Size);
			}
			foreach (IcebergClass c in Enum.GetValues(typeof(IcebergClass)))
			{
				Assert.That(classes, Does.Contain(c));
			}
			foreach (IcebergSize z in Enum.GetValues(typeof(IcebergSize)))
			{
				Assert.That(sizes, Does.Contain(z));
			}
		}

		/// <summary>Highest point above water among vertices whose x lies in a band of the half-length (signed: one end only).</summary>
		private static float Profile(FloatingIce ice, float from, float to, int sign = 0)
		{
			float hl = ice.Length * 0.5f, best = 0f;
			foreach (Vector3 p in ice.Mesh.Positions)
			{
				float x = sign == 0 ? Mathf.Abs(p.x) : p.x * sign;
				if (x >= from * hl && x <= to * hl)
				{
					best = Mathf.Max(best, p.y);
				}
			}
			return best / ice.Freeboard;
		}

		[Test]
		public void IcebergClasses_HaveTheirIIPProportions()
		{
			foreach (IcebergShape s in IceMeshes.Icebergs)
			{
				FloatingIce ice = IceMeshes.BuildIceberg(in s, IceMeshes.Resolution[0], Seed);
				float ratio = ice.Length / ice.Freeboard;
				switch (s.Class)
				{
					case IcebergClass.Tabular:
						Assert.That(ratio, Is.GreaterThan(5f), $"{s.Name}: tabular is over 5:1");
						Assert.That(Profile(ice, 0f, 0.8f), Is.GreaterThan(0.95f), $"{s.Name}: flat top");
						Assert.That(Profile(ice, 0.5f, 0.85f), Is.GreaterThan(0.93f), $"{s.Name}: flat to the edge");
						break;
					case IcebergClass.Blocky:
						Assert.That(ratio, Is.LessThan(5f).And.GreaterThan(1.5f), $"{s.Name}: blocky is under 5:1");
						Assert.That(Profile(ice, 0.5f, 0.85f), Is.GreaterThan(0.88f), $"{s.Name}: flat top");
						break;
					case IcebergClass.Dome:
						Assert.That(Profile(ice, 0f, 0.15f), Is.GreaterThan(0.95f), $"{s.Name}: crown in the middle");
						Assert.That(Profile(ice, 0.85f, 1.1f), Is.LessThan(0.6f), $"{s.Name}: falls away to the edge");
						break;
					case IcebergClass.Pinnacle:
						Assert.That(ice.Freeboard / ice.Length, Is.GreaterThan(0.3f), $"{s.Name}: tall");
						Assert.That(Profile(ice, 0f, 0.15f), Is.GreaterThan(0.9f), $"{s.Name}: central spire");
						Assert.That(Profile(ice, 0.55f, 1.1f), Is.LessThan(0.75f), $"{s.Name}: spire, not a block");
						break;
					case IcebergClass.Wedge:
						Assert.That(Profile(ice, 0.8f, 1.1f, 1), Is.GreaterThan(0.9f), $"{s.Name}: steep high end");
						Assert.That(Profile(ice, 0.8f, 1.1f, -1), Is.LessThan(0.35f), $"{s.Name}: low end");
						break;
					case IcebergClass.Drydock:
						float slot = Profile(ice, 0f, 0.12f);
						Assert.That(slot, Is.LessThan(0.3f).And.GreaterThan(0f), $"{s.Name}: slot to near the waterline");
						Assert.That(Profile(ice, 0.55f, 0.9f, 1), Is.GreaterThan(0.7f), $"{s.Name}: one column");
						Assert.That(Profile(ice, 0.55f, 0.9f, -1), Is.GreaterThan(0.7f), $"{s.Name}: the other column");
						break;
					case IcebergClass.Irregular:
						Assert.That(s.Size == IcebergSize.Growler || s.Size == IcebergSize.BergyBit, $"{s.Name}: irregular is for growlers and bergy bits");
						break;
				}
			}
		}

		// ── Determinism ───────────────────────────────────────────────

		private static void AssertSame(MeshBuilder a, MeshBuilder b, string name)
		{
			Assert.That(a.VertexCount, Is.EqualTo(b.VertexCount), name);
			for (int i = 0; i < a.VertexCount; i++)
			{
				Assert.That(a.Positions[i] == b.Positions[i] && a.Positions[i].x == b.Positions[i].x && a.Positions[i].y == b.Positions[i].y && a.Positions[i].z == b.Positions[i].z, $"{name} vertex {i}");
			}
		}

		private static bool Differ(MeshBuilder a, MeshBuilder b)
		{
			if (a.VertexCount != b.VertexCount)
			{
				return true;
			}
			for (int i = 0; i < a.VertexCount; i++)
			{
				if (a.Positions[i].x != b.Positions[i].x || a.Positions[i].y != b.Positions[i].y || a.Positions[i].z != b.Positions[i].z)
				{
					return true;
				}
			}
			return false;
		}

		[Test]
		public void TheSameSeed_GivesTheSameIce_AndAnotherSeedAnother()
		{
			var builds = new (string name, Func<int, MeshBuilder> build)[]
			{
				("pinnacle", seed => IceMeshes.BuildIceberg(in IceMeshes.Icebergs[11], 10, seed).Mesh),
				("growler", seed => IceMeshes.BuildIceberg(in IceMeshes.Icebergs[0], 10, seed).Mesh),
				("boulder", seed => IceMeshes.BuildBoulder(in IceMeshes.Boulders[0], 10, seed)),
				("serac", seed => IceMeshes.BuildSerac(in IceMeshes.Seracs[0], 10, seed)),
				("floe", seed => IceMeshes.BuildSeaIce(in IceMeshes.SeaIce[3], 10, seed).Mesh),
				("ridge", seed => IceMeshes.BuildPressureRidge(in IceMeshes.Ridges[0], 0, seed)),
			};
			foreach (var (name, build) in builds)
			{
				AssertSame(build(Seed), build(Seed), name);
				Assert.That(Differ(build(Seed), build(Seed + 1)), name + ": another seed is another variant");
			}
		}

		// ── Surfaces ──────────────────────────────────────────────────

		[Test]
		public void EveryIceSurface_Synthesises_AndHasAMaterialProposal()
		{
			var names = new HashSet<string>();
			foreach (SurfaceRecipe r in IceSurfaces.Recipes)
			{
				Assert.That(names.Add(r.Name), $"{r.Name} twice");
				Assert.That(SurfaceCatalogue.TryGround(r.Name, out _), Is.False, $"{r.Name} collides with a ground family");
				SurfaceMaps maps = SurfaceSynth.Generate(in r, 64, Seed);
				foreach (float h in maps.Height)
				{
					Assert.That(float.IsFinite(h), r.Name);
				}
				Assert.That(Array.Exists(IceSurfaces.Materials, m => m.Surface == r.Name), $"{r.Name} has no material proposal");
				Assert.That(IceSurfaces.TryGet(r.Name, out SurfaceRecipe found) && found.Name == r.Name);
			}
			// Dense blue ice is darker and bluer than bubbly white ice, and smoother.
			IceSurfaces.TryGet(IceSurfaces.GlacialBlue, out SurfaceRecipe blue);
			IceSurfaces.TryGet(IceSurfaces.BubblyWhite, out SurfaceRecipe white);
			Assert.That(blue.Mid.b - blue.Mid.r, Is.GreaterThan(white.Mid.b - white.Mid.r));
			Assert.That(blue.Mid.grayscale, Is.LessThan(white.Mid.grayscale));
			Assert.That(blue.Smoothness, Is.GreaterThan(white.Smoothness));
		}
	}
}
