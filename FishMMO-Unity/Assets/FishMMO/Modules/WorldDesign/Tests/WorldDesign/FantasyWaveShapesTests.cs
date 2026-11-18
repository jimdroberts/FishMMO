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
	/// The fantasy wave's Shapes work (2026-10-27): the planner's new gates on scenes drawn by hand (beside a way, at a junction, by a
	/// detected landform, on a tidal island), the carved kinds' rows, templates and cuts. Pure: no scene, no asset.
	/// </summary>
	[TestFixture]
	public class FantasyWaveShapesTests
	{
		private const float Size = 2400f;

		// ── Scenes ───────────────────────────────────────────────────

		private static PointOfInterestKindRule Row(POIType kind, float perKm2, int max, float spacing)
			=> new PointOfInterestKindRule { Kind = kind, Placement = PointOfInterestPlacement.Placed, PerKm2 = perKm2, Max = max, SpacingMetres = spacing };

		/// <summary>A scene of one biome hosting <paramref name="kinds"/>, no water, the given ground.</summary>
		private static PointOfInterestPlanInput Scene(Func<float, float, float> ground, IEnumerable<PointOfInterestKindRule> rows, bool sea = false)
		{
			List<PointOfInterestKindRule> list = rows.ToList();
			var hosted = new Dictionary<POIType, float>();
			foreach (PointOfInterestKindRule row in list)
			{
				hosted[row.Kind] = 1f;
			}
			return new PointOfInterestPlanInput
			{
				WidthMetres = Size,
				DepthMetres = Size,
				Ground = ground,
				SeaLevel = 0f,
				HasSea = sea,
				Water = new PointOfInterestWater { KindAt = (x, z) => WaterKind.None, DistanceAt = (x, z) => 400f },
				Biomes = new List<PointOfInterestBiome> { new PointOfInterestBiome("Test", 7, null, hosted) },
				BiomeAt = (x, z) => 0,
				HardnessAt = (x, z, y) => 0.6f,
				Rules = new PointOfInterestRules(list),
				Seed = 4242,
			};
		}

		private static float Rolling(float x, float z) => 40f + 2f * Mathf.Sin(x / 170f) * Mathf.Cos(z / 230f);

		/// <summary>A way along a line, a point every 2 m, of a class and half-width.</summary>
		private static ScenePath Way(ScenePathClass kind, Vector2 from, Vector2 to, float half, int fromId = 1, int toId = 2)
		{
			int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(from, to) / 2f) + 1);
			var path = new ScenePath { Class = kind, FromId = fromId, ToId = toId, Points = new Vector3[n], HalfWidth = new float[n], Wear = new float[n], Surface = new byte[n], Flags = new byte[n] };
			for (int i = 0; i < n; i++)
			{
				Vector2 p = Vector2.Lerp(from, to, i / (float)(n - 1));
				path.Points[i] = new Vector3(p.x, Rolling(p.x, p.y), p.y);
				path.HalfWidth[i] = half;
			}
			return path;
		}

		/// <summary>The least distance from (x, z) to a way's centre line, and the way's half-width there.</summary>
		private static float DistanceTo(ScenePath path, float x, float z)
		{
			float best = float.PositiveInfinity;
			foreach (Vector3 p in path.Points)
			{
				best = Mathf.Min(best, new Vector2(p.x - x, p.z - z).magnitude);
			}
			return best;
		}

		// ── Beside a way ─────────────────────────────────────────────

		[Test]
		public void AWaysideKind_IsLeftOutOfThePlan_AndSitedBesideTheWayAfterIt()
		{
			PointOfInterestKindRule ambush = Row(POIType.AmbushSite, 5f, 3, 300f).Footprint(10f, 14f, 20f).OnWay(4f);
			PointOfInterestPlanInput input = Scene(Rolling, new[] { ambush });
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(input);
			Assert.That(plan.Records.Any(r => r.Kind == POIType.AmbushSite), Is.False, "a wayside kind waits for the ways");

			ScenePath road = Way(ScenePathClass.Road, new Vector2(-900f, 30f), new Vector2(900f, 30f), 2.5f);
			List<PointOfInterestRecord> added = PointOfInterestPlanner.PlaceBesideWays(input, plan, new[] { road });
			Assert.That(added.Count, Is.EqualTo(3), "its budget is met along an open road");
			foreach (PointOfInterestRecord r in added)
			{
				Assert.That(plan.Records, Does.Contain(r));
				float d = DistanceTo(road, r.Position.x, r.Position.z);
				Assert.That(d, Is.GreaterThanOrEqualTo(r.Radius + 2.5f + 0.7f), $"{r.Position}: its footprint keeps off the carriageway");
				Assert.That(d, Is.LessThanOrEqualTo(r.Radius + 2.5f + 4f + 0.5f), $"{r.Position}: its edge within 4 m of the verge");
				// Facing the road: its heading points at the line z = 30.
				float facing = Mathf.Cos(r.Yaw * Mathf.Deg2Rad);
				Assert.That(Mathf.Sign(facing), Is.EqualTo(Mathf.Sign(30f - r.Position.z)), $"{r.Position}: faces the way");
				Assert.That(plan.KeepOuts.Any(k => k.Id == r.Id), Is.True, "a built wayside site keeps its ground");
				Assert.That(plan.Pads.Any(p => p.Id == r.Id), Is.False, "no pad: the ways are carved already");
			}
			// The same input, plan and ways place the same sites.
			PointOfInterestPlanInput again = Scene(Rolling, new[] { Row(POIType.AmbushSite, 5f, 3, 300f).Footprint(10f, 14f, 20f).OnWay(4f) });
			PointOfInterestPlan second = PointOfInterestPlanner.Plan(again);
			List<PointOfInterestRecord> twice = PointOfInterestPlanner.PlaceBesideWays(again, second, new[] { Way(ScenePathClass.Road, new Vector2(-900f, 30f), new Vector2(900f, 30f), 2.5f) });
			Assert.That(twice.Select(r => (r.Id, r.Position)), Is.EqualTo(added.Select(r => (r.Id, r.Position))));
		}

		[Test]
		public void AWaysideKind_KeepsItsOtherGates_AndNoWayNoSite()
		{
			// Only above 60 m: the road runs at 40, so nothing stands by it.
			PointOfInterestKindRule high = Row(POIType.TollGate, 5f, 3, 300f).Footprint(10f, 14f, 20f).OnWay(3f).Altitude(60f, PointOfInterestKindRule.Unbounded);
			PointOfInterestPlanInput input = Scene(Rolling, new[] { high });
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(input);
			ScenePath road = Way(ScenePathClass.Road, new Vector2(-900f, 30f), new Vector2(900f, 30f), 2.5f);
			Assert.That(PointOfInterestPlanner.PlaceBesideWays(input, plan, new[] { road }), Is.Empty, "its altitude gate still holds");
			PointOfInterestPlanInput open = Scene(Rolling, new[] { Row(POIType.TollGate, 5f, 3, 300f).Footprint(10f, 14f, 20f).OnWay(3f) });
			Assert.That(PointOfInterestPlanner.PlaceBesideWays(open, PointOfInterestPlanner.Plan(open), Array.Empty<ScenePath>()), Is.Empty, "no way, no wayside site");
		}

		[Test]
		public void AWaysideKind_KeepsItsGapFromTheSitesPlannedBefore()
		{
			PointOfInterestKindRule village = Row(POIType.Village, 1f, 1, 600f).Footprint(40f, 40f, 20f);
			PointOfInterestKindRule ambush = Row(POIType.AmbushSite, 20f, 12, 120f).Footprint(10f, 12f, 20f).OnWay(4f).AwayFromSettlements(200f);
			PointOfInterestPlanInput input = Scene(Rolling, new[] { village, ambush });
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(input);
			PointOfInterestRecord town = plan.Records.Single(r => r.Kind == POIType.Village);
			ScenePath road = Way(ScenePathClass.CartTrack, new Vector2(town.Position.x - 900f, town.Position.z), new Vector2(town.Position.x + 900f, town.Position.z), 1.6f);
			List<PointOfInterestRecord> added = PointOfInterestPlanner.PlaceBesideWays(input, plan, new[] { road });
			Assert.That(added, Is.Not.Empty);
			foreach (PointOfInterestRecord r in added)
			{
				float d = new Vector2(r.Position.x - town.Position.x, r.Position.z - town.Position.z).magnitude;
				Assert.That(d, Is.GreaterThanOrEqualTo(200f + town.Radius - 0.01f), "away from the settlement by its rule");
				foreach (PointOfInterestRecord o in added.Where(o => o != r))
				{
					Assert.That(new Vector2(r.Position.x - o.Position.x, r.Position.z - o.Position.z).magnitude, Is.GreaterThanOrEqualTo(120f), "its own kind's spacing");
				}
			}
		}

		// ── At a junction ────────────────────────────────────────────

		[Test]
		public void WayJunctions_FindsWhereASpurJoinsAndWhereWaysCross_NotTheirEnds()
		{
			ScenePath road = Way(ScenePathClass.Road, new Vector2(-800f, 0f), new Vector2(800f, 0f), 2.5f);
			ScenePath spur = Way(ScenePathClass.Trail, new Vector2(300f, 600f), new Vector2(300f, 1f), 0.8f, 5, -1);
			ScenePath across = Way(ScenePathClass.CartTrack, new Vector2(-400f, -700f), new Vector2(-400f, 700f), 1.6f, 6, 7);
			List<Vector3> junctions = PointOfInterestPlanner.WayJunctions(new[] { road, spur, across });
			Assert.That(junctions.Count, Is.EqualTo(2), string.Join(", ", junctions));
			Assert.That(junctions.Any(j => new Vector2(j.x - 300f, j.z).magnitude < 12f), Is.True, "the spur's end on the road");
			Assert.That(junctions.Any(j => new Vector2(j.x + 400f, j.z).magnitude < 12f), Is.True, "the crossing");
			Assert.That(PointOfInterestPlanner.WayJunctions(new[] { road }), Is.Empty, "one way alone meets nothing");
		}

		[Test]
		public void AJunctionKind_StandsOnACornerOfItsJunction_ClearOfEveryArm()
		{
			PointOfInterestKindRule crossroads = Row(POIType.Crossroads, 5f, 1, 400f).Footprint(6f, 8f, 20f).AtJunction(10f);
			PointOfInterestPlanInput input = Scene(Rolling, new[] { crossroads });
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(input);
			ScenePath road = Way(ScenePathClass.Road, new Vector2(-800f, 0f), new Vector2(800f, 0f), 2.5f);
			ScenePath spur = Way(ScenePathClass.Footpath, new Vector2(300f, 600f), new Vector2(300f, 1f), 1f, 5, -1);
			List<PointOfInterestRecord> added = PointOfInterestPlanner.PlaceBesideWays(input, plan, new[] { road, spur });
			Assert.That(added.Count, Is.EqualTo(1));
			PointOfInterestRecord cross = added[0];
			float d = new Vector2(cross.Position.x - 300f, cross.Position.z).magnitude;
			Assert.That(d - cross.Radius, Is.LessThanOrEqualTo(10f + 2.5f + 2f), "its edge by the junction");
			Assert.That(DistanceTo(road, cross.Position.x, cross.Position.z), Is.GreaterThan(cross.Radius + 2.5f), "clear of the road");
			Assert.That(DistanceTo(spur, cross.Position.x, cross.Position.z), Is.GreaterThan(cross.Radius + 1f), "clear of the spur");
			Vector2 toJunction = new Vector2(300f - cross.Position.x, -cross.Position.z).normalized;
			Vector2 facing = new Vector2(Mathf.Sin(cross.Yaw * Mathf.Deg2Rad), Mathf.Cos(cross.Yaw * Mathf.Deg2Rad));
			Assert.That(Vector2.Dot(toJunction, facing), Is.GreaterThan(0.95f), "facing into the junction");
		}

		// ── By a detected landform ───────────────────────────────────

		private const float HillX = 300f, HillZ = -200f;

		private static float Hill(float x, float z)
		{
			float dx = x - HillX, dz = z - HillZ;
			return 30f + 160f * Mathf.Exp(-(dx * dx + dz * dz) / (2f * 260f * 260f));
		}

		[Test]
		public void ALandformGate_SitesOnlyByWhatItsDetectorFinds()
		{
			PointOfInterestKindRule cairn = Row(POIType.SummitCairn, 5f, 2, 300f).Footprint(8f, 10f, 18f).NearLandform(POIType.Peak, 50f);
			Assert.That(cairn.NearDetector, Is.EqualTo(PointOfInterestDetector.Peaks));
			PointOfInterestPlanInput input = Scene(Hill, new[] { cairn });
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(input);
			List<PointOfInterestRecord> cairns = plan.Records.Where(r => r.Kind == POIType.SummitCairn).ToList();
			Assert.That(cairns.Count, Is.EqualTo(1), "one peak: one cairn (its spacing keeps a second off the same summit)");
			Assert.That(new Vector2(cairns[0].Position.x - HillX, cairns[0].Position.z - HillZ).magnitude, Is.LessThanOrEqualTo(50f + 16f * 1.5f), "on the summit");
			// Without the gate the same budget spreads over the whole scene.
			PointOfInterestPlanInput free = Scene(Hill, new[] { Row(POIType.SummitCairn, 5f, 2, 300f).Footprint(8f, 10f, 18f) });
			Assert.That(PointOfInterestPlanner.Plan(free).Records.Count(r => r.Kind == POIType.SummitCairn), Is.EqualTo(2));
			// No peak in flat country: none.
			PointOfInterestPlanInput flat = Scene(Rolling, new[] { Row(POIType.SummitCairn, 5f, 2, 300f).Footprint(8f, 10f, 18f).NearLandform(POIType.Peak, 50f) });
			Assert.That(PointOfInterestPlanner.Plan(flat).Records.Any(r => r.Kind == POIType.SummitCairn), Is.False);
		}

		[Test]
		public void TheFluentGates_SetTheirFields()
		{
			var rule = new PointOfInterestKindRule { Kind = POIType.TrollBridge };
			Assert.That(rule.AtCrossing().RiverCrossing, Is.True);
			Assert.That(new PointOfInterestKindRule().OnWay(5f).BesideWays, Is.True);
			Assert.That(new PointOfInterestKindRule().AtJunction().JunctionMetres, Is.GreaterThan(0f));
			Assert.That(new PointOfInterestKindRule().BesideWays, Is.False);
			Assert.That(PointOfInterestKindRule.DetectorOf(POIType.Pass), Is.EqualTo(PointOfInterestDetector.Passes));
			Assert.That(PointOfInterestKindRule.DetectorOf(POIType.Headland), Is.EqualTo(PointOfInterestDetector.Headlands));
			Assert.That(new PointOfInterestKindRule().Clone().NearDetector, Is.EqualTo(PointOfInterestDetector.None));
		}

		[Test]
		public void TheWaveRows_UseTheRealGates()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			foreach (POIType kind in new[] { POIType.AmbushSite, POIType.WreckedCaravan, POIType.TollGate })
			{
				Assert.That(rules.RuleFor(kind).OnWayMetres, Is.GreaterThan(0f), kind.ToString());
				Assert.That(rules.RuleFor(kind).NearSettlementMetres, Is.EqualTo(0f), $"{kind}: no stand-in gate");
			}
			Assert.That(rules.RuleFor(POIType.Crossroads).JunctionMetres, Is.GreaterThan(0f));
			Assert.That(rules.RuleFor(POIType.TrollBridge).RiverCrossing, Is.True);
			Assert.That(rules.RuleFor(POIType.PassFort).NearDetector, Is.EqualTo(PointOfInterestDetector.Passes));
			Assert.That(rules.RuleFor(POIType.PrayerFlagPass).NearDetector, Is.EqualTo(PointOfInterestDetector.Passes));
			Assert.That(rules.RuleFor(POIType.SummitCairn).NearDetector, Is.EqualTo(PointOfInterestDetector.Peaks));
		}

		// ── On a tidal island ────────────────────────────────────────

		/// <summary>
		/// Mainland west of x = −200 (20 m up), shallow sea (2 m) to an island 70 m round (−20, 0) and on east to x = 250, deep sea (30 m)
		/// beyond, where a second island rises round (650, 0).
		/// </summary>
		private static float Islands(float x, float z)
		{
			if (x < -200f)
			{
				return 20f;
			}
			float floor = x < 250f ? -2f : -30f;
			float near = new Vector2(x + 20f, z).magnitude, far = new Vector2(x - 650f, z).magnitude;
			float h = floor;
			if (near < 90f)
			{
				h = Mathf.Max(h, Mathf.Lerp(14f, floor, Mathf.Clamp01((near - 55f) / 35f)));
			}
			if (far < 90f)
			{
				h = Mathf.Max(h, Mathf.Lerp(14f, floor, Mathf.Clamp01((far - 55f) / 35f)));
			}
			return h;
		}

		[Test]
		public void ATidalIslandKind_StandsOnTheNearIslandFacingTheShore()
		{
			PointOfInterestKindRule abbey = Row(POIType.TidalIslandAbbey, 5f, 2, 300f).Footprint(10f, 14f, 18f).OnTidalIsland(150f);
			PointOfInterestPlanInput input = Scene(Islands, new[] { abbey }, sea: true);
			List<PointOfInterestRecord> abbeys = PointOfInterestPlanner.Plan(input).Records.Where(r => r.Kind == POIType.TidalIslandAbbey).ToList();
			Assert.That(abbeys.Count, Is.EqualTo(1), "only the near island is joined by shallows");
			PointOfInterestRecord site = abbeys[0];
			Assert.That(new Vector2(site.Position.x + 20f, site.Position.z).magnitude, Is.LessThan(40f), "on the near island's heart");
			Assert.That(Mathf.DeltaAngle(site.Yaw, 270f), Is.InRange(-30f, 30f), "facing the mainland to the west");
		}
	
		// ── The carved kinds ─────────────────────────────────────────

		private static readonly POIType[] Carved = { POIType.Cenote, POIType.SandwormPit, POIType.DwarvenGate, POIType.HangingMonastery, POIType.TidalIslandAbbey };

		[Test]
		public void TheCarvedKinds_HaveRowsTemplatesAndShapers()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			List<PointOfInterestTemplateSpec> all = PointOfInterestTemplateSpecs.All();
			foreach (POIType kind in Carved)
			{
				PointOfInterestKindRule rule = rules.RuleFor(kind);
				Assert.That(rule, Is.Not.Null, kind.ToString());
				Assert.That(rule.PerKm2, Is.InRange(0.001f, 0.05f * PointOfInterestRules.FantasyWaveDensity + 1e-5f), $"{kind}: a wave budget");
				Assert.That(rule.DefaultBiomeWeight, Is.EqualTo(0f), $"{kind}: hosted only where a biome names it");
				Assert.That(all.Count(t => t.Kind == kind), Is.InRange(2, 3), $"{kind}: two or three variants");
			}
			Assert.That(rules.RuleFor(POIType.DwarvenGate).Face, Is.True);
			Assert.That(rules.RuleFor(POIType.HangingMonastery).Face, Is.True);
			Assert.That(rules.RuleFor(POIType.TidalIslandAbbey).TidalIslandMetres, Is.GreaterThan(0f));
			Assert.That(DungeonEntranceShaper.Instance.Handles(POIType.DwarvenGate), Is.True);
			Assert.That(CenoteShaper.Instance.Handles(POIType.Cenote), Is.True);
			Assert.That(SandFunnelShaper.Instance.Handles(POIType.SandwormPit), Is.True);
			Assert.That(CausewayShaper.Instance.Handles(POIType.TidalIslandAbbey), Is.True);
			Assert.That(FaceSnapShaper.Instance.Handles(POIType.HangingMonastery), Is.True);
			// A dwarven gate's door is laid at the scale its cut is made for.
			foreach (PointOfInterestTemplateSpec spec in all.Where(t => t.Kind == POIType.DwarvenGate))
			{
				Assert.That(spec.Pieces[0].MinScale, Is.EqualTo(DungeonEntranceShaper.DwarvenGateScale));
				Assert.That(spec.Pieces[0].MaxScale, Is.EqualTo(DungeonEntranceShaper.DwarvenGateScale));
			}
		}

		private static float Flat(float x, float z) => 50f + 0.5f * Mathf.Sin(x / 40f);

		[Test]
		public void TheCenote_IsASheerWellToItsWater_WithAStairDownToItsLedge()
		{
			Assert.That(CenoteShaper.TryPlan(Flat, 0f, 0f, 16f, 90f, 0f, out CenoteShaper.Plan plan), Is.True);
			Assert.That(plan.Depth, Is.InRange(8f, 16.5f), "cut to its water");
			Assert.That(CenoteShaper.CutHeight(plan, 0f, 0f, Flat(0f, 0f)), Is.EqualTo(plan.Floor).Within(1e-4f), "the pool's bed");
			Vector3 ledge = plan.LedgeSpot;
			Assert.That(CenoteShaper.CutHeight(plan, ledge.x, ledge.z, Flat(ledge.x, ledge.z)), Is.EqualTo(plan.Ledge).Within(1e-4f), "the ledge over the pool");
			Assert.That(plan.Ledge - plan.Pool, Is.EqualTo(CenoteShaper.LedgeRise).Within(1e-4f));
			// Sheer: opposite the stair the wall climbs WallGrade a metre.
			float back = (plan.LedgeYaw + 90f) * Mathf.Deg2Rad;
			float r0 = plan.Radius + 0.5f, r1 = plan.Radius + 1.5f;
			float h0 = CenoteShaper.CutHeight(plan, Mathf.Sin(back) * r0, Mathf.Cos(back) * r0, 999f), h1 = CenoteShaper.CutHeight(plan, Mathf.Sin(back) * r1, Mathf.Cos(back) * r1, 999f);
			Assert.That(h1 - h0, Is.EqualTo(CenoteShaper.WallGrade).Within(0.01f), "a sheer wall");
			// The stair, walked down its middle from the rim to the ledge, never steeper than its grade.
			float rs = plan.Radius + 0.5f * CenoteShaper.StairWidth;
			float start = -plan.LedgeArc - plan.StairArc, prev = float.NaN, prevS = 0f;
			int n = 200;
			for (int i = 0; i <= n; i++)
			{
				float a = plan.LedgeYaw * Mathf.Deg2Rad + Mathf.Lerp(start + 0.01f, -plan.LedgeArc, i / (float)n);
				float x = Mathf.Sin(a) * rs, z = Mathf.Cos(a) * rs, s = a * rs;
				float h = CenoteShaper.CutHeight(plan, x, z, Flat(x, z));
				if (!float.IsNaN(prev))
				{
					Assert.That(Mathf.Abs(h - prev) / Mathf.Max(1e-4f, Mathf.Abs(s - prevS)), Is.LessThanOrEqualTo(CenoteShaper.StairGrade + 0.05f), $"stair step {i}");
				}
				prev = h;
				prevS = s;
			}
			Assert.That(prev, Is.EqualTo(plan.Ledge).Within(0.1f), "the stair ends on the ledge");
			// Only lowers, idempotent, and nothing changes past its reach.
			for (float x = -30f; x <= 30f; x += 1.7f)
			{
				for (float z = -30f; z <= 30f; z += 1.9f)
				{
					float g = Flat(x, z), once = CenoteShaper.CutHeight(plan, x, z, g);
					Assert.That(once, Is.LessThanOrEqualTo(g));
					Assert.That(CenoteShaper.CutHeight(plan, x, z, once), Is.EqualTo(once));
					if (new Vector2(x, z).magnitude > plan.Reach)
					{
						Assert.That(once, Is.EqualTo(g));
					}
				}
			}
			// Too near the sea for a well: none.
			Assert.That(CenoteShaper.TryPlan((x, z) => 4f, 0f, 0f, 16f, 0f, 0f, out _), Is.False);
			Assert.That(CenoteShaper.Plan.TryFrom(plan.ToValues(), out CenoteShaper.Plan back2) && back2.Radius == plan.Radius && back2.Pool == plan.Pool, Is.True);
		}

		[Test]
		public void TheSandFunnel_IsAConeAtTheSandsRepose_IdempotentAndNeverRaised()
		{
			SandFunnelShaper.Plan plan = SandFunnelShaper.PlanFor(Flat, 0f, 0f, 18f);
			Assert.That(plan.Lip, Is.EqualTo(18f));
			float grade = Mathf.Tan(30f * Mathf.Deg2Rad);
			Assert.That(SandFunnelShaper.CutHeight(plan, 0f, 0f, 999f), Is.EqualTo(plan.Throat).Within(1e-4f));
			float a = SandFunnelShaper.CutHeight(plan, 8f, 0f, 999f), b = SandFunnelShaper.CutHeight(plan, 12f, 0f, 999f);
			Assert.That((b - a) / 4f, Is.EqualTo(grade).Within(1e-3f), "30° sides");
			Assert.That(plan.Throat, Is.LessThan(Flat(0f, 0f) - 8f), "a funnel, not a dimple");
			for (float x = -26f; x <= 26f; x += 1.3f)
			{
				float g = Flat(x, 3f), once = SandFunnelShaper.CutHeight(plan, x, 3f, g);
				Assert.That(once, Is.LessThanOrEqualTo(g));
				Assert.That(SandFunnelShaper.CutHeight(plan, x, 3f, once), Is.EqualTo(once));
			}
			Assert.That(SandFunnelShaper.Plan.TryFrom(plan.ToValues(), out SandFunnelShaper.Plan again) && again.Throat == plan.Throat, Is.True);
		}

		[Test]
		public void TheCauseway_CrossesTheShallowsToTheShore_AtItsCrest()
		{
			Assert.That(CausewayShaper.TryPlan(Islands, 0f, -20f, 0f, 270f, 190f, out CausewayShaper.Plan plan), Is.True);
			Assert.That(Islands(plan.From.x, plan.From.z), Is.GreaterThanOrEqualTo(0f), "it starts on the island");
			Assert.That(Islands(plan.To.x, plan.To.z), Is.GreaterThanOrEqualTo(CausewayShaper.Crest), "it lands on the mainland");
			Assert.That(plan.To.x, Is.LessThan(-195f), "west, to the mainland");
			// Mid-crossing the sea floor is raised to the crest; a bank falls away either side; dry land is never cut.
			Vector3 mid = Vector3.Lerp(plan.From, plan.To, 0.5f);
			Assert.That(CausewayShaper.CutHeight(plan, mid.x, mid.z, Islands(mid.x, mid.z)), Is.EqualTo(CausewayShaper.Crest).Within(1e-4f));
			Assert.That(CausewayShaper.CutHeight(plan, mid.x, mid.z + 6f, Islands(mid.x, mid.z + 6f)), Is.EqualTo(CausewayShaper.Crest - 3.5f * CausewayShaper.BankGrade).Within(0.05f));
			Assert.That(CausewayShaper.CutHeight(plan, -400f, 0f, 20f), Is.EqualTo(20f));
			// The deep sea to the east is crossed by none.
			Assert.That(CausewayShaper.TryPlan(Islands, 0f, 650f, 0f, 270f, 190f, out _), Is.False);
		}

		/// <summary>A sheer face rising north of z = 0 (30 m over 7 m), level ground before it at 20 m.</summary>
		private static float Cliff(float x, float z) => 20f + 30f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(z / 7f));

		/// <summary>A gentle hillside: 12 m over 40 m.</summary>
		private static float Slope(float x, float z) => 20f + 12f * Mathf.Clamp01(z / 40f);

		[Test]
		public void AHangingMonastery_HangsWhereTheFaceMeetsItsDeck_AndNeedsASheerFace()
		{
			Assert.That(FaceSnapShaper.TrySnap(Cliff, new Vector3(0f, 20f, -8f), 180f, StructurePieces.HangingDeckHeight, StructurePieces.HangingBackReach,
				FaceSnapShaper.SheerRise, out Vector3 at, out float heading, out string problem), Is.True, problem);
			Assert.That(Mathf.DeltaAngle(heading, 180f), Is.InRange(-3f, 3f), "square to the face, facing out");
			// The face at the deck's height stands its back reach behind the origin.
			float behind = Cliff(at.x, at.z + StructurePieces.HangingBackReach) - at.y;
			Assert.That(behind, Is.EqualTo(StructurePieces.HangingDeckHeight).Within(0.3f), "over the foot");
			Assert.That(FaceSnapShaper.TrySnap(Slope, new Vector3(0f, 20f, -8f), 180f, StructurePieces.HangingDeckHeight, StructurePieces.HangingBackReach,
				FaceSnapShaper.SheerRise, out _, out _, out _), Is.False, "a hillside is no face to hang from");
			// The cliff kinds are only set at the foot.
			Assert.That(FaceSnapShaper.TrySnap(Cliff, new Vector3(0f, 20f, -8f), 180f, 0f, 0f, 0f, out Vector3 foot, out _, out _), Is.True);
			Assert.That(foot.z, Is.InRange(-1.5f, 1.5f), "at the face's foot");
			Assert.That(FaceSnapShaper.Instance.Handles(POIType.RockCutFacade) && FaceSnapShaper.Snaps(POIType.CliffRelief), Is.True);
		}

		[Test]
		public void TheDwarvenGate_IsCutToItsDoorsScale_AndOldPlansStillRead()
		{
			Assert.That(DungeonEntranceShaper.ScaleOf(POIType.DwarvenGate), Is.EqualTo(DungeonEntranceShaper.DwarvenGateScale));
			Assert.That(DungeonEntranceShaper.ScaleOf(POIType.DungeonEntrance), Is.EqualTo(1f));
			Assert.That(DungeonEntranceShaper.FindFace(Cliff, new Vector3(0f, 20f, -10f), 180f, DungeonEntranceShaper.DwarvenGateScale, out DungeonEntranceShaper.Plan big, out string problem), Is.True, problem);
			Assert.That(DungeonEntranceShaper.FindFace(Cliff, new Vector3(0f, 20f, -10f), 180f, out DungeonEntranceShaper.Plan small, out _), Is.True);
			Assert.That(big.Scaled, Is.EqualTo(DungeonEntranceShaper.DwarvenGateScale));
			Assert.That(DungeonEntranceShaper.CutArea(big).width, Is.GreaterThan(DungeonEntranceShaper.CutArea(small).width));
			// Its forecourt is levelled out to the scaled frame.
			Vector3 o = big.Outward;
			Vector3 far = big.Threshold + o * (DungeonEntranceShaper.PadDepth * 1.4f);
			Assert.That(DungeonEntranceShaper.CutHeight(big, far.x, far.z, 30f), Is.LessThanOrEqualTo(big.Floor + 0.01f));
			Assert.That(DungeonEntranceShaper.Plan.TryFrom(big.ToValues(), out DungeonEntranceShaper.Plan back) && back.Scaled == big.Scaled, Is.True);
			float[] old = { 1f, 1f, 2f, 3f, 90f, 2f, 5f };
			Assert.That(DungeonEntranceShaper.Plan.TryFrom(old, out DungeonEntranceShaper.Plan legacy) && legacy.Scaled == 1f, Is.True, "a format 1 plan reads at scale 1");
		}

		[Test]
		public void APiecePinnedAtDeckLevel_IsLaidAtTheWaterLine()
		{
			System.Reflection.MethodInfo pinned = typeof(PropsFeature).GetMethod("DeckPinned", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			Assert.That(pinned, Is.Not.Null);
			Assert.That((bool)pinned.Invoke(null, new object[] { "=PileRoundHouse" }), Is.True, "the crannog's round house stands on its piles");
			Assert.That((bool)pinned.Invoke(null, new object[] { "=PileRoundHouse#1" }), Is.True);
			Assert.That((bool)pinned.Invoke(null, new object[] { "=StoneHouse" }), Is.False);
			Assert.That((bool)pinned.Invoke(null, new object[] { "dock" }), Is.False, "a query is not a pin");
		}

		[Test]
		public void TheHangingCloister_HangsOnItsBeams_WithinTheKitsBudget()
		{
			StructurePiece piece = StructurePieces.Find("HangingCloister");
			Assert.That(piece, Is.Not.Null);
			Assert.That(piece.HasTag("monument"), Is.True);
			for (int v = 0; v < piece.Variants; v++)
			{
				StructureMesh m = StructurePieces.Build(piece, v, 0, 7);
				Assert.That(m.Mesh.TriangleCount, Is.LessThanOrEqualTo(piece.TriangleBudget), $"v{v}");
				Bounds b = m.Mesh.Bounds;
				Assert.That(b.max.z, Is.GreaterThan(StructurePieces.HangingBackReach + 2f), $"v{v}: its beams run on into the rock");
				Assert.That(b.min.y, Is.GreaterThanOrEqualTo(-1.3f), $"v{v}: its stilts stand at the foot");
				Assert.That(b.max.y, Is.GreaterThan(StructurePieces.HangingDeckHeight + 5f), $"v{v}: a hall on the deck");
			}
		}
}
}
