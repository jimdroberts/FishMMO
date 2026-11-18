using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// A short, purely cosmetic displacement of a character's model, played the moment a hit is
	/// predicted rather than when the server's correction arrives.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why not just predict the knockback.</b> A knockback moves the target, and on the attacker's
	/// client that target is a peer driven by <c>NetworkTransform</c>. Displacing it locally is
	/// overwritten by the next transform update — one to three ticks away at the streaming LOD band
	/// — so the character snaps back, which reads worse than the delay it was meant to hide. The
	/// authoritative position cannot be predicted here and should not be.
	/// </para>
	/// <para>
	/// <b>What this does instead.</b> It offsets <see cref="ICharacter.MeshRoot"/>, a child of the
	/// networked root that nothing else writes. The root keeps receiving the server's position
	/// untouched; the model leans off it and decays back to zero over
	/// <see cref="ReactionSeconds"/>. By the time the server's real displacement arrives the offset
	/// has largely resolved, so the two compose instead of fighting — the player sees the reaction on
	/// the frame they hit, and the translation catching up half a round trip later is invisible.
	/// </para>
	/// <para>
	/// <b>Cosmetic, and only cosmetic.</b> Nothing here is read by the simulation, by hit detection,
	/// or by lag compensation — all of which work from the root transform and the position history.
	/// A client that lies to itself with this changes nothing anybody else can observe.
	/// </para>
	/// </remarks>
	/// <para>
	/// <b>Deliberately not under Prediction/.</b> It decays on frames, not ticks, and the prediction
	/// path is guarded against wall-clock reads for good reason — anything that advances on
	/// <c>Time.deltaTime</c> cannot be replayed deterministically. This is a visual that happens to
	/// be triggered from a prediction, not a part of one.
	/// </para>
	public class CharacterHitReaction : MonoBehaviour
	{
		/// <summary>
		/// The transform to lean. Falls back to <see cref="ICharacter.MeshRoot"/> when unset.
		/// </summary>
		/// <remarks>
		/// Explicit rather than resolved only by interface lookup, so the component does not depend
		/// on being added after the character behaviour, and so a prefab can point it at a different
		/// child than the model root if the rig wants that.
		/// </remarks>
		[Tooltip("Transform to lean on impact. Falls back to the character's mesh root when unset.")]
		[SerializeField]
		private Transform leanTarget;

		/// <summary>How long a reaction takes to decay back to rest, in seconds.</summary>
		/// <remarks>
		/// Short enough to be gone before the server's own displacement lands at a typical round
		/// trip, so the two do not visibly add. Longer than a couple of frames, or it reads as a
		/// glitch rather than as an impact.
		/// </remarks>
		[Tooltip("Seconds for a hit reaction to decay back to rest.")]
		[Min(0.01f)]
		public float ReactionSeconds = 0.18f;

		/// <summary>Metres of lean at full strength.</summary>
		/// <remarks>
		/// Deliberately small. This is a flinch, not a simulation of the knockback — the real
		/// displacement is the server's, and a large offset here would visibly double the motion
		/// when the authoritative one arrives.
		/// </remarks>
		[Tooltip("Maximum lean distance in metres at full strength.")]
		[Min(0f)]
		public float MaximumOffset = 0.35f;

		private Transform meshRoot;
		private Vector3 restLocalPosition;
		private bool restCaptured;

		private Vector3 offsetDirection;
		private float offsetMagnitude;
		private float remaining;

		/// <summary>
		/// Seconds a predicted shove may wait, after its own slide has ended, for the server's real displacement to catch it
		/// up before the lead it still holds fades out. A round trip plus the transform stream's interpolation, with room to
		/// spare; a shove the server refused (an immortal target, the harm rule) fades over this instead of snapping.
		/// </summary>
		[Tooltip("Seconds a predicted shove waits for the server's real displacement before any lead it still holds fades out.")]
		[Min(0.05f)]
		public float ShoveCatchUpSeconds = 0.6f;

		// A predicted shove (Shove): the slide the server will make, drawn on the model until the root has made it.
		private bool shoving;
		private Vector3 shoveDirection;
		private Vector3 shoveOrigin;
		private float shoveSpeed;
		private float shoveDeceleration;
		private float shoveElapsed;

		/// <summary>True while a reaction is still decaying.</summary>
		public bool IsPlaying => remaining > 0f || shoving;

		/// <summary>
		/// Starts a reaction in a world-space direction.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Restarts rather than accumulates: a burst of hits produces one lean at the newest
		/// direction, not a model that walks away from its own root. Accumulating would let a
		/// multi-hit ability push the mesh arbitrarily far from the character it belongs to.
		/// </para>
		/// <para>
		/// Silently does nothing without a mesh root — an NPC or a character whose model has not
		/// loaded has nothing to lean.
		/// </para>
		/// </remarks>
		/// <param name="worldDirection">Direction of the impact. Normalised internally; zero is ignored.</param>
		/// <param name="strength">0 to 1, scaling <see cref="MaximumOffset"/>.</param>
		public void Play(Vector3 worldDirection, float strength = 1f)
		{
			if (!EnsureMeshRoot())
			{
				return;
			}

			Vector3 flat = worldDirection;
			flat.y = 0f;
			if (flat.sqrMagnitude <= 0.0001f)
			{
				return;
			}

			offsetDirection = flat.normalized;
			offsetMagnitude = MaximumOffset * Mathf.Clamp01(strength);
			remaining = ReactionSeconds;
		}

		/// <summary>
		/// Starts a predicted SHOVE: the model slides along <paramref name="worldDirection"/> as the server's knockback will move
		/// the character, starting now rather than a round trip from now.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The slide follows the same law the server applies (<paramref name="speed"/> bled off at
		/// <paramref name="deceleration"/>), measured from where the networked root stood when it started. Each frame the model
		/// is offset only by the part of that slide the ROOT has not yet made: as the server's real displacement arrives through
		/// the transform stream the offset shrinks to nothing, so the two hand over without a jump and never add up. Whatever
		/// lead is left when the slide is over fades out over <see cref="ShoveCatchUpSeconds"/>.
		/// </para>
		/// <para>
		/// This is what the knockback's displacement could not be on the attacker's client (the root belongs to the server),
		/// done where it can be: on the model, which nothing networked reads.
		/// </para>
		/// </remarks>
		/// <param name="worldDirection">The push's direction; only its horizontal part is used.</param>
		/// <param name="speed">The push, metres per second.</param>
		/// <param name="deceleration">How fast it bleeds off, metres per second per second.</param>
		public void Shove(Vector3 worldDirection, float speed, float deceleration)
		{
			if (!EnsureMeshRoot())
			{
				return;
			}
			Vector3 flat = worldDirection;
			flat.y = 0f;
			if (flat.sqrMagnitude <= 0.0001f || speed <= 0f || deceleration <= 0f)
			{
				return;
			}
			shoveDirection = flat.normalized;
			shoveOrigin = transform.position;
			shoveSpeed = speed;
			shoveDeceleration = deceleration;
			shoveElapsed = 0f;
			shoving = true;
		}

		/// <summary>
		/// How far the model leads its root along a shove: the slide predicted so far less the part the root has made, faded
		/// out once the slide is over. Pure, for the tests.
		/// </summary>
		internal static float ShoveLead(float speed, float deceleration, float elapsed, float rootTravel, float catchUpSeconds)
		{
			float stopTime = speed / deceleration;
			float t = Mathf.Min(elapsed, stopTime);
			float predicted = speed * t - 0.5f * deceleration * t * t;
			float lead = Mathf.Clamp(predicted - rootTravel, 0f, predicted);
			if (elapsed > stopTime)
			{
				float fade = 1f - (elapsed - stopTime) / Mathf.Max(0.01f, catchUpSeconds);
				lead *= Mathf.Clamp01(fade);
			}
			return lead;
		}

		/// <summary>
		/// Decays the current reaction. Runs in <c>LateUpdate</c> so the offset is applied AFTER
		/// anything that writes the character's transform this frame.
		/// </summary>
		private void LateUpdate()
		{
			Step(Time.deltaTime);
		}

		/// <summary>
		/// Advances the decay by a time step.
		/// </summary>
		/// <remarks>
		/// Split out of <see cref="LateUpdate"/> so the schedule can be driven with an explicit
		/// delta. Unity supplies no frame loop in EditMode and its delta in batch mode can exceed a
		/// whole reaction, which makes a component that reads the clock directly untestable — and
		/// this one has enough easing and edge behaviour to be worth testing.
		/// </remarks>
		/// <param name="deltaSeconds">Seconds elapsed since the last step.</param>
		internal void Step(float deltaSeconds)
		{
			if (meshRoot == null || (remaining <= 0f && !shoving))
			{
				return;
			}

			Vector3 worldOffset = Vector3.zero;

			if (remaining > 0f)
			{
				remaining -= deltaSeconds;
				if (remaining > 0f)
				{
					/* Eased rather than linear: an impact should land hard and settle, and a constant-rate
					 * return reads like the model sliding rather than recoiling. */
					float t = remaining / ReactionSeconds;
					worldOffset += offsetDirection * (offsetMagnitude * t * t);
				}
				else
				{
					remaining = 0f;
				}
			}

			if (shoving)
			{
				shoveElapsed += deltaSeconds;
				float rootTravel = Vector3.Dot(transform.position - shoveOrigin, shoveDirection);
				float lead = ShoveLead(shoveSpeed, shoveDeceleration, shoveElapsed, rootTravel, ShoveCatchUpSeconds);
				if (shoveElapsed > shoveSpeed / shoveDeceleration + ShoveCatchUpSeconds)
				{
					shoving = false;
				}
				else
				{
					worldOffset += shoveDirection * lead;
				}
			}

			// Local space, because the offset should follow the character as it turns (Vector, not Direction: metres).
			Vector3 localOffset = meshRoot.parent != null ? meshRoot.parent.InverseTransformVector(worldOffset) : worldOffset;
			meshRoot.localPosition = restLocalPosition + localOffset;
		}

		/// <summary>
		/// Returns the model to rest. Called when the character is despawned or pooled.
		/// </summary>
		/// <remarks>
		/// A pooled character keeps its transforms, so a reaction left mid-decay would be inherited
		/// by whoever the object is reused as — a model visibly off-centre for no reason.
		/// </remarks>
		private void OnDisable()
		{
			ResetToRest();
		}

		/// <summary>
		/// Cancels any reaction and returns the model to rest immediately.
		/// </summary>
		/// <remarks>
		/// Public so a pooling or teardown path can call it directly rather than relying on
		/// <c>OnDisable</c> firing — the object may be recycled without ever being disabled.
		/// </remarks>
		public void ResetToRest()
		{
			remaining = 0f;
			shoving = false;
			if (meshRoot != null && restCaptured)
			{
				meshRoot.localPosition = restLocalPosition;
			}
		}

		/// <summary>
		/// Resolves the mesh root and remembers where it sits at rest.
		/// </summary>
		/// <remarks>
		/// Captured lazily rather than in Awake: the model is instantiated under the mesh root after
		/// the race template resolves, so reading it too early can capture a transform that has not
		/// been positioned yet.
		/// </remarks>
		private bool EnsureMeshRoot()
		{
			if (meshRoot != null)
			{
				return true;
			}

			if (leanTarget != null)
			{
				meshRoot = leanTarget;
			}
			else
			{
				ICharacter character = GetComponent<ICharacter>();
				meshRoot = character?.MeshRoot;
			}
			if (meshRoot == null)
			{
				return false;
			}

			restLocalPosition = meshRoot.localPosition;
			restCaptured = true;
			return true;
		}

		/// <summary>
		/// Plays a reaction on a character, adding the component the first time it is needed.
		/// </summary>
		/// <remarks>
		/// Added on demand rather than authored on prefabs: no character prefab carried it, so the predicted flinch never
		/// played anywhere. Only a client that plays a predicted reaction ever reaches this, so a server never adds one.
		/// </remarks>
		/// <param name="character">The character being hit.</param>
		/// <param name="worldDirection">Direction of the impact.</param>
		/// <param name="strength">0 to 1.</param>
		public static void PlayOn(ICharacter character, Vector3 worldDirection, float strength = 1f)
		{
			Resolve(character)?.Play(worldDirection, strength);
		}

		/// <summary>Plays a predicted shove (<see cref="Shove"/>) on a character, adding the component when needed.</summary>
		public static void ShoveOn(ICharacter character, Vector3 worldDirection, float speed, float deceleration)
		{
			Resolve(character)?.Shove(worldDirection, speed, deceleration);
		}

		private static CharacterHitReaction Resolve(ICharacter character)
		{
			GameObject gameObject = character?.GameObject;
			if (gameObject == null)
			{
				return null;
			}
			if (!gameObject.TryGetComponent(out CharacterHitReaction reaction))
			{
				reaction = gameObject.AddComponent<CharacterHitReaction>();
			}
			return reaction;
		}
	}
}
