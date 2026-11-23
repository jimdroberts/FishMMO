using System.Collections.Generic;
using UnityEngine;
using KinematicCharacterController;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

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

		[Header("Swimming")]
		/// <summary>
		/// Whether this character swims at all. Every player does (swimming becomes a profession later,
		/// a skill on top of it, never a gate in front of it).
		/// </summary>
		public bool CanSwim = true;
		/// <summary>How quickly a swimmer's velocity follows what it is asked for: water is thick, so far softer than the ground's.</summary>
		public float SwimSharpness = 4f;
		/// <summary>How hard a swimmer near the surface is pulled to its float line, metres per second per metre off it.</summary>
		public float SwimBuoyancy = 2.5f;
		/// <summary>The share of the swimming speed left in lava, which is thick.</summary>
		public float LavaSwimSpeedShare = 0.5f;

		[Header("Climbing")]
		/// <summary>Whether this character climbs ladders, rope ladders and ropes (<see cref="SceneClimbables"/>) at all.</summary>
		public bool CanClimb = true;
		/// <summary>
		/// Optional: a character attribute that scales climbing speed. Without one, climbing follows Move Speed alone, so
		/// whatever slows a character's walk slows its climb too.
		/// </summary>
		public CharacterAttributeTemplate ClimbSpeedTemplate;
		/// <summary>How quickly a climber turns to face the climbable (and the way off it over the top).</summary>
		public float ClimbTurnSharpness = 12f;

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
		/// Cached mount (the Mounts module's controller; null without the module or on an NPC). Lazily resolved like the rest.
		/// </summary>
		private ICharacterMount cachedMount;

		/// <summary>Tests only: a mount for a motor with no character (MountMotorTests drive a bare motor).</summary>
		internal ICharacterMount MountOverride { get; set; }

		/// <summary>The mount this controller asks: the character's, or the test's.</summary>
		private ICharacterMount MountSource => MountOverride ?? cachedMount;

		// The mount ridden this tick (issue #109): read from the buff state (reconciled) at the start of every input and motor
		// step, so the server, the owner and each replay drive the same motion. See RefreshMount.
		private bool mounted;
		private MountMotion mountMotion;
		// The rider's own steepest walkable slope, kept while a mount (a llama) lends its own. NaN until first read.
		private float ownMaxStableSlope = float.NaN;

		/// <summary>True while the character rides a mount this tick.</summary>
		public bool IsMounted => mounted;

		/// <summary>True while a flying mount is off stable ground: it flies (<see cref="UpdateMountFlight"/>).</summary>
		public bool IsMountFlying => mounted && mountMotion.Flies && !isSwimming && !climb.IsClimbing && !isFlying &&
			!Motor.GroundingStatus.IsStableOnGround;

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
		private bool ascendInputDown = false;
		// The camera-relative move input in all three dimensions: where a diver goes.
		private Vector3 swimMoveVector;
		private float swimLookPitch;

		// Multi Frame State, this needs to be synchronized
		private float timeSinceJumpRequested = float.MaxValue;
		private float timeSinceLastAbleToJump = 0f;
		private bool isCrouching = false;
		private bool isSwimming = false;
		// Game-master flight (/gm fly): set only by the server, reconciled through IsFlying.
		private bool isFlying = false;
		// Crouch held while flying: sink. Rebuilt from the input every tick.
		private bool descendInputDown;
		private byte climbOutTicks = 0;
		// The climb, reconciled through Climbable, ClimbPhase and ClimbTicks.
		private ClimbState climb = ClimbState.Free;
		private readonly List<int> climbCandidates = new List<int>(4);
		// Set by SetInputs each tick: the controls are taken away (stunned, frozen, mesmerised, dead), so a climber lets go.
		private bool controlsSuppressedThisTick;
		// An ability rush (a charge), reconciled through MotionTicks, MotionSpeed and MotionYaw: see BeginMotion.
		private byte motionTicks;
		private ushort motionSpeed;
		private ushort motionYaw;
		// The owner's rush stops made by a predicted hit, by the replicate tick they followed: see EndMotionFromHit.
		private readonly PredictedRushStops predictedRushStops = new PredictedRushStops();
		private uint lastSimulatedTick;
		private bool hasSimulatedTick;
		private bool recordsPredictedStops;

		// The water at the character this tick: sampled before the motor moves, from the synced clock.
		private WaterSample water = WaterSample.Dry;

		/// <summary>
		/// The synced server tick the water is read at (the tide moves the sea): set by the replicate before each motor
		/// update. Never the replicate's own tick, which is the owner's unsynchronised counter.
		/// </summary>
		public uint WaterTick { get; set; }

		/// <summary>Whether the character is swimming now.</summary>
		public bool IsSwimming => isSwimming;

		/// <summary>Whether the character is in game-master flight now (/gm fly): no gravity, ground or collisions.</summary>
		public bool IsFlying => isFlying;

		/// <summary>True while an ability rush (<see cref="BeginMotion"/>) is driving the character.</summary>
		public bool HasMotion => motionTicks > 0;

		/// <summary>
		/// Starts an ability rush: for <paramref name="durationSeconds"/> the character is driven across the ground along
		/// <paramref name="direction"/> fast enough to cover <paramref name="distance"/>, facing it, whatever its input says.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Predicted.</b> Called from the ability step, which runs after this character's movement step in the same tick,
		/// on the server and on the owner alike (and again on a replayed tick: the ability re-applies it). The rush is then
		/// motor state, reconciled through <c>MotionTicks</c>/<c>MotionSpeed</c>/<c>MotionYaw</c>, so a correction from inside
		/// it continues it rather than dropping it.
		/// </para>
		/// <para>
		/// Speed and heading are quantised here (centimetres per second; 1/65536 of a turn), so the values both peers drive
		/// are the values the wire carries. A rush ends early when something stops it (<see cref="TickMotion"/>), and never
		/// starts while climbing, swimming or flying.
		/// </para>
		/// </remarks>
		/// <param name="direction">The way to rush; only its horizontal part is used.</param>
		/// <param name="distance">How far, metres.</param>
		/// <param name="durationSeconds">Over how long.</param>
		/// <param name="tickDelta">Seconds per network tick.</param>
		/// <returns>True when the rush started.</returns>
		public bool BeginMotion(Vector3 direction, float distance, float durationSeconds, float tickDelta)
		{
			direction.y = 0f;
			if (direction.sqrMagnitude < 1e-6f || distance <= 0f || tickDelta <= 0f || climb.IsClimbing || isSwimming || isFlying)
			{
				return false;
			}
			int ticks = Mathf.Clamp((int)System.Math.Ceiling(System.Math.Max(durationSeconds, tickDelta) / (double)tickDelta), 1, 255);
			motionTicks = (byte)ticks;
			motionSpeed = QuantizeMotionSpeed(distance / (ticks * tickDelta));
			motionYaw = QuantizeMotionYaw(direction);
			return true;
		}

		/// <summary>Ends an ability rush now, from inside the movement step (stunned, rooted): a replay reaches it by itself.</summary>
		public void EndMotion()
		{
			if (motionTicks == 0)
			{
				return;
			}
			motionTicks = 0;
			SettleAfterMotion();
		}

		/// <summary>
		/// Ends an ability rush because a hit stopped it: from OUTSIDE the movement step, where the ability object resolves
		/// its hits.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>On the owner the stop is remembered</b>, against the movement tick it followed. A hit is not run again when the owner
		/// replays a correction, so a correction from before the hit used to replay the rush straight past the target, and
		/// the server's own stop then snapped the charger back to it a moment later. The remembered stop is applied again when
		/// a replay passes its tick (<see cref="ReapplyPredictedRushStop"/>) or when a correction lands past it with the server
		/// still rushing (<see cref="OnReconciled"/>), and forgotten once the server's state shows the rush stopped there (or
		/// after <see cref="PredictedRushStops.ExpiryTicks"/>, when the server never struck: then the server's rush is the truth).
		/// </para>
		/// <para>On the server it is simply the stop; the reconcile carries it.</para>
		/// </remarks>
		public void EndMotionFromHit()
		{
			if (motionTicks == 0)
			{
				return;
			}
			motionTicks = 0;
			SettleAfterMotion();
			if (recordsPredictedStops && hasSimulatedTick)
			{
				predictedRushStops.Record(lastSimulatedTick);
			}
		}

		/// <summary>
		/// Called after every movement step (first pass and replay) with its tick. <paramref name="recordsStops"/> is true on
		/// the owner only, which is the one peer that replays and so the one that must remember hit stops.
		/// </summary>
		public void NoteSimulatedTick(uint tick, bool recordsStops)
		{
			lastSimulatedTick = tick;
			hasSimulatedTick = true;
			recordsPredictedStops = recordsStops;
		}

		/// <summary>On a REPLAYED movement step: the rush stop a hit made after this tick, made again.</summary>
		public void ReapplyPredictedRushStop(uint tick)
		{
			if (motionTicks > 0 && predictedRushStops.StopsAfter(tick))
			{
				motionTicks = 0;
				SettleAfterMotion();
			}
		}

		/// <summary>
		/// After a correction for <paramref name="reconciledTick"/> has been applied (before the replay): forgets the stops the
		/// server has confirmed or never made, and keeps a stop the correction would undo (the server, a tick or two behind
		/// the owner's hit, still rushing at a tick past it).
		/// </summary>
		public void OnReconciled(uint reconciledTick)
		{
			if (predictedRushStops.OnReconciled(reconciledTick, motionTicks > 0))
			{
				motionTicks = 0; // past the stop and still rushing: the replay must not carry on
				SettleAfterMotion();
			}
		}

		/// <summary>
		/// Moves the character to <paramref name="destination"/> at once: an ability blink. Called from the ability step on
		/// the server and the owner (and again on a replayed tick), so both place it identically; any rush ends.
		/// </summary>
		public void TeleportTo(Vector3 destination)
		{
			motionTicks = 0;
			Motor.SetPosition(destination);
			ResetGroundProbe();
		}

		/// <summary>
		/// <see cref="TeleportTo(Vector3)"/>, also turning the character to <paramref name="rotation"/> (a blink that lands
		/// facing what it struck). Server-side callers reach the owner through the next reconcile.
		/// </summary>
		public void TeleportTo(Vector3 destination, Quaternion rotation)
		{
			motionTicks = 0;
			Motor.SetPositionAndRotation(destination, rotation);
			ResetGroundProbe();
		}

		/// <summary>A rush speed in metres per second as the wire carries it: whole centimetres per second.</summary>
		public static ushort QuantizeMotionSpeed(float metresPerSecond)
		{
			return (ushort)Mathf.Clamp(Mathf.RoundToInt(metresPerSecond * 100f), 1, ushort.MaxValue);
		}

		/// <summary>A heading as the wire carries it: 1/65536 of a turn about world up.</summary>
		public static ushort QuantizeMotionYaw(Vector3 direction)
		{
			float degrees = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
			if (degrees < 0f)
			{
				degrees += 360f;
			}
			return (ushort)(Mathf.RoundToInt(degrees / 360f * 65536f) & 0xFFFF);
		}

		/// <summary>The unit horizontal heading a quantised yaw names.</summary>
		public static Vector3 MotionDirectionOf(ushort yaw)
		{
			float radians = yaw / 65536f * 2f * Mathf.PI;
			return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
		}

		/// <summary>Flying speed, metres per second; sprint multiplies it by <see cref="FlySprintMultiplier"/>.</summary>
		public float FlySpeed = 12f;
		/// <summary>Sprinting while flying multiplies the speed by this.</summary>
		public float FlySprintMultiplier = 3f;
		/// <summary>How quickly a flyer's velocity follows what it is asked for.</summary>
		public float FlySharpness = 10f;

		/// <summary>
		/// Starts or stops game-master flight. SERVER ONLY: the owner learns it from the next reconcile (IsFlying), so both
		/// fly the same path from that tick. Flying lets go of a climbable and stops swimming; while it lasts nothing collides
		/// with the character and nothing holds it up or down.
		/// </summary>
		public void SetFlying(bool value)
		{
			if (isFlying == value)
			{
				return;
			}
			if (value)
			{
				if (climb.IsClimbing)
				{
					climb = ClimbState.Free;
				}
				isSwimming = false;
				climbOutTicks = 0;
				motionTicks = 0;
				if (isCrouching)
				{
					isCrouching = false;
					ApplyCapsuleDimensions(false);
				}
			}
			isFlying = value;
			ApplyFlightToMotor();
			if (!value)
			{
				// Back to the world: probe for ground from scratch, as after a teleport.
				ResetGroundProbe();
			}
		}

		/// <summary>The motor's solvers follow the movement mode: flight turns off collisions and ground; swim and climb ground.</summary>
		private void ApplyFlightToMotor()
		{
			SyncMovementCollisions();
			Motor.SetGroundSolvingActivation(!isFlying && !isSwimming && !climb.IsClimbing);
		}

		/// <summary>Whether the motor currently solves movement collisions (its default is on).</summary>
		private bool movementCollisionsSolved = true;

		/// <summary>
		/// The motor's movement collisions follow the movement state: off in flight, and off while stepping on or off a
		/// climbable at its top.
		/// </summary>
		/// <remarks>
		/// The step over a climbable's top used to be collided motion with ground solving off: on uneven rock it could catch
		/// on the edge and time out, or slide past it, and which one happened turned on millimetres of contact, so the owner
		/// and the server could take different ticks to get over it. Now it follows its fixed path (ClimbingRules: up to the
		/// clearance, then over to the landing) with nothing to catch on, a pure function of the climb state. A function of
		/// reconciled state only, so it is the same on the server, live and on every replayed tick.
		/// </remarks>
		private void SyncMovementCollisions()
		{
			bool stepping = climb.IsClimbing && (climb.Phase == ClimbPhase.SteppingOn || climb.Phase == ClimbPhase.SteppingOff);
			bool collide = !isFlying && !stepping;
			if (collide != movementCollisionsSolved)
			{
				Motor.SetMovementCollisionsSolvingActivation(collide);
				movementCollisionsSolved = collide;
			}
		}

		/// <summary>Whether the character is on a ladder, rope ladder or rope now (stepping on or off at its top included).</summary>
		public bool IsClimbing => climb.IsClimbing;

		/// <summary>In the crouched stance (the shorter capsule).</summary>
		public bool IsCrouching => isCrouching;

		/// <summary>The climb: which of the scene's climbables, and where in it (<see cref="ClimbState.Free"/> when not climbing).</summary>
		public ClimbState Climb => climb;

		/// <summary>The water over the character this tick (<see cref="WaterSample.Dry"/> when there is none).</summary>
		public WaterSample Water => water;

		/// <summary>How far under the surface the top of the character's head is, metres; negative while it is out of the water.</summary>
		public float HeadDepth => water.Present ? water.Surface - (Motor.TransientPosition.y + Motor.Capsule.height) : float.NegativeInfinity;

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
		/// did not, be corrected, and repeat. That was an open item from the audit and it
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
			cachedMount = null;
			lastCachedCharacter = null;
		}

		/// <summary>
		/// Reads the mount this character rides now (<see cref="ICharacterMount"/>), and lends the motor a mount's steeper
		/// walkable slope. Called at the start of each input step and each motor update: both read only the buff state, which is
		/// reconciled, so every peer that simulates this character agrees.
		/// </summary>
		private void RefreshMount()
		{
			EnsureCached();
			if (cachedMount == null && Character != null)
			{
				// A part may register after the first tick cached the others (parts attach at spawn): asked again until found.
				Character.TryGet(out cachedMount);
			}
			ICharacterMount source = MountSource;
			mounted = source != null && source.TryGetMotion(out mountMotion);
			if (!mounted)
			{
				mountMotion = default;
			}
			if (Motor == null)
			{
				return;
			}
			if (float.IsNaN(ownMaxStableSlope))
			{
				ownMaxStableSlope = Motor.MaxStableSlopeAngle;
			}
			float slope = mounted && mountMotion.MaxSlopeDegrees > 0f ? mountMotion.MaxSlopeDegrees : ownMaxStableSlope;
			if (Motor.MaxStableSlopeAngle != slope)
			{
				Motor.MaxStableSlopeAngle = slope;
			}
		}

		/// <summary>Gets a rider off (swimming or taking hold of a climbable dismounts, issue #109): inside the motor step.</summary>
		private void DismountForMovement()
		{
			ICharacterMount source = MountSource;
			if (mounted && source != null)
			{
				source.Dismount();
				mounted = false;
				mountMotion = default;
				RefreshMount();
			}
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
				Character.TryGet(out cachedMount);
			}
			else
			{
				cachedAbilityController = null;
				cachedAttributeController = null;
				cachedAnimationController = null;
				cachedMount = null;
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
			climbOutTicks = state.ClimbOutTicks;
			motionTicks = state.MotionTicks;
			motionSpeed = state.MotionSpeed;
			motionYaw = state.MotionYaw;
			// The motor's ground solving follows the swim and climb state: it is off for either, and they are what is reconciled.
			bool wasSolvingGround = !isSwimming && !climb.IsClimbing && !isFlying;
			isFlying = state.IsFlying;
			isSwimming = state.IsSwimming;
			climb.Index = state.Climbable - 1;
			climb.Phase = (ClimbPhase)state.ClimbPhase;
			climb.Ticks = state.ClimbTicks;
			if (!climb.IsClimbing)
			{
				// Never one without the other: a phase with no climbable (or the reverse) is not a climb.
				climb.Index = -1;
				climb.Phase = ClimbPhase.None;
			}
			bool solvesGround = !isSwimming && !climb.IsClimbing && !isFlying;
			if (wasSolvingGround != solvesGround)
			{
				Motor.SetGroundSolvingActivation(solvesGround);
			}
			// Flight and a climb's step over the top move without collisions; restored with them.
			SyncMovementCollisions();

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
			baseState.IsSwimming = isSwimming;
			baseState.ClimbOutTicks = climbOutTicks;
			baseState.Climbable = climb.IsClimbing ? (ushort)(climb.Index + 1) : (ushort)0;
			baseState.ClimbPhase = climb.IsClimbing ? (byte)climb.Phase : (byte)0;
			baseState.ClimbTicks = climb.Ticks;
			baseState.IsFlying = isFlying;
			baseState.MotionTicks = motionTicks;
			baseState.MotionSpeed = motionSpeed;
			baseState.MotionYaw = motionYaw;

			return baseState;
		}

		/// <summary>
		/// Applies one tick of replicated input. Called from <c>KCCPlayer.OnReplicate</c>, once per
		/// replicated or replayed tick — not per frame, despite the upstream sample's name for it.
		/// </summary>
		public void SetInputs(ref KCCInputReplicateData inputs) => SetInputs(ref inputs, controlsSuppressed: false);

		/// <summary>
		/// Applies a tick's replicated input, optionally with the controls taken away.
		/// </summary>
		/// <remarks>
		/// With <paramref name="controlsSuppressed"/> set the move vector is zeroed, no jump,
		/// crouch or sprint is honoured and a jump requested on the previous tick is forgotten,
		/// but the facing is left where it was and the motor is still driven this tick. That is
		/// what a dead or incapacitated character needs: gravity and the ground keep acting on
		/// it, the player just cannot steer it. The virtual camera is still recorded so the aim
		/// origin is current the moment control returns.
		/// </remarks>
		/// <param name="inputs">This tick's replicated input.</param>
		/// <param name="controlsSuppressed">True to ignore the player's controls this tick.</param>
		public void SetInputs(ref KCCInputReplicateData inputs, bool controlsSuppressed)
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

			if (controlsSuppressed)
			{
				/* No look target either, so UpdateRotation leaves the facing where the motor has it. A look kept from the
				 * last controlled tick is a direction fixed in the world: it turned a stunned rider on a turning deck back
				 * to it every tick, undoing the deck's turn (the motor's platform turn), and it was state no reconcile
				 * restores. Off a deck it changes nothing: the facing already stands on that direction. */
				lookInputVector = Vector3.zero;
				moveInputVector = Vector3.zero;
				jumpRequested = false;
				crouchInputDown = false;
				sprintInputDown = false;
				ascendInputDown = false;
				descendInputDown = false;
				swimMoveVector = Vector3.zero;
				/* A stunned, frozen, mesmerised or dead climber loses its hold and falls (BeforeCharacterUpdate lets go): the
				 * hands are what these take away. Both peers reach this on the same tick (KCCPlayer.OnReplicate), so the drop
				 * is predicted like the rest. */
				controlsSuppressedThisTick = true;
				// Nor does a stunned charger keep charging.
				EndMotion();
				return;
			}
			controlsSuppressedThisTick = false;
			RefreshMount();
			bool mountFlies = mounted && mountMotion.Flies;

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

						// Crouching input. In flight crouch sinks instead (the reconciled flag decides, so both peers agree).
						bool crouchHeld = inputs.MoveFlags.IsFlagged(KCCMoveFlags.Crouch);
						// A rider does not crouch; on a flying mount Crouch descends (issue #109).
						crouchInputDown = crouchHeld && !isFlying && !mounted;
						descendInputDown = crouchHeld && (isFlying || mountFlies);

						// Sprinting input
						sprintInputDown = inputs.MoveFlags.IsFlagged(KCCMoveFlags.Sprint);

						// Swimming: jump held rises, and the move input in full 3D, the way the camera looks, dives.
						ascendInputDown = inputs.MoveFlags.IsFlagged(KCCMoveFlags.Ascend);
						swimMoveVector = inputs.CameraRotation * clampedInput;
						Vector3 cameraForward = inputs.CameraRotation * Vector3.forward;
						swimLookPitch = Mathf.Asin(Mathf.Clamp(cameraForward.y, -1f, 1f)) * Mathf.Rad2Deg;

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
			controlsSuppressedThisTick = false;
		}

		/// <summary>
		/// (Called by KinematicCharacterMotor during its update cycle)
		/// This is called before the character begins its movement update
		/// </summary>
		public void BeforeCharacterUpdate(float deltaTime)
		{
			/* Before this tick's move the motor still holds the last tick's solved velocity and grounding, both reconciled
			 * motor state, so a replay reads exactly what the first run read: no field of its own on the wire. */
			motionSlowedLastTick = motionTicks > 0 && IsMotionSlowed(Motor.BaseVelocity, Motor.GroundingStatus);

			// On the first tick after spawn or teleport, seed the motor ground-probe
			// history so it uses a full capsule-radius probe instead of the 0.005f
			// minimum. Without this, characters appear airborne for 1-3 frames.
			if (!hasDoneInitialGroundProbe)
			{
				Motor.LastMovementIterationFoundAnyGround = true;
				hasDoneInitialGroundProbe = true;
			}
			// The mount, and the slope it walks up, before the motor solves the ground this tick.
			RefreshMount();
			// In flight there is no water to swim in and nothing to take hold of.
			if (isFlying)
			{
				return;
			}
			UpdateWater();
			if (climb.IsClimbing && controlsSuppressedThisTick)
			{
				ReleaseClimb(Constants.Character.ClimbRegrabSeconds, deltaTime);
			}
			UpdateClimbAttach(deltaTime);
		}

		/// <summary>
		/// Reads the water at the character and starts or stops swimming: in at <see cref="Constants.Character.SwimEnterSubmersion"/>
		/// of its height under the surface, out under <see cref="Constants.Character.SwimExitSubmersion"/>, so wading at
		/// the depth between never flickers between the two.
		/// </summary>
		private void UpdateWater()
		{
			Vector3 feet = Motor.TransientPosition;
			// The water the feet stand in or over, not the highest over the place: a lava tube's river on the level over a cave's
			// stream is another place's.
			water = WaterQuery.Sample(Motor.gameObject.scene, feet.x, feet.y, feet.z, WaterTick);
			float submersion = water.Present ? (water.Surface - feet.y) / Mathf.Max(0.1f, FullCapsuleHeight) : 0f;
			if (!isSwimming)
			{
				// Not while rising: a swimmer that has just jumped out is still deep for a tick or two.
				if (CanSwim && !climb.IsClimbing && submersion >= Constants.Character.SwimEnterSubmersion && Motor.BaseVelocity.y <= 0.5f)
				{
					EnterSwimming();
				}
			}
			else if (submersion < Constants.Character.SwimExitSubmersion && climbOutTicks == 0)
			{
				ExitSwimming();
			}
		}

		private void EnterSwimming()
		{
			// Deep water takes a rider off its mount (issue #109): the mount was never more than what it looks like.
			DismountForMovement();
			isSwimming = true;
			jumpRequested = false;
			Motor.SetGroundSolvingActivation(false);
			Motor.ForceUnground();
			// Crouch means dive in the water: the swimmer stands at full height.
			if (isCrouching)
			{
				ApplyCapsuleDimensions(crouched: false);
				if (Motor.CharacterOverlap(Motor.TransientPosition, Motor.TransientRotation, probedColliders, Motor.CollidableLayers, QueryTriggerInteraction.Ignore) > 0)
				{
					ApplyCapsuleDimensions(crouched: true);
				}
				else
				{
					isCrouching = false;
				}
			}
		}

		private void ExitSwimming()
		{
			isSwimming = false;
			climbOutTicks = 0;
			Motor.SetGroundSolvingActivation(true);
		}

		/// <summary>How far out of a climbable's line this character hangs: its body's radius and a little air.</summary>
		private float ClimbHang => Motor.Capsule.radius + Constants.Character.ClimbHangClearance;

		/// <summary>The climbable this character holds, from its own scene's list (<see cref="SceneClimbables"/>).</summary>
		private bool TryGetClimbable(out SceneClimbable climbable)
		{
			return SceneClimbables.TryGet(Motor.gameObject.scene, climb.Index, out climbable);
		}

		/// <summary>
		/// Takes hold of a ladder, rope ladder or rope this character is moving at (<see cref="ClimbingRules.TryAttach"/>):
		/// from the floor at its foot or anywhere along it, or walking off its top's edge from the upper floor.
		/// </summary>
		/// <remarks>
		/// Runs before the motor moves, on every simulated and replayed tick. It reads only reconciled state (the feet,
		/// grounding, the wait after letting go), this tick's input and the scene's climbables, which never change while
		/// it is loaded, so the server and the owner take hold on the same tick. Nearest wins; ties go to the lower index.
		/// </remarks>
		private void UpdateClimbAttach(float deltaTime)
		{
			if (!CanClimb || controlsSuppressedThisTick || climb.IsClimbing || climb.Ticks > 0 || moveInputVector.sqrMagnitude < ClimbingRules.MinIntent * ClimbingRules.MinIntent)
			{
				return;
			}
			Vector3 feet = Motor.TransientPosition;
			UnityEngine.SceneManagement.Scene scene = Motor.gameObject.scene;
			if (!SceneClimbables.Near(scene, feet, ClimbingRules.QueryReach, climbCandidates))
			{
				return;
			}
			bool grounded = Motor.GroundingStatus.IsStableOnGround;
			float hang = ClimbHang;
			int best = -1;
			ClimbPhase bestPhase = ClimbPhase.None;
			float bestDistance = float.MaxValue;
			for (int i = 0; i < climbCandidates.Count; i++)
			{
				int index = climbCandidates[i];
				if (SceneClimbables.TryGet(scene, index, out SceneClimbable candidate) &&
					ClimbingRules.TryAttach(in candidate, feet, moveInputVector, grounded, hang, out ClimbPhase phase, out float distance) &&
					distance < bestDistance)
				{
					best = index;
					bestPhase = phase;
					bestDistance = distance;
				}
			}
			if (best >= 0)
			{
				EnterClimbing(best, bestPhase, deltaTime);
			}
		}

		private void EnterClimbing(int index, ClimbPhase phase, float deltaTime)
		{
			// Taking hold of a ladder or a rope gets a rider off its mount first (issue #109).
			DismountForMovement();
			if (isSwimming)
			{
				// Out of the water by the ladder: a swimmer reaching one takes hold of it.
				isSwimming = false;
				climbOutTicks = 0;
			}
			ClimbingRules.Attach(ref climb, index, phase, deltaTime);
			// A jump pressed walking into the ladder would let go of it again at once.
			jumpRequested = false;
			Motor.SetGroundSolvingActivation(false);
			Motor.ForceUnground();
			StandUpIfRoom();
			SyncMovementCollisions();
		}

		/// <summary>Lets go of the climbable, and waits <paramref name="regrabSeconds"/> before taking hold of one again.</summary>
		private void ReleaseClimb(float regrabSeconds, float deltaTime)
		{
			ClimbingRules.Release(ref climb, regrabSeconds, deltaTime);
			Motor.SetGroundSolvingActivation(!isSwimming && !isFlying);
			SyncMovementCollisions();
		}

		/// <summary>Stands a crouching character up if there is room: a climber, like a swimmer, is at full height.</summary>
		private void StandUpIfRoom()
		{
			if (!isCrouching)
			{
				return;
			}
			ApplyCapsuleDimensions(crouched: false);
			if (Motor.CharacterOverlap(Motor.TransientPosition, Motor.TransientRotation, probedColliders, Motor.CollidableLayers, QueryTriggerInteraction.Ignore) > 0)
			{
				ApplyCapsuleDimensions(crouched: true);
			}
			else
			{
				isCrouching = false;
			}
		}

		/// <summary>
		/// A climber's velocity (<see cref="ClimbingRules.Step"/>): pushing into the climbable climbs it, pulling away goes
		/// down, pushing along a ladder shuffles across it, Crouch slides down, Jump lets go and pushes off. No gravity, no
		/// ground.
		/// </summary>
		/// <remarks>
		/// Climbing speed follows Move Speed (and <see cref="ClimbSpeedTemplate"/> where one is set), so a slow slows a
		/// climb as it slows a walk. The floor probe going down is a ray in the character's own physics scene (world
		/// scenes load with local physics), against the fixed ground layers: the same answer on every peer.
		/// </remarks>
		private void UpdateClimbMovement(ref Vector3 currentVelocity, float deltaTime)
		{
			TickJumpRequestTimer(deltaTime);
			if (!TryGetClimbable(out SceneClimbable climbable))
			{
				// Its scene's climbables are gone (unloaded, or a different scene): let go and fall.
				ReleaseClimb(0f, deltaTime);
				return;
			}

			Vector3 feet = Motor.TransientPosition;
			// Into the climbable climbs, away goes down, along it shuffles (ClimbingRules.Axes): the push that took hold climbs on.
			ClimbingRules.Axes(in climbable, climb.Phase, feet, moveInputVector, out float up, out float side);
			ClimbIntent intent = new ClimbIntent
			{
				Up = up,
				Side = side,
				Move = moveInputVector,
				Jump = jumpRequested,
				Slide = crouchInputDown,
			};
			// A jump is a let-go, and nothing else on a climbable: it never waits to fire on landing.
			jumpRequested = false;
			if (climb.Phase == ClimbPhase.Holding && (intent.Up < 0f || intent.Slide))
			{
				// Feet on the floor going down: off onto it, wherever the climb's foot was set.
				intent.FloorBelow = Motor.PhysicsScene.Raycast(feet + Vector3.up * 0.2f, Vector3.down, out RaycastHit floor, 0.3f, Motor.StableGroundLayers, QueryTriggerInteraction.Ignore) &&
					Vector3.Angle(floor.normal, Vector3.up) <= Motor.MaxStableSlopeAngle;
			}

			ClimbStep step = ClimbingRules.Step(in climbable, ref climb, feet, in intent, ClimbHang, ClimbSpeedScale(), deltaTime);
			currentVelocity = step.Velocity;
			// This tick's move already follows the phase the step just chose (the motor moves after UpdateVelocity).
			SyncMovementCollisions();
			if (step.Released)
			{
				Motor.SetGroundSolvingActivation(!isSwimming);
				if (step.Jumped)
				{
					Motor.ForceUnground();
				}
			}
		}

		/// <summary>Move Speed's share, times Climb Speed's where the character has one; 1 with neither.</summary>
		private float ClimbSpeedScale()
		{
			EnsureCached();
			float scale = 1f;
			if (cachedAttributeController != null)
			{
				if (MoveSpeedTemplate != null && cachedAttributeController.TryGetAttribute(MoveSpeedTemplate, out CharacterAttribute moveSpeed))
				{
					scale *= moveSpeed.FinalValueAsPct;
				}
				if (ClimbSpeedTemplate != null && cachedAttributeController.TryGetAttribute(ClimbSpeedTemplate, out CharacterAttribute climbSpeed))
				{
					scale *= climbSpeed.FinalValueAsPct;
				}
			}
			return scale;
		}

		/// <summary>
		/// The end of a climbing tick: the animator hears that the character climbs, and how fast up or down. Without climb
		/// clips (none exist yet) it stands as if on the ground, upright and still, facing the climbable.
		/// </summary>
		private void AfterClimbingUpdate()
		{
			EnsureCached();
			if (cachedAnimationController != null)
			{
				float rate = Mathf.Clamp(Motor.Velocity.y / Constants.Character.ClimbSpeed, -2f, 2f);
				cachedAnimationController.SetCrouching(false);
				cachedAnimationController.SetGrounded(true);
				cachedAnimationController.SetSpeed(0f);
				cachedAnimationController.SetSwimming(false, 0f);
				cachedAnimationController.SetClimbing(true, rate);
			}
		}

		/// <summary>
		/// Largest angle, in degrees, between the rotation and the upright heading it is turning to that counts as arrived.
		/// Below Quaternion.Angle's own resolution (about 0.1 degrees), so the last step of a turn is invisible.
		/// </summary>
		public const float SettleDegrees = 0.05f;

		/// <summary>Speed, in metres per second, below which a character slowing to a stop has stopped.</summary>
		public const float SettleSpeed = 1e-4f;

		/// <summary>
		/// The velocity, or exactly zero when the character is coming to rest and is slower than <see cref="SettleSpeed"/>.
		/// </summary>
		/// <remarks>
		/// The slow-down is an exponential approach too: without this the velocity of a character that let go of the keys took
		/// seconds of ever-smaller float noise to reach zero, and each of those ticks was a reconcile that could not be skipped.
		/// A tenth of a millimetre per second is far below anything drawn; both peers run the same arithmetic.
		/// </remarks>
		public static Vector3 SettleVelocity(Vector3 velocity, Vector3 target)
		{
			return target == Vector3.zero && velocity.sqrMagnitude < SettleSpeed * SettleSpeed ? Vector3.zero : velocity;
		}

		/// <summary>
		/// The rotation, or exactly the upright heading it is turning to once it is within <see cref="SettleDegrees"/>.
		/// </summary>
		/// <remarks>
		/// The turn is an exponential approach (a Slerp toward the heading every tick, then a Slerp back to upright), and in
		/// floats it never lands: a character standing still kept a rotation that changed in its last bits on every tick. The
		/// reconcile carries the rotation exactly, so that noise made every reconcile of an idle player differ from the last
		/// and none could be skipped as a repeat (CharacterPredictionController quiet reconciles). Settling exactly on the
		/// heading gives a fixed point instead. Server and owner run this same arithmetic on the same input, so it is no
		/// less deterministic than the approach it ends. Only when the character turns upright (no bonus orientation), the
		/// only case with a single heading to settle on.
		/// </remarks>
		public static Quaternion SettleRotation(Quaternion rotation, bool hasHeading, Quaternion heading, bool upright)
		{
			/* Without a heading there is nothing to settle on: deriving one from the rotation itself could differ from it in
			 * the last bit and keep it moving. */
			if (!upright || !hasHeading)
			{
				return rotation;
			}
			return Quaternion.Angle(rotation, heading) < SettleDegrees ? heading : rotation;
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
						if (climb.IsClimbing && TryGetClimbable(out SceneClimbable climbable))
						{
							// Upright, facing the climbable (or the way off it once over the top); the camera does not turn a climber.
							Vector3 facing = ClimbingRules.Facing(in climbable, climb.Phase, Motor.TransientPosition);
							currentRotation = Quaternion.Slerp(Motor.TransientRotation, Quaternion.LookRotation(facing, Vector3.up), 1f - Mathf.Exp(-ClimbTurnSharpness * deltaTime));
							break;
						}

						bool hasHeading = false;
						Quaternion heading = Quaternion.identity;
						if (motionTicks > 0)
						{
							// A rush faces where it is going, at once: the camera does not turn a charging character.
							heading = Quaternion.LookRotation(MotionDirectionOf(motionYaw), Vector3.up);
							hasHeading = true;
							currentRotation = heading;
						}
						else if (lookInputVector.sqrMagnitude > 0f && OrientationSharpness > 0f)
						{
							float targetRotationY = Mathf.Atan2(lookInputVector.x, lookInputVector.z) * Mathf.Rad2Deg;

							Quaternion targetQuaternion = Quaternion.Euler(0.0f, targetRotationY, 0.0f);

							if (Mathf.Abs(Quaternion.Angle(Motor.TransientRotation, targetQuaternion) - 180f) <= 3f)
							{
								//Log.Debug("180 degree detected");
								targetRotationY -= 10f;
							}

							heading = Quaternion.Euler(0.0f, targetRotationY, 0.0f);
							hasHeading = true;
							currentRotation = Quaternion.Slerp(Motor.TransientRotation, heading, 1 - Mathf.Exp(-OrientationSharpness * deltaTime));
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
						currentRotation = SettleRotation(currentRotation, hasHeading, heading, BonusOrientationMethod == BonusOrientationMethod.None);
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
						/* A body nobody is steering, below a cloud world's death altitude, hangs where the deck took it. The
						 * server kills whoever reaches that depth (CharacterSystem.CloudWorld), and a corpse left to gravity
						 * would fall for as long as its player sat at the death dialog: at terminal velocity it leaves the
						 * range network positions are written in within minutes. Both peers reach this on the same tick: the
						 * suppression comes from replicated health and state (KCCPlayer.OnReplicate), the height from the
						 * motor, and the deck from the scene, which the client keeps too. */
						if (controlsSuppressedThisTick &&
							SceneCloudWorld.IsLost(Motor.gameObject.scene, Motor.TransientPosition))
						{
							currentVelocity = Vector3.zero;
							break;
						}

						AbilityType abilityType = AbilityType.None;

						float moveInputMagnitude = moveInputVector.sqrMagnitude;

						EnsureCached();

						// Determine ability state
						if (cachedAbilityController != null)
						{
							abilityType = cachedAbilityController.GetCurrentAbilityType();
						}

						if (isFlying)
						{
							UpdateFlyMovement(ref currentVelocity, deltaTime);
							break;
						}

						if (climb.IsClimbing)
						{
							UpdateClimbMovement(ref currentVelocity, deltaTime);
							break;
						}

						if (isSwimming)
						{
							UpdateSwimMovement(ref currentVelocity, abilityType, deltaTime);
							break;
						}

						// A flying mount off stable ground flies (issue #109); on the ground it walks like any other mount.
						if (IsMountFlying && motionTicks == 0)
						{
							UpdateMountFlight(ref currentVelocity, deltaTime);
							break;
						}

						// An ability rush drives the character instead of its input (BeginMotion).
						if (motionTicks > 0)
						{
							UpdateMotionMovement(ref currentVelocity, deltaTime);
							break;
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
		/// One tick of an ability rush: along the rush heading at the rush speed, following the ground's slope when standing
		/// on it, and keeping gravity when not, so a charge off a ledge still falls.
		/// </summary>
		private void UpdateMotionMovement(ref Vector3 currentVelocity, float deltaTime)
		{
			Vector3 direction = MotionDirectionOf(motionYaw);
			float speed = motionSpeed / 100f;
			if (Motor.GroundingStatus.IsStableOnGround)
			{
				currentVelocity = Motor.GetDirectionTangentToSurface(direction, Motor.GroundingStatus.GroundNormal) * speed;
			}
			else
			{
				float vertical = Vector3.Dot(currentVelocity, Motor.CharacterUp);
				currentVelocity = direction * speed + Motor.CharacterUp * vertical + Constants.Character.Gravity * deltaTime;
			}
		}

		/// <summary>Whether the last tick's motion, set before this tick's move (see BeforeCharacterUpdate), was already slowed.</summary>
		private bool motionSlowedLastTick;

		/// <summary>
		/// Whether a solved velocity is under half the rush speed, measured ALONG the surface the character stands on (across
		/// the horizontal in the air), so climbing a slope is not mistaken for being stopped.
		/// </summary>
		private bool IsMotionSlowed(Vector3 velocity, CharacterGroundingReport grounding)
		{
			Vector3 plane = grounding.IsStableOnGround ? grounding.GroundNormal : Motor.CharacterUp;
			return Vector3.ProjectOnPlane(velocity, plane).magnitude < 0.5f * (motionSpeed / 100f);
		}

		/// <summary>
		/// Counts a rush down after the motor has moved, and ends it early when something stopped it: the motor's solved
		/// velocity along the ground stayed under half the rush speed for two ticks running (a wall, a body). Pure motor
		/// state on both peers.
		/// </summary>
		/// <remarks>
		/// Two ticks, not one ("Short Charge stops on slopes"): crossing sloped ground the capsule meets triangle
		/// edges whose contact normal is steeper than either face, the motor rates that one hit unstable and projects the
		/// velocity on it, and the next tick it is back on stable ground at full speed. A single slowed tick ended the rush
		/// there. A wall or a body slows it every tick, so it still stops, one tick later.
		/// </remarks>
		private void TickMotion()
		{
			if (motionTicks == 0)
			{
				return;
			}
			if (climb.IsClimbing || isSwimming || isFlying)
			{
				motionTicks = 0;
				return;
			}
			--motionTicks;
			if (motionSlowedLastTick && IsMotionSlowed(Motor.BaseVelocity, Motor.GroundingStatus))
			{
				motionTicks = 0;
			}
			if (motionTicks == 0)
			{
				SettleAfterMotion();
			}
		}

		/// <summary>A rush that has ended leaves no more than a run's worth of speed behind, so the character does not sail on.</summary>
		private void SettleAfterMotion()
		{
			Vector3 up = Motor.CharacterUp;
			Vector3 velocity = Motor.BaseVelocity;
			Vector3 vertical = Vector3.Project(velocity, up);
			Vector3 across = Vector3.ClampMagnitude(velocity - vertical, Constants.Character.RunSpeed);
			Motor.BaseVelocity = across + vertical;
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

			// Sprint is a held flag, so it has to be paid for on every tick it is down. Holding it
			// with no stamina left falls through to the run-speed path rather than standing still
			// or paying a cost the character cannot cover.
			bool sprinting = false;
			if (sprintInputDown && (SprintSpeedTemplate != null || Unmetered) && moveInputMagnitude > 0f)
			{
				float currentStaminaCost = Constants.Character.SprintStaminaCost * deltaTime;
				if (HasStamina(currentStaminaCost))
				{
					SpendStamina(currentStaminaCost);
					sprinting = true;
				}
			}

			float targetSpeed = ResolveGroundSpeed(isCrouching, sprinting, moveInputMagnitude, cachedAttributeController,
				mounted ? mountMotion.GroundSpeedMultiplier : 1f);

			Vector3 targetMovementVelocity = reorientedInput * targetSpeed;

			// Smooth movement Velocity
			currentVelocity = Vector3.Lerp(currentVelocity, targetMovementVelocity, StableMovementSharpness * deltaTime);
			currentVelocity = SettleVelocity(currentVelocity, targetMovementVelocity);
		}

		/// <summary>
		/// Resolves the ground speed for this tick from the character's stance and the two speed
		/// attributes, with the exploit cap applied to the result.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Move Speed scales every ground speed, including sprint.</b> It used to be an
		/// <c>else if</c> alternative to the sprint branch, which made the two mutually exclusive:
		/// a <c>Minor Increase Move Speed</c> buff was worth +30% walking and exactly 0% sprinting,
		/// and a <c>Mock Slow Debuff</c> did not slow a sprinting character at all. Sprint Speed and
		/// Move Speed are separate attributes with separate ledgers, so both are read and multiplied.
		/// </para>
		/// <para>
		/// Crouch is deliberately unscaled — slow crouch movement is a stance rule, not a speed
		/// attribute, and no speed buff has ever applied to it.
		/// </para>
		/// <para>
		/// Takes the attribute controller explicitly rather than reading the cached field, so the
		/// speed decision can be exercised without running a character through the motor.
		/// </para>
		/// </remarks>
		/// <param name="isCrouching">Whether the character is in the crouch stance.</param>
		/// <param name="isSprinting">
		/// Whether sprint is actually being paid for this tick. <c>false</c> when the sprint key is
		/// held but stamina is exhausted, and the character falls back to
		/// <see cref="Constants.Character.RunSpeed"/> — still scaled by Move Speed.
		/// </param>
		/// <param name="moveInputMagnitude">Magnitude of the move input; sprint is stationary-only.</param>
		/// <param name="attributes">
		/// Attribute source for this character, or <c>null</c> for a character with no attribute
		/// controller (nothing can scale the base speeds in that case).
		/// </param>
		/// <param name="mountMultiplier">
		/// The ridden mount's <see cref="MountMotion.GroundSpeedMultiplier"/> (issue #109), 1 on foot. It multiplies with Move
		/// Speed, under the same cap.
		/// </param>
		internal float ResolveGroundSpeed(
			bool isCrouching,
			bool isSprinting,
			float moveInputMagnitude,
			ICharacterAttributeController attributes,
			float mountMultiplier = 1f)
		{
			if (isCrouching)
			{
				return Constants.Character.CrouchSpeed;
			}

			float moveSpeedMultiplier = mountMultiplier > 0f ? mountMultiplier : 1f;
			if (MoveSpeedTemplate != null &&
				attributes != null &&
				attributes.TryGetAttribute(MoveSpeedTemplate, out CharacterAttribute moveSpeedModifier))
			{
				moveSpeedMultiplier *= moveSpeedModifier.FinalValueAsPct;
			}

			float targetSpeed = Constants.Character.RunSpeed * moveSpeedMultiplier;

			if (isSprinting && moveInputMagnitude > 0f)
			{
				// Sprint Speed is the sprint/run ratio (authored at 100 = the stock 6.0 m/s).
				// Move Speed has already been folded in above and applies to this too. The attribute
				// is only a scale on the ratio — a character whose controller has no Sprint Speed
				// entry still sprints, they just sprint at the stock ratio.
				float sprintMultiplier = 1f;
				if (SprintSpeedTemplate != null &&
					attributes != null &&
					attributes.TryGetAttribute(SprintSpeedTemplate, out CharacterAttribute sprintSpeedModifier))
				{
					sprintMultiplier = sprintSpeedModifier.FinalValueAsPct;
				}

				float sprintRatio = Constants.Character.SprintSpeed / Constants.Character.RunSpeed;
				targetSpeed = Constants.Character.RunSpeed * sprintRatio * sprintMultiplier * moveSpeedMultiplier;
			}

			// Speed cap — enforced identically on client and server through the
			// shared deterministic prediction pipeline. Prevents super-speed exploits
			// from stacking movement buffs or attribute calculation bugs.
			// Because this runs in shared code on both sides, a modified client that
			// removes the clamp will see a brief local misprediction that snaps back
			// on the next reconcile pass. Only reachable by stacking several buffs:
			// 18 m/s is 3x the stock sprint, and this is the ceiling the resolver
			// guarantees rather than the sum of every modifier available.
			const float MaxAllowedSpeed = Constants.Character.SprintSpeed * 3.0f;
			return Mathf.Min(targetSpeed, MaxAllowedSpeed);
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
		/// Game-master flight (<see cref="IsFlying"/>): velocity eases toward the camera's way in full 3D, jump up, crouch down.
		/// </summary>
		private void UpdateFlyMovement(ref Vector3 currentVelocity, float deltaTime)
		{
			// The way the camera looks, in full 3D; jump rises, crouch sinks; sprint is free. No gravity and no jump.
			jumpRequested = false;
			Vector3 direction = swimMoveVector;
			if (ascendInputDown)
			{
				direction += Vector3.up;
			}
			if (descendInputDown)
			{
				direction += Vector3.down;
			}
			if (direction.sqrMagnitude > 1f)
			{
				direction.Normalize();
			}
			float speed = FlySpeed * (sprintInputDown ? FlySprintMultiplier : 1f);
			currentVelocity = Vector3.Lerp(currentVelocity, direction * speed, 1f - Mathf.Exp(-FlySharpness * deltaTime));
		}

		/// <summary>
		/// A flying mount's velocity (issue #109: flight costs stamina): toward the camera's way in full 3D,
		/// held Jump climbing and Crouch descending, sprint faster at twice the cost. Unlike game-master flight it collides with
		/// everything, and touching stable ground lands it (the motor's own ground probe, so no state of its own).
		/// </summary>
		/// <remarks>
		/// Every tick of flight costs <see cref="MountMotion.FlightStaminaPerSecond"/> stamina. With none left the mount GLIDES:
		/// it can no longer climb, sinks at <see cref="MountMotion.GlideSinkSpeed"/> and keeps
		/// <see cref="MountMotion.GlideSpeedShare"/> of its speed going forward, until it lands and the stamina comes back. The
		/// cost is paid in the motor on both peers, like sprinting's.
		/// </remarks>
		private void UpdateMountFlight(ref Vector3 currentVelocity, float deltaTime)
		{
			TickJumpRequestTimer(deltaTime);
			jumpRequested = false;
			timeSinceLastAbleToJump = float.MaxValue;

			bool sprinting = sprintInputDown;
			float cost = Mathf.Max(0f, mountMotion.FlightStaminaPerSecond) * deltaTime * (sprinting ? 2f : 1f);
			bool powered = cost <= 0f || HasStamina(cost);
			if (powered && cost > 0f)
			{
				SpendStamina(cost);
			}
			Vector3 target = MountFlightTarget(mountMotion, swimMoveVector, ascendInputDown, descendInputDown, sprinting, powered);
			currentVelocity = Vector3.Lerp(currentVelocity, target, 1f - Mathf.Exp(-FlySharpness * deltaTime));
		}

		/// <summary>
		/// The velocity a flying mount steers toward (pure, so it is tested on its own): <paramref name="look"/> is the move input
		/// turned the camera's way in 3D; Jump held climbs and Crouch held descends. Unpowered (out of stamina) it glides: level
		/// steering only, at a share of the speed, sinking.
		/// </summary>
		internal static Vector3 MountFlightTarget(in MountMotion motion, Vector3 look, bool ascend, bool descend, bool sprint, bool powered)
		{
			float speed = Mathf.Max(0f, motion.FlySpeed);
			if (!powered)
			{
				Vector3 level = new Vector3(look.x, 0f, look.z);
				if (level.sqrMagnitude > 1f)
				{
					level.Normalize();
				}
				Vector3 glide = level * (speed * Mathf.Clamp01(motion.GlideSpeedShare));
				glide.y = -Mathf.Max(0.5f, motion.GlideSinkSpeed) * (descend ? 2f : 1f);
				return glide;
			}
			Vector3 direction = look;
			if (ascend)
			{
				direction += Vector3.up;
			}
			if (descend)
			{
				direction += Vector3.down;
			}
			if (direction.sqrMagnitude > 1f)
			{
				direction.Normalize();
			}
			float sprintMultiplier = sprint && motion.FlySprintMultiplier > 0f ? motion.FlySprintMultiplier : 1f;
			// The same exploit ceiling the ground speed has, three stock sprints.
			return direction * Mathf.Min(speed * sprintMultiplier, Constants.Character.SprintSpeed * 3f);
		}

		/// <summary>
		/// A swimmer's velocity: floating at the surface or diving free in three dimensions, carried by the current, no
		/// gravity and no ground.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Surface and dive.</b> At the surface the swimmer moves on the level and a spring holds it at its float line,
		/// head and shoulders out. Holding Jump rises and Crouch dives; looking more than
		/// <see cref="Constants.Character.SwimDiveLookDegrees"/> down while moving dives along the look, and once under, the
		/// swimmer goes wherever it looks. It cannot swim up out of the water: past the float line, up is cut.
		/// </para>
		/// <para>
		/// <b>Out.</b> A jump at the surface leaves the water at once (the motor would otherwise read the swimmer as still
		/// deep for the tick or two it takes to rise past the exit mark). A ledge near the surface is climbed by
		/// <see cref="OnMovementHit"/>, which starts <see cref="climbOutTicks"/>.
		/// </para>
		/// </remarks>
		private void UpdateSwimMovement(ref Vector3 currentVelocity, AbilityType abilityType, float deltaTime)
		{
			TickJumpRequestTimer(deltaTime);
			timeSinceLastAbleToJump = 0f;

			if (climbOutTicks > 0)
			{
				// Up the ledge and over it: high enough in the climb's ticks, and forward onto the top.
				float climbSpeed = (Constants.Character.SwimClimbOutLedgeMetres + 0.6f) / (Constants.Character.SwimClimbOutTicks * deltaTime);
				currentVelocity = Vector3.up * climbSpeed + Motor.CharacterForward * 1.5f;
				return;
			}

			float floatDepth = Constants.Character.SwimFloatSubmersion * FullCapsuleHeight;
			// Positive below the float line, negative above it.
			float belowFloat = water.Present ? (water.Surface - Motor.TransientPosition.y) - floatDepth : 0f;
			bool atSurface = belowFloat < 0.35f;

			// Speed: the Swim Speed attribute scales it; sprint is paid for in stamina every tick it is held, as on land.
			float speed = Constants.Character.SwimSpeed;
			if (SwimSpeedTemplate != null && cachedAttributeController != null &&
				cachedAttributeController.TryGetAttribute(SwimSpeedTemplate, out CharacterAttribute swimSpeedModifier))
			{
				speed *= swimSpeedModifier.FinalValueAsPct;
			}
			bool moving = moveInputVector.sqrMagnitude > 0f;
			if (sprintInputDown && moving)
			{
				float cost = Constants.Character.SwimSprintStaminaCost * deltaTime;
				if (HasStamina(cost))
				{
					SpendStamina(cost);
					speed *= Constants.Character.SwimSprintSpeed / Constants.Character.SwimSpeed;
				}
			}
			if (water.Body == WaterBody.Lava)
			{
				speed *= LavaSwimSpeedShare;
			}
			speed = Mathf.Min(speed, Constants.Character.SwimSprintSpeed * 3f);

			// Where to: on the level at the surface, along the look once under or diving.
			bool lookingDown = swimLookPitch < -Constants.Character.SwimDiveLookDegrees;
			Vector3 wish = (!atSurface || (moving && lookingDown)) ? swimMoveVector : moveInputVector;
			float vertical = (ascendInputDown ? 1f : 0f) - (crouchInputDown ? 1f : 0f);
			Vector3 target = wish * speed + Vector3.up * (vertical * Constants.Character.SwimVerticalSpeed);

			if (atSurface && vertical == 0f && wish.y > -0.1f)
			{
				// Floating: held at the line, head and shoulders out.
				target.y = Mathf.Clamp(belowFloat * SwimBuoyancy, -2f, 2f);
			}
			else if (!atSurface && vertical == 0f && !moving)
			{
				// Still under water, a swimmer's own air lifts it, slowly.
				target.y = 0.4f;
			}
			if (belowFloat <= 0f && target.y > 0f)
			{
				// Never up out of the water by swimming: only a jump leaves it.
				target.y = 0f;
			}

			// The current carries the swimmer.
			target += new Vector3(water.Current.x, 0f, water.Current.y);

			currentVelocity = Vector3.Lerp(currentVelocity, target, 1f - Mathf.Exp(-SwimSharpness * deltaTime));

			if (jumpRequested && atSurface && abilityType == AbilityType.None && HasStamina(Constants.Character.JumpStaminaCost))
			{
				// Out of the water: up and away, swimming no longer.
				SpendStamina(Constants.Character.JumpStaminaCost);
				currentVelocity.y = Constants.Character.JumpUpSpeed * 0.75f;
				currentVelocity += moveInputVector * JumpScalableForwardSpeed;
				jumpRequested = false;
				ExitSwimming();
				Motor.ForceUnground();
			}
		}

		/// <summary>
		/// The jump request's age, saturating instead of accumulating forever.
		/// </summary>
		/// <remarks>
		/// This starts at float.MaxValue, where adding deltaTime is a no-op, so the field is
		/// constant and costs nothing on the wire — until the first jump resets it to 0, after
		/// which it changed every tick for the rest of the session and put 4 bytes in every
		/// reconcile (120 B/s per player) to express "still much greater than the grace window".
		/// The only reader compares it against JumpPreGroundingGraceTime, so anything past that
		/// is indistinguishable; parking it at MaxValue restores the quiet state.
		/// </remarks>
		private void TickJumpRequestTimer(float deltaTime)
		{
			if (timeSinceJumpRequested < float.MaxValue)
			{
				timeSinceJumpRequested += deltaTime;
				if (timeSinceJumpRequested > JumpPreGroundingGraceTime + 1f)
				{
					timeSinceJumpRequested = float.MaxValue;
				}
			}
		}

		/// <summary>
		/// A body with no character at all (the world test bed's walker drives this controller with no server, no
		/// character and no attributes) has no stamina to pay with, so nothing is charged and nothing is refused. Every
		/// player has its character from <c>PlayerCharacter.OnAwake</c>, on every peer, so this is never so in play; a
		/// character whose attributes lack stamina is still refused, as before.
		/// </summary>
		private bool Unmetered => Character == null && cachedAttributeController == null;

		/// <summary>Whether <paramref name="cost"/> stamina can be paid now (always, unmetered).</summary>
		private bool HasStamina(float cost)
		{
			if (Unmetered)
			{
				return true;
			}
			return cachedAttributeController != null &&
				cachedAttributeController.TryGetStaminaAttribute(out CharacterResourceAttribute stamina) &&
				stamina.CurrentValue >= cost;
		}

		/// <summary>Pays <paramref name="cost"/> stamina, after <see cref="HasStamina"/> said it could be.</summary>
		private void SpendStamina(float cost)
		{
			if (cachedAttributeController != null && cachedAttributeController.TryGetStaminaAttribute(out CharacterResourceAttribute stamina))
			{
				stamina.Consume(cost);
			}
		}

		/// <summary>
		/// Handles jump requests including stamina consumption, ground grace period, and jump force application.
		/// </summary>
		private void HandleJumping(ref Vector3 currentVelocity, AbilityType abilityType, float deltaTime)
		{
			TickJumpRequestTimer(deltaTime);
			if (jumpRequested)
			{
				// See if we actually are allowed to jump
				if (HasStamina(Constants.Character.JumpStaminaCost) &&
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
					if (JumpSpeedTemplate != null && cachedAttributeController != null &&
						cachedAttributeController.TryGetAttribute(JumpSpeedTemplate, out CharacterAttribute jumpSpeedModifier))
					{
						jumpSpeed *= jumpSpeedModifier.FinalValueAsPct;
					}
					if (mounted)
					{
						// A mount jumps its own height (a frog's 2.5×), and a leaper adds its leap along the steering (issue #109).
						jumpSpeed *= mountMotion.JumpSpeedMultiplier;
					}
					currentVelocity += (jumpDirection * jumpSpeed) - Vector3.Project(currentVelocity, Motor.CharacterUp);
					currentVelocity += (moveInputVector * JumpScalableForwardSpeed);
					if (mounted && mountMotion.LeapSpeed > 0f)
					{
						currentVelocity += Vector3.ClampMagnitude(moveInputVector, 1f) * mountMotion.LeapSpeed;
					}

					// Consume stamina when jumping
					SpendStamina(Constants.Character.JumpStaminaCost);

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
			// The wait before taking hold again runs down while not climbing (swimming or not): reconciled, so a replay counts alike.
			ClimbingRules.TickCooldown(ref climb);
			// An ability rush counts down (and stops when blocked) on every peer that simulates the motor.
			TickMotion();

			switch (CurrentCharacterState)
			{
				case KCCCharacterState.Default:
					{
						if (climb.IsClimbing)
						{
							AfterClimbingUpdate();
							break;
						}

						if (isSwimming)
						{
							AfterSwimmingUpdate();
							break;
						}

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
							// In flight the motor solves no ground, but the figure stands as on the ground rather than falls.
							cachedAnimationController.SetGrounded(isFlying || IsMountFlying || Motor.GroundingStatus.FoundAnyGround);

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
							cachedAnimationController.SetSwimming(false, 0f);
							cachedAnimationController.SetClimbing(false, 0f);
						}
						break;
					}
			}
		}

		/// <summary>
		/// The end of a swimming tick: the climb counts down, a jump request that waited too long lapses, and the
		/// animator hears how the swimmer lies in the water.
		/// </summary>
		private void AfterSwimmingUpdate()
		{
			if (climbOutTicks > 0)
			{
				climbOutTicks--;
			}
			if (jumpRequested && timeSinceJumpRequested > JumpPreGroundingGraceTime)
			{
				jumpRequested = false;
			}
			EnsureCached();
			if (cachedAnimationController != null)
			{
				Vector3 velocity = Motor.Velocity;
				float planar = new Vector2(velocity.x - water.Current.x, velocity.z - water.Current.y).magnitude;
				// Lying along the stroke: level when floating, nose down when diving, up when rising.
				float pitch = planar > 0.2f || Mathf.Abs(velocity.y) > 0.2f
					? -Mathf.Atan2(velocity.y, Mathf.Max(0.2f, planar)) * Mathf.Rad2Deg
					: 0f;
				cachedAnimationController.SetCrouching(false);
				cachedAnimationController.SetGrounded(false);
				cachedAnimationController.SetSpeed(planar > 0.1f ? Mathf.Clamp01(planar / Constants.Character.SwimSpeed) : 0f);
				cachedAnimationController.SetSwimming(true, Mathf.Clamp(pitch, -80f, 80f));
				cachedAnimationController.SetClimbing(false, 0f);
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
			TryStartClimbOut(hitNormal, hitPoint);
		}

		/// <summary>
		/// A swimmer at the surface pushing into a bank or a ledge whose top stands no more than
		/// <see cref="Constants.Character.SwimClimbOutLedgeMetres"/> over the water climbs out onto it.
		/// </summary>
		/// <remarks>
		/// Runs inside the motor's move, so on a replay too: everything it reads is reconciled state or the scene's
		/// fixed geometry, and what it writes (<see cref="climbOutTicks"/>) is reconciled.
		/// </remarks>
		private void TryStartClimbOut(Vector3 hitNormal, Vector3 hitPoint)
		{
			if (!isSwimming || climbOutTicks > 0 || !water.Present || moveInputVector.sqrMagnitude < 0.25f)
			{
				return;
			}
			Vector3 wall = Vector3.ProjectOnPlane(hitNormal, Vector3.up);
			if (hitNormal.y > 0.5f || wall.sqrMagnitude < 1e-4f)
			{
				return;
			}
			wall.Normalize();
			if (Vector3.Dot(moveInputVector.normalized, -wall) < 0.6f)
			{
				return;
			}
			float floatDepth = Constants.Character.SwimFloatSubmersion * FullCapsuleHeight;
			if (water.Surface - Motor.TransientPosition.y > floatDepth + 0.5f)
			{
				return; // under water: no ledge to climb from down there
			}
			float reach = Constants.Character.SwimClimbOutLedgeMetres;
			Vector3 probe = new Vector3(hitPoint.x, water.Surface + reach + 0.2f, hitPoint.z) - wall * (Motor.Capsule.radius + 0.1f);
			if (Motor.PhysicsScene.Raycast(probe, Vector3.down, out RaycastHit top, reach + 0.6f, Motor.StableGroundLayers, QueryTriggerInteraction.Ignore) &&
				top.point.y > water.Surface - 0.2f &&
				Vector3.Angle(top.normal, Vector3.up) <= Motor.MaxStableSlopeAngle)
			{
				climbOutTicks = (byte)Constants.Character.SwimClimbOutTicks;
			}
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