using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using FishMMO.Server.Implementation.World.SceneServer.Navigation;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// NPCs on the scene's climbables: each ladder or rope is a two-way NavMesh link from its foot to its landing, an NPC
	/// that reaches one is matched back to its climbable from the link's ends, and walks the climb's own points: to its
	/// foot, up the line, over the top lifted clear of the edge, onto the landing (or the same down).
	/// </summary>
	[TestFixture]
	public class ClimbLinkTests
	{
		private static SceneClimbable Ladder(float x) => new SceneClimbable
		{
			Kind = SceneClimbableKind.Ladder,
			Bottom = new Vector3(x, 0f, 10f),
			Top = new Vector3(x, 6f, 10f),
			Out = new Vector3(0f, 0f, -1f),
			TopExit = new Vector3(0f, 0f, 1f),
			HalfWidth = 0.3f,
		};

		[Test]
		public void ALinkJoinsTheFootToTheLanding()
		{
			SceneClimbable ladder = Ladder(10f);
			ClimbingRules.LinkEnds(in ladder, out Vector3 lower, out Vector3 upper);
			Assert.That(lower, Is.EqualTo(ladder.Bottom));
			Assert.That(upper, Is.EqualTo(new Vector3(10f, 6f, 10f + Constants.Character.ClimbLandingMetres)));
		}

		[Test]
		public void TheLinkGoesBothWaysInTheClimbArea()
		{
			NavMeshLinkData link = SceneNavMeshes.LinkFor(new Vector3(1f, 0f, 2f), new Vector3(1f, 6f, 2.6f), 7, 5);
			Assert.That(link.bidirectional, Is.True);
			Assert.That(link.area, Is.EqualTo(5));
			Assert.That(link.agentTypeID, Is.EqualTo(7));
			Assert.That(link.startPosition, Is.EqualTo(new Vector3(1f, 0f, 2f)));
			Assert.That(link.endPosition, Is.EqualTo(new Vector3(1f, 6f, 2.6f)));
			Assert.That(link.costModifier, Is.LessThan(0f), "costed by its area");
		}

		[Test]
		public void ALinksEndsFindItsClimbableAndWhichWayItIsTaken()
		{
			var list = new List<SceneClimbable> { Ladder(0f), Ladder(10f), Ladder(20f) };
			ClimbingRules.LinkEnds(list[1], out Vector3 lower, out Vector3 upper);
			// The NavMesh puts a link's ends on its polygons, a little off where they were asked for.
			Vector3 nudge = new Vector3(0.2f, 0.05f, -0.1f);

			Assert.That(AIController.TryMatchClimbable(list, lower + nudge, upper - nudge, out int index, out bool upward), Is.True);
			Assert.That(index, Is.EqualTo(1));
			Assert.That(upward, Is.True);

			Assert.That(AIController.TryMatchClimbable(list, upper, lower, out index, out upward), Is.True);
			Assert.That(index, Is.EqualTo(1));
			Assert.That(upward, Is.False);

			Assert.That(AIController.TryMatchClimbable(list, new Vector3(5f, 0f, 10f), new Vector3(5f, 6f, 10.6f), out _, out _), Is.False, "a link no climbable made");
			Assert.That(AIController.TryMatchClimbable(null, lower, upper, out _, out _), Is.False);
		}

		[Test]
		public void TheWayUpGoesToTheFootUpTheLineAndOverTheTopClearOfTheEdge()
		{
			SceneClimbable ladder = Ladder(10f);
			ClimbingRules.LinkEnds(in ladder, out Vector3 lower, out Vector3 upper);
			var points = new List<Vector3>();
			Vector3 start = lower + new Vector3(0f, 0f, -0.8f);
			ClimbingRules.TraversalPoints(in ladder, true, 0.45f, start, upper, points);

			Assert.That(points[0], Is.EqualTo(start));
			Assert.That(points[points.Count - 1], Is.EqualTo(upper));
			float riseY = ladder.Top.y + Constants.Character.ClimbStepClearance;
			// Walk it finely: wherever the feet are past the wall's line (z ≥ 10) they are above the upper floor.
			float length = ClimbingRules.PathLength(points);
			for (float d = 0f; d <= length; d += 0.02f)
			{
				Vector3 at = ClimbingRules.PointAlong(points, d, out _);
				if (at.z > 10f - 0.3f && at.z < upper.z - 0.01f && at.y < 5.9f)
				{
					Assert.Fail($"{d:F2} m along: {at} cuts into the ledge");
				}
			}
			Assert.That(points.Exists(p => Mathf.Approximately(p.y, riseY)), Is.True, "lifted clear of the edge on the way over");
		}

		[Test]
		public void TheWayDownIsTheWayUpReversed()
		{
			SceneClimbable ladder = Ladder(10f);
			ClimbingRules.LinkEnds(in ladder, out Vector3 lower, out Vector3 upper);
			var up = new List<Vector3>();
			var down = new List<Vector3>();
			ClimbingRules.TraversalPoints(in ladder, true, 0.45f, lower, upper, up);
			ClimbingRules.TraversalPoints(in ladder, false, 0.45f, upper, lower, down);
			Assert.That(down.Count, Is.EqualTo(up.Count));
			for (int i = 0; i < up.Count; i++)
			{
				Assert.That(down[i], Is.EqualTo(up[up.Count - 1 - i]), $"point {i}");
			}
		}

		[Test]
		public void PointAlongClampsToTheEndsAndReportsItsLeg()
		{
			var points = new List<Vector3> { Vector3.zero, new Vector3(0f, 2f, 0f), new Vector3(1f, 2f, 0f) };
			Assert.That(ClimbingRules.PathLength(points), Is.EqualTo(3f).Within(1e-5f));
			Assert.That(ClimbingRules.PointAlong(points, -1f, out int leg), Is.EqualTo(Vector3.zero));
			Assert.That(leg, Is.EqualTo(0));
			Assert.That((ClimbingRules.PointAlong(points, 1f, out leg) - new Vector3(0f, 1f, 0f)).magnitude, Is.LessThan(1e-5f));
			Assert.That(leg, Is.EqualTo(0));
			Assert.That((ClimbingRules.PointAlong(points, 2.5f, out leg) - new Vector3(0.5f, 2f, 0f)).magnitude, Is.LessThan(1e-5f));
			Assert.That(leg, Is.EqualTo(1));
			Assert.That(ClimbingRules.PointAlong(points, 9f, out leg), Is.EqualTo(points[2]));
		}
	}
}
