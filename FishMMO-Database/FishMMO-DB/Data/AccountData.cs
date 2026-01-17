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
		public string Name { get; set; }

		/// <summary>
		/// Gets or sets the password salt for SRP authentication.
		/// </summary>
		public string Salt { get; set; }

		/// <summary>
		/// Gets or sets the password verifier for SRP authentication.
		/// </summary>
		public string Verifier { get; set; }

		/// <summary>
		/// Gets or sets the account access level.
		/// </summary>
		public byte AccessLevel { get; set; }

		/// <summary>
		/// Gets or sets the account creation timestamp (UTC).
		/// </summary>
		public DateTime Created { get; set; }

		/// <summary>
		/// Gets or sets the last login timestamp (UTC).
		/// </summary>
		public DateTime LastLogin { get; set; }
	}
}