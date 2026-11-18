namespace FishMMO.Shared
{
	/// <summary>
	/// Result types for character creation attempts, indicating success or specific failure reasons.
	/// All members have explicit byte values to prevent wire-protocol reordering when new values are inserted.
	/// </summary>
	public enum CharacterCreateResult : byte
	{
		/// <summary>Character creation succeeded.</summary>
		Success = 0,
		/// <summary>Too many characters exist for this account.</summary>
		TooMany = 1,
		/// <summary>Character name is invalid (e.g., contains forbidden characters or is empty).</summary>
		InvalidCharacterName = 2,
		/// <summary>Character name is already taken by another player.</summary>
		CharacterNameTaken = 3,
		/// <summary>Spawn location or spawner is invalid.</summary>
		InvalidSpawn = 4,
		/// <summary>An internal server error occurred during character creation.</summary>
		Error = 5,
	}
}
