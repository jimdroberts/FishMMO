namespace FishMMO.Shared
{
	public class RegionApplyBuffAction : RegionAction
	{
		public BuffTemplate Buff;

		public override void Invoke(Character character, Region region)
		{
			if (Buff == null || character == null)
			{
				return;
			}
			character.BuffController.Apply(Buff);
		}
	}
}