namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character pet buff data transfer object.
	/// </summary>
	public struct CharacterPetBuffData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly int Level;
		public readonly float BuffTimeEnd;

		public CharacterPetBuffData(long id, long characterID, int templateID, int level, float buffTimeEnd)
		{
			ID = id;
			CharacterID = characterID;
			TemplateID = templateID;
			Level = level;
			BuffTimeEnd = buffTimeEnd;
		}
	}
}