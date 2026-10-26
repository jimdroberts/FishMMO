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
				// Each biome hosts the kinds its authored template lists (the spec column, from these rules by its name).
				Biomes = new List<PointOfInterestBiome>
				{
					Hosting("Plains", 11, rules), Hosting("Forest", 12, rules), Hosting("Glacier", 13, rules),
				},
				BiomeAt = BiomeAt,
				HardnessAt = (x, z, y) => 0.6f,
				Settings = settings ?? new PointOfInterestPlanSettings(),
				Rules = rules ?? PointOfInterestRules.Default(),
				Seed = seed,
				Builds = builds,
			};
		}

		/// <summary>A scene biome as the stage hands it to the planner: the kinds its authored template hosts (BiomeArtSpec.Entry.SiteKinds).</summary>
		internal static PointOfInterestBiome Hosting(string name, int id, PointOfInterestRules rules)
		{
			var kinds = new Dictionary<POIType, float>();
			foreach (FishMMO.Shared.Biomes.BiomeSiteKind kind in BiomeArtSpec.For(name).SiteKinds(rules))
			{
				kinds[kind.Kind] = kind.Weight;
			}
			return new PointOfInterestBiome(name, id, null, kinds);
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
				// A bridge spans water, and a river-bank kind (a watermill) stands with its front at the water's edge.
				if (!IsPlaced(record, rules) || record.Kind == POIType.Bridge || rules.RuleFor(record.Kind).RiverBank)
				{
					continue;
				}
				placed++;
				PointOfInterestKindRule rule = rules.RuleFor(record.Kind);
				float x = record.Position.x, z = record.Position.z;
				Assert.That(Mathf.Abs(x) + record.Radius, Is.LessThanOrEqualTo(Size * 0.5f - 48f + 0.01f), $"{record.Kind} keeps its footprint 48 m inside the edge");
				Assert.That(Mathf.Abs(z) + record.Radius, Is.LessThanOrEqualTo(Size * 0.5f - 48f + 0.01f), $"{record.Kind} keeps its footprint 48 m inside the edge");
				// Its footprint's edge (its outline's, or its disc's) all round, and half way in, is dry.
				for (int k = 0; k < 16; k++)
				{
					float a = k * Mathf.PI / 8f;
					float edge = PointOfInterestOutline.ReachToward(record.Outline, record.Radius, a * Mathf.Rad2Deg);
					foreach (float share in new[] { 1f, 0.5f })
					{
						WaterKind kind = KindAt(x + edge * share * Mathf.Sin(a), z + edge * share * Mathf.Cos(a));
						Assert.That(kind == WaterKind.None || kind == WaterKind.Bank, Is.True, $"{record.Kind} at ({x:F0}, {z:F0}) has no water in its footprint");
					}
				}
				Assert.That(KindAt(x, z), Is.EqualTo(WaterKind.None).Or.EqualTo(WaterKind.Bank));
				Assert.That(DistanceAt(x, z), Is.GreaterThanOrEqualTo(PointOfInterestOutline.Min(record.Outline, record.Radius)), $"{record.Kind} at ({x:F0}, {z:F0}) does not straddle the river or the lake");
				if (!rule.Face)
				{
					// The planner's own measure: across a 16 m cell each way.
					float gx = (Ground(x + 16f, z) - Ground(x - 16f, z)) / 32f, gz = (Ground(x, z + 16f) - Ground(x, z - 16f)) / 32f;
					float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
					Assert.That(slope, Is.LessThanOrEqualTo(rule.MaxSlopeDegrees + 5f), $"{record.Kind} stands on ground no steeper than its kind allows");
				}
				string biome = new[] { "Plains", "Forest", "Glacier" }[BiomeAt(x, z)];
				Assert.That(Hosting(biome, 0, rules).WeightOf(rule), Is.GreaterThan(0f), $"{record.Kind} stands in a biome it takes");
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

		/// <summary>
		/// Every outlined site's outline (2026-10-18: footprints not circles) is closed and drawn at every bearing, inside its
		/// radius (the bounding reach), never under the least share of its drawn radius, and not a disc; its pad and keep-out
		/// carry it (the keep-out grown by its margin), and the keep-out holds every point of it.
		/// </summary>
		[Test]
		public void OutlinedSitesAreDrawnInsideTheirReach()
		{
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(Input());
			int outlined = 0, irregular = 0;
			foreach (PointOfInterestRecord record in plan.Records)
			{
				if (!PointOfInterestOutline.Drawn(record.Outline))
				{
					continue;
				}
				outlined++;
				Assert.That(record.Outline.Length, Is.EqualTo(PointOfInterestOutline.Bearings), $"{record.Kind}: drawn at every bearing");
				float most = PointOfInterestOutline.Max(record.Outline, record.Radius), least = PointOfInterestOutline.Min(record.Outline, record.Radius);
				Assert.That(most, Is.LessThanOrEqualTo(record.Radius + 0.01f), $"{record.Kind}: no point past its radius");
				Assert.That(least, Is.GreaterThan(0.25f * record.Radius), $"{record.Kind}: never pinched to nothing");
				irregular += most - least > 0.05f * most ? 1 : 0;
				PointOfInterestPad pad = plan.Pads.Find(p => p.Id == record.Id);
				Assert.That(pad.Outline, Is.EqualTo(record.Outline), $"{record.Kind}: the pad is the site's footprint");
				PointOfInterestKeepOut keepOut = plan.KeepOuts.Find(k => k.Id == record.Id);
				for (int k = 0; k < record.Outline.Length; k++)
				{
					Vector2 dir = PointOfInterestOutline.Direction(k, record.Outline.Length);
					Vector2 at = new Vector2(record.Position.x, record.Position.z) + dir * (record.Outline[k] * 0.99f);
					Assert.That(keepOut.Contains(at.x, at.y), Is.True, $"{record.Kind}: its keep-out holds its outline");
					Assert.That(pad.Contains(at.x, at.y), Is.True, $"{record.Kind}: its pad holds its outline");
					Vector2 past = new Vector2(record.Position.x, record.Position.z) + dir * (record.Outline[k] + keepOut.Radius - record.Radius + 1f);
					Assert.That(keepOut.Contains(past.x, past.y), Is.False, $"{record.Kind}: its keep-out ends past its margin");
				}
			}
			Assert.That(outlined, Is.GreaterThan(3), "pad kinds are outlined");
			Assert.That(irregular, Is.GreaterThan(0), "the outlines are not discs");
		}

		/// <summary>
		/// A town on a steady hillside is terraced (2026-10-18: hill towns): two to four levels rising up the hill, each a
		/// metre or more over the one below, a ramp up every bank at the ramp's grade, and the site standing at its centre's level.
		/// </summary>
		[Test]
		public void ATownOnAHillsideStepsUpItInTerraces()
		{
			var rules = PointOfInterestRules.Default();
			var input = new PointOfInterestPlanInput
			{
				WidthMetres = 1600f,
				DepthMetres = 1600f,
				// A hillside climbing 0.05 m per metre to the north (within a town's relief), and nothing else.
				Ground = (x, z) => 100f + 0.05f * z,
				// Plains, which host towns.
				Biomes = new List<PointOfInterestBiome> { Hosting("Plains", 11, rules) },
				BiomeAt = (x, z) => 0,
				// One town, whatever the budget draws.
				Settings = new PointOfInterestPlanSettings { Overrides = { new PointOfInterestKindOverride { Kind = POIType.Town, Min = 1, Max = 1 } } },
				Rules = rules,
				Seed = 99,
				Builds = kind => kind == POIType.Town,
			};
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(input);
			PointOfInterestRecord town = plan.Records.Find(r => r.Kind == POIType.Town);
			Assume.That(town, Is.Not.Null, "a town is sited on the hillside");
			PointOfInterestPad pad = plan.Pads.Find(p => p.Id == town.Id);
			Assert.That(PointOfInterestTerraces.Terraced(pad), Is.True, "the hillside town is terraced");
			Assert.That(pad.TerraceLevels.Length, Is.InRange(2, 4));
			Assert.That(pad.TerraceAxis.y, Is.GreaterThan(0.95f), "its terraces step up the hill (north)");
			for (int i = 1; i < pad.TerraceLevels.Length; i++)
			{
				Assert.That(pad.TerraceLevels[i] - pad.TerraceLevels[i - 1], Is.GreaterThanOrEqualTo(1f), "each terrace stands over the one below");
			}
			Assert.That(pad.Height, Is.EqualTo(town.Position.y), "the site stands on its pad");
			Assert.That(pad.Height, Is.EqualTo(PointOfInterestTerraces.LevelAt(pad, pad.Centre.x, pad.Centre.z)).Within(0.01f), "at its centre's level");
			// Up each ramp's middle the ground climbs no steeper than a ramp's grade.
			Vector2 across = PointOfInterestTerraces.Across(pad.TerraceAxis);
			for (int s = 0; s < pad.TerraceSteps.Length; s++)
			{
				float previous = float.NaN;
				for (float u = pad.TerraceSteps[s] - 30f; u <= pad.TerraceSteps[s] + 30f; u += 0.5f)
				{
					Vector2 p = new Vector2(pad.Centre.x, pad.Centre.z) + pad.TerraceAxis * u + across * pad.TerraceRamps[s];
					float h = PointOfInterestTerraces.LevelAt(pad, p.x, p.y);
					if (!float.IsNaN(previous))
					{
						Assert.That(h - previous, Is.LessThanOrEqualTo(PointOfInterestTerraces.RampGrade * 0.5f + 1e-3f), $"the ramp up bank {s} at {u:0.0} m");
						Assert.That(h - previous, Is.GreaterThanOrEqualTo(-1e-3f), "it only climbs");
					}
					previous = h;
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

		/// <summary>
		/// A kind's count comes from the area its biomes hold, by their weights (2026-10-25), not the whole scene's: a kind no
		/// biome hosts counts nothing, one hosted at 0.2 a fifth, a favoured one more than the scene's area.
		/// </summary>
		[Test]
		public void TheBudgetAreaIsTheAreaTheKindsBiomesHold()
		{
			var rule = new PointOfInterestKindRule { Kind = POIType.Shrine, DefaultBiomeWeight = 1f };
			var biomes = new List<PointOfInterestBiome>
			{
				new PointOfInterestBiome("Host", 1, null, new Dictionary<POIType, float> { [POIType.Shrine] = 1f }),
				new PointOfInterestBiome("Shy", 2, null, new Dictionary<POIType, float> { [POIType.Shrine] = 0.2f }),
				new PointOfInterestBiome("None", 3, null, new Dictionary<POIType, float>()),
				new PointOfInterestBiome("Fond", 4, null, new Dictionary<POIType, float> { [POIType.Shrine] = 2f }),
			};
			float Area(params int[] cells) => PointOfInterestPlanner.HostedAreaKm2(rule, 10f, cells, biomes);

			Assert.That(Area(100, 0, 0, 0, 0), Is.EqualTo(10f).Within(1e-4f), "all host ground: the scene's area");
			Assert.That(Area(0, 0, 100, 0, 0), Is.EqualTo(0f), "ground that does not host the kind counts nothing");
			Assert.That(Area(50, 0, 50, 0, 0), Is.EqualTo(5f).Within(1e-4f), "half the scene hosts it: half the area");
			Assert.That(Area(0, 100, 0, 0, 0), Is.EqualTo(2f).Within(1e-4f), "weight 0.2: a fifth");
			Assert.That(Area(0, 0, 0, 100, 0), Is.EqualTo(20f).Within(1e-4f), "weight 2: twice over");
			Assert.That(Area(0, 0, 0, 0, 100), Is.EqualTo(10f).Within(1e-4f), "cells with no biome take the kind's default");
			Assert.That(PointOfInterestPlanner.HostedAreaKm2(rule, 10f, new int[5], biomes), Is.EqualTo(0f), "no cells, no area");

			var settings = new PointOfInterestPlanSettings();
			var dense = new PointOfInterestKindRule { Kind = POIType.Shrine, PerKm2 = 1f, Min = 0, Max = 100 };
			Assert.That(PointOfInterestPlanner.Budget(dense, settings, PointOfInterestPlanner.HostedAreaKm2(dense, 10f, new[] { 0, 0, 100, 0, 0 }, biomes), 1), Is.EqualTo(0),
				"a scene of ground that does not host the kind gets none of it");
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
