using KinematicCharacterController;
using UnityEngine;

namespace FishMMO.TestHarness
{
	/// <summary>
	/// One tick of scripted rider input. Sampled once at prediction time and stored, exactly as a
	/// replicate input is: rollback replays the STORED stream, never re-decides.
	/// </summary>
	public struct SimRiderInput
	{
		/// <summary>World-space horizontal move direction, normalized or zero.</summary>
		public Vector3 Move;
		/// <summary>True on the tick a jump is requested.</summary>
		public bool Jump;
	}

	/// <summary>
	/// The minimal deterministic character brain the platform simulation rides on: gravity, ground
	/// move, jump, and the SAME platform-velocity injection path the networked stack uses
	/// (<see cref="KinematicCharacterMotor.SetPlatformVelocity"/> ahead of the motor phases).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Deliberately NOT <c>KCCController</c>: that class is a NetworkBehaviour wired into the
	/// character/replicate stack. What the platform scene must prove is the MOTOR-level contract —
	/// that a rider standing on a deterministic <c>KCCPlatform</c> stays on it through direction
	/// reversals, rollbacks and latency — and the motor, the platform, and the velocity-injection
	/// seam here are the real shipped pieces. Everything above that seam (input sourcing, ability
	/// gating, stamina) is out of scope for this scene and covered elsewhere.
	/// </para>
	/// <para>
	/// Everything here is a pure function of (motor state, the tick's input, platform velocity),
	/// which is what makes server and client twins bit-identical when fed the same stream — the
	/// property every divergence metric in the harness assumes.
	/// </para>
	/// </remarks>
	public sealed class SimRiderController : MonoBehaviour, ICharacterController
	{
		/// <summary>Ground move speed in units per second.</summary>
		public float MoveSpeed = 4f;

		/// <summary>Upward velocity applied on a jump tick.</summary>
		public float JumpSpeed = 8f;

		/// <summary>Gravity in units per second squared (negative = down).</summary>
		public float Gravity = -20f;

		/// <summary>The motor this controller drives.</summary>
		public KinematicCharacterMotor Motor { get; private set; }

		/// <summary>The input for the tick currently being simulated. Set by the harness per step.</summary>
		private SimRiderInput currentInput;

		/// <summary>Jump latched until consumed by the next velocity update.</summary>
		private bool jumpRequested;

		/// <summary>
		/// Binds the motor. Called by the harness after it has configured capsule and layers.
		/// </summary>
		public void Bind(KinematicCharacterMotor motor)
		{
			Motor = motor;
			Motor.CharacterController = this;
			/* The FishMMO motor routes every query through its scene-stacked PhysicsScene, which
			 * the game's loading path assigns per character (`Motor.SetPhysicsScene(...)`). An
			 * unassigned PhysicsScene queries NOTHING — the rider free-falls through the whole
			 * world — so the harness must do what the loader does. */
			Motor.SetPhysicsScene(gameObject.scene.GetPhysicsScene());
		}

		/// <summary>
		/// Simulates exactly one tick: injects the platform velocity through the shipped seam,
		/// then runs both motor phases — the same call order <c>KCCPlayer</c> uses.
		/// </summary>
		/// <param name="input">The tick's stored input.</param>
		/// <param name="platformVelocity">The velocity of the platform under the rider, or zero.</param>
		/// <param name="deltaTime">Fixed tick delta.</param>
		public void SimulateTick(in SimRiderInput input, Vector3 platformVelocity, float deltaTime)
		{
			currentInput = input;
			if (input.Jump)
			{
				jumpRequested = true;
			}

			Motor.SetPlatformVelocity(platformVelocity);
			Motor.UpdatePhase1(deltaTime);
			Motor.UpdatePhase2(deltaTime);

			// Mirror the motor's transient result onto the transform so ground probes and the
			// next tick's queries see where the character actually is.
			transform.SetPositionAndRotation(Motor.TransientPosition, Motor.TransientRotation);
		}

		/// <inheritdoc/>
		public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
		{
			// Yaw toward the move direction, deterministically; identity when idle.
			if (currentInput.Move.sqrMagnitude > 1e-6f)
			{
				currentRotation = Quaternion.LookRotation(currentInput.Move, Vector3.up);
			}
		}

		/// <inheritdoc/>
		public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
		{
			if (Motor.GroundingStatus.IsStableOnGround)
			{
				Vector3 planar = currentInput.Move * MoveSpeed;
				currentVelocity = new Vector3(planar.x, 0f, planar.z);

				if (jumpRequested)
				{
					jumpRequested = false;
					Motor.ForceUnground();
					currentVelocity.y = JumpSpeed;
				}
			}
			else
			{
				// Keep horizontal momentum in the air; integrate gravity.
				currentVelocity.y += Gravity * deltaTime;
			}
		}

		/// <inheritdoc/>
		public void BeforeCharacterUpdate(float deltaTime) { }

		/// <inheritdoc/>
		public void PostGroundingUpdate(float deltaTime) { }

		/// <inheritdoc/>
		public void AfterCharacterUpdate(float deltaTime) { }

		/// <inheritdoc/>
		public bool IsColliderValidForCollisions(Collider coll)
		{
			// World separation is done purely by the motor's CollidableLayers mask; nothing to add.
			return true;
		}

		/// <inheritdoc/>
		public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport) { }

		/// <inheritdoc/>
		public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport) { }

		/// <inheritdoc/>
		public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport hitStabilityReport) { }

		/// <inheritdoc/>
		public void OnDiscreteCollisionDetected(Collider hitCollider) { }
	}
}
