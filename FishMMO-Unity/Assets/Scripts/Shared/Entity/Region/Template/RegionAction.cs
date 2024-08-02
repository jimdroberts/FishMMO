using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class RegionAction : ScriptableObject
	{
		public abstract void Invoke(IPlayerCharacter character, Region region, bool isReconciling);
	}
}