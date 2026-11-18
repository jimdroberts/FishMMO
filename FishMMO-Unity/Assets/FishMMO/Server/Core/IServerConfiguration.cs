namespace FishMMO.Server.Core
{
	/// <summary>
	/// Interface for a service that manages server configuration.
	/// Provides methods to retrieve and save configuration values for different server types.
	/// </summary>
	public interface IServerConfiguration
	{
		/// <summary>
		/// Saves default configuration values if they are missing for the specified server type and working directory.
		/// </summary>
		/// <param name="serverTypeName">The name of the server type.</param>
		/// <param name="serverType">The type of the server.</param>
		/// <param name="workingDirectory">The working directory for the server.</param>
		void SaveDefaultsIfMissing(string serverTypeName, string serverType, string workingDirectory);

		/// <summary>
		/// Retrieves a string value from the configuration.
		/// </summary>
		/// <param name="key">The configuration key.</param>
		/// <param name="defaultValue">The default value to return if the key is not found.</param>
		/// <returns>The string value associated with the key, or the default value if not found.</returns>
		string GetString(string key, string defaultValue);

		/// <summary>
		/// Attempts to retrieve a string value from the configuration for the
		/// specified <paramref name="key"/>.
		/// </summary>
		/// <param name="key">The configuration key to look up.</param>
		/// <param name="value">When this method returns <c>true</c>, contains the string value associated with <paramref name="key"/>; otherwise <c>null</c> or an empty string as provided by the underlying implementation.</param>
		/// <returns><c>true</c> if the key exists and a value was retrieved; otherwise <c>false</c>.</returns>
		bool TryGetString(string key, out string value);

		/// <summary>
		/// Retrieves an unsigned short value from the configuration.
		/// </summary>
		/// <param name="key">The configuration key.</param>
		/// <param name="defaultValue">The default value to return if the key is not found.</param>
		/// <returns>The ushort value associated with the key, or the default value if not found.</returns>
		ushort GetUShort(string key, ushort defaultValue);

		/// <summary>
		/// Attempts to retrieve an unsigned short value from the configuration for the specified <paramref name="key"/>.
		/// </summary>
		/// <param name="key">The configuration key to look up.</param>
		/// <param name="value">When this method returns <c>true</c>, contains the ushort value associated with <paramref name="key"/>; otherwise <c>0</c> (or the default provided by the underlying implementation).</param>
		/// <returns><c>true</c> if the key exists and a value was retrieved; otherwise <c>false</c>.</returns>
		bool TryGetUShort(string key, out ushort value);

		/// <summary>
		/// Retrieves an integer value from the configuration.
		/// </summary>
		/// <param name="key">The configuration key.</param>
		/// <param name="defaultValue">The default value to return if the key is not found.</param>
		/// <returns>The integer value associated with the key, or the default value if not found.</returns>
		int GetInt(string key, int defaultValue);

		/// <summary>
		/// Attempts to retrieve an integer value from the configuration for the specified <paramref name="key"/>.
		/// </summary>
		/// <param name="key">The configuration key to look up.</param>
		/// <param name="value">When this method returns <c>true</c>, contains the integer value associated with <paramref name="key"/>; otherwise <c>0</c> (or the default provided by the underlying implementation).</param>
		/// <returns><c>true</c> if the key exists and a value was retrieved; otherwise <c>false</c>.</returns>
		bool TryGetInt(string key, out int value);
	}
}