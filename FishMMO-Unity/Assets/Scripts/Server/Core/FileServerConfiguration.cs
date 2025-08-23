using FishMMO.Shared;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Handles all logic related to loading, saving, and accessing
	/// the application's configuration settings from files.
	/// </summary>
	public class FileServerConfiguration : IServerConfiguration
	{
		private readonly Configuration config;

		/// <summary>
		/// Initializes a new instance of the <see cref="FileServerConfiguration"/> class.
		/// </summary>
		public FileServerConfiguration()
		{
			string workingDirectory = Constants.GetWorkingDirectory();
			config = new Configuration(workingDirectory);
		}

		/// <summary>
		/// Saves default configuration values if they are missing for the specified server type and working directory.
		/// </summary>
		/// <param name="serverTypeName">The name of the server type.</param>
		/// <param name="serverType">The type of the server.</param>
		/// <param name="workingDirectory">The working directory for the server.</param>
		public void SaveDefaultsIfMissing(string serverTypeName, string serverType, string workingDirectory)
		{
			if (!config.Load(serverTypeName))
			{
				// Set default values if the config file doesn't exist
				config.Set("ServerName", "TestName");
				config.Set("MaximumClients", 4000);
				config.Set("Address", "127.0.0.1");
				config.Set("Port", GetDefaultPort(serverTypeName));
				config.Set("StaleSceneTimeout", 5);
				config.Set("ServerType", serverType);
#if !UNITY_EDITOR
				config.Save();
#endif
			}
		}

		/// <summary>
		/// Retrieves a string value from the configuration.
		/// </summary>
		/// <param name="key">The configuration key.</param>
		/// <param name="defaultValue">The default value to return if the key is not found.</param>
		/// <returns>The string value associated with the key, or the default value if not found.</returns>
		public string GetString(string key, string defaultValue)
		{
			if (config.TryGetString(key, out string value))
			{
				return value;
			}
			return defaultValue;
		}

		/// <summary>
		/// Retrieves an unsigned short value from the configuration.
		/// </summary>
		/// <param name="key">The configuration key.</param>
		/// <param name="defaultValue">The default value to return if the key is not found.</param>
		/// <returns>The ushort value associated with the key, or the default value if not found.</returns>
		public ushort GetUShort(string key, ushort defaultValue)
		{
			if (config.TryGetUShort(key, out ushort value))
			{
				return value;
			}
			return defaultValue;
		}

		/// <summary>
		/// Retrieves an integer value from the configuration.
		/// </summary>
		/// <param name="key">The configuration key.</param>
		/// <param name="defaultValue">The default value to return if the key is not found.</param>
		/// <returns>The integer value associated with the key, or the default value if not found.</returns>
		public int GetInt(string key, int defaultValue)
		{
			if (config.TryGetInt(key, out int value))
			{
				return value;
			}
			return defaultValue;
		}

		/// <summary>
		/// Gets the default port for a given server type name.
		/// </summary>
		/// <param name="serverTypeName">The name of the server type.</param>
		/// <returns>The default port as a string.</returns>
		private static string GetDefaultPort(string serverTypeName)
		{
			ServerType type = GetServerType(serverTypeName);
			switch (type)
			{
				case ServerType.Login: return "7770";
				case ServerType.World: return "7780";
				case ServerType.Scene: return "7781";
				default: return "7770";
			}
		}

		/// <summary>
		/// Determines the <see cref="ServerType"/> based on the server type name string.
		/// </summary>
		/// <param name="serverTypeName">The name of the server type.</param>
		/// <returns>The corresponding <see cref="ServerType"/> enum value.</returns>
		private static ServerType GetServerType(string serverTypeName)
		{
			string upper = serverTypeName.ToUpperInvariant();
			if (upper.StartsWith("LOGIN"))
			{
				return ServerType.Login;
			}
			if (upper.StartsWith("WORLD"))
			{
				return ServerType.World;
			}
			if (upper.StartsWith("SCENE"))
			{
				return ServerType.Scene;
			}
			return ServerType.Invalid;
		}
	}
}