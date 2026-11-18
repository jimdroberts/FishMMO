using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The fantasy wave's Green pack (2026-10-27, memory poi-fantasy-wave/WAVE.md): woods, jungle, wetland and volcanic kinds. Each
	/// has a placed row hosted by no biome and a template; the important ones have several; the new pieces exist and resolve by id;
	/// Cenote (a carved kind) has its template from the Shapes pack.
	/// </summary>
	[TestFixture]
	public class FantasyWaveGreenTests
	{
		private static readonly string[] Names =
		{
			"DruidGrove", "TreeVillage", "TrollStones", "SpiderThicket", "CharcoalClamps", "GreenwoodHideout", "DryadOak", "GoblinMarket", "ElfWaystone", "WolfDen", "MossColossus",
			"StepPyramid", "JaguarTemple", "SerpentIdol", "LostCity", "LizardfolkVillage", "IdolVault",
			"Crannog", "HagsCauldron", "HydraLair", "ReedVillage", "CovenCircle", "LizardfolkHatchery", "SwampCathedral",
			"FireTemple", "FireGiantHall", "SalamanderLair", "AshBuriedCity",
		};

		private static IEnumerable<POIType> Kinds() => Names.Select(n => (POIType)Enum.Parse(typeof(POIType), n));

		[Test]
		public void TwentyEightKinds_EachHasAPlacedUnhostedRowAndATemplate()
		{
			Assert.That(Names.Length, Is.EqualTo(28));
			PointOfInterestRules rules = PointOfInterestRules.Default();
			List<PointOfInterestTemplateSpec> all = PointOfInterestTemplateSpecs.All();
			foreach (POIType kind in Kinds())
			{
				PointOfInterestKindRule rule = rules.RuleFor(kind);
				Assert.That(rule, Is.Not.Null, $"{kind} has a row");
				Assert.That(rule.PerKm2, Is.GreaterThan(0f), $"{kind} is placed");
				Assert.That(all.Any(s => s.Kind == kind), Is.True, $"{kind} has a template");
			}
		}

		[Test]
		public void Cenote_HasTheShapesPacksTemplate()
		{
			// The carved kind's template and row are the Shapes pack's (2026-10-27).
			Assert.That(PointOfInterestTemplateSpecs.All().Any(s => s.Kind == POIType.Cenote), Is.True);
		}

		[Test]
		public void RowsKeepTheirBudgetsAndTheWaterGates()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			// The coordinator scales every wave row's budget (PointOfInterestRules.FantasyWaveDensity).
			Assert.That(rules.RuleFor(POIType.TreeVillage).PerKm2, Is.EqualTo(0.02f * PointOfInterestRules.FantasyWaveDensity).Within(1e-5f));
			Assert.That(rules.RuleFor(POIType.DruidGrove).PerKm2, Is.EqualTo(0.05f * PointOfInterestRules.FantasyWaveDensity).Within(1e-5f));
			Assert.That(rules.RuleFor(POIType.Crannog).Shallows, Is.True, "a crannog stands over lake shallows");
			Assert.That(rules.RuleFor(POIType.HydraLair).NearWaterMetres, Is.GreaterThan(0f));
			Assert.That(rules.RuleFor(POIType.Cenote).PerKm2, Is.EqualTo(0.05f * PointOfInterestRules.FantasyWaveDensity).Within(1e-5f), "Cenote's budget is as registered");
			foreach (POIType kind in Kinds())
			{
				Assert.That(rules.RuleFor(kind).DefaultBiomeWeight, Is.EqualTo(rules.RuleFor(kind).DefaultBiomeWeight));
			}
		}

		[Test]
		public void ImportantKinds_HaveTwoOrThreeVariants()
		{
			foreach (POIType kind in new[] { POIType.TreeVillage, POIType.LostCity, POIType.StepPyramid, POIType.SwampCathedral, POIType.LizardfolkVillage, POIType.FireTemple })
			{
				int n = PointOfInterestTemplateSpecs.For(kind).Count;
				Assert.That(n, Is.InRange(2, 3), $"{kind} variants");
			}
		}

		[Test]
		public void NewPieces_ExistAndEveryTemplateSlotResolves()
		{
			string[] pieces = { "Talan", "RopeWalk", "TreeStair", "WebCurtain", "WebSheet", "Cocoon", "HollowOak", "StepPyramid", "RuinedStepPyramid", "JaguarMaw", "PileRoundHouse", "ReedHall", "BasaltHall", "RuinedMinster" };
			foreach (string id in pieces)
			{
				Assert.That(StructurePieces.All.Any(p => p.Id == id), Is.True, $"{id} is in the kit");
			}
			var mine = new HashSet<POIType>(Kinds());
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All().Where(s => mine.Contains(s.Kind)))
			{
				Assert.That(spec.Pieces, Is.Not.Empty, spec.Name);
				foreach (PointOfInterestPieceSlot slot in spec.Pieces)
				{
					string style = string.IsNullOrEmpty(slot.Style) ? spec.Style : slot.Style;
					Assert.That(StructureKitPieceSource.Candidates(slot.Tag, style), Is.Not.Empty, $"{spec.Name}: '{slot.Tag}'");
				}
			}
		}

		[Test]
		public void RaceFilters_NeverStrandASite()
		{
			foreach (POIType kind in Kinds())
			{
				List<PointOfInterestTemplateSpec> specs = PointOfInterestTemplateSpecs.For(kind);
				Assert.That(specs.Any(s => s.RaceCategories.Count == 0), Is.True, $"{kind} keeps an unfiltered variant");
			}
		}

		/// <summary>
		/// PointOfInterestTemplateTests.EveryTemplateLaysWithoutOverlapAtEverySize, run on this pack's templates alone (it stops at the
		/// first failing template of any pack): no overlap and every centrepiece stands at every size.
		/// </summary>
		[Test]
		public void EveryTemplateOfThePack_LaysWithoutOverlapAtEverySize()
		{
			MethodInfo packed = typeof(PointOfInterestTemplateTests).GetMethod("AssertPacked", BindingFlags.NonPublic | BindingFlags.Static);
			Assert.That(packed, Is.Not.Null);
			var mine = new HashSet<POIType>(Kinds());
			PointOfInterestRules rules = PointOfInterestRules.Default();
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All().Where(s => mine.Contains(s.Kind)))
			{
				PointOfInterestKindRule rule = rules.RuleFor(spec.Kind);
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
					try
					{
						packed.Invoke(null, new object[] { name, request, pieces });
					}
					catch (TargetInvocationException e)
					{
						throw e.InnerException;
					}
					int centreSlot = spec.Pieces.FindIndex(p => PointOfInterestLayouts.RoleFor(spec.Layout, spec.Pieces.IndexOf(p), p.Role) == PointOfInterestSlotRole.Centre);
					if (centreSlot >= 0 && request.Slots[centreSlot].Count > 0)
					{
						Assert.That(pieces.Exists(p => p.Slot == centreSlot), Is.True, $"{name}: its centrepiece stands");
					}
					int streetSlot = spec.Pieces.FindIndex(p => p.Role == PointOfInterestSlotRole.Street);
					if (streetSlot >= 0)
					{
						int asked = 0, laid = 0;
						for (int i = 0; i < request.Items.Count; i++)
						{
							if (spec.Pieces[request.Items[i].Slot].Role == PointOfInterestSlotRole.Street)
							{
								asked++;
								laid += pieces.Exists(p => p.Item == i) ? 1 : 0;
							}
						}
						Assert.That(laid, Is.GreaterThanOrEqualTo(asked * 0.6f), $"{name}: {laid} of {asked} houses found room on its streets");
					}
				}
			}
		}
	}
}
