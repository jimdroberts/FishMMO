using UnityEngine;
using UnityEngine.SceneManagement;
using KinematicCharacterController;
using FishMMO.Shared;
using FishMMO.Shared.Weather;

namespace FishMMO.TestHarness.World
{
	/// <summary>
	/// The body the world test bed's camera follows in walk mode: the game's own character movement, a
	/// <see cref="KinematicCharacterMotor"/> driven by the <see cref="KCCController"/> players have, so it walks, steps,
	/// slides, jumps, swims, dives, climbs out of water and climbs ladders and ropes exactly as a player does. What it
	/// looks like is <see cref="WorldSimWalkerBody"/>'s.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The real movement, not a copy of it.</b> It was a capsule swept through the scene by rules of its own, which
	/// could not swim or climb and stood where a player might not. Now what stops the walker stops a player.
	/// </para>
	/// <para>
	/// <b>Stepped here,</b> once a frame, as KCCPlayer steps it once a tick: the inputs, the motor's two phases, the
	/// transform. No server and no prediction. The water is read at the bed's own tick (<see cref="WeatherQuery.CurrentTick"/>),
	/// the climbables are the scene's (<see cref="SceneClimbables"/>). There is no character, so the controller runs
	/// unmetered (no stamina to pay for a jump or a sprint) and unscaled (no Move Speed).
	/// </para>
	/// <para>
	/// <b>Set up as the playable prefabs are</b> (Assets/Prefabs/Shared/Entity/PlayableCharacters): a capsule 0.3 m round
	/// and 1.6 m tall with its pivot at the feet, the same step, slope, ledge and layers. It lives on Player, so no query
	/// finds it. Prop collision is streamed: the walker registers itself as a focus, so the rocks and trunks round it are
	/// built the moment it is put down rather than on the streamer's next tick.
	/// </para>
	/// </remarks>
	public sealed class WorldSimWalker : MonoBehaviour
	{
		// The playable prefabs' values.
		private const float CapsuleRadius = 0.3f;
		private const float CapsuleHeight = 1.6f;
		private const float CrouchedHeight = 0.8f;

		/// <summary>On walkable ground this frame.</summary>
		public bool Grounded => motor != null && motor.GroundingStatus.IsStableOnGround;

		/// <summary>In the water, floating or diving.</summary>
		public bool Swimming => controller != null && controller.IsSwimming;

		/// <summary>On a ladder or a rope.</summary>
		public bool Climbing => controller != null && controller.IsClimbing;

		/// <summary>Crouched.</summary>
		public bool Crouching => controller != null && controller.IsCrouching;

		/// <summary>The height of the capsule in metres (less while crouching); its pivot is its feet.</summary>
		public float Height => motor != null && motor.Capsule != null ? motor.Capsule.height : CapsuleHeight;

		/// <summary>The radius of the capsule in metres.</summary>
		public float Radius => motor != null && motor.Capsule != null ? motor.Capsule.radius : CapsuleRadius;

		/// <summary>World position of the soles of its feet.</summary>
		public Vector3 Feet => transform.position;

		/// <summary>A drop past this below the last ground stood on is off the edge of the scene: put back.</summary>
		private const float LostMetres = 500f;

		private KinematicCharacterMotor motor;
		private KCCController controller;
		private WorldSimWalkerBody body;
		private int groundMask;
		private Vector3 lastSafe;

		/// <summary>
		/// A walker set up as a player is, in the same scene as <paramref name="near"/>, so its motor's queries run in
		/// that scene's physics.
		/// </summary>
		public static WorldSimWalker Create(Component near)
		{
			var host = new GameObject("World Sim Walker");
			// Built inactive, so the motor, the controller and the walker wake with their settings in place.
			host.SetActive(false);
			if (near != null && near.gameObject.scene.IsValid())
			{
				SceneManager.MoveGameObjectToScene(host, near.gameObject.scene);
			}
			int player = LayerMask.NameToLayer("Player");
			if (player >= 0)
			{
				host.layer = player;
			}
			host.AddComponent<CapsuleCollider>();
			Rigidbody rigidbody = host.AddComponent<Rigidbody>();
			rigidbody.isKinematic = true;
			rigidbody.useGravity = false;

			KinematicCharacterMotor motor = host.AddComponent<KinematicCharacterMotor>();
			motor.MaxStableSlopeAngle = 60f;
			motor.MaxStepHeight = 0.25f;
			motor.MinRequiredStepDepth = 0.1f;
			motor.LedgeAndDenivelationHandling = true;
			motor.MaxStableDistanceFromLedge = 0.3f;
			motor.MaxStableDenivelationAngle = 180f;
			// Solid: Default, Water and Ground; standable: Default and Ground (water is solid but never stood on).
			motor.CollidableLayers = Mask(145, "Default", "Water", "Ground");
			motor.StableGroundLayers = Mask(129, "Default", "Ground");

			KCCController controller = host.AddComponent<KCCController>();
			controller.Motor = motor;
			controller.StableMovementSharpness = 10000f;
			// Facing where it goes, not where the camera looks: the bed's camera swings freely round it, and the clips
			// have no strafe.
			controller.OrientationMethod = OrientationMethod.TowardsMovement;
			controller.OrientationSharpness = 10f;
			controller.MaxAirMoveSpeed = 4f;
			controller.AirAccelerationSpeed = 2.5f;
			controller.Drag = 0.1f;
			controller.AllowJumpingWhenSliding = true;
			controller.JumpPreGroundingGraceTime = 0.1f;
			controller.JumpPostGroundingGraceTime = 0.1f;
			controller.CrouchedCapsuleHeight = CrouchedHeight;
			controller.FullCapsuleHeight = CapsuleHeight;
			controller.CapsuleBaseOffset = CapsuleHeight * 0.5f;
			motor.CharacterController = controller;

			WorldSimWalker walker = host.AddComponent<WorldSimWalker>();
			host.SetActive(true);
			// The capsule's settings are the motor's own (serialized, not settable): sized once it has woken.
			motor.SetCapsuleDimensions(CapsuleRadius, CapsuleHeight, CapsuleHeight * 0.5f);
			motor.SetPhysicsScene(host.scene.GetPhysicsScene());
			walker.body = WorldSimWalkerBody.Dress(walker);
			return walker;
		}

