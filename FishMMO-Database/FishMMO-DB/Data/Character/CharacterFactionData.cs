namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character faction data transfer object.
	/// </summary>
	public struct CharacterFactionData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly int Value;

		public CharacterFactionData(long id, long characterID, int templateID, int value)
		{
			ID = id;
			CharacterID = characterID;
			TemplateID = templateID;
			Value = value;
		}
	}
}