using FishMMO.Database;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// Turns a failed <see cref="DatabaseResult"/> into the reply the panel gives for it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A failed result is one of three different things, and the panel used to answer all three the
	/// same way — a 400 carrying <c>ErrorMessage</c>:
	/// </para>
	/// <list type="bullet">
	/// <item><description>
	/// <b>Not found.</b> The row the request named does not exist. A 404, with the caller's own
	/// wording, because "no such account" is a sentence the page already knows how to say.
	/// </description></item>
	/// <item><description>
	/// <b>A refusal.</b> The service said no: a value out of range, a ticket above the actor's tier, a
	/// stale version, a code that does not redeem. The message was written by the service to be read,
	/// and it is passed through as a 400 exactly as before.
	/// </description></item>
	/// <item><description>
	/// <b>A fault.</b> The database failed: a dropped connection, a timeout, a statement that threw.
	/// Its message is built from the underlying exception (<c>BaseService.MapFinalException</c>) and
	/// can carry a host, a port or a column name, which is nobody's business in a browser — and a 400
	/// told the browser the request was wrong, when trying it again was the right thing to do. A 503
	/// with the caller's own wording, and the real message goes to the log, where it is useful.
	/// </description></item>
	/// </list>
	/// <para>
	/// A controller that treats a particular code specially (a 409 for a stale edit, a named
	/// collision) still does so before calling this. This is the answer for everything else.
	/// </para>
	/// </remarks>
	public static class DatabaseReplies
	{
		/// <summary>
		/// Whether a failure is the service refusing, rather than the database failing.
		/// </summary>
		/// <remarks>
		/// Every code here carries a message that was either written by a service for its reader or
		/// is one of the fixed, safe sentences the base service maps a constraint violation to.
		/// <c>INVALID_ARGUMENT</c> and <c>INVALID_OPERATION</c> are deliberately absent: they are
		/// built from framework exception text, including EF's inner exceptions.
		/// </remarks>
		public static bool IsRefusal(string? errorCode) => errorCode switch
		{
			DatabaseErrorCodes.ValidationError => true,
			DatabaseErrorCodes.CapacityExceeded => true,
			DatabaseErrorCodes.Forbidden => true,
			DatabaseErrorCodes.AlreadyExists => true,
			DatabaseErrorCodes.StaleState => true,
			DatabaseErrorCodes.DuplicateReplay => true,
			DatabaseErrorCodes.UniqueViolation => true,
			DatabaseErrorCodes.ForeignKeyViolation => true,
			DatabaseErrorCodes.NotNullViolation => true,
			DatabaseErrorCodes.CheckViolation => true,
			_ => false,
		};

		/// <summary>Whether a failure means the row asked for does not exist.</summary>
		public static bool IsNotFound(string? errorCode) => errorCode == DatabaseErrorCodes.NotFound;

		/// <summary>Whether a failure is the database failing, rather than an answer about the request.</summary>
		public static bool IsFault(string? errorCode) => !IsNotFound(errorCode) && !IsRefusal(errorCode);

		/// <summary>The reply for a failed result. See the remarks on this class.</summary>
		/// <param name="controller">The controller answering.</param>
		/// <param name="result">The failed result.</param>
		/// <param name="log">Where a fault's real message is written; null only when the caller has already logged it.</param>
		/// <param name="fault">What the reader is told when the database failed. Also the fallback for a refusal with no message.</param>
		/// <param name="notFound">What the reader is told when the row does not exist; the service's own message when null.</param>
		public static IActionResult Failure(ControllerBase controller, DatabaseResult result, ILogger? log, string fault, string? notFound = null) =>
			Failure(controller, result.ErrorCode, result.ErrorMessage, log, fault, notFound);

		/// <inheritdoc cref="Failure(ControllerBase, DatabaseResult, ILogger, string, string)"/>
		public static IActionResult Failure<T>(ControllerBase controller, DatabaseResult<T> result, ILogger? log, string fault, string? notFound = null) =>
			Failure(controller, result.ErrorCode, result.ErrorMessage, log, fault, notFound);

		private static IActionResult Failure(ControllerBase controller, string? errorCode, string? errorMessage, ILogger? log, string fault, string? notFound)
		{
			if (IsNotFound(errorCode))
			{
				return controller.NotFound(new { error = notFound ?? errorMessage ?? fault });
			}
			if (IsRefusal(errorCode))
			{
				return controller.BadRequest(new { error = errorMessage ?? fault });
			}

			log?.LogWarning("'{Endpoint}' for '{Actor}' failed on the database: [{Code}] {Message}",
				controller.ControllerContext?.ActionDescriptor?.DisplayName,
				controller.User?.Identity?.Name,
				errorCode, errorMessage);
			return controller.StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = fault });
		}

		/// <summary>
		/// The audit row's outcome for a failed result: the service's sentence for a refusal, and the
		/// code as well for a fault, so the row says which kind of failure it was.
		/// </summary>
		public static string? Outcome(DatabaseResult result) => Outcome(result.ErrorCode, result.ErrorMessage);

		/// <inheritdoc cref="Outcome(DatabaseResult)"/>
		public static string? Outcome<T>(DatabaseResult<T> result) => Outcome(result.ErrorCode, result.ErrorMessage);

		private static string? Outcome(string? errorCode, string? errorMessage) =>
			IsFault(errorCode) ? $"Failed: the database did not answer [{errorCode}] {errorMessage}" : errorMessage;
	}
}
