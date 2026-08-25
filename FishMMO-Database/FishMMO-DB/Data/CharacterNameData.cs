namespace FishMMO.Database.Data
{
	/// <summary>
	/// A character's ID paired with its display name.
	/// </summary>
	/// <remarks>
	/// The result shape of a bulk name lookup. Deliberately carries nothing else: it exists to
	/// label rows in lists that are shown to other players, and anything more would make a
	/// display query into a way of reading character state.
	/// </remarks>
	public readonly struct CharacterNameData
	{
		/// <summary>The character's ID.</summary>
		public readonly long CharacterID;

		/// <summary>The character's display name.</summary>
		public readonly string Name;

		public CharacterNameData(long characterID, string name)
		{
			CharacterID = characterID;
			Name = name;
		}
	}
}
