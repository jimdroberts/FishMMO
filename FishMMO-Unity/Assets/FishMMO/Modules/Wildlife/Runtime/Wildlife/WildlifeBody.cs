using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A wild animal's body and its animation: the species' procedural mesh (<see cref="WildlifeBodyMesh"/>) under the
	/// character's mesh root, posed each frame by four shader parameters (FishMMO/Wildlife). Stands in for the humanoid
	/// CharacterAnimationController on wildlife prefabs, so the ability and damage code that triggers attacks, casts and
	/// deaths reaches it unchanged. Client only; on the server it does nothing.
	/// </summary>
	public class WildlifeBody : CharacterBehaviour, ICharacterAnimationController
	{
		private static readonly int GaitId = Shader.PropertyToID("_WildGait");
		private static readonly int PoseId = Shader.PropertyToID("_WildPose");
		private static readonly int FlightId = Shader.PropertyToID("_WildFlight");

		/// <summary>Seconds an attack's lunge takes, out and back.</summary>
		public const float AttackSeconds = 0.45f;

		/// <summary>Seconds the fall onto its side takes.</summary>
		public const float DeathSeconds = 0.7f;

#if !UNITY_SERVER
		private MeshRenderer bodyRenderer;
		private MaterialPropertyBlock block;
		private WildlifeTemplate species;
		private Vector3 lastPosition;
		private bool hasLastPosition;
		private float speed;
		private float phase;
		private float wingPhase;
		private float attackTimer;
		private float death;
		private float deathTarget;
		/// <summary>The flight vector last sent (z is the fade-in, below), so a fade step can be sent alone.</summary>
		private Vector4 flight;
		/// <summary>Unscaled time the animal came into view; negative once it is drawn whole.</summary>
		private float appearStarted = -1f;
#endif

		public override void OnStartCharacter()
		{
			base.OnStartCharacter();
#if !UNITY_SERVER
			BuildBody();
#endif
		}

		/// <summary>
		/// Builds the body on every client that spawns this animal.
		/// </summary>
		/// <remarks>
		/// <see cref="OnStartCharacter"/> alone never ran for an NPC: only the OWNER's player character starts its behaviours
		/// (PlayerCharacter.TryInitializeLocalClient), so every animal arrived on clients with a nameplate and no body. OnStartClient runs for every spawned object, after its payload.
		/// </remarks>
		public override void OnStartClient()
		{
			base.OnStartClient();
#if !UNITY_SERVER
			BuildBody();
			// Coming into view: dissolves in over the time a humanoid takes (CharacterAnimationController.AppearSeconds).
			appearStarted = Time.unscaledTime;
			SendAppear();
#endif
		}

		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
#if !UNITY_SERVER
			hasLastPosition = false;
			speed = 0f;
			attackTimer = 0f;
			death = deathTarget = 0f;
			appearStarted = -1f;
			flight = Vector4.zero;
#endif
		}

