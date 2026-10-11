using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The point-of-interest planner (<see cref="PointOfInterestPlanner"/>) on a scene drawn by hand: the same input plans
	/// the same records; a kind sited later never moves one sited before it; every placed site stands on ground its kind
	/// may stand on, inside the scene, clear of water and of its neighbours; budgets clamp; and the data's falls, rivers,
	/// lakes, peaks and biome hearts are found. Pure: no scene, no asset, no naming templates.
	/// </summary>
	[TestFixture]
	public class PointOfInterestPlannerTests
	{
		private const float Size = 3000f;
		private const float RiverZ = 300f;
		private const float RiverWidth = 10f;
		private const float FallX = 0f;
		private const float HillX = 600f, HillZ = -500f;
		private const float LakeX = -700f, LakeZ = -700f, LakeRadius = 80f;

		/// <summary>Rolling ground about 40 m up with a 120 m hill at (600, −500).</summary>
		private static float Ground(float x, float z)
		{
			float dx = x - HillX, dz = z - HillZ;
			return 40f + 25f * Mathf.Sin(x / 300f) * Mathf.Cos(z / 350f) + 120f * Mathf.Exp(-(dx * dx + dz * dz) / (2f * 150f * 150f));
		}

		/// <summary>A river along +x at z = 300, every 10 m, falling 4 m over one point at x = 0.</summary>
		private static RiverPath River()
		{
			const int n = 281;
			var river = new RiverPath
			{
				Id = 4,
				PlanetRiver = 77,
				Start = RiverEnd.Edge,
				End = RiverEnd.Edge,
				X = new float[n], Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Surface = new float[n], Bed = new float[n], Curvature = new float[n], Reach = new RiverReach[n],
			};
			for (int i = 0; i < n; i++)
			{
				float x = -1400f + i * 10f;
				river.X[i] = x;
				river.Z[i] = RiverZ;
				river.S[i] = i * 10f;
				river.Discharge[i] = 20f;
				river.Width[i] = RiverWidth;
				river.Depth[i] = 1f;
				river.Surface[i] = 30f - i * 0.01f - (x > FallX ? 4f : x == FallX ? 3f : 0f);
				river.Bed[i] = river.Surface[i] - 1f;
				river.Reach[i] = x == FallX ? RiverReach.Fall : RiverReach.Run;
			}
			return river;
		}

		private static WaterKind KindAt(float x, float z)
		{
			float across = Mathf.Abs(z - RiverZ);
			if (across <= RiverWidth * 0.5f)
			{
				return WaterKind.River;
			}
			float lx = x - LakeX, lz = z - LakeZ;
			if (lx * lx + lz * lz <= LakeRadius * LakeRadius)
			{
				return WaterKind.Lake;
			}
			return across <= RiverWidth * 0.5f + 6f ? WaterKind.Bank : WaterKind.None;
		}

		private static float DistanceAt(float x, float z)
		{
			float river = Mathf.Max(0f, Mathf.Abs(z - RiverZ) - RiverWidth * 0.5f);
			float lake = Mathf.Max(0f, Mathf.Sqrt((x - LakeX) * (x - LakeX) + (z - LakeZ) * (z - LakeZ)) - LakeRadius);
			return Mathf.Min(400f, Mathf.Min(river, lake));
		}

		/// <summary>Plains in the west, forest in the east, a glacier disc 450 m round (−800, 800).</summary>
		private static int BiomeAt(float x, float z)
		{
			float gx = x + 800f, gz = z - 800f;
			if (gx * gx + gz * gz < 450f * 450f)
			{
				return 2;
			}
			return x < 0f ? 0 : 1;
		}

		private static PointOfInterestPlanInput Input(PointOfInterestRules rules = null, PointOfInterestPlanSettings settings = null, int seed = 1234,
			Func<POIType, bool> builds = null)
		{
			return new PointOfInterestPlanInput
			{
				WidthMetres = Size,
				DepthMetres = Size,
				Ground = Ground,
				SeaLevel = 0f,
				HasSea = false,
				Water = new PointOfInterestWater
				{
					KindAt = KindAt,
					DistanceAt = DistanceAt,
					Rivers = new List<RiverPath> { River() },
					Lakes = new List<PointOfInterestLake> { new PointOfInterestLake { Id = 3, X = LakeX, Z = LakeZ, Level = 30f, AreaM2 = Mathf.PI * LakeRadius * LakeRadius } },
				},
				Biomes = new List<PointOfInterestBiome>
				{
					new PointOfInterestBiome("Plains", 11), new PointOfInterestBiome("Forest", 12), new PointOfInterestBiome("Glacier", 13),
				},
				BiomeAt = BiomeAt,
				HardnessAt = (x, z, y) => 0.6f,
				Settings = settings ?? new PointOfInterestPlanSettings(),
				Rules = rules ?? PointOfInterestRules.Default(),
				Seed = seed,
				Builds = builds,
			};
		}

		private static bool IsPlaced(PointOfInterestRecord record, PointOfInterestRules rules)
			=> rules.RuleFor(record.Kind)?.Placement == PointOfInterestPlacement.Placed;

		// ── Determinism ───────────────────────────────────────────

		[Test]
		public void SameInputPlansTheSameRecords()
		{
			PointOfInterestPlan a = PointOfInterestPlanner.Plan(Input());
			PointOfInterestPlan b = PointOfInterestPlanner.Plan(Input());
			Assert.That(a.Records.Count, Is.GreaterThan(10), "a 9 km² scene of habitable ground gets a fair number of sites");
			Assert.That(b.Records.Count, Is.EqualTo(a.Records.Count));
			for (int i = 0; i < a.Records.Count; i++)
			{
				Assert.That(b.Records[i].Id, Is.EqualTo(a.Records[i].Id));
				Assert.That(b.Records[i].Kind, Is.EqualTo(a.Records[i].Kind));
				Assert.That(b.Records[i].Position, Is.EqualTo(a.Records[i].Position));
				Assert.That(b.Records[i].Yaw, Is.EqualTo(a.Records[i].Yaw));
				Assert.That(b.Records[i].Radius, Is.EqualTo(a.Records[i].Radius));
				Assert.That(b.Records[i].SiteSeed, Is.EqualTo(a.Records[i].SiteSeed));
			}
			Assert.That(b.Pads.Count, Is.EqualTo(a.Pads.Count));
			Assert.That(b.KeepOuts.Count, Is.EqualTo(a.KeepOuts.Count));

			var ids = new HashSet<int>();
			foreach (PointOfInterestRecord record in a.Records)
			{
				Assert.That(ids.Add(record.Id), Is.True, $"id {record.Id} is unique in the scene");
				Assert.That(record.UnlockIndex, Is.EqualTo(-1), "the planner assigns no unlock index");
				Assert.That(record.RequiresDiscovery, Is.True, "every generated POI requires discovery");
			}
		}

		[Test]
		public void ADifferentSeedPlansDifferently()
		{
			PointOfInterestPlan a = PointOfInterestPlanner.Plan(Input(seed: 1));
			PointOfInterestPlan b = PointOfInterestPlanner.Plan(Input(seed: 2));
			var rules = PointOfInterestRules.Default();
			var placedA = new List<Vector3>();
			var placedB = new List<Vector3>();
			a.Records.ForEach(r => { if (IsPlaced(r, rules)) placedA.Add(r.Position); });
			b.Records.ForEach(r => { if (IsPlaced(r, rules)) placedB.Add(r.Position); });
			Assert.That(placedB, Is.Not.EqualTo(placedA), "the seed reaches the placed sites");
		}

		[Test]
		public void AKindSitedLaterNeverMovesOneSitedBefore()
		{
			PointOfInterestPlan full = PointOfInterestPlanner.Plan(Input());
			// The resources are sited last: dropping one leaves everything else exactly where it was.
			PointOfInterestRules without = PointOfInterestRules.Default();
			without.Remove(POIType.HerbGrove);
			PointOfInterestPlan less = PointOfInterestPlanner.Plan(Input(without));
			var kept = new List<PointOfInterestRecord>(full.Records);
			kept.RemoveAll(r => r.Kind == POIType.HerbGrove);
			Assert.That(less.Records.Count, Is.EqualTo(kept.Count));
			for (int i = 0; i < kept.Count; i++)
			{
				Assert.That(less.Records[i].Id, Is.EqualTo(kept[i].Id));
				Assert.That(less.Records[i].Position, Is.EqualTo(kept[i].Position));
			}

			// A kind in the middle of the order: every kind sited before it is untouched.
			PointOfInterestRules noGraves = PointOfInterestRules.Default();
			noGraves.Remove(POIType.Graveyard);
			PointOfInterestPlan other = PointOfInterestPlanner.Plan(Input(noGraves));
			int graveTier = PointOfInterestRules.TierOf(POIType.Graveyard);
			bool Before(PointOfInterestRecord r)
			{
				if (r.Kind == POIType.Graveyard)
				{
					return false;
				}
				if (!IsPlaced(r, noGraves))
				{
					return true;
				}
				int tier = PointOfInterestRules.TierOf(r.Kind);
				return tier < graveTier || (tier == graveTier && (int)r.Kind < (int)POIType.Graveyard);
			}
			var expected = full.Records.FindAll(Before);
			var actual = other.Records.FindAll(Before);
			Assert.That(actual.Count, Is.EqualTo(expected.Count));
			for (int i = 0; i < expected.Count; i++)
			{
				Assert.That(actual[i].Id, Is.EqualTo(expected[i].Id));
				Assert.That(actual[i].Position, Is.EqualTo(expected[i].Position));
			}
		}

		// ── Ground ────────────────────────────────────────────────

		[Test]
		public void EveryPlacedSiteStandsOnValidGround()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(Input(rules));
			int placed = 0;
			foreach (PointOfInterestRecord record in plan.Records)
			{
				if (!IsPlaced(record, rules) || record.Kind == POIType.Bridge)
				{
					continue;
				}
				placed++;
				PointOfInterestKindRule rule = rules.RuleFor(record.Kind);
				float x = record.Position.x, z = record.Position.z;
				Assert.That(Mathf.Abs(x) + record.Radius, Is.LessThanOrEqualTo(Size * 0.5f - 48f + 0.01f), $"{record.Kind} keeps its footprint 48 m inside the edge");
				Assert.That(Mathf.Abs(z) + record.Radius, Is.LessThanOrEqualTo(Size * 0.5f - 48f + 0.01f), $"{record.Kind} keeps its footprint 48 m inside the edge");
				for (int k = 0; k < 8; k++)
				{
					float a = k * Mathf.PI / 4f;
					WaterKind kind = KindAt(x + record.Radius * Mathf.Sin(a), z + record.Radius * Mathf.Cos(a));
					Assert.That(kind == WaterKind.None || kind == WaterKind.Bank, Is.True, $"{record.Kind} at ({x:F0}, {z:F0}) has no water in its footprint");
				}
				Assert.That(KindAt(x, z), Is.EqualTo(WaterKind.None).Or.EqualTo(WaterKind.Bank));
				Assert.That(DistanceAt(x, z), Is.GreaterThanOrEqualTo(record.Radius), $"{record.Kind} at ({x:F0}, {z:F0}) does not straddle the river or the lake");
				if (!rule.Face)
				{
					// The planner's own measure: across a 16 m cell each way.
					float gx = (Ground(x + 16f, z) - Ground(x - 16f, z)) / 32f, gz = (Ground(x, z + 16f) - Ground(x, z - 16f)) / 32f;
					float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
					Assert.That(slope, Is.LessThanOrEqualTo(rule.MaxSlopeDegrees + 5f), $"{record.Kind} stands on ground no steeper than its kind allows");
				}
				Assert.That(rule.BiomeWeight(new[] { "Plains", "Forest", "Glacier" }[BiomeAt(x, z)]), Is.GreaterThan(0f), $"{record.Kind} stands in a biome it takes");
			}
			Assert.That(placed, Is.GreaterThan(5));
		}

		[Test]
		public void SpacingAndGapsHold()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			PointOfInterestPlanInput input = Input(rules);
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(input);
			var placed = plan.Records.FindAll(r => IsPlaced(r, rules) && r.Kind != POIType.Bridge);
			for (int i = 0; i < placed.Count; i++)
			{
				for (int j = i + 1; j < placed.Count; j++)
				{
					PointOfInterestRecord a = placed[i], b = placed[j];
					float d = Vector2.Distance(new Vector2(a.Position.x, a.Position.z), new Vector2(b.Position.x, b.Position.z));
					if (a.Kind == b.Kind)
					{
						Assert.That(d, Is.GreaterThanOrEqualTo(rules.RuleFor(a.Kind).SpacingMetres - 0.05f), $"two {a.Kind} keep their spacing");
					}
					Assert.That(d, Is.GreaterThanOrEqualTo(a.Radius + b.Radius + input.SiteGapMetres - 0.05f), $"{a.Kind} and {b.Kind} footprints keep their gap");
				}
			}
		}

		[Test]
		public void PadsAndKeepOutsOnlyForWhatIsBuilt()
		{
			PointOfInterestPlan none = PointOfInterestPlanner.Plan(Input(builds: kind => false));
			Assert.That(none.Pads, Is.Empty, "a site that builds nothing flattens nothing");
			Assert.That(none.KeepOuts, Is.Empty);

			PointOfInterestPlan all = PointOfInterestPlanner.Plan(Input());
			Assert.That(all.Pads, Is.Not.Empty);
			foreach (PointOfInterestPad pad in all.Pads)
			{
				PointOfInterestRecord record = all.Find(pad.Id);
				Assert.That(record, Is.Not.Null);
				Assert.That(PointOfInterestKinds.Info(record.Kind).Has(PointOfInterestTraits.Pad), Is.True);
				Assert.That(pad.Radius, Is.EqualTo(record.Radius));
				Assert.That(pad.Blend, Is.InRange(8f, 15f));
				Assert.That(pad.Height, Is.EqualTo(record.Position.y), "the site stands on its pad");
			}
			foreach (PointOfInterestKeepOut keepOut in all.KeepOuts)
			{
				Assert.That(all.IsKeptOut(keepOut.X, keepOut.Z), Is.True);
			}
		}

		// ── Budgets ───────────────────────────────────────────────

		[Test]
		public void BudgetsClamp()
		{
			var rule = new PointOfInterestKindRule { Kind = POIType.Village, PerKm2 = 0.25f, Min = 1, Max = 5 };
			var normal = new PointOfInterestPlanSettings();
			Assert.That(PointOfInterestPlanner.Budget(rule, normal, 12f, 1), Is.InRange(3, 4), "0.25 per km² on 12 km² is 3");
			Assert.That(PointOfInterestPlanner.Budget(rule, new PointOfInterestPlanSettings { Multiplier = 4f, DensityScale = 1.8f }, 20f, 1), Is.EqualTo(5), "clamped to Max");
			Assert.That(PointOfInterestPlanner.Budget(rule, new PointOfInterestPlanSettings { Multiplier = 0f }, 20f, 1), Is.EqualTo(1), "clamped to Min");
			Assert.That(PointOfInterestPlanner.Budget(rule, new PointOfInterestPlanSettings { NaturalOnly = true }, 20f, 1), Is.EqualTo(0), "natural only places nothing");

			var capital = new PointOfInterestKindRule { Kind = POIType.Capital, PerKm2 = 0f, Min = 0, Max = 1 };
			Assert.That(PointOfInterestPlanner.Budget(capital, normal, 20f, 1), Is.EqualTo(0), "no capital unless asked");
			Assert.That(PointOfInterestPlanner.Budget(capital, new PointOfInterestPlanSettings { Capital = true }, 20f, 1), Is.EqualTo(1), "one capital when asked");
		}

		[Test]
		public void CapitalOnlyWhenAsked()
		{
			PointOfInterestPlan without = PointOfInterestPlanner.Plan(Input());
			Assert.That(without.Records.Exists(r => r.Kind == POIType.Capital), Is.False);
			PointOfInterestPlan with = PointOfInterestPlanner.Plan(Input(settings: new PointOfInterestPlanSettings { Capital = true }));
			Assert.That(with.Records.FindAll(r => r.Kind == POIType.Capital).Count, Is.EqualTo(1));
		}

		[Test]
		public void NaturalOnlyMarksOnlyDetectedKinds()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(Input(rules, new PointOfInterestPlanSettings { NaturalOnly = true }));
			Assert.That(plan.Records, Is.Not.Empty);
			foreach (PointOfInterestRecord record in plan.Records)
			{
				Assert.That(rules.RuleFor(record.Kind).Placement, Is.EqualTo(PointOfInterestPlacement.Detected), $"{record.Kind} is a detected kind");
			}
			Assert.That(plan.Pads, Is.Empty);
		}

		[Test]
		public void AnOverrideTurnsAKindOff()
		{
			var settings = new PointOfInterestPlanSettings();
			settings.Overrides.Add(new PointOfInterestKindOverride { Kind = POIType.Village, Enabled = false });
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(Input(settings: settings));
			Assert.That(plan.Records.Exists(r => r.Kind == POIType.Village), Is.False);
		}

		// ── Detection ─────────────────────────────────────────────

		[Test]
		public void FindsTheFallTheRiverAndTheLake()
		{
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(Input());
			PointOfInterestRecord fall = plan.Records.Find(r => r.Kind == POIType.Waterfall);
			Assert.That(fall, Is.Not.Null, "the 4 m drop is a waterfall");
			Assert.That(fall.Position.x, Is.EqualTo(FallX).Within(15f));
			Assert.That(fall.RiverId, Is.EqualTo(4));
			Assert.That(fall.PlanetRiver, Is.EqualTo(77));

			List<PointOfInterestRecord> rivers = plan.Records.FindAll(r => r.Kind == POIType.River);
			Assert.That(rivers.Count, Is.EqualTo(1), "one record per planet river");
			Assert.That(rivers[0].PlanetRiver, Is.EqualTo(77));
			Assert.That(rivers[0].Position.z, Is.EqualTo(RiverZ).Within(0.01f));

			PointOfInterestRecord lake = plan.Records.Find(r => r.Kind == POIType.Lake);
			Assert.That(lake, Is.Not.Null);
			Assert.That(lake.LakeId, Is.EqualTo(3));
			Assert.That(lake.Position.x, Is.EqualTo(LakeX).Within(0.01f));
		}

		[Test]
		public void FindsThePeakAndTheGlacier()
		{
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(Input());
			PointOfInterestRecord peak = plan.Records.Find(r => r.Kind == POIType.Peak);
			Assert.That(peak, Is.Not.Null, "the 120 m hill is a peak");
			Assert.That(Vector2.Distance(new Vector2(peak.Position.x, peak.Position.z), new Vector2(HillX, HillZ)), Is.LessThan(40f));

			PointOfInterestRecord glacier = plan.Records.Find(r => r.Kind == POIType.Glacier);
			Assert.That(glacier, Is.Not.Null, "a 0.64 km² glacier biome is marked");
			Assert.That(Vector2.Distance(new Vector2(glacier.Position.x, glacier.Position.z), new Vector2(-800f, 800f)), Is.LessThan(40f), "at its middle");
		}

		[Test]
		public void ComponentsSplitConnectedCells()
		{
			// Two blobs on a 6×3 grid: the left pair and the right column.
			var mask = new[]
			{
				true, true, false, false, false, true,
				false, false, false, false, false, true,
				false, false, false, false, false, true,
			};
			List<List<int>> groups = PointOfInterestPlanner.Components(mask, 6, 3);
			Assert.That(groups.Count, Is.EqualTo(2));
			Assert.That(groups[0], Is.EqualTo(new List<int> { 0, 1 }));
			Assert.That(groups[1], Is.EqualTo(new List<int> { 5, 11, 17 }));
		}

		[Test]
		public void AFaceScoresAtItsFootAndFacesOut()
		{
			// A 30 m wall rising north of z = 4 over 6 m, flat ground south of it.
			float Cliff(float x, float z) => z < 4f ? 0f : z > 10f ? 30f : (z - 4f) * 5f;
			float score = PointOfInterestPlanner.FaceScore(Cliff, null, 0f, 0f, 0f, out float yaw);
			Assert.That(score, Is.GreaterThanOrEqualTo(PointOfInterestPlanner.MinFaceScore));
			Assert.That(Mathf.DeltaAngle(yaw, 180f), Is.EqualTo(0f).Within(23f), "the site faces south, away from the wall");
			Assert.That(PointOfInterestPlanner.FaceScore((x, z) => 0f, null, 0f, 0f, 0f, out _), Is.EqualTo(0f), "flat ground has no face");
			Assert.That(PointOfInterestPlanner.FaceScore(Cliff, (x, z, y) => 0.1f, 0f, 0f, 0.5f, out _), Is.LessThan(score), "soft rock scores less");
		}
	}
}
