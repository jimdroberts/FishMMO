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
	/// slides, jumps, swims, dives, climbs out of water and climbs ladders and ropes exactly as a player does. It can also
	/// sit, on the ground or on whatever seat is behind it (the game has no sitting yet; this is the figure's). What it
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

		/// <summary>The capsule's height while it swims (always the full height: crouch means dive in the water).</summary>
		public const float SwimmingHeight = CapsuleHeight;

		/// <summary>On walkable ground this frame.</summary>
		public bool Grounded => motor != null && motor.GroundingStatus.IsStableOnGround;

		/// <summary>In the water, floating or diving.</summary>
		public bool Swimming => controller != null && controller.IsSwimming;

		/// <summary>On a ladder or a rope.</summary>
		public bool Climbing => controller != null && controller.IsClimbing;

		/// <summary>Crouched.</summary>
		public bool Crouching => controller != null && controller.IsCrouching;

		/// <summary>Sitting, on the ground or a seat.</summary>
		public bool Sitting => sitting;

		/// <summary>
		/// The still water's surface over the walker while it swims, world metres (the tide, never the waves: what the
		/// controller floats it on); NaN when it is not swimming.
		/// </summary>
		public float StillWater
		{
			get
			{
				if (!Swimming)
				{
					return float.NaN;
				}
				float depth = controller.HeadDepth;
				return float.IsInfinity(depth) ? float.NaN : transform.position.y + Height + depth;
			}
		}

		/// <summary>How high the figure is drawn over the capsule, metres: the swell it rides, the ground it finds.</summary>
		public float ShownLift => body != null ? body.Lift : 0f;

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
		/// <summary>The ship's deck stood on last frame, and where it was then, for the carry (<see cref="Carry"/>).</summary>
		private Transform deck;
		private Matrix4x4 deckWas;
		private float deckYawWas;
		private bool sitting;

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
			sitting = false;
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

			if (sitting)
			{
				// Any move, a jump or a crouch gets up (the jump only gets up), and so does losing the ground under it.
				bool getUp = forward != 0f || right != 0f || jump || crouch || !Grounded;
				if (!getUp)
				{
					forward = right = 0f;
					sprint = false;
				}
				else
				{
					StandUp();
					jump = false;
				}
			}

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
			Carry(deltaTime, out Vector3 carried, out float turned);
			motor.SetPlatformVelocity(carried, turned);
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
				body.Pose((position - start) / deltaTime, Grounded, Swimming, Climbing, Crouching, StillWater, deltaTime);
			}
		}

		// ── Sitting ──

		/// <summary>Sits down, or gets up when sitting.</summary>
		public void ToggleSit()
		{
			if (sitting)
			{
				StandUp();
			}
			else
			{
				SitDown();
			}
		}

		/// <summary>
		/// Sits down: on a seat when one is near (<see cref="FindSeat"/>), its back turned to it, else on the ground where it
		/// stands. Only standing on the ground: not swimming, climbing, crouched or in the air.
		/// </summary>
		public void SitDown()
		{
			if (sitting || motor == null || !Grounded || Swimming || Climbing || Crouching)
			{
				return;
			}
			sitting = true;
			bool seat = FindSeat(out Vector3 toward, out float height, out float edge);
			if (seat)
			{
				// Its back to the seat.
				Quaternion facing = Quaternion.LookRotation(-toward, Vector3.up);
				motor.SetRotation(facing);
				transform.rotation = facing;
			}
			if (body != null)
			{
				body.SitDown(seat, height, edge);
			}
		}

		/// <summary>Gets up.</summary>
		public void StandUp()
		{
			sitting = false;
			if (body != null)
			{
				body.StandUp();
			}
		}

		/// <summary>
		/// The nearest seat round the walker (<see cref="SitRules.IsSeat"/>: a level top seat-high over its feet and deep
		/// enough) with room over it to sit up in, whose front edge is within <see cref="SitRules.Reach"/>. A bench, a wall's
		/// coping, a crate, a flat rock: whatever a body would sit on. Looked for behind first, then to the sides, then ahead,
		/// so of two seats as near the one behind wins.
		/// </summary>
		private bool FindSeat(out Vector3 toward, out float height, out float edge)
		{
			toward = -transform.forward;
			height = 0f;
			edge = float.MaxValue;
			PhysicsScene physics = gameObject.scene.GetPhysicsScene();
			Vector3 feet = transform.position;
			for (int i = 0; i < 8; i++)
			{
				// 180°, then ±45° about it, ±90°, ±135°, 0°.
				int step = (i + 1) / 2;
				float angle = 180f + (i % 2 == 1 ? 45f : -45f) * step;
				Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * transform.forward;
				for (float d = 0.2f; d <= SitRules.Reach && d < edge; d += 0.05f)
				{
					if (TopAt(physics, feet, feet + direction * d, out float top, out float level)
						&& TopAt(physics, feet, feet + direction * (d + SitRules.Depth), out float back, out float levelBack)
						&& SitRules.IsSeat(top, back, Mathf.Min(level, levelBack))
						&& !physics.Raycast(new Vector3(feet.x, feet.y + top + 0.05f, feet.z) + direction * (d + 0.1f), Vector3.up, 1.0f, groundMask, QueryTriggerInteraction.Ignore))
					{
						toward = direction;
						height = top;
						edge = d;
						break;
					}
				}
			}
			return edge < float.MaxValue;
		}

		/// <summary>The top of whatever is under <paramref name="at"/> up to seat height and a little over, as metres over
		/// <paramref name="feet"/>, and how level it is (its normal's up component).</summary>
		private bool TopAt(PhysicsScene physics, Vector3 feet, Vector3 at, out float top, out float level)
		{
			float reach = SitRules.MaxSeatHeight + 0.3f;
			bool hit = physics.Raycast(new Vector3(at.x, feet.y + reach, at.z), Vector3.down, out RaycastHit found, reach, groundMask, QueryTriggerInteraction.Ignore);
			top = hit ? found.point.y - feet.y : 0f;
			level = hit ? found.normal.y : 0f;
			return hit;
		}

		/// <summary>
		/// What a ship under way gives a walker standing on its deck: the velocity that keeps the deck point under its feet
		/// where the deck has taken it since last frame, and the yaw the hull turned through. The game's player is carried
		/// by <see cref="FloatingBoats.TryGetCarry"/> on the synced tick; the bed has no tick (its ships sail by the
		/// presentation clock), so the carry is read off the hull's own collider as it moved, as a platform's would be.
		/// </summary>
		private void Carry(float deltaTime, out Vector3 velocity, out float turn)
		{
			velocity = Vector3.zero;
			turn = 0f;
			Collider under = motor.GroundingStatus.IsStableOnGround ? motor.GroundingStatus.GroundCollider : null;
			Transform hull = under != null && under.GetComponentInParent<FloatingBoats>() != null ? under.transform : null;
			if (hull != null && hull == deck && deltaTime > 0f)
			{
				Vector3 local = deckWas.inverse.MultiplyPoint3x4(transform.position);
				velocity = (hull.localToWorldMatrix.MultiplyPoint3x4(local) - transform.position) / deltaTime;
				turn = Mathf.DeltaAngle(deckYawWas, hull.eulerAngles.y);
			}
			deck = hull;
			if (hull != null)
			{
				deckWas = hull.localToWorldMatrix;
				deckYawWas = hull.eulerAngles.y;
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
