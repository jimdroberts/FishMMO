using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Ends the life of a one-shot FX instance spawned by <see cref="PlayFXAction"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>This exists because the action used to own no part of what it spawned.</b>
	/// <see cref="PlayFXAction.Execute"/> ended at a bare <c>Instantiate(FXPrefab, position,
	/// rotation)</c>: no parent, no owner, no end. Every effect was therefore one prefab-authoring
	/// mistake away from being permanent, and the shipped content made that mistake — <c>Fire.prefab</c>,
	/// played by the impact effect of Lesser Fireball, Orc Firebolt and Scroll of Flame Impact, is a
	/// <c>looping</c>, <c>prewarm</c> ambient burn with <c>stopAction: None</c>. A looping system
	/// with no stop action never reaches an end, and nothing else was going to end it, so each hit
	/// left one particle system behind in the world on every client that saw the hit, for the whole
	/// session (issue #258, and issue #269 once a scene change made them obvious).
	/// </para>
	/// <para>
	/// <b>What is left behind is only the visuals.</b> No <see cref="AbilityObject"/> is involved and
	/// none is spawned by this path, which is why walking into a leftover triggers no ability events:
	/// there is nothing there to hit. That is the whole of the defect — a particle system with no
	/// one to switch it off.
	/// </para>
	/// <para>
	/// <b>The fix belongs to the action, not to the prefab.</b> "Every FX prefab despawns itself" is
	/// a rule nothing enforces and the action cannot check, and it makes each new effect's lifetime
	/// an authoring accident. The action instead attaches this component to the instance it created,
	/// which measures what the effect needs and then destroys the instance. A prefab that already
	/// despawns itself (<c>Explosion.prefab</c>'s <c>stopAction: Destroy</c>) destroys the instance
	/// first, and this component goes with it.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class FXInstanceLifetime : MonoBehaviour
	{
		/// <summary>
		/// Shortest life an instance can be given.
		/// </summary>
		/// <remarks>
		/// A floor for an effect that measures as nothing at all — no particle system, no trail. It is
		/// deliberately not zero, so that "measurement found nothing to measure" cannot itself become
		/// an instant disappearance.
		/// </remarks>
		public const float MinimumLifetime = 1.0f;

		/// <summary>
		/// Added to every measurement so the effect's own last frame is not cut off.
		/// </summary>
		public const float LifetimePadding = 0.5f;

		/// <summary>
		/// Longest life an instance can be given.
		/// </summary>
		/// <remarks>
		/// A backstop, not a target: it only has to stop a prefab that reports something absurd from
		/// holding an instance for the rest of the session.
		/// </remarks>
		public const float MaximumLifetime = 60.0f;

		/// <summary>
		/// Seconds this instance lives after it is attached. Zero means it has not been measured yet.
		/// </summary>
		[Tooltip("Seconds this instance lives before it is destroyed. Filled in from the effect's own duration when the component is attached.")]
		[SerializeField]
		private float lifetime;

		private float elapsed;

		/// <summary>
		/// How long this instance lives, in seconds.
		/// </summary>
		public float Lifetime => lifetime;

		/// <summary>
		/// Bounds a spawned FX instance, measuring its life if it does not have one.
		/// </summary>
		/// <param name="instance">The instance to bound. Null is tolerated so callers can inline it.</param>
		/// <returns>The component now bounding the instance, or null if there was no instance.</returns>
		public static FXInstanceLifetime Attach(GameObject instance)
		{
			if (instance == null)
			{
				return null;
			}

			FXInstanceLifetime bound = instance.GetComponent<FXInstanceLifetime>();
			if (bound == null)
			{
				bound = instance.AddComponent<FXInstanceLifetime>();
			}

			/* Measuring here rather than only in Awake: AddComponent does not run Awake in edit mode,
			 * and an instance under an inactive root keeps its Awake until it is activated. */
			bound.Bound();
			return bound;
		}

		/// <summary>
		/// Gives this instance a life if it does not have one.
		/// </summary>
		/// <remarks>
		/// Idempotent, so it is safe both as the attach path and as the <c>Awake</c> path. A lifetime
		/// authored on the prefab, or measured on an earlier call, is left alone.
		/// </remarks>
		internal void Bound()
		{
			if (lifetime > 0f)
			{
				return;
			}

			lifetime = CalculateLifetime(gameObject);
		}

		private void Awake()
		{
			Bound();
		}

		/// <summary>
		/// Spends time against this instance's life and answers whether the life is over.
		/// </summary>
		/// <param name="deltaTime">Seconds to spend.</param>
		/// <returns>True once the instance has lived its whole life.</returns>
		/// <remarks>
		/// Split out of <see cref="Update"/> so the rule is assertable: <c>Time.deltaTime</c> is zero
		/// in edit mode, so a test that drove <c>Update</c> would never reach the end of anything. Call
		/// it once per frame; it is the caller's business to destroy the object when it says so.
		/// </remarks>
		internal bool Tick(float deltaTime)
		{
			elapsed += deltaTime;
			return elapsed >= lifetime;
		}

		private void Update()
		{
			if (Tick(Time.deltaTime))
			{
				Destroy(gameObject);
			}
		}

		/// <summary>
		/// Measures how long an effect needs in order to finish.
		/// </summary>
		/// <param name="instance">The FX instance to measure.</param>
		/// <returns>Seconds to keep the instance alive.</returns>
		/// <remarks>
		/// <para>
		/// Reads the systems' authored values rather than sampling the running ones, so the answer is
		/// available the instant the instance exists — there is no need to wait for a frame of
		/// simulation, and it is answerable in edit mode where nothing is simulating at all.
		/// </para>
		/// <para>
		/// Particle systems and trails are what an authored FX is made of here; a bespoke script or an
		/// <c>Animator</c> is not measured, and lands on <see cref="MinimumLifetime"/> unless the
		/// prefab sets <c>lifetime</c> itself. Sub-systems living outside the instance are not
		/// measured either. The measurement is generous on purpose — being a little long is invisible,
		/// being short truncates the effect.
		/// </para>
		/// </remarks>
		internal static float CalculateLifetime(GameObject instance)
		{
			if (instance == null)
			{
				return MinimumLifetime;
			}

			float duration = 0f;
			float startLifetime = 0f;
			bool looping = false;
			bool prewarm = false;

			/* Children included: an FX prefab's visible body is frequently a nested instance, which is
			 * a child object — Flame.prefab's fire, for instance. */
			ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
			for (int i = 0; i < systems.Length; i++)
			{
				ParticleSystem system = systems[i];
				if (system == null)
				{
					continue;
				}

				ParticleSystem.MainModule main = system.main;
				duration = Mathf.Max(duration, main.duration);
				startLifetime = Mathf.Max(startLifetime, main.startLifetime.constantMax);

				/* Read from every system, not from the first: a child is allowed to be the looping one. */
				looping |= main.loop;
				prewarm |= main.prewarm;
			}

			float trail = 0f;
			TrailRenderer[] trails = instance.GetComponentsInChildren<TrailRenderer>(true);
			for (int i = 0; i < trails.Length; i++)
			{
				if (trails[i] != null)
				{
					trail = Mathf.Max(trail, trails[i].time);
				}
			}

			/* One period: the system's own length, or the longest-lived particle it emits, whichever is
			 * longer. A single particle outlives its system (Fire.prefab is a 0.5s system emitting 1s
			 * particles), so the system length alone would cut the effect off halfway. */
			float period = Mathf.Max(Mathf.Max(duration, startLifetime), trail);

			/* A looped system never reaches an end of its own, and a prewarmed one has already spent a
			 * full period before the effect is first seen. Either way one more period is the shortest
			 * life the author can have meant. */
			if (looping || prewarm)
			{
				period += duration;
			}

			return Mathf.Clamp(period + LifetimePadding, MinimumLifetime, MaximumLifetime);
		}
	}
}
