using System;

namespace FishMMO.Database.Exceptions
{
	/// <summary>
	/// Exception thrown when a database connection cannot be established or is lost.
	/// Always considered transient - retry logic should be applied.
	/// </summary>
	public sealed class DatabaseConnectionException : DatabaseException
	{
		/// <summary>
		/// Gets the host address that failed to connect (sanitized).
		/// </summary>
		public string Host { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseConnectionException"/> class.
		/// </summary>
		/// <param name="host">Sanitized host identifier (e.g., "production-db" instead of full address).</param>
		/// <param name="innerException">The underlying connection exception.</param>
		public DatabaseConnectionException(string host, Exception innerException)
			: base(
				safeMessage: "Unable to connect to the database. Please try again later.",
				detailedMessage: $"Failed to connect to database host '{host}': {innerException?.Message}",
				errorCode: "DB_CONNECTION_FAILED",
				isTransient: true)
		{
			Host = host ?? "unknown";
			if (innerException != null)
			{
				Data["InnerExceptionType"] = innerException.GetType().Name;
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseConnectionException"/> class with custom message.
		/// </summary>
		/// <param name="safeMessage">Safe message for clients.</param>
		/// <param name="detailedMessage">Detailed message for logging.</param>
		/// <param name="host">Sanitized host identifier.</param>
		public DatabaseConnectionException(string safeMessage, string detailedMessage, string host)
			: base(safeMessage, detailedMessage, "DB_CONNECTION_FAILED", isTransient: true)
		{
			Host = host ?? "unknown";
		}
	}
}