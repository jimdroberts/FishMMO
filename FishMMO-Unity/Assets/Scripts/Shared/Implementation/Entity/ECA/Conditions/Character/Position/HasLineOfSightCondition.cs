using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Passes when nothing blocks the line from the initiator to the evaluated character.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Tests world geometry, never characters.</b> <see cref="Blockers"/> should name terrain and
	/// structures. Including the character layer would make the answer depend on where every OTHER
	/// character stands, which is both the peer-position sensitivity this project works to avoid and
	/// a rule players cannot predict — a teammate walking through the line would cancel an ability
	/// mid-cast. Terrain does not move, so this needs no lag compensation to be correct, and it
	/// stays correct at any latency.
	/// </para>
	/// <para>
	/// This mirrors the line-of-sight option on <c>TargetedEntitySelector</c> deliberately: the same
	/// test, available as a condition so it can gate abilities that select their targets some other
	/// way.
	/// </para>
	/// </remarks>
	[Serializable]
	public class HasLineOfSightCondition : BaseCondition
	{
		/// <summary>Layers treated as sight-blocking.</summary>
		/// <remarks>
		/// Empty means nothing blocks, so the condition passes. That is the safe default for an
		/// unconfigured condition: refusing every cast because a layer mask was left unset is a
		/// silent, total failure, while passing is merely the absence of a restriction the author
		/// has not finished expressing.
		/// </remarks>
		[Tooltip("Layers that block sight. Should contain terrain and structures, never characters.")]
		public LayerMask Blockers;

		/// <summary>Vertical offset applied to both ends of the test, so it does not clip the ground.</summary>
		[Tooltip("Eye height for both ends of the line, so the test does not clip the ground.")]
		[Min(0f)]
		public float EyeHeight = 1.45f;

		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter subject = eventData?.TargetCharacter ?? initiator;

			if (initiator == null || subject == null)
			{
				return false;
			}

			Transform from = initiator.Transform;
			Transform to = subject.Transform;
			if (from == null || to == null)
			{
				return false;
			}

			// A character can always see itself; the segment below would be zero length anyway.
			if (ReferenceEquals(from, to))
			{
				return true;
			}

			if (Blockers.value == 0)
			{
				return true;
			}

			GameObject initiatorObject = initiator.GameObject;
			if (initiatorObject == null)
			{
				return false;
			}

			Vector3 eye = from.position + Vector3.up * EyeHeight;
			Vector3 aim = to.position + Vector3.up * EyeHeight;
			Vector3 delta = aim - eye;
			float distance = delta.magnitude;
			if (distance <= 0.001f)
			{
				return true;
			}

			/* The character's OWN physics scene, not the global Physics API. A scene server hosts
			 * many scenes at once and the global API queries the default one, which holds none of
			 * the colliders around this character — the same trap TargetController documents. */
			PhysicsScene physicsScene = initiatorObject.scene.GetPhysicsScene();

			return !physicsScene.Raycast(eye, delta / distance, out _, distance,
				Blockers, QueryTriggerInteraction.Ignore);
		}
	}
}
