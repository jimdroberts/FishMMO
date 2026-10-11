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
		public void AJunctionTakesTheNearerEdge()
		{
			ScenePath road = Line(new Vector3(-40f, 0f, 0f), new Vector3(40f, 0f, 0f), 2.4f, ScenePathClass.Road);
			ScenePath trail = Line(new Vector3(1.8f, 0f, -30f), new Vector3(1.8f, 0f, 30f), 0.4f, ScenePathClass.Trail);
			trail.Id = 2;
			ScenePathField field = ScenePathField.Build(new[] { road, trail });
			// 1 m off the road's centre (1.4 m inside its edge) and on the trail's centre (0.4 m inside its): the road's.
			Assert.That(field.Sample(1.8f, 1f, out float edge, out float half, out _, out _), Is.True);
			Assert.That(edge, Is.EqualTo(-1.4f).Within(0.08f));
			Assert.That(half, Is.EqualTo(2.4f).Within(0.05f));
			// Off the road, the trail's own.
			Assert.That(field.Sample(1.8f, 12f, out edge, out half, out _, out _), Is.True);
			Assert.That(half, Is.EqualTo(0.4f).Within(0.05f));
			Assert.That(edge, Is.EqualTo(-0.4f).Within(0.08f));
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

		[Test]
		public void AStreetTownsWaysComeInAtItsStreetEnds()
		{
			PathPlan plan = PathNetworkPlanner.Plan(Input(ThreeTowns()));
			foreach (ScenePath road in plan.Paths)
			{
				foreach (float entrance in new[] { road.FromEntrance, road.ToEntrance })
				{
					Assert.That(float.IsNaN(entrance), Is.False);
					Assert.That(Mathf.Repeat(entrance, 90f), Is.EqualTo(0f).Within(1e-3f), "at a street's end");
				}
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
