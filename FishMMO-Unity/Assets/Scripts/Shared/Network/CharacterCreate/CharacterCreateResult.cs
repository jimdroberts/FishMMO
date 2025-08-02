namespace FishMMO.Shared
{
	/// <summary>
	/// Result types for character creation attempts, indicating success or specific failure reasons.
	/// </summary>
	public enum CharacterCreateResult : byte
	{
		/// <summary>Character creation succeeded.</summary>
		Success = 0,
		/// <summary>Too many characters exist for this account.</summary>
		TooMany = 1,
		/// <summary>Character name is invalid (e.g., contains forbidden characters or is empty).</summary>
		InvalidCharacterName,
		/// <summary>Character name is already taken by another player.</summary>
		CharacterNameTaken,
		/// <summary>Spawn location or spawner is invalid.</summary>
		InvalidSpawn,
	}
}