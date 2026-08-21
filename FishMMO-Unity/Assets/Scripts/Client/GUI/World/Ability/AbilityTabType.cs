namespace FishMMO.Client
{
	/// <summary>
	/// Tabs available in the abilities panel.
	/// </summary>
	public enum AbilityTabType : byte
	{
		/// <summary>Abilities the character has learned and can slot.</summary>
		Ability = 0,
		/// <summary>Ability templates the character knows.</summary>
		KnownAbility,
		/// <summary>Ability event templates the character knows.</summary>
		KnownAbilityEvent,
	}
}
