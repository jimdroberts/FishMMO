using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// What others left in the caves and mines (2026-10-20, caves livened): where <see cref="CaveStory.Plan"/> puts the remains of the
	/// lost, carvings, falls, a mine's track and tubs and a hidden cache — the same every time, sparse, only at dead ends where a
	/// way is filled, track only on a mine's level runs, carvings only in natural rock, nothing by a mouth or in a site's own
	/// chamber — and the art the dressing lays them with (StructurePieces.CaveStory.cs): cold, measured as the placer is told,
	/// the coffer big enough to hide the lootable chest it is laid round.
	/// </summary>
	[TestFixture]
	public class CaveStoryTests
	{
		private const int ArtSeed = ProceduralArtCatalogue.DefaultSeed;
		private const float CaveLift = 60f;

		private static Func<float, float, float> Escarpment(float tiltDegrees, int variant)
		{
			float a = tiltDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			return (x, z) =>
			{
				float u = c * z - s * x, w = s * z + c * x;
				float t = Mathf.Clamp01(u / 30f);
				float face = 10f + 60f * (t * t * (3f - 2f * t));
				float roll = 3f * Mathf.Sin(w * 0.05f + variant) + 2f * Mathf.Sin(u * 0.031f + 0.7f * variant) * Mathf.Clamp01(u / 40f);
				return CaveLift + face + roll * Mathf.Clamp01((u + 10f) / 30f) - 0.04f * Mathf.Max(0f, -u);
			};
		}

		/// <summary>A system planned and stored, rebuilt from its values as the dressing reads it.</summary>
		private static CaveSystem PlanCave(CaveForm form, int size, int seed)
		{
			float tilt = seed * 13f % 40f - 20f;
			CaveSystem system = CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed,
				CaveGround.Sample(Escarpment(tilt, seed), -560f, -560f, 560f, 560f, 2f), 0f, out string problem);
			Assert.That(system, Is.Not.Null, $"{form} size {size} seed {seed} was refused: {problem}");
			return CaveSystem.FromValues(system.ToValues(), system.Origin, system.Seed);
		}

		private static List<CaveSystem> planned;

		/// <summary>Caves of every size, ice caves, lava tubes and mines over a few seeds each, planned once for the fixture.</summary>
		private static List<CaveSystem> Planned()
		{
			if (planned == null)
			{
				planned = new List<CaveSystem>();
				foreach ((CaveForm form, int size, int seeds) in new[]
				{
					(CaveForm.Cave, 0, 4), (CaveForm.Cave, 1, 6), (CaveForm.Cave, 2, 3), (CaveForm.IceCave, 1, 3), (CaveForm.LavaTube, 1, 3), (CaveForm.Mine, 1, 4), (CaveForm.Mine, 2, 3),
				})
				{
					for (int seed = 1; seed <= seeds; seed++)
					{
						planned.Add(PlanCave(form, size, seed));
					}
				}
			}
			return planned;
		}

		private static string Name(CaveSystem s) => $"{s.Form} size {s.SizeClass} seed {s.Seed}";

		// ── The plan ──────────────────────────────────────────────────

		[Test]
		public void ThePlan_IsTheSameEveryTime()
		{
			foreach (CaveSystem system in Planned())
			{
				CaveStoryPlan a = CaveStory.Plan(system), b = CaveStory.Plan(system);
				Assert.That(b.Spots.Count, Is.EqualTo(a.Spots.Count), Name(system));
				for (int i = 0; i < a.Spots.Count; i++)
				{
					Assert.That(b.Spots[i].Kind, Is.EqualTo(a.Spots[i].Kind), Name(system));
					Assert.That((b.Spots[i].At - a.Spots[i].At).sqrMagnitude, Is.EqualTo(0f), Name(system));
					Assert.That(b.Spots[i].Variant, Is.EqualTo(a.Spots[i].Variant), Name(system));
				}
			}
		}

		[Test]
		public void TheStory_IsSparse()
		{
			foreach (CaveSystem system in Planned())
			{
				CaveStoryPlan plan = CaveStory.Plan(system);
				int size = system.SizeClass;
				Assert.That(plan.Count(CaveStoryKind.Remains), Is.LessThanOrEqualTo(size == 0 ? 1 : size == 1 ? 2 : 3), $"{Name(system)}: remains");
				Assert.That(plan.Count(CaveStoryKind.Carving), Is.LessThanOrEqualTo(size == 2 ? 2 : 1), $"{Name(system)}: carvings");
				Assert.That(plan.Count(CaveStoryKind.Cache), Is.LessThanOrEqualTo(size == 2 ? 2 : 1), $"{Name(system)}: caches");
				Assert.That(plan.Count(CaveStoryKind.CaveIn), Is.LessThanOrEqualTo(CaveStory.MaxCaveIns + size), $"{Name(system)}: open ends fallen in");
				Assert.That(plan.Count(CaveStoryKind.Cart), Is.LessThanOrEqualTo(1), $"{Name(system)}: tipped tubs");
				Assert.That(plan.Count(CaveStoryKind.Fall), Is.LessThanOrEqualTo(1 + size), $"{Name(system)}: drifts half fallen");
			}
		}

		[Test]
		public void OverManyCaves_EveryKindOfStoryIsTold()
		{
			var told = new Dictionary<CaveStoryKind, int>();
			foreach (CaveSystem system in Planned())
			{
				foreach (CaveStorySpot s in CaveStory.Plan(system).Spots)
				{
					told.TryGetValue(s.Kind, out int n);
					told[s.Kind] = n + 1;
				}
			}
			Console.WriteLine("story spots: " + string.Join(", ", told.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));
			foreach (CaveStoryKind kind in new[] { CaveStoryKind.Remains, CaveStoryKind.Carving, CaveStoryKind.Cache, CaveStoryKind.Rails })
			{
				Assert.That(told.ContainsKey(kind), Is.True, $"no {kind} in {Planned().Count} systems");
			}
			Assert.That(told.ContainsKey(CaveStoryKind.Choke) || told.ContainsKey(CaveStoryKind.CaveIn), Is.True, "no dead end fell in anywhere");
		}

		[Test]
		public void FallsAreOnlyAtDeadEnds_AndEveryChokedEndIsDressed()
		{
			foreach (CaveSystem system in Planned())
			{
				List<CaveDeadEnd> ends = CaveStory.DeadEnds(system);
				CaveStoryPlan plan = CaveStory.Plan(system);
				foreach (CaveStorySpot s in plan.Spots.Where(x => x.Kind == CaveStoryKind.Choke))
				{
					Assert.That(ends.Any(e => e.Passage == s.Passage && e.Solid == s.Solid), Is.True, $"{Name(system)}: a choke on passage {s.Passage} the layout did not choke");
					Assert.That(system.Solids[s.Solid].Kind, Is.EqualTo(CaveSolidKind.Rubble));
				}
				// An open end is filled only where nothing goes on from it.
				foreach (CaveStorySpot s in plan.Spots.Where(x => x.Kind == CaveStoryKind.CaveIn))
				{
					Assert.That(ends.Any(e => e.Passage == s.Passage && e.Solid < 0), Is.True, $"{Name(system)}: a {s.Kind} on passage {s.Passage}, which goes on");
					Assert.That(system.NearChasmOrClimb(s.At + Vector3.up * 0.5f, 1f), Is.False, $"{Name(system)}: a cave-in at a climb");
					CavePassage passage = system.Passages[s.Passage];
					CaveNode last = passage.Nodes[passage.Nodes.Count - 1];
					foreach (CaveChamber c in system.Chambers)
					{
						Assert.That(InFootprint(c, last.Centre, last.Radius), Is.False, $"{Name(system)}: a {s.Kind} ends in a chamber");
					}
					for (int pi = 0; pi < system.Passages.Count; pi++)
					{
						if (pi == s.Passage)
						{
							continue;
						}
						foreach (CaveNode n in system.Passages[pi].Nodes)
						{
							float apart = new Vector2(n.Centre.x - last.Centre.x, n.Centre.z - last.Centre.z).magnitude;
							Assert.That(apart > last.Radius + 2f || Mathf.Abs(n.Floor - last.Floor) >= 4f, Is.True, $"{Name(system)}: passage {pi} meets the fallen end of {s.Passage}");
						}
					}
				}
				// Every dead end the layout choked is dressed.
				foreach (CaveDeadEnd e in ends.Where(x => x.Solid >= 0))
				{
					Assert.That(plan.Spots.Any(s => s.Kind == CaveStoryKind.Choke && s.Passage == e.Passage), Is.True, $"{Name(system)}: the choke at the end of passage {e.Passage} is bare");
					Assert.That(system.Solids[e.Solid].Kind, Is.EqualTo(CaveSolidKind.Rubble));
				}
			}
		}

		[Test]
		public void TrackIsOnlyInMines_AlongLevelRuns()
		{
			foreach (CaveSystem system in Planned())
			{
				foreach (CaveStorySpot s in CaveStory.Plan(system).Spots.Where(x => x.Kind == CaveStoryKind.Rails || x.Kind == CaveStoryKind.Cart))
				{
					Assert.That(system.Form, Is.EqualTo(CaveForm.Mine), $"{Name(system)}: track in a natural cave");
					if (s.Kind == CaveStoryKind.Cart)
					{
						continue;
					}
					Assert.That(s.To - s.From, Is.GreaterThanOrEqualTo(CaveStory.RailRunMin - 1e-3f), $"{Name(system)}: a run too short to lay");
					CavePassage passage = system.Passages[s.Passage];
					Assert.That(passage.Kind, Is.EqualTo(CavePassageKind.Branch), $"{Name(system)}: the trunk's track is the dressing's own");
					for (int i = 1; i < passage.Nodes.Count; i++)
					{
						CaveNode a = passage.Nodes[i - 1], b = passage.Nodes[i];
						if (b.Along <= s.From + 1e-3f || a.Along >= s.To - 1e-3f || b.Along - a.Along < 1e-3f)
						{
							continue;
						}
						Assert.That(Mathf.Abs(b.Floor - a.Floor) / (b.Along - a.Along), Is.LessThanOrEqualTo(CaveStory.RailGrade + 1e-4f), $"{Name(system)}: track down a steep stretch of passage {s.Passage}");
					}
				}
			}
		}

		[Test]
		public void NothingStandsByAMouth_OrInASitesOwnChamber_OrUnderWater()
		{
			foreach (CaveSystem system in Planned())
			{
				foreach (CaveStorySpot s in CaveStory.Plan(system).Spots)
				{
					if (s.Kind != CaveStoryKind.Rails)
					{
						Assert.That(CaveStory.NearMouth(system, s.At), Is.False, $"{Name(system)}: a {s.Kind} by a mouth at {s.At}");
					}
					if (s.Chamber >= 0)
					{
						Assert.That(CaveStory.IsOwn(system, system.Chambers[s.Chamber]), Is.False, $"{Name(system)}: a {s.Kind} in a site's own chamber");
					}
					if (!float.IsNaN(system.WaterLevel) && s.Kind != CaveStoryKind.Rails)
					{
						Assert.That(system.Origin.y + s.At.y, Is.GreaterThan(system.WaterLevel), $"{Name(system)}: a {s.Kind} under water");
					}
				}
			}
		}

		[Test]
		public void CarvingsAreOnlyInNaturalRock_AndTheLostLieApart()
		{
			foreach (CaveSystem system in Planned())
			{
				CaveStoryPlan plan = CaveStory.Plan(system);
				if (!CaveStory.Carves(system.Form))
				{
					Assert.That(plan.Count(CaveStoryKind.Carving), Is.Zero, $"{Name(system)}: carved");
				}
				foreach (CaveStorySpot s in plan.Spots.Where(x => x.Kind == CaveStoryKind.Carving))
				{
					Assert.That(s.Variant, Is.InRange(0, StructurePieces.CaveCarvingFaces.Length - 1));
					Assert.That(system.Chambers[s.Chamber].Kind, Is.Not.EqualTo(CaveChamberKind.Stope));
				}
				List<CaveStorySpot> lost = plan.Spots.Where(x => x.Kind == CaveStoryKind.Remains).ToList();
				for (int i = 0; i < lost.Count; i++)
				{
					if (system.Form == CaveForm.Mine && lost[i].Variant != (int)CaveStoryPose.Seated)
					{
						Assert.That(lost[i].Variant, Is.EqualTo((int)CaveStoryPose.Miner), $"{Name(system)}: a fallen adventurer in a mine");
					}
					for (int j = i + 1; j < lost.Count; j++)
					{
						Assert.That((lost[i].At - lost[j].At).magnitude, Is.GreaterThanOrEqualTo(CaveStory.RemainsApart), $"{Name(system)}: two remains together");
					}
				}
				foreach (CaveStorySpot s in plan.Spots.Where(x => x.Kind == CaveStoryKind.Cache))
				{
					var expected = system.Form == CaveForm.Mine ? CaveStoryCacheKind.Miners : system.Form == CaveForm.SeaCave || system.Form == CaveForm.Grotto ? CaveStoryCacheKind.Smugglers : CaveStoryCacheKind.Adventurers;
					Assert.That((CaveStoryCacheKind)s.Variant, Is.EqualTo(expected), Name(system));
				}
			}
		}


		// ── The art ───────────────────────────────────────────────────

		private static readonly string[] StoryPieces = { "LostRemains", "CaveCarving", "CaveInTimbers", "CacheCoffer" };

		[Test]
		public void TheStorysPieces_AreColdAndCollideWithNothing_AndNoTemplateDrawsThem()
		{
			foreach (string id in StoryPieces)
			{
				StructurePiece piece = StructureKit.Get(id);
				Assert.That(piece, Is.Not.Null, id);
				Assert.That(piece.Collides, Is.False, $"{id} collides");
				foreach (string tag in piece.Tags)
				{
					Assert.That(tag, Is.EqualTo("cave").Or.EqualTo("mine"), $"{id} carries '{tag}', which a template's query may draw");
				}
				for (int v = 0; v < piece.Variants; v++)
				{
					Assert.That(StructurePieces.EmittersOf(piece, v), Is.Empty, $"{id} v{v} gives off light");
					Console.WriteLine($"{id} v{v}: " + string.Join(" / ", Enumerable.Range(0, StructurePieces.LevelCount).Select(l => StructurePieces.Build(piece, v, l, ArtSeed).Mesh.TriangleCount)) + $" triangles (budget {piece.TriangleBudget})");
					foreach (StructureMaterial m in StructurePieces.Build(piece, v, 0, ArtSeed).Materials)
					{
						Assert.That(m, Is.Not.EqualTo(StructureMaterial.Lamplight).And.Not.EqualTo(StructureMaterial.Glass).And.Not.EqualTo(StructureMaterial.Runelight), $"{id} v{v} glows");
					}
				}
			}
			Assert.That(StructureKit.Get("LostRemains").Variants, Is.EqualTo(3));
			Assert.That(StructureKit.Get("CaveCarving").Variants, Is.EqualTo(StructurePieces.CaveCarvingFaces.Length));
		}

		[Test]
		public void TheCoffer_HidesTheChestItIsLaidRound()
		{
			StructurePiece coffer = StructureKit.Get("CacheCoffer");
			Vector3 inside = StructurePieces.CacheCofferInside;
			for (int v = 0; v < coffer.Variants; v++)
			{
				for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
				{
					Bounds b = StructurePieces.Build(coffer, v, lod, ArtSeed).Mesh.Bounds;
					Assert.That(b.min.x, Is.LessThan(-0.5f * inside.x - 0.02f), $"v{v} LOD{lod}: the chest shows at the left");
					Assert.That(b.max.x, Is.GreaterThan(0.5f * inside.x + 0.02f), $"v{v} LOD{lod}: the chest shows at the right");
					Assert.That(b.min.z, Is.LessThan(-0.5f * inside.z - 0.02f), $"v{v} LOD{lod}: the chest shows at the front");
					Assert.That(b.max.z, Is.GreaterThan(0.5f * inside.z + 0.02f), $"v{v} LOD{lod}: the chest shows at the back");
					Assert.That(b.max.y, Is.GreaterThan(inside.y + 0.02f), $"v{v} LOD{lod}: the chest's lid shows over it");
					Assert.That(b.min.y, Is.GreaterThanOrEqualTo(-0.01f), $"v{v} LOD{lod}: sunk into the floor");
				}
			}
		}

		[Test]
		public void ACarvingsFace_IsAsLargeAsThePlacerMeasures()
		{
			StructurePiece carving = StructureKit.Get("CaveCarving");
			for (int v = 0; v < carving.Variants; v++)
			{
				Vector2 face = StructurePieces.CaveCarvingFaces[v];
				Bounds b = StructurePieces.Build(carving, v, 0, ArtSeed).Mesh.Bounds;
				Assert.That(b.size.x, Is.EqualTo(face.x).Within(0.15f), $"v{v} width");
				Assert.That(b.max.y, Is.EqualTo(face.y).Within(0.12f), $"v{v} height");
				Assert.That(b.min.y, Is.GreaterThan(-0.1f), $"v{v} reaches under its sill");
				Assert.That(b.min.z, Is.GreaterThan(-0.1f), $"v{v} stands out of its wall");
				Assert.That(b.max.z, Is.EqualTo(StructurePieces.CaveCarvingRoot).Within(0.01f), $"v{v} root");
			}
		}

		private static bool InFootprint(in CaveChamber c, Vector3 p, float grow)
		{
			float r = c.Yaw * Mathf.Deg2Rad;
			float dx = p.x - c.Centre.x, dz = p.z - c.Centre.z;
			float u = (dx * Mathf.Sin(r) + dz * Mathf.Cos(r)) / Mathf.Max(0.5f, c.Radii.x + grow);
			float w = (dx * Mathf.Cos(r) - dz * Mathf.Sin(r)) / Mathf.Max(0.5f, c.Radii.z + grow);
			return u * u + w * w <= 1f && Mathf.Abs(p.y - CaveSystem.FloorOf(c, p)) < 4f;
		}
	}
}
