using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Account data transfer object.
	/// </summary>
	public struct AccountData
	{
		/// <summary>
		/// Gets or sets the account name (unique identifier).
		/// </summary>
		public readonly string Name;

		/// <summary>
		/// Gets or sets the password salt for SRP authentication.
		/// </summary>
		public readonly string Salt;

		/// <summary>
		/// Gets or sets the password verifier for SRP authentication.
		/// </summary>
		public readonly string Verifier;

		/// <summary>
		/// Gets or sets the account access level.
		/// </summary>
		public readonly byte AccessLevel;

		/// <summary>
		/// Gets or sets the account creation timestamp (UTC).
		/// </summary>
		public readonly DateTime Created;

		/// <summary>
		/// Gets or sets the last login timestamp (UTC).
		/// </summary>
		public readonly DateTime LastLogin;

		public AccountData(string name, string salt, string verifier, byte accessLevel, DateTime created, DateTime lastLogin)
		{
			Name = name;
			Salt = salt;
			Verifier = verifier;
			AccessLevel = accessLevel;
			Created = created;
			LastLogin = lastLogin;
		}
	}
}