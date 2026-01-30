using System;

namespace FishMMO.Database.Exceptions
{
	/// <summary>
	/// Thrown when database persistence fails after maximum retry attempts.
	/// </summary>
	public class DatabasePersistenceException : Exception
	{
		public DatabasePersistenceException(string message) : base(message)
		{
		}

		public DatabasePersistenceException(string message, Exception innerException) : base(message, innerException)
		{
		}
	}
}
