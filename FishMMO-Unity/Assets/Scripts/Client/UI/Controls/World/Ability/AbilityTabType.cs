namespace FishMMO.Client
{
	public enum AbilityTabType : byte
	{
		/// <summary>
		/// No tab selected.
		/// </summary>
		None = 0,
		/// <summary>
		/// Tab for all abilities.
		/// </summary>
		Ability,
		/// <summary>
		/// Tab for known abilities.
		/// </summary>
		KnownAbility,
		/// <summary>
		/// Tab for known ability events.
		/// </summary>
		KnownAbilityEvent,
	}
}