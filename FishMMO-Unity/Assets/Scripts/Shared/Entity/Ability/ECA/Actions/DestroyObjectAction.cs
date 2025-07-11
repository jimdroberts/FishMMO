using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Destroy Object Action", menuName = "FishMMO/Actions/Destroy Object")]
	public class DestroyObjectAction : BaseAction
	{
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out TargetEventData targetEventData))
			{
				if (targetEventData.Target != null)
				{
					if (targetEventData.Immediate)
					{
						DestroyImmediate(targetEventData.Target);
					}
					else
					{
						Destroy(targetEventData.Target);
					}
				}
			}
			else
			{
				Log.Warning("DestroyObjectAction", "Expected TargetEventData.");
			}
		}
	}
}