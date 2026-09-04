using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins the scene-scoped NPC separation that replaces NavMesh crowd avoidance (issue #220).
	/// </summary>
	[TestFixture]
	public class AISeparationTests
	{
		[Test]
		public void NothingInside_NoPush()
		{
			Vector3 push = AISeparation.Resolve(Vector3.zero, new List<Vector3> { new Vector3(5f, 0f, 0f) }, 1f, 1f);
			Assert.That(push, Is.EqualTo(Vector3.zero));
		}

		[Test]
		public void OverlappingNeighbour_PushesDirectlyAway_Horizontally()
		{
			Vector3 push = AISeparation.Resolve(Vector3.zero, new List<Vector3> { new Vector3(0.5f, 3f, 0f) }, 1f, 2f);

			Assert.That(push.x, Is.LessThan(0f));
			Assert.That(push.y, Is.EqualTo(0f), "separation never lifts or sinks a body");
			Assert.That(push.z, Is.EqualTo(0f).Within(1e-6f));
		}

		[Test]
		public void PushGrowsWithOverlap_ButNeverExceedsMaxSpeed()
		{
			var near = new List<Vector3> { new Vector3(0.2f, 0f, 0f) };
			var far = new List<Vector3> { new Vector3(0.8f, 0f, 0f) };

			float nearSpeed = AISeparation.Resolve(Vector3.zero, near, 1f, 2f).magnitude;
			float farSpeed = AISeparation.Resolve(Vector3.zero, far, 1f, 2f).magnitude;

			Assert.That(nearSpeed, Is.GreaterThan(farSpeed));
			Assert.That(nearSpeed, Is.LessThanOrEqualTo(2f + 1e-5f));

			var crowd = new List<Vector3>();
			for (int i = 0; i < 8; i++) crowd.Add(new Vector3(0.1f, 0f, 0.01f * i));
			Assert.That(AISeparation.Resolve(Vector3.zero, crowd, 1f, 2f).magnitude, Is.LessThanOrEqualTo(2f + 1e-5f));
		}

		[Test]
		public void CoincidentNeighbour_StillProducesAStablePush()
		{
			Vector3 a = AISeparation.Resolve(Vector3.one, new List<Vector3> { Vector3.one }, 1f, 1f);
			Vector3 b = AISeparation.Resolve(Vector3.one, new List<Vector3> { Vector3.one }, 1f, 1f);

			Assert.That(a.magnitude, Is.GreaterThan(0f));
			Assert.That(a, Is.EqualTo(b), "the same overlap must push the same way every tick");
		}

		[Test]
		public void OpposingNeighbours_CancelOut()
		{
			var pair = new List<Vector3> { new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 0f, 0f) };
			Assert.That(AISeparation.Resolve(Vector3.zero, pair, 1f, 1f).magnitude, Is.LessThan(1e-5f));
		}

		[Test]
		public void DisabledByZeroSpeedOrRadius()
		{
			var near = new List<Vector3> { new Vector3(0.1f, 0f, 0f) };
			Assert.That(AISeparation.Resolve(Vector3.zero, near, 1f, 0f), Is.EqualTo(Vector3.zero));
			Assert.That(AISeparation.Resolve(Vector3.zero, near, 0f, 1f), Is.EqualTo(Vector3.zero));
		}
	}
}
