using System;
using System.Runtime.CompilerServices;
using Npgsql;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Utility methods for PostgreSQL SQLSTATE code classification.
	/// </summary>
	internal static class SqlStateHelper
	{
		/// <summary>
		/// Extracts the PostgreSQL SQLSTATE from an exception chain, if present.
		/// </summary>
		/// <param name="exception">The exception to inspect.</param>
		/// <returns>The SQLSTATE code, or null if not found.</returns>
		public static string? TryGetPostgresSqlState(Exception exception)
		{
			for (var current = exception; current != null; current = current.InnerException)
			{
				if (current is PostgresException pgEx) return pgEx.SqlState;
			}
			return null;
		}

		/// <summary>
		/// Determines whether an exception represents a transient failure that is safe to retry.
		/// </summary>
		/// <param name="exception">The exception.</param>
		/// <param name="sqlState">The PostgreSQL SQLSTATE, if available.</param>
		/// <returns>True if the failure is considered transient; otherwise false.</returns>
		/// <remarks>
		/// Cancellation is never treated as transient.
		/// Transience is determined from <see cref="NpgsqlException.IsTransient"/>, well-known SQLSTATE codes,
		/// and certain exception types such as <see cref="TimeoutException"/>.
		/// </remarks>
		public static bool IsTransientDatabaseFailure(Exception exception, string? sqlState)
		{
			if (exception is OperationCanceledException) return false;

			for (var current = exception; current != null; current = current.InnerException)
			{
				if (current is NpgsqlException npgsqlEx && npgsqlEx.IsTransient) return true;
			}

			if (exception is TimeoutException) return true;

			if (!string.IsNullOrWhiteSpace(sqlState))
			{
				return IsTimeoutSqlState(sqlState) || IsConnectionSqlState(sqlState) || IsTransientSqlState(sqlState);
			}

			return false;
		}

		/// <summary>
		/// Determines whether a SQLSTATE represents a query cancellation/timeout (57014).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsTimeoutSqlState(string? sqlState) =>
			string.Equals(sqlState, PostgresSqlState.QueryCanceled, StringComparison.Ordinal);

		/// <summary>
		/// Determines whether a SQLSTATE represents a connection-level failure.
		/// Includes 08xxx (connection errors) and 57Pxx (admin shutdown).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsConnectionSqlState(string? sqlState)
		{
			if (string.IsNullOrWhiteSpace(sqlState)) return false;
			return sqlState.StartsWith(PostgresSqlState.ConnectionClassPrefix, StringComparison.Ordinal)
				|| sqlState == PostgresSqlState.AdminShutdown
				|| sqlState == PostgresSqlState.CrashShutdown
				|| sqlState == PostgresSqlState.CannotConnectNow;
		}

		/// <summary>
		/// Determines whether a SQLSTATE represents a transient, retryable server-side failure.
		/// Includes deadlock (40P01), serialization failure (40001), lock timeout (55P03), 
		/// and too many connections (53300).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsTransientSqlState(string? sqlState)
		{
			if (string.IsNullOrWhiteSpace(sqlState)) return false;
			return sqlState == PostgresSqlState.DeadlockDetected
				|| sqlState == PostgresSqlState.SerializationFailure
				|| sqlState == PostgresSqlState.LockNotAvailable
				|| sqlState == PostgresSqlState.TooManyConnections;
		}
	}
}
