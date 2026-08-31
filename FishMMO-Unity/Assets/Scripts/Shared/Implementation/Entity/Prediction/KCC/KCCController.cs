using System.Collections.Generic;
using UnityEngine;
using KinematicCharacterController;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Character movement states for the KCC controller state machine.
	/// </summary>
	public enum KCCCharacterState
	{
		Default,
	}

	/// <summary>
	/// Determines how the character faces based on input or camera direction.
	/// </summary>
	public enum OrientationMethod
	{
		TowardsCamera,
		TowardsMovement,
	}

	/// <summary>
	/// Input data supplied by an AI controller for character movement and facing.
	/// </summary>
	public struct AICharacterInputs
	{
		/// <summary>
		/// Movement direction vector for AI-controlled characters.
		/// </summary>
		public Vector3 MoveVector;
		/// <summary>
		/// Look/facing direction vector for AI-controlled characters.
		/// </summary>
		public Vector3 LookVector;
	}

	/// <summary>
	/// Additional orientation modes that adjust the character up vector.
	/// </summary>
	public enum BonusOrientationMethod
	{
		None,
		TowardsGravity,
		TowardsGroundSlopeAndGravity,
	}

	/// <summary>
	/// Kinematic Character Controller wrapper that implements <see cref="ICharacterController"/>.
	/// Handles movement, jumping, crouching, sprinting, and state transitions.
	/// </summary>
	public class KCCController : MonoBehaviour, ICharacterController
	{
		/// <summary>
		/// The player character this controller belongs to.
		/// </summary>
		public IPlayerCharacter Character;
		/// <summary>
		/// The kinematic character motor driving movement and collision.
		/// </summary>
		public KinematicCharacterMotor Motor;

		[Header("Stable Movement")]
		/// <summary>
		/// Sharpness for stable ground movement acceleration (higher = snappier).
		/// </summary>
		public float StableMovementSharpness = 20f;
		/// <summary>
		/// Sharpness for character rotation smoothing.
		/// </summary>
		public float OrientationSharpness = 10f;
		/// <summary>
		/// Method used to determine character facing direction.
		/// </summary>
		public OrientationMethod OrientationMethod = OrientationMethod.TowardsCamera;

		[Header("Air Movement")]
		/// <summary>
		/// Maximum horizontal speed while airborne.
		/// </summary>
		public float MaxAirMoveSpeed = 6f;
		/// <summary>
		/// Acceleration applied to air movement.
		/// </summary>
		public float AirAccelerationSpeed = 0f;
		/// <summary>
		/// Air drag coefficient applied to velocity.
		/// </summary>
		public float Drag = 0.1f;

		[Header("Jumping")]
		/// <summary>
		/// If true, jumping is allowed while sliding on unstable ground.
		/// </summary>
		public bool AllowJumpingWhenSliding = false;
		/// <summary>
		/// Forward speed added to velocity when jumping.
		/// </summary>
		public float JumpScalableForwardSpeed = 0f;
		/// <summary>
		/// Grace period before landing during which a jump request is still accepted.
		/// </summary>
		public float JumpPreGroundingGraceTime = 0f;
		/// <summary>
		/// Grace period after leaving ground during which a jump is still allowed.
		/// </summary>
		public float JumpPostGroundingGraceTime = 0f;

		[Header("Misc")]
		/// <summary>
		/// Colliders to ignore for character collisions.
		/// </summary>
		public List<Collider> IgnoredColliders = new List<Collider>();
		/// <summary>
		/// Additional orientation method for adjusting the character up vector.
		/// </summary>
		public BonusOrientationMethod BonusOrientationMethod = BonusOrientationMethod.None;
		/// <summary>
		/// Sharpness for bonus orientation smoothing.
		/// </summary>
		public float BonusOrientationSharpness = 10f;
		/// <summary>
		/// Root transform of the character mesh for visual scaling (e.g., crouch).
		/// </summary>
		public Transform MeshRoot;
		/// <summary>
		/// Transform that the camera follows and orbits around.
		/// </summary>
		public Transform CameraFollowPoint;
		/// <summary>
		/// Capsule height when crouching.
		/// </summary>
		public float CrouchedCapsuleHeight = 0.5f;
		/// <summary>
		/// Capsule height when standing.
		/// </summary>
		public float FullCapsuleHeight = 2f;
		/// <summary>
		/// Base offset of the capsule from the character pivot.
		/// </summary>
		public float CapsuleBaseOffset = 1f;
		/// <summary>
		/// Template for the character attribute that modifies movement speed.
		/// </summary>
		public CharacterAttributeTemplate MoveSpeedTemplate;
		/// <summary>
		/// Template for the character attribute that modifies sprint speed.
		/// </summary>
		public CharacterAttributeTemplate SprintSpeedTemplate;
		/// <summary>
		/// Template for the character attribute that modifies jump speed.
		/// </summary>
		public CharacterAttributeTemplate JumpSpeedTemplate;
		/// <summary>
		/// Template for the character attribute that modifies swim speed.
		/// </summary>
		public CharacterAttributeTemplate SwimSpeedTemplate;
		/// <summary>
		/// Template for the character attribute that modifies fast fall speed.
		/// </summary>
		public CharacterAttributeTemplate FastFallSpeedTemplate;
		/// <summary>
		/// Template for the character attribute that modifies gravity.
		/// </summary>
		public CharacterAttributeTemplate GravityTemplate;

		/// <summary>
		/// The current state of the character state machine.
		/// </summary>
		public KCCCharacterState CurrentCharacterState { get; private set; }

		private Collider[] probedColliders = new Collider[8];

		/// <summary>
		/// Cached ability controller reference to avoid per-tick dictionary lookups.
		/// Lazily resolved when <see cref="Character"/> is first set.
		/// </summary>
		private IAbilityController cachedAbilityController;

		/// <summary>
		/// Cached attribute controller reference to avoid per-tick dictionary lookups.
		/// Lazily resolved when <see cref="Character"/> is first set.
		/// </summary>
		private ICharacterAttributeController cachedAttributeController;

		/// <summary>
		/// Cached animation controller reference to avoid per-tick dictionary lookups.
		/// Lazily resolved when <see cref="Character"/> is first set.
		/// </summary>
		private ICharacterAnimationController cachedAnimationController;

		/// <summary>
		/// Tracks which <see cref="Character"/> reference the cached components belong to.
		/// Re-caches when the reference changes (e.g., NPC possession or respawn).
		/// </summary>
		private IPlayerCharacter lastCachedCharacter;

		// Current frame input state
		private Vector3 moveInputVector;
		private Vector3 lookInputVector;
		private bool crouchInputDown = false;
		private bool jumpRequested = false;
		private bool sprintInputDown = false;

		// Multi Frame State, this needs to be synchronized
		private float timeSinceJumpRequested = float.MaxValue;
		private float timeSinceLastAbleToJump = 0f;
		private bool isCrouching = false;

		/// <summary>
		/// Position of the virtual camera used for input-relative movement calculations.
		/// </summary>
		public Vector3 VirtualCameraPosition { get; private set; }
		/// <summary>
		/// Rotation of the virtual camera used for input-relative movement calculations.
		/// </summary>
		public Quaternion VirtualCameraRotation { get; private set; }
		/* IsJumping was REMOVED, not left unused.
		 *
		 * It was a public bool set in HandleJumping and cleared in OnLanded, with no reader anywhere
		 * in the project — and it was also predicted state written from inside the replicate body
		 * that no field of CharacterReconcileData restored. That combination is the trap: harmless
		 * only for as long as nothing reads it, and a genuine divergence the moment something does,
		 * because a replay re-runs HandleJumping while nothing ever puts the flag back.
		 *
		 * Anything that needs "is this character mid-jump" should derive it from state that IS
		 * reconciled — Motor.GroundingStatus and BaseVelocity are both in KinematicCharacterMotorState
		 * — or add a field to the reconcile deliberately. Do not reintroduce a bare flag here. */

		/// <summary>
		/// Tracks whether the motor has completed its first ground probe. When it is false,
		/// <c>BeforeCharacterUpdate</c> forces <c>LastMovementIterationFoundAnyGround</c> true so the
		/// motor selects the full-radius probe distance instead of the 0.005 minimum, which stops a
		/// freshly spawned or teleported character reading as airborne for 1-3 frames.
		/// <para>
		/// <b>Only <see cref="ResetGroundProbe"/> clears this, and only a teleport or a fresh spawn
		/// calls it.</b> <see cref="ApplyState"/> deliberately does NOT — see the comment there. It
		/// used to, on every reconcile, which made the next tick force
		/// <c>LastMovementIterationFoundAnyGround</c> true and overwrite the value the reconcile had
		/// just restored from the server, so an airborne owner could snap to ground where the server
		/// did not, be corrected, and repeat. That was an open item from the 2026-08-28 audit and it
		/// is closed.
		/// </para>
		/// </summary>
		private bool hasDoneInitialGroundProbe = false;

		/// <summary>
		/// Initializes the character state to default.
		/// </summary>
		private void Awake()
		{
			// Handle initial state
			TransitionToState(KCCCharacterState.Default);
		}

		/// <summary>
		/// Resets input vectors when the component is enabled.
		/// </summary>
		private void OnEnable()
		{
			moveInputVector = Vector3.zero;
			lookInputVector = Vector3.zero;
		}

		/// <summary>
		/// Clears cached character references when disabled.
		/// </summary>
		private void OnDisable()
		{
			ClearCachedCharacterReferences();
		}

		private void ClearCachedCharacterReferences()
		{
			cachedAbilityController = null;
			cachedAttributeController = null;
			cachedAnimationController = null;
			lastCachedCharacter = null;
		}

		/// <summary>
		/// Lazily resolves and caches component references from <see cref="Character"/>.
		/// Re-caches if the <see cref="Character"/> reference changes.
		/// </summary>
		private void EnsureCached()
		{
			if (Character == lastCachedCharacter && lastCachedCharacter != null) return;
			lastCachedCharacter = Character;
			if (Character != null)
			{
				Character.TryGet(out cachedAbilityController);
				Character.TryGet(out cachedAttributeController);
				Character.TryGet(out cachedAnimationController);
			}
			else
			{
				cachedAbilityController = null;
				cachedAttributeController = null;
				cachedAnimationController = null;
			}
		}

		/// <summary>
		/// Handles movement state transitions and enter/exit callbacks
		/// </summary>
		public void TransitionToState(KCCCharacterState newState)
		{
			KCCCharacterState tmpInitialState = CurrentCharacterState;
			OnStateExit(tmpInitialState, newState);
			CurrentCharacterState = newState;
			OnStateEnter(newState, tmpInitialState);
		}

		/// <summary>
		/// Event when entering a state
		/// </summary>
		public void OnStateEnter(KCCCharacterState state, KCCCharacterState fromState)
		{
			switch (state)
			{
				case KCCCharacterState.Default:
					{
						break;
					}
			}
		}

		/// <summary>
		/// Event when exiting a state
		/// </summary>
		public void OnStateExit(KCCCharacterState state, KCCCharacterState toState)
		{
			switch (state)
			{
				case KCCCharacterState.Default:
					{
						break;
					}
			}
		}

		/// <summary>
		/// Applies a saved motor state to restore the character pose (used during reconcile).
		/// </summary>
		/// <param name="state">The motor state to apply.</param>
		public void ApplyState(KinematicCharacterMotorState state)
		{
			/* hasDoneInitialGroundProbe is deliberately NOT reset here.
			 *
			 * It used to be, on the grounds that this is called for a teleport as well as a
			 * reconcile — but a reconcile is the overwhelmingly common case, and clearing the flag
			 * makes BeforeCharacterUpdate force LastMovementIterationFoundAnyGround true on the next
			 * tick, overwriting the value this very method just restored from the server. The first
			 * replayed tick then chose the full-radius ground probe where the server had used the
			 * 0.005 minimum, so an airborne owner could snap to ground where the server did not, be
			 * corrected, and repeat. Teleports go through ResetGroundProbe() instead. */

			// Take any state needed for the controller here
			bool wasCrouching = isCrouching;
			isCrouching = state.IsCrouching;
			jumpRequested = state.JumpRequested;
			timeSinceLastAbleToJump = state.TimeSinceLastAbleToJump;
			timeSinceJumpRequested = state.TimeSinceJumpRequested;

			/* Restore the collider to match the restored crouch state.
			 *
			 * Only the two input TRANSITIONS in AfterCharacterUpdate resize the capsule, so a
			 * reconcile that flipped isCrouching without a matching transition left the collider at
			 * the wrong height permanently — and self-sustainingly, because with crouchInputDown now
			 * equal to isCrouching neither transition can ever fire again. The owner then simulated
			 * collisions, step handling and ground probing against a capsule the server did not
			 * have, and the aim origin moved with it (CharacterAimOrigin reads Capsule.height). */
			if (wasCrouching != isCrouching)
			{
				ApplyCapsuleDimensions(isCrouching);
			}

			Motor.ApplyState(state);
		}

		/// <summary>
		/// Sizes the motor capsule for the crouched or standing pose.
		/// </summary>
		/// <remarks>
		/// Shared by the crouch input transitions and by <see cref="ApplyState"/>, so a reconciled
		/// crouch flag and the collider can never disagree.
		/// </remarks>
		/// <param name="crouched">True for the crouched capsule, false for the full one.</param>
		private void ApplyCapsuleDimensions(bool crouched)
		{
			if (crouched)
			{
				Motor.SetCapsuleDimensions(Motor.Capsule.radius, CrouchedCapsuleHeight,
					CapsuleBaseOffset / (FullCapsuleHeight / CrouchedCapsuleHeight));
			}
			else
			{
				Motor.SetCapsuleDimensions(Motor.Capsule.radius, FullCapsuleHeight, CapsuleBaseOffset);
			}
		}

		/// <summary>
		/// Forces a full-radius ground probe on the next motor update, so a character that has just
		/// been placed does not read as airborne for a frame or three.
		/// </summary>
		/// <remarks>
		/// Call this for a teleport or a fresh spawn — NOT for a reconcile, which restores
		/// <c>LastMovementIterationFoundAnyGround</c> authoritatively and must not have it forced.
		/// </remarks>
		public void ResetGroundProbe()
		{
			hasDoneInitialGroundProbe = false;
		}

		/// <summary>
		/// Captures the current motor and controller state for reconciliation.
		/// </summary>
		/// <returns>A snapshot of the current kinematic state.</returns>
		public KinematicCharacterMotorState GetState()
		{
			KinematicCharacterMotorState baseState = Motor.GetState();

			/* Canonicalise the ungrounded grounding normals to zero, at the PRODUCER.
			 *
			 * The motor does not leave them zero: every UpdatePhase1 assigns a fresh report and
			 * immediately seeds it with the character's up vector
			 * (KinematicCharacterMotor: GroundingStatus.GroundNormal = _characterUp), and ProbeGround
			 * only overwrites that when it actually finds ground. So an airborne snapshot carried
			 * (0,1,0) as its GroundNormal while Inner and Outer really were zero.
			 *
			 * The wire format omits all three normals when FoundAnyGround is false and the reader
			 * reconstructs them as zero, which is correct for a value nothing reads while airborne —
			 * but it left the two peers holding DIFFERENT baselines for the delta chain. The next
			 * landing then encoded a difference against (0,1,0) that the reader added onto (0,0,0),
			 * and the two vectors sit on opposite sides of the packed encoding (0xFFFF0000 against
			 * the zero-vector fallback 0x80000000), so the decoded normal came out roughly a quarter
			 * turn wrong until the next absolute snapshot — which, having the same asymmetry,
			 * re-established the mismatch rather than repairing it.
			 *
			 * Zeroing here makes the value the writer diffs against the value the reader holds, which
			 * is the same rule the aim direction follows. It cannot disturb the local simulation: the
			 * motor overwrites GroundingStatus wholesale at the top of its next update, and nothing
			 * reads LastGroundingStatus.GroundNormal. */
			if (!baseState.GroundingStatus.FoundAnyGround)
			{
				baseState.GroundingStatus.GroundNormal = Vector3.zero;
				baseState.GroundingStatus.InnerGroundNormal = Vector3.zero;
				baseState.GroundingStatus.OuterGroundNormal = Vector3.zero;
			}

			// Apply state from controller here.
			baseState.IsCrouching = isCrouching;
			baseState.JumpRequested = jumpRequested;
			baseState.TimeSinceLastAbleToJump = timeSinceLastAbleToJump;
			baseState.TimeSinceJumpRequested = timeSinceJumpRequested;

			return baseState;
		}

		/// <summary>
		/// Applies one tick of replicated input. Called from <c>KCCPlayer.OnReplicate</c>, once per
		/// replicated or replayed tick — not per frame, despite the upstream sample's name for it.
		/// </summary>
		public void SetInputs(ref KCCInputReplicateData inputs)
		{
			// Validate camera rotation to prevent clients from sending arbitrary
			// rotations to manipulate movement direction. A hacked client could
			// otherwise move in any direction regardless of camera orientation.
			float qDot = Quaternion.Dot(inputs.CameraRotation, inputs.CameraRotation);
			if (qDot < 0.99f || qDot > 1.01f ||
				float.IsNaN(inputs.CameraRotation.w) || float.IsInfinity(inputs.CameraRotation.w) ||
				float.IsNaN(inputs.CameraRotation.x) || float.IsInfinity(inputs.CameraRotation.x) ||
				float.IsNaN(inputs.CameraRotation.y) || float.IsInfinity(inputs.CameraRotation.y) ||
				float.IsNaN(inputs.CameraRotation.z) || float.IsInfinity(inputs.CameraRotation.z))
			{
				inputs.CameraRotation = Quaternion.identity;
			}

			VirtualCameraPosition = inputs.CameraPosition;
			VirtualCameraRotation = inputs.CameraRotation;

			// Clamp input

			// Sanitize movement axes 342200224 NaN/Infinity bypasses Vector3.ClampMagnitude
			// and propagates into the KCC motor, corrupting collision and reconcile state.
			if (float.IsNaN(inputs.MoveAxisForward) || float.IsInfinity(inputs.MoveAxisForward))
				inputs.MoveAxisForward = 0f;
			if (float.IsNaN(inputs.MoveAxisRight) || float.IsInfinity(inputs.MoveAxisRight))
				inputs.MoveAxisRight = 0f;
			Vector3 clampedInput = Vector3.ClampMagnitude(new Vector3(inputs.MoveAxisRight, 0f, inputs.MoveAxisForward), 1f);

			// Calculate camera direction and rotation on the character plane
			Vector3 cameraPlanarDirection = Vector3.ProjectOnPlane(inputs.CameraRotation * Vector3.forward, Motor.CharacterUp).normalized;
			if (cameraPlanarDirection.sqrMagnitude == 0f)
			{
				cameraPlanarDirection = Vector3.ProjectOnPlane(inputs.CameraRotation * Vector3.up, Motor.CharacterUp).normalized;
			}
			Quaternion cameraPlanarRotation = Quaternion.LookRotation(cameraPlanarDirection, Motor.CharacterUp);

			switch (CurrentCharacterState)
			{
				case KCCCharacterState.Default:
					{
						// Move and look inputs — write to the field, not a local
						moveInputVector = cameraPlanarRotation * clampedInput;

						switch (OrientationMethod)
						{
							case OrientationMethod.TowardsCamera:
								lookInputVector = cameraPlanarDirection;
								break;
							case OrientationMethod.TowardsMovement:
								lookInputVector = moveInputVector.normalized;
								break;
						}

						// Jumping input
						if (inputs.MoveFlags.IsFlagged(KCCMoveFlags.Jump))
						{
							timeSinceJumpRequested = 0f;
							jumpRequested = true;
						}

						// Crouching input
						crouchInputDown = inputs.MoveFlags.IsFlagged(KCCMoveFlags.Crouch);

						// Sprinting input
						sprintInputDown = inputs.MoveFlags.IsFlagged(KCCMoveFlags.Sprint);

						break;
					}
			}
		}

		/// <summary>
		/// This is called every frame by the AI script in order to tell the character what its inputs are
		/// </summary>
		public void SetInputs(ref AICharacterInputs inputs)
		{
			moveInputVector = inputs.MoveVector;
			lookInputVector = inputs.LookVector;
		}

		/// <summary>
		/// (Called by KinematicCharacterMotor during its update cycle)
		/// This is called before the character begins its movement update
		/// </summary>
		public void BeforeCharacterUpdate(float deltaTime)
		{
			// On the first tick after spawn or teleport, seed the motor ground-probe
			// history so it uses a full capsule-radius probe instead of the 0.005f
			// minimum. Without this, characters appear airborne for 1-3 frames.
			if (!hasDoneInitialGroundProbe)
			{
				Motor.LastMovementIterationFoundAnyGround = true;
				hasDoneInitialGroundProbe = true;
			}
		}

		/// <summary>
		/// (Called by KinematicCharacterMotor during its update cycle)
		/// This is where you tell your character what its rotation should be right now. 
		/// This is the ONLY place where you should set the character's rotation
		/// </summary>
		public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
		{
			switch (CurrentCharacterState)
			{
				case KCCCharacterState.Default:
					{
						if (lookInputVector.sqrMagnitude > 0f && OrientationSharpness > 0f)
						{
							float targetRotationY = Mathf.Atan2(lookInputVector.x, lookInputVector.z) * Mathf.Rad2Deg;

							Quaternion targetQuaternion = Quaternion.Euler(0.0f, targetRotationY, 0.0f);

							if (Mathf.Abs(Quaternion.Angle(Motor.TransientRotation, targetQuaternion) - 180f) <= 3f)
							{
								//Log.Debug("180 degree detected");
								targetRotationY -= 10f;
							}

							currentRotation = Quaternion.Slerp(Motor.TransientRotation, Quaternion.Euler(0.0f, targetRotationY, 0.0f), 1 - Mathf.Exp(-OrientationSharpness * deltaTime));
						}

						Vector3 currentUp = (currentRotation * Vector3.up);
						if (BonusOrientationMethod == BonusOrientationMethod.TowardsGravity)
						{
							// Rotate from current up to invert gravity
							Vector3 smoothedGravityDir = Vector3.Slerp(currentUp, -Constants.Character.Gravity.normalized, 1 - Mathf.Exp(-BonusOrientationSharpness * deltaTime));
							currentRotation = Quaternion.FromToRotation(currentUp, smoothedGravityDir) * currentRotation;
						}
						else if (BonusOrientationMethod == BonusOrientationMethod.TowardsGroundSlopeAndGravity)
						{
							if (Motor.GroundingStatus.IsStableOnGround)
							{
								Vector3 initialCharacterBottomHemiCenter = Motor.TransientPosition + (currentUp * Motor.Capsule.radius);

								Vector3 smoothedGroundNormal = Vector3.Slerp(Motor.CharacterUp, Motor.GroundingStatus.GroundNormal, 1 - Mathf.Exp(-BonusOrientationSharpness * deltaTime));
								currentRotation = Quaternion.FromToRotation(currentUp, smoothedGroundNormal) * currentRotation;

								// Move the position to create a rotation around the bottom hemi center instead of around the pivot
								Motor.SetTransientPosition(initialCharacterBottomHemiCenter + (currentRotation * Vector3.down * Motor.Capsule.radius));
							}
							else
							{
								Vector3 smoothedGravityDir = Vector3.Slerp(currentUp, -Constants.Character.Gravity.normalized, 1 - Mathf.Exp(-BonusOrientationSharpness * deltaTime));
								currentRotation = Quaternion.FromToRotation(currentUp, smoothedGravityDir) * currentRotation;
							}
						}
						else
						{
							Vector3 smoothedGravityDir = Vector3.Slerp(currentUp, Vector3.up, 1 - Mathf.Exp(-BonusOrientationSharpness * deltaTime));
							currentRotation = Quaternion.FromToRotation(currentUp, smoothedGravityDir) * currentRotation;
						}
						break;
					}
			}
		}

		/// <summary>
		/// (Called by KinematicCharacterMotor during its update cycle)
		/// This is where you tell your character what its velocity should be right now. 
		/// This is the ONLY place where you can set the character's velocity
		/// </summary>
		public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
		{
			switch (CurrentCharacterState)
			{
				case KCCCharacterState.Default:
					{
						AbilityType abilityType = AbilityType.None;

						float moveInputMagnitude = moveInputVector.sqrMagnitude;

						EnsureCached();

						// Determine ability state
						if (cachedAbilityController != null)
						{
							abilityType = cachedAbilityController.GetCurrentAbilityType();
						}

						// Ground movement
						if (Motor.GroundingStatus.IsStableOnGround)
						{
							UpdateGroundMovement(ref currentVelocity, moveInputMagnitude, deltaTime);
						}
						// Air movement
						else
						{
							UpdateAirMovement(ref currentVelocity, abilityType, moveInputMagnitude, deltaTime);
						}

						// Handle jumping
						HandleJumping(ref currentVelocity, abilityType, deltaTime);

						// Gravity is reset when using aerial abilities
						if (abilityType == AbilityType.AerialPhysical ||
							abilityType == AbilityType.AerialMagic)
						{
							currentVelocity.y = 0.0f;
						}

						break;
					}
			}
		}

		/// <summary>
		/// Handles ground-based movement including walking, sprinting, and crouching speed calculations.
		/// </summary>
		private void UpdateGroundMovement(ref Vector3 currentVelocity, float moveInputMagnitude, float deltaTime)
		{
			float currentVelocityMagnitude = currentVelocity.magnitude;

			Vector3 effectiveGroundNormal = Motor.GroundingStatus.GroundNormal;

			// Reorient velocity on slope
			currentVelocity = Motor.GetDirectionTangentToSurface(currentVelocity, effectiveGroundNormal) * currentVelocityMagnitude;

			// Calculate target velocity
			Vector3 inputRight = Vector3.Cross(moveInputVector, Motor.CharacterUp);
			Vector3 reorientedInput = Vector3.Cross(effectiveGroundNormal, inputRight).normalized * moveInputVector.magnitude;

			float targetSpeed = Constants.Character.RunSpeed;

			if (cachedAttributeController != null)
			{
				if (isCrouching)
				{
					targetSpeed = Constants.Character.CrouchSpeed;
				}
				else if (sprintInputDown &&
						 SprintSpeedTemplate != null &&
						 moveInputMagnitude > 0f &&
						 cachedAttributeController.TryGetStaminaAttribute(out CharacterResourceAttribute stamina) &&
						 cachedAttributeController.TryGetAttribute(SprintSpeedTemplate, out CharacterAttribute sprintSpeedModifier))
				{
					float currentStaminaCost = Constants.Character.SprintStaminaCost * deltaTime;

					if (stamina.CurrentValue >= currentStaminaCost)
					{
						stamina.Consume(currentStaminaCost);
						targetSpeed = Constants.Character.SprintSpeed * sprintSpeedModifier.FinalValueAsPct;
					}
				}
				else if (MoveSpeedTemplate != null &&
						 cachedAttributeController.TryGetAttribute(MoveSpeedTemplate, out CharacterAttribute moveSpeedModifier))
				{
					targetSpeed = Constants.Character.RunSpeed * moveSpeedModifier.FinalValueAsPct;
				}
			}
			else
			{
				if (isCrouching)
				{
					targetSpeed = Constants.Character.CrouchSpeed;
				}
				else if (sprintInputDown)
				{
					targetSpeed = Constants.Character.SprintSpeed;
				}
			}


			// Speed cap — enforced identically on client and server through the
			// shared deterministic prediction pipeline. Prevents super-speed exploits
			// from stacking movement buffs or attribute calculation bugs.
			// Because this runs in shared code on both sides, a modified client that
			// removes the clamp will see a brief local misprediction that snaps back
			// on the next reconcile pass.
			const float MaxAllowedSpeed = Constants.Character.SprintSpeed * 3.0f;
			targetSpeed = Mathf.Min(targetSpeed, MaxAllowedSpeed);

			Vector3 targetMovementVelocity = reorientedInput * targetSpeed;

			// Smooth movement Velocity
			currentVelocity = Vector3.Lerp(currentVelocity, targetMovementVelocity, StableMovementSharpness * deltaTime);
		}

		/// <summary>
		/// Handles air-based movement including air acceleration, gravity, fast fall, and drag.
		/// </summary>
		private void UpdateAirMovement(ref Vector3 currentVelocity, AbilityType abilityType, float moveInputMagnitude, float deltaTime)
		{
			// Add move input
			if (moveInputMagnitude > 0f)
			{
				Vector3 addedVelocity = moveInputVector * AirAccelerationSpeed * deltaTime;

				Vector3 currentVelocityOnInputsPlane = Vector3.ProjectOnPlane(currentVelocity, Motor.CharacterUp);

				// Limit air velocity from inputs
				if (currentVelocityOnInputsPlane.magnitude < MaxAirMoveSpeed)
				{
					// clamp addedVel to make total vel not exceed max vel on inputs plane
					Vector3 newTotal = Vector3.ClampMagnitude(currentVelocityOnInputsPlane + addedVelocity, MaxAirMoveSpeed);
					addedVelocity = newTotal - currentVelocityOnInputsPlane;
				}
				else
				{
					// Make sure added vel doesn't go in the direction of the already-exceeding velocity
					if (Vector3.Dot(currentVelocityOnInputsPlane, addedVelocity) > 0f)
					{
						addedVelocity = Vector3.ProjectOnPlane(addedVelocity, currentVelocityOnInputsPlane.normalized);
					}
				}

				// Prevent air-climbing sloped walls
				if (Motor.GroundingStatus.FoundAnyGround)
				{
					if (Vector3.Dot(currentVelocity + addedVelocity, addedVelocity) > 0f)
					{
						Vector3 perpenticularObstructionNormal = Vector3.Cross(Vector3.Cross(Motor.CharacterUp, Motor.GroundingStatus.GroundNormal), Motor.CharacterUp).normalized;
						addedVelocity = Vector3.ProjectOnPlane(addedVelocity, perpenticularObstructionNormal);
					}
				}

				// Apply added velocity
				currentVelocity += addedVelocity;
			}

			if (cachedAttributeController != null)
			{
				// Gravity is not applied while an aerial attack is activating
				if (abilityType != AbilityType.AerialPhysical &&
					abilityType != AbilityType.AerialMagic &&
					GravityTemplate != null &&
					cachedAttributeController.TryGetAttribute(GravityTemplate, out CharacterAttribute gravityModifier))
				{
					currentVelocity += Constants.Character.Gravity * gravityModifier.FinalValueAsPct * deltaTime;
				}

				// Fast Fall
				if (isCrouching &&
					FastFallSpeedTemplate != null &&
					cachedAttributeController.TryGetAttribute(FastFallSpeedTemplate, out CharacterAttribute fastFallModifier))
				{
					currentVelocity.y += Constants.Character.Gravity.y * fastFallModifier.FinalValueAsPct * deltaTime;
				}
			}
			else
			{
				// Default Gravity
				currentVelocity += Constants.Character.Gravity * deltaTime;
			}

			// Drag
			currentVelocity *= (1f / (1f + (Drag * deltaTime)));
		}

		/// <summary>
		/// Handles jump requests including stamina consumption, ground grace period, and jump force application.
		/// </summary>
		private void HandleJumping(ref Vector3 currentVelocity, AbilityType abilityType, float deltaTime)
		{
			/* Saturate instead of accumulating forever.
			 *
			 * This starts at float.MaxValue, where adding deltaTime is a no-op, so the field is
			 * constant and costs nothing on the wire — until the first jump resets it to 0, after
			 * which it changed every tick for the rest of the session and put 4 bytes in every
			 * reconcile (120 B/s per player) to express "still much greater than the grace window".
			 * The only reader compares it against JumpPreGroundingGraceTime, so anything past that
			 * is indistinguishable; parking it at MaxValue restores the quiet state. */
			if (timeSinceJumpRequested < float.MaxValue)
			{
				timeSinceJumpRequested += deltaTime;
				if (timeSinceJumpRequested > JumpPreGroundingGraceTime + 1f)
				{
					timeSinceJumpRequested = float.MaxValue;
				}
			}
			if (jumpRequested)
			{
				// See if we actually are allowed to jump
				if (cachedAttributeController != null &&
					cachedAttributeController.TryGetStaminaAttribute(out CharacterResourceAttribute stamina) &&
					stamina.CurrentValue >= Constants.Character.JumpStaminaCost &&
					abilityType == AbilityType.None &&
					(AllowJumpingWhenSliding ? Motor.GroundingStatus.FoundAnyGround : Motor.GroundingStatus.IsStableOnGround) &&
					timeSinceLastAbleToJump <= JumpPostGroundingGraceTime)
				{
					// Calculate jump direction before ungrounding
					Vector3 jumpDirection = Motor.CharacterUp;
					if (Motor.GroundingStatus.FoundAnyGround && !Motor.GroundingStatus.IsStableOnGround)
					{
						jumpDirection = Motor.GroundingStatus.GroundNormal;
					}

					// Makes the character skip ground probing/snapping on its next update. 
					// If this line weren't here, the character would remain snapped to the ground when trying to jump. Try commenting this line out and see.
					Motor.ForceUnground();

					// Add to the return velocity and reset jump state
					float jumpSpeed = Constants.Character.JumpUpSpeed;
					if (JumpSpeedTemplate != null &&
						cachedAttributeController.TryGetAttribute(JumpSpeedTemplate, out CharacterAttribute jumpSpeedModifier))
					{
						jumpSpeed *= jumpSpeedModifier.FinalValueAsPct;
					}
					currentVelocity += (jumpDirection * jumpSpeed) - Vector3.Project(currentVelocity, Motor.CharacterUp);
					currentVelocity += (moveInputVector * JumpScalableForwardSpeed);

					// Consume stamina when jumping
					stamina.Consume(Constants.Character.JumpStaminaCost);

					jumpRequested = false;
				}
			}
		}

		/// <summary>
		/// (Called by KinematicCharacterMotor during its update cycle)
		/// This is called after the character has finished its movement update
		/// </summary>
		public void AfterCharacterUpdate(float deltaTime)
		{
			switch (CurrentCharacterState)
			{
				case KCCCharacterState.Default:
					{
						// Handle jump-related values
						{
							if (jumpRequested && timeSinceJumpRequested > JumpPreGroundingGraceTime)
							{
								jumpRequested = false;
							}

							if (AllowJumpingWhenSliding ? Motor.GroundingStatus.FoundAnyGround : Motor.GroundingStatus.IsStableOnGround)
							{
								// If we're on a ground surface, reset jumping values
								timeSinceLastAbleToJump = 0f;
							}
							else
							{
								// Keep track of time since we were last able to jump (for grace period)
								timeSinceLastAbleToJump += deltaTime;
							}
						}

						// Handle crouch transitions (the first branch crouches, the second tries to uncrouch)
						if (!isCrouching && crouchInputDown)
						{
							isCrouching = true;
							ApplyCapsuleDimensions(crouched: true);
						}
						else if (isCrouching && !crouchInputDown)
						{
							// Do an overlap test with the character's standing height to see if there are any obstructions
							ApplyCapsuleDimensions(crouched: false);
							if (Motor.CharacterOverlap(
								Motor.TransientPosition,
								Motor.TransientRotation,
								probedColliders,
								Motor.CollidableLayers,
								QueryTriggerInteraction.Ignore) > 0)
							{
								// If obstructions, just stick to crouching dimensions
								// This is offset to ensure the crouch goes towards the feet
								// instead of towards the head. Otherwise we can't uncrouch!
								//MeshRoot.localScale = new Vector3(1f, CrouchedCapsuleHeight / FullCapsuleHeight, 1f);
								ApplyCapsuleDimensions(crouched: true);
							}
							else
							{
								// If no obstructions, uncrouch
								isCrouching = false;
							}
						}

						EnsureCached();
						if (cachedAnimationController != null)
						{
							cachedAnimationController.SetCrouching(isCrouching);
							cachedAnimationController.SetGrounded(Motor.GroundingStatus.FoundAnyGround);

							// Set speed parameter for locomotion blend tree
							float speed = 0f;
							if (Motor.GroundingStatus.IsStableOnGround)
							{
								float vel = Motor.Velocity.magnitude;
								if (vel > 0.1f)
								{
									float sprintThreshold = Constants.Character.SprintSpeed * 0.8f;
									float runThreshold = Constants.Character.RunSpeed * 0.5f;

									if (isCrouching)
										speed = 0.3f; // Crouch walk
									else if (vel >= sprintThreshold)
										speed = 1.5f; // Sprint
									else if (vel >= runThreshold)
										speed = 1.0f; // Run
									else
										speed = 0.5f; // Walk
								}
							}
							cachedAnimationController.SetSpeed(speed);
						}
						break;
					}
			}
		}

		/// <summary>
		/// Called after grounding update to handle landing and leaving ground events.
		/// Triggers OnLanded or OnLeaveStableGround as appropriate.
		/// </summary>
		/// <param name="deltaTime">Frame time.</param>
		public void PostGroundingUpdate(float deltaTime)
		{
			// Handle landing and leaving ground
			if (Motor.GroundingStatus.IsStableOnGround && !Motor.LastGroundingStatus.IsStableOnGround)
			{
				OnLanded();
			}
			else if (!Motor.GroundingStatus.IsStableOnGround && Motor.LastGroundingStatus.IsStableOnGround)
			{
				OnLeaveStableGround();
			}
		}

		/// <summary>
		/// Determines if a collider is valid for collision checks, ignoring those in the IgnoredColliders list.
		/// </summary>
		/// <param name="coll">Collider to check.</param>
		/// <returns>True if valid for collision, false if ignored.</returns>
		public bool IsColliderValidForCollisions(Collider coll)
		{
			return IgnoredColliders.Count == 0 || !IgnoredColliders.Contains(coll);
		}

		/// <summary>
		/// Called when the character hits the ground. Can be used for landing effects or stability checks.
		/// </summary>
		/// <param name="hitCollider">Collider hit.</param>
		/// <param name="hitNormal">Normal of the hit surface.</param>
		/// <param name="hitPoint">Point of contact.</param>
		/// <param name="hitStabilityReport">Stability report for the hit.</param>
		public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport)
		{
			// Implement landing logic or ground hit effects here if needed.
		}

		/// <summary>
		/// Called when the character hits something during movement. Can be used for collision effects.
		/// </summary>
		/// <param name="hitCollider">Collider hit.</param>
		/// <param name="hitNormal">Normal of the hit surface.</param>
		/// <param name="hitPoint">Point of contact.</param>
		/// <param name="hitStabilityReport">Stability report for the hit.</param>
		public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport)
		{
			// Implement movement hit logic or effects here if needed.
		}

		/// <summary>
		/// Processes the hit stability report for advanced collision and grounding logic.
		/// </summary>
		/// <param name="hitCollider">Collider hit.</param>
		/// <param name="hitNormal">Normal of the hit surface.</param>
		/// <param name="hitPoint">Point of contact.</param>
		/// <param name="atCharacterPosition">Character position at hit.</param>
		/// <param name="atCharacterRotation">Character rotation at hit.</param>
		/// <param name="hitStabilityReport">Stability report for the hit.</param>
		public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport hitStabilityReport)
		{
			// Implement advanced hit stability processing here if needed.
		}

		/// <summary>
		/// Called when the character lands on stable ground.
		/// </summary>
		/// <remarks>
		/// Empty, and deliberately so — the extension point is the point, exactly as with
		/// <see cref="OnLeaveStableGround"/>. Anything added here runs on every replayed tick of a
		/// reconcile as well as on the live one, so it must be either idempotent or reconciled; see
		/// the note where <c>IsJumping</c> was removed.
		/// </remarks>
		protected void OnLanded()
		{
		}

		/// <summary>
		/// Called when the character leaves stable ground. Can be used for airborne effects.
		/// </summary>
		protected void OnLeaveStableGround()
		{
			// Implement airborne logic or effects here if needed.
		}

		/// <summary>
		/// Called when a discrete collision is detected. Can be used for collision effects.
		/// </summary>
		/// <param name="hitCollider">Collider hit.</param>
		public void OnDiscreteCollisionDetected(Collider hitCollider)
		{
			// Implement discrete collision logic or effects here if needed.
		}
	}
}