namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// Result codes for account operations.
	/// </summary>
	public enum AccountOperationResult
	{
		/// <summary>
		/// Operation completed successfully.
		/// </summary>
		Success,

		/// <summary>
		/// Account was created successfully.
		/// </summary>
		AccountCreated,

		/// <summary>
		/// Proceed to SRP verification stage.
		/// </summary>
		SrpVerify,

		/// <summary>
		/// Invalid username or password.
		/// </summary>
		InvalidCredentials,

		/// <summary>
		/// Account is banned.
		/// </summary>
		Banned,

		/// <summary>
		/// Database error occurred.
		/// </summary>
		DatabaseError,

		/// <summary>
		/// Account name already exists (unique constraint violation).
		/// </summary>
		UniqueConstraintViolation,

		/// <summary>
		/// Account not found.
		/// </summary>
		NotFound
	}
}