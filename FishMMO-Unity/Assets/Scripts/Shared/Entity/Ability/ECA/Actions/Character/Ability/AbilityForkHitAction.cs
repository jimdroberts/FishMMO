using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Ability Fork Hit Action", menuName = "FishMMO/Triggers/Actions/Ability/Ability Fork Hit")]
	public class AbilityForkHitAction : BaseAction
	{
		public float Arc = 180.0f;
		public float Distance = 60.0f;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out AbilityCollisionEventData abilityEventData))
			{
				var abilityObject = abilityEventData.AbilityObject;
				if (abilityObject != null)
				{
					abilityObject.Transform.rotation = abilityObject.Transform.forward.GetRandomConicalDirection(
						abilityObject.Transform.position, Arc, Distance, abilityObject.RNG);
				}
			}
		}
	}
}