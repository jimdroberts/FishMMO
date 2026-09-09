namespace FishMMO.Client
{
	/// <summary>
	/// The two kinds of thing the abilities panel holds.
	/// </summary>
	/// <remarks>
	/// The distinction the panel exists to make. An ability is finished and can be used; knowledge
	/// is a part that becomes an ability at an Ability Crafter. Mixing them was the reported
	/// confusion (issue #247): the slots looked identical, so a template read as an ability that
	/// simply refused to work.
	/// </remarks>
	public enum AbilityTabType : byte
	{
		/// <summary>Abilities the character has learned and can slot on a hotkey.</summary>
		Ability = 0,
		/// <summary>Base abilities and effects the character knows, which craft into abilities.</summary>
		Knowledge,
	}

	/// <summary>
	/// Narrows the knowledge tab to one kind of part.
	/// </summary>
	public enum KnowledgeFilterType : byte
	{
		/// <summary>Everything the character can craft with.</summary>
		All = 0,
		/// <summary>Base abilities: the core an ability is built around.</summary>
		BaseAbilities,
		/// <summary>Effects: what a base ability is configured with.</summary>
		Effects,
	}
}
