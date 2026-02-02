using System;

namespace FishMMO.Database.Exceptions
{
	/// <summary>
	/// Exception thrown when a database query execution fails.
	/// May be transient (deadlock, temporary lock) or permanent (syntax error, permission denied).
	/// </summary>
	public sealed class DatabaseQueryException : DatabaseException
	{
		/// <summary>
		/// Gets the sanitized operation name that failed.
		/// </summary>
		public string Operation { get; }

		/// <summary>
		/// Gets the PostgreSQL error code if available (e.g., "23505" for a unique constraint conflict).
		/// </summary>
		public string? PostgreSqlErrorCode { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseQueryException"/> class.
		/// </summary>
		/// <param name="operation">Sanitized operation name (e.g., "CreateCharacter").</param>
		/// <param name="safeMessage">Safe message for clients.</param>
		/// <param name="detailedMessage">Detailed message for logging.</param>
		/// <param name="isTransient">Whether the error is transient.</param>
		/// <param name="postgreSqlErrorCode">PostgreSQL-specific error code.</param>
		/// <param name="innerException">The underlying query exception.</param>
		public DatabaseQueryException(
			string operation,
			string safeMessage,
			string detailedMessage,
			bool isTransient = false,
			string? postgreSqlErrorCode = null,
			Exception? innerException = null)
			: base(
				safeMessage: safeMessage ?? "A database error occurred while processing your request.",
				detailedMessage: detailedMessage,
				errorCode: "DB_QUERY_FAILED",
				isTransient: isTransient)
		{
			Operation = operation ?? "unknown";
			PostgreSqlErrorCode = postgreSqlErrorCode;

			if (innerException != null)
			{
				Data["InnerExceptionType"] = innerException.GetType().Name;
			}

			if (!string.IsNullOrWhiteSpace(postgreSqlErrorCode))
			{
				Data["PostgreSqlErrorCode"] = postgreSqlErrorCode;
			}
		}
	}
}