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
	/// The fantasy wave's Cold pack (2026-10-27, memory poi-fantasy-wave/WAVE.md, packs C, Co and S): 21 built kinds (the tidal island
	/// abbey is the Shapes pack's, its templates there), each with templates, its important kinds with variants, its nine new pieces in the
	/// kit and laid by a template that names them, its rows' ground gates and budgets (written density times the wave's density), and the
	/// pieces' meshes (the lich's spire is the Keeps pack's black tower in frost, the longship beached on the kit's own hull). The row and
	/// template tests are pure; the piece tests build meshes (nativerun outside Unity).
	/// </summary>
	[TestFixture]
	public class FantasyWaveColdTests
	{
		[OneTimeSetUp]
		public void RegisterKit() => StructureKitPieceSource.Register();

		private static readonly POIType[] Built =
		{
			POIType.FrostGiantSteading, POIType.RuneStone, POIType.FrozenArmy, POIType.DragonInTheIce, POIType.MammothGraveyard, POIType.WinterWolfDen,
			POIType.AuroraShrine, POIType.LichSpire, POIType.IcePalace, POIType.FrozenShipwreck,
			POIType.CliffCastle, POIType.SmugglersTunnel, POIType.WreckersBeacon, POIType.SirenRock, POIType.SeaWitchGrotto, POIType.LongshipLanding, POIType.KrakenWreck,
			POIType.MerfolkVillage, POIType.KrakenGrounds, POIType.LadyOfTheLake, POIType.NymphPool, POIType.LakeMonsterLair,
		};

		private static readonly string[] NewPieces =
			{ "IceBlockHall", "IceEncasedDragon", "LichSpire", "IceSpireHall", "IceSpire", "FrozenSoldier", "BeachedLongship", "CoralHall", "ShellHouse" };

		/// <summary>The density each row is written at (before the wave's density scales it), per kind.</summary>
		private static readonly Dictionary<POIType, float> Written = new Dictionary<POIType, float>
		{
			{ POIType.FrostGiantSteading, 0.02f }, { POIType.RuneStone, 0.08f }, { POIType.FrozenArmy, 0.05f }, { POIType.DragonInTheIce, 0.02f },
			{ POIType.MammothGraveyard, 0.08f }, { POIType.WinterWolfDen, 0.08f }, { POIType.AuroraShrine, 0.05f }, { POIType.LichSpire, 0.02f },
			{ POIType.IcePalace, 0.02f }, { POIType.FrozenShipwreck, 0.05f }, { POIType.CliffCastle, 0.02f }, { POIType.SmugglersTunnel, 0.08f },
			{ POIType.WreckersBeacon, 0.08f }, { POIType.SirenRock, 0.05f }, { POIType.SeaWitchGrotto, 0.05f }, { POIType.LongshipLanding, 0.05f },
			{ POIType.KrakenWreck, 0.05f }, { POIType.MerfolkVillage, 0.02f }, { POIType.KrakenGrounds, 0.02f }, { POIType.LadyOfTheLake, 0.05f },
			{ POIType.NymphPool, 0.08f }, { POIType.LakeMonsterLair, 0.05f },
		};

		[Test]
		public void EveryBuiltKindHasATemplate_AndTheAbbeyHasNone()
		{
			List<PointOfInterestTemplateSpec> all = PointOfInterestTemplateSpecs.All();
			foreach (POIType kind in Built)
			{
				Assert.That(all.Any(s => s.Kind == kind), Is.True, $"{kind} has a template");
				Assert.That(PointOfInterestKinds.Info(kind).Has(PointOfInterestTraits.Structure), Is.True, $"{kind} is built by the kit");
			}
			Assert.That(all.Any(s => s.Kind == POIType.TidalIslandAbbey), Is.True, "the abbey's templates are the Shapes pack's (2026-10-27)");
		}

		[Test]
		public void ImportantKindsHaveTwoOrThreeVariants_AndTheRestHaveAtLeastOne()
		{
			foreach (POIType kind in new[] { POIType.IcePalace, POIType.FrostGiantSteading, POIType.MerfolkVillage, POIType.CliffCastle, POIType.LongshipLanding })
			{
				Assert.That(PointOfInterestTemplateSpecs.For(kind).Count, Is.InRange(2, 3), $"{kind} has 2-3 variants");
			}
			foreach (POIType kind in Built)
			{
				Assert.That(PointOfInterestTemplateSpecs.For(kind).Count, Is.InRange(1, 3), kind.ToString());
			}
		}

		[Test]
		public void KindsWithARace_KeepAnUnfilteredTemplate()
		{
			foreach (POIType kind in Built)
			{
				Assert.That(PointOfInterestTemplateSpecs.For(kind).Any(s => s.RaceCategories.Count == 0), Is.True, $"{kind} keeps a template that fits every site");
			}
		}

		[Test]
		public void TheRuinedVariants_AreHeldByNobody_AndTheIceKindsHoldNoRaceFilterOnTheirBigVariants()
		{
			foreach (var (kind, variant) in new[] { (POIType.FrostGiantSteading, "Sundered Steading"), (POIType.IcePalace, "Fallen Court"), (POIType.CliffCastle, "Ruined Cliff Castle"),
				(POIType.LongshipLanding, "Abandoned Landing"), (POIType.MerfolkVillage, "Drowned Reefhold") })
			{
				PointOfInterestTemplateSpec spec = PointOfInterestTemplateSpecs.For(kind).SingleOrDefault(s => s.Variant == variant);
				Assert.That(spec, Is.Not.Null, $"{kind} - {variant}");
				Assert.That(spec.Holder, Is.EqualTo(PointOfInterestHolder.Ruined), $"{kind} - {variant}");
				Assert.That(spec.RaceCategories, Is.Empty, $"{kind} - {variant} fits every site");
			}
		}

		[Test]
		public void TheUnderwaterKinds_AreBuiltWithTheSeaFloorsFinish()
		{
			foreach (POIType kind in new[] { POIType.MerfolkVillage, POIType.KrakenGrounds, POIType.LakeMonsterLair })
			{
				foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.For(kind))
				{
					Assert.That(spec.Finish, Is.EqualTo(PointOfInterestFinish.Algae), $"{spec.Name}");
					Assert.That(spec.PadFlatness, Is.EqualTo(0f), $"{spec.Name}: no flat pad on the sea floor");
				}
			}
		}

		[Test]
		public void TheNewPieces_AreInTheKit_AndEachIsLaidByATemplate()
		{
			List<PointOfInterestTemplateSpec> all = PointOfInterestTemplateSpecs.All();
			foreach (string id in NewPieces)
			{
				StructurePiece piece = StructurePieces.Find(id);
				Assert.That(piece, Is.Not.Null, $"{id} is in the kit");
				Assert.That(piece.Variants, Is.EqualTo(2), id);
				Assert.That(piece.Description, Is.Not.Empty, id);
				Assert.That(all.Any(s => s.Pieces.Any(p => StructureKitPieceSource.BaseQuery(p.Tag) == "=" + id)), Is.True, $"a template lays {id}");
			}
		}

		[Test]
		public void EveryRow_IsHostedByNoBiome_AtTheWrittenDensityTimesTheWavesAndGatedForItsGround()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			foreach (POIType kind in Built)
			{
				PointOfInterestKindRule rule = rules.RuleFor(kind);
				Assert.That(rule.DefaultBiomeWeight, Is.EqualTo(0f), kind.ToString());
				Assert.That(rule.Biomes, Is.Empty, kind.ToString());
				Assert.That(rule.PerKm2, Is.EqualTo(Written[kind] * PointOfInterestRules.FantasyWaveDensity).Within(1e-5f), $"{kind}: written density times the wave's");
				Assert.That(rule.Max, Is.InRange(1, 2), kind.ToString());
				Assert.That(rule.Placement, Is.EqualTo(PointOfInterestPlacement.Placed), kind.ToString());
			}
			// The great ones are rarer than the ordinary: tier-1 landmarks 0.02 written, ordinary 0.05-0.08.
			foreach (POIType kind in new[] { POIType.IcePalace, POIType.LichSpire, POIType.DragonInTheIce, POIType.FrostGiantSteading, POIType.CliffCastle, POIType.MerfolkVillage, POIType.KrakenGrounds })
			{
				Assert.That(rules.RuleFor(kind).Max, Is.EqualTo(1), $"{kind} is a one-off per scene");
			}
			// The sea floor, a lake bed, a shore.
			foreach (POIType kind in new[] { POIType.MerfolkVillage, POIType.KrakenGrounds, POIType.LakeMonsterLair })
			{
				Assert.That(rules.RuleFor(kind).Underwater, Is.True, kind.ToString());
			}
			Assert.That(rules.RuleFor(POIType.LakeMonsterLair).InLakes, Is.True, "the lair lies under a lake");
			Assert.That(rules.RuleFor(POIType.MerfolkVillage).MaxDepth, Is.EqualTo(30f), "the merfolk keep to the shallows");
			Assert.That(rules.RuleFor(POIType.KrakenGrounds).MinDepth, Is.GreaterThanOrEqualTo(20f), "the kraken keeps the deep");
			Assert.That(rules.RuleFor(POIType.LadyOfTheLake).NearWaterMetres, Is.GreaterThan(0f));
			Assert.That(rules.RuleFor(POIType.NymphPool).NearWaterMetres, Is.GreaterThan(0f));
			// The coast: a face for the tunnel and the grotto, exposed high ground for the headland castle and the false light, a low strand for the landing.
			Assert.That(rules.RuleFor(POIType.SmugglersTunnel).Face, Is.True);
			Assert.That(rules.RuleFor(POIType.SeaWitchGrotto).Face, Is.True);
			Assert.That(rules.RuleFor(POIType.CliffCastle).Exposed, Is.True);
			Assert.That(rules.RuleFor(POIType.CliffCastle).MinAltitude, Is.GreaterThan(0f));
			Assert.That(rules.RuleFor(POIType.WreckersBeacon).Exposed, Is.True);
			foreach (POIType kind in new[] { POIType.CliffCastle, POIType.SmugglersTunnel, POIType.WreckersBeacon, POIType.SirenRock, POIType.SeaWitchGrotto, POIType.LongshipLanding, POIType.KrakenWreck, POIType.FrozenShipwreck })
			{
				Assert.That(rules.RuleFor(kind).CoastMetres, Is.GreaterThan(0f), $"{kind} is on the coast");
			}
			Assert.That(rules.RuleFor(POIType.LongshipLanding).MaxAltitude, Is.LessThanOrEqualTo(8f), "the longships are drawn up on a low strand");
		}

		[Test]
		public void ThePiecesAreLaidByIdOnly_NoOlderQueryDrawsThem()
		{
			// Their tags are ones no template query uses; a query by one of the older queries must not reach a new piece.
			string[] queries =
			{
				PointOfInterestTemplateSpecs.House, PointOfInterestTemplateSpecs.Wall, PointOfInterestTemplateSpecs.Watchtower, PointOfInterestTemplateSpecs.Keep,
				PointOfInterestTemplateSpecs.Bones, PointOfInterestTemplateSpecs.Statue, PointOfInterestTemplateSpecs.Pier, PointOfInterestTemplateSpecs.Shipwreck,
				PointOfInterestTemplateSpecs.WallTower, PointOfInterestTemplateSpecs.Storage, PointOfInterestTemplateSpecs.Banner,
			};
			foreach (string q in queries)
			{
				foreach (string id in NewPieces)
				{
					StructurePiece piece = StructurePieces.Find(id);
					if (q.StartsWith("="))
					{
						continue;
					}
					// A query is its tags joined with '+' (all required) and '!' (none allowed).
					string[] must = q.Split('!')[0].Split('+');
					string[] mustNot = q.Split('!').Skip(1).ToArray();
					bool draws = must.All(piece.HasTag) && !mustNot.Any(piece.HasTag);
					Assert.That(draws, Is.False, $"{id} must not be drawn by the query '{q}'");
				}
			}
		}

		// ── Meshes (nativerun) ────────────────────────────────────

		private static List<StructureSolid> Solids(string id, int variant, int lod = 0) => StructurePieces.BuildSolids(StructurePieces.Find(id), variant, lod, StructurePieces.DefaultSeed);

		private static Bounds BoundsOf(string id, int variant) => StructurePieces.Build(StructurePieces.Find(id), variant, 0, StructurePieces.DefaultSeed).Mesh.Bounds;

		[Test]
		public void EveryNewPiece_BuildsAValidClosedMesh_WithinItsBudget_AtEveryLevel()
		{
			foreach (string id in NewPieces)
			{
				StructurePiece p = StructurePieces.Find(id);
				for (int v = 0; v < p.Variants; v++)
				{
					int previous = int.MaxValue;
					for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
					{
						StructureMesh m = StructurePieces.Build(p, v, lod, StructurePieces.DefaultSeed);
						Assert.That(m.Mesh.Validate(true), Is.Empty, $"{id} v{v} L{lod}");
						int tris = m.Mesh.TriangleCount;
						Assert.That(tris, Is.InRange(1, lod == 0 ? p.TriangleBudget : previous), $"{id} v{v} L{lod}: {tris} triangles");
						previous = tris;
					}
				}
			}
		}

		[Test]
		public void TheIceIsBuiltInDirectly_SoNoPaletteReLaysItInStone()
		{
			foreach (string id in new[] { "IceBlockHall", "IceEncasedDragon", "IceSpireHall", "IceSpire", "FrozenSoldier" })
			{
				for (int v = 0; v < 2; v++)
				{
					Assert.That(Solids(id, v).Any(s => s.Material == StructureMaterial.IceBlock), Is.True, $"{id} v{v} has ice");
					Assert.That(Solids(id, v).Where(s => s.Material == StructureMaterial.IceBlock).All(s => s.Part == null), Is.True, $"{id} v{v}: ice plays no palette part");
				}
			}
			Assert.That(Solids("IceBlockHall", 0).Any(s => s.Material == StructureMaterial.Bone), Is.True, "the hall hangs mammoth tusks");
			Assert.That(Solids("IceEncasedDragon", 0).Any(s => s.Material == StructureMaterial.Bone), Is.True, "the dragon's bones break out of the ice");
		}

		[Test]
		public void TheLichSpire_IsTheBlackSpireInFrost()
		{
			for (int v = 0; v < 2; v++)
			{
				List<StructureSolid> lich = Solids("LichSpire", v), dark = Solids("DarkSpire", v);
				Assert.That(lich.Count, Is.GreaterThan(dark.Count), $"v{v}: the frost is laid over the black spire");
				Assert.That(lich.Any(s => s.Material == StructureMaterial.Slate), Is.True, $"v{v} is still the black slate");
				Assert.That(lich.Any(s => s.Material == StructureMaterial.IceBlock), Is.True, $"v{v} is bound in ice");
				Assert.That(BoundsOf("LichSpire", v).max.y, Is.EqualTo(BoundsOf("DarkSpire", v).max.y).Within(0.5f), $"v{v} stands as tall as the spire it is made from");
			}
			Assert.That(StructurePieces.RoomsOf(StructurePieces.Find("LichSpire"), 0).Count, Is.GreaterThan(0), "the keeper's room at its foot");
		}

		[Test]
		public void TheDoorsOfTheHalls_AreOpenWayIn()
		{
			foreach (string id in new[] { "IceBlockHall", "IceSpireHall", "LichSpire" })
			{
				for (int v = 0; v < 2; v++)
				{
					Assert.That(StructurePieces.DoorsOf(StructurePieces.Find(id), v).Count, Is.GreaterThan(0), $"{id} v{v} has a door");
				}
			}
		}

		[Test]
		public void TheBeachedLongship_RestsOnTheGround_AndTheCoralHallsStandOnTheirFooting()
		{
			for (int v = 0; v < 2; v++)
			{
				Bounds ship = BoundsOf("BeachedLongship", v);
				Assert.That(ship.min.y, Is.GreaterThan(-0.5f), $"BeachedLongship v{v} is drawn up, not sunk");
				Assert.That(ship.size.x, Is.InRange(18f, 26f), $"BeachedLongship v{v} is a longship");
				Assert.That(Solids("BeachedLongship", v).Any(s => s.Material == StructureMaterial.Timber), Is.True);
				Bounds hall = BoundsOf("CoralHall", v);
				Assert.That(hall.min.y, Is.GreaterThanOrEqualTo(-FoundationDepthOf()), $"CoralHall v{v}");
				Assert.That(Solids("CoralHall", v).Any(s => s.Material == StructureMaterial.Faelight), Is.True, $"CoralHall v{v} burns sea-lamps");
				Assert.That(Solids("ShellHouse", v).Any(s => s.Material == StructureMaterial.Marble), Is.True, $"ShellHouse v{v} is shell");
			}
						Assert.That(Solids("BeachedLongship", 1).Count(s => s.Material == StructureMaterial.Canvas), Is.GreaterThan(Solids("BeachedLongship", 0).Count(s => s.Material == StructureMaterial.Canvas)), "v1 has more canvas than v0");
		}

		private static float FoundationDepthOf() => 1.3f;
	}
}
