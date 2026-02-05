using System;

namespace FishMMO.Database.Exceptions
{
	/// <summary>
	/// Exception thrown when a requested entity or record is not found in the database.
	/// Not transient - indicates the data does not exist.
	/// </summary>
	public sealed class DatabaseEntityNotFoundException : DatabaseException
	{
		/// <summary>
		/// Gets the sanitized entity type that was not found (e.g., "Character", "Account").
		/// </summary>
		public string EntityType { get; }

		/// <summary>
		/// Gets the sanitized identifier used in the search.
		/// </summary>
		public string Identifier { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseEntityNotFoundException"/> class.
		/// </summary>
		/// <param name="entityType">Sanitized entity type name.</param>
		/// <param name="identifier">Sanitized identifier (e.g., username without value, "by name").</param>
		public DatabaseEntityNotFoundException(string entityType, string identifier)
			: base(
				safeMessage: $"The requested {entityType?.ToLowerInvariant() ?? "item"} was not found.",
				detailedMessage: $"Entity of type '{entityType}' with identifier '{identifier}' was not found.",
				errorCode: "DB_NOT_FOUND")
		{
			EntityType = entityType ?? "unknown";
			Identifier = identifier ?? "unknown";
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseEntityNotFoundException"/> class with custom message.
		/// </summary>
		/// <param name="entityType">Sanitized entity type name.</param>
		/// <param name="identifier">Sanitized identifier.</param>
		/// <param name="safeMessage">Custom safe message for clients.</param>
		public DatabaseEntityNotFoundException(string entityType, string identifier, string safeMessage)
			: base(
				safeMessage: safeMessage ?? "The requested item was not found.",
				detailedMessage: $"Entity of type '{entityType}' with identifier '{identifier}' was not found.",
				errorCode: "DB_NOT_FOUND")
		{
			EntityType = entityType ?? "unknown";
			Identifier = identifier ?? "unknown";
		}
	}
}