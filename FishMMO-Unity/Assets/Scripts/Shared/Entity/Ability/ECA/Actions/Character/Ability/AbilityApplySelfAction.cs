using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Ability Apply Self Action", menuName = "FishMMO/Actions/Ability Apply Self")]
	public class AbilityApplySelfAction : BaseAction
	{
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out TargetEventData targetEventData))
			{
			}
			else
			{
				Log.Warning("AbilityApplySelfAction: Expected TargetEventData.");
			}
		}
	}
}