namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// PostgreSQL SQLSTATE codes for common constraint violations, connection errors, and transient failures.
	/// </summary>
	internal static class PostgresSqlState
	{
		#region Constraint Violations (Class 23)

		/// <summary>Unique constraint violation (23505).</summary>
		public const string UniqueViolation = "23505";

		/// <summary>Foreign key constraint violation (23503).</summary>
		public const string ForeignKeyViolation = "23503";

		/// <summary>Not null constraint violation (23502).</summary>
		public const string NotNullViolation = "23502";

		/// <summary>Check constraint violation (23514).</summary>
		public const string CheckViolation = "23514";

		#endregion

		#region Connection Errors (Class 08 and Admin Shutdown)

		/// <summary>Connection exception class prefix (08xxx).</summary>
		public const string ConnectionClassPrefix = "08";

		/// <summary>Admin shutdown (57P01).</summary>
		public const string AdminShutdown = "57P01";

		/// <summary>Crash shutdown (57P02).</summary>
		public const string CrashShutdown = "57P02";

		/// <summary>Cannot connect now (57P03).</summary>
		public const string CannotConnectNow = "57P03";

		/// <summary>Protocol violation (08P01), often seen with PgBouncer reload/reset mismatches.</summary>
		public const string ProtocolViolation = "08P01";

		#endregion

		#region Authentication / Authorization

		/// <summary>Invalid authorization specification (28000), commonly PgBouncer auth mismatch.</summary>
		public const string InvalidAuthorizationSpecification = "28000";

		#endregion

		#region Transient Failures

		/// <summary>Query cancelled / statement timeout (57014).</summary>
		public const string QueryCanceled = "57014";

		/// <summary>Deadlock detected (40P01).</summary>
		public const string DeadlockDetected = "40P01";

		/// <summary>Serialization failure (40001).</summary>
		public const string SerializationFailure = "40001";

		/// <summary>Lock not available / lock timeout (55P03).</summary>
		public const string LockNotAvailable = "55P03";

		/// <summary>Too many connections (53300).</summary>
		public const string TooManyConnections = "53300";

		/// <summary>Internal error (XX000), can occur transiently when backend drops behind PgBouncer.</summary>
		public const string InternalError = "XX000";

		#endregion
	}
}
