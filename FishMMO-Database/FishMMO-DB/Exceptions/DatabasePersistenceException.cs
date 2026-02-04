using System;

namespace FishMMO.Database.Exceptions
{
	/// <summary>
	/// Exception thrown when persistence cannot be completed and the caller must decide how to proceed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This exception is intended to be mapped to a safe <c>DatabaseResult</c> without leaking internal SQL/connection details.
	/// The error is typically transient (e.g., repeated timeouts or deadlocks), but callers should re-read and/or apply
	/// higher-level retry/compensation strategies as appropriate.
	/// </para>
	/// </remarks>
	public sealed class DatabasePersistenceException : DatabaseException
	{
		private const string DefaultSafeMessage = "The database could not persist the changes.";

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabasePersistenceException"/> class.
		/// </summary>
		public DatabasePersistenceException()
			: base(safeMessage: DefaultSafeMessage, errorCode: "PERSISTENCE_FAILED", isTransient: true)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabasePersistenceException"/> class.
		/// </summary>
		/// <param name="safeMessage">Safe message suitable for client display.</param>
		public DatabasePersistenceException(string safeMessage)
			: base(safeMessage: string.IsNullOrWhiteSpace(safeMessage) ? DefaultSafeMessage : safeMessage,
				errorCode: "PERSISTENCE_FAILED",
				isTransient: true)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabasePersistenceException"/> class.
		/// </summary>
		/// <param name="safeMessage">Safe message suitable for client display.</param>
		/// <param name="innerException">The exception that caused persistence to fail.</param>
		public DatabasePersistenceException(string safeMessage, Exception innerException)
			: base(
				safeMessage: string.IsNullOrWhiteSpace(safeMessage) ? DefaultSafeMessage : safeMessage,
				innerException: innerException,
				errorCode: "PERSISTENCE_FAILED",
				isTransient: true)
		{
		}
	}
}