		private static LayerMask Mask(int fallback, params string[] layers)
		{
			int mask = LayerMask.GetMask(layers);
			return mask != 0 ? mask : fallback;
		}

		private void Awake()
		{
			motor = GetComponent<KinematicCharacterMotor>();
			controller = GetComponent<KCCController>();
			int solid = LayerMask.GetMask("Default", "Ground");
			groundMask = (solid != 0 ? solid : Physics.DefaultRaycastLayers) & ~(1 << gameObject.layer);
		}

		private void OnEnable()
		{
			PropColliderStreamer.AddFocus(transform, PropColliderStreamer.ImmediateRadius);
			// It treads the ground and parts the grass as a character does.
			FishMMO.Client.GroundTrailMap.AddSource(transform);
		}

		private void OnDisable()
		{
			PropColliderStreamer.RemoveFocus(transform);
			FishMMO.Client.GroundTrailMap.RemoveSource(transform);
		}

		/// <summary>
		/// Stands the walker, facing as its transform does, on whatever is under <paramref name="feet"/>: the ground found
		/// looking down from a little above it, or the terrain, or the point itself when there is neither.
		/// </summary>
		public void PlaceAt(Vector3 feet)
		{
			// Re-registered so the chunks round the new spot are finished before the first step.
			PropColliderStreamer.RemoveFocus(transform);
			PropColliderStreamer.AddFocus(transform, PropColliderStreamer.ImmediateRadius);
			Vector3 at = feet;
			if (gameObject.scene.GetPhysicsScene().Raycast(feet + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 4f, groundMask, QueryTriggerInteraction.Ignore))
			{
				at = hit.point;
			}
			else
			{
				float ground = WorldSimCamera.GroundBelow(feet);
				if (!float.IsNegativeInfinity(ground))
				{
					at.y = ground;
				}
			}
			Quaternion facing = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
			motor.SetPositionAndRotation(at, facing);
			motor.BaseVelocity = Vector3.zero;
			controller.ResetGroundProbe();
			transform.SetPositionAndRotation(at, facing);
			lastSafe = at;
			if (body != null)
			{
				body.Settle();
			}
			Physics.SyncTransforms();
		}

		/// <summary>
		/// One frame of the player's controls: <paramref name="forward"/> and <paramref name="right"/> are the move keys
		/// (−1..1), <paramref name="view"/> the camera's rotation (it decides what forward is, and looking down while
		/// swimming dives). <paramref name="jump"/> is the press, <paramref name="jumpHeld"/> the key held (rising in water),
		/// <paramref name="crouch"/> crouches, dives, and slides down a ladder; <paramref name="sprint"/> sprints.
		/// </summary>
		public void Move(float forward, float right, Quaternion view, bool jump, bool jumpHeld, bool crouch, bool sprint, float deltaTime)
		{
			if (motor == null || deltaTime <= 0f)
			{
				return;
			}
			// A shader compiling on first sight stalls the frame for seconds; a step that long is a teleport, not a walk.
			deltaTime = Mathf.Min(deltaTime, 0.1f);

			int flags = 0;
			flags.EnableBit(KCCMoveFlags.IsActualData);
			if (jump)
			{
				flags.EnableBit(KCCMoveFlags.Jump);
			}
			if (jumpHeld)
			{
				flags.EnableBit(KCCMoveFlags.Ascend);
			}
			if (crouch)
			{
				flags.EnableBit(KCCMoveFlags.Crouch);
			}
			if (sprint)
			{
				flags.EnableBit(KCCMoveFlags.Sprint);
			}
			var input = new KCCInputReplicateData(forward, right, flags, transform.position + Vector3.up * CapsuleHeight, view);

			Vector3 start = transform.position;
			controller.SetInputs(ref input);
			controller.WaterTick = WeatherQuery.CurrentTick;
			motor.SetPlatformVelocity(Vector3.zero, 0f);
			motor.UpdatePhase1(deltaTime);
			motor.UpdatePhase2(deltaTime);
			Vector3 position = motor.TransientPosition;
			if (Grounded)
			{
				lastSafe = position;
			}
			else if (position.y < lastSafe.y - LostMetres)
			{
				// Walked off the edge of the scene: back to the last ground stood on.
				PlaceAt(lastSafe);
				return;
			}
			transform.SetPositionAndRotation(position, motor.TransientRotation);

			if (body != null)
			{
				body.Pose((position - start) / deltaTime, Grounded, Swimming, Climbing, Crouching, deltaTime);
			}
		}

		/// <summary>Throws a punch (the figure's; nothing is hit: the bed has no combat).</summary>
		public void Punch()
		{
			if (body != null)
			{
				body.Punch();
			}
		}
	}
}
