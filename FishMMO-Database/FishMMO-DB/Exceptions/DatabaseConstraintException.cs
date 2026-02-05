using System;

namespace FishMMO.Database.Exceptions
{
	/// <summary>
	/// Exception thrown when a database constraint is violated (unique, foreign key, check, etc.).
	/// Not transient - indicates data validation error or business logic violation.
	/// </summary>
	public sealed class DatabaseConstraintException : DatabaseException
	{
		/// <summary>
		/// Gets the type of constraint that was violated.
		/// </summary>
		public ConstraintType ConstraintType { get; }

		/// <summary>
		/// Gets the sanitized constraint name (if available).
		/// </summary>
		public string ConstraintName { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseConstraintException"/> class.
		/// </summary>
		/// <param name="constraintType">Type of constraint violated.</param>
		/// <param name="constraintName">Sanitized constraint name.</param>
		/// <param name="safeMessage">Safe message for clients.</param>
		/// <param name="innerException">The underlying constraint violation exception.</param>
		public DatabaseConstraintException(
			ConstraintType constraintType,
			string constraintName,
			string safeMessage,
			Exception innerException = null)
			: base(
				safeMessage: safeMessage ?? "The operation conflicts with existing data.",
				detailedMessage: $"Constraint violation: {constraintType} constraint '{constraintName}' failed.",
				errorCode: $"DB_CONSTRAINT_{constraintType.ToString().ToUpperInvariant()}")
		{
			ConstraintType = constraintType;
			ConstraintName = constraintName ?? "unknown";

			if (innerException != null)
			{
				Data["InnerExceptionType"] = innerException.GetType().Name;
			}
		}
	}

	/// <summary>
	/// Types of database constraints that can be violated.
	/// </summary>
	public enum ConstraintType
	{
		/// <summary>
		/// Unknown constraint type.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// Unique constraint violation (duplicate key).
		/// </summary>
		Unique = 1,

		/// <summary>
		/// Foreign key constraint violation.
		/// </summary>
		ForeignKey = 2,

		/// <summary>
		/// Check constraint violation.
		/// </summary>
		Check = 3,

		/// <summary>
		/// Not null constraint violation.
		/// </summary>
		NotNull = 4,

		/// <summary>
		/// Primary key constraint violation.
		/// </summary>
		PrimaryKey = 5
	}
}