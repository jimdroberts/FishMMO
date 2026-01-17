namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// Result codes for character operations.
	/// </summary>
	public enum CharacterOperationResult
	{
		/// <summary>
		/// Operation completed successfully.
		/// </summary>
		Success,

		/// <summary>
		/// Character was created successfully.
		/// </summary>
		CharacterCreated,

		/// <summary>
		/// Character was deleted successfully.
		/// </summary>
		CharacterDeleted,

		/// <summary>
		/// Character was updated successfully.
		/// </summary>
		CharacterUpdated,

		/// <summary>
		/// Character name already exists.
		/// </summary>
		NameAlreadyExists,

		/// <summary>
		/// Character not found.
		/// </summary>
		NotFound,

		/// <summary>
		/// Character is currently online and cannot be modified.
		/// </summary>
		CharacterOnline,

		/// <summary>
		/// Character limit reached for account.
		/// </summary>
		LimitReached,

		/// <summary>
		/// Invalid character name.
		/// </summary>
		InvalidName,

		/// <summary>
		/// Database error occurred.
		/// </summary>
		DatabaseError
	}
}