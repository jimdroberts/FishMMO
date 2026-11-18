using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Server.Implementation.World.SceneServer.AI;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins the scene-scoped NPC separation that replaces NavMesh crowd avoidance (issue #220).
	/// </summary>
	[TestFixture]
	public class AISeparationTests
	{
		private const uint SELF = 11u;

		/// <summary>Keys for a neighbour list: distinct, and none equal to <see cref="SELF"/>.</summary>
		private static List<uint> KeysFor(List<Vector3> neighbours)
		{
			List<uint> keys = new List<uint>(neighbours.Count);
			for (int i = 0; i < neighbours.Count; ++i)
			{
				keys.Add(100u + (uint)i);
			}
			return keys;
		}

		private static Vector3 Resolve(Vector3 self, List<Vector3> neighbours, float radius, float maxSpeed)
		{
			return AISeparation.Resolve(self, SELF, neighbours, KeysFor(neighbours), radius, maxSpeed);
		}

		[Test]
		public void NothingInside_NoPush()
		{
			Vector3 push = Resolve(Vector3.zero, new List<Vector3> { new Vector3(5f, 0f, 0f) }, 1f, 1f);
			Assert.That(push, Is.EqualTo(Vector3.zero));
		}

		[Test]
		public void OverlappingNeighbour_PushesDirectlyAway_Horizontally()
		{
			Vector3 push = Resolve(Vector3.zero, new List<Vector3> { new Vector3(0.5f, 3f, 0f) }, 1f, 2f);

			Assert.That(push.x, Is.LessThan(0f));
			Assert.That(push.y, Is.EqualTo(0f), "separation never lifts or sinks a body");
			Assert.That(push.z, Is.EqualTo(0f).Within(1e-6f));
		}

		[Test]
		public void PushGrowsWithOverlap_ButNeverExceedsMaxSpeed()
		{
			var near = new List<Vector3> { new Vector3(0.2f, 0f, 0f) };
			var far = new List<Vector3> { new Vector3(0.8f, 0f, 0f) };

			float nearSpeed = Resolve(Vector3.zero, near, 1f, 2f).magnitude;
			float farSpeed = Resolve(Vector3.zero, far, 1f, 2f).magnitude;

			Assert.That(nearSpeed, Is.GreaterThan(farSpeed));
			Assert.That(nearSpeed, Is.LessThanOrEqualTo(2f + 1e-5f));

			var crowd = new List<Vector3>();
			for (int i = 0; i < 8; i++) crowd.Add(new Vector3(0.1f, 0f, 0.01f * i));
			Assert.That(Resolve(Vector3.zero, crowd, 1f, 2f).magnitude, Is.LessThanOrEqualTo(2f + 1e-5f));
		}

		[Test]
		public void CoincidentNeighbour_StillProducesAStablePush()
		{
			Vector3 a = Resolve(Vector3.one, new List<Vector3> { Vector3.one }, 1f, 1f);
			Vector3 b = Resolve(Vector3.one, new List<Vector3> { Vector3.one }, 1f, 1f);

			Assert.That(a.magnitude, Is.GreaterThan(0f));
			Assert.That(a, Is.EqualTo(b), "the same overlap must push the same way every tick");
		}

		/// <summary>
		/// Two NPCs on one point push in opposite directions, so they separate.
		/// </summary>
		/// <remarks>
		/// The fixed +x tie-break this replaced pushed BOTH of them +x: they slid together at the
		/// push speed to a NavMesh edge and stayed stacked there. A spawner without random
		/// placement stacks every NPC it places on its one point, so this is the common case at a
		/// spawn, not a corner one.
		/// </remarks>
		[Test]
		public void CoincidentPair_PushesApart_InOppositeDirections()
		{
			Vector3 spot = new Vector3(3f, 1f, -2f);
			const uint A = 0x1234u;
			const uint B = 0xBEEFu;

			Vector3 pushA = AISeparation.Resolve(spot, A, new List<Vector3> { spot }, new List<uint> { B }, 1f, 1f);
			Vector3 pushB = AISeparation.Resolve(spot, B, new List<Vector3> { spot }, new List<uint> { A }, 1f, 1f);

			Assert.That(pushA.magnitude, Is.GreaterThan(0.5f));
			Assert.That(pushB.magnitude, Is.GreaterThan(0.5f));
			Assert.That((pushA + pushB).magnitude, Is.LessThan(1e-4f),
				"a stacked pair must push in exactly opposite directions, or both slide together");
			Assert.That(pushA.y, Is.EqualTo(0f));
		}

		/// <summary>
		/// Different stacked pairs separate along different axes, so a stack of three or more does
		/// not line up on one axis either.
		/// </summary>
		[Test]
		public void CoincidentPairs_UseDifferentAxes()
		{
			Vector3 ab = AISeparation.CoincidentPushDirection(1u, 2u);
			Vector3 ac = AISeparation.CoincidentPushDirection(1u, 3u);

			Assert.That(Mathf.Abs(Vector3.Dot(ab, ac)), Is.LessThan(0.999f),
				"two pairs sharing a member must not share an axis");
			Assert.That(AISeparation.CoincidentPushDirection(2u, 1u), Is.EqualTo(-ab),
				"the other member of a pair takes the opposite end of the pair's axis");
			Assert.That(ab.magnitude, Is.EqualTo(1f).Within(1e-5f));
		}

		[Test]
		public void OpposingNeighbours_CancelOut()
		{
			var pair = new List<Vector3> { new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 0f, 0f) };
			Assert.That(Resolve(Vector3.zero, pair, 1f, 1f).magnitude, Is.LessThan(1e-5f));
		}

		[Test]
		public void DisabledByZeroSpeedOrRadius()
		{
			var near = new List<Vector3> { new Vector3(0.1f, 0f, 0f) };
			Assert.That(Resolve(Vector3.zero, near, 1f, 0f), Is.EqualTo(Vector3.zero));
			Assert.That(Resolve(Vector3.zero, near, 0f, 1f), Is.EqualTo(Vector3.zero));
		}
	}
}
