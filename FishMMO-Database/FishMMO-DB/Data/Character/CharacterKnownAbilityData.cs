namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character known ability data transfer object.
	/// </summary>
	public struct CharacterKnownAbilityData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly int TemplateID;

		public CharacterKnownAbilityData(long id, long characterID, int templateID)
		{
			ID = id;
			CharacterID = characterID;
			TemplateID = templateID;
		}
	}
}