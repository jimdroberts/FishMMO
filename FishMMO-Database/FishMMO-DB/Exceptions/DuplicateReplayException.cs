using System;

namespace FishMMO.Database.Exceptions
{
	/// <summary>
	/// Thrown when a version-gated write is attempted with an incoming Version equal to the persisted Version.
	/// This represents a duplicate replay (or double-submit) and must not be treated as success.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is a logical concurrency failure and should not be retried.
	/// </para>
	/// </remarks>
	public sealed class DuplicateReplayException : Exception
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="DuplicateReplayException"/> class.
		/// </summary>
		public DuplicateReplayException() : base(string.Empty)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DuplicateReplayException"/> class.
		/// </summary>
		/// <param name="message">Safe error message describing the duplicate replay.</param>
		public DuplicateReplayException(string message) : base(message)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DuplicateReplayException"/> class.
		/// </summary>
		/// <param name="message">Safe error message describing the duplicate replay.</param>
		/// <param name="innerException">The exception that caused this exception.</param>
		public DuplicateReplayException(string message, Exception innerException) : base(message, innerException)
		{
		}
	}
}