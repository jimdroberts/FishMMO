using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Carved caves (<see cref="CaveSolid"/>, <see cref="CaveShaper"/>) on a synthetic escarpment: the tunnel stays under
	/// three metres of rock past its mouth, its shell is a closed valid mesh within budget, the terrain is holed only at the
	/// mouth, the floor can be walked to the chamber, and the same seed gives the same mesh. Then the overhang slab
	/// (<see cref="OverhangPlacer"/>), the shelter and arch plans (<see cref="OverhangShaper"/>), and FallLedges' output
	/// pinned against its own code from before the slab was moved out of it.
	/// </summary>
	[TestFixture]
	public class PointOfInterestCaveTests
	{
		/// <summary>The terrain's hole grid at 1200 m tiles and 512 holes a tile: 2.34 m quads from the scene's corner.</summary>
		private const float HoleStep = 1200f / 512f, GridX = -2400f, GridZ = -2400f;

		/// <summary>A foot at 10 m, a face rising 60 m over 30 m (about 63° at its steepest), a rolling plateau behind; turned by <paramref name="tiltDegrees"/>.</summary>
		private static Func<float, float, float> Escarpment(float tiltDegrees, int variant)
		{
			float a = tiltDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			return (x, z) =>
			{
				float u = c * z - s * x, w = s * z + c * x;
				float t = Mathf.Clamp01(u / 30f);
				float face = 10f + 60f * (t * t * (3f - 2f * t));
				float roll = 3f * Mathf.Sin(w * 0.05f + variant) + 2f * Mathf.Sin(u * 0.031f + 0.7f * variant) * Mathf.Clamp01(u / 40f);
				return face + roll * Mathf.Clamp01((u + 10f) / 30f) - 0.04f * Mathf.Max(0f, -u);
			};
		}

		/// <summary>A sea cliff: sea floor at −1.5 m past z −12, a beach rising to 1 m at z −2, a cliff to 40 m by z 25.</summary>
		private static Func<float, float, float> SeaCliff()
			=> (x, z) =>
			{
				float t = Mathf.Clamp01(z / 25f);
				return -1.5f + 2.5f * Mathf.Clamp01((z + 12f) / 10f) + 39f * (t * t * (3f - 2f * t)) + 0.8f * Mathf.Sin(x * 0.07f) * Mathf.Clamp01(z / 10f);
			};

		/// <summary>The face of <see cref="Escarpment"/> turned by <paramref name="tilt"/> faces this way (out of the slope).</summary>
		private static float YawOut(float tilt) => Mathf.Repeat(180f - tilt, 360f);

		private static CaveGround Patch(Func<float, float, float> g, float reach = 170f) => CaveGround.Sample(g, -reach, -reach, reach, reach, 1f);

		private static CaveSolid PlanCave(CaveForm form, int size, int seed, out Func<float, float, float> g)
		{
			float tilt = seed * 13f % 40f - 20f;
			g = form == CaveForm.SeaCave ? SeaCliff() : Escarpment(tilt, seed);
			var mouth = new Vector3(0f, 0f, form == CaveForm.SeaCave ? -4f : -1f);
			float yaw = form == CaveForm.SeaCave ? 180f : YawOut(tilt);
			CaveSolid solid = CaveSolid.Plan(form, size, mouth, yaw, seed * 7919, Patch(g), 0f, out string problem);
			Assert.That(solid, Is.Not.Null, $"{form} size {size} seed {seed} was refused: {problem}");
			return solid;
		}

		private static MeshBuilder[] Mesh(CaveSolid solid, Func<float, float, float> g)
		{
			Bounds e = solid.Extent;
			CaveGround ground = CaveGround.Sample(g, solid.Origin.x + e.min.x - 4f, solid.Origin.z + e.min.z - 4f,
				solid.Origin.x + e.max.x + 4f, solid.Origin.z + e.max.z + 4f, HoleStep, GridX, GridZ);
			return solid.BuildMeshes(ground, out _);
		}

		/// <summary>The roof's height over a point: marched up through the noisy void to the rock.</summary>
		private static float RoofOver(CaveSolid solid, Vector3 p)
		{
			float y = p.y;
			while (solid.Void(new Vector3(p.x, y, p.z)) < 0f && y < p.y + 60f)
			{
				y += 0.05f;
			}
			return y;
		}

		/// <summary>The floor under a point: marched down through the void to the rock.</summary>
		private static float FloorUnder(CaveSolid solid, Vector3 p)
		{
			float y = p.y;
			while (solid.Void(new Vector3(p.x, y, p.z)) < 0f && y > p.y - 60f)
			{
				y -= 0.02f;
			}
			return y;
		}

		private static readonly (CaveForm Form, int Size)[] Shapes =
		{
			(CaveForm.Cave, 0), (CaveForm.Cave, 2), (CaveForm.Grotto, 1), (CaveForm.IceCave, 1), (CaveForm.LavaTube, 1), (CaveForm.SeaCave, 0),
		};

		// ── The tunnel ────────────────────────────────────────────

		[Test]
		public void TheTunnelStaysUnderThreeMetresOfRockPastItsMouth()
		{
			foreach ((CaveForm form, int size) in Shapes)
			{
				for (int seed = 1; seed <= 3; seed++)
				{
					if (form == CaveForm.SeaCave && seed > 2)
					{
						continue;
					}
					CaveSolid solid = PlanCave(form, size, seed, out Func<float, float, float> g);
					var probes = new List<Vector3>();
					foreach (CaveNode node in solid.Nodes)
					{
						if (node.Along < solid.MouthZone)
						{
							continue;
						}
						for (int k = -2; k <= 2; k++)
						{
							probes.Add(node.Centre + new Vector3(k * 0.35f * node.Radius, 0f, 0f));
						}
					}
					probes.Add(solid.ChamberCentre);
					foreach (Vector3 p in probes)
					{
						if (solid.Void(p) >= 0f)
						{
							continue;
						}
						float ground = g(p.x + solid.Origin.x, p.z + solid.Origin.z) - solid.Origin.y;
						Assert.That(ground - RoofOver(solid, p), Is.GreaterThanOrEqualTo(CaveSolid.MinCover - 0.05f),
							$"{form} {size} seed {seed}: thin roof at {p}");
					}
				}
			}
		}

		[Test]
		public void TheShellIsAClosedValidMeshWithinItsBudget()
		{
			foreach ((CaveForm form, int size) in Shapes)
			{
				CaveSolid solid = PlanCave(form, size, 1, out Func<float, float, float> g);
				solid.HoleCells(g, HoleStep, GridX, GridZ);
				MeshBuilder[] levels = Mesh(solid, g);
				int[] budget = CaveSolid.LodTriangles(form, size);
				for (int l = 0; l < levels.Length; l++)
				{
					List<string> problems = levels[l].Validate(true);
					Assert.That(problems, Is.Empty, $"{form} {size} LOD{l}: {string.Join("; ", problems)}");
					Assert.That(OpenEdges(levels[l]), Is.Zero, $"{form} {size} LOD{l} has edges not on exactly two faces");
					Assert.That(levels[l].TriangleCount, Is.LessThanOrEqualTo(budget[l]), $"{form} {size} LOD{l}");
				}
				Assert.That(budget[0], Is.LessThanOrEqualTo(size >= 2 && form != CaveForm.Grotto ? 6000 : size == 0 || form == CaveForm.Grotto ? 2500 : 4000));
			}
		}

		/// <summary>Edges not on exactly two faces, vertices welded by position (crease copies share one exactly).</summary>
		private static int OpenEdges(MeshBuilder m)
		{
			var weld = new Dictionary<Vector3, int>();
			var use = new Dictionary<(int, int), int>();
			List<int> t = m.Submeshes[0];
			int Id(int v)
			{
				if (!weld.TryGetValue(m.Positions[v], out int id))
				{
					weld[m.Positions[v]] = id = weld.Count;
				}
				return id;
			}
			for (int i = 0; i < t.Count; i += 3)
			{
				int a = Id(t[i]), b = Id(t[i + 1]), c = Id(t[i + 2]);
				foreach ((int x, int y) in new[] { (a, b), (b, c), (c, a) })
				{
					var key = x < y ? (x, y) : (y, x);
					use[key] = use.TryGetValue(key, out int n) ? n + 1 : 1;
				}
			}
			int open = 0;
			foreach (int n in use.Values)
			{
				if (n != 2)
				{
					open++;
				}
			}
			return open;
		}

		[Test]
		public void TheInnerWallFacesIntoTheVoid()
		{
			CaveSolid solid = PlanCave(CaveForm.Cave, 1, 2, out Func<float, float, float> g);
			solid.HoleCells(g, HoleStep, GridX, GridZ);
			MeshBuilder lod0 = Mesh(solid, g)[0];
			int inner = 0, facing = 0;
			for (int i = 0; i < lod0.VertexCount; i++)
			{
				Vector3 p = lod0.Positions[i];
				// A vertex on the tunnel's own wall (not the buried outer face or the lip): its normal points into the void.
				if (Mathf.Abs(solid.Void(p)) > 0.05f || p.y > g(p.x + solid.Origin.x, p.z + solid.Origin.z) - solid.Origin.y - 1f)
				{
					continue;
				}
				inner++;
				if (solid.Void(p + lod0.Normals[i] * 0.3f) < solid.Void(p))
				{
					facing++;
				}
			}
			Assert.That(inner, Is.GreaterThan(100));
			Assert.That(facing, Is.GreaterThanOrEqualTo((int)(0.97f * inner)), $"{facing} of {inner} wall normals point into the cave");
		}

		[Test]
		public void TheTerrainIsHoledOnlyAtTheMouth()
		{
			foreach ((CaveForm form, int size) in Shapes)
			{
				CaveSolid solid = PlanCave(form, size, 2, out Func<float, float, float> g);
				List<Vector2Int> cells = solid.HoleCells(g, HoleStep, GridX, GridZ);
				Assert.That(cells.Count, Is.GreaterThan(0), $"{form} {size}: the mouth opens no hole");
				float reach = solid.MouthZone + solid.Radius + 2f * HoleStep;
				foreach (Vector2Int c in cells)
				{
					float x = GridX + (c.x + 0.5f) * HoleStep - solid.Origin.x, z = GridZ + (c.y + 0.5f) * HoleStep - solid.Origin.z;
					Assert.That(Mathf.Sqrt(x * x + z * z), Is.LessThanOrEqualTo(reach), $"{form} {size}: a hole at ({x:0}, {z:0}) from the mouth");
				}
				// The shell reaches every holed quad's far corner, so no hole's edge looks into nothing.
				Assert.That(solid.Collar, Is.GreaterThan(0f));
				Assert.That(solid.ShellMouth, Is.GreaterThan(solid.Collar));
			}
		}

		[Test]
		public void TheFloorCanBeWalkedToTheChamber()
		{
			foreach ((CaveForm form, int size) in Shapes)
			{
				CaveSolid solid = PlanCave(form, size, form == CaveForm.SeaCave ? 1 : 3 - size % 2, out _);
				var path = new List<Vector3>();
				for (int i = 1; i < solid.Nodes.Count; i++)
				{
					Vector3 a = solid.Nodes[i - 1].Centre, b = solid.Nodes[i].Centre;
					int steps = Mathf.CeilToInt(Vector3.Distance(a, b) / 0.5f);
					for (int s = 0; s < steps; s++)
					{
						path.Add(Vector3.Lerp(a, b, s / (float)steps));
					}
				}
				Vector3 room = new Vector3(solid.ChamberCentre.x, solid.ChamberFloor + 1.5f, solid.ChamberCentre.z);
				Vector3 last = solid.Nodes[solid.Nodes.Count - 1].Centre;
				for (int s = 0; s <= 8; s++)
				{
					path.Add(Vector3.Lerp(last, room, s / 8f));
				}
				float previousFloor = float.NaN;
				Vector3 previous = default;
				foreach (Vector3 p in path)
				{
					float floor = FloorUnder(solid, p);
					float roof = RoofOver(solid, new Vector3(p.x, floor + 0.1f, p.z));
					Assert.That(roof - floor, Is.GreaterThanOrEqualTo(2.2f), $"{form} {size}: headroom at {p}");
					if (!float.IsNaN(previousFloor))
					{
						float run = new Vector2(p.x - previous.x, p.z - previous.z).magnitude;
						if (run > 0.2f)
						{
							float slope = Mathf.Atan2(Mathf.Abs(floor - previousFloor), run) * Mathf.Rad2Deg;
							Assert.That(slope, Is.LessThanOrEqualTo(35f), $"{form} {size}: the floor at {p} is too steep to walk");
						}
					}
					previousFloor = floor;
					previous = p;
				}
				Assert.That(solid.ChamberRadii.x / solid.Nodes[solid.Nodes.Count - 1].Radius, Is.InRange(1.4f, 2.6f), $"{form} {size}: chamber size");
			}
		}

		// ── Determinism and storage ───────────────────────────────

		[Test]
		public void TheSameSeedGivesTheSameMesh()
		{
			CaveSolid a = PlanCave(CaveForm.Cave, 0, 4, out Func<float, float, float> g);
			CaveSolid b = PlanCave(CaveForm.Cave, 0, 4, out _);
			a.HoleCells(g, HoleStep, GridX, GridZ);
			b.HoleCells(g, HoleStep, GridX, GridZ);
			Assert.That(CaveSolid.Hash(Mesh(a, g)[0]), Is.EqualTo(CaveSolid.Hash(Mesh(b, g)[0])));
			CaveSolid c = PlanCave(CaveForm.Cave, 0, 5, out Func<float, float, float> g5);
			c.HoleCells(g5, HoleStep, GridX, GridZ);
			Assert.That(CaveSolid.Hash(Mesh(c, g5)[0]), Is.Not.EqualTo(CaveSolid.Hash(Mesh(a, g)[0])), "another seed, another cave");
		}

		[Test]
		public void AStoredCaveBuildsTheSameMeshAgain()
		{
			CaveSolid solid = PlanCave(CaveForm.LavaTube, 0, 2, out Func<float, float, float> g);
			solid.HoleCells(g, HoleStep, GridX, GridZ);
			CaveSolid back = CaveSolid.FromValues(solid.ToValues(), solid.Origin, solid.Seed);
			Assert.That(back, Is.Not.Null);
			Assert.That(back.Nodes.Count, Is.EqualTo(solid.Nodes.Count));
			Assert.That(back.ChamberFloorWorld, Is.EqualTo(solid.ChamberFloorWorld));
			Assert.That(CaveSolid.Hash(Mesh(back, g)[0]), Is.EqualTo(CaveSolid.Hash(Mesh(solid, g)[0])), "a repaint rebuilds the cave it planned");
		}

		[Test]
		public void FlatGroundAndAHighSeaFootAreRefused()
		{
			Func<float, float, float> flat = (x, z) => 5f;
			Assert.That(CaveSolid.Plan(CaveForm.Cave, 1, Vector3.zero, 180f, 11, Patch(flat), 0f, out string problem), Is.Null);
			Assert.That(problem, Is.Not.Empty);
			// A sea cave whose face's foot stands 10 m over the sea.
			Func<float, float, float> high = (x, z) => 10f + 40f * Mathf.Clamp01(z / 25f);
			Assert.That(CaveSolid.Plan(CaveForm.SeaCave, 0, new Vector3(0f, 0f, -2f), 180f, 11, Patch(high), 0f, out problem), Is.Null);
		}

		[Test]
		public void ASeaCaveFloorLiesJustUnderTheSea()
		{
			CaveSolid solid = PlanCave(CaveForm.SeaCave, 0, 1, out _);
			Assert.That(solid.Origin.y, Is.InRange(-0.6f, 0f));
			foreach (CaveNode node in solid.Nodes)
			{
				if (node.Along <= solid.MouthZone)
				{
					Assert.That(solid.Origin.y + node.Floor, Is.InRange(-1.2f, 0.3f), "awash through its mouth");
				}
			}
		}

		[Test]
		public void EveryCaveKindHasAShaperAndArchesAndOverhangsTheirs()
		{
			PointOfInterestTerrainShapers.Register();
			foreach (POIType kind in new[] { POIType.Cave, POIType.Grotto, POIType.SeaCave, POIType.IceCave, POIType.LavaTube })
			{
				Assert.That(CaveShaper.Instance.Handles(kind), Is.True, kind.ToString());
				Assert.That(PointOfInterestShapers.For(kind), Is.SameAs(CaveShaper.Instance), $"{kind} is registered");
			}
			Assert.That(PointOfInterestShapers.For(POIType.Overhang), Is.SameAs(OverhangShaper.Instance));
			Assert.That(PointOfInterestShapers.For(POIType.NaturalArch), Is.SameAs(OverhangShaper.Instance));
			Assert.That(CaveShaper.Instance.Handles(POIType.Village), Is.False);
		}

		// ── Overhangs and arches ──────────────────────────────────

		[Test]
		public void AShelterSlabIsTheSameForASeedAndStandsOverItsFoot()
		{
			for (int size = 0; size <= 2; size++)
			{
				for (int seed = 1; seed <= 6; seed++)
				{
					float tilt = seed * 7f - 20f;
					Func<float, float, float> g = Escarpment(tilt, seed);
					var foot = new Vector3(0f, 0f, -1f);
					Assert.That(OverhangPlacer.PlanAt(foot, YawOut(tilt), size, g, seed, out OverhangSlab a, out string problem), Is.True, problem);
					Assert.That(OverhangPlacer.PlanAt(foot, YawOut(tilt), size, g, seed, out OverhangSlab b, out _), Is.True);
					Assert.That((b.Middle, b.Size, b.Yaw, b.AlcoveFront, b.AlcoveBack), Is.EqualTo((a.Middle, a.Size, a.Yaw, a.AlcoveFront, a.AlcoveBack)));
					float underside = a.Middle.y - 0.5f * a.Size.y;
					Assert.That(underside - a.Foot.y, Is.GreaterThanOrEqualTo(OverhangPlacer.ClearanceFor(size) - 1e-3f), "headroom under the slab");
					Assert.That(a.Size.x, Is.InRange(3f, 10f), "3–10 m across");
					Assert.That(a.AlcoveFront, Is.GreaterThan(a.AlcoveBack));
					// Its back is in the slope: the ground there stands over its top.
					float yr = a.Yaw * Mathf.Deg2Rad;
					var outward = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
					Vector3 back = a.Middle - outward * (0.5f * a.Size.z);
					Assert.That(g(back.x, back.z), Is.GreaterThanOrEqualTo(a.Middle.y + 0.5f * a.Size.y - 1e-3f), "the slab's back is buried");
				}
			}
		}

		[Test]
		public void AShelterNeedsAFace()
		{
			Func<float, float, float> flat = (x, z) => 3f;
			Assert.That(OverhangPlacer.PlanAt(Vector3.zero, 0f, 1, flat, 3, out _, out string problem), Is.False);
			Assert.That(problem, Is.Not.Empty);
		}

		[Test]
		public void OverhangsAndArchesPlanTheirBuildDeterministically()
		{
			for (int seed = 1; seed <= 6; seed++)
			{
				float tilt = seed * 7f - 20f;
				Func<float, float, float> g = Escarpment(tilt, seed);
				var foot = new Vector3(0f, 0f, -1f);
				Assert.That(OverhangShaper.PlanSite(POIType.Overhang, 0, foot, YawOut(tilt), seed, g, out OverhangShaper.Plan small, out string problem), Is.True, problem);
				Assert.That(small.Build, Is.EqualTo(OverhangBuild.Slab));
				Assert.That(OverhangShaper.PlanSite(POIType.Overhang, 2, foot, YawOut(tilt), seed, g, out OverhangShaper.Plan large, out problem), Is.True, problem);
				Assert.That(large.Build, Is.EqualTo(OverhangBuild.Section), "a large overhang is the leaning section");
				Assert.That(OverhangShaper.PlanSite(POIType.NaturalArch, 1, foot, YawOut(tilt), seed, g, out OverhangShaper.Plan arch, out problem), Is.True, problem);
				Assert.That(arch.Build, Is.EqualTo(OverhangBuild.Arch));
				Assert.That(arch.Variant, Is.InRange(0, CliffSections.VariantCount - 1));
				// The arch's length (its local x) runs out of the face.
				Vector3 x = Quaternion.Euler(0f, arch.Yaw, 0f) * Vector3.right;
				float yr = YawOut(tilt) * Mathf.Deg2Rad;
				Assert.That(Vector3.Dot(x, new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr))), Is.GreaterThan(0.999f));
				// The leaning section's front (its −z) faces out.
				Vector3 front = Quaternion.Euler(0f, large.Yaw, 0f) * Vector3.back;
				Assert.That(Vector3.Dot(front, new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr))), Is.GreaterThan(0.999f));
				// Stored and read back unchanged.
				Assert.That(OverhangShaper.Plan.TryFrom(small.ToValues(), out OverhangShaper.Plan back), Is.True);
				Assert.That((back.Build, back.Slab.Middle, back.Slab.Size), Is.EqualTo((small.Build, small.Slab.Middle, small.Slab.Size)));
				OverhangShaper.PlanSite(POIType.Overhang, 0, foot, YawOut(tilt), seed, g, out OverhangShaper.Plan again, out _);
				Assert.That(again.Slab.Middle, Is.EqualTo(small.Slab.Middle));
			}
		}

		[Test]
		public void TheShelterAndArchSectionsAreCatalogued()
		{
			Assert.That(Array.IndexOf(CliffSections.Styles, "Overhang"), Is.GreaterThanOrEqualTo(0));
			Assert.That(Array.IndexOf(CliffSections.Styles, "Arch"), Is.GreaterThanOrEqualTo(0));
			Assert.That(CliffSections.StyleOf("Overhang").Lean, Is.GreaterThanOrEqualTo(3f), "the shelter leans well out");
			Assert.That(CliffSections.StyleOf("Arch").ArchChance, Is.EqualTo(1f), "every arch section has its arch");
			Assert.That(CliffSections.IsPointOfInterest("Overhang") && CliffSections.IsPointOfInterest("Arch") && !CliffSections.IsPointOfInterest("Wall"), Is.True);
			// No cliff role places them: they are a point of interest's alone.
			foreach (CliffRole role in (CliffRole[])Enum.GetValues(typeof(CliffRole)))
			{
				foreach (CliffShape shape in CliffRocks.FaceShapes(role))
				{
					Assert.That(CliffSections.IsPointOfInterest(shape.Section), Is.False, $"{role} places {shape.Section}");
				}
			}
		}

		[Test]
		public void TheArchOpensThroughTheFinsMiddle()
		{
			for (int v = 0; v < CliffSections.VariantCount; v++)
			{
				CliffSection solid = CliffSections.Solid("Arch", v, ProceduralArtCatalogue.DefaultSeed);
				for (float y = 0.5f; y <= 4f; y += 0.5f)
				{
					for (float z = -6f; z <= 6f; z += 0.25f)
					{
						Assert.That(solid.EvaluateLow(new Vector3(0f, y, z)), Is.GreaterThan(0f), $"Arch {v}: rock in the arch at y {y}, z {z}");
					}
				}
			}
		}

		// ── FallLedges, before and after ──────────────────────────

		[Test]
		public void TheSlabDrawIsFallLedgesOwnArithmetic()
		{
			var settings = new FallLedgeSettings();
			for (int seed = 0; seed < 400; seed++)
			{
				var r = new System.Random(seed);
				float faceHeight = 0.5f + 60f * (float)r.NextDouble(), width = 1f + 30f * (float)r.NextDouble(), side = 0.5f * width + settings.SideSlackMetres;
				var x = new System.Random(seed * 31 + 7);
				var y = new System.Random(seed * 31 + 7);
				// As FallLedges wrote it before the slab moved into OverhangPlacer.
				float reach = Mathf.Lerp(settings.MinOverhang, Mathf.Clamp(0.3f * faceHeight, settings.MinOverhang, settings.MaxOverhang), (float)x.NextDouble());
				float back = reach * Mathf.Lerp(1.2f, 2f, (float)x.NextDouble());
				float thickness = Mathf.Clamp(0.15f * faceHeight, 0.6f, 2f);
				thickness = Mathf.Min(thickness, faceHeight - settings.PoolClearMetres - 0.05f);
				float across = Mathf.Min(width * Mathf.Lerp(1f, 1.2f, (float)x.NextDouble()), 2f * side);
				OverhangDimensions d = OverhangPlacer.Draw(faceHeight, faceHeight - settings.PoolClearMetres - 0.05f, width, 2f * side,
					new OverhangSettings { MinReach = settings.MinOverhang, MaxReach = settings.MaxOverhang }, () => (float)y.NextDouble());
				Assert.That((d.Reach, d.Back, d.Thickness, d.Across), Is.EqualTo((reach, back, thickness, across)), $"seed {seed}");
				Assert.That(y.Next(), Is.EqualTo(x.Next()), "the same number of draws");
			}
		}

		[Test]
		public void TheStretchIsFallLedgesOwnArithmetic()
		{
			var r = new System.Random(5);
			for (int i = 0; i < 200; i++)
			{
				float F(float lo, float hi) => lo + (hi - lo) * (float)r.NextDouble();
				var art = new Bounds(new Vector3(F(-1, 1), F(-1, 1), F(-1, 1)), new Vector3(F(0.001f, 3), F(0.001f, 3), F(0.001f, 3)));
				var middle = new Vector3(F(-500, 500), F(-50, 200), F(-500, 500));
				var size = new Vector3(F(0.2f, 10), F(0.2f, 3), F(0.2f, 8));
				Quaternion rotation = Quaternion.Euler(F(-5, 5), F(0, 360), F(-5, 5));
				Vector3 own = art.size;
				var scale = new Vector3(size.x / Mathf.Max(0.01f, own.x), size.y / Mathf.Max(0.01f, own.y), size.z / Mathf.Max(0.01f, own.z));
				Vector3 at = middle - rotation * Vector3.Scale(art.center, scale);
				OverhangPlacer.Stretch(art, middle, rotation, size, out Vector3 position, out Vector3 stretched);
				Assert.That((position.x, position.y, position.z, stretched.x, stretched.y, stretched.z), Is.EqualTo((at.x, at.y, at.z, scale.x, scale.y, scale.z)));
			}
		}

		/// <summary>
		/// The whole plan, every rock bit for bit, against FallLedges' own planner as it stood before the slab moved out
		/// (<see cref="FrozenFallLedges"/>, copied from it unchanged), over falls of several drops, rocks and seeds: the
		/// golden output before and after.
		/// </summary>
		[Test]
		public void FallLedgesPlansWhatItPlannedBeforeTheRefactor()
		{
			int rocks = 0, overhangs = 0;
			ulong hash = 1469598103934665603UL;
			foreach (float drop in new[] { 3f, 8f, 20f, 45f })
			{
				foreach (string rock in new[] { "Sandstone", "Basalt", "Granite", "Slate" })
				{
					for (uint seed = 1; seed <= 25; seed++)
					{
						List<FallLedge> now = FallLedges.Plan(FallWater(drop), FallGround(drop), (x, y, z) => rock, seed);
						List<FallLedge> before = FrozenFallLedges.Plan(FallWater(drop), FallGround(drop), (x, y, z) => rock, seed);
						Assert.That(now.Count, Is.EqualTo(before.Count), $"drop {drop} {rock} seed {seed}");
						for (int i = 0; i < now.Count; i++)
						{
							FallLedge a = now[i], b = before[i];
							Assert.That((a.Kind, a.Prefab, a.River, a.Lip, a.Foot), Is.EqualTo((b.Kind, b.Prefab, b.River, b.Lip, b.Foot)));
							Assert.That(new[] { a.Position.x, a.Position.y, a.Position.z, a.Size.x, a.Size.y, a.Size.z, a.Across, a.FrontAlong },
								Is.EqualTo(new[] { b.Position.x, b.Position.y, b.Position.z, b.Size.x, b.Size.y, b.Size.z, b.Across, b.FrontAlong }),
								$"drop {drop} {rock} seed {seed} rock {i} ({a.Kind})");
							Assert.That(new[] { a.Rotation.x, a.Rotation.y, a.Rotation.z, a.Rotation.w }, Is.EqualTo(new[] { b.Rotation.x, b.Rotation.y, b.Rotation.z, b.Rotation.w }));
							unchecked
							{
								foreach (float f in new[] { a.Position.x, a.Position.y, a.Position.z, a.Size.x, a.Size.y, a.Size.z })
								{
									hash = (hash ^ (ulong)BitConverter.SingleToInt32Bits(f)) * 1099511628211UL;
								}
							}
							rocks++;
							if (a.Kind == FallRockKind.Overhang)
							{
								overhangs++;
							}
						}
					}
				}
			}
			Assert.That(overhangs, Is.GreaterThan(10), "the sweep reaches the overhang");
			TestContext.WriteLine($"FallLedges golden: {rocks} rocks, {overhangs} overhangs, hash {hash:X16}");
		}

		private const float FallSpacing = 2f, FallWidth = 8f, LipSurface = 50f, FallDepth = 1f;
		private const int LipIndex = 15;

		/// <summary>FallLedgeTests' river: straight along +x, a knickpoint step over point 16.</summary>
		private static SceneWater FallWater(float drop)
		{
			const int n = 40;
			var river = new RiverPath
			{
				Id = 5,
				X = new float[n], Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Surface = new float[n], Bed = new float[n], Curvature = new float[n], Reach = new RiverReach[n],
			};
			for (int i = 0; i < n; i++)
			{
				river.X[i] = i * FallSpacing;
				river.S[i] = i * FallSpacing;
				river.Discharge[i] = 6f;
				river.Width[i] = FallWidth;
				river.Depth[i] = FallDepth;
				river.Surface[i] = i <= LipIndex ? LipSurface : i == LipIndex + 1 ? LipSurface - 0.9f * drop : LipSurface - drop;
				river.Bed[i] = river.Surface[i] - FallDepth;
				river.Reach[i] = i == LipIndex + 1 ? RiverReach.Fall : RiverReach.Run;
			}
			var water = new SceneWater();
			water.Rivers.Add(river);
			return water;
		}

		private static Func<float, float, float> FallGround(float drop)
		{
			float lipX = LipIndex * FallSpacing;
			float top = LipSurface - FallDepth, bottom = LipSurface - drop - 3f;
			return (x, z) => Mathf.Lerp(top, bottom, Mathf.Clamp01((x - (lipX + 1f)) / 0.5f)) + 0.05f * Mathf.Sin(z * 0.9f);
		}

		/// <summary>FallLedges.Plan and PlanFall exactly as they were before the overhang slab moved into <see cref="OverhangPlacer"/> (git ab3cfb147).</summary>
		private static class FrozenFallLedges
		{
			/// <summary>Where the falls' rock goes. Deterministic in the seed.</summary>
			/// <param name="ground">The finished ground at (east, north), scene metres.</param>
			/// <param name="rockTypeAt">The rock at (east, altitude, north), a geology name or null (read as massive rock).</param>
			public static List<FallLedge> Plan(SceneWater water, Func<float, float, float> ground, Func<float, float, float, string> rockTypeAt,
				uint seed, FallLedgeSettings settings = null)
			{
				settings ??= new FallLedgeSettings();
				var result = new List<FallLedge>();
				foreach (RiverPath river in water.Rivers)
				{
					foreach (FallLedges.FallSpan fall in FallLedges.FindFalls(river, settings.MinDropMetres, settings.PoolLevelMetres))
					{
						if (result.Count >= settings.MaxRocks)
						{
							return result;
						}
						var random = new System.Random(unchecked((int)(seed ^ (uint)(river.Id * 0x9E3779B1u) ^ (uint)(fall.Lip * 0x85EBCA6Bu) ^ 0xFA11ED6Eu)));
						PlanFall(river, fall, ground, rockTypeAt, random, settings, result);
					}
				}
				if (result.Count > settings.MaxRocks)
				{
					result.RemoveRange(settings.MaxRocks, result.Count - settings.MaxRocks);
				}
				return result;
			}

			private static void PlanFall(RiverPath river, FallLedges.FallSpan fall, Func<float, float, float> ground, Func<float, float, float, string> rockTypeAt,
				System.Random random, FallLedgeSettings settings, List<FallLedge> result)
			{
				int lip = fall.Lip, foot = fall.Foot;
				float width = Mathf.Max(1f, river.Width[lip]);
				float half = 0.5f * width;
				float side = half + settings.SideSlackMetres;
				float lipSurface = river.Surface[lip];
				float pool = river.Surface[foot];
				float run = river.S[foot] - river.S[lip];

				/* Downstream as the water leaves the lip: the lip to the point after it. The run of a knickpoint's step is a
				 * metre or two, so the line hardly turns over it. */
				int next = Math.Min(foot, lip + 1);
				float tx = river.X[next] - river.X[lip], tz = river.Z[next] - river.Z[lip];
				float tl = Mathf.Sqrt(tx * tx + tz * tz);
				if (tl < 1e-4f)
				{
					return;
				}
				tx /= tl;
				tz /= tl;
				// Left, looking downstream.
				float nx = -tz, nz = tx;
				Vector3 Point(float along, float across) => new Vector3(river.X[lip] + tx * along + nx * across, 0f, river.Z[lip] + tz * along + nz * across);
				float GroundAt(float along, float across)
				{
					Vector3 p = Point(along, across);
					return ground(p.x, p.z);
				}

				/* The face is the ground's drop, not the water's: from the lip's bed (the water runs over it, a depth above)
				 * down to the pool's surface (below that it is under the pool). The ledges stand on that. */
				float bedAtLip = Mathf.Min(GroundAt(0f, 0f), lipSurface - Mathf.Max(0f, river.Depth[lip]));
				float faceTop = Mathf.Min(bedAtLip, lipSurface);
				float faceHeight = faceTop - pool;
				if (faceHeight < 0.5f)
				{
					return;
				}
				float searchTo = run + 2f;
				// The first distance downstream of the lip, along a line across offset `across`, where the ground is down to `altitude`.
				float FaceAt(float altitude, float across)
				{
					for (float d = 0f; d <= searchTo; d += 0.25f)
					{
						if (GroundAt(d, across) <= altitude)
						{
							return d;
						}
					}
					return float.NaN;
				}

				Vector3 lipPoint = Point(0f, 0f);
				string lipRock = rockTypeAt?.Invoke(lipPoint.x, faceTop - 0.25f, lipPoint.z);
				float midFace = FaceAt(faceTop - 0.5f * faceHeight, 0f);
				Vector3 midPoint = Point(float.IsNaN(midFace) ? 0f : midFace, 0f);
				string faceRock = rockTypeAt?.Invoke(midPoint.x, faceTop - 0.5f * faceHeight, midPoint.z);
				FallLedges.FaceHabit habit = FallLedges.Habit(faceRock);
				bool cap = FallLedges.HardCap(lipRock, faceRock);
				Quaternion downstream = Quaternion.LookRotation(new Vector3(tx, 0f, tz), Vector3.up);

				// ── Ledges ──
				/* How many the rock gives, and no more than the face has room for: one per two metres of it. */
				int most = habit == FallLedges.FaceHabit.Bedded ? 3 : habit == FallLedges.FaceHabit.Jointed ? 2 : 1;
				int least = habit == FallLedges.FaceHabit.Massive ? 0 : 1;
				int count = least + random.Next(most - least + 1);
				count = Math.Min(count, Math.Max(1, Mathf.FloorToInt(faceHeight / 2f)));
				string ledgeArt = $"Boulder_{RiverBoulders.Material(faceRock)}_Slab";
				for (int k = 0; k < count; k++)
				{
					/* One band of the face each, jittered within it, so the steps stand one above another down the fall and
					 * the water drops from one to the next rather than all striking at one height. */
					float band = (settings.MaxFaceShare - settings.MinFaceShare) / count;
					float share = settings.MinFaceShare + band * (k + (float)random.NextDouble());
					float top = faceTop - share * faceHeight;
					// Bedded rock steps in thinner beds than lava flows or cleaved rock break into.
					float thickness = Mathf.Lerp(0.4f, 1.2f, (float)random.NextDouble()) * (habit == FallLedges.FaceHabit.Bedded ? 1f : 1.3f);
					thickness = Mathf.Min(thickness, top - pool - settings.PoolClearMetres);
					float span = Mathf.Lerp(settings.MinSpanWidths, settings.MaxSpanWidths, (float)random.NextDouble()) * width;
					// Across the water's path, so it intercepts part of the curtain, within the span rock may be laid in.
					float centre = ((float)random.NextDouble() * 2f - 1f) * 0.5f * width;
					span = Mathf.Min(span, 2f * side);
					centre = Mathf.Clamp(centre, -side + 0.5f * span, side - 0.5f * span);
					float protrusion = Mathf.Lerp(settings.MinProtrusion, Mathf.Clamp(0.2f * faceHeight, settings.MinProtrusion, settings.MaxProtrusion),
						(float)random.NextDouble());
					float outShare = Mathf.Lerp(settings.MinOutShare, settings.MaxOutShare, (float)random.NextDouble());
					float depth = protrusion / outShare;
					// A dip of a few degrees, as beds and flows lie; and a little roll, so the steps are not ruled lines.
					Quaternion tilt = Quaternion.Euler((float)(random.NextDouble() - 0.5) * 8f, 0f, (float)(random.NextDouble() - 0.5) * 6f);
					if (thickness < settings.MinThickness)
					{
						continue;   // the pool is too near: this step would stand in it
					}

					/* Wider than one rock may be: laid as pieces with gaps, each gap about a tenth of the span and never under
					 * half a metre, so the water runs through between them. */
					float widest = settings.MaxRockWidths * width;
					int pieces = Mathf.Max(1, Mathf.CeilToInt(span / widest));
					float gap = pieces > 1 ? Mathf.Max(0.5f, 0.1f * span) : 0f;
					float piece = (span - gap * (pieces - 1)) / pieces;
					if (piece < 0.5f)
					{
						pieces = 1;
						gap = 0f;
						piece = Mathf.Min(span, widest);
					}
					piece = Mathf.Min(piece, widest);
					for (int p = 0; p < pieces; p++)
					{
						float across = centre - 0.5f * span + 0.5f * piece + p * (piece + gap);
						// Where the face is at this ledge's top, at its own place across: the ground drops there.
						float face = FaceAt(top, across);
						if (float.IsNaN(face))
						{
							continue;
						}
						float front = face + protrusion;
						if (front > run)
						{
							continue;   // past the foot: that is the pool's floor, not the face
						}
						float middle = front - 0.5f * depth;
						Vector3 at = Point(middle, across);
						at.y = top - 0.5f * thickness;
						result.Add(new FallLedge
						{
							Kind = FallRockKind.Ledge,
							Position = at,
							Size = new Vector3(piece, thickness, depth),
							Rotation = downstream * tilt,
							Prefab = ledgeArt,
							River = river.Id,
							Lip = lip,
							Foot = foot,
							Across = across,
							FrontAlong = front,
						});
					}
				}

				// ── The overhang ──
				/* A plunge fall: the face from a tenth of the way down to nine tenths stands steeper than PlungeDegrees. A
				 * heightfield blurs a sheer step over one sample, so a tall step still reads near upright and a short one
				 * does not, which is as it should be: a metre-high step is a cascade, not a plunge. */
				float upper = FaceAt(faceTop - 0.1f * faceHeight, 0f), lower = FaceAt(faceTop - 0.9f * faceHeight, 0f);
				bool plunge = !float.IsNaN(upper) && !float.IsNaN(lower)
					&& Mathf.Atan2(0.8f * faceHeight, Mathf.Max(1e-3f, lower - upper)) * Mathf.Rad2Deg >= settings.PlungeDegrees;
				if (plunge && cap)
				{
					/* The cap's slab, its top just under the lip's bed so the river runs on over it and leaves from its front
					 * edge, projecting over the face, its back sunk under the bed upstream. As wide as the channel and a little
					 * more (the cap runs on into the banks): it splits nothing, the water runs over it, so the one-rock limit
					 * on ledges does not bind it. */
					float reach = Mathf.Lerp(settings.MinOverhang, Mathf.Clamp(0.3f * faceHeight, settings.MinOverhang, settings.MaxOverhang), (float)random.NextDouble());
					float back = reach * Mathf.Lerp(1.2f, 2f, (float)random.NextDouble());
					float thickness = Mathf.Clamp(0.15f * faceHeight, 0.6f, 2f);
					thickness = Mathf.Min(thickness, faceHeight - settings.PoolClearMetres - 0.05f);
					float across = Mathf.Min(width * Mathf.Lerp(1f, 1.2f, (float)random.NextDouble()), 2f * side);
					float front = upper + reach;
					// Not held to the foot as the ledges are: it stands at the lip's level, far over the pool, however short the run.
					if (thickness >= settings.MinThickness)
					{
						Vector3 at = Point(front - 0.5f * (reach + back), 0f);
						at.y = faceTop - 0.05f - 0.5f * thickness;
						result.Add(new FallLedge
						{
							Kind = FallRockKind.Overhang,
							Position = at,
							Size = new Vector3(across, thickness, reach + back),
							Rotation = downstream,
							Prefab = $"Boulder_{RiverBoulders.Material(lipRock)}_Slab",
							River = river.Id,
							Lip = lip,
							Foot = foot,
							Across = 0f,
							FrontAlong = front,
						});
					}
				}

				// ── Lip boulders ──
				/* None, one or two lodged on the brink, partly sunk, one each side of the line so they never touch: the lip
				 * profile gets its gaps and the curtain leaves it in separate sheets. Just upstream of the edge, so each sits
				 * on the lip's bed rather than hanging over the drop. */
				double roll = random.NextDouble();
				int boulders = Math.Min(settings.MaxLipBoulders, roll < 0.35 ? 0 : roll < 0.75 ? 1 : 2);
				float firstSide = random.NextDouble() < 0.5 ? -1f : 1f;
				float lastAcross = float.NaN, lastSize = 0f;
				for (int b = 0; b < boulders; b++)
				{
					float size = Mathf.Clamp(Mathf.Lerp(0.15f, 0.3f, (float)random.NextDouble()) * width, 0.6f, Mathf.Max(0.6f, Mathf.Min(2.4f, settings.MaxRockWidths * width)));
					float across = (b == 0 ? firstSide : -firstSide) * Mathf.Lerp(0.15f, 0.6f, (float)random.NextDouble()) * half;
					across = Mathf.Clamp(across, -side + 0.5f * size, side - 0.5f * size);
					if (!float.IsNaN(lastAcross) && Mathf.Abs(across - lastAcross) < 0.5f * (size + lastSize) + 0.3f)
					{
						continue;   // too close to the first to leave water between them
					}
					float along = -0.5f * size * Mathf.Lerp(0.6f, 1.1f, (float)random.NextDouble());
					float buried = Mathf.Lerp(0.3f, 0.5f, (float)random.NextDouble());
					Vector3 at = Point(along, across);
					at.y = ground(at.x, at.z) - buried * size;
					// Worn round on a lip; the odd slab freshly fallen from the cap.
					string shape = random.NextDouble() < 0.8 ? "Round" : "Slab";
					result.Add(new FallLedge
					{
						Kind = FallRockKind.LipBoulder,
						Position = at,
						Size = new Vector3(size, size, size),
						Rotation = Quaternion.Euler((float)(random.NextDouble() - 0.5) * 20f, (float)random.NextDouble() * 360f, (float)(random.NextDouble() - 0.5) * 20f),
						Prefab = $"Boulder_{RiverBoulders.Material(lipRock)}_{shape}",
						River = river.Id,
						Lip = lip,
						Foot = foot,
						Across = across,
						FrontAlong = along + 0.5f * size,
					});
					lastAcross = across;
					lastSize = size;
				}
			}
		}
	}
}
