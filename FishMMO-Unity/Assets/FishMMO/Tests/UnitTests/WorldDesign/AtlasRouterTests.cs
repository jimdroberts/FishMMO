using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Atlas;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The atlas route planner: lines leave from spread-out ports, never cross a scene, report
	/// when no clean path exists, and come out the same every time.
	/// </summary>
	[TestFixture]
	public class AtlasRouterTests
	{
		private static Vector2[] Box(float cx, float cy, float w, float h, float degrees = 0f)
		{
			float c = Mathf.Cos(degrees * Mathf.Deg2Rad), s = Mathf.Sin(degrees * Mathf.Deg2Rad);
			var corners = new[] { new Vector2(-w, -h), new Vector2(w, -h), new Vector2(w, h), new Vector2(-w, h) };
			for (int i = 0; i < 4; i++)
			{
				Vector2 p = corners[i] * 0.5f;
				corners[i] = new Vector2(cx + p.x * c - p.y * s, cy + p.x * s + p.y * c);
			}
			return corners;
		}

		/// <summary>Fails if any part of a clean route, past its first and last step, lies inside a scene.</summary>
		private static void AssertAvoidsScenes(List<Vector2[]> scenes, AtlasRoute route, string context)
		{
			if (!route.Clean)
			{
				return;
			}
			List<Vector2> p = route.Points;
			for (int i = 0; i + 1 < p.Count; i++)
			{
				for (int k = 0; k <= 20; k++)
				{
					float t = k / 20f;
					// The first stub starts on its scene's edge, and the last ends on one.
					if ((i == 0 && t < 0.02f) || (i == p.Count - 2 && t > 0.98f))
					{
						continue;
					}
					Vector2 point = Vector2.Lerp(p[i], p[i + 1], t);
					for (int s = 0; s < scenes.Count; s++)
					{
						LogAssert.IsFalse(AtlasGeometry.Contains(scenes[s], point) && !OnBoundary(scenes[s], point),
							$"{context}: route {route.Request.From}→{route.Request.To} passes through scene {s} at {point}");
					}
				}
			}
		}

		private static bool OnBoundary(Vector2[] polygon, Vector2 point)
		{
			for (int i = 0; i < polygon.Length; i++)
			{
				Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
				Vector2 ab = b - a;
				float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude);
				if (Vector2.Distance(point, a + ab * t) < 1e-3f)
				{
					return true;
				}
			}
			return false;
		}

		[Test]
		public void TwoScenesGetACleanLineBetweenTheirEdges()
		{
			var scenes = new List<Vector2[]> { Box(0, 0, 2, 2), Box(8, 0, 2, 2) };
			List<AtlasRoute> routes = AtlasRouter.Route(scenes, new[] { new AtlasRouteRequest(0, 1) }, AtlasRouterSettings.Default);
			LogAssert.AreEqual(1, routes.Count);
			AtlasRoute route = routes[0];
			LogAssert.IsTrue(route.Clean);
			LogAssert.IsFalse(route.SharesCorridor);
			Assert.That(route.Points[0].x, Is.EqualTo(1f).Within(1e-4f), "leaves from the side facing the other scene");
			Assert.That(route.Points[route.Points.Count - 1].x, Is.EqualTo(7f).Within(1e-4f), "arrives on the facing side");
			AssertAvoidsScenes(scenes, route, "pair");
		}

		[Test]
		public void ALineGoesAroundASceneInTheWay()
		{
			var scenes = new List<Vector2[]> { Box(0, 0, 2, 2), Box(6, 0, 2, 6), Box(12, 0, 2, 2) };
			List<AtlasRoute> routes = AtlasRouter.Route(scenes, new[] { new AtlasRouteRequest(0, 2) }, AtlasRouterSettings.Default);
			AtlasRoute route = routes[0];
			LogAssert.IsTrue(route.Clean, "there is room around the middle scene");
			AssertAvoidsScenes(scenes, route, "detour");
			float furthest = 0f;
			foreach (Vector2 p in route.Points)
			{
				furthest = Mathf.Max(furthest, Mathf.Abs(p.y));
			}
			LogAssert.IsTrue(furthest > 3f, $"the line must bend past the 6 km wall, reached only {furthest:0.00} km");
		}

		[Test]
		public void LinesFromOneSideLeaveThroughDifferentPorts()
		{
			var scenes = new List<Vector2[]> { Box(0, 0, 3, 3), Box(10, 3, 2, 2), Box(10, -3, 2, 2) };
			var requests = new[] { new AtlasRouteRequest(0, 1), new AtlasRouteRequest(0, 2) };
			List<AtlasRoute> routes = AtlasRouter.Route(scenes, requests, AtlasRouterSettings.Default);
			LogAssert.IsTrue(routes[0].Clean && routes[1].Clean);
			LogAssert.AreNotEqual(routes[0].Points[0], routes[1].Points[0], "each line has its own exit");
			LogAssert.AreNotEqual(routes[0].Points[1], routes[1].Points[1], "and its own first cell");
			LogAssert.IsTrue(routes[0].Points[0].y > routes[1].Points[0].y, "exits are ordered toward their targets");
			foreach (AtlasRoute route in routes)
			{
				AssertAvoidsScenes(scenes, route, "ports");
			}
		}

		[Test]
		public void AnEnclosedSceneHasNoCleanRoute()
		{
			// A ring of scenes packed so tightly their padding closes every gap.
			var scenes = new List<Vector2[]> { Box(0, 0, 1, 1) };
			for (int i = 0; i < 8; i++)
			{
				float angle = i * Mathf.PI / 4f;
				scenes.Add(Box(Mathf.Cos(angle) * 2.2f, Mathf.Sin(angle) * 2.2f, 1.8f, 1.8f, i * 45f));
			}
			scenes.Add(Box(15, 0, 2, 2));
			List<AtlasRoute> routes = AtlasRouter.Route(scenes, new[] { new AtlasRouteRequest(0, scenes.Count - 1) }, AtlasRouterSettings.Default);
			LogAssert.IsFalse(routes[0].Clean, "the centre scene is walled in");
			LogAssert.AreEqual(2, routes[0].Points.Count, "an unroutable link is a straight line, for the designer to flag");
		}

		[Test]
		public void AnInvalidRequestProducesNothing()
		{
			var scenes = new List<Vector2[]> { Box(0, 0, 2, 2) };
			List<AtlasRoute> routes = AtlasRouter.Route(scenes, new[] { new AtlasRouteRequest(0, 0), new AtlasRouteRequest(0, 5) }, AtlasRouterSettings.Default);
			LogAssert.AreEqual(2, routes.Count);
			LogAssert.AreEqual(0, routes[0].Points.Count);
			LogAssert.AreEqual(0, routes[1].Points.Count);
		}

		[Test]
		public void RoutingIsDeterministic()
		{
			var scenes = new List<Vector2[]> { Box(0, 0, 2, 3), Box(7, 4, 2, 2, 20f), Box(9, -5, 3, 2), Box(-6, 6, 2, 2) };
			var requests = new[] { new AtlasRouteRequest(0, 1), new AtlasRouteRequest(1, 2), new AtlasRouteRequest(3, 2), new AtlasRouteRequest(0, 3) };
			List<AtlasRoute> first = AtlasRouter.Route(scenes, requests, AtlasRouterSettings.Default);
			List<AtlasRoute> second = AtlasRouter.Route(scenes, requests, AtlasRouterSettings.Default);
			for (int i = 0; i < first.Count; i++)
			{
				CollectionAssert.AreEqual(first[i].Points, second[i].Points, $"route {i}");
				LogAssert.AreEqual(first[i].Clean, second[i].Clean);
			}
		}

		[Test]
		public void RandomLayoutsNeverRouteThroughAScene()
		{
			int clean = 0, total = 0;
			for (int seed = 1; seed <= 20; seed++)
			{
				var random = new System.Random(seed);
				var scenes = new List<Vector2[]>();
				var centres = new List<Vector2>();
				int attempts = 0;
				while (scenes.Count < 6 && attempts++ < 500)
				{
					var centre = new Vector2((float)(random.NextDouble() * 30 - 15), (float)(random.NextDouble() * 30 - 15));
					float w = 1f + (float)random.NextDouble() * 3f, h = 1f + (float)random.NextDouble() * 3f;
					bool clear = true;
					foreach (Vector2 other in centres)
					{
						// Keep scenes at least a couple of km apart, as the atlas's own warnings expect.
						if (Vector2.Distance(other, centre) < 7f)
						{
							clear = false;
							break;
						}
					}
					if (!clear)
					{
						continue;
					}
					centres.Add(centre);
					scenes.Add(Box(centre.x, centre.y, w, h, (float)(random.NextDouble() * 90.0)));
				}
				var requests = new List<AtlasRouteRequest>();
				for (int i = 0; i < scenes.Count; i++)
				{
					requests.Add(new AtlasRouteRequest(i, (i + 1 + random.Next(scenes.Count - 1)) % scenes.Count));
				}
				foreach (AtlasRoute route in AtlasRouter.Route(scenes, requests, AtlasRouterSettings.Default))
				{
					total++;
					if (route.Clean)
					{
						clean++;
					}
					AssertAvoidsScenes(scenes, route, $"seed {seed}");
				}
			}
			LogAssert.IsTrue(clean >= total * 0.9f, $"well-spaced layouts should route almost everything cleanly: {clean}/{total}");
		}

		[Test]
		public void ScenesTooCloseToRouteAroundGetADirectCleanLine()
		{
			// 0.6 km apart: inside each other's padding, but nothing between them.
			var scenes = new List<Vector2[]> { Box(0, 0, 2, 2), Box(2.6f, 0, 2, 2), Box(0, 6, 2, 2) };
			List<AtlasRoute> routes = AtlasRouter.Route(scenes, new[] { new AtlasRouteRequest(0, 1, new Vector2(0.2f, 0.1f), new Vector2(2.5f, 0.1f)) }, AtlasRouterSettings.Default);
			AtlasRoute route = routes[0];
			LogAssert.IsTrue(route.Clean, "a short gap with nothing in it is not a problem");
			LogAssert.IsTrue(route.Direct, "and is drawn straight across");
			AssertAvoidsScenes(scenes, route, "close pair");
			Assert.That(route.Points[0].x, Is.EqualTo(1f).Within(1e-3f));
			Assert.That(route.Points[route.Points.Count - 1].x, Is.EqualTo(1.6f).Within(1e-3f));
		}

		[Test]
		public void ADirectLineThroughAnotherSceneIsNotClean()
		{
			var scenes = new List<Vector2[]> { Box(0, 0, 2, 2), Box(1.9f, 0, 1.2f, 3), Box(3.8f, 0, 2, 2) };
			AtlasRoute route = AtlasRouter.Route(scenes, new[] { new AtlasRouteRequest(0, 2) }, AtlasRouterSettings.Default)[0];
			LogAssert.IsFalse(route.Clean, "the scene between them blocks the straight line");
		}

		[Test]
		public void AClearPairGetsAStraightLineNotAStaircase()
		{
			// Offset exits on a diagonal: the grid path is a staircase, the drawn line must not be.
			var scenes = new List<Vector2[]> { Box(0, 0, 2, 2), Box(9, 4, 2, 2) };
			var request = new AtlasRouteRequest(0, 1, new Vector2(0.2f, 0.3f), new Vector2(9f, 4f));
			AtlasRoute route = AtlasRouter.Route(scenes, new[] { request }, AtlasRouterSettings.Default)[0];
			LogAssert.IsTrue(route.Clean);
			AssertAvoidsScenes(scenes, route, "diagonal");
			// Direction changes sharper than 30° between consecutive short steps mean a kink.
			int kinks = 0;
			for (int i = 1; i + 1 < route.Points.Count; i++)
			{
				Vector2 a = route.Points[i] - route.Points[i - 1], b = route.Points[i + 1] - route.Points[i];
				if (a.sqrMagnitude > 1e-8f && b.sqrMagnitude > 1e-8f && Vector2.Angle(a, b) > 30f)
				{
					kinks++;
				}
			}
			LogAssert.AreEqual(0, kinks, "every corner is rounded");
			LogAssert.IsTrue(route.Points.Count < 40, $"pulled tight into a few runs, got {route.Points.Count} points");
		}

		[Test]
		public void ALoneExitSitsBesideItsTeleporter()
		{
			var scenes = new List<Vector2[]> { Box(0, 0, 2, 4), Box(10, 0, 2, 4) };
			var request = new AtlasRouteRequest(0, 1, new Vector2(0.5f, 1.2f), new Vector2(9.5f, -0.8f));
			AtlasRoute route = AtlasRouter.Route(scenes, new[] { request }, AtlasRouterSettings.Default)[0];
			LogAssert.IsTrue(route.Clean);
			Assert.That(route.Points[0].x, Is.EqualTo(1f).Within(1e-3f), "leaves through the side facing the other scene");
			Assert.That(route.Points[0].y, Is.EqualTo(1.2f).Within(0.05f), "level with the teleporter");
			Vector2 last = route.Points[route.Points.Count - 1];
			Assert.That(last.x, Is.EqualTo(9f).Within(1e-3f));
			Assert.That(last.y, Is.EqualTo(-0.8f).Within(0.05f), "level with the arrival point");
			// The exit leaves square to the edge.
			Vector2 stub = route.Points[1] - route.Points[0];
			LogAssert.IsTrue(Vector2.Angle(stub, Vector2.right) < 1f, $"the exit is perpendicular to the edge, got {stub}");
		}

		[Test]
		public void RoundedCornersKeepTheirEndsAndStayNearTheCorner()
		{
			var line = new List<Vector2> { new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, 3), new Vector2(8, 3) };
			List<Vector2> round = AtlasRouter.RoundCorners(line, 0.5f);
			LogAssert.AreEqual(line[0], round[0]);
			LogAssert.AreEqual(line[line.Count - 1], round[round.Count - 1]);
			foreach (Vector2 p in round)
			{
				float nearest = float.MaxValue;
				for (int i = 0; i + 1 < line.Count; i++)
				{
					Vector2 a = line[i], ab = line[i + 1] - a;
					float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
					nearest = Mathf.Min(nearest, Vector2.Distance(p, a + ab * t));
				}
				LogAssert.IsTrue(nearest <= 0.5f * 0.71f, $"{p} strays {nearest:0.000} km from the line");
			}
			LogAssert.IsFalse(round.Contains(new Vector2(4, 0)), "the corner itself is cut");
			List<Vector2> straight = AtlasRouter.RoundCorners(new List<Vector2> { Vector2.zero, Vector2.right, new Vector2(2, 0) }, 0.5f);
			LogAssert.AreEqual(3, straight.Count, "a straight run is left alone");
		}
	}
}
