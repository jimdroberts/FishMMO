using UnityEngine;

public abstract class RegionAction : ScriptableObject
{
	public abstract void Invoke(Character character, Region region);
}