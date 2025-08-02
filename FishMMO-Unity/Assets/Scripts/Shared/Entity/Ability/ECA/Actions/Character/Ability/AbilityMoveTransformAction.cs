using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that moves a transform in a specified direction based on ability speed.
	/// </summary>
	[CreateAssetMenu(fileName = "New Ability Move Transform Action", menuName = "FishMMO/Triggers/Actions/Ability/Move Transform")]
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
			if (eventData.TryGet(out AbilityTickEventData tickData) && tickData.Target != null)
			{
				tickData.Target.position += tickData.Target.rotation * MoveDirection * tickData.AbilityObject.Ability.Speed * tickData.DeltaTime;
			}
			else
			{
				Log.Warning("MoveTransformAction", "Expected AbilityTickEventData.");
			}
		}

		/// <summary>
		/// Returns a formatted description of the move transform action for UI display.
		/// </summary>
		/// <returns>A string describing the move direction and ability speed.</returns>
		public override string GetFormattedDescription()
		{
			return $"Moves transform in direction <color=#FFD700>{MoveDirection}</color> based on ability speed.";
		}
	}
}