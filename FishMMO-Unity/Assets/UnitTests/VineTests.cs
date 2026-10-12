using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Vines and ivy (VineMeshes, VineAtlas, VineRules, VineBiomes; Jim, 2026-10-12): valid, deterministic single-sub-mesh
	/// levels within budget that keep their leaf cover as they step down; each form sitting where it is placed from (a
	/// curtain under its edge, a mat in front of its face, a spill on the ground); climbers built on and moving with their
	/// tree's trunk; and the placement rules: headroom under a cave's lintel, the occasional curtained mouth, no mat across a
	/// doorway, curtains off a crag's edge, trees chosen by place. All pure: no engine call, so they run outside Unity.
	/// </summary>
	[TestFixture]
	public class VineTests
	{
		private const int Seed = 20261002;

		/// <summary>Triangle budgets per level: a piece near the camera, in the middle distance, far off.</summary>
		private static readonly int[] PieceBudget = { 1500, 700, 320 };
		private static readonly int[] ClimberBudget = { 2500, 1100, 400 };

		[Test]
		public void Atlas_IsDeterministic_AndEveryLeafCellHasLeaves()
		{
			Color32[] a = VineAtlas.Generate(Seed), b = VineAtlas.Generate(Seed);
			Assert.That(a.Length, Is.EqualTo(VineAtlas.Width * VineAtlas.Height));
			for (int i = 0; i < a.Length; i += 97)
			{
				Assert.That(b[i].a, Is.EqualTo(a[i].a));
				Assert.That(b[i].r, Is.EqualTo(a[i].r));
			}
			foreach (VineCell cell in Enum.GetValues(typeof(VineCell)))
			{
				float coverage = VineAtlas.Coverage(a, cell);
				if (cell == VineCell.Stem)
				{
					Assert.That(coverage, Is.EqualTo(1f), "the stem cell is opaque bark");
				}
				else
				{
					// Leaves, with room between them: neither an empty card nor a solid one.
					Assert.That(coverage, Is.InRange(0.05f, 0.6f), cell.ToString());
				}
			}
		}

		[Test]
		public void Pieces_AreValidDeterministicSingleSubmesh_WithinBudget_AndKeepTheirCover()
		{
			foreach (VineSpecies species in ProceduralArtCatalogue.Vines)
			{
				foreach (VinePiece piece in ProceduralArtCatalogue.VinePieces)
				{
					for (int v = 0; v < piece.Variants; v++)
					{
						string name = ProceduralArtCatalogue.VineName(species.Name, in piece, v);
						int previous = int.MaxValue;
						float full = 0f;
						for (int lod = 0; lod < VineMeshes.Levels; lod++)
						{
							MeshBuilder mesh = VineMeshes.Build(in species, in piece, v, lod, Seed);
							List<string> problems = mesh.Validate(false);
							Assert.That(problems, Is.Empty, $"{name} LOD{lod}: {string.Join("; ", problems)}");
							Assert.That(mesh.Submeshes.Count, Is.EqualTo(1), name);
							Assert.That(mesh.TriangleCount, Is.InRange(1, PieceBudget[lod]), $"{name} LOD{lod}");
							Assert.That(mesh.TriangleCount, Is.LessThan(previous), $"{name} LOD{lod} is no lighter than the level before");
							previous = mesh.TriangleCount;
							Assert.That(mesh.Wind.Count, Is.EqualTo(mesh.VertexCount));
							float area = VineMeshes.LeafArea(mesh);
							if (lod == 0)
							{
								full = area;
								Assert.That(full, Is.GreaterThan(0.5f), name);
							}
							else
							{
								// A far level carries at least the full vine's leaf area, or a curtain thins to a few loose cards.
								Assert.That(area / full, Is.InRange(1.05f, 2.3f), $"{name} LOD{lod} leaf share");
							}
							MeshBuilder again = VineMeshes.Build(in species, in piece, v, lod, Seed);
							Assert.That(again.VertexCount, Is.EqualTo(mesh.VertexCount), $"{name} LOD{lod} is not deterministic");
							for (int i = 0; i < mesh.VertexCount; i += 7)
							{
								Assert.That(again.Positions[i], Is.EqualTo(mesh.Positions[i]), $"{name} LOD{lod} is not deterministic");
							}
						}
					}
				}
			}
		}

		[Test]
		public void Pieces_SitWhereTheyArePlacedFrom()
		{
			foreach (VineSpecies species in ProceduralArtCatalogue.Vines)
			{
				foreach (VinePiece piece in ProceduralArtCatalogue.VinePieces)
				{
					Bounds b = VineMeshes.Build(in species, in piece, 0, 0, Seed).Bounds;
					string name = ProceduralArtCatalogue.VineName(species.Name, in piece, 0);
					switch (piece.Form)
					{
						case VineForm.Curtain:
							// Hung from its edge: nothing stands far over it, it falls about its length, and it lies back over the top only a little.
							Assert.That(b.max.y, Is.LessThan(0.35f), $"{name} top");
							Assert.That(-b.min.y, Is.InRange(piece.Length * 0.5f, piece.Length * 1.2f), $"{name} drop");
							Assert.That(b.min.z, Is.GreaterThan(-0.8f), $"{name} reaches too far back over its edge");
							break;
						case VineForm.Mat:
							// In front of its face, never through it; about as wide and tall as its size.
							Assert.That(b.min.z, Is.GreaterThan(-0.1f), $"{name} goes into its wall");
							Assert.That(b.max.z, Is.LessThan(0.45f), $"{name} stands off its wall");
							// (Its edge leaves reach out past the outline by up to a leaf mass.)
							Assert.That(b.max.y, Is.InRange(piece.Length * 0.6f, piece.Length * 1.3f + 0.4f), $"{name} height");
							Assert.That(b.size.x, Is.InRange(piece.Width * 0.7f, piece.Width * 1.3f + 0.8f), $"{name} width");
							break;
						case VineForm.Spill:
							// Lying on the ground.
							Assert.That(b.min.y, Is.GreaterThan(-0.05f), $"{name} under the ground");
							Assert.That(b.max.y, Is.LessThan(0.45f), $"{name} stands up off the ground");
							break;
					}
				}
			}
		}

		[Test]
		public void Climbers_AreBuiltOnTheirTreesTrunk_AndFollowIt()
		{
			foreach (ProceduralArtCatalogue.VineHost host in ProceduralArtCatalogue.VineHosts)
			{
				TreeSpecies tree = Array.Find(ProceduralArtCatalogue.Trees, t => t.Name == host.Tree);
				Assert.That(tree.Name, Is.EqualTo(host.Tree), $"vine host '{host.Tree}' is not a tree species");
				MeshBuilder lod0 = TreeMeshes.Build(in tree, 0, Seed);
				VineMeshes.TrunkProfile trunk = VineMeshes.TrunkProfile.From(lod0);
				Assert.That(trunk.Valid, $"{host.Tree}: no trunk read from its level 0");
				foreach (string vine in host.Vines)
				{
					Assert.That(ProceduralArtCatalogue.TryVine(vine, out VineSpecies sp), $"{host.Tree} names '{vine}'");
					for (int v = 0; v < ProceduralArtCatalogue.ClimberReach.Length; v++)
					{
						string name = ProceduralArtCatalogue.VineClimberName(vine, host.Tree, v);
						int previous = int.MaxValue;
						for (int lod = 0; lod < VineMeshes.Levels; lod++)
						{
							MeshBuilder m = VineMeshes.BuildClimber(in sp, trunk, tree.Height, ProceduralArtCatalogue.ClimberReach[v], ProceduralArtCatalogue.ClimberMaxMetres[v], host.Tree, v, lod, Seed);
							Assert.That(m.Validate(false), Is.Empty, $"{name} LOD{lod}");
							Assert.That(m.TriangleCount, Is.InRange(1, ClimberBudget[lod]), $"{name} LOD{lod}");
							Assert.That(m.TriangleCount, Is.LessThan(previous), $"{name} LOD{lod}");
							previous = m.TriangleCount;
							Assert.That(m.Bounds.max.y, Is.LessThanOrEqualTo(ProceduralArtCatalogue.ClimberMaxMetres[v] + 1.5f), $"{name} climbs past its reach");
							for (int i = 0; i < m.VertexCount; i++)
							{
								// Marked as the trunk's rings: the shader thickens and leans it with the trunk.
								Assert.That(m.PartData[i].z, Is.EqualTo(0f), $"{name}: a fixed part");
								Assert.That(m.PartData[i].w, Is.EqualTo(1f), $"{name}: marked round the trunk's centre line");
								// Near the bark: no further from the trunk's axis than the trunk plus a leaf.
								Vector3 p = m.Positions[i];
								Vector3 c = trunk.CentreAt(Mathf.Clamp(p.y, 0f, trunk.Top));
								float off = new Vector2(p.x - c.x, p.z - c.z).magnitude - trunk.RadiusAt(Mathf.Clamp(p.y, 0f, trunk.Top));
								Assert.That(off, Is.LessThan(lod == 0 ? 0.75f : 1.6f), $"{name} LOD{lod} vertex {i} stands off the bark");
								// The bark's own sway at that height, so it moves with the trunk.
								float f = Mathf.Clamp01(p.y / tree.Height);
								// (A far level's cards are stretched up the trunk from their own sway: a few hundredths more.)
								Assert.That(m.Wind[i].x, Is.EqualTo(f * f * Mathf.Min(tree.Height / 10f, 2.4f)).Within(lod == 0 ? 0.05f : 0.1f), $"{name} sway");
							}
						}
					}
				}
			}
		}

		[Test]
		public void Catalogue_NamesAreUnique_AndBiomesAreRealOnesWithKnownSpecies()
		{
			var names = new HashSet<string>();
			foreach (string name in ProceduralArtCatalogue.VineBaseNames())
			{
				Assert.That(names.Add(name), $"{name} twice");
			}
			var biomes = new HashSet<string>();
			foreach (BiomeArtSpec.Entry e in BiomeArtSpec.Entries)
			{
				biomes.Add(e.Biome);
			}
			foreach (VineBiomes.Habitat h in VineBiomes.All)
			{
				Assert.That(biomes.Contains(h.Biome), $"VineBiomes names '{h.Biome}', which the art spec does not");
				foreach ((string species, float weight) in h.Species)
				{
					Assert.That(ProceduralArtCatalogue.TryVine(species, out _), $"{h.Biome}: '{species}'");
					Assert.That(weight, Is.GreaterThan(0f));
				}
				foreach (float d in new[] { h.Caves, h.Ruins, h.Cliffs, h.Trees })
				{
					Assert.That(d, Is.InRange(0f, 1f), h.Biome);
				}
			}
			// None in the deserts, the ice and the volcanic wastes.
			foreach (string dry in new[] { "Desert", "High Desert", "Salt Flat", "Dust Sea", "Glacier", "Ice Sheet", "Snow", "Tundra", "Volcanic", "Ice Cave", "Alpine", "Badlands" })
			{
				Assert.That(VineBiomes.For(dry), Is.Null, dry);
			}
		}

		// ── Placement rules ──────────────────────────────────

		private static float PieceLength(in VineSpot spot)
		{
			ProceduralArtCatalogue.TryVinePiece(spot.Form, spot.Size, out VinePiece piece);
			return piece.Length;
		}

		[Test]
		public void CaveMouth_KeepsHeadroomUnlessCurtained_AndCurtainsSomeMouths()
		{
			VineBiomes.Habitat forest = VineBiomes.For("Forest");
			var left = new Vector3(-2f, 4f, 0f);
			var right = new Vector3(2f, 4f, 0f);
			const float height = 4f;
			int curtained = 0, mouths = 400;
			for (int s = 0; s < mouths; s++)
			{
				List<VineSpot> spots = VineRules.CaveMouth(left, right, Vector3.forward, height, forest, new DeterministicRNG(1000 + s), null, (x, z) => 0f, out VineRules.CaveGrowth growth);
				Assert.That(spots.Count, Is.GreaterThan(2), "a forest cave's mouth is overgrown");
				curtained += growth.Curtained ? 1 : 0;
				foreach (VineSpot spot in spots)
				{
					Assert.That(spot.Prefab, Does.StartWith(ProceduralArtCatalogue.VinePrefabFolderName + "/" + ProceduralArtCatalogue.VinePrefix));
					if (spot.Form != VineForm.Curtain || growth.Curtained)
					{
						continue;
					}
					// Over the way in (the middle third), a curtain's lowest leaves stay over a walking head.
					if (Mathf.Abs(spot.Position.x) < 0.7f)
					{
						float bottom = spot.Position.y - PieceLength(spot) * spot.Scale.y;
						Assert.That(bottom, Is.GreaterThan(VineRules.Headroom - 0.45f), $"seed {s}: a curtain hangs into the way in");
					}
				}
			}
			float share = curtained / (float)mouths;
			Assert.That(share, Is.InRange(VineRules.CurtainedShare * 0.6f, VineRules.CurtainedShare * 1.4f), "the curtained share of forest caves");
			// None outside a vine biome.
			Assert.That(VineRules.CaveMouth(left, right, Vector3.forward, height, VineBiomes.For("Desert"), new DeterministicRNG(1), null, null, out _), Is.Empty);
		}

		[Test]
		public void CaveMouth_GrowsOnTheRockRoundTheOpening_WhenTheRockIsKnown()
		{
			// A cliff face at z = 0 (rock behind it), the opening 4 m wide and 4 m tall cut through it.
			Func<Vector3, bool> solid = p => p.y < 0f || p.z < 0f && !(Mathf.Abs(p.x) < 2f && p.y < 4f);
			int around = 0;
			for (int s = 0; s < 40; s++)
			{
				List<VineSpot> spots = VineRules.CaveMouth(new Vector3(-2f, 4f, 0f), new Vector3(2f, 4f, 0f), Vector3.forward, 4f, VineBiomes.For("Forest"),
					new DeterministicRNG(s), solid, (x, z) => 0f, out VineRules.CaveGrowth growth);
				around += growth.Around;
				foreach (VineSpot spot in spots)
				{
					if (spot.Form != VineForm.Mat)
					{
						continue;
					}
					// On the face, not floating in front of it nor inside the opening.
					Assert.That(spot.Position.z, Is.InRange(-0.2f, 0.25f), $"seed {s}: a mat off the face");
					Assert.That(spot.Tilt, Is.LessThan(10f), $"seed {s}: an upright face read as leaning");
					if (spot.Position.y < 3.5f)
					{
						Assert.That(Mathf.Abs(spot.Position.x), Is.GreaterThan(1.9f), $"seed {s}: a mat in the opening");
					}
				}
			}
			Assert.That(around, Is.GreaterThan(20), "growth on the rock either side of the opening");
		}

		[Test]
		public void WallFaces_NeverRunAcrossADoorway()
		{
			int checkedFaces = 0;
			foreach (string id in new[] { "StoneHouse", "RuinedStoneHouse", "WallSegment", "RuinedWall", "Keep" })
			{
				StructurePiece piece = StructurePieces.Find(id);
				if (piece == null)
				{
					continue;
				}
				for (int v = 0; v < piece.Variants; v++)
				{
					List<StructureSolid> solids = StructurePieces.BuildSolids(piece, v, 1, StructurePieces.DefaultSeed);
					IReadOnlyList<StructureDoor> doors = StructurePieces.DoorsOf(piece, v);
					List<VineRules.WallFace> faces = VineRules.WallFaces(solids, doors, StructurePieces.RoomsOf(piece, v));
					foreach (VineRules.WallFace face in faces)
					{
						checkedFaces++;
						Assert.That(face.Height, Is.GreaterThanOrEqualTo(1.2f), id);
						Vector3 along = Vector3.Cross(Vector3.up, face.Normal);
						foreach (StructureDoor door in doors)
						{
							Vector3 mid = door.Hinge + door.Across.normalized * (door.Width * 0.5f);
							// A door in this face's plane, within the stretch and below its top: the stretch would cross it. (A stilt house's
							// door stands over its stilts: a mat up the stilts stops below it.)
							if (Mathf.Abs(Vector3.Dot(mid - face.Foot, face.Normal)) > 1.2f || door.Hinge.y > face.Foot.y + face.Height)
							{
								continue;
							}
							float u = Vector3.Dot(mid - face.Foot, along);
							Assert.That(Mathf.Abs(u) > face.Width * 0.5f + door.Width * 0.5f - 0.05f, $"{id}_{v}: a mat stretch across a doorway");
						}
					}
				}
			}
			Assert.That(checkedFaces, Is.GreaterThan(5), "the stone pieces offer faces to grow on");
		}

		[Test]
		public void CragCurtains_HangFromTheEdge_AsLongAsTheDrop()
		{
			// A block 6 m tall standing on flat ground: its downhill (+z) face is the drop.
			var positions = new List<Vector3>();
			var triangles = new List<int>();
			void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
			{
				int i = positions.Count;
				positions.Add(a); positions.Add(b); positions.Add(c); positions.Add(d);
				triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
			}
			Quad(new Vector3(-4f, 6f, -3f), new Vector3(4f, 6f, -3f), new Vector3(4f, 6f, 3f), new Vector3(-4f, 6f, 3f));
			Quad(new Vector3(-4f, 0f, 3f), new Vector3(4f, 0f, 3f), new Vector3(4f, 6f, 3f), new Vector3(-4f, 6f, 3f));
			List<VineSpot> spots = VineRules.CragCurtains(positions, triangles, Vector3.forward, (x, z) => 0f, "Ivy", new DeterministicRNG(7), 3);
			Assert.That(spots.Count, Is.InRange(1, 3));
			foreach (VineSpot spot in spots)
			{
				Assert.That(spot.Form, Is.EqualTo(VineForm.Curtain));
				Assert.That(spot.Position.y, Is.EqualTo(6f).Within(0.05f), "hung from the top");
				Assert.That(spot.Position.z, Is.InRange(2.6f, 3.3f), "at the edge");
				Assert.That(spot.Yaw, Is.EqualTo(0f).Within(8f), "facing downhill");
				float bottom = spot.Position.y - PieceLength(spot) * spot.Scale.y;
				Assert.That(bottom, Is.InRange(-0.3f, 2.5f), "about as long as the drop");
			}
		}

		[Test]
		public void Climbs_IsDeterministic_AndFollowsTheHabitatsShare()
		{
			VineBiomes.Habitat forest = VineBiomes.For("Forest");
			string[] oak = ProceduralArtCatalogue.VinesOn("Oak");
			int climbed = 0, n = 4000;
			for (int i = 0; i < n; i++)
			{
				var at = new Vector3(i * 3.7f, 10f, (i % 61) * 5.3f);
				bool a = VineRules.Climbs(forest, oak, at, out string s1, out int v1);
				bool b = VineRules.Climbs(forest, oak, at, out string s2, out int v2);
				Assert.That(b, Is.EqualTo(a));
				Assert.That(s2, Is.EqualTo(s1));
				Assert.That(v2, Is.EqualTo(v1));
				if (a)
				{
					climbed++;
					Assert.That(oak, Does.Contain(s1));
				}
			}
			Assert.That(climbed / (float)n, Is.EqualTo(forest.Trees).Within(0.03f));
			Assert.That(VineRules.Climbs(forest, ProceduralArtCatalogue.VinesOn("Spruce"), Vector3.zero, out _, out _), Is.False, "spruce carries none");
		}
	}
}
