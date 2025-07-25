using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Destroy Object Action", menuName = "FishMMO/Triggers/Actions/Destroy Object")]
	public class DestroyObjectAction : BaseAction
	{
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out TargetEventData targetEventData))
			{
				if (targetEventData.Target != null)
				{
					Destroy(targetEventData.Target);
					targetEventData.Target.SetActive(false);
				}
			}
			else
			{
				Log.Warning("DestroyObjectAction", "Expected TargetEventData.");
			}
		}
	}
}