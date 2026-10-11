using System;
using System.Collections.Generic;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The structure kit for generated points of interest (StructurePieces, StructureKit): every piece's levels are valid,
	/// closed enough to collide with, within their triangle budget and the same every time; the catalogue answers every
	/// tag the POI templates use; ruins stand lower than what they were; the generated paths are listed where the art
	/// generator and its staleness check look.
	/// </summary>
	[TestFixture]
	public class StructureKitTests
	{
		private const int Seed = ProceduralArtCatalogue.DefaultSeed;

		/// <summary>The tags the POI plan names (Jim's list): each must find at least one piece.</summary>
		private static readonly string[] PlanTags = { "tent", "camp", "sacred", "dead", "building", "wall", "ruin", "portal", "swamp" };

		/// <summary>The pieces the plan lists, by id: none may go missing from the table.</summary>
		private static readonly string[] PlanPieces =
		{
			"TentSmall", "TentMedium", "TentLarge", "LeanTo", "CampfireRing", "PalisadeSegment", "PalisadeGate", "Crate", "Barrel", "BannerPole", "Bedroll",
			"StandingStone", "CircleStone", "Altar", "Shrine", "Obelisk", "StatuePlinth", "Statue", "PortalArch", "PortalRing",
			"Gravestone", "GraveMound", "Mausoleum", "CryptEntrance", "BonePile", "IronFence",
			"TimberHouseSmall", "TimberHouseLarge", "StoneHouse", "Longhouse", "Watchtower", "WallSegment", "WallTower", "Gatehouse", "Keep",
			"StiltHut", "Well", "Lighthouse", "MarketStall", "Pier", "BridgeSpan",
			"RuinedWall", "FallenColumn", "Column", "RubblePile", "CollapsedTower", "RuinedStoneHouse", "BrokenColumn", "RuinedStatue",
		};

		[Test]
		public void EveryPlannedPiece_IsInTheTable_AndIdsAreUnique()
		{
			var ids = new HashSet<string>(StringComparer.Ordinal);
			foreach (StructurePiece p in StructureKit.All)
			{
				Assert.That(ids.Add(p.Id), Is.True, $"piece id '{p.Id}' is used twice");
				Assert.That(p.Variants, Is.GreaterThanOrEqualTo(2), $"{p.Id}: every piece has at least two variants");
			}
			foreach (string id in PlanPieces)
			{
				Assert.That(StructureKit.Get(id), Is.Not.Null, $"the plan's '{id}' is missing");
			}
			Assert.That(StructureKit.Get("Gravestone").Variants, Is.EqualTo(6), "six gravestone shapes");
		}

		[Test]
		public void EveryPiece_HasKnownTags_AndEveryTagResolves()
		{
			var known = new HashSet<string>(StructureKit.Tags, StringComparer.Ordinal);
			foreach (StructurePiece p in StructureKit.All)
			{
				Assert.That(p.Tags, Is.Not.Empty, $"{p.Id} has no tag");
				foreach (string tag in p.Tags)
				{
					Assert.That(known.Contains(tag), Is.True, $"{p.Id} carries '{tag}', which StructurePieces.Tags does not list");
				}
			}
			foreach (string tag in StructureKit.Tags)
			{
				Assert.That(StructureKit.WithTag(tag), Is.Not.Empty, $"no piece carries '{tag}'");
			}
			foreach (string tag in PlanTags)
			{
				Assert.That(known.Contains(tag), Is.True, $"the plan's tag '{tag}' is not in the kit");
			}
		}

		[Test]
		public void Pick_IsSeeded_AndHonoursTagAndStyle()
		{
			StructurePiece a = StructureKit.Pick("tent", StructureStyle.Hide, 42);
			Assert.That(a, Is.Not.Null);
			Assert.That(a.HasTag("tent"), Is.True);
			Assert.That(a.Style, Is.EqualTo(StructureStyle.Hide));
			Assert.That(StructureKit.Pick("tent", StructureStyle.Hide, 42), Is.SameAs(a), "same seed, same piece");
			// A style no tent has falls back to any tent.
			StructurePiece fallback = StructureKit.Pick("tent", StructureStyle.Iron, 7);
			Assert.That(fallback, Is.Not.Null);
			Assert.That(fallback.HasTag("tent"), Is.True);
			Assert.That(StructureKit.Pick("no-such-tag", null, 1), Is.Null);
			int v = StructureKit.PickVariant(a, 99);
			Assert.That(v, Is.InRange(0, a.Variants - 1));
			Assert.That(StructureKit.PickVariant(a, 99), Is.EqualTo(v));
		}

		[Test]
		public void EveryLevel_IsValid_WithinBudget_AndClosedEnough()
		{
			foreach (StructurePiece p in StructureKit.All)
			{
				for (int v = 0; v < p.Variants; v++)
				{
					int previous = int.MaxValue;
					for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
					{
						string name = $"{p.Id} v{v} LOD{lod}";
						StructureMesh m = StructurePieces.Build(p, v, lod, Seed);
						List<string> problems = m.Mesh.Validate(true);
						Assert.That(problems, Is.Empty, $"{name}: {(problems.Count > 0 ? problems[0] : "")}");
						int tris = m.Mesh.TriangleCount;
						Assert.That(tris, Is.GreaterThan(0), $"{name} is empty");
						Assert.That(tris, Is.LessThanOrEqualTo(previous), $"{name} has more triangles than the level before it");
						if (lod == 0)
						{
							Assert.That(tris, Is.LessThanOrEqualTo(p.TriangleBudget), $"{name}: {tris} triangles over its {p.Size} budget");
						}
						previous = tris;
						Assert.That(m.Materials.Length, Is.EqualTo(m.Mesh.Submeshes.Count), $"{name}: one material per submesh");

						Bounds b = m.Mesh.Bounds;
						Assert.That(b.size.x, Is.InRange(0.1f, 40f), $"{name} width");
						Assert.That(b.size.z, Is.InRange(0.1f, 40f), $"{name} depth");
						// A deck piece (pier, bridge span) has its walking surface at its origin and hangs below it: its height is
						// its whole depth, not its top above the origin.
						Assert.That(p.Anchor == StructureAnchor.Deck ? b.size.y : b.max.y, Is.InRange(0.05f, 25f), $"{name} height");
						Assert.That(b.min.y, Is.GreaterThanOrEqualTo(p.Anchor == StructureAnchor.Deck ? -6f : -1.3f), $"{name} reaches too far below its origin");
						// Centred on the origin (a ruin's rubble may pull it off by a little).
						Assert.That(Mathf.Abs(b.center.x), Is.LessThan(0.25f * b.size.x + 0.6f), $"{name} is not centred in x");
						Assert.That(Mathf.Abs(b.center.z), Is.LessThan(0.25f * b.size.z + 0.6f), $"{name} is not centred in z");

						// Closed enough: welded by position, almost every edge is shared by an even number of triangles
						// (the solids are closed; slivers dropped by the mesh writer leave a few odd ones).
						Assert.That(OddEdgeShare(m.Mesh), Is.LessThan(0.02), $"{name} is open");
					}
				}
			}
		}

		[Test]
		public void Building_IsDeterministic_AndTheSeedMatters()
		{
			foreach (StructurePiece p in StructureKit.All)
			{
				for (int v = 0; v < p.Variants; v++)
				{
					long a = Hash(StructurePieces.Build(p, v, 0, Seed).Mesh);
					long b = Hash(StructurePieces.Build(p, v, 0, Seed).Mesh);
					Assert.That(b, Is.EqualTo(a), $"{p.Id} v{v} built twice differs");
				}
			}
			Assert.That(Hash(StructurePieces.Build(StructureKit.Get("StandingStone"), 0, 0, Seed + 1).Mesh),
				Is.Not.EqualTo(Hash(StructurePieces.Build(StructureKit.Get("StandingStone"), 0, 0, Seed).Mesh)), "a rough stone follows the seed");
		}

		[Test]
		public void Ruins_StandLowerThanWhatTheyWere_AndBreakAlikeAtEveryLevel()
		{
			foreach (StructurePiece p in StructureKit.All)
			{
				if (p.RuinOf == null)
				{
					continue;
				}
				StructurePiece intact = StructureKit.Get(p.RuinOf);
				Assert.That(intact, Is.Not.Null, $"{p.Id} is a ruin of the missing '{p.RuinOf}'");
				Assert.That(p.HasTag("ruin"), Is.True, $"{p.Id} is not tagged a ruin");
				Assert.That(p.Height, Is.LessThan(intact.Height), $"{p.Id} stands as tall as {intact.Id}");
				for (int v = 0; v < p.Variants; v++)
				{
					// The break line is the same at every level: the coarsest level's top is close to the finest's.
					float top0 = StructurePieces.Build(p, v, 0, Seed).Mesh.Bounds.max.y;
					float top2 = StructurePieces.Build(p, v, StructurePieces.LevelCount - 1, Seed).Mesh.Bounds.max.y;
					Assert.That(Mathf.Abs(top0 - top2), Is.LessThan(0.15f * top0 + 0.3f), $"{p.Id} v{v} breaks differently at its last level");
				}
			}
		}

		[Test]
		public void Measurements_AreFromTheMeshes()
		{
			foreach (StructurePiece p in StructureKit.All)
			{
				Assert.That(p.Footprint.x, Is.GreaterThan(0.05f), p.Id);
				Assert.That(p.Footprint.y, Is.GreaterThan(0.05f), p.Id);
				Assert.That(p.Height, Is.GreaterThan(0.05f), p.Id);
			}
			StructurePiece wall = StructureKit.Get("WallSegment");
			Assert.That(wall.Footprint.x, Is.EqualTo(wall.ModuleLength).Within(0.05f), "a wall module is exactly its length along x");
			StructurePiece pier = StructureKit.Get("Pier");
			Assert.That(pier.Anchor, Is.EqualTo(StructureAnchor.Deck));
			Assert.That(pier.Footprint.y, Is.EqualTo(pier.ModuleLength).Within(0.05f), "a pier module is exactly its length along z");
			Assert.That(pier.Height, Is.LessThan(1f), "a plain pier's deck is its top");
		}

		[Test]
		public void GeneratedFiles_AreListed_WhereTheGeneratorLooks()
		{
			var payload = new HashSet<string>(ProceduralArtCatalogue.PayloadPaths());
			var wrappers = new HashSet<string>(ProceduralArtCatalogue.WrapperPaths());
			var prefabs = new HashSet<string>(ProceduralArtCatalogue.AllPrefabNames());
			foreach (string surface in StructureSurfaces.Names)
			{
				foreach (StructureFinish finish in StructurePieces.Finishes)
				{
					Assert.That(payload, Does.Contain(ProceduralArtCatalogue.StructureTexture(surface, "Albedo", finish)));
				}
				Assert.That(payload, Does.Contain(ProceduralArtCatalogue.StructureTexture(surface, "Normal")));
				Assert.That(payload, Does.Contain(ProceduralArtCatalogue.StructureTexture(surface, "Mask")));
			}
			foreach (StructureMaterial material in Enum.GetValues(typeof(StructureMaterial)))
			{
				foreach (StructureFinish finish in StructurePieces.Finishes)
				{
					Assert.That(wrappers, Does.Contain(ProceduralArtCatalogue.MaterialPath(StructurePieces.MaterialName(material, finish))));
				}
			}
			foreach (StructurePiece p in StructureKit.All)
			{
				for (int v = 0; v < p.Variants; v++)
				{
					for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
					{
						Assert.That(payload, Does.Contain(ProceduralArtCatalogue.MeshPath(StructurePieces.MeshName(p.Id, v, lod))));
					}
					foreach (StructureFinish finish in StructurePieces.Finishes)
					{
						string name = StructurePieces.PrefabName(p.Id, v, finish);
						Assert.That(prefabs, Does.Contain(name));
						string path = StructureKit.PrefabPath(p.Id, v, finish);
						Assert.That(path, Does.StartWith(ProceduralArtCatalogue.StructurePrefabsFolder + "/" + StructurePieces.Prefix));
						Assert.That(wrappers, Does.Contain(path));
					}
				}
			}
			Assert.That(StructureKit.PrefabPath("TentSmall", 1, StructureFinish.Mossy),
				Is.EqualTo("Assets/Prefabs/Shared/Biomes/Generated/Prefabs/Structures/Structure_TentSmall_1_Mossy.prefab"), "paths are a contract");
			foreach (string source in new[] { "StructureSolids.cs", "StructurePieces.cs", "StructureRuins.cs", "StructureSurfaces.cs", "BiomeArtGenerator.Structures.cs" })
			{
				Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain(source), $"{source} decides generated bytes, so it is hashed");
			}
		}

		[Test]
		public void Surfaces_AreTileableMapsOfTheRightSize()
		{
			foreach (string surface in StructureSurfaces.Names)
			{
				SurfaceMaps maps = StructureSurfaces.Generate(surface, 64, Seed);
				Assert.That(maps.Size, Is.EqualTo(64), surface);
				Assert.That(maps.Albedo.Length, Is.EqualTo(64 * 64), surface);
				Assert.That(maps.Normal.Length, Is.EqualTo(64 * 64), surface);
				Assert.That(maps.Mask.Length, Is.EqualTo(64 * 64), surface);
				foreach (StructureFinish finish in StructurePieces.Finishes)
				{
					Color32[] a = StructureSurfaces.Finish(maps, surface, finish, Seed);
					Assert.That(a.Length, Is.EqualTo(64 * 64), $"{surface} {finish}");
				}
				Assert.That(StructureSurfaces.TileMetres(surface), Is.GreaterThan(0.5f), surface);
			}
		}

		// ── Helpers ───────────────────────────────────────────────────

		private static double OddEdgeShare(MeshBuilder m)
		{
			var keys = new Dictionary<(long, long, long), int>();
			int Id(Vector3 p)
			{
				var k = ((long)Math.Round(p.x * 1e4), (long)Math.Round(p.y * 1e4), (long)Math.Round(p.z * 1e4));
				if (!keys.TryGetValue(k, out int id))
				{
					id = keys.Count;
					keys[k] = id;
				}
				return id;
			}
			var edges = new Dictionary<(int, int), int>();
			foreach (List<int> s in m.Submeshes)
			{
				for (int t = 0; t + 2 < s.Count; t += 3)
				{
					int a = Id(m.Positions[s[t]]), b = Id(m.Positions[s[t + 1]]), c = Id(m.Positions[s[t + 2]]);
					foreach ((int, int) e in new[] { (a, b), (b, c), (c, a) })
					{
						(int, int) k = e.Item1 < e.Item2 ? e : (e.Item2, e.Item1);
						edges.TryGetValue(k, out int n);
						edges[k] = n + 1;
					}
				}
			}
			if (edges.Count == 0)
			{
				return 0;
			}
			int odd = 0;
			foreach (int n in edges.Values)
			{
				if ((n & 1) != 0) odd++;
			}
			return odd / (double)edges.Count;
		}

		private static long Hash(MeshBuilder m)
		{
			long h = 1469598103934665603;
			void Mix(float f)
			{
				h ^= (long)Math.Round(f * 1e5);
				h *= 1099511628211;
			}
			for (int i = 0; i < m.VertexCount; i++)
			{
				Vector3 p = m.Positions[i], n = m.Normals[i];
				Mix(p.x); Mix(p.y); Mix(p.z); Mix(n.x); Mix(n.y); Mix(n.z); Mix(m.UVs[i].x); Mix(m.UVs[i].y);
			}
			foreach (List<int> s in m.Submeshes)
			{
				foreach (int i in s)
				{
					h ^= i;
					h *= 1099511628211;
				}
			}
			return h;
		}
	}
}
