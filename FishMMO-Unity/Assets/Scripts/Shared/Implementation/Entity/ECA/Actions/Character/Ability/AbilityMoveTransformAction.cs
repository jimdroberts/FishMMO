using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that moves an ability object along a straight line from its spawn pose, at the
	/// ability's speed, evaluated in closed form from the object's integer tick count.
	/// </summary>
	[Serializable]
	public class AbilityMoveTransformAction : BaseAction
	{
		/// <summary>
		/// The direction the transform should move. Vector3(0,0,1) is forward, Vector3(1,0,0) is right, Vector3(0,1,0) is up.
		/// </summary>
		[Tooltip("The direction the transform should move. Vector3(0,0,1) is forward, Vector3(1,0,0) is right, Vector3(0,1,0) is up.")]
		public Vector3 MoveDirection;

		/// <summary>
		/// Executes the move transform action, moving the target transform in the specified direction based on ability speed.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing tick and target information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out AbilityTickEventData tickData) && tickData.AbilityObject != null)
			{
				AbilityObject abilityObject = tickData.AbilityObject;

				/* Closed form, not accumulation.
				 *
				 * `position += rotation * dir * speed * dt` is reproducible only while every peer
				 * takes exactly the same number of steps with exactly the same float state, and it
				 * drifts over a long lifetime as the sum accumulates rounding. Evaluating from the
				 * spawn pose and an integer tick count is reproducible from the spawn tuple alone —
				 * which is precisely what an observer that rebuilt this object from
				 * AbilityActivatedBroadcast holds — and yields the same position for the same tick
				 * regardless of how the peer arrived at that tick. Tick rate is still baked into the
				 * trajectory (ElapsedTicks * DeltaTime), so it must stay fixed; that was already true. */
				float elapsedSeconds = abilityObject.ElapsedTicks * tickData.DeltaTime;
				// AbilityObject.Transform is cached in Awake; fall back to the component's own
				// transform so an object that has not been through Awake (edit-mode construction)
				// evaluates the same closed form rather than dereferencing null.
				Transform abilityTransform = abilityObject.Transform != null ? abilityObject.Transform : abilityObject.transform;
				abilityTransform.position =
					abilityObject.SpawnPosition +
					abilityObject.SpawnRotation * MoveDirection * (abilityObject.Speed * elapsedSeconds);
			}
			else
			{
				Log.Warning("MoveTransformAction", "Expected AbilityTickEventData.");
			}
		}
	}
}