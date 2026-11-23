using FishMMO.Shared;
using FishMMO.Shared.Core;
using KinematicCharacterController;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A real motor carrying a rider (issue #109): a bare KinematicCharacterMotor and KCCController (as the world test bed's
	/// walker drives one, no server and no character, so stamina is unmetered), stepped tick by tick on a ground plane, with
	/// a stub mount in place of the Mounts module's controller. Each case rides against the same motor on foot.
	/// </summary>
	[TestFixture]
	public class MountMotorTests
	{
		private const float Dt = 1f / 30f;
		private const float CapsuleHeight = 1.6f;

		private GameObject ground;
		private GameObject ramp;
		private GameObject host;
		private KinematicCharacterMotor motor;
		private KCCController controller;
		private StubMount mount;

		private sealed class StubMount : ICharacterMount
		{
			public MountMotion Motion;
			public bool Riding;
			public int Dismounts;
			public bool IsMounted => Riding;
			public bool TryGetMotion(out MountMotion motion)
			{
				motion = Riding ? Motion : default;
				return Riding;
			}
			public void Dismount()
			{
				Riding = false;
				Dismounts++;
			}
			public ICharacter Character => null;
			public bool Initialized => true;
			public void InitializeOnce(ICharacter character) { }
			public void OnStartCharacter() { }
			public void OnStopCharacter() { }
		}

		[SetUp]
		public void SetUp()
		{
			ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
			ground.name = "MountMotorTests Ground";
			ground.transform.position = new Vector3(0f, -0.5f, 0f);
			ground.transform.localScale = new Vector3(400f, 1f, 400f);
			mount = new StubMount();
		}

		[TearDown]
		public void TearDown()
		{
			foreach (GameObject go in new[] { host, ground, ramp })
			{
				if (go != null)
				{
					Object.DestroyImmediate(go);
				}
			}
		}

		/// <summary>A rider on the ground at <paramref name="at"/>, the motor set up as a playable character's.</summary>
		private void Spawn(Vector3 at)
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
			host = new GameObject("MountMotorTests Rider");
			host.SetActive(false);
			int player = LayerMask.NameToLayer("Player");
			if (player >= 0)
			{
				host.layer = player;
			}
			host.AddComponent<CapsuleCollider>();
			Rigidbody body = host.AddComponent<Rigidbody>();
			body.isKinematic = true;
			body.useGravity = false;
			motor = host.AddComponent<KinematicCharacterMotor>();
			motor.MaxStableSlopeAngle = 60f;
			motor.MaxStepHeight = 0.25f;
			motor.MinRequiredStepDepth = 0.1f;
			motor.CollidableLayers = Physics.DefaultRaycastLayers & ~(player >= 0 ? 1 << player : 0);
			motor.StableGroundLayers = motor.CollidableLayers;
			controller = host.AddComponent<KCCController>();
			controller.Motor = motor;
			controller.StableMovementSharpness = 10000f;
			controller.OrientationMethod = OrientationMethod.TowardsMovement;
			controller.MaxAirMoveSpeed = 4f;
			controller.AirAccelerationSpeed = 2.5f;
			controller.Drag = 0.1f;
			controller.JumpPreGroundingGraceTime = 0.1f;
			controller.JumpPostGroundingGraceTime = 0.1f;
			controller.FullCapsuleHeight = CapsuleHeight;
			controller.CapsuleBaseOffset = CapsuleHeight * 0.5f;
			controller.MountOverride = mount;
			motor.CharacterController = controller;
			host.SetActive(true);
			// EditMode runs no Awake: the motor caches its transform and capsule there.
			typeof(KinematicCharacterMotor).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.Invoke(motor, null);
			motor.SetCapsuleDimensions(0.4f, CapsuleHeight, CapsuleHeight * 0.5f);
			motor.SetPhysicsScene(host.scene.GetPhysicsScene());
			motor.SetPosition(at);
			host.transform.position = at;
			Physics.SyncTransforms();
			Step(10, 0f, 0);
		}

		private void Step(int ticks, float forward, int flags)
		{
			for (int i = 0; i < ticks; i++)
			{
				int f = flags;
				f.EnableBit(KCCMoveFlags.IsActualData);
				var input = new KCCInputReplicateData(forward, 0f, f, motor.TransientPosition + Vector3.up * CapsuleHeight, Quaternion.identity);
				controller.SetInputs(ref input);
				motor.UpdatePhase1(Dt);
				motor.UpdatePhase2(Dt);
				host.transform.SetPositionAndRotation(motor.TransientPosition, motor.TransientRotation);
				Physics.SyncTransforms();
			}
		}

		private static int Flag(KCCMoveFlags flag)
		{
			int f = 0;
			f.EnableBit(flag);
			return f;
		}

		private float WalkDistance(bool riding, MountMotion motion)
		{
			mount.Motion = motion;
			mount.Riding = riding;
			Spawn(Vector3.zero);
			Vector3 start = motor.TransientPosition;
			Step(60, 1f, 0);
			Vector3 end = motor.TransientPosition;
			return new Vector2(end.x - start.x, end.z - start.z).magnitude;
		}

		/// <summary>The highest the feet rise over the ground after one jump.</summary>
		private float JumpApex(bool riding, MountMotion motion, out float travelled)
		{
			mount.Motion = motion;
			mount.Riding = riding;
			Spawn(Vector3.zero);
			float floor = motor.TransientPosition.y;
			Vector3 start = motor.TransientPosition;
			Step(1, 1f, Flag(KCCMoveFlags.Jump));
			float apex = floor;
			for (int i = 0; i < 120; i++)
			{
				Step(1, 1f, 0);
				apex = Mathf.Max(apex, motor.TransientPosition.y);
				if (i > 5 && motor.GroundingStatus.IsStableOnGround)
				{
					break;
				}
			}
			travelled = new Vector2(motor.TransientPosition.x - start.x, motor.TransientPosition.z - start.z).magnitude;
			return apex - floor;
		}

		[Test]
		public void Horse_CoversItsMultipleOfTheGround()
		{
			float onFoot = WalkDistance(false, default);
			float horse = WalkDistance(true, new MountMotion { GroundSpeedMultiplier = 1.7f });
			Assert.That(onFoot, Is.GreaterThan(5f), "the rider walked at all");
			Assert.That(horse / onFoot, Is.EqualTo(1.7f).Within(0.08f));
		}

		[Test]
		public void Frog_LeapsHigherAndFarther()
		{
			float footApex = JumpApex(false, default, out float footTravel);
			float frogApex = JumpApex(true, new MountMotion { GroundSpeedMultiplier = 1.2f, JumpHeightMultiplier = 2.5f, LeapSpeed = 6f }, out float frogTravel);
			Assert.That(footApex, Is.GreaterThan(0.5f), "the rider jumped at all");
			Assert.That(frogApex / footApex, Is.EqualTo(2.5f).Within(0.35f), "2.5x as high");
			Assert.That(frogTravel, Is.GreaterThan(footTravel * 2f), "and far: the leap's forward speed");
		}

		[Test]
		public void Dragon_TakesOff_Climbs_Holds_AndLands()
		{
			mount.Motion = new MountMotion { GroundSpeedMultiplier = 1.4f, Flies = true, FlySpeed = 15f, FlySprintMultiplier = 2f, FlightStaminaPerSecond = 3f, GlideSinkSpeed = 3f, GlideSpeedShare = 0.6f };
			mount.Riding = true;
			Spawn(Vector3.zero);
			Assert.That(controller.IsMountFlying, Is.False, "standing, it walks");

			Step(1, 0f, Flag(KCCMoveFlags.Jump));
			Step(60, 0f, Flag(KCCMoveFlags.Ascend));
			Assert.That(controller.IsMountFlying, Is.True);
			float climbed = motor.TransientPosition.y;
			Assert.That(climbed, Is.GreaterThan(10f), "held Jump climbs far past any jump");

			// Let go: the climb eases out (FlySharpness, a tenth of a second), then it holds its height, no gravity aloft.
			Step(15, 0f, 0);
			float held = motor.TransientPosition.y;
			Assert.That(held - climbed, Is.LessThan(2.5f), "the climb eases out, it does not carry on");
			Step(45, 0f, 0);
			Assert.That(Mathf.Abs(motor.TransientPosition.y - held), Is.LessThan(0.1f), "and then it hangs there");

			for (int i = 0; i < 300 && !motor.GroundingStatus.IsStableOnGround; i++)
			{
				Step(1, 0f, Flag(KCCMoveFlags.Crouch));
			}
			Assert.That(motor.GroundingStatus.IsStableOnGround, Is.True, "Crouch brings it down onto the ground");
			Step(2, 0f, 0);
			Assert.That(controller.IsMountFlying, Is.False, "landed, it walks again");
		}

		[Test]
		public void OnFoot_NoMountFlies()
		{
			mount.Riding = false;
			Spawn(Vector3.zero);
			Step(1, 0f, Flag(KCCMoveFlags.Jump));
			Step(60, 0f, Flag(KCCMoveFlags.Ascend));
			Assert.That(motor.TransientPosition.y, Is.LessThan(3f), "a jump, and back down");
		}

		[Test]
		public void Llama_WalksUpASlopeTooSteepOnFoot()
		{
			const float degrees = 68f;
			ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
			ramp.name = "MountMotorTests Ramp";
			ramp.transform.localScale = new Vector3(8f, 0.4f, 30f);
			ramp.transform.rotation = Quaternion.Euler(-degrees, 0f, 0f);
			float rad = degrees * Mathf.Deg2Rad;
			ramp.transform.position = new Vector3(0f, Mathf.Sin(rad) * 15f, 3f + Mathf.Cos(rad) * 15f);
			Physics.SyncTransforms();

			float onFoot = Climb(false);
			float llama = Climb(true);
			Assert.That(onFoot, Is.LessThan(1f), $"{degrees}° is a wall on foot (60° is the most)");
			Assert.That(llama, Is.GreaterThan(2f), "a llama (75°) walks up it");

			float Climb(bool riding)
			{
				mount.Motion = new MountMotion { GroundSpeedMultiplier = 1.4f, MaxSlopeDegrees = 75f };
				mount.Riding = riding;
				Spawn(Vector3.zero);
				Step(150, 1f, 0);
				return motor.TransientPosition.y;
			}
		}

		[Test]
		public void SlopeLent_IsGivenBack_WhenTheRiderGetsOff()
		{
			mount.Motion = new MountMotion { MaxSlopeDegrees = 75f };
			mount.Riding = true;
			Spawn(Vector3.zero);
			Assert.That(motor.MaxStableSlopeAngle, Is.EqualTo(75f));
			mount.Riding = false;
			Step(1, 0f, 0);
			Assert.That(motor.MaxStableSlopeAngle, Is.EqualTo(60f), "the rider's own again");
		}
	}
}
