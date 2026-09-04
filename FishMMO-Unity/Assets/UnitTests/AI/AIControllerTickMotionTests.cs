using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins the tick-driven NavMeshAgent stepping introduced for issue #220: the transform must
	/// advance by exactly one tick of velocity per tick, whatever the server frame rate, and the
	/// heading must follow the velocity on the same clock without pitching or spinning on noise.
	/// </summary>
	[TestFixture]
	public class AIControllerTickMotionTests
	{
		private const float TickDelta = 1f / 30f;

		[Test]
		public void TickStep_IsVelocityTimesTickDelta_IndependentOfFrameCount()
		{
			Vector3 velocity = new Vector3(0f, 0f, Constants.Character.RunSpeed);

			Vector3 step = AIController.ResolveTickStep(velocity, TickDelta);

			// One tick, one slice — never one, two or three frames' worth.
			Assert.That(step.z, Is.EqualTo(Constants.Character.RunSpeed * TickDelta).Within(1e-6f));
			Assert.That(step.x, Is.EqualTo(0f));
			Assert.That(step.y, Is.EqualTo(0f));
		}

		[Test]
		public void TickStep_AtWalkSpeed_IsAboveTheWireGrid()
		{
			// 1 cm wire resolution (positionMultiplier 100). A walking tick must span several cells,
			// or quantisation turns constant speed into alternating stalls and hops.
			Vector3 step = AIController.ResolveTickStep(new Vector3(Constants.Character.WalkSpeed, 0f, 0f), TickDelta);
			Assert.That(step.magnitude, Is.GreaterThan(0.04f));
		}

		[Test]
		public void TickHeading_TurnsTowardVelocity_BoundedByAngularSpeed()
		{
			Quaternion facingForward = Quaternion.identity;
			Vector3 movingRight = new Vector3(2f, 0f, 0f);

			bool changed = AIController.ResolveTickHeading(facingForward, movingRight, 120f, TickDelta, out Quaternion result);

			Assert.That(changed, Is.True);
			float turned = Quaternion.Angle(facingForward, result);
			Assert.That(turned, Is.EqualTo(120f * TickDelta).Within(0.01f), "turn must be capped at angularSpeed × tickDelta");
		}

		[Test]
		public void TickHeading_ReachesVelocityDirection_WhenWithinOneTicksTurn()
		{
			Quaternion facing = Quaternion.Euler(0f, 2f, 0f);
			Vector3 velocity = Vector3.forward * 3f;

			AIController.ResolveTickHeading(facing, velocity, 120f, TickDelta, out Quaternion result);

			Assert.That(Quaternion.Angle(result, Quaternion.identity), Is.LessThan(0.01f));
		}

		[Test]
		public void TickHeading_IgnoresVerticalVelocity()
		{
			// A slope must not pitch the character.
			Vector3 upSlope = new Vector3(0f, 1.5f, 1.5f);

			AIController.ResolveTickHeading(Quaternion.identity, upSlope, 720f, TickDelta, out Quaternion result);

			Vector3 euler = result.eulerAngles;
			Assert.That(Mathf.Abs(Mathf.DeltaAngle(0f, euler.x)), Is.LessThan(0.01f));
			Assert.That(Mathf.Abs(Mathf.DeltaAngle(0f, euler.z)), Is.LessThan(0.01f));
		}

		[Test]
		public void TickHeading_LeavesHeadingAlone_BelowTheSpeedThreshold()
		{
			Quaternion facing = Quaternion.Euler(0f, 90f, 0f);
			Vector3 crawl = new Vector3(0f, 0f, AIController.HEADING_SPEED_THRESHOLD * 0.5f);

			bool changed = AIController.ResolveTickHeading(facing, crawl, 120f, TickDelta, out Quaternion result);

			Assert.That(changed, Is.False);
			Assert.That(result, Is.EqualTo(facing));
		}

		[Test]
		public void TickHeading_NoTurnAtZeroAngularSpeed()
		{
			bool changed = AIController.ResolveTickHeading(Quaternion.identity, Vector3.right * 3f, 0f, TickDelta, out Quaternion result);

			Assert.That(changed, Is.False);
			Assert.That(Quaternion.Angle(result, Quaternion.identity), Is.LessThan(0.001f));
		}
	}
}
