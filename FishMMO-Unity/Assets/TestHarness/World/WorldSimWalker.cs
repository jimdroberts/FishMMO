using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;

namespace FishMMO.TestHarness.World
{
	/// <summary>
	/// The body the world test bed's camera follows in walk mode: a plain capsule with a capsule
	/// collider, moved by sweeping that capsule through the scene, so it stands on the terrain, climbs
	/// what a person could climb and stops at what they could not.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Kinematic, not a rigidbody pushed about.</b> A dynamic capsule on frictionless material
	/// slides down every gentle slope it is left standing on, and one with friction sticks to walls
	/// it is walked into. Sweeping the collider and sliding along what it hits gives the same answer
	/// every frame, which is what a bed for looking at the world wants from its legs.
	/// </para>
	/// <para>
	/// It collides with the layers the game's characters stand on (Default and Ground) and lives on
	/// Player itself, so no sweep ever finds the capsule it is sweeping. Prop collision is streamed:
	/// the walker registers itself as a focus, so the rocks and trunks round it are built the moment
	/// it is put down rather than on the streamer's next tick.
	/// </para>
	/// </remarks>
	[RequireComponent(typeof(CapsuleCollider))]
	public sealed class WorldSimWalker : MonoBehaviour
	{
		public float WalkSpeed = 4.5f;
		public float RunSpeed = 9f;
		public float JumpSpeed = 5f;
		[Tooltip("Steepest ground, in degrees, the walker stands on. Anything steeper is a wall: it is slid along, never walked up.")]
		public float SlopeLimit = 50f;
		[Tooltip("Tallest ledge, in metres, the walker steps straight up onto.")]
		public float StepHeight = 0.35f;
		[Tooltip("Gap kept between the capsule and whatever it touches, so a sweep never starts inside what it hit last.")]
		public float Skin = 0.02f;

		/// <summary>On walkable ground this frame.</summary>
		public bool Grounded { get; private set; }

		/// <summary>The height of the capsule in world metres; its pivot is its centre.</summary>
		public float Height => capsule == null ? 0f : capsule.height * transform.lossyScale.y;

		/// <summary>World position of the soles of its feet.</summary>
		public Vector3 Feet => transform.position - Vector3.up * (Height * 0.5f);

		private const int MaxSlides = 4;
		private const float TurnRate = 12f;

		private CapsuleCollider capsule;
		private int mask;
		private float verticalSpeed;
		private Vector3 groundNormal = Vector3.up;
		private Vector3 lastSafe;
		private readonly Collider[] overlaps = new Collider[16];

		/// <summary>
		/// A capsule the size of a person — 1.8 m tall, 0.35 m round — in the same scene as
		/// <paramref name="near"/>, so its sweeps run in that scene's physics.
		/// </summary>
		public static WorldSimWalker Create(Component near)
		{
			GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
			body.name = "World Sim Walker";
			if (near != null && near.gameObject.scene.IsValid())
			{
				SceneManager.MoveGameObjectToScene(body, near.gameObject.scene);
			}
			int player = LayerMask.NameToLayer("Player");
			if (player >= 0)
			{
				body.layer = player;
			}
			// The primitive is 2 m by 1 m; scaled rather than re-shaped so its mesh and its collider stay one shape.
			body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
			Rigidbody rigidbody = body.AddComponent<Rigidbody>();
			rigidbody.isKinematic = true;
			rigidbody.useGravity = false;
			return body.AddComponent<WorldSimWalker>();
		}

