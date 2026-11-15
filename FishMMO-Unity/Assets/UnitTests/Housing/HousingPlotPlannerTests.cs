using System.Collections.Generic;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for <see cref="HousingPlotPlanner"/>: where the scene cut lays housing lots.
	/// </summary>
	/// <remarks>
	/// The properties that matter are the ones a player can lose land to: the same cut lays the same
	/// lots, lots a previous cut laid never move, and a re-cut on the same ground lays nothing new.
	/// </remarks>
	[TestFixture]
	public class HousingPlotPlannerTests
	{
		/// <summary>A 4 km square scene, flat at 10 m except a hill in one quarter, with a city and a capital.</summary>
		private static HousingPlotPlanner.Input Scene(int seed = 1234)
		{
			var input = new HousingPlotPlanner.Input
			{
				Seed = seed,
				HalfWidth = 2000f,
				HalfDepth = 2000f,
				GroundAt = (x, z) => x > 800f && z > 800f ? 10f + (x - 800f) * 0.3f : 10f,
				// A river along z = -600.
				Blocked = (x, z) => Mathf.Abs(z + 600f) < 20f,
			};
			input.Settlements.Add(new HousingPlotPlanner.Settlement { Id = 7, Name = "Townsend", X = -900f, Z = 300f, Radius = 120f });
			input.Settlements.Add(new HousingPlotPlanner.Settlement { Id = 3, Name = "Kingsmouth", X = 500f, Z = -200f, Radius = 200f, Capital = true });
			foreach (HousingPlotPlanner.Settlement s in input.Settlements)
			{
				input.Sites.Add(new HousingPlotPlanner.Disc { X = s.X, Z = s.Z, Radius = s.Radius });
			}
			input.Sites.Add(new HousingPlotPlanner.Disc { X = -200f, Z = 1200f, Radius = 60f });
			return input;
		}

		[Test]
		public void TheSameInput_LaysTheSameLots()
		{
			List<ScenePlotManifest.Lot> a = HousingPlotPlanner.Plan(Scene()).Lots;
			List<ScenePlotManifest.Lot> b = HousingPlotPlanner.Plan(Scene()).Lots;
			Assert.That(a.Count, Is.GreaterThan(0));
			Assert.That(b.Count, Is.EqualTo(a.Count));
			for (int i = 0; i < a.Count; i++)
			{
				Assert.That(b[i].Key, Is.EqualTo(a[i].Key));
				Assert.That(b[i].Centre, Is.EqualTo(a[i].Centre));
				Assert.That(b[i].Price, Is.EqualTo(a[i].Price));
			}
		}

		[Test]
		public void CitiesGetOneDistrict_CapitalsTwo()
		{
			HousingPlotPlanner.Result result = HousingPlotPlanner.Plan(Scene());
			Assert.That(result.NewDistricts, Is.EqualTo(3));
			var groups = new HashSet<string>();
			foreach (ScenePlotManifest.Lot lot in result.Lots)
			{
				if (lot.District)
				{
					groups.Add(lot.Group);
				}
			}
			Assert.That(groups, Is.EquivalentTo(new[] { "Townsend housing", "Kingsmouth housing", "Kingsmouth housing 2" }));
		}

		[Test]
		public void DistrictsHoldSixToTwentyFourLots()
		{
			var perGroup = new Dictionary<string, int>();
			foreach (ScenePlotManifest.Lot lot in HousingPlotPlanner.Plan(Scene()).Lots)
			{
				if (lot.District)
				{
					perGroup.TryGetValue(lot.Group, out int n);
					perGroup[lot.Group] = n + 1;
				}
			}
			foreach (KeyValuePair<string, int> pair in perGroup)
			{
				Assert.That(pair.Value, Is.InRange(HousingPlotPlanner.MinDistrictLots, HousingPlotPlanner.LotsPerRun * 4), pair.Key);
			}
		}

		[Test]
		public void LotsNeverOverlap_AndKeysAreUnique()
		{
			List<ScenePlotManifest.Lot> lots = HousingPlotPlanner.Plan(Scene()).Lots;
			var keys = new HashSet<string>();
			for (int i = 0; i < lots.Count; i++)
			{
				Assert.That(keys.Add(lots[i].Key), Is.True, lots[i].Key);
				for (int j = i + 1; j < lots.Count; j++)
				{
					float reachX = (lots[i].Size.x + lots[j].Size.x) * 0.5f;
					float reachZ = (lots[i].Size.y + lots[j].Size.y) * 0.5f;
					bool apart = Mathf.Abs(lots[i].Centre.x - lots[j].Centre.x) >= reachX || Mathf.Abs(lots[i].Centre.z - lots[j].Centre.z) >= reachZ;
					Assert.That(apart, Is.True, $"{lots[i].Key} overlaps {lots[j].Key}");
				}
			}
		}

		[Test]
		public void NoLotStandsOnBlockedOrSteepGround()
		{
			HousingPlotPlanner.Input input = Scene();
			foreach (ScenePlotManifest.Lot lot in HousingPlotPlanner.Plan(input).Lots)
			{
				float half = lot.Size.x * 0.5f;
				Assert.That(Mathf.Abs(lot.Centre.z + 600f) > 20f + half, Is.True, $"{lot.Key} is on the river");
				Assert.That(lot.Centre.x - half > 800f && lot.Centre.z - half > 800f, Is.False, $"{lot.Key} is on the hill");
			}
		}

		[Test]
		public void CountrysideLotsKeepAwayFromSites()
		{
			HousingPlotPlanner.Input input = Scene();
			foreach (ScenePlotManifest.Lot lot in HousingPlotPlanner.Plan(input).Lots)
			{
				if (lot.District)
				{
					continue;
				}
				foreach (HousingPlotPlanner.Disc site in input.Sites)
				{
					float d = Vector2.Distance(new Vector2(lot.Centre.x, lot.Centre.z), new Vector2(site.X, site.Z)) - site.Radius;
					Assert.That(d, Is.GreaterThanOrEqualTo(HousingPlotPlanner.CountryFromSites - lot.Size.x), lot.Key);
				}
			}
		}

		[Test]
		public void ARecutOnTheSameGround_LaysNothingNew()
		{
			List<ScenePlotManifest.Lot> first = HousingPlotPlanner.Plan(Scene()).Lots;
			HousingPlotPlanner.Input again = Scene();
			again.Kept.AddRange(first);
			HousingPlotPlanner.Result second = HousingPlotPlanner.Plan(again);
			Assert.That(second.NewDistrictLots + second.NewCountryLots, Is.EqualTo(0));
			Assert.That(second.Lots.Count, Is.EqualTo(first.Count));
		}

		[Test]
		public void KeptLotsNeverMove_EvenWhenTheGroundChanged()
		{
			List<ScenePlotManifest.Lot> first = HousingPlotPlanner.Plan(Scene()).Lots;
			HousingPlotPlanner.Input changed = Scene(seed: 99);
			// The whole scene is now blocked: nothing new fits, but nothing kept may be dropped.
			changed.Blocked = (x, z) => true;
			changed.Kept.AddRange(first);
			List<ScenePlotManifest.Lot> second = HousingPlotPlanner.Plan(changed).Lots;
			Assert.That(second.Count, Is.EqualTo(first.Count));
			for (int i = 0; i < first.Count; i++)
			{
				Assert.That(second[i].Key, Is.EqualTo(first[i].Key));
				Assert.That(second[i].Centre, Is.EqualTo(first[i].Centre));
			}
		}

		[Test]
		public void APostFacesItsDistrictLane()
		{
			foreach (ScenePlotManifest.Lot lot in HousingPlotPlanner.Plan(Scene()).Lots)
			{
				Vector3 front = HousingPlotPlanner.FrontDirection(lot.Front);
				Assert.That(front.sqrMagnitude, Is.EqualTo(1f).Within(1e-5f));
			}
			Assert.That(HousingPlotPlanner.FrontDirection(0), Is.EqualTo(Vector3.right));
			Assert.That(HousingPlotPlanner.FrontDirection(3), Is.EqualTo(Vector3.back));
		}

		[Test]
		public void PricesRiseWithSize_AndCapitalsCostMore()
		{
			Assert.That(HousingPlotPlanner.PriceOf(16f, 1f), Is.LessThan(HousingPlotPlanner.PriceOf(24f, 1f)));
			Assert.That(HousingPlotPlanner.PriceOf(24f, 1f), Is.LessThan(HousingPlotPlanner.PriceOf(32f, 1f)));
			Assert.That(HousingPlotPlanner.PriceOf(24f, 1.5f), Is.GreaterThan(HousingPlotPlanner.PriceOf(24f, 1f)));
			Assert.That(HousingPlotPlanner.PriceOf(16f, 1f) % 50, Is.EqualTo(0));
		}

		[Test]
		public void KeysAreValidPlotIdentities()
		{
			foreach (ScenePlotManifest.Lot lot in HousingPlotPlanner.Plan(Scene()).Lots)
			{
				Assert.That(PlotIdentity.TryCreate("Moron 2", lot.Key, out PlotIdentity identity), Is.True, lot.Key);
				Assert.That(identity.PlotKey, Is.EqualTo(lot.Key));
			}
		}
	}
}
