using System;

namespace FishMMO.Database.Exceptions
{
	/// <summary>
	/// Exception thrown to represent an operation-level failure that should surface as a specific
	/// <see cref="DatabaseResult"/> error code/message (including non-SQL business failures).
	/// </summary>
	/// <remarks>
	/// This is primarily used to bridge services that return <see cref="DatabaseResult"/> values into
	/// retry-safe/idempotent execution pipelines that require failures to be represented as exceptions.
	/// </remarks>
	public sealed class DatabaseOperationFailedException : DatabaseException
	{
		/// <summary>
		/// Gets the operation name.
		/// </summary>
		public string Operation { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseOperationFailedException"/> class.
		/// </summary>
		/// <param name="operation">Sanitized operation name (e.g., "SaveGuildMembership").</param>
		/// <param name="errorCode">Stable error code to return in <see cref="DatabaseResult"/>.</param>
		/// <param name="safeMessage">Safe message for callers.</param>
		/// <param name="isTransient">Whether the failure is transient.</param>
		/// <param name="innerException">Optional inner exception.</param>
		public DatabaseOperationFailedException(
			string operation,
			string errorCode,
			string safeMessage,
			bool isTransient = false,
			Exception? innerException = null)
			: base(
				safeMessage: safeMessage ?? "A database operation failed.",
				detailedMessage: safeMessage ?? "A database operation failed.",
				errorCode: string.IsNullOrWhiteSpace(errorCode) ? "DATABASE_ERROR" : errorCode,
				isTransient: isTransient)
		{
			Operation = string.IsNullOrWhiteSpace(operation) ? "unknown" : operation;

			if (innerException != null)
			{
				Data["InnerExceptionType"] = innerException.GetType().Name;
			}
		}
	}
}