public class RegionApplyBuffAction : RegionAction
{
	public BuffTemplate buff;

	public override void Invoke(Character character, Region region)
	{
		if (buff == null || character == null)
		{
			return;
		}
		character.BuffController.Apply(buff);
	}
}