using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The fantasy wave's Dry pack (2026-10-27, memory poi-fantasy-wave/WAVE.md, packs G, A and H): 33 kinds, every one but the three
	/// carved ones (the Shapes agent's) with a template, its important kinds with variants, its eight new pieces in the kit and
	/// laid by the templates that name them, its face kinds on faces, and its rows hosted by no biome. Pure (no meshes).
	/// </summary>
	[TestFixture]
	public class FantasyWaveDryTests
	{
		private static readonly POIType[] Built =
		{
			POIType.Grange, POIType.Dovecote, POIType.BeeGarden, POIType.VillageGreen, POIType.ScarecrowField, POIType.LongBarrow, POIType.Henge,
			POIType.GallowsHill, POIType.KurganField, POIType.NomadCamp, POIType.StoneFigures, POIType.Kraal, POIType.Baobab, POIType.Shieling,
			POIType.Necropolis, POIType.Pyramid, POIType.Sphinx, POIType.RockCutFacade, POIType.SandBuriedTemple, POIType.DjinnPrison,
			POIType.CaravanHalt, POIType.BeastBoneyard, POIType.CliffPalace, POIType.DreadRoad,
			POIType.MountainKingsHall, POIType.PassFort, POIType.Eyrie, POIType.GriffinRoost, POIType.WyvernNest, POIType.CliffRelief,
			POIType.SummitCairn, POIType.PrayerFlagPass, POIType.SilverMineTown,
		};

		private static readonly POIType[] Carved = { POIType.SandwormPit, POIType.DwarvenGate, POIType.HangingMonastery };

		private static readonly string[] NewPieces =
			{ "RoundHut", "Baobab", "Pyramid", "Sphinx", "RockCutFacade", "AlcoveHouse", "CarvedHallFront", "CliffRelief" };

		[Test]
		public void EveryBuiltKindHasATemplate_AndTheCarvedOnesTheShapesPacks()
		{
			List<PointOfInterestTemplateSpec> all = PointOfInterestTemplateSpecs.All();
			foreach (POIType kind in Built)
			{
				Assert.That(all.Any(s => s.Kind == kind), Is.True, $"{kind} has a template");
			}
			foreach (POIType kind in Carved)
			{
				// The carved kinds' templates are the Shapes pack's (PointOfInterestTemplateSpecs.Fantasy.Shapes.cs, 2026-10-27).
				Assert.That(all.Count(s => s.Kind == kind), Is.GreaterThanOrEqualTo(1), $"{kind} has the Shapes pack's template");
			}
		}

		[Test]
		public void ImportantKindsHaveTwoOrThreeVariants()
		{
			foreach (POIType kind in new[] { POIType.Necropolis, POIType.Pyramid, POIType.MountainKingsHall, POIType.NomadCamp, POIType.Grange, POIType.SilverMineTown })
			{
				int n = PointOfInterestTemplateSpecs.For(kind).Count;
				Assert.That(n, Is.InRange(2, 3), $"{kind} has 2-3 variants");
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
		public void FaceKinds_StandOnFaces_AndEveryRowIsHostedByNoBiome()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			foreach (POIType kind in new[] { POIType.RockCutFacade, POIType.CliffPalace, POIType.CliffRelief })
			{
				Assert.That(rules.RuleFor(kind).Face, Is.True, $"{kind} is on a cliff face");
				Assert.That(rules.RuleFor(kind).MinHardness, Is.GreaterThan(0f), kind.ToString());
			}
			foreach (POIType kind in Built)
			{
				PointOfInterestKindRule rule = rules.RuleFor(kind);
				Assert.That(rule.DefaultBiomeWeight, Is.EqualTo(0f), kind.ToString());
				Assert.That(rule.Biomes, Is.Empty, kind.ToString());
				Assert.That(rule.PerKm2, Is.InRange(0.001f, 0.1f * PointOfInterestRules.FantasyWaveDensity + 1e-5f), $"{kind}: a budget of the pack's size");
				Assert.That(rule.Max, Is.GreaterThan(0), kind.ToString());
			}
		}

		[Test]
		public void TheLandformKinds_StandOnExposedHighGround()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			foreach (POIType kind in new[] { POIType.PassFort, POIType.SummitCairn, POIType.PrayerFlagPass })
			{
				Assert.That(rules.RuleFor(kind).Exposed, Is.True, kind.ToString());
				Assert.That(rules.RuleFor(kind).MinAltitude, Is.GreaterThan(0f), kind.ToString());
			}
		}
	}
}
