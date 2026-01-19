namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character attribute data transfer object.
	/// </summary>
	public struct CharacterAttributeData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly int Value;
		public readonly float CurrentValue;

		public CharacterAttributeData(long id, long characterID, int templateID, int value, float currentValue)
		{
			ID = id;
			CharacterID = characterID;
			TemplateID = templateID;
			Value = value;
			CurrentValue = currentValue;
		}
	}
}