#if !UNITY_SERVER
		private void BuildBody()
		{
			species = Character is NPC npc ? npc.Wildlife as WildlifeTemplate : null;
			if (species == null || species.Material == null || bodyRenderer != null)
			{
				return;
			}
			Transform root = Character is BaseCharacter character && character.MeshRoot != null ? character.MeshRoot : transform;
			var body = new GameObject("WildlifeBody");
			body.transform.SetParent(root, false);
			body.AddComponent<MeshFilter>().sharedMesh = WildlifeBodyMesh.For(species);
			bodyRenderer = body.AddComponent<MeshRenderer>();
			bodyRenderer.sharedMaterial = species.Material;
			bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
			BaseCharacter.ApplyVisualLayer(body);
			block = new MaterialPropertyBlock();
		}

		private void LateUpdate()
		{
			if (bodyRenderer == null)
			{
				return;
			}
			float dt = Time.deltaTime;
			if (dt <= 0f)
			{
				return;
			}
			Vector3 position = transform.position;
			if (hasLastPosition)
			{
				Vector3 moved = position - lastPosition;
				moved.y = 0f;
				float measured = moved.magnitude / dt;
				// A teleport or respawn is not a sprint.
				speed = measured > 30f ? 0f : Mathf.Lerp(speed, measured, 1f - Mathf.Exp(-12f * dt));
			}
			lastPosition = position;
			hasLastPosition = true;

			if (attackTimer > 0f)
			{
				attackTimer = Mathf.Max(0f, attackTimer - dt);
			}
			death = Mathf.MoveTowards(death, deathTarget, dt / DeathSeconds);

			// Nothing to pose while nobody sees it (the shadow pass counts as seen).
			if (!bodyRenderer.isVisible)
			{
				// The fade-in still advances: isVisible is last frame's, so the first frame on screen draws what was sent here.
				SendAppear();
				return;
			}

			WildlifeBodySpec spec = species.Body;
			WildlifeGait gait = WildlifeGait.Evaluate(spec, species.RunSpeed, speed, death > 0f ? 0f : 1f);
			phase = Mathf.Repeat(phase + gait.StepsPerSecond * Mathf.PI * 2f * dt, Mathf.PI * 2f);
			float attack = attackTimer > 0f ? Mathf.Sin((1f - attackTimer / AttackSeconds) * Mathf.PI) : 0f;
			float wag = Mathf.Sin(Time.time * (speed > 0.2f ? 9f : 4f)) * (speed > 0.2f ? 0.12f : 0.3f);
			float fallen = Mathf.SmoothStep(0f, 1f, death);

			WildlifeFlight wings = WildlifeFlight.Evaluate(spec, species.RunSpeed, speed, wingPhase, attack, fallen);
			wingPhase = Mathf.Repeat(wingPhase + wings.BeatsPerSecond * Mathf.PI * 2f * dt, Mathf.PI * 2f);
			float legSwing = spec.WingSpan > 0f ? gait.LegSwing * 0.15f : gait.LegSwing;
			float bob = spec.HoverHeight > 0f ? 0f : gait.Bob;

			block.SetVector(GaitId, new Vector4(phase, legSwing, gait.Gallop, bob));
			block.SetVector(PoseId, new Vector4(attack, fallen, wag, spec.Width * 0.5f));
			flight = new Vector4(wings.WingAngle, wings.Height, AppearHidden(), 0f);
			block.SetVector(FlightId, flight);
			bodyRenderer.SetPropertyBlock(block);
		}

		/// <summary>
		/// The share of the body still dissolved by the fade-in, 1 at the spawn to 0 after
		/// <see cref="CharacterAnimationController.AppearSeconds"/> (FishWildlife.hlsl clips it through the shared screen-door
		/// pattern, in every pass). Ends the fade-in once whole.
		/// </summary>
		private float AppearHidden()
		{
			if (appearStarted < 0f)
			{
				return 0f;
			}
			float shown = (Time.unscaledTime - appearStarted) / CharacterAnimationController.AppearSeconds;
			if (shown >= 1f)
			{
				appearStarted = -1f;
				return 0f;
			}
			return 1f - Mathf.Max(0f, shown);
		}

		/// <summary>Sends the fade-in step alone (the pose keeps what was last sent), while the animal is fading in.</summary>
		private void SendAppear()
		{
			if (bodyRenderer == null || block == null || (appearStarted < 0f && flight.z == 0f))
			{
				return;
			}
			flight.z = AppearHidden();
			block.SetVector(FlightId, flight);
			bodyRenderer.SetPropertyBlock(block);
		}
