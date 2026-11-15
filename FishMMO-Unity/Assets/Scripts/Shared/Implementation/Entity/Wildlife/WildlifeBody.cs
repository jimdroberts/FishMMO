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
		private float attackTimer;
		private float death;
		private float deathTarget;
#endif

		public override void OnStartCharacter()
		{
			base.OnStartCharacter();
#if !UNITY_SERVER
			BuildBody();
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
#endif
		}

#if !UNITY_SERVER
		private void BuildBody()
		{
			species = Character is NPC npc ? npc.Wildlife : null;
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
				return;
			}

			WildlifeBodySpec spec = species.Body;
			WildlifeGait gait = WildlifeGait.Evaluate(spec, species.RunSpeed, speed, death > 0f ? 0f : 1f);
			phase = Mathf.Repeat(phase + gait.StepsPerSecond * Mathf.PI * 2f * dt, Mathf.PI * 2f);
			float attack = attackTimer > 0f ? Mathf.Sin((1f - attackTimer / AttackSeconds) * Mathf.PI) : 0f;
			float wag = Mathf.Sin(Time.time * (speed > 0.2f ? 9f : 4f)) * (speed > 0.2f ? 0.12f : 0.3f);

			block.SetVector(GaitId, new Vector4(phase, gait.LegSwing, gait.Gallop, gait.Bob));
			block.SetVector(PoseId, new Vector4(attack, Mathf.SmoothStep(0f, 1f, death), wag, spec.Width * 0.5f));
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
}
