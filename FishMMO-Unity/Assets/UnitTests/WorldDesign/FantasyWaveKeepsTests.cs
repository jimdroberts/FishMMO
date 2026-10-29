using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The fantasy wave's keeps and towers (2026-10-27, memory poi-fantasy-wave/WAVE.md "Keeps &amp; towers"): who holds a template's place
	/// (PointOfInterestHolder: a garrison keeps the kind's features, a hostile band trades the waypoint and the service NPCs for a pack, an
	/// elite and perhaps its lord, a ruin keeps no NPC at all), the nine new variants resolving and laying without overlap at every size,
	/// the dark spire and the elven tower in the kit, and the keep's and tower's denser rows. The holder and catalogue tests are pure;
	/// the pieces' and the laying tests build meshes (nativerun outside Unity).
	/// </summary>
	[TestFixture]
	public class FantasyWaveKeepsTests
	{
		[OneTimeSetUp]
		public void RegisterKit() => StructureKitPieceSource.Register();

		/// <summary>Every new variant: its kind, name and holder.</summary>
		private static readonly (POIType kind, string variant, PointOfInterestHolder holder, bool boss)[] Variants =
		{
			(POIType.Keep, "Orc Hold", PointOfInterestHolder.Hostile, false),
			(POIType.Keep, "Ruined Keep", PointOfInterestHolder.Ruined, false),
			(POIType.Keep, "Dark Keep", PointOfInterestHolder.Hostile, true),
			(POIType.Keep, "Dwarven Hold", PointOfInterestHolder.Garrison, false),
			(POIType.Keep, "Elven Hall", PointOfInterestHolder.Garrison, false),
			(POIType.Tower, "Dark Tower", PointOfInterestHolder.Hostile, true),
			(POIType.Tower, "Ruined Watchtower", PointOfInterestHolder.Ruined, false),
			(POIType.Tower, "Elven Tower", PointOfInterestHolder.Garrison, false),
			(POIType.Tower, "Dwarven Tower", PointOfInterestHolder.Garrison, false),
		};

		private static PointOfInterestTemplateSpec Spec(POIType kind, string variant)
			=> PointOfInterestTemplateSpecs.For(kind).SingleOrDefault(s => s.Variant == variant);

		/// <summary>A feature list as words: its types in order, a spawner by its role.</summary>
		private static string[] Words(IEnumerable<PointOfInterestFeature> features)
			=> features.Select(f => f is NpcSpawnerFeature npc ? npc.Role.ToString() : f.GetType().Name).ToArray();

		// ── The holder ────────────────────────────────────────────

		[Test]
		public void AGarrison_KeepsTheKindsOwnFeatures()
		{
			foreach (POIType kind in new[] { POIType.Keep, POIType.Tower, POIType.Village, POIType.Mine })
			{
				var spec = new PointOfInterestTemplateSpec { Kind = kind, Variant = "Test" };
				for (int size = 0; size <= 2; size++)
				{
					Assert.That(Words(PointOfInterestTemplateAuthoring.KindFeatures(spec, size)), Is.EqualTo(Words(PointOfInterestFeatureDefaults.For(kind, size))), $"{kind} at {size}");
				}
			}
			Assert.That(Words(PointOfInterestTemplateAuthoring.KindFeatures(Spec(POIType.Keep, "Dwarven Hold"), 1)),
				Is.EqualTo(new[] { "RegionFeature", "ExplorationFeature", "WaypointFeature", "Guard", "Merchant" }), "a dwarven hold's garrison is a keep's");
		}

		[Test]
		public void AHostileBand_TradesTheServicesForAPackAnEliteAndItsLord()
		{
			string[] Hostile(POIType kind, string variant, int size) => Words(PointOfInterestTemplateAuthoring.KindFeatures(Spec(kind, variant), size));
			string[] small = { "RegionFeature", "ExplorationFeature", "MonsterPack" };
			string[] larger = { "RegionFeature", "ExplorationFeature", "MonsterPack", "Elite" };
			string[] lord = { "RegionFeature", "ExplorationFeature", "MonsterPack", "Elite", "BossSpawnerFeature" };
			Assert.That(Hostile(POIType.Keep, "Orc Hold", 0), Is.EqualTo(small));
			Assert.That(Hostile(POIType.Keep, "Orc Hold", 1), Is.EqualTo(larger));
			Assert.That(Hostile(POIType.Keep, "Orc Hold", 2), Is.EqualTo(larger), "an orc hold names no lord");
			foreach ((POIType kind, string variant) in new[] { (POIType.Keep, "Dark Keep"), (POIType.Tower, "Dark Tower") })
			{
				Assert.That(Hostile(kind, variant, 0), Is.EqualTo(small), variant);
				Assert.That(Hostile(kind, variant, 1), Is.EqualTo(larger), variant);
				Assert.That(Hostile(kind, variant, 2), Is.EqualTo(lord), $"{variant}: its lord waits in the large one, after the pack");
			}
			// A settlement taken: every service gone; a mine held keeps its ore, the band before it; a hostile kind keeps one pack.
			var village = new PointOfInterestTemplateSpec { Kind = POIType.Village, Variant = "Test" }.Held(PointOfInterestHolder.Hostile, boss: true);
			Assert.That(Words(PointOfInterestTemplateAuthoring.KindFeatures(village, 2)), Is.EqualTo(lord));
			var mine = new PointOfInterestTemplateSpec { Kind = POIType.Mine, Variant = "Test" }.Held(PointOfInterestHolder.Hostile);
			Assert.That(Words(PointOfInterestTemplateAuthoring.KindFeatures(mine, 1)), Is.EqualTo(new[] { "RegionFeature", "ExplorationFeature", "MonsterPack", "Elite", "PointOfInterestChamberFeature" }));
			var camp = new PointOfInterestTemplateSpec { Kind = POIType.BanditCamp, Variant = "Test" }.Held(PointOfInterestHolder.Hostile);
			Assert.That(Words(PointOfInterestTemplateAuthoring.KindFeatures(camp, 2)), Is.EqualTo(Words(PointOfInterestFeatureDefaults.For(POIType.BanditCamp, 2))));
		}

		[Test]
		public void ARuin_KeepsNoNpcWaypointOrRespawn()
		{
			string[] bare = { "RegionFeature", "ExplorationFeature" };
			for (int size = 0; size <= 2; size++)
			{
				Assert.That(Words(PointOfInterestTemplateAuthoring.KindFeatures(Spec(POIType.Keep, "Ruined Keep"), size)), Is.EqualTo(bare), $"Ruined Keep at {size}");
				Assert.That(Words(PointOfInterestTemplateAuthoring.KindFeatures(Spec(POIType.Tower, "Ruined Watchtower"), size)), Is.EqualTo(bare), $"Ruined Watchtower at {size}");
			}
			var village = new PointOfInterestTemplateSpec { Kind = POIType.Village, Variant = "Test" }.Held(PointOfInterestHolder.Ruined);
			Assert.That(Words(PointOfInterestTemplateAuthoring.KindFeatures(village, 2)), Is.EqualTo(bare));
			var lair = new PointOfInterestTemplateSpec { Kind = POIType.BossLair, Variant = "Test" }.Held(PointOfInterestHolder.Ruined);
			Assert.That(Words(PointOfInterestTemplateAuthoring.KindFeatures(lair, 2)), Is.EqualTo(bare), "no boss either");
			// What is gathered and the way down stay.
			var mine = new PointOfInterestTemplateSpec { Kind = POIType.Mine, Variant = "Test" }.Held(PointOfInterestHolder.Ruined);
			Assert.That(Words(PointOfInterestTemplateAuthoring.KindFeatures(mine, 1)), Is.EqualTo(new[] { "RegionFeature", "ExplorationFeature", "PointOfInterestChamberFeature" }));
			var cave = new PointOfInterestTemplateSpec { Kind = POIType.Cave, Variant = "Test" }.Held(PointOfInterestHolder.Ruined);
			Assert.That(PointOfInterestTemplateAuthoring.KindFeatures(cave, 2).Any(f => f is DungeonEntranceFeature), Is.True);
		}

		// ── The variants ──────────────────────────────────────────

		[Test]
		public void EveryVariant_IsInTheCatalogue_WithItsHolder()
		{
			foreach ((POIType kind, string variant, PointOfInterestHolder holder, bool boss) in Variants)
			{
				PointOfInterestTemplateSpec spec = Spec(kind, variant);
				Assert.That(spec, Is.Not.Null, $"{kind} - {variant}");
				Assert.That(spec.Holder, Is.EqualTo(holder), spec.Name);
				Assert.That(spec.HolderBoss, Is.EqualTo(boss), spec.Name);
				Assert.That(spec.Sizes().Count(), Is.EqualTo(3), $"{spec.Name}: every size");
			}
			// The older variants are their garrisons'; the wizard's tower stays a neutral mage's.
			foreach (string variant in new[] { "Stone", "Palisade" })
			{
				Assert.That(Spec(POIType.Keep, variant).Holder, Is.EqualTo(PointOfInterestHolder.Garrison), variant);
			}
			foreach (string variant in new[] { "Timber", "Stone", "Wizard", "Observatory" })
			{
				Assert.That(Spec(POIType.Tower, variant).Holder, Is.EqualTo(PointOfInterestHolder.Garrison), variant);
			}
			// A keep takes a race, so the hostile ones are held by their peoples and one variant still fits any race; a tower takes none.
			Assert.That(Spec(POIType.Keep, "Orc Hold").RaceCategories, Is.EquivalentTo(new[] { "Humanoid", "Beastfolk", "Giant" }));
			Assert.That(Spec(POIType.Keep, "Dark Keep").RaceCategories, Is.EquivalentTo(new[] { "Undead", "Humanoid" }));
			Assert.That(Spec(POIType.Keep, "Ruined Keep").RaceCategories, Is.Empty);
			Assert.That(PointOfInterestTemplateSpecs.For(POIType.Tower).All(s => s.RaceCategories.Count == 0), Is.True);
			// The spires are laid by id.
			Assert.That(Spec(POIType.Tower, "Dark Tower").Pieces[0].Tag, Is.EqualTo("=DarkSpire"));
			Assert.That(Spec(POIType.Tower, "Elven Tower").Pieces[0].Tag, Is.EqualTo("=ElvenSpire"));
		}

		[Test]
		public void EveryVariant_ResolvesInTheKit()
		{
			foreach ((POIType kind, string variant, _, _) in Variants)
			{
				foreach (PointOfInterestPieceSlot slot in Spec(kind, variant).Pieces)
				{
					Assert.That(StructureKitPieceSource.Resolves(slot.Tag), Is.True, $"{kind} - {variant}: '{slot.Tag}'");
				}
			}
		}

		/// <summary>Lays every new variant at every size as PointOfInterestTemplateTests does: its centrepiece stands, its wall ring fits, nothing overlaps.</summary>
		[Test]
		public void EveryVariant_LaysWithoutOverlapAtEverySize()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			foreach ((POIType kind, string variant, _, _) in Variants)
			{
				PointOfInterestTemplateSpec spec = Spec(kind, variant);
				PointOfInterestKindRule rule = rules.RuleFor(kind);
				foreach (int size in spec.Sizes())
				{
					float radius = Mathf.Lerp(rule.FootprintMin, rule.FootprintMax, size / 2f);
					var random = new DeterministicRNG(1000 + size);
					var request = new PointOfInterestLayoutRequest { Layout = spec.Layout, Radius = radius, Gap = 1f, SpanHalf = radius, ShoreAhead = radius * PointOfInterestLayouts.Inset };
					for (int s = 0; s < spec.Pieces.Count; s++)
					{
						PointOfInterestPieceSlot slot = spec.Pieces[s];
						string style = string.IsNullOrEmpty(slot.Style) ? spec.Style : slot.Style;
						PointOfInterestSlotRole role = PointOfInterestLayouts.RoleFor(spec.Layout, s, slot.Role);
						int count = PropsFeature.CountFor(slot, size, true, random);
						PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, 0));
						bool station = role == PointOfInterestSlotRole.Gate || role == PointOfInterestSlotRole.WallTower;
						request.Slots.Add(new PointOfInterestLayoutSlot
						{
							Role = role, Footprint = metrics.Footprint, Module = metrics.Module, Count = count,
							Walk = role == PointOfInterestSlotRole.Perimeter ? PropsFeature.PerimeterWalk(slot.Tag, style) : 0f,
							Join = station ? PropsFeature.StationJoin(slot.Tag, style, 0) : 0f,
						});
						if (PointOfInterestLayouts.Generates(role))
						{
							continue;
						}
						for (int k = 0; k < count; k++)
						{
							int seed = random.Next();
							float decay = random.Range(slot.MinDecay, Mathf.Max(slot.MinDecay, slot.MaxDecay));
							float scale = random.Range(slot.MinScale, Mathf.Max(slot.MinScale, slot.MaxScale));
							PointOfInterestPieceMetrics piece = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, seed, decay));
							request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = piece.Footprint * scale });
						}
					}
					List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, random);
					string name = $"{spec.Name} at size {size} ({radius:0} m)";
					int centre = spec.Pieces.FindIndex(p => p.Role == PointOfInterestSlotRole.Centre);
					Assert.That(pieces.Exists(p => p.Slot == centre), Is.True, $"{name}: its centrepiece stands");
					int wall = spec.Pieces.FindIndex(p => p.Role == PointOfInterestSlotRole.Perimeter);
					if (wall >= 0)
					{
						Assert.That(pieces.Count(p => p.Slot == wall), Is.GreaterThanOrEqualTo(3), $"{name}: its wall ring fits");
					}
					for (int i = 0; i < pieces.Count; i++)
					{
						for (int j = i + 1; j < pieces.Count; j++)
						{
							if (Ring(pieces[i]) && Ring(pieces[j]))
							{
								continue;
							}
							Assert.That(Overlap(pieces[i], pieces[j]), Is.LessThan(0.05f), $"{name}: {pieces[i].Role} at {pieces[i].Offset} and {pieces[j].Role} at {pieces[j].Offset} overlap");
						}
					}
					Console.WriteLine($"[keeps] {name}: {pieces.Count} pieces of {request.Items.Count} asked");
				}
			}
		}

		private static bool Ring(PointOfInterestLayoutPiece p)
			=> p.Role == PointOfInterestSlotRole.Perimeter || p.Role == PointOfInterestSlotRole.Gate || p.Role == PointOfInterestSlotRole.WallTower;

		/// <summary>How far two footprints overlap along their least-overlapping axis, metres (as PointOfInterestTemplateTests measures).</summary>
		private static float Overlap(PointOfInterestLayoutPiece a, PointOfInterestLayoutPiece b)
		{
			var ca = new Vector2[4];
			var cb = new Vector2[4];
			PointOfInterestLayouts.Corners(a.Offset, a.Yaw, a.Footprint, ca);
			PointOfInterestLayouts.Corners(b.Offset, b.Yaw, b.Footprint, cb);
			float least = float.PositiveInfinity;
			foreach (Vector2[] poly in new[] { ca, cb })
			{
				for (int e = 0; e < 4; e++)
				{
					Vector2 edge = poly[(e + 1) % 4] - poly[e];
					if (edge.sqrMagnitude < 1e-8f)
					{
						continue;
					}
					Vector2 axis = new Vector2(-edge.y, edge.x).normalized;
					float minA = float.PositiveInfinity, maxA = float.NegativeInfinity, minB = float.PositiveInfinity, maxB = float.NegativeInfinity;
					foreach (Vector2 p in ca) { float d = Vector2.Dot(p, axis); minA = Mathf.Min(minA, d); maxA = Mathf.Max(maxA, d); }
					foreach (Vector2 p in cb) { float d = Vector2.Dot(p, axis); minB = Mathf.Min(minB, d); maxB = Mathf.Max(maxB, d); }
					least = Mathf.Min(least, Mathf.Min(maxA, maxB) - Mathf.Max(minA, minB));
				}
			}
			return float.IsPositiveInfinity(least) ? 0f : least;
		}

		/// <summary>The authored templates carry the holder's features: the props first, then the band (builds the pieces to reserve the spots).</summary>
		[Test]
		public void TheAuthoredTemplates_CarryTheHoldersFeatures()
		{
			List<PointOfInterestFeature> dark = PointOfInterestTemplateAuthoring.FeaturesFor(Spec(POIType.Tower, "Dark Tower"), 2);
			Assert.That(dark[0], Is.InstanceOf<PropsFeature>());
			Assert.That(Words(dark.Skip(1)), Is.EqualTo(new[] { "RegionFeature", "ExplorationFeature", "MonsterPack", "Elite", "BossSpawnerFeature" }));
			List<PointOfInterestFeature> ruin = PointOfInterestTemplateAuthoring.FeaturesFor(Spec(POIType.Keep, "Ruined Keep"), 1);
			Assert.That(ruin.Any(f => f is WaypointFeature || f is NpcSpawnerFeature), Is.False);
			List<PointOfInterestFeature> elves = PointOfInterestTemplateAuthoring.FeaturesFor(Spec(POIType.Keep, "Elven Hall"), 1);
			Assert.That(elves.OfType<WaypointFeature>().Count(), Is.EqualTo(1), "a garrison keeps its waypoint");
		}

		// ── The pieces ────────────────────────────────────────────

		[Test]
		public void TheSpires_AreInTheKit_WithADoorARoomAndTheirOwnStone()
		{
			foreach ((string id, StructureMaterial stone, float tallest) in new[] { ("DarkSpire", StructureMaterial.Slate, 21f), ("ElvenSpire", StructureMaterial.Marble, 22f) })
			{
				StructurePiece piece = StructurePieces.Find(id);
				Assert.That(piece, Is.Not.Null, id);
				Assert.That(piece.Style, Is.EqualTo(StructureStyle.Stone), id);
				Assert.That(piece.Size, Is.EqualTo(StructureSize.Building), id);
				Assert.That(piece.Variants, Is.EqualTo(2), id);
				Assert.That(piece.Tags, Is.EquivalentTo(id == "DarkSpire" ? new[] { "building", "tower", "wizard" } : new[] { "building", "tower" }), id);
				Assert.That(piece.Height, Is.InRange(tallest, 25f), $"{id}: a spire, under the kit's 25 m");
				for (int v = 0; v < piece.Variants; v++)
				{
					Assert.That(StructurePieces.DoorsOf(piece, v).Count, Is.EqualTo(1), $"{id} v{v}: one door");
					Assert.That(StructurePieces.RoomsOf(piece, v).Count, Is.EqualTo(1), $"{id} v{v}: its room at the foot");
					Assert.That(StructurePieces.EmittersOf(piece, v).Count, Is.LessThanOrEqualTo(1), $"{id} v{v}");
					int previous = int.MaxValue;
					for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
					{
						List<StructureSolid> solids = StructurePieces.BuildSolids(piece, v, lod, StructurePieces.DefaultSeed);
						Assert.That(solids.Exists(s => s.Material == stone), Is.True, $"{id} v{v} L{lod}: built of {stone}");
						Assert.That(solids.Where(s => s.Material == stone).All(s => s.Part == null), Is.True, $"{id} v{v} L{lod}: its {stone} plays no palette part, so no biome re-lays it");
						int tris = StructurePieces.Build(piece, v, lod, StructurePieces.DefaultSeed).Mesh.TriangleCount;
						Assert.That(tris, Is.LessThanOrEqualTo(lod == 0 ? piece.TriangleBudget : previous), $"{id} v{v} L{lod}: {tris} triangles");
						previous = tris;
					}
				}
			}
			Assert.That(StructurePieces.EmittersOf(StructurePieces.Find("DarkSpire"), 1).Single().kind, Is.EqualTo(FishMMO.Shared.SceneEmitterKind.Fire), "Barad-dûr's fire bowl");
			Assert.That(StructurePieces.EmittersOf(StructurePieces.Find("DarkSpire"), 0).Single().kind, Is.EqualTo(FishMMO.Shared.SceneEmitterKind.Glow), "Orthanc's orb");
		}

		// ── The rows ──────────────────────────────────────────────

		[Test]
		public void KeepsAndTowers_AreDenser_AndHeldByMorePeoples()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			PointOfInterestKindRule keep = rules.RuleFor(POIType.Keep), tower = rules.RuleFor(POIType.Tower);
			Assert.That(keep.PerKm2, Is.EqualTo(0.10f).Within(1e-6f));
			Assert.That(keep.Max, Is.EqualTo(3));
			Assert.That(tower.PerKm2, Is.EqualTo(0.20f).Within(1e-6f));
			Assert.That(tower.Max, Is.EqualTo(4));
			foreach (PointOfInterestKindRule rule in new[] { keep, tower })
			{
				Assert.That(rule.RaceCategories, Is.SupersetOf(new[] { "Humanoid", "Beastfolk", "Giant", "Undead" }), rule.Kind.ToString());
			}
		}
	}
}
