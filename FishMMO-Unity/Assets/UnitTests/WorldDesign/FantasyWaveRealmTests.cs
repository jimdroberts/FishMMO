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
	/// The fantasy wave's Realm pack (2026-10-27, memory poi-fantasy-wave/WAVE.md): its 33 kinds (30 universal, 3 strongholds)
	/// each have a template, the most important have two or three distinct variants, every slot resolves in the kit, and the
	/// rows carry the ground gates their sketches imply (a troll's bridge at a river crossing, a beacon on exposed ground).
	/// </summary>
	[TestFixture]
	public class FantasyWaveRealmTests
	{
		private static readonly string[] Realm =
		{
			"DragonLair", "TrollBridge", "GiantsSteading", "GoblinWarren", "HauntedManor", "HangingTree", "BuriedHoard", "AmbushSite",
			"WreckedCaravan", "Crossroads", "BoundaryStone", "SignalBeacon", "TollGate", "Hamlet", "ManorHouse", "SiegeCamp",
			"BurnedVillage", "WarbandCamp", "CultistHideout", "AncientWall", "SpellScar", "PlanarRift", "PetrifiedBattle", "TitansBones",
			"WishingStone", "ElderMonolith", "WaygateRing", "GolemGraveyard", "KoboldDelving", "LostLibrary",
			"Commandery", "LostThrone", "RoyalMausoleum",
		};

		private static IEnumerable<POIType> Kinds() => Realm.Select(n => (POIType)Enum.Parse(typeof(POIType), n));

		[OneTimeSetUp]
		public void RegisterKit() => StructureKitPieceSource.Register();

		[Test]
		public void EveryRealmKindHasATemplate_AndTheGreatOnesTwoOrThreeVariants()
		{
			List<PointOfInterestTemplateSpec> specs = PointOfInterestTemplateSpecs.All();
			Assert.That(Realm.Length, Is.EqualTo(33));
			foreach (POIType kind in Kinds())
			{
				Assert.That(specs.Count(s => s.Kind == kind), Is.GreaterThanOrEqualTo(1), $"{kind} has a template");
			}
			foreach (POIType kind in new[] { POIType.DragonLair, POIType.GiantsSteading, POIType.HauntedManor, POIType.WarbandCamp, POIType.ManorHouse, POIType.Hamlet, POIType.SignalBeacon, POIType.LostLibrary })
			{
				int n = specs.Count(s => s.Kind == kind);
				Assert.That(n, Is.InRange(2, 3), $"{kind} has two or three variants");
				Assert.That(specs.Where(s => s.Kind == kind).Select(s => s.Variant).Distinct().Count(), Is.EqualTo(n), $"{kind}: distinct variant names");
			}
		}

		[Test]
		public void EveryRealmSlotResolvesInTheKit_AndEveryTemplateTakesASizeAndAnyRaceStillFits()
		{
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All().Where(s => Kinds().Contains(s.Kind)))
			{
				Assert.That(spec.Pieces, Is.Not.Empty, spec.Name);
				foreach (PointOfInterestPieceSlot slot in spec.Pieces)
				{
					string style = string.IsNullOrEmpty(slot.Style) ? spec.Style : slot.Style;
					Assert.That(StructureKitPieceSource.Candidates(slot.Tag, style), Is.Not.Empty, $"{spec.Name}: '{slot.Tag}' resolves");
				}
				PointOfInterestKindInfo info = PointOfInterestKinds.Info(spec.Kind);
				if (info.Has(PointOfInterestTraits.UsesRace))
				{
					Assert.That(PointOfInterestTemplateSpecs.All().Exists(s => s.Kind == spec.Kind && s.RaceCategories.Count == 0 && s.Small && s.Medium && s.Large), Is.True, $"{spec.Kind} keeps an unfiltered variant for every size");
				}
			}
		}

		[Test]
		public void TheRowsCarryTheirGates_AndStayHostedByNoBiome()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			float density = PointOfInterestRules.FantasyWaveDensity;
			Assert.That(rules.RuleFor(POIType.TrollBridge).RiverCrossing, Is.True, "a troll's bridge is sited where a river crosses a way");
			Assert.That(rules.RuleFor(POIType.SignalBeacon).Exposed, Is.True, "a beacon stands on exposed ground");
			Assert.That(rules.RuleFor(POIType.KoboldDelving).Face, Is.True, "a delving is cut into a face");
			Assert.That(rules.RuleFor(POIType.DragonLair).PerKm2, Is.EqualTo(0.02f * density).Within(1e-6f));
			Assert.That(rules.RuleFor(POIType.DragonLair).Max, Is.EqualTo(1));
			Assert.That(rules.RuleFor(POIType.WaygateRing).Max, Is.EqualTo(1));
			foreach (POIType kind in Kinds())
			{
				PointOfInterestKindRule rule = rules.RuleFor(kind);
				Assert.That(rule.DefaultBiomeWeight, Is.EqualTo(0f), $"{kind}: hosted by no biome by default");
				Assert.That(rule.Biomes, Is.Empty, kind.ToString());
				Assert.That(rule.PerKm2, Is.InRange(0.01f * density, 0.1f * density + 1e-6f), $"{kind}: a budget in the stated band");
				Assert.That(rule.FootprintMin, Is.LessThanOrEqualTo(rule.FootprintMax), kind.ToString());
			}
		}

		/// <summary>
		/// The layout check of PointOfInterestTemplateTests.EveryTemplateLaysWithoutOverlapAtEverySize, for this pack only and reporting
		/// every template that fails (not just the first): each lays its centrepiece and its wall ring, nothing overlaps, all inside.
		/// </summary>
		[Test]
		public void EveryRealmTemplateLaysWithoutOverlapAtEverySize()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			System.Reflection.MethodInfo packed = typeof(PointOfInterestTemplateTests).GetMethod("AssertPacked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			var failures = new List<string>();
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All().Where(s => Kinds().Contains(s.Kind)))
			{
				PointOfInterestKindRule rule = rules.RuleFor(spec.Kind);
				foreach (int size in spec.Sizes())
				{
					float radius = UnityEngine.Mathf.Lerp(rule.FootprintMin, rule.FootprintMax, size / 2f);
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
							float decay = random.Range(slot.MinDecay, UnityEngine.Mathf.Max(slot.MinDecay, slot.MaxDecay));
							float scale = random.Range(slot.MinScale, UnityEngine.Mathf.Max(slot.MinScale, slot.MaxScale));
							PointOfInterestPieceMetrics piece = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, style, seed, decay));
							request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = piece.Footprint * scale });
						}
					}
					List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, random);
					string name = $"{spec.Name} at size {size} ({radius:0} m)";
					try
					{
						packed.Invoke(null, new object[] { name, request, pieces });
						int centreSlot = spec.Pieces.FindIndex(p => PointOfInterestLayouts.RoleFor(spec.Layout, spec.Pieces.IndexOf(p), p.Role) == PointOfInterestSlotRole.Centre);
						if (centreSlot >= 0 && request.Slots[centreSlot].Count > 0 && !pieces.Exists(p => p.Slot == centreSlot))
						{
							failures.Add($"{name}: its centrepiece does not stand");
						}
						int wallSlot = spec.Pieces.FindIndex(p => p.Role == PointOfInterestSlotRole.Perimeter);
						if (wallSlot >= 0 && pieces.FindAll(p => p.Slot == wallSlot).Count < 3)
						{
							failures.Add($"{name}: its wall ring does not fit");
						}
						int asked = 0, laid = 0;
						for (int i = 0; i < request.Items.Count; i++)
						{
							if (spec.Pieces[request.Items[i].Slot].Role == PointOfInterestSlotRole.Street)
							{
								asked++;
								laid += pieces.Exists(p => p.Item == i) ? 1 : 0;
							}
						}
						if (asked > 0 && laid < asked * 0.6f)
						{
							failures.Add($"{name}: only {laid} of {asked} houses found room on its streets");
						}
					}
					catch (System.Reflection.TargetInvocationException e)
					{
						failures.Add(e.InnerException?.Message.Trim().Split('\n')[0] ?? name);
					}
				}
			}
			Assert.That(failures, Is.Empty, string.Join("\n", failures));
		}
	}
}
