using System;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Exceptions;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Maps an exception from a database operation to the failure every service reports: an error
	/// code, a message safe to hand to a caller, and whether retrying could help.
	/// </summary>
	/// <remarks>
	/// Lifted out of <see cref="BaseService{TEntity}"/>, which forwards to it, so code that runs
	/// outside the service wrappers — <see cref="UnitOfWorkService"/>, and raw-connection paths such
	/// as <c>CharacterSessionOwnershipService.AssertOwnershipAsync</c> — reports a failure exactly
	/// as the wrappers do. Those paths used to answer every exception, a unique violation or a
	/// stale write included, as a transient DATABASE_ERROR, so a caller that retries on
	/// <see cref="DatabaseResult.IsTransient"/> retried faults that could only fail again, and the
	/// log never named the real one (issue #267).
	/// </remarks>
	internal static class DatabaseExceptionMapper
	{
		private const string StaleStateDefaultMessage = "Write rejected due to an optimistic concurrency conflict.";
		private const string DuplicateReplayDefaultMessage = "Write rejected because the incoming Version equals the persisted Version (duplicate replay).";

		/// <summary>
		/// Outcome classification for exception handling in database operations.
		/// </summary>
		internal enum Outcome
		{
			/// <summary>A non-retryable stale state conflict (optimistic concurrency).</summary>
			StaleState,
			/// <summary>A duplicate replay exception (same version replay).</summary>
			DuplicateReplay,
			/// <summary>A transient failure that can be retried.</summary>
			Transient,
			/// <summary>A non-retryable terminal failure.</summary>
			Terminal
		}

		/// <summary>
		/// Maps any exception to (Code, Message, IsTransient), exactly as the service wrappers do.
		/// </summary>
		internal static (string Code, string Message, bool IsTransient) Map(Exception ex) =>
			MapOutcome(ex, Classify(ex, SqlStateHelper.TryGetPostgresSqlState(ex)));

		/// <summary>
		/// <see cref="Map"/> as a failed <see cref="DatabaseResult"/>.
		/// </summary>
		internal static DatabaseResult ToFailure(Exception ex)
		{
			var (code, message, isTransient) = Map(ex);
			return DatabaseResult.Failure(code, message, isTransient);
		}

		/// <summary>
		/// <see cref="Map"/> as a failed <see cref="DatabaseResult{TResult}"/>.
		/// </summary>
		internal static DatabaseResult<TResult> ToFailure<TResult>(Exception ex)
		{
			var (code, message, isTransient) = Map(ex);
			return DatabaseResult<TResult>.Failure(code, message, isTransient);
		}

		/// <summary>
		/// Classifies an exception to determine the appropriate handling strategy.
		/// </summary>
		internal static Outcome Classify(Exception ex, string? sqlState)
		{
			if (ex is DbUpdateConcurrencyException) return Outcome.StaleState;
			if (ex is StaleStateException) return Outcome.StaleState;
			if (ex is DuplicateReplayException) return Outcome.DuplicateReplay;
			if (SqlStateHelper.IsTransientDatabaseFailure(ex, sqlState)) return Outcome.Transient;
			return Outcome.Terminal;
		}

		/// <summary>
		/// Maps an exception outcome to error code, message, and transience.
		/// </summary>
		internal static (string Code, string Message, bool IsTransient) MapOutcome(Exception ex, Outcome outcome)
		{
			switch (outcome)
			{
				case Outcome.StaleState:
					var staleMessage = ex is StaleStateException staleEx ? staleEx.Message : StaleStateDefaultMessage;
					return (DatabaseErrorCodes.StaleState, staleMessage, false);
				case Outcome.DuplicateReplay:
					var dupMessage = string.IsNullOrWhiteSpace(ex.Message) ? DuplicateReplayDefaultMessage : ex.Message;
					return (DatabaseErrorCodes.DuplicateReplay, dupMessage, false);
				default:
					var sqlState = SqlStateHelper.TryGetPostgresSqlState(ex);
					return MapFinal(ex, sqlState);
			}
		}

		/// <summary>
		/// Maps a terminal (non-retryable) exception into a standardized failure code/message/transience tuple.
		/// </summary>
		/// <param name="ex">The exception.</param>
		/// <param name="sqlState">PostgreSQL SQLSTATE, if available.</param>
		/// <returns>A tuple of (Code, Message, IsTransient).</returns>
		internal static (string Code, string Message, bool IsTransient) MapFinal(Exception ex, string? sqlState)
		{
			if (ex is DuplicateReplayException duplicateEx)
			{
				var message = string.IsNullOrWhiteSpace(duplicateEx.Message) ? DuplicateReplayDefaultMessage : duplicateEx.Message;
				return (DatabaseErrorCodes.DuplicateReplay, message, false);
			}

			if (ex is OperationCanceledException)
			{
				return (DatabaseErrorCodes.Canceled, "The database operation was canceled.", false);
			}

			// Prefer explicit, safe database-layer exceptions.
			// These are designed to avoid leaking SQL/connection details to callers.
			if (ex is DatabaseException dbEx)
			{
				return (
					string.IsNullOrWhiteSpace(dbEx.ErrorCode) ? DatabaseErrorCodes.DatabaseError : dbEx.ErrorCode,
					string.IsNullOrWhiteSpace(dbEx.SafeMessage) ? "A database error occurred." : dbEx.SafeMessage,
					dbEx.IsTransient);
			}

			if (SqlStateHelper.IsPgBouncerConfigurationSqlState(sqlState))
			{
				return (DatabaseErrorCodes.InvalidConfiguration, "Database authentication configuration is invalid.", false);
			}

			if (sqlState == PostgresSqlState.UniqueViolation)
			{
				return (DatabaseErrorCodes.UniqueViolation, "The record already exists.", false);
			}

			if (sqlState == PostgresSqlState.ForeignKeyViolation)
			{
				return (DatabaseErrorCodes.ForeignKeyViolation, "A referenced record was not found.", false);
			}

			if (sqlState == PostgresSqlState.NotNullViolation)
			{
				return (DatabaseErrorCodes.NotNullViolation, "A required field was missing.", false);
			}

			if (sqlState == PostgresSqlState.CheckViolation)
			{
				return (DatabaseErrorCodes.CheckViolation, "One or more values were invalid.", false);
			}

			if (ex is ArgumentException argEx)
			{
				return (DatabaseErrorCodes.InvalidArgument, ExceptionDiagnosticHelper.SanitizeExceptionMessage(argEx.Message), false);
			}

			if (ex is InvalidOperationException invEx)
			{
				/* Carry the inner exception through. EF's LINQ failures say only "An exception was
				 * thrown while attempting to evaluate a LINQ query parameter expression. See the
				 * inner exception for more information." — and the inner exception was being
				 * dropped here, so the one sentence that names the actual fault never reached the
				 * log. An item snapshot rolling back on every save reported exactly that and
				 * nothing else, which is unactionable. */
				string invMessage = ExceptionDiagnosticHelper.SanitizeExceptionMessage(invEx.Message);
				for (Exception inner = invEx.InnerException; inner != null; inner = inner.InnerException)
				{
					invMessage += $" -> [{inner.GetType().Name}] {ExceptionDiagnosticHelper.SanitizeExceptionMessage(inner.Message)}";
				}
				return (DatabaseErrorCodes.InvalidOperation, invMessage, false);
			}

			// Sanitize the outermost exception message to strip .NET parameter-name annotations
			// while keeping the useful diagnostic content (error description, type info).
			// The exception type chain is appended for disambiguation — it helps distinguish
			// Npgsql mapping failures from constraint violations without exposing raw SQL.
			//
			// For PostgresException with a known SqlState, the sqlState was already handled
			// above (UniqueViolation, ForeignKeyViolation, etc.).  For unrecognised SqlStates,
			// the sanitized message preserves the PostgreSQL error text while the type chain
			// gives the protocol-level code for operators to look up.
			var isTransient = SqlStateHelper.IsTransientDatabaseFailure(ex, sqlState);
			var sanitizedMessage = ExceptionDiagnosticHelper.SanitizeExceptionMessage(ex.Message);
			var typeChain = ExceptionDiagnosticHelper.BuildSafeExceptionDiagnostic(ex);
			return (DatabaseErrorCodes.DatabaseError, $"A database error occurred. {sanitizedMessage} ({typeChain}).", isTransient);
		}
	}
}
