using UnityEngine;

public class RegionDisplayNameAction : RegionAction
{
	public Color displayColor;

	public override void Invoke(Character character, Region region)
	{
		if (region == null || character == null)
		{
			return;
		}
		//UILabel3D.Create(region.regionName, 24, displayColor, true, character.transform);
	}
}