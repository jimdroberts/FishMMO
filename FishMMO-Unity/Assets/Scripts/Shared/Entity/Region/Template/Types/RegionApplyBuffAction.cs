using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Region action that applies a specified buff to a player character when invoked.
	/// </summary>
	[CreateAssetMenu(fileName = "New Region Apply Buff Action", menuName = "FishMMO/Region/Region Apply Buff", order = 1)]
	public class RegionApplyBuffAction : RegionAction
	{
		/// <summary>
		/// The buff template to apply to the player character when this region action is triggered.
		/// </summary>
		public BaseBuffTemplate Buff;

		/// <summary>
		/// Invokes the region action, applying the specified buff to the player character if possible.
		/// </summary>
		/// <param name="character">The player character to apply the buff to.</param>
		/// <param name="region">The region in which the action is triggered.</param>
		/// <param name="isReconciling">Indicates if the action is part of a reconciliation process.</param>
		public override void Invoke(IPlayerCharacter character, Region region, bool isReconciling)
		{
			// Only apply the buff if all required references are valid and the character has a buff controller.
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