namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character pet attribute data transfer object.
	/// </summary>
	public struct CharacterPetAttributeData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly int Value;

		public CharacterPetAttributeData(long id, long characterID, int templateID, int value)
		{
			ID = id;
			CharacterID = characterID;
			TemplateID = templateID;
			Value = value;
		}
	}
}