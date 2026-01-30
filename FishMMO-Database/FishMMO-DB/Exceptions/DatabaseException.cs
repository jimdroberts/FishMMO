using System;

namespace FishMMO.Database.Exceptions
{
	/// <summary>
	/// Base exception for all database-related errors in the FishMMO database layer.
	/// Provides safe error messages for client communication while preserving detailed information for logging.
	/// Follows Open/Closed Principle: extensible through inheritance for specific error types.
	/// </summary>
	public class DatabaseException : Exception
	{
		/// <summary>
		/// Gets the error code for categorizing the exception type.
		/// Used for client-side error handling and logging aggregation.
		/// </summary>
		public string ErrorCode { get; protected set; }

		/// <summary>
		/// Gets the safe error message suitable for displaying to end users or clients.
		/// Does not contain sensitive information like SQL queries, connection strings, or internal details.
		/// </summary>
		public string SafeMessage { get; protected set; }

		/// <summary>
		/// Gets a value indicating whether this error is transient and may succeed on retry.
		/// </summary>
		public bool IsTransient { get; protected set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseException"/> class.
		/// </summary>
		/// <param name="safeMessage">Safe error message for client communication.</param>
		/// <param name="errorCode">Error code for categorization.</param>
		/// <param name="isTransient">Whether the error is transient and retryable.</param>
		public DatabaseException(string safeMessage, string errorCode = "DB_ERROR", bool isTransient = false)
			: base(safeMessage)
		{
			SafeMessage = safeMessage ?? "A database error occurred.";
			ErrorCode = errorCode ?? "DB_ERROR";
			IsTransient = isTransient;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseException"/> class with detailed internal message.
		/// </summary>
		/// <param name="safeMessage">Safe error message for client communication.</param>
		/// <param name="detailedMessage">Detailed message for logging (may contain sensitive data).</param>
		/// <param name="errorCode">Error code for categorization.</param>
		/// <param name="isTransient">Whether the error is transient and retryable.</param>
		public DatabaseException(string safeMessage, string detailedMessage, string errorCode = "DB_ERROR", bool isTransient = false)
			: base(detailedMessage)
		{
			SafeMessage = safeMessage ?? "A database error occurred.";
			ErrorCode = errorCode ?? "DB_ERROR";
			IsTransient = isTransient;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseException"/> class with an inner exception.
		/// </summary>
		/// <param name="safeMessage">Safe error message for client communication.</param>
		/// <param name="innerException">The exception that caused this exception.</param>
		/// <param name="errorCode">Error code for categorization.</param>
		/// <param name="isTransient">Whether the error is transient and retryable.</param>
		public DatabaseException(string safeMessage, Exception innerException, string errorCode = "DB_ERROR", bool isTransient = false)
			: base(safeMessage, innerException)
		{
			SafeMessage = safeMessage ?? "A database error occurred.";
			ErrorCode = errorCode ?? "DB_ERROR";
			IsTransient = isTransient;
		}

		/// <summary>
		/// Returns a string representation of the exception including error code.
		/// </summary>
		/// <returns>Formatted string with error code and message.</returns>
		public override string ToString()
		{
			return $"[{ErrorCode}] {SafeMessage}";
		}
	}
}