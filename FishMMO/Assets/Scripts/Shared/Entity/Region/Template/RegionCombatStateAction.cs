// do we want a combat state???
public class RegionCombatStateAction : RegionAction
{
	public bool enableCombat;

	public override void Invoke(Character character, Region region)
	{
		/*if (character == null || character.CombatController == null)
		{
			return;
		}
		character.CombatController.SetCombatStatus(enableCombat);*/
	}
}