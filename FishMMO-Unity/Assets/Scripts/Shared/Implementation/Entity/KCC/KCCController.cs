using System.Collections.Generic;
using UnityEngine;
using KinematicCharacterController;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	public enum KCCCharacterState
	{
		Default,
	}

	public enum OrientationMethod
	{
		TowardsCamera,
		TowardsMovement,
	}

	public struct AICharacterInputs
	{
		public Vector3 MoveVector;
		public Vector3 LookVector;
	}

	public enum BonusOrientationMethod
	{
		None,
		TowardsGravity,
		TowardsGroundSlopeAndGravity,
	}

	public class KCCController : MonoBehaviour, ICharacterController
	{
		public IPlayerCharacter Character;
		public KinematicCharacterMotor Motor;

		[Header("Stable Movement")]
		public float StableMovementSharpness = 20f;
		public float OrientationSharpness = 10f;
		public OrientationMethod OrientationMethod = OrientationMethod.TowardsCamera;

		[Header("Air Movement")]
		public float MaxAirMoveSpeed = 6f;
		public float AirAccelerationSpeed = 0f;
		public float Drag = 0.1f;

		[Header("Jumping")]
		public bool AllowJumpingWhenSliding = false;
		public float JumpScalableForwardSpeed = 0f;
		public float JumpPreGroundingGraceTime = 0f;
		public float JumpPostGroundingGraceTime = 0f;

		[Header("Misc")]
		public List<Collider> IgnoredColliders = new List<Collider>();
		public BonusOrientationMethod BonusOrientationMethod = BonusOrientationMethod.None;
		public float BonusOrientationSharpness = 10f;
		public Transform MeshRoot;
		public Transform CameraFollowPoint;
		public float CrouchedCapsuleHeight = 0.5f;
		public float FullCapsuleHeight = 2f;
		public float CapsuleBaseOffset = 1f;
		public CharacterAttributeTemplate MoveSpeedTemplate;
		public CharacterAttributeTemplate SprintSpeedTemplate;
		public CharacterAttributeTemplate JumpSpeedTemplate;
		public CharacterAttributeTemplate SwimSpeedTemplate;
		public CharacterAttributeTemplate FastFallSpeedTemplate;
		public CharacterAttributeTemplate GravityTemplate;

		public KCCCharacterState CurrentCharacterState { get; private set; }

		private Collider[] probedColliders = new Collider[8];

		private Animator animator = null;

		// Current frame input state
		private Vector3 moveInputVector;
		private Vector3 lookInputVector;
		private Quaternion cameraRotation;
		private bool crouchInputDown = false;
		private bool jumpRequested = false;
		private bool sprintInputDown = false;

		// Multi Frame State, this needs to be synchronized
		private float timeSinceJumpRequested = float.MaxValue;
		private float timeSinceLastAbleToJump = 0f;
		private bool isCrouching = false;

		public Vector3 VirtualCameraPosition { get; private set; }
		public Quaternion VirtualCameraRotation { get; private set; }
		public bool IsJumping { get; private set; }

		private void Awake()
		{
			// Handle initial state
			TransitionToState(KCCCharacterState.Default);

			animator = GetComponentInChildren<Animator>();
		}

		private void OnEnable()
		{
			moveInputVector = Vector3.zero;
			lookInputVector = Vector3.zero;
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

		public void ApplyState(KinematicCharacterMotorState state)
		{
			// Take any state needed for the controller here
			isCrouching = state.IsCrouching;
			jumpRequested = state.JumpRequested;
			timeSinceLastAbleToJump = state.TimeSinceLastAbleToJump;
			timeSinceJumpRequested = state.TimeSinceJumpRequested;

			Motor.ApplyState(state);
		}

		public KinematicCharacterMotorState GetState()
		{
			KinematicCharacterMotorState baseState = Motor.GetState();

			// Apply state from controller here.
			baseState.IsCrouching = isCrouching;
			baseState.JumpRequested = jumpRequested;
			baseState.TimeSinceLastAbleToJump = timeSinceLastAbleToJump;
			baseState.TimeSinceJumpRequested = timeSinceJumpRequested;

			return baseState;
		}

		/// <summary>
		/// This is called every frame by ExamplePlayer in order to tell the character what its inputs are
		/// </summary>
		public void SetInputs(ref KCCInputReplicateData inputs)
		{
			VirtualCameraPosition = inputs.CameraPosition;
			VirtualCameraRotation = inputs.CameraRotation;

			//Log.Debug("VPos:" + VirtualCameraPosition + " VRot:" + VirtualCameraRotation);

			// Clamp input
			Vector3 moveInputVector = Vector3.ClampMagnitude(new Vector3(inputs.MoveAxisRight, 0f, inputs.MoveAxisForward), 1f);

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
						// Move and look inputs
						moveInputVector = cameraPlanarRotation * moveInputVector;

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
							float targetRotationY = Mathf.Atan2(lookInputVector.x, lookInputVector.z) * Mathf.Rad2Deg + cameraRotation.eulerAngles.y;

							Quaternion targetQuarternion = Quaternion.Euler(0.0f, targetRotationY, 0.0f);

							if (Mathf.Abs(Quaternion.Angle(Motor.TransientRotation, targetQuarternion) - 180f) <= 3f)
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

						// Determine ability state
						if (Character.TryGet(out IAbilityController abilityController))
						{
							abilityType = abilityController.GetCurrentAbilityType();
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

			if (Character.TryGet(out ICharacterAttributeController attributeController))
			{
				if (isCrouching)
				{
					targetSpeed = Constants.Character.CrouchSpeed;
				}
				else if (sprintInputDown &&
						 SprintSpeedTemplate != null &&
						 moveInputMagnitude > 0f &&
						 attributeController.TryGetStaminaAttribute(out CharacterResourceAttribute stamina) &&
						 attributeController.TryGetAttribute(SprintSpeedTemplate, out CharacterAttribute sprintSpeedModifier))
				{
					float currentStaminaCost = Constants.Character.SprintStaminaCost * deltaTime;

					if (stamina.CurrentValue >= currentStaminaCost)
					{
						stamina.Consume(currentStaminaCost);
						targetSpeed = Constants.Character.SprintSpeed * sprintSpeedModifier.FinalValueAsPct;
					}
				}
				else if (MoveSpeedTemplate != null &&
						 attributeController.TryGetAttribute(MoveSpeedTemplate, out CharacterAttribute moveSpeedModifier))
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

			// Find the gravity attribute in the characters attribute controller
			if (Character.TryGet(out ICharacterAttributeController attributeController))
			{
				// Gravity is not applied while an aerial attack is activating
				if (abilityType != AbilityType.AerialPhysical &&
					abilityType != AbilityType.AerialMagic &&
					GravityTemplate != null &&
					attributeController.TryGetAttribute(GravityTemplate, out CharacterAttribute gravityModifier))
				{
					currentVelocity += Constants.Character.Gravity * gravityModifier.FinalValueAsPct * deltaTime;
				}

				// Fast Fall
				if (isCrouching &&
					FastFallSpeedTemplate != null &&
					attributeController.TryGetAttribute(FastFallSpeedTemplate, out CharacterAttribute fastFallModifier))
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
			timeSinceJumpRequested += deltaTime;
			if (jumpRequested)
			{
				// See if we actually are allowed to jump
				if (Character.TryGet(out ICharacterAttributeController attributeController) &&
					attributeController.TryGetStaminaAttribute(out CharacterResourceAttribute stamina) &&
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
						attributeController.TryGetAttribute(JumpSpeedTemplate, out CharacterAttribute jumpSpeedModifier))
					{
						jumpSpeed *= jumpSpeedModifier.FinalValueAsPct;
					}
					currentVelocity += (jumpDirection * jumpSpeed) - Vector3.Project(currentVelocity, Motor.CharacterUp);
					currentVelocity += (moveInputVector * JumpScalableForwardSpeed);

					// Consume stamina when jumping
					stamina.Consume(Constants.Character.JumpStaminaCost);

					jumpRequested = false;

					IsJumping = true;
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

						// Handle uncrouching
						if (!isCrouching && crouchInputDown)
						{
							isCrouching = true;
							Motor.SetCapsuleDimensions(Motor.Capsule.radius, CrouchedCapsuleHeight, CapsuleBaseOffset / (FullCapsuleHeight / CrouchedCapsuleHeight));
						}
						else if (isCrouching && !crouchInputDown)
						{
							// Do an overlap test with the character's standing height to see if there are any obstructions
							Motor.SetCapsuleDimensions(Motor.Capsule.radius, FullCapsuleHeight, CapsuleBaseOffset);
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
								Motor.SetCapsuleDimensions(Motor.Capsule.radius, CrouchedCapsuleHeight, CapsuleBaseOffset / (FullCapsuleHeight / CrouchedCapsuleHeight));
							}
							else
							{
								// If no obstructions, uncrouch
								isCrouching = false;
							}
						}

						if (animator != null)
						{
							animator.SetBool("Crouching", isCrouching);
							animator.SetBool("OnGround", Motor.GroundingStatus.FoundAnyGround);
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
			if (IgnoredColliders.Count == 0)
			{
				return true;
			}

			if (IgnoredColliders.Contains(coll))
			{
				return false;
			}

			return true;
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
		/// Called when the character lands on stable ground. Resets jumping state.
		/// </summary>
		protected void OnLanded()
		{
			IsJumping = false;
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