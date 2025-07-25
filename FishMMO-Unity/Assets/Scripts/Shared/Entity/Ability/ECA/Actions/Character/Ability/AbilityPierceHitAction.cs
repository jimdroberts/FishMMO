using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Ability Pierce Hit Action", menuName = "FishMMO/Triggers/Actions/Ability/Pierce Hit")]
	public class AbilityPierceHitAction : BaseAction
	{
		public int PierceCount = -1;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out AbilityCollisionEventData pierceEventData))
			{
				pierceEventData.AbilityObject.HitCount += PierceCount;
			}
		}

		public override string GetFormattedDescription()
		{
			return $"Pierces <color=#FFD700>{PierceCount}</color> additional targets.";
		}
	}
}