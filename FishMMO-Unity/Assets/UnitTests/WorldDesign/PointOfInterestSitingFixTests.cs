using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The two defects the first real cut showed (Flo Monolith probe, 2026-10-10): a capital asked for before the cut was
	/// never sited on rugged ground ("0 of 1 sited"), and every fall on one river carried the river's one name ("Dunwy
	/// Falls" seven times). Pure: no scene, no asset.
	/// </summary>
	[TestFixture]
	public class PointOfInterestSitingFixTests
	{
		private const float Size = 3000f;

		/// <summary>Egg-crate hills, 44 m from trough to crest every ~380 m: no 400 m disc anywhere is flat enough.</summary>
		private static float Rugged(float x, float z) => 60f + 22f * Mathf.Sin(x / 60f) * Mathf.Sin(z / 70f);

		private static PointOfInterestPlanInput RuggedInput(bool capital) => new PointOfInterestPlanInput
		{
			WidthMetres = Size,
			DepthMetres = Size,
			Ground = Rugged,
			SeaLevel = 0f,
			HasSea = false,
			Water = new PointOfInterestWater
			{
				KindAt = (x, z) => WaterKind.None,
				DistanceAt = (x, z) => 10000f,
				Rivers = new List<RiverPath>(),
				Lakes = new List<PointOfInterestLake>(),
			},
			Biomes = new List<PointOfInterestBiome> { new PointOfInterestBiome("Plains", 11), new PointOfInterestBiome("Forest", 12) },
			BiomeAt = (x, z) => x < 0f ? 0 : 1,
			HardnessAt = (x, z, y) => 0.6f,
			Settings = new PointOfInterestPlanSettings { Capital = capital },
			Rules = PointOfInterestRules.Default(),
			Seed = 77,
		};

		[Test]
		public void ACapitalAskedForIsSitedEvenOnRuggedGround()
		{
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(RuggedInput(true));
			List<PointOfInterestRecord> capitals = plan.Records.FindAll(r => r.Kind == POIType.Capital);
			Assert.That(capitals.Count, Is.EqualTo(1), string.Join("\n", plan.Notes));
		}

		[Test]
		public void NoCapitalUnlessAskedForOnRuggedGroundEither()
		{
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(RuggedInput(false));
			Assert.That(plan.Records.Exists(r => r.Kind == POIType.Capital), Is.False);
		}

		[Test]
		public void RelaxedSitingIsDeterministic()
		{
			PointOfInterestPlan a = PointOfInterestPlanner.Plan(RuggedInput(true));
			PointOfInterestPlan b = PointOfInterestPlanner.Plan(RuggedInput(true));
			PointOfInterestRecord ca = a.Records.Find(r => r.Kind == POIType.Capital);
			PointOfInterestRecord cb = b.Records.Find(r => r.Kind == POIType.Capital);
			Assert.That(ca.Position, Is.EqualTo(cb.Position));
			Assert.That(ca.Radius, Is.EqualTo(cb.Radius));
		}

		private static PointOfInterestRecord Fall(int id, float y, string name, int planetRiver = 9)
			=> new PointOfInterestRecord { Id = id, Kind = POIType.Waterfall, Position = new Vector3(0f, y, 0f), Name = name, PlanetRiver = planetRiver };

		[Test]
		public void FallsSharingARiversNameAreToldApartHighestFirst()
		{
			var records = new List<PointOfInterestRecord> { Fall(1, 120f, "Dunwy Falls"), Fall(2, 300f, "Dunwy Falls"), Fall(3, 200f, "Dunwy Falls") };
			PointOfInterestNaming.QualifyRiverFeatures(records);
			Assert.That(records[1].Name, Is.EqualTo("Upper Dunwy Falls"));
			Assert.That(records[2].Name, Is.EqualTo("Middle Dunwy Falls"));
			Assert.That(records[0].Name, Is.EqualTo("Lower Dunwy Falls"));
		}

		[Test]
		public void ManyFallsTakeOrdinalsAndALoneFallKeepsItsName()
		{
			var records = new List<PointOfInterestRecord>();
			for (int i = 0; i < 7; i++)
			{
				records.Add(Fall(i, 500f - i * 50f, "Dunwy Falls"));
			}
			records.Add(Fall(99, 10f, "Tamar Falls", planetRiver: 4));
			PointOfInterestNaming.QualifyRiverFeatures(records);
			Assert.That(records[0].Name, Is.EqualTo("First Dunwy Falls"));
			Assert.That(records[6].Name, Is.EqualTo("Seventh Dunwy Falls"));
			Assert.That(records[7].Name, Is.EqualTo("Tamar Falls"));
			var names = new HashSet<string>();
			foreach (PointOfInterestRecord record in records)
			{
				Assert.That(names.Add(record.Name), Is.True, $"'{record.Name}' repeats");
			}
		}

		[Test]
		public void ARiversOwnRecordsAreNeverQualified()
		{
			var records = new List<PointOfInterestRecord>
			{
				new PointOfInterestRecord { Id = 1, Kind = POIType.River, Position = Vector3.up * 50f, Name = "River Dunwy", PlanetRiver = 9 },
				new PointOfInterestRecord { Id = 2, Kind = POIType.River, Position = Vector3.up * 20f, Name = "River Dunwy", PlanetRiver = 9 },
			};
			PointOfInterestNaming.QualifyRiverFeatures(records);
			Assert.That(records.TrueForAll(r => r.Name == "River Dunwy"), Is.True);
		}

		[Test]
		public void APadOnAHillsideBanksAtTheEmbankmentSlopeNotAScarp()
		{
			// A 60 m pad at 100 m on ground falling 0.4 m per metre (22°) to the east: the pad sits 24 m above the ground at its edge.
			const float radius = 60f, blend = 12f, height = 100f;
			float Ground(float d) => height - 0.4f * d;
			float previous = PointOfInterestTerrain.PadHeight(radius, Ground(radius), radius, blend, height);
			for (float d = radius + 1f; d <= radius + PointOfInterestTerrain.MaxEmbankmentMetres + 20f; d += 1f)
			{
				float h = PointOfInterestTerrain.PadHeight(d, Ground(d), radius, blend, height);
				// Never steeper than the bank, but for the fade over the reach's last quarter, which may add a little while it
				// closes a gap the bank has not yet closed (about 32° here); a scarp would be many times the slope.
				Assert.That(previous - h, Is.LessThanOrEqualTo(PointOfInterestTerrain.EmbankmentSlope + 0.05f), $"a step at {d} m");
				Assert.That(h, Is.GreaterThanOrEqualTo(Ground(d) - 1e-3f), "a fill never digs below the ground");
				previous = h;
			}
			// The bank (0.6/m after a 12 m fillet) meets the hillside (0.4/m) 24 m below the rim 138 m out.
			Assert.That(PointOfInterestTerrain.PadHeight(radius + 140f, Ground(radius + 140f), radius, blend, height), Is.EqualTo(Ground(radius + 140f)).Within(1e-3f));
			// And past the reach the ground is untouched, so the edit ends without a step.
			float past = radius + PointOfInterestTerrain.MaxEmbankmentMetres;
			Assert.That(PointOfInterestTerrain.PadHeight(past, Ground(past), radius, blend, height), Is.EqualTo(Ground(past)).Within(1e-3f));
		}

		[Test]
		public void APadOnLevelGroundLeavesItsSurroundsAlone()
		{
			const float radius = 40f, blend = 10f, height = 50f;
			Assert.That(PointOfInterestTerrain.PadHeight(20f, 51.5f, radius, blend, height), Is.EqualTo(height), "inside the pad: the pad");
			Assert.That(PointOfInterestTerrain.PadHeight(radius + blend + 5f, 50.4f, radius, blend, height), Is.EqualTo(50.4f).Within(1e-4f), "past the rim: the ground");
		}

		/// <summary>
		/// A dry channel 6 m wide and 2 m deep, its sides carved up to ground at 0 m; past the near bank flat ground, past the
		/// far bank a hillside climbing on. The bridge must sit level on the lower bank's top and meet the ground at both
		/// ends: it used to stop part way up the channel sides at the site's own ground, buried in one bank, hanging off the
		/// other (Jim's screenshot, 2026-10-10).
		/// </summary>
		private static float Channel(float x, float z)
		{
			float d = Mathf.Abs(z);
			if (d < 3f)
			{
				return -2f;
			}
			if (d < 6f)
			{
				return -2f + (d - 3f) * (2f / 3f);
			}
			return z > 0f ? 0.5f * (d - 6f) : 0f;
		}

		[Test]
		public void ABridgeLiesLevelOnTheLowerBankAndMeetsBothBanks()
		{
			var site = new PointOfInterestRecord { Kind = POIType.Bridge, Position = new Vector3(0f, -2f, 0f), Radius = 10f, Yaw = 0f };
			var context = new PointOfInterestSiteContext { Record = site, GroundAt = Channel };
			PropsFeature.Crossing crossing = PropsFeature.MeasureCrossing(context);
			Assert.That(crossing.DeckFrom, Is.EqualTo(0f).Within(0.2f), "the near bank's top");
			Assert.That(crossing.DeckTo, Is.EqualTo(0f).Within(0.2f), "level with it: the hillside is not climbed");
			Assert.That(crossing.From, Is.InRange(-8f, -6f), "the near end just past the bank's lip");
			Assert.That(crossing.To, Is.InRange(6f, 8f), "the far end where its ground first reaches the deck");
			// Both ends on their ground (within the metre's bearing past the lip).
			Assert.That(crossing.DeckAt(crossing.From) - Channel(0f, crossing.From), Is.InRange(-0.6f, 0.2f));
			Assert.That(crossing.DeckAt(crossing.To) - Channel(0f, crossing.To), Is.InRange(-0.6f, 0.2f));
		}

		[Test]
		public void TheLeadingArticleStaysInFront()
		{
			Assert.That(PointOfInterestNaming.Qualify("The Hidden Race", "Upper"), Is.EqualTo("The Upper Hidden Race"));
			Assert.That(PointOfInterestNaming.Qualify("Dunwy Falls", "Lower"), Is.EqualTo("Lower Dunwy Falls"));
		}
	}
}
