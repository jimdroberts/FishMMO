namespace FishMMO.Shared
{
	/// <summary>
	/// Shared utility methods for the AI subsystem. Provides common operations
	/// used across ability rotations, boss scripts, and attacking states to
	/// eliminate duplication (DRY).
	/// </summary>
	public static class AIUtility
	{
		/// <summary>
		/// Finds the first <see cref="Ability"/> instance whose template ID matches
		/// the specified value. Used by ability rotations, boss scripts, and any system
		/// that needs to resolve a template ID to a learned ability instance.
		/// </summary>
		/// <param name="abilityController">The NPC's ability controller containing known abilities.</param>
		/// <param name="templateID">The ability template ID to search for.</param>
		/// <returns>The matching ability instance, or null if not found.</returns>
		public static Ability FindAbilityByTemplate(IAbilityController abilityController, int templateID)
		{
			if (abilityController == null)
				return null;

			foreach (var kvp in abilityController.KnownAbilities)
			{
				Ability ability = kvp.Value;
				if (ability != null && ability.Template != null && ability.Template.ID == templateID)
					return ability;
			}
			return null;
		}
	}
}
