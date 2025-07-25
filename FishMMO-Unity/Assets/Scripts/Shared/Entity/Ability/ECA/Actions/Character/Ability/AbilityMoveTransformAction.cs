using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Ability Move Transform Action", menuName = "FishMMO/Triggers/Actions/Ability/Move Transform")]
	public class AbilityMoveTransformAction : BaseAction
	{
		[Tooltip("The direction the transform should move. Vector3(0,0,1) is forward, Vector3(1,0,0) is right, Vector3(0,1,0) is up.")]
		public Vector3 MoveDirection;

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

		public override string GetFormattedDescription()
		{
			return $"Moves transform in direction <color=#FFD700>{MoveDirection}</color> based on ability speed.";
		}
	}
}