#endif

		// ── ICharacterAnimationController ──────────────────────────────────────

		public void TriggerAttack()
		{
#if !UNITY_SERVER
			attackTimer = AttackSeconds;
#endif
		}

		public void TriggerCast() => TriggerAttack();

		public void TriggerDeath()
		{
#if !UNITY_SERVER
			deathTarget = 1f;
#endif
		}

		public void ResetDeath()
		{
#if !UNITY_SERVER
			deathTarget = 0f;
			death = 0f;
#endif
		}

		public void SetSpeed(float speed) { }
		public void SetGrounded(bool grounded) { }
		public void SetCrouching(bool crouching) { }
		public void SetSwimming(bool swimming, float pitch) { }
		public void SetClimbing(bool climbing, float rate) { }
		public void TriggerJump() { }
		public void SetBlocking(bool blocking) { }
		public void TriggerRoll() { }
		public void TriggerDeflect() { }
		public void SetRootMotion(bool enabled) { }
	}

	/// <summary>
	/// How an animal's legs move at a speed: steps per second, leg swing, how far into a gallop, and the body's bob. Pure,
	/// so it is tested on its own.
	/// </summary>
	public readonly struct WildlifeGait
	{
		public readonly float StepsPerSecond;
		public readonly float LegSwing;
		public readonly float Gallop;
		public readonly float Bob;

		private WildlifeGait(float steps, float swing, float gallop, float bob)
		{
			StepsPerSecond = steps;
			LegSwing = swing;
			Gallop = gallop;
			Bob = bob;
		}

		/// <param name="spec">The body (its leg length sets the stride).</param>
		/// <param name="runSpeed">The species' run speed: a gallop is reached near it.</param>
		/// <param name="speed">Measured ground speed, m/s.</param>
		/// <param name="alive">1 alive, 0 dead (no gait).</param>
		public static WildlifeGait Evaluate(WildlifeBodySpec spec, float runSpeed, float speed, float alive)
		{
			if (alive <= 0f || speed < 0.05f)
			{
				return new WildlifeGait(0f, 0f, 0f, 0f);
			}
			float leg = Mathf.Max(spec.LegLength, 0.1f);
			// Faster legs are longer strides: a stride grows from 1.4 legs at a walk to 3.5 at full gallop.
			float gallop = Mathf.Clamp01((speed - leg * 2.5f) / Mathf.Max(runSpeed * 0.7f - leg * 2.5f, 0.1f));
			float stride = leg * Mathf.Lerp(1.4f, 3.5f, gallop);
			float steps = speed / stride;
			float swing = Mathf.Lerp(0.35f, 0.75f, gallop) * Mathf.Clamp01(speed / (leg * 1.2f));
			float bob = gallop * 0.05f * spec.ShoulderHeight;
			return new WildlifeGait(steps, swing, gallop, bob);
		}
	}

	/// <summary>
	/// How a winged animal flies at a moment: its wingbeat, the wing's angle and how high it is drawn over the ground the server
	/// moves it on (it swoops lower to bite and drops as it dies). Nothing for an animal without wings. Pure, so it is tested
	/// on its own.
	/// </summary>
	public readonly struct WildlifeFlight
	{
		/// <summary>Wingbeats a second.</summary>
		public readonly float BeatsPerSecond;

		/// <summary>The wing's angle up from level, radians.</summary>
		public readonly float WingAngle;

		/// <summary>Metres over the ground.</summary>
		public readonly float Height;

		private WildlifeFlight(float beats, float angle, float height)
		{
			BeatsPerSecond = beats;
			WingAngle = angle;
			Height = height;
		}

		/// <summary>How far a wing sweeps either side of its middle, radians.</summary>
		public const float BeatSweep = 0.85f;

		/// <param name="spec">The body (its wing span and flying height).</param>
		/// <param name="runSpeed">The species' run speed: beats quicken toward it.</param>
		/// <param name="speed">Measured ground speed, m/s.</param>
		/// <param name="wingPhase">Where in its beat the wing is, radians.</param>
		/// <param name="attack">The bite's lunge, 0..1.</param>
		/// <param name="fallen">0 alive .. 1 lying dead.</param>
		public static WildlifeFlight Evaluate(WildlifeBodySpec spec, float runSpeed, float speed, float wingPhase, float attack, float fallen)
		{
			if (spec.WingSpan <= 0f)
			{
				return new WildlifeFlight(0f, 0f, 0f);
			}
			float alive = 1f - Mathf.Clamp01(fallen);
			float haste = Mathf.Clamp01(speed / Mathf.Max(runSpeed, 0.1f));
			float beats = alive > 0f ? Mathf.Lerp(2.6f, 4.4f, haste) : 0f;
			// Dead, the wings lie flat on the ground with it.
			float angle = alive * (0.15f + BeatSweep * Mathf.Sin(wingPhase));
			// The body lifts on each downstroke; it swoops down to bite.
			float height = spec.HoverHeight > 0f
				? alive * (spec.HoverHeight * (1f - 0.6f * Mathf.Clamp01(attack)) + 0.12f * Mathf.Sin(wingPhase - 1.2f))
				: 0f;
			return new WildlifeFlight(beats, angle, Mathf.Max(0f, height));
		}
	}
}
