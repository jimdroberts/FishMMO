using System.Collections.Generic;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Animates a character's model on every client: a <see cref="CharacterBody"/> built on the model's
	/// <see cref="CharacterModel"/>, fed what the character is doing.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Who tells it what.</b> The character this client owns is told by its own movement (KCCController calls
	/// <see cref="SetGrounded"/>, <see cref="SetCrouching"/>, <see cref="SetSwimming"/>, <see cref="SetClimbing"/> each
	/// tick) and its own abilities (<see cref="TriggerAttack"/>, <see cref="TriggerCast"/>, …). Everyone else's movement
	/// never runs here: another player is placed by its NetworkTransform (state forwarding is off) and an NPC moves on the
	/// server alone. So a character nothing has told anything lately is read from what this client can see
	/// (<see cref="ObservedMotion"/>: the ground under it, the water round it, the climbable it is on), its casts and attacks
	/// come from the cast broadcast every observer already receives (<see cref="AbilityController.AnimateObservedCast"/>),
	/// and its death from the death broadcast (<see cref="TriggerDeath"/>). Nothing extra goes on the wire.
	/// </para>
	/// <para>
	/// <b>The speed is always seen, never told:</b> every body measures how far its model really went each frame, so the
	/// feet match the ground under prediction smoothing and interpolation alike.
	/// </para>
	/// <para>
	/// Client only: a dedicated server compiles none of it (the setters are empty there).
	/// </para>
	/// </remarks>
	public class CharacterAnimationController : CharacterBehaviour, ICharacterAnimationController, IModelReadyHandler
	{
#if !UNITY_SERVER
		/// <summary>Seconds after the movement last spoke that its word still holds; after it, the body reads what it sees.</summary>
		private const float FedSeconds = 0.25f;

		/// <summary>How quickly the measured velocity follows the model's motion, per second (it smooths a jittery transform).</summary>
		private const float VelocityRate = 20f;

		private static readonly List<int> nearClimbables = new List<int>();

		private CharacterBody body;
		private Animator bodyAnimator;
		private Transform bodyRoot;
		private Vector3 lastPosition;
		private bool hasLastPosition;
		private Vector3 velocity;
		private int groundMask;

		// What the movement last said (the owner's own character).
		private float fedAt = float.NegativeInfinity;
		private bool fedGrounded = true;
		private bool fedCrouching;
		private bool fedSwimming;
		private bool fedClimbing;

		// What was last read off a watched character.
		private bool seenSwimming;
#endif

		/// <summary>
		/// Attempts initial discovery. May fail if model isn't loaded.
		/// <see cref="OnModelReady"/> provides the guaranteed re-acquisition.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();
#if !UNITY_SERVER
			TryBuildBody();
#endif
		}

		/// <inheritdoc />
		public void OnModelReady()
		{
#if !UNITY_SERVER
			TryBuildBody();
#endif
		}

		/// <summary>Drops the body before the object returns to the pool: its model is destroyed and replaced on the next spawn.</summary>
		/// <param name="asServer">True if called on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
#if !UNITY_SERVER
			DropBody();
			fedAt = float.NegativeInfinity;
			seenSwimming = false;
#endif
		}

