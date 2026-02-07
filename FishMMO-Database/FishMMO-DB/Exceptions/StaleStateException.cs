using System;

namespace FishMMO.Database.Exceptions
{
	/// <summary>
	/// Thrown when an operation fails due to stale state (optimistic concurrency violation).
	/// The incoming version is behind the persisted version, indicating a conflicting write occurred.
	/// This is a non-transient, non-retryable logical failure.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Services throw this exception inside <c>BaseService</c> execution wrappers.
	/// <c>BaseService.ClassifyException</c> maps it to <see cref="DatabaseErrorCodes.StaleState"/>
	/// and returns a <see cref="DatabaseResult"/> failure. Callers should re-read the entity
	/// and reconcile before reattempting the operation.
	/// </para>
	/// </remarks>
	public sealed class StaleStateException : DatabaseException
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="StaleStateException"/> class.
		/// </summary>
		/// <param name="message">A message describing the concurrency conflict.</param>
		public StaleStateException(string message)
			: base(safeMessage: message, errorCode: DatabaseErrorCodes.StaleState)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="StaleStateException"/> class.
		/// </summary>
		/// <param name="message">A message describing the concurrency conflict.</param>
		/// <param name="innerException">The exception that caused this concurrency failure.</param>
		public StaleStateException(string message, Exception innerException)
			: base(safeMessage: message, innerException: innerException, errorCode: DatabaseErrorCodes.StaleState)
		{
		}
	}
}