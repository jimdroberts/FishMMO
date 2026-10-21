using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>The path field the shaders read: distances exact along a way, seamless across pages, junctions to the nearer edge.</summary>
	[TestFixture]
	public class ScenePathFieldTests
	{
		internal static ScenePath Line(Vector3 a, Vector3 b, float half, ScenePathClass kind = ScenePathClass.Footpath, int n = 41)
		{
			var path = new ScenePath
			{
				Id = 1,
				Class = kind,
				Points = new Vector3[n],
				HalfWidth = new float[n],
				Wear = new float[n],
				Surface = new byte[n],
				Flags = new byte[n],
			};
			for (int i = 0; i < n; i++)
			{
				path.Points[i] = Vector3.Lerp(a, b, i / (float)(n - 1));
				path.HalfWidth[i] = half;
				path.Wear[i] = 0.8f;
				path.Surface[i] = (byte)ScenePathStyle.For(kind).Surface;
			}
			return path;
		}

		[Test]
		public void TheEdgeDistanceIsExactAcrossAStraightWay()
		{
			ScenePathField field = ScenePathField.Build(new[] { Line(new Vector3(-40f, 0f, 3.3f), new Vector3(40f, 0f, 3.3f), 1f) });
			foreach (float d in new[] { 0.6f, 0.9f, 1.0f, 1.5f, 2.5f })
			{
				Assert.That(field.Sample(7.1f, 3.3f + d, out float edge, out float half, out _, out _), Is.True, $"stored {d} m off");
				Assert.That(edge, Is.EqualTo(d - 1f).Within(0.06f), $"{d} m off the centre line");
				Assert.That(half, Is.EqualTo(1f).Within(0.04f));
			}
		}

		[Test]
		public void PagesAgreeAcrossTheirBorders()
		{
			ScenePathField field = ScenePathField.Build(new[] { Line(new Vector3(-60f, 0f, 1f), new Vector3(60f, 0f, 9f), 0.7f) });
			// Either side of every page border the path crosses.
			for (float x = -48f; x <= 48f; x += ScenePathField.PageMetres)
			{
				float border = Mathf.Round((x - field.Origin.x) / ScenePathField.PageMetres) * ScenePathField.PageMetres + field.Origin.x;
				float z = 1f + (border + 60f) / 120f * 8f + 1.2f;
				Assert.That(field.Sample(border - 0.01f, z, out float left, out _, out _, out _), Is.True);
				Assert.That(field.Sample(border + 0.01f, z, out float right, out _, out _, out _), Is.True);
				Assert.That(right, Is.EqualTo(left).Within(0.03f), $"the border at x = {border}");
			}
		}

		[Test]
		public void NothingIsStoredFarFromAnyWay()
		{
			ScenePathField field = ScenePathField.Build(new[] { Line(new Vector3(-40f, 0f, 0f), new Vector3(40f, 0f, 0f), 1f) });
			Assert.That(field.Sample(0f, 120f, out _, out _, out _, out _), Is.False);
			Assert.That(field.PageCount, Is.LessThan(30), "a few pages along an 80 m way, not a sheet");
		}

		[Test]
		public void AJunctionTakesTheNearerEdgeAndBlendsTheWaysNearIt()
		{
			ScenePath road = Line(new Vector3(-40f, 0f, 0f), new Vector3(40f, 0f, 0f), 2.4f, ScenePathClass.Road);
			ScenePath trail = Line(new Vector3(1.8f, 0f, -30f), new Vector3(1.8f, 0f, 30f), 0.4f, ScenePathClass.Trail);
			trail.Id = 2;
			ScenePathField field = ScenePathField.Build(new[] { road, trail });
			// On the road's centre (2.4 m inside its edge, 2 m deeper than the trail's): the road's alone.
			Assert.That(field.Sample(1.8f, 0f, out float edge, out float half, out _, out float surface), Is.True);
			// (The centre line's crease, read between texels a quarter metre either side of it.)
			Assert.That(edge, Is.EqualTo(-2.15f).Within(0.08f));
			Assert.That(half, Is.EqualTo(2.4f).Within(0.05f));
			Assert.That(surface, Is.EqualTo(ScenePathStyle.SurfaceCode(ScenePathStyle.For(ScenePathClass.Road).Surface)).Within(0.01f));
			// 1 m off it (1.4 m inside the road's edge, 0.4 m inside the trail's): the nearer edge, mostly the road, some trail.
			Assert.That(field.Sample(1.8f, 1f, out edge, out half, out _, out _), Is.True);
			Assert.That(edge, Is.EqualTo(-1.4f).Within(0.08f));
			Assert.That(half, Is.GreaterThan(1.9f).And.LessThan(2.35f), "the trail blends in at the road's margin");
			// Off the road, the trail's own.
			Assert.That(field.Sample(1.8f, 12f, out edge, out half, out _, out surface), Is.True);
			Assert.That(half, Is.EqualTo(0.4f).Within(0.05f));
			Assert.That(edge, Is.EqualTo(-0.4f).Within(0.08f));
			Assert.That(surface, Is.EqualTo(ScenePathStyle.SurfaceCode(ScenePathStyle.For(ScenePathClass.Trail).Surface)).Within(0.01f));
		}

		[Test]
		public void WaysMeetingBlendAcrossTheirSeamRatherThanSwitchingAtIt()
		{
			// Two ways of one width side by side, a gravel road and a stone street, 3 m apart: the seam runs down z = 0.
			ScenePath road = Line(new Vector3(-40f, 0f, -1.5f), new Vector3(40f, 0f, -1.5f), 1.5f, ScenePathClass.Road);
			ScenePath street = Line(new Vector3(-40f, 0f, 1.5f), new Vector3(40f, 0f, 1.5f), 1.5f, ScenePathClass.Street);
			street.Id = 2;
			for (int i = 0; i < street.Count; i++)
			{
				street.Wear[i] = 0.2f;
			}
			ScenePathField field = ScenePathField.Build(new[] { road, street });
			float gravel = ScenePathStyle.SurfaceCode(ScenePathSurface.Gravel), stone = ScenePathStyle.SurfaceCode(ScenePathSurface.Stone);
			// Even on the seam.
			Assert.That(field.Sample(5.1f, 0f, out _, out _, out float wear, out float surface), Is.True);
			Assert.That(surface, Is.EqualTo((gravel + stone) / 2f).Within(0.02f));
			Assert.That(wear, Is.EqualTo(0.5f).Within(0.02f));
			// No step anywhere across it: a quarter metre at a time, nothing jumps.
			float last = -1f;
			for (float z = -2f; z <= 2f; z += 0.25f)
			{
				Assert.That(field.Sample(5.1f, z, out _, out _, out _, out surface), Is.True);
				Assert.That(surface, Is.InRange(gravel - 0.01f, stone + 0.01f), "a blend of the two, never another surface");
				if (last >= 0f)
				{
					Assert.That(Mathf.Abs(surface - last), Is.LessThan((stone - gravel) * 0.3f), $"a step at z = {z}");
				}
				last = surface;
			}
			// Each its own past the blend.
			Assert.That(field.Sample(5.1f, -2.5f, out _, out _, out _, out surface), Is.True);
			Assert.That(surface, Is.EqualTo(gravel).Within(0.01f));
			Assert.That(field.Sample(5.1f, 2.5f, out _, out _, out _, out surface), Is.True);
			Assert.That(surface, Is.EqualTo(stone).Within(0.01f));
		}

		[Test]
		public void EarthAndGravelBlendWithoutPassingThroughATrack()
		{
			// The track's ruts and median would show wherever a blend's code passed it: it is coded past the earth's end.
			float track = ScenePathStyle.SurfaceCode(ScenePathSurface.Track), earth = ScenePathStyle.SurfaceCode(ScenePathSurface.Earth);
			float gravel = ScenePathStyle.SurfaceCode(ScenePathSurface.Gravel), stone = ScenePathStyle.SurfaceCode(ScenePathSurface.Stone);
			Assert.That(track, Is.LessThan(earth));
			Assert.That(earth, Is.LessThan(gravel));
			Assert.That(gravel, Is.LessThan(stone));
		}

		[Test]
		public void AWayPetersOutWhereItEndsInASiteButMeetsTheWayItJoins()
		{
			// A cart track joining a road at its start (FromId -1), ending in a village (ToId 7).
			ScenePath track = Line(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 80f), 1.5f, ScenePathClass.CartTrack, 41);
			track.FromId = -1;
			track.ToId = 7;
			ScenePathField field = ScenePathField.Build(new[] { track });
			Assert.That(field.Sample(0f, 40f, out _, out float middle, out _, out _), Is.True);
			Assert.That(middle, Is.EqualTo(1.5f).Within(0.05f), "full width along the way");
			Assert.That(field.Sample(0f, 1f, out _, out float joined, out _, out _), Is.True);
			Assert.That(joined, Is.EqualTo(1.5f).Within(0.05f), "full width where it meets the road");
			bool found = field.Sample(0f, 79.5f, out float edge, out float end, out float wear, out _);
			Assert.That(!found || end < 0.3f, Is.True, $"narrowed to almost nothing at its end in the village (half {end:0.00})");
			Assert.That(!found || edge > -0.3f, Is.True, "no full-width cap at the end");
			field.Sample(0f, 40f, out _, out _, out float middleWear, out _);
			Assert.That(!found || wear < middleWear * 0.5f, Is.True, "worn away into the ground at its end");
		}

		/// <summary>A 6 × 6 m building's plan between its end midpoints, flagged as AddYards writes it.</summary>
		private static ScenePath Plan(float half, float wear, ScenePathPointFlags shape)
		{
			ScenePath plan = Line(new Vector3(-3f, 0f, 0f), new Vector3(3f, 0f, 0f), half, ScenePathClass.Yard, 2);
			plan.FromId = -1;
			plan.ToId = -1;
			plan.Wear[0] = plan.Wear[1] = wear;
			plan.Flags[0] = plan.Flags[1] = (byte)(ScenePathPointFlags.InSite | shape);
			return plan;
		}

		[Test]
		public void ASquaredYardReachesItsCornersAndNoFurther()
		{
			ScenePathField field = ScenePathField.Build(new[] { Plan(3f, 1f, ScenePathPointFlags.Squared) });
			// Half a metre in from the end by a corner (the round-ended strip, a 3 m circle here, left it 0.2 m outside).
			Assert.That(field.Sample(2.5f, 2f, out float edge, out _, out _, out _), Is.True);
			Assert.That(edge, Is.EqualTo(-0.5f).Within(0.08f), "inside the corner");
			// Off the end's middle and off the corner: distances to the rectangle, not to its points.
			Assert.That(field.Sample(4f, 0f, out edge, out _, out _, out _), Is.True);
			Assert.That(edge, Is.EqualTo(1f).Within(0.08f), "a metre past the end");
			Assert.That(field.Sample(4f, 4f, out edge, out _, out _, out _), Is.True);
			Assert.That(edge, Is.EqualTo(Mathf.Sqrt(2f)).Within(0.08f), "diagonally off the corner");
		}

		[Test]
		public void NoWayCrossingAFloorBringsItsWearIndoors()
		{
			// A ruin's apron (worn 0.3) round a floor, and a door path whose round end reaches 0.55 m in through the wall.
			ScenePath apron = Plan(3.4f, 0.3f, ScenePathPointFlags.Squared);
			apron.Points[0].x = -3.4f;
			apron.Points[1].x = 3.4f;
			ScenePath floor = Plan(3f, 1f, ScenePathPointFlags.Squared | ScenePathPointFlags.Floor);
			ScenePath door = Line(new Vector3(0f, 0f, -3f), new Vector3(0f, 0f, -6.5f), 0.55f, ScenePathClass.Yard, 2);
			door.FromId = -1;
			door.ToId = -1;
			door.Wear[0] = door.Wear[1] = 0.27f;
			floor.Id = 2;
			door.Id = 3;
			// Written in each order: the floor holds either way.
			foreach (ScenePath[] order in new[] { new[] { apron, floor, door }, new[] { door, apron, floor } })
			{
				ScenePathField field = ScenePathField.Build(order);
				// Just inside the front wall, where the apron and the door path's end both reach in through it.
				Assert.That(field.Sample(0f, -2.75f, out float edge, out _, out float wear, out _), Is.True);
				Assert.That(wear, Is.EqualTo(1f).Within(0.02f), "the floor's wear, not the door path's");
				Assert.That(edge, Is.LessThan(-0.5f), "and the deepest edge of what covers it");
				// Out in the apron, by the nearer edge as ever: the apron's own wear.
				Assert.That(field.Sample(2.5f, 3.25f, out _, out _, out wear, out _), Is.True);
				Assert.That(wear, Is.EqualTo(0.3f).Within(0.02f), "the apron outside the wall");
				// Out on the door path, its own.
				Assert.That(field.Sample(0f, -5f, out _, out _, out wear, out _), Is.True);
				Assert.That(wear, Is.EqualTo(0.27f).Within(0.02f), "the door path outside");
			}
		}

		[Test]
		public void ABridgesSpanIsNotDrawnOnTheBedUnderIt()
		{
			ScenePath path = Line(new Vector3(0f, 0f, -60f), new Vector3(0f, 0f, 60f), 1.5f, ScenePathClass.Road, 61);
			for (int i = 20; i <= 40; i++)
			{
				path.Flags[i] = (byte)ScenePathPointFlags.Bridge;
			}
			ScenePathField field = ScenePathField.Build(new[] { path });
			bool found = field.Sample(0f, 0f, out float edge, out _, out _, out _);
			Assert.That(!found || edge >= ScenePathField.OutsideMetres - 0.05f, Is.True, "mid-span is open ground");
			Assert.That(field.Sample(0f, -40f, out edge, out _, out _, out _), Is.True);
			Assert.That(edge, Is.LessThan(0f), "the road either side is drawn");
		}
	}

	/// <summary>The router: straight on the flat, switchbacks under its grade, round lakes, onto the ways already laid.</summary>
	[TestFixture]
	public class PathRouterTests
	{
		private static PathRouter Router(Func<float, float, float> ground, Func<float, float, PathCellWater> water = null)
		{
			var router = new PathRouter(1600f, 1600f, 4f, 7);
			router.Fill(ground, water, (x, z) => 2f, null, 16f);
			return router;
		}

		private static float Length(PathRouter router, PathRouter.Route route)
		{
			float length = 0f;
			for (int i = 1; i < route.Cells.Count; i++)
			{
				int a = route.Cells[i - 1], b = route.Cells[i];
				length += new Vector2(router.XOf(a % router.Width) - router.XOf(b % router.Width), router.ZOf(a / router.Width) - router.ZOf(b / router.Width)).magnitude;
			}
			return length;
		}

		[Test]
		public void OnTheFlatARoadRunsNearlyStraight()
		{
			PathRouter router = Router((x, z) => 10f);
			PathRouter.Route route = router.Find(new PathRouter.Request { Class = ScenePathClass.Road, From = new Vector2(-500f, -100f), To = new Vector2(500f, 200f) });
			Assert.That(route, Is.Not.Null);
			float straight = new Vector2(1000f, 300f).magnitude;
			Assert.That(Length(router, route), Is.LessThan(straight * 1.06f));
		}

		[Test]
		public void ARoadTakesASteepHillsideInSwitchbacksUnderItsGrade()
		{
			// 35 % the whole way up: a road (11 %) may not climb it straight.
			PathRouter router = Router((x, z) => 0.35f * z);
			PathRouter.Route route = router.Find(new PathRouter.Request { Class = ScenePathClass.Road, From = new Vector2(0f, -250f), To = new Vector2(0f, 250f) });
			Assert.That(route, Is.Not.Null);
			float steepest = 0f;
			for (int i = 1; i < route.Cells.Count; i++)
			{
				int a = route.Cells[i - 1], b = route.Cells[i];
				float run = new Vector2(router.XOf(a % router.Width) - router.XOf(b % router.Width), router.ZOf(a / router.Width) - router.ZOf(b / router.Width)).magnitude;
				steepest = Mathf.Max(steepest, Mathf.Abs(router.Height[b] - router.Height[a]) / run);
			}
			Assert.That(steepest, Is.LessThanOrEqualTo(ScenePathStyle.For(ScenePathClass.Road).MaxGrade * 2.2f + 1e-3f));
			Assert.That(Length(router, route), Is.GreaterThan(500f * 1.5f), "it winds to gain the height");
		}

		[Test]
		public void AWayGoesRoundALake()
		{
			PathRouter router = Router((x, z) => 10f, (x, z) => x * x + z * z < 150f * 150f ? PathCellWater.Open : PathCellWater.Dry);
			PathRouter.Route route = router.Find(new PathRouter.Request { Class = ScenePathClass.Footpath, From = new Vector2(-400f, 0f), To = new Vector2(400f, 0f) });
			Assert.That(route, Is.Not.Null);
			foreach (int cell in route.Cells)
			{
				Assert.That(router.Water[cell], Is.Not.EqualTo(PathCellWater.Open));
			}
		}

		[Test]
		public void ASpurStopsWhereItMeetsAWay()
		{
			PathRouter router = Router((x, z) => 10f);
			var road = new List<Vector3>();
			for (float x = -600f; x <= 600f; x += 2f)
			{
				road.Add(new Vector3(x, 10f, 0f));
			}
			router.MarkWay(road, ScenePathClass.Road, 2.4f);
			PathRouter.Route route = router.Find(new PathRouter.Request
			{
				Class = ScenePathClass.Footpath,
				From = new Vector2(100f, 300f),
				To = new Vector2(-500f, 0f),
				JoinNetwork = 8f,
			});
			Assert.That(route, Is.Not.Null);
			Assert.That(route.Joined, Is.True);
			Assert.That(Length(router, route), Is.LessThan(360f), "it meets the road nearby rather than walking to the far town");
		}

		/// <summary>
		/// An escarpment: low ground south of z = 0, 60 m higher north of z = 6 (a wall of ten metres a metre), and east of
		/// x = 340 a gentle ramp up it. Flo Monolith's falls are such a wall, and ways came down it (2026-10-12).
		/// </summary>
		public static float Escarpment(float x, float z)
		{
			float wall = 10f + 60f * Mathf.Clamp01(z / 6f);
			float ramp = 10f + 60f * Mathf.Clamp01((z + 150f) / 300f);
			return x < 300f ? wall : x > 340f ? ramp : Mathf.Lerp(wall, ramp, (x - 300f) / 40f);
		}

		[Test]
		public void AWayNeverStepsDownACliffOntoABridge()
		{
			PathRouter router = Router(Escarpment);
			// A bridge at the cliff's foot whose marked strip runs on up the wall (a deck aimed up a fall): its cells were
			// climbed for free, and a relaxed search stepped straight down the cliff onto it.
			router.MarkBridge(9, new Vector3(0f, 10f, -4f), 0f, 14f);
			PathRouter.Route route = router.Find(new PathRouter.Request { Class = ScenePathClass.Footpath, From = new Vector2(0f, -100f), To = new Vector2(0f, 100f), Relaxed = true });
			Assert.That(route, Is.Not.Null, "it goes round by the ramp");
			for (int i = 1; i < route.Cells.Count; i++)
			{
				int a = route.Cells[i - 1], b = route.Cells[i];
				if (router.IsDeck(a) && router.IsDeck(b) && router.Bridge[a] == router.Bridge[b])
				{
					continue;
				}
				float run = new Vector2(router.XOf(a % router.Width) - router.XOf(b % router.Width), router.ZOf(a / router.Width) - router.ZOf(b / router.Width)).magnitude;
				Assert.That(Mathf.Abs(router.Height[b] - router.Height[a]), Is.LessThanOrEqualTo(PathRouter.WalkableGrade * run + 1e-3f), $"step {i} climbs a cliff");
				Assert.That(router.IsSteep(b) && i < route.Cells.Count - 1, Is.False, $"step {i} enters a cell with a cliff in it");
			}
		}

		[Test]
		public void AWayIsNetworkOnlyAtItsOwnHeight()
		{
			PathRouter router = Router(Escarpment);
			// A way along the cliff's foot: the cells on the top beside it are not on it.
			var foot = new List<Vector3>();
			for (float x = -200f; x <= 200f; x += 2f)
			{
				foot.Add(new Vector3(x, 10f, -2f));
			}
			router.MarkWay(foot, ScenePathClass.Footpath, 4f);
			Assert.That(router.Network[router.IndexAt(0f, -2f)], Is.Not.EqualTo((byte)255));
			// The next cell north is centred 4 m up the wall, 20 m above the way.
			Assert.That(router.Network[router.IndexAt(0f, 3f)], Is.EqualTo((byte)255), "the wall above is not on the way at its foot");
		}

		/// <summary>A gorge along z: plateaus at 50 m, a floor at 20 m between walls (two metres of wall each side) with <paramref name="halfFloor"/> metres either side of x = 0.</summary>
		public static float Gorge(float x, float z, float halfFloor) =>
			Mathf.Abs(x) < halfFloor ? 20f : Mathf.Abs(x) < halfFloor + 2f ? Mathf.Lerp(20f, 50f, (Mathf.Abs(x) - halfFloor) / 2f) : 50f;

		[Test]
		public void AGorgeIsBridgedRimToRim()
		{
			PathRouter router = Router((x, z) => Gorge(x, z, 8f));
			PathRouter.Route route = router.Find(new PathRouter.Request { Class = ScenePathClass.Footpath, From = new Vector2(-200f, 0f), To = new Vector2(200f, 0f) });
			Assert.That(route, Is.Not.Null, "the gorge runs the map's length: only a bridge crosses it");
			float longest = 0f;
			for (int i = 1; i < route.Cells.Count; i++)
			{
				int a = route.Cells[i - 1], b = route.Cells[i];
				longest = Mathf.Max(longest, new Vector2(router.XOf(a % router.Width) - router.XOf(b % router.Width), router.ZOf(a / router.Width) - router.ZOf(b / router.Width)).magnitude);
				Assert.That(router.Height[b], Is.GreaterThan(45f), $"step {i} goes down into the gorge");
			}
			Assert.That(longest, Is.InRange(20f, PathRouter.MaxGorgeSpan + router.Cell), "one span rim to rim");
		}

		[Test]
		public void AGorgeWiderThanABridgeSpansIsNotCrossed()
		{
			PathRouter router = Router((x, z) => Gorge(x, z, 30f));
			PathRouter.Route route = router.Find(new PathRouter.Request { Class = ScenePathClass.Footpath, From = new Vector2(-200f, 0f), To = new Vector2(200f, 0f), Relaxed = true });
			Assert.That(route, Is.Null);
		}
	}

	/// <summary>The network: neighbour links between settlements, fading spurs, fords and bridges, and the same plan every time.</summary>
	[TestFixture]
	public class PathNetworkPlannerTests
	{
		private static PointOfInterestRecord Site(int id, POIType kind, float x, float z, float radius)
			=> new PointOfInterestRecord { Id = id, Kind = kind, Position = new Vector3(x, 10f, z), Radius = radius, Yaw = 0f };

		private static PathPlanInput Input(List<PointOfInterestRecord> records, Func<float, float, PathCellWater> water = null, float depth = 0f)
		{
			var keepOuts = new List<PointOfInterestKeepOut>();
			foreach (PointOfInterestRecord r in records)
			{
				keepOuts.Add(new PointOfInterestKeepOut { Id = r.Id, X = r.Position.x, Z = r.Position.z, Radius = r.Radius });
			}
			return new PathPlanInput
			{
				WidthMetres = 3200f,
				DepthMetres = 3200f,
				Ground = (x, z) => 10f,
				WaterAt = water,
				WaterDepth = (x, z) => depth,
				Records = records,
				KeepOuts = keepOuts,
				HasStreets = r => PathNetworkPlanner.RankOf(r.Kind) >= 3,
				Seed = 11,
			};
		}

		private static List<PointOfInterestRecord> ThreeTowns() => new List<PointOfInterestRecord>
		{
			Site(1, POIType.Town, -1000f, 0f, 40f),
			Site(2, POIType.Town, 0f, 0f, 40f),
			Site(3, POIType.Town, 1000f, 0f, 40f),
		};

		[Test]
		public void TheLesserEndDecidesTheClass()
		{
			Assert.That(PathNetworkPlanner.LinkClass(5, 4), Is.EqualTo(ScenePathClass.Highway));
			Assert.That(PathNetworkPlanner.LinkClass(3, 4), Is.EqualTo(ScenePathClass.Road));
			Assert.That(PathNetworkPlanner.LinkClass(3, 3), Is.EqualTo(ScenePathClass.Road));
			Assert.That(PathNetworkPlanner.LinkClass(2, 5), Is.EqualTo(ScenePathClass.CartTrack));
		}

		[Test]
		public void TownsInALineAreJoinedNeighbourToNeighbour()
		{
			PathPlan plan = PathNetworkPlanner.Plan(Input(ThreeTowns()));
			List<ScenePath> roads = plan.Paths.FindAll(p => p.Class == ScenePathClass.Road);
			Assert.That(roads.Count, Is.EqualTo(2), string.Join("\n", plan.Notes));
			Assert.That(roads.Exists(p => (p.FromId == 1 || p.ToId == 1) && (p.FromId == 3 || p.ToId == 3)), Is.False, "the end towns meet through the middle one");
		}

		/// <summary>
		/// A street town's ways come in on their own bearings toward where they go (2026-10-18: the road decides the street),
		/// each ending inside on that bearing <see cref="SiteEntrances.EntryDepth"/> of the town's reach in, where the layout's
		/// street from that entrance passes.
		/// </summary>
		[Test]
		public void AStreetTownsWaysComeInOnTheirOwnBearings()
		{
			List<PointOfInterestRecord> towns = ThreeTowns();
			PathPlan plan = PathNetworkPlanner.Plan(Input(towns));
			foreach (ScenePath road in plan.Paths)
			{
				for (int end = 0; end < 2; end++)
				{
					PointOfInterestRecord site = towns.Find(t => t.Id == (end == 0 ? road.FromId : road.ToId));
					PointOfInterestRecord other = towns.Find(t => t.Id == (end == 0 ? road.ToId : road.FromId));
					float entrance = end == 0 ? road.FromEntrance : road.ToEntrance;
					Assert.That(float.IsNaN(entrance), Is.False);
					float toward = Mathf.Atan2(other.Position.x - site.Position.x, other.Position.z - site.Position.z) * Mathf.Rad2Deg - site.Yaw;
					Assert.That(Mathf.Abs(Mathf.DeltaAngle(entrance, toward)), Is.LessThanOrEqualTo(1f), "on the way's own bearing");
					Vector2 dir = SiteEntrances.Direction(site, entrance);
					Vector2 expected = new Vector2(site.Position.x, site.Position.z) + dir * (SiteEntrances.EntryDepth * site.Radius);
					float nearest = float.PositiveInfinity;
					for (int i = 0; i < road.Count; i++)
					{
						if ((road.FlagsAt(i) & ScenePathPointFlags.InSite) != 0)
						{
							nearest = Mathf.Min(nearest, Vector2.Distance(new Vector2(road.Points[i].x, road.Points[i].z), expected));
						}
					}
					Assert.That(nearest, Is.LessThan(0.5f), $"way {road.Id} ends inside on its bearing, where the street from it runs");
				}
			}
		}

		/// <summary>Two roads reaching a town from near bearings come in by one entrance (they meet outside its gate).</summary>
		[Test]
		public void WaysFromNearBearingsShareAnEntrance()
		{
			var towns = new List<PointOfInterestRecord>
			{
				Site(1, POIType.Town, 0f, 0f, 40f),
				Site(2, POIType.Town, 1000f, -200f, 40f),
				Site(3, POIType.Town, 1000f, 200f, 40f),
			};
			PathPlan plan = PathNetworkPlanner.Plan(Input(towns));
			var entrances = new List<float>();
			foreach (ScenePath road in plan.Paths)
			{
				if (road.FromId == 1)
				{
					entrances.Add(road.FromEntrance);
				}
				else if (road.ToId == 1)
				{
					entrances.Add(road.ToEntrance);
				}
			}
			Assert.That(entrances.Count, Is.EqualTo(2), string.Join("\n", plan.Notes));
			Assert.That(entrances[1], Is.EqualTo(entrances[0]), "the second road takes the first's entrance, 23° from its own");
		}

		private static PointOfInterestShape Moat(PointOfInterestRecord site, float inner, float width)
			=> new PointOfInterestShape { SiteId = site.Id, Shaper = MoatShaper.ShaperName, Position = site.Position, Values = new[] { 1f, inner, width, 3f, 0f } };

		/// <summary>
		/// Every way into a moated site comes in past its moat on the bearing of the gate it opens, crossing the ditch inside
		/// the site (on the drawbridge), and none is routed through the ditch or the bailey: the way that leaves the gate
		/// for a neighbour off to the side goes round outside the moat.
		/// </summary>
		[TestCase(POIType.Castle)]
		[TestCase(POIType.Keep)]
		public void AMoatedSitesWaysCrossOnlyAtTheirGates(POIType kind)
		{
			PointOfInterestRecord stronghold = Site(2, kind, 0f, 0f, 40f);
			var records = new List<PointOfInterestRecord> { Site(1, POIType.Town, -1000f, 60f, 40f), stronghold, Site(3, POIType.Town, 1000f, -90f, 40f) };
			PathPlanInput input = Input(records);
			const float Inner = 41.5f, Width = 8f;
			input.Shapes = new List<PointOfInterestShape> { Moat(stronghold, Inner, Width) };
			PathPlan plan = PathNetworkPlanner.Plan(input);
			List<ScenePath> ways = plan.Paths.FindAll(p => p.FromId == 2 || p.ToId == 2);
			Assert.That(ways.Count, Is.EqualTo(2), string.Join("\n", plan.Notes));
			foreach (ScenePath way in ways)
			{
				float entrance = way.FromId == 2 ? way.FromEntrance : way.ToEntrance;
				Assert.That(float.IsNaN(entrance), Is.False, "the layout opens a gate on the way's bearing");
				if (kind == POIType.Castle)
				{
					// A walled site's ways come in on their own bearings; its gates open there.
					PointOfInterestRecord other = records.Find(r => r.Id == (way.FromId == 2 ? way.ToId : way.FromId));
					float toward = Mathf.Atan2(other.Position.x, other.Position.z) * Mathf.Rad2Deg;
					Assert.That(Mathf.Abs(Mathf.DeltaAngle(entrance, toward)), Is.LessThanOrEqualTo(1f), "a walled site's way comes in on its own bearing");
				}
				Vector2 gate = new Vector2(Mathf.Sin(entrance * Mathf.Deg2Rad), Mathf.Cos(entrance * Mathf.Deg2Rad));
				bool crossed = false;
				for (int i = 0; i < way.Count; i++)
				{
					var p = new Vector2(way.Points[i].x, way.Points[i].z);
					if ((way.FlagsAt(i) & ScenePathPointFlags.InSite) == 0)
					{
						Assert.That(p.magnitude, Is.GreaterThanOrEqualTo(Inner + Width), $"way {way.Id} point {i} at {p} is routed through the moat or the bailey");
					}
					else if (p.magnitude > Inner && p.magnitude < Inner + Width)
					{
						crossed = true;
						Assert.That(Mathf.Abs(p.x * gate.y - p.y * gate.x), Is.LessThan(0.5f), $"way {way.Id} crosses the ditch at {p}, off its gate's bearing {entrance}");
					}
				}
				Assert.That(crossed, Is.True, $"way {way.Id} crosses its moat inside the site");
			}
		}

		[Test]
		public void ARuinTakesATrailThatFadesTowardIt()
		{
			List<PointOfInterestRecord> records = ThreeTowns();
			records.Add(Site(9, POIType.Ruins, 100f, 700f, 30f));
			PathPlan plan = PathNetworkPlanner.Plan(Input(records));
			ScenePath trail = plan.Paths.Find(p => p.ToId == 9);
			Assert.That(trail, Is.Not.Null, string.Join("\n", plan.Notes));
			Assert.That(trail.Class, Is.EqualTo(ScenePathClass.Trail));
			Assert.That(trail.Wear[trail.Count - 1], Is.LessThan(trail.Wear[0]), "kept where it leaves the road");
			Assert.That(trail.Wear[trail.Count - 1], Is.LessThan(0.25f), "mostly overgrown at the ruin");
			Assert.That(trail.FromId, Is.EqualTo(-1).Or.EqualTo(2), "it joins the network");
		}

		[Test]
		public void AShallowRiverIsFordedAndADeepOneBridged()
		{
			List<PointOfInterestRecord> records = ThreeTowns();
			Func<float, float, PathCellWater> river = (x, z) => Mathf.Abs(x - 500f) < 8f ? PathCellWater.River : PathCellWater.Dry;

			PathPlan shallow = PathNetworkPlanner.Plan(Input(records, river, 0.3f));
			Assert.That(shallow.Bridges.Count, Is.Zero);
			Assert.That(shallow.Fords, Is.GreaterThanOrEqualTo(1));

			PathPlan deep = PathNetworkPlanner.Plan(Input(records, river, 1.5f));
			Assert.That(deep.Bridges.Count, Is.EqualTo(1), string.Join("\n", deep.Notes));
			float yaw = deep.Bridges[0].Yaw;
			Assert.That(Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)), Mathf.Abs(Mathf.DeltaAngle(yaw, 270f))), Is.LessThan(25f), "across the river");
			Assert.That(deep.Bridges[0].Span, Is.InRange(12f, 40f));
		}

		[Test]
		public void ARoadBetweenSettlementsWadesOnlyABroadShallowRiver()
		{
			// Deeper than its class fords: bridged by anyone.
			Assert.That(PathNetworkPlanner.Wades(ScenePathClass.CartTrack, false, 0.6f, 5f), Is.False);
			Assert.That(PathNetworkPlanner.Wades(ScenePathClass.Highway, true, 0.05f, 30f), Is.False);
			// A stream a cart could wade: a spur wades it, the settlements' road bridges it unless it is broad.
			Assert.That(PathNetworkPlanner.Wades(ScenePathClass.CartTrack, false, 0.4f, 5f), Is.True);
			Assert.That(PathNetworkPlanner.Wades(ScenePathClass.CartTrack, true, 0.4f, 5f), Is.False);
			Assert.That(PathNetworkPlanner.Wades(ScenePathClass.CartTrack, true, 0.4f, PathNetworkPlanner.SettlementFordWidthMetres + 1f), Is.True);
			Assert.That(PathNetworkPlanner.Wades(ScenePathClass.Trail, false, 0.6f, 5f), Is.True);
		}

		/// <summary>
		/// A stream 5 m wide and 0.3 m deep, which every class could wade: the road between the towns bridges it, and the
		/// trail to a ruin across a second such stream wades it. By depth alone the roads of a hill country waded every
		/// stream they met (Flo Monolith, 2026-10-13).
		/// </summary>
		[Test]
		public void TheTownsRoadBridgesAStreamTheRuinsTrailWades()
		{
			List<PointOfInterestRecord> records = ThreeTowns();
			records.Add(Site(9, POIType.Ruins, -300f, 700f, 30f));
			Func<float, float, PathCellWater> river = (x, z) => Mathf.Abs(x - 500f) < 2.5f || Mathf.Abs(z - 350f) < 2.5f ? PathCellWater.River : PathCellWater.Dry;
			PathPlan plan = PathNetworkPlanner.Plan(Input(records, river, 0.3f));
			string notes = string.Join("\n", plan.Notes);
			ScenePath road = plan.Paths.Find(p => p.Class == ScenePathClass.Road && (p.FromId == 3 || p.ToId == 3));
			Assert.That(road, Is.Not.Null, notes);
			Assert.That(plan.Bridges.Exists(b => b.PathId == road.Id && Mathf.Abs(b.Centre.x - 500f) < 6f), Is.True, "the road is bridged over the stream\n" + notes);
			Assert.That(Flagged(road, ScenePathPointFlags.Ford), Is.False, "the road wades nothing");
			ScenePath trail = plan.Paths.Find(p => p.ToId == 9);
			Assert.That(trail, Is.Not.Null, notes);
			Assert.That(Flagged(trail, ScenePathPointFlags.Ford), Is.True, "the trail wades its stream");
			Assert.That(plan.Bridges.Exists(b => b.PathId == trail.Id), Is.False, "no bridge on the trail");
		}

		/// <summary>
		/// A stream 6 m wide, a hand deep but for a channel 0.6 m wide and 0.9 m deep down its middle: the trail across it is
		/// bridged, whether or not one of its points (2 m apart) falls in the channel. Its depth was read at those points only.
		/// </summary>
		[Test]
		public void AStreamsDeepChannelIsFoundBetweenTheWaysPoints()
		{
			List<PointOfInterestRecord> records = ThreeTowns();
			records.Add(Site(9, POIType.Ruins, -300f, 700f, 30f));
			Func<float, float, PathCellWater> river = (x, z) => Mathf.Abs(z - 350f) < 3f ? PathCellWater.River : PathCellWater.Dry;
			PathPlanInput input = Input(records, river);
			input.WaterDepth = (x, z) => Mathf.Abs(z - 350f) < 0.3f ? 0.9f : 0.15f;
			PathPlan plan = PathNetworkPlanner.Plan(input);
			ScenePath trail = plan.Paths.Find(p => p.ToId == 9);
			Assert.That(trail, Is.Not.Null, string.Join("\n", plan.Notes));
			Assert.That(plan.Bridges.Exists(b => b.PathId == trail.Id), Is.True, "the trail is bridged over the channel\n" + string.Join("\n", plan.Notes));
			Assert.That(Flagged(trail, ScenePathPointFlags.Ford), Is.False, "nothing waded");
		}

		private static bool Flagged(ScenePath path, ScenePathPointFlags flag)
		{
			for (int i = 0; i < path.Count; i++)
			{
				if ((path.FlagsAt(i) & flag) != 0)
				{
					return true;
				}
			}
			return false;
		}

		[Test]
		public void AWayRunsStraightOntoItsBridge()
		{
			List<PointOfInterestRecord> records = ThreeTowns();
			// A river slanting across the way between the middle and east towns, so the route meets it at an angle.
			Func<float, float, PathCellWater> river = (x, z) => Mathf.Abs((x - 500f) * 0.8f - z * 0.6f) < 7f ? PathCellWater.River : PathCellWater.Dry;
			PathPlan plan = PathNetworkPlanner.Plan(Input(records, river, 1.5f));
			Assert.That(plan.Bridges.Count, Is.EqualTo(1), string.Join("\n", plan.Notes));
			PathBridgeSite bridge = plan.Bridges[0];
			ScenePath road = plan.Paths.Find(p => p.Id == bridge.PathId);
			float r = bridge.Yaw * Mathf.Deg2Rad;
			var axis = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			var across = new Vector2(-axis.y, axis.x);
			float deck = bridge.Span * 0.5f + 2.5f;
			int onAxis = 0;
			for (int i = 0; i < road.Count; i++)
			{
				var offset = new Vector2(road.Points[i].x - bridge.Centre.x, road.Points[i].z - bridge.Centre.z);
				float along = Vector2.Dot(offset, axis), side = Vector2.Dot(offset, across);
				if (Mathf.Abs(along) <= deck + 4f && Mathf.Abs(side) < 20f)
				{
					Assert.That(Mathf.Abs(side), Is.LessThan(0.05f), $"point {i}, {along:0.0} m along the bridge, is off its axis");
					bool bridged = (road.FlagsAt(i) & ScenePathPointFlags.Bridge) != 0;
					Assert.That(bridged, Is.EqualTo(Mathf.Abs(along) <= deck), $"point {i}: on the deck exactly where it is over it (along {along:0.00}, deck {deck:0.00}, side {side:0.000}, of {road.Count}, flags {road.FlagsAt(i)})");
					onAxis++;
				}
			}
			Assert.That(onAxis, Is.GreaterThan(5));
		}

		/// <summary>
		/// A river slanting 60° across the way: the bridge used to take the way's heading through the water, so its deck ran
		/// aslant down the river and one end stood out over the falling bank (Flo Monolith, 2026-10-12). It turns to cross
		/// square (as far as <c>BridgeTurnDegrees</c> allows), and the way eases onto it and off again without a jump.
		/// </summary>
		[Test]
		public void ABridgeCrossesItsRiverSquare()
		{
			List<PointOfInterestRecord> records = ThreeTowns();
			// Its banks run at 60° to east-west; square across it is a heading of 150° (or 330°).
			Func<float, float, PathCellWater> river = (x, z) => Mathf.Abs((x - 500f) * 0.5f - z * 0.866f) < 5f ? PathCellWater.River : PathCellWater.Dry;
			PathPlan plan = PathNetworkPlanner.Plan(Input(records, river, 1.5f));
			Assert.That(plan.Bridges.Count, Is.EqualTo(1), string.Join("\n", plan.Notes));
			PathBridgeSite bridge = plan.Bridges[0];
			ScenePath road = plan.Paths.Find(p => p.Id == bridge.PathId);
			// The way's own heading over the river, from 40 m before the bridge to 40 m past it: the bridge turns from it toward
			// square by as much as BridgeTurnDegrees (40°) allows. (Since 2026-10-18 the towns' ways come in on their own
			// bearings, due east here, so the way meets the river 60° off square and the bridge turns the full 40°.)
			Vector3 before = road.Points[0], after = road.Points[road.Count - 1];
			float bestBefore = float.PositiveInfinity, bestAfter = float.PositiveInfinity;
			foreach (Vector3 p in road.Points)
			{
				float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(bridge.Centre.x, bridge.Centre.z));
				bool ahead = Vector2.Dot(new Vector2(p.x - bridge.Centre.x, p.z - bridge.Centre.z), new Vector2(after.x - before.x, after.z - before.z)) > 0f;
				if (ahead && Mathf.Abs(d - 40f) < bestAfter)
				{
					bestAfter = Mathf.Abs(d - 40f);
					after = p;
				}
				else if (!ahead && Mathf.Abs(d - 40f) < bestBefore)
				{
					bestBefore = Mathf.Abs(d - 40f);
					before = p;
				}
			}
			float wayYaw = Mathf.Atan2(after.x - before.x, after.z - before.z) * Mathf.Rad2Deg;
			float Off(float yaw) => Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(yaw, 150f)), Mathf.Abs(Mathf.DeltaAngle(yaw, 330f)));
			float allowed = Mathf.Max(0f, Off(wayYaw) - 40f);
			Assert.That(Off(bridge.Yaw), Is.LessThan(allowed + 10f), $"square across the river as far as its turn allows (yaw {bridge.Yaw:0.0}, way {wayYaw:0.0})");
			Assert.That(bridge.Span, Is.InRange(9f, 14f), "the water's width across it, not the way's slant through it");
			for (int i = 1; i < road.Count; i++)
			{
				Assert.That(Vector3.Distance(road.Points[i], road.Points[i - 1]), Is.LessThan(6f), $"no jump in the way at point {i}");
			}
		}

		/// <summary>
		/// A town below an escarpment and one above it beyond a river along its top, and a waterfall's site at the cliff's
		/// foot: every way goes round by the ramp, crosses on a bridge between banks at one height, and enters the fall's
		/// site from ground a walker can stand on. In Flo Monolith a spur joined a way 60 m below across the cliff's edge,
		/// a bridge was aimed up the fall, and its approach was straightened up the wall (2026-10-12).
		/// </summary>
		[Test]
		public void AWayNeverClimbsACliffToReachItsBridge()
		{
			var records = new List<PointOfInterestRecord>
			{
				Site(1, POIType.Town, 0f, -400f, 40f),
				Site(2, POIType.Town, 0f, 400f, 40f),
				Site(3, POIType.Waterfall, 0f, -10f, 6f),
			};
			Func<float, float, PathCellWater> river = (x, z) => z > 20f && z < 28f && x < 600f ? PathCellWater.River : PathCellWater.Dry;
			PathPlanInput input = Input(records, river, 1.5f);
			input.Ground = PathRouterTests.Escarpment;
			PathPlan plan = PathNetworkPlanner.Plan(input);
			Assert.That(plan.Paths.Exists(p => p.FromId == 1 && p.ToId == 2 || p.FromId == 2 && p.ToId == 1), Is.True, string.Join("\n", plan.Notes));
			var notes = new List<string>();
			foreach (ScenePath path in plan.Paths)
			{
				PathNetworkPlanner.CountUnwalkable(path, notes);
			}
			Assert.That(notes, Is.Empty, string.Join("\n", notes));
		}

		/// <summary>
		/// A waterfall's site stands in its river, midway between lip and pool, and both towns are on one bank: the spur to it
		/// ends on the bank it comes from, and no bridge is laid. Its inner point was in the water and the run of river at the
		/// way's end was bridged: a deck over the fall that no way crossed.
		/// </summary>
		[Test]
		public void ASpurToAWaterfallInItsRiverLaysNoBridge()
		{
			var records = new List<PointOfInterestRecord>
			{
				Site(1, POIType.Town, 0f, -400f, 40f),
				Site(2, POIType.Town, 300f, -400f, 40f),
				Site(3, POIType.Waterfall, 0f, 24f, 8f),
			};
			Func<float, float, PathCellWater> river = (x, z) => z > 20f && z < 28f ? PathCellWater.River : PathCellWater.Dry;
			PathPlan plan = PathNetworkPlanner.Plan(Input(records, river, 3f));
			ScenePath spur = plan.Paths.Find(p => p.ToId == 3 || p.FromId == 3);
			Assert.That(spur, Is.Not.Null, string.Join("\n", plan.Notes));
			Assert.That(plan.Bridges, Is.Empty, string.Join("\n", plan.Notes));
			for (int i = 0; i < spur.Count; i++)
			{
				Assert.That(river(spur.Points[i].x, spur.Points[i].z), Is.EqualTo(PathCellWater.Dry), $"spur point {i} is in the river");
			}
		}

		/// <summary>Two towns either side of a gorge 20 m wide that runs the map's length: one bridge, rim to rim, at the rims' height.</summary>
		[Test]
		public void TownsAcrossAGorgeAreJoinedByOneBridgeOverIt()
		{
			var records = new List<PointOfInterestRecord>
			{
				Site(1, POIType.Town, -300f, 0f, 40f),
				Site(2, POIType.Town, 300f, 0f, 40f),
			};
			PathPlanInput input = Input(records);
			input.Ground = (x, z) => PathRouterTests.Gorge(x, z, 8f);
			PathPlan plan = PathNetworkPlanner.Plan(input);
			Assert.That(plan.Gorges, Is.EqualTo(1), string.Join("\n", plan.Notes));
			PathBridgeSite bridge = plan.Bridges.Find(b => b.Gorge);
			Assert.That(bridge.Span, Is.InRange(20f, PathRouter.MaxGorgeSpan + input.CellMetres));
			Assert.That(bridge.Centre.y, Is.EqualTo(50f).Within(0.5f), "at the rims' height");
			Assert.That(Mathf.Abs(bridge.Centre.x), Is.LessThan(3f), "over the gorge");
			var notes = new List<string>();
			foreach (ScenePath path in plan.Paths)
			{
				PathNetworkPlanner.CountUnwalkable(path, notes);
				for (int i = 0; i < path.Count; i++)
				{
					if ((path.FlagsAt(i) & ScenePathPointFlags.Bridge) == 0)
					{
						Assert.That(path.Points[i].y, Is.GreaterThan(45f), $"way {path.Id} point {i} is down in the gorge off the deck");
					}
				}
			}
			Assert.That(notes, Is.Empty, string.Join("\n", notes));
		}

		/// <summary>
		/// Two towns either side of a gorge whose east rim stands 4 m over its west, the way between them crossing it aslant:
		/// the bridge is a kept stone arch on a level deck, its approaches built up on the low rim and cut into the high one
		/// past the deck's ends (<see cref="PropsFeature.TryArchDeck"/>), and the way runs straight along the bridge's axis
		/// over them as well as the deck, onto the ramps' crests rather than across their banked sides.
		/// </summary>
		[Test]
		public void AWayOntoARakedGorgesArchRunsStraightOverItsApproaches()
		{
			var records = new List<PointOfInterestRecord>
			{
				Site(1, POIType.Town, -300f, 300f, 40f),
				Site(2, POIType.Town, 300f, -300f, 40f),
			};
			PathPlanInput input = Input(records);
			input.Ground = (x, z) =>
			{
				float d = Mathf.Abs(x), rim = x < 0f ? 50f : 54f;
				return d < 8f ? 20f : d < 10f ? Mathf.Lerp(20f, rim, (d - 8f) / 2f) : rim;
			};
			PathPlan plan = PathNetworkPlanner.Plan(input);
			Assert.That(plan.Gorges, Is.EqualTo(1), string.Join("\n", plan.Notes));
			PathBridgeSite bridge = plan.Bridges.Find(b => b.Gorge);
			Assert.That(bridge.Approach, Is.GreaterThan(bridge.Span * 0.5f + 3f), "the arch's approaches reach on past its deck");
			Assert.That(bridge.Approach, Is.LessThan(bridge.Span * 0.5f + PropsFeature.MaxArchApproachMetres + 6f));

			float yaw = bridge.Yaw * Mathf.Deg2Rad;
			var axis = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
			var centre = new Vector2(bridge.Centre.x, bridge.Centre.z);
			int onAxis = 0;
			foreach (ScenePath path in plan.Paths)
			{
				for (int i = 0; i < path.Count; i++)
				{
					var offset = new Vector2(path.Points[i].x, path.Points[i].z) - centre;
					float along = Vector2.Dot(offset, axis);
					if ((path.FlagsAt(i) & ScenePathPointFlags.InSite) != 0 || Mathf.Abs(along) > bridge.Approach)
					{
						continue;
					}
					float side = Mathf.Abs(offset.x * axis.y - offset.y * axis.x);
					if (side > 30f)
					{
						continue;
					}
					Assert.That(side, Is.LessThan(0.3f), $"way {path.Id} point {i}, {along:0.0} m along the bridge, is {side:0.0} m off its axis");
					onAxis++;
				}
			}
			Assert.That(onAxis, Is.GreaterThan(4), "a way crosses the bridge");
		}

		[Test]
		public void PlanningIsDeterministic()
		{
			List<PointOfInterestRecord> records = ThreeTowns();
			records.Add(Site(9, POIType.Ruins, 100f, 700f, 30f));
			records.Add(Site(10, POIType.Shrine, -600f, -400f, 12f));
			PathPlan a = PathNetworkPlanner.Plan(Input(records));
			PathPlan b = PathNetworkPlanner.Plan(Input(records));
			Assert.That(b.Paths.Count, Is.EqualTo(a.Paths.Count));
			for (int p = 0; p < a.Paths.Count; p++)
			{
				Assert.That(b.Paths[p].Points, Is.EqualTo(a.Paths[p].Points));
				Assert.That(b.Paths[p].Wear, Is.EqualTo(a.Paths[p].Wear));
			}
		}
	}

	/// <summary>
	/// The river's level up its banks is smooth down a falling river: the bed paint and the plants' waterline used the
	/// highest of the grid's samples about a point, which stepped at every sample and cut the banks into teeth (2026-10-11).
	/// </summary>
	[TestFixture]
	public class RiverBankLevelTests
	{
		[Test]
		public void TheLevelBesideAFallingRiverHasNoSteps()
		{
			// A river 8 m wide running diagonally across the grid, falling a metre every 25 m.
			const int n = 41;
			var river = new RiverPath { Id = 0, Perennial = true, X = new float[n], Z = new float[n], Width = new float[n], Surface = new float[n] };
			for (int i = 0; i < n; i++)
			{
				river.X[i] = i * 10f * 0.8f;
				river.Z[i] = i * 10f * 0.6f;
				river.Width[i] = 8f;
				river.Surface[i] = 20f - i * 10f / 25f;
			}
			var water = new SceneWater();
			water.Rivers.Add(river);
			// Along the bank, 1.5 m outside it, in half-metre steps.
			var normal = new Vector2(-0.6f, 0.8f);
			float previous = float.NaN;
			for (float s = 20f; s < 380f; s += 0.5f)
			{
				float x = 0.8f * s + normal.x * 5.5f, z = 0.6f * s + normal.y * 5.5f;
				float level = water.RiverSurfaceNear(x, z);
				Assert.That(float.IsNegativeInfinity(level), Is.False, $"answered {s} m along");
				if (!float.IsNaN(previous))
				{
					// A 1-in-25 river falls 2 cm a half metre; a step would be a whole segment's fall (40 cm).
					Assert.That(previous - level, Is.InRange(-1e-3f, 0.03f), $"{s} m along");
				}
				previous = level;
			}
			Assert.That(float.IsNegativeInfinity(water.RiverSurfaceNear(0.8f * 100f + normal.x * 9f, 0.6f * 100f + normal.y * 9f)), Is.True,
				"past the banks and the reach, nothing");
		}
	}

	/// <summary>Every biome ground wears to a generated path and road ground: one that exists, and is not the ground itself.</summary>
	[TestFixture]
	public class PathGroundTests
	{
		[Test]
		public void EveryGroundWearsToAGeneratedPathGroundOtherThanItself()
		{
			foreach (string ground in SurfaceCatalogue.GroundNames())
			{
				if (ground.StartsWith("Path", StringComparison.Ordinal))
				{
					continue;
				}
				string path = SurfaceCatalogue.PathGroundFor(ground), road = SurfaceCatalogue.RoadGroundFor(ground);
				Assert.That(SurfaceCatalogue.TryGround(path, out _), Is.True, $"{ground}: its path ground '{path}' has no recipe");
				Assert.That(SurfaceCatalogue.TryGround(road, out _), Is.True, $"{ground}: its road ground '{road}' has no recipe");
				Assert.That(path, Is.Not.EqualTo(ground), $"{ground}: a path worn into it would be its own texture");
			}
		}

		[Test]
		public void TheCountryDecidesThePath()
		{
			Assert.That(SurfaceCatalogue.PathGroundFor(Ground.Grass), Is.EqualTo(Ground.PathLoam));
			Assert.That(SurfaceCatalogue.PathGroundFor(Ground.ForestFloor), Is.EqualTo(Ground.PathLitter));
			Assert.That(SurfaceCatalogue.PathGroundFor(Ground.Sand), Is.EqualTo(Ground.PathSand));
			Assert.That(SurfaceCatalogue.PathGroundFor(Ground.Snow), Is.EqualTo(Ground.PathSnow));
			Assert.That(SurfaceCatalogue.PathGroundFor(Ground.Peat), Is.EqualTo(Ground.PathMud));
			Assert.That(SurfaceCatalogue.RoadGroundFor(Ground.Grass), Is.EqualTo(Ground.Gravel));
			Assert.That(SurfaceCatalogue.RoadGroundFor(Ground.Sand), Is.EqualTo(Ground.PathSand), "a desert road is packed sand");
		}
	}

	/// <summary>The carve: a level bench on a hillside, meeting the ground at the shoulder, and the same level when carved again.</summary>
	[TestFixture]
	public class PathCarverTests
	{
		[Test]
		public void ABenchOnAHillsideIsLevelAcrossAndMeetsTheGroundPastItsShoulder()
		{
			// Ground falling 0.3 m a metre to the north; a cart track along x at z = 0.
			float Ground(float x, float z) => 50f - 0.3f * z;
			ScenePath track = ScenePathFieldTests.Line(new Vector3(-60f, 50f, 0f), new Vector3(60f, 50f, 0f), 1.5f, ScenePathClass.CartTrack, 61);
			var paths = new List<ScenePath> { track };
			float[][] targets = PathCarver.Targets(paths, Ground, null);
			var index = new PathSegmentIndex(paths, p => p.MaxHalfWidth + ScenePathStyle.For(p.Class).Shoulder);
			ScenePathStyle style = ScenePathStyle.For(ScenePathClass.CartTrack);

			float left = PathCarver.Blend(index, targets, 0f, -1f, out float wl);
			float right = PathCarver.Blend(index, targets, 0f, 1f, out float wr);
			Assert.That(wl, Is.EqualTo(1f));
			Assert.That(wr, Is.EqualTo(1f));
			Assert.That(right, Is.EqualTo(left).Within(1e-3f), "level across the way");
			Assert.That(left, Is.EqualTo(50f - style.Dip).Within(1e-3f), "at the centre line's height, worn a little");
			PathCarver.Blend(index, targets, 0f, 1.5f + style.Shoulder + 0.05f, out float past);
			Assert.That(past, Is.Zero, "the ground past the shoulder is left alone");
		}

		[Test]
		public void CarvingAgainLaysTheWayAtTheSameLevel()
		{
			float Ground(float x, float z) => 20f + 2.5f * Mathf.Sin(x / 17f) + 0.15f * z;
			ScenePath trail = ScenePathFieldTests.Line(new Vector3(-80f, 20f, 5f), new Vector3(80f, 20f, -5f), 0.4f, ScenePathClass.Trail, 81);
			var paths = new List<ScenePath> { trail };
			float[][] first = PathCarver.Targets(paths, Ground, null);
			var index = new PathSegmentIndex(paths, p => p.MaxHalfWidth + ScenePathStyle.For(p.Class).Shoulder);
			float Carved(float x, float z)
			{
				float level = PathCarver.Blend(index, first, x, z, out float w);
				return float.IsNaN(level) ? Ground(x, z) : Mathf.Lerp(Ground(x, z), level, w);
			}
			float[][] second = PathCarver.Targets(paths, Carved, null);
			for (int i = 0; i < trail.Count; i++)
			{
				Assert.That(second[0][i], Is.EqualTo(first[0][i]).Within(0.01f), $"point {i}");
			}
		}
	}
}
