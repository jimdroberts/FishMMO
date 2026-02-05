namespace FishMMO.Database
{
	/// <summary>
	/// Standardized error codes for database operations across all services.
	/// Use these constants instead of inline strings to ensure consistency.
	/// </summary>
	public static class DatabaseErrorCodes
	{
		#region Validation Errors

		/// <summary>
		/// Input validation failed (invalid length, format, null/empty, etc.).
		/// </summary>
		public const string ValidationError = "VALIDATION_ERROR";

		/// <summary>
		/// An argument passed to a method was invalid.
		/// </summary>
		public const string InvalidArgument = "INVALID_ARGUMENT";

		/// <summary>
		/// The requested operation is not valid in the current state.
		/// </summary>
		public const string InvalidOperation = "INVALID_OPERATION";

		/// <summary>
		/// Configuration is invalid or missing.
		/// </summary>
		public const string InvalidConfiguration = "INVALID_CONFIGURATION";

		#endregion

		#region Entity Errors

		/// <summary>
		/// The requested entity was not found in the database.
		/// </summary>
		public const string NotFound = "NOT_FOUND";

		/// <summary>
		/// A duplicate entity already exists (unique constraint would be violated).
		/// </summary>
		public const string AlreadyExists = "ALREADY_EXISTS";

		/// <summary>
		/// The resource has reached its maximum capacity.
		/// </summary>
		public const string CapacityExceeded = "CAPACITY_EXCEEDED";

		#endregion

		#region Authorization Errors

		/// <summary>
		/// Access to the resource is forbidden (banned, unauthorized, etc.).
		/// </summary>
		public const string Forbidden = "FORBIDDEN";

		#endregion

		#region Concurrency Errors

		/// <summary>
		/// Optimistic concurrency conflict - the entity version is stale.
		/// </summary>
		public const string StaleState = "STALE_STATE";

		/// <summary>
		/// The operation was rejected because the incoming version equals the persisted version (duplicate replay).
		/// </summary>
		public const string DuplicateReplay = "DUPLICATE_REPLAY";

		#endregion

		#region Constraint Violations

		/// <summary>
		/// A unique constraint was violated.
		/// </summary>
		public const string UniqueViolation = "UNIQUE_VIOLATION";

		/// <summary>
		/// A foreign key constraint was violated.
		/// </summary>
		public const string ForeignKeyViolation = "FOREIGN_KEY_VIOLATION";

		/// <summary>
		/// A not-null constraint was violated.
		/// </summary>
		public const string NotNullViolation = "NOT_NULL_VIOLATION";

		/// <summary>
		/// A check constraint was violated.
		/// </summary>
		public const string CheckViolation = "CHECK_VIOLATION";

		#endregion

		#region Operation Errors

		/// <summary>
		/// The operation was canceled.
		/// </summary>
		public const string Canceled = "OPERATION_CANCELED";

		/// <summary>
		/// A generic database error occurred.
		/// </summary>
		public const string DatabaseError = "DATABASE_ERROR";

		/// <summary>
		/// Maximum retry attempts exceeded for transient failures.
		/// </summary>
		public const string MaxRetries = "MAX_RETRIES";

		/// <summary>
		/// Transaction rollback failed after an operation error.
		/// </summary>
		public const string RollbackFailed = "ROLLBACK_FAILED";

		/// <summary>
		/// The object has already been disposed.
		/// </summary>
		public const string ObjectDisposed = "OBJECT_DISPOSED";

		#endregion
	}
}