namespace FishMMO.Shared
{
	public class RegionCombatStateAction : RegionAction
	{
		public bool EnableCombat;

		public override void Invoke(Character character, Region region)
		{
			/*if (character == null || character.CombatController == null)
			{
				return;
			}
			character.CombatController.SetCombatStatus(enableCombat);*/
		}
	}
}