using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The motor's mount maths (issue #109): a flying mount's steering, its glide when the stamina is gone, and a mount's
	/// jump. Pure: the motor's own step is exercised in play.
	/// </summary>
	[TestFixture]
	public class MountMotionTests
	{
		private static MountMotion Dragon => new MountMotion
		{
			GroundSpeedMultiplier = 1.4f,
			Flies = true,
			FlySpeed = 15f,
			FlySprintMultiplier = 2f,
			FlightStaminaPerSecond = 3f,
			GlideSinkSpeed = 3f,
			GlideSpeedShare = 0.6f,
		};

		[Test]
		public void Flight_GoesTheWayTheCameraLooks_AtFlySpeed()
		{
			Vector3 look = new Vector3(0f, 0.5f, 1f).normalized;
			Vector3 target = KCCController.MountFlightTarget(Dragon, look, false, false, false, powered: true);
			Assert.That(target.magnitude, Is.EqualTo(15f).Within(0.001f));
			Assert.That(Vector3.Angle(target, look), Is.LessThan(0.01f));
		}

		[Test]
		public void Flight_HeldJumpClimbs_CrouchDescends()
		{
			Vector3 up = KCCController.MountFlightTarget(Dragon, Vector3.zero, true, false, false, true);
			Vector3 down = KCCController.MountFlightTarget(Dragon, Vector3.zero, false, true, false, true);
			Assert.That(up.y, Is.GreaterThan(10f));
			Assert.That(down.y, Is.LessThan(-10f));
		}

		[Test]
		public void Flight_Sprint_IsFasterButCapped()
		{
			Vector3 sprint = KCCController.MountFlightTarget(Dragon, Vector3.forward, false, false, true, true);
			Assert.That(sprint.magnitude, Is.GreaterThan(15f));
			Assert.That(sprint.magnitude, Is.LessThanOrEqualTo(Constants.Character.SprintSpeed * 3f + 0.001f),
				"the same exploit ceiling the ground has");
		}

		/// <summary>Flight costs stamina — spent, the mount glides down and cannot climb.</summary>
		[Test]
		public void OutOfStamina_Glides_SinkingAndNeverClimbing()
		{
			Vector3 glide = KCCController.MountFlightTarget(Dragon, new Vector3(0f, 0.8f, 0.6f), true, false, true, powered: false);
			Assert.That(glide.y, Is.EqualTo(-3f).Within(0.001f), "it sinks, held Jump or not");
			Vector3 level = new Vector3(glide.x, 0f, glide.z);
			Assert.That(level.magnitude, Is.LessThanOrEqualTo(15f * 0.6f + 0.001f), "a share of its speed, sprint or not");
			Assert.That(level.z, Is.GreaterThan(0f), "still steered forward");
		}

		[Test]
		public void JumpHeightMultiplier_ScalesTheTakeOffSpeedByItsRoot()
		{
			var frog = new MountMotion { JumpHeightMultiplier = 2.5f };
			Assert.That(frog.JumpSpeedMultiplier, Is.EqualTo(Mathf.Sqrt(2.5f)).Within(0.0001f), "height goes with speed squared");
			Assert.That(new MountMotion().JumpSpeedMultiplier, Is.EqualTo(1f), "unset is an ordinary jump");
		}
	}
}
