using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class RegionAction : ScriptableObject
	{
		public abstract void Invoke(Character character, Region region);
	}
}