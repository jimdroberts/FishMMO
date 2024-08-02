using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Region Combat State Action", menuName = "Region/Region Combat State", order = 1)]
	public class RegionCombatStateAction : RegionAction
	{
		public bool EnableCombat;

		public override void Invoke(IPlayerCharacter character, Region region, bool isReconciling)
		{
			/*if (character == null || character.CombatController == null)
			{
				return;
			}
			character.CombatController.SetCombatStatus(enableCombat);*/
		}
	}
}