		private void Awake()
		{
			capsule = GetComponent<CapsuleCollider>();
			int solid = LayerMask.GetMask("Default", "Ground");
			mask = (solid != 0 ? solid : Physics.DefaultRaycastLayers) & ~(1 << gameObject.layer);
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
		/// Stands the walker on whatever is under <paramref name="feet"/>: the ground found sweeping
		/// down from a little above it, or the terrain, or the point itself when there is neither.
		/// </summary>
		public void PlaceAt(Vector3 feet)
		{
			float half = Height * 0.5f;
			Vector3 centre = feet + Vector3.up * (half + 2f);
			// Re-registered so the chunks round the new spot are finished before the first sweep.
			PropColliderStreamer.RemoveFocus(transform);
			PropColliderStreamer.AddFocus(transform, PropColliderStreamer.ImmediateRadius);
			if (Cast(centre, Vector3.down, 4f, out RaycastHit hit))
			{
				centre += Vector3.down * Mathf.Max(0f, hit.distance - Skin);
			}
			else
			{
				float ground = WorldSimCamera.GroundBelow(feet);
				centre = new Vector3(feet.x, (float.IsNegativeInfinity(ground) ? feet.y : ground) + half + Skin, feet.z);
			}
			transform.position = centre;
			lastSafe = centre;
			verticalSpeed = 0f;
			Grounded = false;
			Physics.SyncTransforms();
		}

		/// <summary>
		/// One frame of walking: <paramref name="wish"/> is the flat direction asked for (length up to
		/// one), turned into speed here.
		/// </summary>
		public void Move(Vector3 wish, bool run, bool jump, float deltaTime)
		{
			if (capsule == null || deltaTime <= 0f)
			{
				return;
			}
			// A shader compiling on first sight stalls the frame for seconds; a step that long is a
			// teleport, not a walk.
			deltaTime = Mathf.Min(deltaTime, 0.1f);
			wish.y = 0f;
			if (wish.sqrMagnitude > 1f)
			{
				wish.Normalize();
			}
			Vector3 horizontal = wish * (run ? RunSpeed : WalkSpeed) * deltaTime;
			float gravity = Mathf.Abs(Physics.gravity.y);

			Vector3 position = Depenetrate(transform.position);

			if (Grounded && jump)
			{
				Grounded = false;
				verticalSpeed = JumpSpeed;
			}

			if (Grounded)
			{
				// Along the ground, so walking downhill does not launch off it a frame at a time.
				Vector3 along = Vector3.ProjectOnPlane(horizontal, groundNormal);
				if (along.sqrMagnitude > 1e-10f)
				{
					along = along.normalized * horizontal.magnitude;
				}
				position = Walk(position, along);
				// Held to the ground: as far down as a run down the steepest walkable slope drops in a frame.
				float snap = StepHeight + horizontal.magnitude * Mathf.Tan(SlopeLimit * Mathf.Deg2Rad) + Skin;
				if (Cast(position, Vector3.down, snap + Skin, out RaycastHit below) && Walkable(below.normal))
				{
					position += Vector3.down * Mathf.Max(0f, below.distance - Skin);
					groundNormal = below.normal;
					verticalSpeed = 0f;
				}
				else
				{
					Grounded = false;
					verticalSpeed = 0f;
				}
			}
			else
			{
				verticalSpeed -= gravity * deltaTime;
				Vector3 motion = horizontal + Vector3.up * (verticalSpeed * deltaTime);
				Vector3 before = position;
				position = Slide(position, motion, false);
				// A ceiling ends the rise there rather than holding the walker against it.
				if (verticalSpeed > 0f && position.y - before.y < motion.y * 0.5f)
				{
					verticalSpeed = 0f;
				}
			}

			// Landed this frame? A grounded step has already found its ground.
			if (!Grounded)
			{
				if (verticalSpeed <= 0f && Cast(position, Vector3.down, Skin * 3f, out RaycastHit ground) && Walkable(ground.normal))
				{
					Grounded = true;
					groundNormal = ground.normal;
					verticalSpeed = 0f;
				}
				else
				{
					groundNormal = Vector3.up;
				}
			}

			position = KeepAboveTerrain(position);
			if (Grounded)
			{
				lastSafe = position;
			}
			else if (position.y < lastSafe.y - 500f)
			{
				// Walked off the edge of the scene: back to the last ground stood on.
				position = lastSafe;
				verticalSpeed = 0f;
			}
			transform.position = position;

			if (wish.sqrMagnitude > 1e-4f)
			{
				Quaternion facing = Quaternion.LookRotation(wish, Vector3.up);
				transform.rotation = Quaternion.Slerp(transform.rotation, facing, 1f - Mathf.Exp(-TurnRate * deltaTime));
			}
		}

		/// <summary>A grounded move, stepping up onto a ledge when the plain move is stopped short.</summary>
		private Vector3 Walk(Vector3 position, Vector3 motion)
		{
			Vector3 plain = Slide(position, motion, true);
			float wanted = Flat(motion).magnitude;
			float got = Flat(plain - position).magnitude;
			if (StepHeight <= 0f || wanted < 1e-4f || got >= wanted * 0.9f)
			{
				return plain;
			}
			float rise = Cast(position, Vector3.up, StepHeight + Skin, out RaycastHit roof) ? Mathf.Max(0f, roof.distance - Skin) : StepHeight;
			Vector3 raised = position + Vector3.up * rise;
			Vector3 across = Slide(raised, Flat(motion).normalized * wanted, true);
			if (!Cast(across, Vector3.down, rise + Skin * 2f, out RaycastHit landing) || !Walkable(landing.normal))
			{
				return plain;
			}
			Vector3 stepped = across + Vector3.down * Mathf.Max(0f, landing.distance - Skin);
			return Flat(stepped - position).magnitude > got + 1e-3f ? stepped : plain;
		}

		/// <summary>Sweeps the capsule along <paramref name="motion"/>, sliding along whatever it meets.</summary>
		private Vector3 Slide(Vector3 position, Vector3 motion, bool grounded)
		{
			for (int i = 0; i < MaxSlides; i++)
			{
				float distance = motion.magnitude;
				if (distance < 1e-5f)
				{
					break;
				}
				Vector3 direction = motion / distance;
				if (!Cast(position, direction, distance + Skin, out RaycastHit hit))
				{
					position += motion;
					break;
				}
				float travel = Mathf.Max(0f, hit.distance - Skin);
				position += direction * travel;
				motion = direction * (distance - travel);
				Vector3 normal = hit.normal;
				// On foot a slope too steep to stand on is a wall: slid along, not climbed by pushing into it.
				if (grounded && !Walkable(normal))
				{
					Vector3 flat = Flat(normal);
					if (flat.sqrMagnitude > 1e-6f)
					{
						normal = flat.normalized;
					}
				}
				motion = Vector3.ProjectOnPlane(motion, normal);
			}
			return position;
		}

		/// <summary>Pushes the capsule out of anything it has ended up inside.</summary>
		private Vector3 Depenetrate(Vector3 position)
		{
			PhysicsScene physics = gameObject.scene.GetPhysicsScene();
			for (int pass = 0; pass < 3; pass++)
			{
				Ends(position, out Vector3 top, out Vector3 bottom, out float radius);
				int count = physics.OverlapCapsule(top, bottom, radius, overlaps, mask, QueryTriggerInteraction.Ignore);
				bool moved = false;
				for (int i = 0; i < count; i++)
				{
					Collider other = overlaps[i];
					if (other == capsule)
					{
						continue;
					}
					if (Physics.ComputePenetration(capsule, position, transform.rotation, other, other.transform.position, other.transform.rotation,
						out Vector3 direction, out float distance))
					{
						position += direction * (distance + Skin);
						moved = true;
					}
				}
				if (!moved)
				{
					break;
				}
			}
			return position;
		}

		/// <summary>
		/// The terrain catches the capsule anyway; this is for a seam between two tiles a sweep
		/// slipped through, where the walker would otherwise fall out of the world.
		/// </summary>
		private Vector3 KeepAboveTerrain(Vector3 position)
		{
			float ground = WorldSimCamera.GroundBelow(position);
			float feet = position.y - Height * 0.5f;
			// Not in a cave or under an overhang: there the walker is meant to be under the hillside's surface.
			if (!float.IsNegativeInfinity(ground) && feet < ground - 0.25f
				&& !WorldSimCamera.Sheltered(gameObject.scene.GetPhysicsScene(), position, ground, mask))
			{
				position.y = ground + Height * 0.5f + Skin;
				verticalSpeed = Mathf.Max(0f, verticalSpeed);
			}
			return position;
		}

		private bool Cast(Vector3 position, Vector3 direction, float distance, out RaycastHit hit)
		{
			Ends(position, out Vector3 top, out Vector3 bottom, out float radius);
			return gameObject.scene.GetPhysicsScene().CapsuleCast(top, bottom, radius, direction, out hit, distance, mask, QueryTriggerInteraction.Ignore);
		}

		/// <summary>The centres of the capsule's two end spheres and their radius, in world space, at <paramref name="position"/>.</summary>
		private void Ends(Vector3 position, out Vector3 top, out Vector3 bottom, out float radius)
		{
			Vector3 scale = transform.lossyScale;
			radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
			float half = Mathf.Max(0f, capsule.height * Mathf.Abs(scale.y) * 0.5f - radius);
			Vector3 centre = position + transform.rotation * Vector3.Scale(capsule.center, scale);
			top = centre + Vector3.up * half;
			bottom = centre - Vector3.up * half;
		}

		private bool Walkable(Vector3 normal) => normal.y >= Mathf.Cos(SlopeLimit * Mathf.Deg2Rad);

		private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
	}
}