#if !UNITY_SERVER
		private void OnDestroy()
		{
			DropBody();
		}

		private void OnEnable()
		{
			body?.SetPlaying(true);
		}

		private void OnDisable()
		{
			body?.SetPlaying(false);
		}

		private void DropBody()
		{
			body?.Dispose();
			body = null;
			bodyAnimator = null;
			bodyRoot = null;
			hasLastPosition = false;
		}

		/// <summary>
		/// Builds the body on the model under MeshRoot (its <see cref="CharacterModel"/>), once per model: a model swapped
		/// for another gets a new one.
		/// </summary>
		private void TryBuildBody()
		{
			if (Character == null || Character.MeshRoot == null)
			{
				return;
			}
			CharacterModel model = Character.MeshRoot.GetComponentInChildren<CharacterModel>(true);
			Animator animator = model != null ? model.GetComponent<Animator>() : null;
			if (animator == null)
			{
				return;
			}
			if (body != null && animator == bodyAnimator)
			{
				return;
			}
			DropBody();
			// Its own character is always animated; another off screen only keeps its bones' transforms when seen.
			animator.cullingMode = base.IsOwner ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullUpdateTransforms;
			body = CharacterBody.Create(animator, model.Animations, name);
			if (body == null)
			{
				return;
			}
			bodyAnimator = animator;
			bodyRoot = animator.transform.parent != null ? animator.transform.parent : animator.transform;
			int solid = LayerMask.GetMask("Default", "Ground");
			groundMask = solid != 0 ? solid : Physics.DefaultRaycastLayers;
			int playerLayer = LayerMask.NameToLayer("Player");
			if (playerLayer >= 0)
			{
				groundMask &= ~(1 << playerLayer);
			}
			RestorePoseForCurrentState();
		}

		/// <summary>One frame of the body's motion, before the animation is evaluated.</summary>
		private void Update()
		{
			if (body == null || bodyRoot == null)
			{
				return;
			}
			float deltaTime = Time.deltaTime;
			if (deltaTime <= 0f)
			{
				return;
			}
			Vector3 position = bodyRoot.position;
			if (hasLastPosition)
			{
				Vector3 moved = (position - lastPosition) / deltaTime;
				// A teleport (or a respawn) is not a sprint.
				velocity = moved.sqrMagnitude > 900f ? Vector3.zero : Vector3.Lerp(velocity, moved, 1f - Mathf.Exp(-VelocityRate * deltaTime));
			}
			lastPosition = position;
			hasLastPosition = true;

			var motion = new CharacterMotion { Velocity = velocity, StillWater = float.NaN };
			if (Time.time - fedAt <= FedSeconds)
			{
				motion.Grounded = fedGrounded;
				motion.Crouching = fedCrouching;
				motion.Swimming = fedSwimming;
				motion.Climbing = fedClimbing;
			}
			else
			{
				Observe(position, ref motion);
			}
			body.Pose(in motion, deltaTime);
		}

		/// <summary>The figure set on the ground, its seat or the waves, after the animation has posed it.</summary>
		private void LateUpdate()
		{
			body?.Place(Time.deltaTime);
		}

		/// <summary>What a watched character is doing, read off where it is (<see cref="ObservedMotion"/>).</summary>
		private void Observe(Vector3 feet, ref CharacterMotion motion)
		{
			PhysicsScene physics = gameObject.scene.GetPhysicsScene();
			motion.Grounded = physics.Raycast(feet + Vector3.up * ObservedMotion.GroundProbeHeight, Vector3.down,
				ObservedMotion.GroundProbeHeight + ObservedMotion.GroundTolerance, groundMask, QueryTriggerInteraction.Ignore);

			float still = SurfaceWater.TryGetSurfaceAt(feet.x, feet.z, out float level) ? level : float.NaN;
			seenSwimming = ObservedMotion.Swimming(seenSwimming, still, feet.y, CharacterBody.CapsuleHeight);
			motion.Swimming = seenSwimming;
			motion.StillWater = still;
			if (seenSwimming)
			{
				motion.Grounded = false;
				return;
			}

			if (!motion.Grounded && SceneClimbables.Near(gameObject.scene, feet, 1.5f, nearClimbables))
			{
				foreach (int index in nearClimbables)
				{
					if (SceneClimbables.TryGet(gameObject.scene, index, out SceneClimbable climbable)
						&& ObservedMotion.OnClimbable(in climbable, feet, 0.3f))
					{
						motion.Climbing = true;
						break;
					}
				}
			}
		}

		/// <summary>
		/// Lays a character that is already dead down as soon as its body exists: death is an event, and a character that
		/// arrives dead (a corpse walked up to, a reconnect) never heard it.
		/// </summary>
		private void RestorePoseForCurrentState()
		{
			if (body != null && Character != null && Character.IsFlagged(CharacterFlags.IsDead))
			{
				body.Die(already: true);
			}
		}

		private void Fed()
		{
			fedAt = Time.time;
		}
#endif

		/// <inheritdoc />
		/// <remarks>Not used: the body measures how far its model really goes, which every peer can see.</remarks>
		public void SetSpeed(float speed)
		{
		}

		/// <inheritdoc />
		public void SetGrounded(bool grounded)
		{
#if !UNITY_SERVER
			fedGrounded = grounded;
			Fed();
#endif
		}

		/// <inheritdoc />
		public void SetCrouching(bool crouching)
		{
#if !UNITY_SERVER
			fedCrouching = crouching;
			Fed();
#endif
		}

		/// <inheritdoc />
		/// <remarks>The pitch is the body's own business: it lies along the swim it can see (<see cref="SwimPose"/>).</remarks>
		public void SetSwimming(bool swimming, float pitch)
		{
#if !UNITY_SERVER
			fedSwimming = swimming;
			Fed();
#endif
		}

		/// <inheritdoc />
		/// <remarks>The rate is the body's own business: the climb is stepped by the height it sees climbed.</remarks>
		public void SetClimbing(bool climbing, float rate)
		{
#if !UNITY_SERVER
			fedClimbing = climbing;
			Fed();
#endif
		}

		/// <inheritdoc />
		/// <remarks>Not used: a body in the air rises, tops out and falls by its own vertical speed.</remarks>
		public void TriggerJump()
		{
		}

		/// <inheritdoc />
		public void TriggerAttack()
		{
#if !UNITY_SERVER
			body?.Punch();
#endif
		}

		/// <inheritdoc />
		public void SetBlocking(bool blocking)
		{
#if !UNITY_SERVER
			body?.SetBlocking(blocking);
#endif
		}

		/// <inheritdoc />
		public void TriggerRoll()
		{
#if !UNITY_SERVER
			body?.Roll();
#endif
		}

		/// <inheritdoc />
		public void TriggerCast()
		{
#if !UNITY_SERVER
			body?.Cast();
#endif
		}

		/// <inheritdoc />
		/// <remarks>Death takes everything else's place: the body falls and lies, its guard and its actions dropped.</remarks>
		public void TriggerDeath()
		{
#if !UNITY_SERVER
			body?.Die();
#endif
		}

		/// <inheritdoc />
		public void ResetDeath()
		{
#if !UNITY_SERVER
			body?.Revive();
#endif
		}

		/// <inheritdoc />
		/// <remarks>Not used: every clip is played in place and the movement moves the character.</remarks>
		public void SetRootMotion(bool enabled)
		{
		}
	}
}
