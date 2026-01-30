using System;

namespace FishMMO.Database.Exceptions
{
	/// <summary>
	/// Thrown when an operation fails due to stale state (optimistic concurrency violation).
	/// This is a logical failure that should NOT be retried.
	/// </summary>
	public class StaleStateException : Exception
	{
		public StaleStateException(string message) : base(message)
		{
		}

		public StaleStateException(string message, Exception innerException) : base(message, innerException)
		{
		}
	}
}