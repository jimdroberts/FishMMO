using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The pure seams around the point-of-interest planner: unlock indices carried over a re-cut, names unique in a scene,
	/// races and templates drawn by seed, layouts inside their footprints, and the catalogue's rows.
	/// </summary>
	[TestFixture]
	public class PointOfInterestSeamTests
	{
		private static PointOfInterestRecord Site(int id, POIType kind, float x, float z, int unlock = -1)
			=> new PointOfInterestRecord { Id = id, Kind = kind, Position = new Vector3(x, 0f, z), UnlockIndex = unlock };

		// ── Unlock indices ────────────────────────────────────────

		[Test]
		public void UnlockIndicesCarryOverByNearestSameKind()
		{
			var previous = new List<PointOfInterestRecord>
			{
				Site(1, POIType.Village, 0f, 0f, 0),
				Site(2, POIType.Village, 1000f, 0f, 1),
				Site(3, POIType.Portal, 0f, 50f, 2),
				Site(4, POIType.Town, 3000f, 0f, 3),
			};
			var fresh = new List<PointOfInterestRecord>
			{
				Site(10, POIType.Village, 1100f, 20f),   // 102 m from village 1 → 1
				Site(11, POIType.Village, 30f, -40f),    // 50 m from village 0 → 0
				Site(12, POIType.Portal, 500f, 500f),    // too far from the portal → new
				Site(13, POIType.Shrine, 0f, 0f),        // not unlockable
				Site(14, POIType.Town, 0f, 60f),         // the town moved 3 km → new
			};
			bool Unlockable(PointOfInterestRecord r) => r.Kind != POIType.Shrine;
			PointOfInterestUnlocks.Assign(fresh, previous, Unlockable);
			Assert.That(fresh[0].UnlockIndex, Is.EqualTo(1));
			Assert.That(fresh[1].UnlockIndex, Is.EqualTo(0));
			Assert.That(fresh[3].UnlockIndex, Is.EqualTo(-1), "a kind that is not unlockable gets none");
			// The new ones take the lowest indices nobody holds: 2 and 3 are free again.
			Assert.That(fresh[2].UnlockIndex, Is.EqualTo(2));
			Assert.That(fresh[4].UnlockIndex, Is.EqualTo(3));
		}

		[Test]
		public void APreviousIndexIsUsedOnce()
		{
			var previous = new List<PointOfInterestRecord> { Site(1, POIType.Village, 0f, 0f, 5) };
			var fresh = new List<PointOfInterestRecord> { Site(10, POIType.Village, 120f, 0f), Site(11, POIType.Village, 10f, 0f) };
			PointOfInterestUnlocks.Assign(fresh, previous, r => true);
			Assert.That(fresh[1].UnlockIndex, Is.EqualTo(5), "the nearer site keeps the index");
			Assert.That(fresh[0].UnlockIndex, Is.EqualTo(0), "the other takes the lowest free one");
		}

		[Test]
		public void NoPredicateAssignsNothing()
		{
			var fresh = new List<PointOfInterestRecord> { Site(10, POIType.Village, 0f, 0f, 4) };
			PointOfInterestUnlocks.Assign(fresh, null, null);
			Assert.That(fresh[0].UnlockIndex, Is.EqualTo(-1));
		}

		// ── Names ─────────────────────────────────────────────────

		[Test]
		public void NamesAreUniqueInTheScene()
		{
			var records = new List<PointOfInterestRecord>
			{
				Site(1, POIType.Cave, 0f, 0f), Site(2, POIType.Cave, 100f, 0f), Site(3, POIType.Cave, 200f, 0f),
				Site(4, POIType.River, 0f, 100f), Site(5, POIType.Waterfall, 0f, 200f),
			};
			records[3].PlanetRiver = 9;
			records[4].PlanetRiver = 9;
			// A namer that says the same thing on its first draw and something new on each retry.
			PointOfInterestNaming.NameAll(records, (record, index) => record.PlanetRiver >= 0 ? "The Long Water" : index == 0 ? "Blackmaw" : $"Blackmaw {index}",
				record => PointOfInterestNaming.Describe(record, "Pine Forest"));
			Assert.That(records[0].Name, Is.EqualTo("Blackmaw"));
			Assert.That(records[1].Name, Is.EqualTo("Blackmaw 1"));
			Assert.That(records[2].Name, Is.EqualTo("Blackmaw 2"));
			Assert.That(records[3].Name, Is.EqualTo("The Long Water"));
			Assert.That(records[4].Name, Is.EqualTo("The Long Water"), "a river's fall keeps its planet river's name, a repeat or not");
			Assert.That(records[0].Description, Is.EqualTo("A cave in the Pine Forest."));
		}

		[Test]
		public void WithNoNamerSitesTakeTheirKindsNames()
		{
			var records = new List<PointOfInterestRecord> { Site(1, POIType.Shrine, 0f, 0f), Site(2, POIType.Shrine, 1f, 0f), Site(3, POIType.Obelisk, 2f, 0f) };
			PointOfInterestNaming.NameAll(records, null);
			Assert.That(records[0].Name, Is.EqualTo("Shrine"));
			Assert.That(records[1].Name, Is.EqualTo("Shrine 2"));
			Assert.That(records[2].Name, Is.EqualTo("Obelisk"));
			Assert.That(PointOfInterestNaming.Describe(Site(4, POIType.Obelisk, 0f, 0f), "Desert"), Is.EqualTo("An obelisk in the Desert."));
		}

		// ── Races and templates ───────────────────────────────────

		[Test]
		public void RacesComeFromTheKindsCategoriesFirst()
		{
			var candidates = new List<PointOfInterestRaces.Candidate>
			{
				new PointOfInterestRaces.Candidate("human", "Humanoid", 3f),
				new PointOfInterestRaces.Candidate("ghoul", "Undead", 1f),
				new PointOfInterestRaces.Candidate("wolf", "Beast", 1f),
			};
			for (int seed = 0; seed < 50; seed++)
			{
				Assert.That(PointOfInterestRaces.Pick(candidates, new[] { "Undead" }, seed), Is.EqualTo("ghoul"));
				string any = PointOfInterestRaces.Pick(candidates, new[] { "Fey" }, seed);
				Assert.That(new[] { "human", "ghoul", "wolf" }, Does.Contain(any), "no race of the kind's categories: any race of the biome");
				Assert.That(PointOfInterestRaces.Pick(candidates, null, seed), Is.EqualTo(PointOfInterestRaces.Pick(candidates, null, seed)), "the same seed picks the same race");
			}
			Assert.That(PointOfInterestRaces.Pick(new List<PointOfInterestRaces.Candidate>(), null, 1), Is.Empty);
		}

		[Test]
		public void AWeightedPickSkipsWhatWeighsNothing()
		{
			var items = new List<string> { "a", "b", "c" };
			var weights = new List<float> { 0f, 1f, 0f };
			for (int seed = 0; seed < 50; seed++)
			{
				Assert.That(PointOfInterestTemplateLibrary.Pick(items, weights, 1f, seed), Is.EqualTo("b"));
			}
			Assert.That(PointOfInterestTemplateLibrary.Pick(items, new List<float> { 0f, 0f, 0f }, 0f, 1), Is.Null, "nothing fits: a marker-only site");
		}

		// ── Layouts ───────────────────────────────────────────────

		[Test]
		public void LayoutsStayInsideTheFootprint()
		{
			foreach (PointOfInterestLayout layout in System.Enum.GetValues(typeof(PointOfInterestLayout)))
			{
				var counts = new[] { 6, 4, 3 };
				List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(layout, 40f, counts, new DeterministicRNG(7));
				Assert.That(pieces.Count, Is.EqualTo(13), $"{layout} lays every piece");
				var perSlot = new int[3];
				foreach (PointOfInterestLayoutPiece piece in pieces)
				{
					Assert.That(piece.Offset.magnitude, Is.LessThanOrEqualTo(40f * PointOfInterestLayouts.Inset + 1e-3f), $"{layout} keeps its pieces inside");
					perSlot[piece.Slot]++;
				}
				Assert.That(perSlot, Is.EqualTo(counts));

				List<PointOfInterestLayoutPiece> again = PointOfInterestLayouts.Place(layout, 40f, counts, new DeterministicRNG(7));
				for (int i = 0; i < pieces.Count; i++)
				{
					Assert.That(again[i].Offset, Is.EqualTo(pieces[i].Offset), $"{layout} is the same for the same seed");
				}
			}
		}

		[Test]
		public void APadEasesBackIntoTheGround()
		{
			Assert.That(PointOfInterestTerrain.PadWeight(5f, 10f, 8f), Is.EqualTo(1f));
			Assert.That(PointOfInterestTerrain.PadWeight(14f, 10f, 8f), Is.EqualTo(0.5f).Within(1e-4f));
			Assert.That(PointOfInterestTerrain.PadWeight(18f, 10f, 8f), Is.EqualTo(0f));
		}

		// ── The catalogue ─────────────────────────────────────────

		[Test]
		public void EveryRowIsAKnownKindAndBiomeSpecificKindsStayHome()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			foreach (PointOfInterestKindRule rule in PointOfInterestRules.Defaults())
			{
				Assert.That(PointOfInterestKinds.IsKnown(rule.Kind), Is.True, $"{rule.Kind} is in the kinds table");
				Assert.That(rule.FootprintMax, Is.GreaterThanOrEqualTo(rule.FootprintMin), $"{rule.Kind} footprint range");
				Assert.That(rule.Max, Is.GreaterThanOrEqualTo(rule.Min), $"{rule.Kind} budget range");
				if (rule.Placement == PointOfInterestPlacement.Detected)
				{
					Assert.That(rule.Detector, Is.Not.EqualTo(PointOfInterestDetector.None), $"{rule.Kind} has a detector");
				}
			}
			Assert.That(rules.RuleFor(POIType.WitchHut).BiomeWeight("Swamp"), Is.GreaterThan(0f));
			Assert.That(rules.RuleFor(POIType.WitchHut).BiomeWeight("Plains"), Is.EqualTo(0f), "swamp kinds only in swamps");
			Assert.That(rules.RuleFor(POIType.StiltVillage).BiomeWeight("Farmland"), Is.EqualTo(0f), "a stilt village is a swamp kind though it is a settlement");
			Assert.That(rules.RuleFor(POIType.Oasis).BiomeWeight("Scrubland"), Is.EqualTo(0f), "a later weight for a biome wins");
			Assert.That(rules.RuleFor(POIType.Village).BiomeWeight("Molten Surface"), Is.EqualTo(0f));
			Assert.That(rules.RuleFor(POIType.Camp).BiomeWeight("Regolith Plain"), Is.EqualTo(0f), "no camps on alien ground");
		}

		[Test]
		public void AnAssetRowReplacesOnlyItsKind()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default().With(new[]
			{
				new PointOfInterestKindRule { Kind = POIType.Shrine, PerKm2 = 9f, Max = 9 },
			});
			Assert.That(rules.RuleFor(POIType.Shrine).PerKm2, Is.EqualTo(9f));
			Assert.That(rules.RuleFor(POIType.Village).PerKm2, Is.EqualTo(PointOfInterestRules.Default().RuleFor(POIType.Village).PerKm2));
		}
	}
}
