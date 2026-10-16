using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Each stone material's own boulders, small rocks and pebbles (decision 6 of the biome suite, 2026-10-16:
	/// <see cref="StoneMeshes"/>, <see cref="StoneStyles"/>): every material has its shapes, every mesh is sound, inside
	/// the budget the shared meshes had (boulders 1 200 / 300 / 48 triangles, a small rock 108 + its companion's 48,
	/// pebbles no more than 216), deterministic, bedded, and the materials measurably differ the way their rocks do.
	/// Pure C#: every test here runs outside the editor.
	/// </summary>
	[TestFixture]
	public class StoneMeshTests
	{
		private const int Seed = 1234;

		/// <summary>The most triangles a small rock's group has: its stone at 108 and a companion at 48.</summary>
		private const int RocksBudget = 156;

		/// <summary>The most triangles a pebble group has: one pebble at 108 and two at 48, or four at 48.</summary>
		private const int PebblesBudget = 216;

		private static IEnumerable<string> Materials => ProceduralArtCatalogue.AllRockMaterials.Select(m => m.Name);

		private static string[] Slots => ProceduralArtCatalogue.BoulderShapes.Select(s => s.Name).ToArray();

		private static StoneMeasure MeasureOf(in StoneShape shape, int resolution) => StoneMeshes.Measure(StoneMeshes.Build(in shape, resolution, Seed));

		private static bool SameBytes(MeshBuilder a, MeshBuilder b)
		{
			if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount)
			{
				return false;
			}
			for (int i = 0; i < a.VertexCount; i++)
			{
				Vector3 p = a.Positions[i], q = b.Positions[i];
				Vector3 n = a.Normals[i], m = b.Normals[i];
				if (p.x != q.x || p.y != q.y || p.z != q.z || n.x != m.x || n.y != m.y || n.z != m.z || a.UVs[i].x != b.UVs[i].x || a.UVs[i].y != b.UVs[i].y)
				{
					return false;
				}
			}
			return a.Submeshes[0].SequenceEqual(b.Submeshes[0]);
		}

		// ── Coverage ──────────────────────────────────────────────────

		[Test]
		public void EveryStoneMaterial_HasShapesOfItsOwn_AndEveryShapeSetAMaterial()
		{
			foreach (string material in Materials)
			{
				Assert.That(StoneStyles.Has(material), Is.True, $"{material} would be built with granite's shapes");
			}
			Assert.That(StoneStyles.Materials, Is.EquivalentTo(Materials), "a shape set no material uses is never generated");
		}

		[Test]
		public void TheCrustsRocks_EachHaveAStone_AndRiverBouldersUseIt()
		{
			var prefabs = new HashSet<string>(ProceduralArtCatalogue.AllPrefabNames());
			foreach (RockType type in RockTypes.Crust)
			{
				string stone = RiverBoulders.Material(type.Name);
				string expected = type.Name == "Granite" ? "Grey" : type.Name;
				Assert.That(stone, Is.EqualTo(expected), $"{type.Name}'s boulders, fall ledges and overhang slabs");
				foreach (string slot in Slots)
				{
					Assert.That(prefabs, Does.Contain(ProceduralArtCatalogue.BoulderPrefab(stone, slot)));
				}
			}
			Assert.That(RiverBoulders.Material("Scoria"), Is.EqualTo("Scoria"));
			Assert.That(RiverBoulders.Material("Breccia"), Is.EqualTo("Breccia"));
			Assert.That(RiverBoulders.Material("WaterIce"), Is.EqualTo("Ice"));
			Assert.That(RiverBoulders.Material(null), Is.EqualTo("Grey"), "unknown geology: granite, as before");
			Assert.That(RiverBoulders.Material("Halite"), Is.EqualTo("Grey"), "a mineral with no stone of its own");
			Assert.That(RiverBoulders.Material("Coral"), Is.EqualTo("Grey"), "a stone that is no rock type is never a river's bedrock");
		}

		[Test]
		public void EveryStone_WearsAMaterialThatIsWritten_AndNoMaterialIsWrittenTwice()
		{
			var wrappers = ProceduralArtCatalogue.WrapperPaths().ToList();
			Assert.That(wrappers.Distinct().Count(), Is.EqualTo(wrappers.Count), "a wrapper listed twice");
			var written = new HashSet<string>(wrappers);
			foreach (RockMaterialSpec stone in ProceduralArtCatalogue.AllRockMaterials)
			{
				RockMaterialSpec s = stone;
				string material = ProceduralArtCatalogue.StoneMaterial(in s);
				Assert.That(written, Does.Contain(ProceduralArtCatalogue.MaterialPath(material)), stone.Name);
				if (stone.WearsRockType != null)
				{
					Assert.That(RockTypes.TryGet(stone.WearsRockType, out RockType type), Is.True, stone.Name);
					Assert.That(RockArtNames.HasOwnMaterial(in type), Is.True, $"{stone.Name}: Rock_{type.Name} is written with the formations");
					Assert.That(stone.Name, Is.EqualTo(stone.WearsRockType), "a stone wearing a type's material is named for it, so Rock_{Name} is that material");
				}
			}
			Assert.That(RockArtNames.RockSurfaceTypeForLegacy("Flint"), Is.EqualTo("Obsidian"));
			Assert.That(RockArtNames.RockSurfaceTypeForLegacy("Cobble"), Is.EqualTo("Quartzite"));
		}

		[Test]
		public void EveryStoneMesh_IsAPayloadFile_ListedOnce()
		{
			var payload = ProceduralArtCatalogue.PayloadPaths().ToList();
			Assert.That(payload.Distinct().Count(), Is.EqualTo(payload.Count), "a payload file listed twice");
			var set = new HashSet<string>(payload);
			foreach (string material in Materials)
			{
				foreach (string slot in Slots)
				{
					for (int lod = 0; lod < ProceduralArtCatalogue.BoulderResolution.Length; lod++)
					{
						Assert.That(set, Does.Contain(ProceduralArtCatalogue.MeshPath(ProceduralArtCatalogue.BoulderMesh(material, slot, lod))));
					}
				}
				Assert.That(set, Does.Contain(ProceduralArtCatalogue.MeshPath(ProceduralArtCatalogue.SmallRocksMesh(material))));
				Assert.That(set, Does.Contain(ProceduralArtCatalogue.MeshPath(ProceduralArtCatalogue.PebblesMesh(material))));
			}
		}

		// ── Soundness and budgets ─────────────────────────────────────

		[Test]
		public void Boulders_AreClosedOutwardFacingBeddedDeterministic_AndWithinBudget()
		{
			int[] res = ProceduralArtCatalogue.BoulderResolution;
			foreach (string material in Materials)
			{
				StoneSet set = StoneStyles.For(material);
				foreach (string slot in Slots)
				{
					StoneShape shape = set.Boulder(slot);
					for (int lod = 0; lod < res.Length; lod++)
					{
						string what = $"{material} {slot} LOD{lod}";
						MeshBuilder mesh = StoneMeshes.Build(in shape, res[lod], Seed);
						List<string> problems = mesh.Validate(true);
						Assert.That(problems, Is.Empty, $"{what}: {string.Join("; ", problems.Take(3))}");
						Assert.That(mesh.TriangleCount, Is.EqualTo(StoneMeshes.TrianglesAt(res[lod])), what);
						Assert.That(mesh.Submeshes.Count, Is.EqualTo(1), what);
						Assert.That(SameBytes(mesh, StoneMeshes.Build(in shape, res[lod], Seed)), Is.True, $"{what}: the same seed builds the same bytes");
						Bounds b = mesh.Bounds;
						Assert.That(b.min.y, Is.LessThan(0f), $"{what} floats");
						Assert.That(b.max.y, Is.GreaterThan(-b.min.y), $"{what} is mostly buried");
					}
				}
			}
		}

		[Test]
		public void DetailGroups_AreSoundSingleSubmeshDeterministic_OnTheGround_AndWithinBudget()
		{
			foreach (string material in Materials)
			{
				StoneSet set = StoneStyles.For(material);
				foreach ((string kind, StonePiece[] pieces, int budget) in new[] { ("rocks", set.Rocks, RocksBudget), ("pebbles", set.Pebbles, PebblesBudget) })
				{
					string what = $"{material} {kind}";
					Assert.That(pieces.Length, Is.GreaterThanOrEqualTo(2), $"{what}: a detail prefab's variants are the pieces of its mesh");
					MeshBuilder mesh = StoneMeshes.BuildGroup(pieces, Seed);
					List<string> problems = mesh.Validate(true);
					Assert.That(problems, Is.Empty, $"{what}: {string.Join("; ", problems.Take(3))}");
					Assert.That(mesh.Submeshes.Count, Is.EqualTo(1), $"{what}: a detail prototype draws one sub-mesh");
					Assert.That(mesh.TriangleCount, Is.LessThanOrEqualTo(budget), what);
					Assert.That(mesh.Bounds.min.y, Is.LessThan(0f), $"{what} floats");
					Assert.That(SameBytes(mesh, StoneMeshes.BuildGroup(pieces, Seed)), Is.True, $"{what}: deterministic");
				}
			}
		}

		[Test]
		public void AnotherSeed_BuildsAnotherStone()
		{
			StoneShape shape = StoneStyles.For("Basalt").Round;
			Assert.That(SameBytes(StoneMeshes.Build(in shape, 5, Seed), StoneMeshes.Build(in shape, 5, Seed + 1)), Is.False);
		}

		[Test]
		public void Stress_EveryStoneIsSound_UnderManySeeds()
		{
			// The relaxing pass clears the folds a coarse grid makes of a crease (StoneMeshes.Relax): no seed may leave one.
			int[] res = ProceduralArtCatalogue.BoulderResolution;
			var failures = new List<string>();
			for (int seed = 0; seed < 8; seed++)
			{
				foreach (string material in Materials)
				{
					StoneSet set = StoneStyles.For(material);
					foreach (string slot in Slots)
					{
						StoneShape shape = set.Boulder(slot);
						foreach (int r in res)
						{
							if (StoneMeshes.Build(in shape, r, seed).Validate(true).Count > 0)
							{
								failures.Add($"{material} {slot} res {r} seed {seed}");
							}
						}
					}
					if (StoneMeshes.BuildGroup(set.Rocks, seed).Validate(true).Count > 0) failures.Add($"{material} rocks seed {seed}");
					if (StoneMeshes.BuildGroup(set.Pebbles, seed).Validate(true).Count > 0) failures.Add($"{material} pebbles seed {seed}");
				}
			}
			Assert.That(failures, Is.Empty);
		}

		// ── The slots keep their roles ────────────────────────────────

		[Test]
		public void EverySlot_KeepsItsRole_AndRoughlyItsOldSize()
		{
			foreach (string material in Materials)
			{
				StoneSet set = StoneStyles.For(material);
				StoneMeasure round = MeasureOf(set.Round, 10), slab = MeasureOf(set.Slab, 10), spire = MeasureOf(set.Spire, 10);
				// The Slab is what fall ledges, lip stones and overhang slabs stretch to a box: it must be a slab.
				Assert.That(slab.Flatness, Is.LessThan(0.45f), $"{material}'s Slab");
				Assert.That(slab.Flatness, Is.LessThan(round.Flatness), $"{material}: the Slab is flatter than the Round");
				Assert.That(spire.Flatness, Is.GreaterThan(1.2f), $"{material}'s Spire stands tall");
				foreach (RockShape slot in ProceduralArtCatalogue.BoulderShapes)
				{
					Vector3 nominal = slot.Size * slot.Proportions;
					Vector3 size = StoneMeshes.Build(set.Boulder(slot.Name), 10, Seed).Bounds.size;
					float ratio = Mathf.Max(size.x, size.y, size.z) / Mathf.Max(nominal.x, nominal.y, nominal.z);
					Assert.That(ratio, Is.InRange(0.6f, 1.4f), $"{material} {slot.Name}: a scatter rule's scale range still fits it");
				}
			}
		}

		// ── The materials differ as their rocks do ────────────────────

		private static float MeanPebbleFlatness(StoneSet set) => set.Pebbles.Average(p => MeasureOf(p.Shape, 8).Flatness);

		[Test]
		public void PlatyRocks_BreakIntoFlakes_AndBlockyOnesDoNot()
		{
			foreach (string platy in new[] { "Shale", "Slate", "Schist" })
			{
				StoneSet set = StoneStyles.For(platy);
				Assert.That(MeasureOf(set.Rocks[0].Shape, 8).Flatness, Is.LessThan(0.4f), $"{platy}'s small rock is a plate");
				Assert.That(MeanPebbleFlatness(set), Is.LessThan(0.4f), $"{platy}'s pebbles are flakes");
			}
			foreach (string blocky in new[] { "Grey", "Basalt", "Limestone", "Nodule", "Pumice", "Tuff", "Chalk", "Marble", "Quartzite", "Breccia", "Conglomerate" })
			{
				StoneSet set = StoneStyles.For(blocky);
				Assert.That(MeasureOf(set.Rocks[0].Shape, 8).Flatness, Is.GreaterThan(0.55f), $"{blocky}'s small rock is a block or a lump");
				Assert.That(MeanPebbleFlatness(set), Is.GreaterThan(0.5f), $"{blocky}'s pebbles are chunky");
			}
		}

		[Test]
		public void FracturedRocks_AreAngular_AndWornOnesSmooth()
		{
			foreach (string angular in new[] { "Basalt", "Quartzite", "Breccia", "Obsidian", "Shale", "Slate", "Marble" })
			{
				Assert.That(MeasureOf(StoneStyles.For(angular).Rocks[0].Shape, 8).Angularity, Is.GreaterThanOrEqualTo(0.065f), $"{angular}'s small rock has sharp arrises");
			}
			foreach (string smooth in new[] { "Cobble", "Chalk", "Tuff" })
			{
				Assert.That(MeasureOf(StoneStyles.For(smooth).Rocks[0].Shape, 8).Angularity, Is.LessThanOrEqualTo(0.05f), $"{smooth}'s small rock is worn round");
			}
			StoneMeasure cobble = MeasureOf(StoneStyles.For("Cobble").Round, 10);
			Assert.That(cobble.Angularity, Is.LessThan(0.01f), "a river cobble has no arris");
			Assert.That(cobble.Concavity, Is.LessThan(0.01f), "nor a hollow: a smooth ellipsoid");
		}

		[Test]
		public void PittedAndKnobblyRocks_FoldInward_AndSmoothOnesDoNot()
		{
			foreach (string rough in new[] { "Pumice", "Scoria", "Nodule", "Flint", "Obsidian" })
			{
				Assert.That(MeasureOf(StoneStyles.For(rough).Rocks[0].Shape, 8).Concavity, Is.GreaterThanOrEqualTo(0.1f), $"{rough}'s small rock: vesicles, knobs or scars");
			}
			foreach (string rough in new[] { "Limestone", "Pumice", "Scoria", "Nodule", "Conglomerate", "Coral", "Flint" })
			{
				Assert.That(MeasureOf(StoneStyles.For(rough).Round, 10).Concavity, Is.GreaterThanOrEqualTo(0.12f), $"{rough}'s boulder: pits, flutes, knobs or meanders");
			}
			foreach (string smooth in new[] { "Cobble", "Chalk", "Grey", "Ice" })
			{
				Assert.That(MeasureOf(StoneStyles.For(smooth).Round, 10).Concavity, Is.LessThanOrEqualTo(0.08f), $"{smooth}'s boulder is not pitted");
			}
		}

		[Test]
		public void NoTwoMaterials_MeasureAlike()
		{
			// A descriptor of every slot's habit, weighted so each figure counts about as much; the closest pair today
			// (marble and quartzite) is 0.27 apart.
			var descriptors = new Dictionary<string, float[]>();
			foreach (string material in Materials)
			{
				StoneSet set = StoneStyles.For(material);
				StoneMeasure round = MeasureOf(set.Round, 10), rock = MeasureOf(set.Rocks[0].Shape, 8);
				descriptors[material] = new[]
				{
					round.Flatness, 3f * round.Angularity, 3f * round.Concavity, MeasureOf(set.Slab, 10).Flatness, 0.25f * MeasureOf(set.Spire, 10).Flatness,
					rock.Flatness, 3f * rock.Angularity, 3f * rock.Concavity, MeanPebbleFlatness(set),
				};
			}
			string[] names = descriptors.Keys.ToArray();
			for (int a = 0; a < names.Length; a++)
			{
				for (int b = a + 1; b < names.Length; b++)
				{
					float distance = 0f;
					for (int k = 0; k < descriptors[names[a]].Length; k++)
					{
						distance += Mathf.Abs(descriptors[names[a]][k] - descriptors[names[b]][k]);
					}
					Assert.That(distance, Is.GreaterThan(0.2f), $"{names[a]} and {names[b]} have nearly the same shapes");
				}
			}
		}

		[Test]
		public void TheMeasure_TellsASphereFromABox()
		{
			StoneMeasure ball = MeasureOf(StoneShape.Of("Test/Ball", 1f, 1f, 1f).Noise(0f, 0f), 10);
			StoneMeasure box = MeasureOf(StoneShape.Of("Test/Box", 1f, 1f, 1f).Noise(0f, 0f).Cut(6, 0.6f, 0.01f, -0.3f, 0.9f), 10);
			Assert.That(ball.Angularity, Is.LessThan(0.01f));
			Assert.That(ball.Concavity, Is.LessThan(0.01f));
			Assert.That(box.Angularity, Is.GreaterThan(ball.Angularity + 0.03f));
			Assert.That(ball.Flatness, Is.InRange(0.8f, 1.05f), "bedding flattens the underside only a little");
		}
	}
}
