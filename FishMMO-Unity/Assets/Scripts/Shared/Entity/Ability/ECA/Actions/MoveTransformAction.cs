using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Move Transform Action", menuName = "FishMMO/Actions/Move Transform")]
	public class MoveTransformAction : BaseAction
	{
		[Tooltip("The direction the transform should move. Vector3(0,0,1) is forward, Vector3(1,0,0) is right, Vector3(0,1,0) is up.")]
		public Vector3 MoveDirection;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out MoveTickEventData tickData) && tickData.Target != null)
			{
				tickData.Target.position += tickData.Target.rotation * MoveDirection * tickData.Speed * tickData.DeltaTime;
			}
			else
			{
				Log.Warning("MoveTransformAction", "Expected MoveTickEventData.");
			}
		}
	}
}