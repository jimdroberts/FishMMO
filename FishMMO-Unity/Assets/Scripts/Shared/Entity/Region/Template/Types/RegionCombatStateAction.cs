using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Region action that enables or disables combat state for the player character when invoked.
	/// </summary>
	[CreateAssetMenu(fileName = "New Region Combat State Action", menuName = "FishMMO/Region/Region Combat State", order = 1)]
	public class RegionCombatStateAction : RegionAction
	{
		/// <summary>
		/// If true, combat is enabled for the player character; if false, combat is disabled.
		/// </summary>
		public bool EnableCombat;

		/// <summary>
		/// Invokes the region action, setting the combat state for the player character if implemented.
		/// </summary>
		/// <param name="character">The player character whose combat state will be changed.</param>
		/// <param name="region">The region in which the action is triggered.</param>
		/// <param name="isReconciling">Indicates if the action is part of a reconciliation process.</param>
		public override void Invoke(IPlayerCharacter character, Region region, bool isReconciling)
		{
			// Intended logic: Set the combat state for the character using their CombatController.
			// This code is currently commented out, possibly due to incomplete implementation or dependency issues.
			/*
			if (character == null || character.CombatController == null)
			{
				return;
			}
			character.CombatController.SetCombatStatus(EnableCombat);
			*/
		}
	}
}