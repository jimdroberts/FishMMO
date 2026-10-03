using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The rock-type catalogue and its formation geometry: every type × shape × variant × level of
	/// detail is a valid, closed, outward-wound, finite, deterministic mesh, fitted to its declared
	/// size, bedded on its pivot, within its triangle budget, with the same silhouette at every
	/// level; and the catalogue keeps the names everything else relies on.
	/// </summary>
	/// <remarks>
	/// Closedness is checked on welded positions (0.1 mm cells, as <see cref="MeshBuilder"/>'s
	/// normal pass welds): every directed edge must appear exactly once and its reverse exactly
	/// once, and every connected shell must enclose positive signed volume. A formation is a set of
	/// closed shells (beds, columns, fragments), so the check runs per shell, not on the whole.
	/// </remarks>
	[TestFixture]
	public class RockFormationTests
	{
		private const int Seed = 1234;

		private static int[] Resolutions => ProceduralArtCatalogue.BoulderResolution;

		private static IEnumerable<TestCaseData> Shapes()
		{
			foreach (RockType type in RockTypes.All)
			{
				foreach (FormationShape shape in type.Shapes)
				{
					yield return new TestCaseData(type.Name, shape.Name).SetName($"{type.Name}_{shape.Name}");
				}
			}
		}

		private static void Get(string typeName, string shapeName, out RockType type, out FormationShape shape)
		{
			Assert.That(RockTypes.TryGet(typeName, out type), Is.True, typeName);
			Assert.That(RockTypes.TryShape(in type, shapeName, out shape), Is.True, shapeName);
		}

		// ── Geometry ──────────────────────────────────────────────

		[TestCaseSource(nameof(Shapes))]
		public void Formation_IsValidClosedFittedBedded_AndWithinBudget(string typeName, string shapeName)
		{
			Get(typeName, shapeName, out RockType type, out FormationShape shape);
			for (int variant = 0; variant < RockTypes.VariantCount; variant++)
			{
				for (int lod = 0; lod < Resolutions.Length; lod++)
				{
					string what = $"{typeName}/{shapeName} v{variant} LOD{lod}";
					MeshBuilder m = RockFormations.Build(in type, in shape, Resolutions[lod], variant, Seed);

					List<string> problems = m.Validate(true);
					Assert.That(problems, Is.Empty, $"{what}: {string.Join("; ", problems.Take(5))}");
					for (int i = 0; i < m.VertexCount; i++)
					{
						Assert.That(Finite(m.Positions[i]) && Finite(m.Normals[i]), Is.True, $"{what}: vertex {i} is not finite");
					}
					AssertClosed(m, what);

					Bounds b = m.Bounds;
					float horizontal = Mathf.Max(b.size.x, b.size.z);
					Assert.That(horizontal, Is.EqualTo(shape.Size).Within(0.01f * shape.Size), $"{what}: footprint");
					Assert.That(b.size.y, Is.EqualTo(shape.Height).Within(0.01f * shape.Height), $"{what}: height");
					Assert.That(Mathf.Abs(b.center.x) + Mathf.Abs(b.center.z), Is.LessThan(0.02f * shape.Size), $"{what}: not centred on its pivot");
					// Bedded: some of it below the pivot, most of it above.
					Assert.That(b.min.y, Is.LessThan(0f), $"{what} floats");
					Assert.That(b.max.y, Is.GreaterThan(-b.min.y), $"{what} is mostly buried");

					Assert.That(m.TriangleCount, Is.LessThanOrEqualTo(RockFormations.TriangleBudget[lod]), $"{what}: triangle budget");
					if (lod > 0)
					{
						Assert.That(m.TriangleCount, Is.LessThan(RockFormations.Build(in type, in shape, Resolutions[lod - 1], variant, Seed).TriangleCount), $"{what}: not coarser than the level above");
					}

					// UVs tile once per TextureMetres of surface, give or take the charts' stretch.
					float density = UvDensity(m) * RockMeshes.TextureMetres;
					Assert.That(density, Is.InRange(0.6f, 1.6f), $"{what}: UV density");
				}
			}
		}

		[TestCaseSource(nameof(Shapes))]
		public void Formation_AtTheLightResolutions_IsValidClosed_AndLighter(string typeName, string shapeName)
		{
			Get(typeName, shapeName, out RockType type, out FormationShape shape);
			for (int lod = 0; lod < RockFormations.LightResolution.Length; lod++)
			{
				string what = $"{typeName}/{shapeName} light LOD{lod}";
				MeshBuilder m = RockFormations.Build(in type, in shape, RockFormations.LightResolution[lod], 0, Seed);
				Assert.That(m.Validate(true), Is.Empty, what);
				AssertClosed(m, what);
				Assert.That(m.TriangleCount, Is.LessThanOrEqualTo(RockFormations.Build(in type, in shape, Resolutions[lod], 0, Seed).TriangleCount), $"{what}: not lighter");
				Assert.That(Mathf.Max(m.Bounds.size.x, m.Bounds.size.z), Is.EqualTo(shape.Size).Within(0.01f * shape.Size), what);
			}
		}

		[TestCaseSource(nameof(Shapes))]
		public void Formation_IsDeterministicPerSeed_AndVariantsDiffer(string typeName, string shapeName)
		{
			Get(typeName, shapeName, out RockType type, out FormationShape shape);
			for (int lod = 0; lod < Resolutions.Length; lod++)
			{
				MeshBuilder a = RockFormations.Build(in type, in shape, Resolutions[lod], 0, Seed);
				MeshBuilder b = RockFormations.Build(in type, in shape, Resolutions[lod], 0, Seed);
				Assert.That(b.VertexCount, Is.EqualTo(a.VertexCount));
				Assert.That(b.Submeshes[0], Is.EqualTo(a.Submeshes[0]));
				for (int i = 0; i < a.VertexCount; i++)
				{
					Assert.That(b.Positions[i] == a.Positions[i] && b.Positions[i].x == a.Positions[i].x && b.Positions[i].y == a.Positions[i].y && b.Positions[i].z == a.Positions[i].z,
						Is.True, $"{typeName}/{shapeName} LOD{lod} vertex {i}");
				}
			}
			MeshBuilder first = RockFormations.Build(in type, in shape, Resolutions[0], 0, Seed);
			for (int variant = 1; variant < RockTypes.VariantCount; variant++)
			{
				MeshBuilder other = RockFormations.Build(in type, in shape, Resolutions[0], variant, Seed);
				bool same = other.VertexCount == first.VertexCount && Enumerable.Range(0, first.VertexCount).All(i => other.Positions[i] == first.Positions[i]);
				Assert.That(same, Is.False, $"{typeName}/{shapeName}: variant {variant} repeats variant 0");
			}
		}

		[TestCaseSource(nameof(Shapes))]
		public void Formation_KeepsItsSilhouetteAtEveryLevel(string typeName, string shapeName)
		{
			Get(typeName, shapeName, out RockType type, out FormationShape shape);
			for (int variant = 0; variant < RockTypes.VariantCount; variant++)
			{
				MeshBuilder fine = RockFormations.Build(in type, in shape, Resolutions[0], variant, Seed);
				for (int lod = 1; lod < Resolutions.Length; lod++)
				{
					MeshBuilder coarse = RockFormations.Build(in type, in shape, Resolutions[lod], variant, Seed);
					float tolerance = (lod == 1 ? 0.07f : 0.14f) * shape.Size;
					// The support function in 26 directions: how far the silhouette reaches each way.
					foreach (Vector3 d in Directions())
					{
						float diff = Mathf.Abs(Support(fine, d) - Support(coarse, d));
						Assert.That(diff, Is.LessThanOrEqualTo(tolerance), $"{typeName}/{shapeName} v{variant} LOD{lod} reaches {diff:F3} m differently toward {d}");
					}
				}
			}
		}

		[Test]
		public void StackedBlocks_RestOnWhatIsBelow()
		{
			// A tor block or a perched boulder sits into the block below it, under its lowest point,
			// rather than hovering on the block's nominal flat top.
			foreach (RockType type in RockTypes.All)
			{
				foreach (FormationShape shape in type.Shapes)
				{
					if (shape.Kind != FormationKind.Tor && shape.Kind != FormationKind.Perched)
					{
						continue;
					}
					for (int variant = 0; variant < RockTypes.VariantCount; variant++)
					{
						MeshBuilder m = RockFormations.Build(in type, in shape, Resolutions[0], variant, Seed);
						int[] shell = Shells(m, out _);
						int top = shell[Enumerable.Range(0, m.VertexCount).OrderByDescending(i => m.Positions[i].y).First()];
						Vector3 lowest = m.Positions[Enumerable.Range(0, m.VertexCount).Where(i => shell[i] == top).OrderBy(i => m.Positions[i].y).First()];
						float below = float.MinValue;
						for (int i = 0; i < m.VertexCount; i++)
						{
							Vector3 q = m.Positions[i];
							float dx = q.x - lowest.x, dz = q.z - lowest.z;
							if (shell[i] != top && dx * dx + dz * dz < 0.15f * 0.15f)
							{
								below = Mathf.Max(below, q.y);
							}
						}
						Assert.That(below, Is.GreaterThan(lowest.y), $"{type.Name}/{shape.Name} v{variant}: the top block does not touch the one below");
					}
				}
			}
		}

		[Test]
		public void Columns_AreOneClosedPrismPerColumn()
		{
			foreach (RockType type in RockTypes.All)
			{
				foreach (FormationShape shape in type.Shapes)
				{
					if (shape.Kind != FormationKind.Columns && shape.Kind != FormationKind.FallenColumns)
					{
						continue;
					}
					for (int lod = 0; lod < Resolutions.Length; lod++)
					{
						MeshBuilder m = RockFormations.BuildColumns(in type, in shape, Resolutions[lod], 0, Seed);
						Shells(m, out _, out int shells);
						Assert.That(shells, Is.EqualTo(shape.Count), $"{type.Name}/{shape.Name} LOD{lod}");
					}
				}
			}
		}

		// ── Catalogue ─────────────────────────────────────────────

		[Test]
		public void Catalogue_HasTheRockTypes_InTheirFamilies()
		{
			string[] igneous = { "Granite", "Basalt", "Andesite", "Obsidian", "Pumice", "Tuff" };
			string[] sedimentary = { "Sandstone", "Limestone", "Shale", "Conglomerate", "Chalk" };
			string[] metamorphic = { "Slate", "Schist", "Gneiss", "Marble", "Quartzite" };
			foreach ((string[] names, RockFamily family) in new[] { (igneous, RockFamily.Igneous), (sedimentary, RockFamily.Sedimentary), (metamorphic, RockFamily.Metamorphic) })
			{
				foreach (string name in names)
				{
					Assert.That(RockTypes.TryGet(name, out RockType type), Is.True, name);
					Assert.That(type.Family, Is.EqualTo(family), name);
					Assert.That(type.Shapes, Is.Not.Empty, name);
					Assert.That(type.Summary, Is.Not.Empty, name);
				}
			}
			Assert.That(RockTypes.All.Select(t => t.Name), Is.Unique);
			foreach (RockType type in RockTypes.All)
			{
				Assert.That(type.Shapes.Select(s => s.Name), Is.Unique, type.Name);
				foreach (FormationShape shape in type.Shapes)
				{
					Assert.That(shape.Size, Is.GreaterThan(0f), $"{type.Name}/{shape.Name}");
					Assert.That(shape.Height, Is.GreaterThan(0f), $"{type.Name}/{shape.Name}");
				}
			}
		}

		[Test]
		public void LegacyMaterials_WearTheirRockTypeSurface_SoBouldersFormationsAndCliffsMatch()
		{
			Assert.That(RockArtNames.RockSurfaceTypeForLegacy("Grey"), Is.EqualTo("Granite"),
				"Rock_Grey is granite's material; it must use RockSurface_Granite like Cliff_Granite does");
			foreach (string same in new[] { "Sandstone", "Basalt", "Limestone" })
			{
				Assert.That(RockArtNames.RockSurfaceTypeForLegacy(same), Is.EqualTo(same), $"Rock_{same} wears RockSurface_{same}");
			}
			foreach (RockMaterialSpec material in ProceduralArtCatalogue.RockMaterials)
			{
				Assert.That(RockArtNames.RockSurfaceTypeForLegacy(material.Name), Is.Not.Null, $"{material.Name} has a rock surface");
			}
		}

		[Test]
		public void Catalogue_KeepsTheLegacyNames_AndAddsOnlyNewOnes()
		{
			// Every legacy rock material stands for a type, so Boulder_{material}_{shape} can map on.
			foreach (RockMaterialSpec material in ProceduralArtCatalogue.RockMaterials)
			{
				Assert.That(RockTypes.ForLegacyMaterial(material.Name, out RockType type), Is.True, material.Name);
				Assert.That(type.Name == material.Name || material.Name == "Grey", Is.True, $"{material.Name} maps to {type.Name}");
			}
			// No new type × shape name can collide with a legacy prefab, and none reuses a legacy shape name.
			var legacy = new HashSet<string>(ProceduralArtCatalogue.AllPrefabNames());
			var legacyShapes = new HashSet<string>(ProceduralArtCatalogue.BoulderShapes.Select(s => s.Name));
			foreach (RockType type in RockTypes.All)
			{
				foreach (FormationShape shape in type.Shapes)
				{
					Assert.That(legacyShapes.Contains(shape.Name), Is.False, $"{type.Name}/{shape.Name} reuses a legacy boulder shape name");
					Assert.That(legacy.Contains(ProceduralArtCatalogue.BoulderPrefab(type.Name, shape.Name)), Is.False, $"{type.Name}/{shape.Name}");
				}
			}
		}

		[Test]
		public void Catalogue_Surfaces_TileWithTheRockUVs_AndUseExistingMotifs()
		{
			var groundNames = new HashSet<string>(SurfaceCatalogue.GroundNames());
			foreach (RockType type in RockTypes.All)
			{
				SurfaceRecipe r = type.Surface;
				Assert.That(r.Name, Is.Not.Empty, type.Name);
				Assert.That(groundNames.Contains(r.Name), Is.False, $"{type.Name}'s surface shares a ground family's name");
				Assert.That(r.TileMetres, Is.EqualTo(RockMeshes.TextureMetres), type.Name);
				Assert.That(Enum.IsDefined(typeof(SurfaceMotif), r.Motif), Is.True, type.Name);
				Assert.That(r.Dark.grayscale, Is.LessThanOrEqualTo(r.Mid.grayscale), $"{type.Name}: the ramp runs shadowed → typical → sunlit");
				Assert.That(r.Mid.grayscale, Is.LessThanOrEqualTo(r.Light.grayscale), $"{type.Name}: the ramp runs shadowed → typical → sunlit");
				if (r.Speckles != null)
				{
					Assert.That(r.SpeckleCount, Is.GreaterThan(0), type.Name);
				}
			}
			// Bedded and foliated rocks carry strata; granite carries its crystals as speckles.
			foreach (string name in new[] { "Sandstone", "Shale", "Slate", "Schist", "Gneiss" })
			{
				RockTypes.TryGet(name, out RockType t);
				Assert.That(t.Surface.Motif, Is.EqualTo(SurfaceMotif.Strata), name);
			}
			Assert.That(RockTypes.Granite.Surface.Speckles, Is.Not.Null.And.Not.Empty);
		}

		// ── Helpers ───────────────────────────────────────────────

		private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

		private static long Key(Vector3 p)
		{
			long x = (long)Math.Round(p.x * 10000f) & 0x1FFFFF;
			long y = (long)Math.Round(p.y * 10000f) & 0x1FFFFF;
			long z = (long)Math.Round(p.z * 10000f) & 0x1FFFFF;
			return (x << 42) | (y << 21) | z;
		}

		private static int[] Shells(MeshBuilder m, out int[] weld) => Shells(m, out weld, out _);

		/// <summary>The shell (connected component over welded positions) each vertex belongs to.</summary>
		private static int[] Shells(MeshBuilder m, out int[] weld, out int count)
		{
			var ids = new Dictionary<long, int>();
			weld = new int[m.VertexCount];
			for (int i = 0; i < m.VertexCount; i++)
			{
				long k = Key(m.Positions[i]);
				if (!ids.TryGetValue(k, out int id))
				{
					id = ids.Count;
					ids[k] = id;
				}
				weld[i] = id;
			}
			int[] parent = Enumerable.Range(0, ids.Count).ToArray();
			int Find(int x)
			{
				while (parent[x] != x)
				{
					x = parent[x] = parent[parent[x]];
				}
				return x;
			}
			List<int> tri = m.Submeshes[0];
			for (int t = 0; t + 2 < tri.Count; t += 3)
			{
				parent[Find(weld[tri[t]])] = Find(weld[tri[t + 1]]);
				parent[Find(weld[tri[t + 1]])] = Find(weld[tri[t + 2]]);
			}
			var shell = new int[m.VertexCount];
			var roots = new HashSet<int>();
			for (int i = 0; i < m.VertexCount; i++)
			{
				shell[i] = Find(weld[i]);
				roots.Add(shell[i]);
			}
			count = roots.Count;
			return shell;
		}

		private static void AssertClosed(MeshBuilder m, string what)
		{
			int[] shell = Shells(m, out int[] weld);
			var directed = new Dictionary<long, int>();
			var volume = new Dictionary<int, double>();
			foreach (List<int> tri in m.Submeshes)
			{
				for (int t = 0; t + 2 < tri.Count; t += 3)
				{
					int a = weld[tri[t]], b = weld[tri[t + 1]], c = weld[tri[t + 2]];
					Assert.That(a != b && b != c && a != c, Is.True, $"{what}: triangle {t / 3} collapses when welded");
					foreach ((int x, int y) in new[] { (a, b), (b, c), (c, a) })
					{
						long k = ((long)x << 32) | (uint)y;
						directed.TryGetValue(k, out int n);
						directed[k] = n + 1;
					}
					Vector3 pa = m.Positions[tri[t]], pb = m.Positions[tri[t + 1]], pc = m.Positions[tri[t + 2]];
					volume.TryGetValue(shell[tri[t]], out double v);
					volume[shell[tri[t]]] = v + Vector3.Dot(pa, Vector3.Cross(pb, pc)) / 6.0;
				}
			}
			foreach (KeyValuePair<long, int> e in directed)
			{
				Assert.That(e.Value, Is.EqualTo(1), $"{what}: an edge is used twice the same way (inconsistent winding)");
				long reverse = ((e.Key & 0xffffffffL) << 32) | (uint)(e.Key >> 32);
				Assert.That(directed.ContainsKey(reverse), Is.True, $"{what}: an open edge (the mesh is not closed)");
			}
			foreach (KeyValuePair<int, double> s in volume)
			{
				Assert.That(s.Value, Is.GreaterThan(0.0), $"{what}: a shell is wound inside out");
			}
		}

		private static IEnumerable<Vector3> Directions()
		{
			for (int x = -1; x <= 1; x++)
			{
				for (int y = -1; y <= 1; y++)
				{
					for (int z = -1; z <= 1; z++)
					{
						if (x != 0 || y != 0 || z != 0)
						{
							yield return new Vector3(x, y, z).normalized;
						}
					}
				}
			}
		}

		private static float Support(MeshBuilder m, Vector3 d)
		{
			float s = float.MinValue;
			foreach (Vector3 p in m.Positions)
			{
				s = Mathf.Max(s, Vector3.Dot(p, d));
			}
			return s;
		}

		private static float UvDensity(MeshBuilder m)
		{
			double uv = 0, length = 0;
			List<int> tri = m.Submeshes[0];
			for (int t = 0; t + 2 < tri.Count; t += 3)
			{
				for (int e = 0; e < 3; e++)
				{
					int a = tri[t + e], b = tri[t + (e + 1) % 3];
					uv += (m.UVs[a] - m.UVs[b]).magnitude;
					length += (m.Positions[a] - m.Positions[b]).magnitude;
				}
			}
			return (float)(uv / Math.Max(1e-9, length));
		}
	}
}
