using System;

namespace FishMMO.Database.Exceptions
{
	/// <summary>
	/// Thrown when a version-gated write is attempted with an incoming Version equal to the persisted Version.
	/// This represents a duplicate replay (or double-submit) and must not be treated as success.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is a non-transient logical concurrency failure and should not be retried.
	/// <c>BaseService.ClassifyException</c> maps it to <see cref="DatabaseErrorCodes.DuplicateReplay"/>
	/// and returns a <see cref="DatabaseResult"/> failure.
	/// </para>
	/// </remarks>
	public sealed class DuplicateReplayException : DatabaseException
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="DuplicateReplayException"/> class.
		/// </summary>
		public DuplicateReplayException()
			: base(safeMessage: "Write rejected because the incoming Version equals the persisted Version (duplicate replay).",
				errorCode: DatabaseErrorCodes.DuplicateReplay)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DuplicateReplayException"/> class.
		/// </summary>
		/// <param name="message">Safe error message describing the duplicate replay.</param>
		public DuplicateReplayException(string message)
			: base(safeMessage: message, errorCode: DatabaseErrorCodes.DuplicateReplay)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DuplicateReplayException"/> class.
		/// </summary>
		/// <param name="message">Safe error message describing the duplicate replay.</param>
		/// <param name="innerException">The exception that caused this exception.</param>
		public DuplicateReplayException(string message, Exception innerException)
			: base(safeMessage: message, innerException: innerException, errorCode: DatabaseErrorCodes.DuplicateReplay)
		{
		}
	}
}