using System;

namespace FishMMO.Database.Exceptions
{
	/// <summary>
	/// Exception thrown when a database operation times out.
	/// Considered transient - operation may succeed on retry with better conditions.
	/// </summary>
	public sealed class DatabaseTimeoutException : DatabaseException
	{
		/// <summary>
		/// Gets the timeout duration in seconds.
		/// </summary>
		public int TimeoutSeconds { get; }

		/// <summary>
		/// Gets the operation that timed out (sanitized, e.g., "CharacterQuery" not full SQL).
		/// </summary>
		public string Operation { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseTimeoutException"/> class.
		/// </summary>
		/// <param name="operation">Sanitized operation name.</param>
		/// <param name="timeoutSeconds">Timeout duration in seconds.</param>
		/// <param name="innerException">The underlying timeout exception.</param>
		public DatabaseTimeoutException(string operation, int timeoutSeconds, Exception innerException = null)
			: base(
				safeMessage: "The database operation took too long. Please try again.",
				detailedMessage: $"Database operation '{operation}' timed out after {timeoutSeconds} seconds.",
				errorCode: "DB_TIMEOUT",
				isTransient: true)
		{
			Operation = operation ?? "unknown";
			TimeoutSeconds = timeoutSeconds;

			if (innerException != null)
			{
				Data["InnerExceptionType"] = innerException.GetType().Name;
			}
		}
	}
}