using System;

namespace FishMMO.Database.Exceptions
{
	/// <summary>
	/// Exception thrown when a database operation is canceled by the caller.
	/// This is not a timeout and is not considered transient.
	/// </summary>
	public sealed class DatabaseOperationCanceledException : DatabaseException
	{
		/// <summary>
		/// Gets the operation name associated with the cancellation.
		/// </summary>
		public string Operation { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseOperationCanceledException"/> class.
		/// </summary>
		/// <param name="operation">Sanitized operation name.</param>
		/// <param name="innerException">The underlying cancellation exception.</param>
		public DatabaseOperationCanceledException(string operation, Exception innerException = null)
			: base(
				safeMessage: "The database operation was canceled.",
				detailedMessage: $"Database operation '{operation}' was canceled by the caller.",
				errorCode: "DB_CANCELED")
		{
			Operation = operation ?? "unknown";

			if (innerException != null)
			{
				Data["InnerExceptionType"] = innerException.GetType().Name;
			}
		}
	}
}