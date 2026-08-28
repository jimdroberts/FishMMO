using FishMMO.Shared.Core;
using KinematicCharacterController;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Derives the point a character aims from, out of state the server already owns.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this is not sent.</b> The aim origin used to travel as
	/// <c>CharacterReplicateData.CameraPosition</c> — three raw floats, taken from the owning
	/// client verbatim. It reached <c>ITargetController.UpdateTarget</c>, which the server raycasts
	/// from, and <c>AbilityObject.Spawn</c>, which places the hitbox, without ever being compared
	/// against the caster's own position. A modified client therefore chose the point the server
	/// searched for victims from, and an ability's range was measured from that point rather than
	/// from the character, so any target anywhere in the scene was reachable. Deriving the origin
	/// here closes that by construction: there is no longer a field to lie in.
	/// </para>
	/// <para>
	/// <b>Why it stays deterministic.</b> Every peer computes this from the motor's transient
	/// position, which reconcile already keeps in agreement, and <c>KCCPlayer</c> (Order 80) runs
	/// before <c>AbilityController</c> (Order 100), so the motor has been advanced for the current
	/// tick before the origin is read. The ability simulation is deterministic and reads this on
	/// every peer, so it must not consult anything frame-rate dependent or client-local.
	/// </para>
	/// <para>
	/// <b>What changes for players.</b> Aim now originates at the character's eye rather than at
	/// the camera, which in third person sits behind and above. Direction is unaffected — that is
	/// still the camera forward, quantised by <see cref="AimDirectionCompression"/>. The visible
	/// difference is at close range and around cover: a character can no longer shoot from a
	/// vantage point its body does not occupy. That is the intended behaviour for a
	/// server-authoritative game, not a regression.
	/// </para>
	/// </remarks>
	public static class CharacterAimOrigin
	{
		/// <summary>
		/// Distance below the top of the capsule the eye sits at.
		/// </summary>
		/// <remarks>
		/// Taken off the live capsule rather than stored as an absolute height so crouching moves
		/// the origin with the body — the KCC swaps between <c>CapsuleHeight</c> and
		/// <c>CrouchedCapsuleHeight</c>, and an absolute eye height would leave a crouched
		/// character aiming from above its own head.
		/// </remarks>
		private const float EyeInsetFromTop = 0.15f;

		/// <summary>
		/// Fallback eye height for a character with no motor, in the motor's absence.
		/// </summary>
		private const float FallbackEyeHeight = 1.45f;

		/// <summary>
		/// Returns the world-space point <paramref name="character"/> aims from.
		/// </summary>
		/// <remarks>
		/// Handles both kinds of character through one call, which is what keeps a player and an
		/// NPC resolving their origin the same way. A player has a KCC motor and its capsule gives
		/// a crouch-aware eye. An NPC has none — it moves under a NavMeshAgent — so its origin
		/// comes off the transform, which every peer already holds via its NetworkTransform. In
		/// neither case is anything read from the owning client.
		/// </remarks>
		/// <param name="character">The character casting. May be null.</param>
		/// <returns>The aim origin in world space.</returns>
		public static Vector3 Resolve(ICharacter character)
		{
			if (character == null)
			{
				return Vector3.zero;
			}

			KinematicCharacterMotor motor = character is IPlayerCharacter player ? player.Motor : null;
			return Resolve(motor, character.Transform);
		}

		/// <summary>
		/// Returns the world-space point <paramref name="motor"/>'s character aims from.
		/// </summary>
		/// <param name="motor">The character's motor. May be null.</param>
		/// <param name="fallbackTransform">Used when there is no motor. May be null.</param>
		/// <returns>The aim origin in world space.</returns>
		public static Vector3 Resolve(KinematicCharacterMotor motor, Transform fallbackTransform)
		{
			if (motor == null)
			{
				return fallbackTransform != null
					? fallbackTransform.position + Vector3.up * FallbackEyeHeight
					: Vector3.zero;
			}

			/* TransientPosition rather than transform.position: the KCC writes the simulated
			 * position here first and only flushes it to the transform afterwards, so during a
			 * replicate the transform can still hold the previous tick's value. Reading the
			 * transform would make the origin lag the simulation by a tick on the owner while
			 * matching it on the server. */
			float height = motor.Capsule != null ? motor.Capsule.height : FallbackEyeHeight;
			return motor.TransientPosition + motor.CharacterUp * Mathf.Max(0f, height - EyeInsetFromTop);
		}
	}
}
