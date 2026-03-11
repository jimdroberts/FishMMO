namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character archetype data transfer object.
	/// </summary>
	public struct CharacterArchetypeData : IVersioned<CharacterArchetypeData>
	{
		/// <summary>
		/// Database row identifier.
		/// </summary>
		public readonly long ID;

		/// <summary>
		/// Logical version for optimistic concurrency control.
		/// </summary>
		public readonly long Version;

		/// <summary>
		/// Owning character identifier.
		/// </summary>
		public readonly long CharacterID;

		/// <summary>
		/// Template identifier referencing the archetype definition.
		/// </summary>
		public readonly int TemplateID;

		long IVersioned<CharacterArchetypeData>.Version => Version;

		/// <summary>
		/// Initializes a new instance without a version (defaults to 0).
		/// </summary>
		public CharacterArchetypeData(long id, long characterID, int templateID)
			: this(id, version: 0, characterID, templateID)
		{
		}

		/// <summary>
		/// Initializes a new instance with all fields.
		/// </summary>
		public CharacterArchetypeData(long id, long version, long characterID, int templateID)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
		}

		/// <inheritdoc/>
		public CharacterArchetypeData WithVersion(long newVersion)
		{
			return new CharacterArchetypeData(ID, newVersion, CharacterID, TemplateID);
		}
	}
}