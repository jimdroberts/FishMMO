using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Client;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I6f of the biome suite (2026-10-26, decisions 22 and 34): each rock's formation variant count (four for every rock
	/// there was, two for the suite's formation-only and off-world rocks), the stone details' variant meshes the GPU detail
	/// scatter cycles per instance, the drafts' named stone sets, and the orphan list. Pure C#: every test here runs outside
	/// the editor but <see cref="NamedSets_AndDetailVariants_AreClosedSoundAndWithinBudget"/>, which needs only the mesh maths.
	/// </summary>
	[TestFixture]
	public class RockVariantTests
	{
		private const int Seed = 1234;

		private static HashSet<string> RegionalNames => new HashSet<string>(RegionalRocks.Crust.Concat(RegionalRocks.FormationRocks).Select(t => t.Name));

		// ── Formations ────────────────────────────────────────────────

		[Test]
		public void EveryRockBeforeTheSuitesOwn_KeepsFourVariants()
		{
			HashSet<string> regional = RegionalNames;
			var rows = new HashSet<string>(RockVariantCounts.Formations.Select(r => r.Rock));
			int old = 0;
			foreach (RockType type in RockTypes.All.Where(t => !regional.Contains(t.Name)))
			{
				old++;
				Assert.That(rows.Contains(type.Name), Is.False, $"{type.Name}: an existing rock has no row, so its art is unchanged");
				Assert.That(RockVariantCounts.Of(type.Name), Is.EqualTo(4), type.Name);
				Assert.That(RockArtNames.AllFormations().Count(f => f.Type.Name == type.Name), Is.EqualTo(type.Shapes.Length * 4), type.Name);
			}
			Assert.That(old, Is.EqualTo(29), "the sixteen crust rocks and the thirteen mineral, ice and alien ones");
			Assert.That(RockTypes.VariantCount, Is.EqualTo(4), "the cave dressing draws in 0..3");
			Assert.That(RockVariantCounts.Of(null), Is.EqualTo(4));
			Assert.That(RockVariantCounts.Of("NoSuchRock"), Is.EqualTo(4));
		}

		[Test]
		public void EverySuiteRock_HasARow_FormationOnlyAndOffWorldOnesTwo()
		{
			var rows = RockVariantCounts.Formations;
			Assert.That(rows.Select(r => r.Rock), Is.EquivalentTo(RegionalNames), "a row for each of the suite's rocks, and only those");
			Assert.That(rows.Select(r => r.Rock).Distinct().Count(), Is.EqualTo(rows.Length));
			var formationOnly = new HashSet<string>(RegionalRocks.FormationRocks.Select(t => t.Name));
			foreach ((string rock, int variants, string why) in rows)
			{
				Assert.That(new[] { 1, 2, 4 }, Does.Contain(variants), $"{rock}: a count dividing four keeps a cave's draw even");
				Assert.That(RockVariantCounts.Of(rock), Is.EqualTo(variants), rock);
				if (formationOnly.Contains(rock))
				{
					Assert.That(variants, Is.EqualTo(2), $"{rock} is formation-only");
				}
			}
			string[] fewer = rows.Where(r => !formationOnly.Contains(r.Rock) && r.Variants == 2).Select(r => r.Rock).ToArray();
			Assert.That(fewer, Is.EquivalentTo(new[]
			{
				"Cryolava", "Marl", "Duststone", "Troctolite", "Cryokarst", "RaftIce", "SableIce", "Norite", "Variolite", "Platebasalt",
				"MarineIce", "Alunite", "Tessera", "Hazestone", "BrineIce", "Peridotite", "Radiolarite", "SulcusIce",
			}), "the crust rocks of the off-world and deep-floor biomes only");
		}

		[Test]
		public void FormationNames_WrapRoundTheRocksCount()
		{
			Assert.That(RockArtNames.FormationPrefab("Granite", "Corestone", 3), Is.EqualTo("Formation_Granite_Corestone_3"));
			Assert.That(RockArtNames.FormationPrefab("Natrite", "SaltSpire", 3), Is.EqualTo("Formation_Natrite_SaltSpire_1"));
			Assert.That(RockArtNames.FormationPrefab("Natrite", "SaltSpire", 2), Is.EqualTo("Formation_Natrite_SaltSpire_0"));
			Assert.That(RockArtNames.FormationMesh("Natrite", "SaltSpire", 3, 1), Is.EqualTo("Formation_Natrite_SaltSpire_1_LOD1"));
			Assert.That(RockVariantCounts.Wrap("Natrite", -1), Is.EqualTo(1));
			Assert.That(RockArtNames.FormationPrefab("Komatiite", "Scree", 3), Does.EndWith("_3"), "a lived-on world's crust rock keeps four");
			// Every draw a cave's dressing can make names a prefab the generator makes.
			var made = new HashSet<string>(ProceduralArtCatalogue.RockPrefabNames());
			foreach (RockType type in RockTypes.All)
			{
				foreach (FormationShape shape in type.Shapes)
				{
					for (int draw = 0; draw < RockTypes.VariantCount; draw++)
					{
						Assert.That(made, Does.Contain(RockArtNames.FormationPrefab(type.Name, shape.Name, draw)), $"{type.Name} {shape.Name} draw {draw}");
					}
				}
			}
		}

		[Test]
		public void TheGenerator_MakesEachRocksOwnCount_AndNoMore()
		{
			var payload = new HashSet<string>(ProceduralArtCatalogue.RockPayloadPaths());
			int lods = ProceduralArtCatalogue.FormationResolution.Length;
			long newMeshes = 0, atFour = 0;
			HashSet<string> regional = RegionalNames;
			foreach (RockType type in RockTypes.All)
			{
				int n = RockVariantCounts.Of(type.Name);
				Assert.That(RockArtNames.Formations(in type, type.Shapes[0].Name).Length, Is.EqualTo(n), type.Name);
				foreach (FormationShape shape in type.Shapes)
				{
					for (int v = 0; v < RockTypes.VariantCount; v++)
					{
						string mesh = ProceduralArtCatalogue.MeshPath($"Formation_{type.Name}_{shape.Name}_{v}_LOD0");
						Assert.That(payload.Contains(mesh), Is.EqualTo(v < n), $"{type.Name} {shape.Name} variant {v}");
					}
				}
				if (regional.Contains(type.Name))
				{
					newMeshes += type.Shapes.Length * n * lods;
					atFour += type.Shapes.Length * 4 * lods;
				}
			}
			Assert.That(newMeshes, Is.LessThan(atFour * 0.82), "the suite's rocks: about a fifth fewer meshes than at four apiece");
		}

		// ── Stone details ─────────────────────────────────────────────

		[Test]
		public void StoneDetails_OldStonesKeepOneMesh_TheSuitesCycleThree()
		{
			var suite = new HashSet<string>(RegionalRocks.Crust.Select(t => t.Name).Concat(StoneStyles.NamedStoneNames));
			var payload = new HashSet<string>(ProceduralArtCatalogue.PayloadPaths());
			foreach (RockMaterialSpec stone in ProceduralArtCatalogue.AllRockMaterials)
			{
				int n = RockVariantCounts.StoneDetailsOf(stone.Name);
				Assert.That(n, Is.EqualTo(suite.Contains(stone.Name) ? 3 : 1), stone.Name);
				Assert.That(ProceduralArtCatalogue.SmallRocksVariantMesh(stone.Name, 0), Is.EqualTo(ProceduralArtCatalogue.SmallRocksMesh(stone.Name)), "variant 0 is the mesh it always had");
				Assert.That(ProceduralArtCatalogue.PebblesVariantMesh(stone.Name, 0), Is.EqualTo(ProceduralArtCatalogue.PebblesMesh(stone.Name)));
				for (int v = 0; v < RockVariantCounts.MaxStoneDetails; v++)
				{
					Assert.That(payload.Contains(ProceduralArtCatalogue.MeshPath(ProceduralArtCatalogue.SmallRocksVariantMesh(stone.Name, v))), Is.EqualTo(v < n), $"{stone.Name} rocks {v}");
					Assert.That(payload.Contains(ProceduralArtCatalogue.MeshPath(ProceduralArtCatalogue.PebblesVariantMesh(stone.Name, v))), Is.EqualTo(v < n), $"{stone.Name} pebbles {v}");
				}
			}
			Assert.That(RockVariantCounts.MaxStoneDetails, Is.EqualTo(DetailScatterSettings.MaxVariants), "the generator's variants fit the scatter's slices");
			Assert.That(ProceduralArtCatalogue.DetailVariantChildPrefix, Is.EqualTo(DetailScatterSettings.VariantChildPrefix), "the generator names the children the scatter reads");
			Assert.That(ProceduralArtCatalogue.DetailVariantChild(2), Is.EqualTo("Variant2"));
		}

		[Test]
		public void DetailVariant_ZeroIsTheGroupItself_OthersTurnAndRename()
		{
			StonePiece[] pebbles = StoneStyles.For("Rhine").Pebbles;
			Assert.That(StoneStyles.DetailVariant(pebbles, 0), Is.SameAs(pebbles), "variant 0 keeps the bytes it always had");
			StonePiece[] one = StoneStyles.DetailVariant(pebbles, 1);
			Assert.That(one.Length, Is.EqualTo(pebbles.Length));
			Assert.That(one[0].Shape.Size, Is.EqualTo(pebbles[1].Shape.Size), "another piece in the middle");
			Assert.That(one[0].Shape.Name, Is.EqualTo(pebbles[1].Shape.Name + "/v1"), "its own randomness");
			Assert.That(pebbles[1].Shape.Name, Does.Not.EndWith("/v1"), "the set's own pieces untouched");
		}

		// ── Named sets ────────────────────────────────────────────────

		[Test]
		public void NamedSets_AreStonesOfTheirOwn_OverTheirRocksSurface()
		{
			var prefabs = new HashSet<string>(ProceduralArtCatalogue.AllPrefabNames());
			var materials = ProceduralArtCatalogue.AllRockMaterials.ToDictionary(m => m.Name);
			foreach ((string name, string surface, string ground) in StoneStyles.NamedStones)
			{
				Assert.That(StoneStyles.Has(name), Is.True, name);
				Assert.That(RockTypes.TryGet(surface, out _), Is.True, $"{name} wears {surface}'s surface");
				Assert.That(materials.ContainsKey(name), Is.True, name);
				Assert.That(materials[name].WearsRockType, Is.EqualTo(name == surface ? name : null), name);
				Assert.That(materials[name].GroundFamily, Is.EqualTo(ground), name);
				if (name != surface)
				{
					Assert.That(RockArtNames.RockSurfaceTypeForLegacy(name), Is.EqualTo(surface), name);
					Assert.That(RockTypes.TryGet(name, out _), Is.False, $"{name}: Rock_{name} is its own material, no rock type's");
				}
			}
			// The prefab names the drafts asked for (Detail_Cobbles_Imbricated is a small rock here).
			foreach (string asked in new[]
			{
				"Detail_Pebbles_Maerl", "Detail_Pebbles_CoralRubble", "Detail_Pebbles_Rhine", "Detail_Rocks_Imbricated", "Detail_Rocks_Rubble",
				"Detail_Pebbles_Chert", "Detail_Rocks_BlockFall", "Detail_Pebbles_Furrow", "Detail_Pebbles_Strand", "Detail_Pebbles_HazeKnot",
				"Detail_Rocks_IceCobble", "Detail_Pebbles_IceCobble", "Detail_Rocks_Striated", "Detail_Rocks_Tezontle", "Detail_Rocks_Duneslab",
				"Detail_Pebbles_Duneslab", "Boulder_Striated_Round",
				// Served by the sets that were there.
				"Detail_Pebbles_Till", "Boulder_Till_Round", "Detail_Rocks_Dolerite", "Detail_Rocks_Coralstone", "Detail_Rocks_Greywacke",
				"Detail_Pebbles_CoralRag", "Boulder_CoralRag_Round", "Detail_Rocks_Komatiite", "Detail_Rocks_Trachyte", "Detail_Rocks_Quartzite",
				"Detail_Pebbles_Radiolarite", "Detail_Rocks_Radiolarite", "Boulder_Greenstone_Round", "Detail_Rocks_Molasse", "Detail_Pebbles_Caliche",
				"Detail_Pebbles_Sandstone",
			})
			{
				Assert.That(prefabs, Does.Contain(asked));
			}
			// A river on a rock still drops that rock's own boulders, never a named set over its surface.
			Assert.That(RiverBoulders.Material("Limestone"), Is.EqualTo("Limestone"));
			Assert.That(RiverBoulders.Material("Scoria"), Is.EqualTo("Scoria"));
			Assert.That(RiverBoulders.Material("Greywacke"), Is.EqualTo("Greywacke"));
			Assert.That(RiverBoulders.Material("Gneiss"), Is.EqualTo("Gneiss"));
		}

		/// <summary>How close two corners are to count as one: 10 µm, far under anything drawn.</summary>
		public const float WeldMetres = 1e-5f;

		/// <summary>
		/// Every edge of the mesh, its corners welded within <see cref="WeldMetres"/>, is used once each way: a closed,
		/// consistently wound surface. Welded within a tolerance, not bit for bit: a fluted stone's charts meet where the
		/// azimuth wraps (atan2 of +0 and −0), and the flutes' cosine there differs in its last bit, a seam of 1e-7 m.
		/// </summary>
		public static string ClosureProblem(MeshBuilder mesh)
		{
			var cells = new Dictionary<(long, long, long), List<int>>();
			var corners = new List<Vector3>();
			var weld = new int[mesh.VertexCount];
			for (int i = 0; i < mesh.VertexCount; i++)
			{
				Vector3 p = mesh.Positions[i];
				long cx = (long)Math.Floor(p.x / WeldMetres), cy = (long)Math.Floor(p.y / WeldMetres), cz = (long)Math.Floor(p.z / WeldMetres);
				int id = -1;
				for (long dx = -1; dx <= 1 && id < 0; dx++)
				{
					for (long dy = -1; dy <= 1 && id < 0; dy++)
					{
						for (long dz = -1; dz <= 1 && id < 0; dz++)
						{
							if (cells.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int> near))
							{
								foreach (int other in near)
								{
									if ((corners[other] - p).sqrMagnitude <= WeldMetres * WeldMetres)
									{
										id = other;
										break;
									}
								}
							}
						}
					}
				}
				if (id < 0)
				{
					id = corners.Count;
					corners.Add(p);
					if (!cells.TryGetValue((cx, cy, cz), out List<int> list))
					{
						cells[(cx, cy, cz)] = list = new List<int>();
					}
					list.Add(id);
				}
				weld[i] = id;
			}
			var edges = new Dictionary<(int, int), int>();
			foreach (List<int> list in mesh.Submeshes)
			{
				for (int t = 0; t < list.Count; t += 3)
				{
					int a = weld[list[t]], b = weld[list[t + 1]], c = weld[list[t + 2]];
					if (a == b || b == c || a == c)
					{
						return $"triangle {t / 3} collapses when welded";
					}
					foreach ((int, int) e in new[] { (a, b), (b, c), (c, a) })
					{
						edges.TryGetValue(e, out int n);
						edges[e] = n + 1;
					}
				}
			}
			foreach (KeyValuePair<(int, int), int> e in edges)
			{
				if (e.Value != 1)
				{
					return $"edge {e.Key} is used {e.Value} times one way";
				}
				if (!edges.ContainsKey((e.Key.Item2, e.Key.Item1)))
				{
					return $"edge {e.Key} is open (no triangle the other way)";
				}
			}
			return null;
		}

		[Test]
		public void NamedSets_AndDetailVariants_AreClosedSoundAndWithinBudget()
		{
			int[] res = ProceduralArtCatalogue.BoulderResolution;
			foreach (string name in StoneStyles.NamedStoneNames)
			{
				StoneSet set = StoneStyles.For(name);
				foreach (RockShape slot in ProceduralArtCatalogue.BoulderShapes)
				{
					StoneShape shape = set.Boulder(slot.Name);
					for (int lod = 0; lod < res.Length; lod++)
					{
						MeshBuilder mesh = StoneMeshes.Build(in shape, res[lod], Seed);
						Assert.That(mesh.Validate(true), Is.Empty, $"{name} {slot.Name} LOD{lod}");
						Assert.That(ClosureProblem(mesh), Is.Null, $"{name} {slot.Name} LOD{lod}");
					}
				}
			}
			foreach (RockMaterialSpec stone in ProceduralArtCatalogue.AllRockMaterials)
			{
				int n = RockVariantCounts.StoneDetailsOf(stone.Name);
				StoneSet set = StoneStyles.For(stone.Name);
				foreach ((string kind, StonePiece[] pieces, int budget) in new[] { ("rocks", set.Rocks, 156), ("pebbles", set.Pebbles, 216) })
				{
					MeshBuilder first = StoneMeshes.BuildGroup(pieces, Seed);
					for (int v = 0; v < n; v++)
					{
						string what = $"{stone.Name} {kind} variant {v}";
						MeshBuilder mesh = v == 0 ? first : StoneMeshes.BuildGroup(StoneStyles.DetailVariant(pieces, v), Seed);
						Assert.That(mesh.Validate(true), Is.Empty, what);
						Assert.That(ClosureProblem(mesh), Is.Null, what);
						Assert.That(mesh.Submeshes.Count, Is.EqualTo(1), what);
						Assert.That(mesh.TriangleCount, Is.LessThanOrEqualTo(budget), what);
						Assert.That(mesh.Bounds.min.y, Is.LessThan(0f), $"{what} floats");
						if (v > 0)
						{
							Assert.That(mesh.Positions.SequenceEqual(first.Positions), Is.False, $"{what} is a mesh of its own");
						}
					}
				}
			}
		}

		// ── The GPU scatter's pick ────────────────────────────────────

		[Test]
		public void TheScattersVariantPick_IsEven_AndItsSlotsHoldTheirShare()
		{
			Assert.That(DetailScatterMath.Variant(12345u, 1), Is.EqualTo(0));
			Assert.That(DetailScatterMath.Variant(12345u, 0), Is.EqualTo(0));
			var counts = new int[3];
			const int draws = 30000;
			for (int i = 0; i < draws; i++)
			{
				uint h = DetailScatterMath.Hash(i % 173, i / 173, 0xABCDu, 7, i % 5 + 1);
				int v = DetailScatterMath.Variant(h, 3);
				Assert.That(v, Is.InRange(0, 2));
				counts[v]++;
			}
			foreach (int c in counts)
			{
				Assert.That(c, Is.InRange(draws / 3 - 600, draws / 3 + 600), "every variant alike likely");
			}
			// A slot sized by VariantBound holds its variant's instances: checked over many fields of a few sizes.
			foreach (int bound in new[] { 1, 10, 100, 1000, 20000 })
			{
				long share = DetailScatterMath.VariantBound(bound, 3);
				Assert.That(share, Is.LessThanOrEqualTo(bound));
				Assert.That(share, Is.GreaterThanOrEqualTo((long)Math.Ceiling(bound / 3.0)));
				for (int field = 0; field < 200; field++)
				{
					var inField = new int[3];
					for (int k = 0; k < bound; k++)
					{
						inField[DetailScatterMath.Variant(DetailScatterMath.Hash(field, k, 0x51u, field, k + 1), 3)]++;
					}
					Assert.That(inField.Max(), Is.LessThanOrEqualTo(share), $"bound {bound} field {field}");
				}
			}
			Assert.That(DetailScatterMath.VariantBound(500, 1), Is.EqualTo(500), "a type of one mesh keeps its whole bound");
		}

		[Test]
		public void TheCompute_PicksAsTheCpuDoes()
		{
			string compute = File.ReadAllText("Assets/Prefabs/Client/Weather/Shaders/FishDetailScatter.compute");
			StringAssert.Contains($"#define SCATTER_VARIANT_STREAM {DetailScatterMath.VariantStream}u", compute);
			StringAssert.Contains($"#define SCATTER_MAX_VARIANTS {DetailScatterSettings.MaxVariants}u", compute);
			StringAssert.Contains("level = min((uint)(ScatterUnit(h, SCATTER_VARIANT_STREAM) * (float)variants), variants - 1u);", compute);
			StringAssert.Contains("uint variants = (bush || levels > 1u) ? 1u : clamp((uint)(model.lod.w + 0.5), 1u, SCATTER_MAX_VARIANTS);", compute);
			Assert.That(DetailScatterSettings.MaxVariants, Is.EqualTo(DetailScatterSettings.MaxLevels), "a variant takes the slot a level would");
			string renderer = File.ReadAllText("Assets/Scripts/Client/World/Terrain/Scatter/DetailScatterRenderer.cs");
			StringAssert.Contains("ShareVariantNeeds(types);", renderer);
			StringAssert.Contains("Lod = new Vector4(type.Levels.Length, type.Bush ? 1f : 0f, type.MeshTop, VariantsOf(type)),", renderer);
		}

		// ── Orphans ───────────────────────────────────────────────────

		[Test]
		public void Orphans_AreTheRockFilesNothingMakes_InTheGeneratedFoldersOnly()
		{
			string meshes = ProceduralArtCatalogue.MeshesFolder, prefabs = ProceduralArtCatalogue.PrefabsFolder;
			string[] onDisk =
			{
				meshes + "/Boulder_Round_LOD0.asset", meshes + "/Boulder_Round_LOD0.asset.meta", meshes + "/Pebbles.asset",
				meshes + "/Formation_Granite_Corestone_0_LOD0.asset", meshes + "/Tree_Oak_LOD0.asset",
				prefabs + "/Formation_Natrite_SaltSpire_3.prefab", prefabs + "/Rocks/Crag_Conglomerate.prefab", "Assets/Elsewhere/Boulder_X.asset",
				ProceduralArtCatalogue.TexturesFolder.Replace('/', '\\') + "\\RockSurface_Gone_Albedo.png",
			};
			string[] expected = { meshes + "/Formation_Granite_Corestone_0_LOD0.asset" };
			Assert.That(ProceduralArtOrphans.Find(onDisk, expected), Is.EqualTo(new[]
			{
				meshes + "/Boulder_Round_LOD0.asset", meshes + "/Pebbles.asset", ProceduralArtCatalogue.TexturesFolder + "/RockSurface_Gone_Albedo.png",
				prefabs + "/Formation_Natrite_SaltSpire_3.prefab",
			}.OrderBy(p => p, StringComparer.Ordinal).ToArray()), "another family's files, subfolders, .meta files and other folders are never listed");
			// The generator lists, never deletes.
			string generator = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/ProceduralArt/ProceduralArtOrphans.cs");
			Assert.That(generator, Does.Not.Contain("File.Delete").And.Not.Contain("DeleteAsset").And.Not.Contain("ProceduralArtPayload.Delete"));
		}
	}
}
