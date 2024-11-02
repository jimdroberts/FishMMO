using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Region Apply Buff Action", menuName = "Region/Region Apply Buff", order = 1)]
	public class RegionApplyBuffAction : RegionAction
	{
		public BaseBuffTemplate Buff;

		public override void Invoke(IPlayerCharacter character, Region region, bool isReconciling)
		{
			if (Buff == null ||
				character == null ||
				!character.TryGet(out IBuffController buffController))
			{
				return;
			}
			buffController.Apply(Buff);
		}
	}
}