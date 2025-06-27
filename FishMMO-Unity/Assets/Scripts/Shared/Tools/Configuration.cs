using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Cysharp.Text;

namespace FishMMO.Shared
{
	public class Configuration
	{
		public const string DEFAULT_FILENAME = "Configuration";
		public const string EXTENSION = ".cfg";
		public const string FULL_NAME = DEFAULT_FILENAME + EXTENSION;

		/// <summary>
		/// Represents the globally accessible configuration instance. This should typically be set once at application startup.
		/// </summary>
		public static Configuration GlobalSettings { get; private set; }

		private CultureInfo cultureInfo = CultureInfo.InvariantCulture;

		/// <summary>
		/// Stores the configuration settings as key-value pairs. Keys are treated case-insensitively using <see cref="StringComparer.OrdinalIgnoreCase"/>.
		/// </summary>
		private Dictionary<string, string> settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Gets the default directory where configuration files are saved and loaded. This value is set during construction.
		/// </summary>
		public string DefaultFileDirectory { get; }

		/// <summary>
		/// Gets or sets the base name of the configuration file (without the extension).
		/// </summary>
		public string FileName { get; set; } = DEFAULT_FILENAME;

		/// <summary>
		/// Initializes a new instance of the Configuration class with a specified default file directory.
		/// Throws an <see cref="ArgumentNullException"/> if the provided directory path is null or empty.
		/// </summary>
		/// <param name="defaultFileDirectory">The default directory where configuration files are saved and loaded.</param>
		public Configuration(string defaultFileDirectory)
		{
			if (string.IsNullOrWhiteSpace(defaultFileDirectory))
			{
				throw new ArgumentNullException(nameof(defaultFileDirectory), "Default file directory cannot be null or empty.");
			}
			DefaultFileDirectory = defaultFileDirectory;
		}

		/// <summary>
		/// Sets the global configuration instance. This method should typically be called once at application startup
		/// to initialize <see cref="GlobalSettings"/>.
		/// Throws an <see cref="ArgumentNullException"/> if the provided configuration instance is null.
		/// </summary>
		/// <param name="config">The configuration instance to set as global.</param>
		public static void SetGlobalSettings(Configuration config)
		{
			GlobalSettings = config ?? throw new ArgumentNullException(nameof(config));
		}

		/// <summary>
		/// Returns a string representation of the configuration, including its full file path
		/// and all stored key-value pairs for debugging purposes.
		/// </summary>
		public override string ToString()
		{
			// Creates a StringBuilder from Cysharp.Text for high-performance string concatenation (reduces allocations).
			using (var sb = ZString.CreateStringBuilder())
			{
				sb.Append("Configuration Path: ");
				sb.Append(Path.Combine(DefaultFileDirectory, FileName + EXTENSION));
				sb.AppendLine();

				if (settings.Count > 0)
				{
					sb.AppendLine("Settings:");
					foreach (KeyValuePair<string, string> setting in settings)
					{
						sb.Append("  "); // Adds indentation for better readability of the output.
						sb.Append(setting.Key);
						sb.Append(" = ");
						sb.Append(setting.Value);
						sb.AppendLine();
					}
				}
				else
				{
					sb.AppendLine("No settings loaded.");
				}
				return sb.ToString();
			}
		}

		/// <summary>
		/// Combines the settings from another configuration with this configuration.
		/// Existing entries in this configuration will be overwritten by values from the 'other' configuration.
		/// If the 'other' configuration is null, no changes are made.
		/// If you want to merge without overwriting, you'll need different logic (e.g., `settings.TryAdd`).
		/// </summary>
		/// <param name="other">The other configuration to combine with.</param>
		public void Combine(Configuration other)
		{
			if (other == null)
			{
				return;
			}

			// Iterates through each key-value pair in the other configuration and assigns them to this configuration.
			// This operation will overwrite any existing keys in 'this' configuration.
			foreach (KeyValuePair<string, string> pair in other.settings)
			{
				settings[pair.Key] = pair.Value;
			}
		}

		/// <summary>
		/// Saves the current configuration to the default file path, using the <see cref="DefaultFileDirectory"/>
		/// and <see cref="FileName"/> with the <see cref="EXTENSION"/>.
		/// </summary>
		public void Save()
		{
			Save(DefaultFileDirectory, FileName + EXTENSION);
		}

		/// <summary>
		/// Saves the current configuration to a specified file path.
		/// Each setting is written as "key=value" on a new line.
		/// The file is created or truncated if it already exists, and encoded in UTF-8 without a Byte Order Mark (BOM).
		/// Includes error handling for common file I/O and access exceptions.
		/// </summary>
		/// <param name="fileDirectory">The directory to save the file in.</param>
		/// <param name="fullFileName">The full file name (e.g., "myconfig.cfg").</param>
		public void Save(string fileDirectory, string fullFileName)
		{
			if (string.IsNullOrWhiteSpace(fileDirectory) || string.IsNullOrWhiteSpace(fullFileName))
			{
				Console.WriteLine("Warning: Cannot save configuration. File directory or file name is invalid.");
				return;
			}

			string fullPath = Path.Combine(fileDirectory, fullFileName);

			try
			{
				// Creates the directory if it does not already exist.
				if (!Directory.Exists(fileDirectory))
				{
					Directory.CreateDirectory(fileDirectory);
				}

				// Opens/creates the file for writing, truncating if it already exists, and ensures exclusive access.
				// Uses StreamWriter with UTF8 encoding without a Byte Order Mark (BOM) for cleaner text files.
				using (FileStream fs = File.Open(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
				using (StreamWriter sw = new StreamWriter(fs, new UTF8Encoding(false)))
				{
					// Writes each key-value pair to the file in "key=value" format, followed by a new line.
					foreach (KeyValuePair<string, string> pair in settings)
					{
						sw.WriteLine($"{pair.Key}={pair.Value}");
					}
				}
			}
			// Catches specific exceptions related to file access permissions.
			catch (UnauthorizedAccessException ex)
			{
				Console.WriteLine($"Error: Access denied when saving configuration to {fullPath}. {ex.Message}");
			}
			// Catches specific exceptions related to I/O operations (e.g., disk full, file in use).
			catch (IOException ex)
			{
				Console.WriteLine($"Error: An I/O error occurred while saving configuration to {fullPath}. {ex.Message}");
			}
			// Catches any other unexpected exceptions during the save process.
			catch (Exception ex)
			{
				Console.WriteLine($"An unexpected error occurred while saving configuration to {fullPath}. {ex.Message}");
			}
		}

		/// <summary>
		/// Removes the UTF-8 Byte Order Mark (BOM) from a string and any null characters.
		/// This is crucial for correctly parsing files that might have been saved with a BOM.
		/// </summary>
		/// <param name="s">The input string.</param>
		/// <returns>The string with BOM and null characters removed.</returns>
		private string RemoveBOM(string s)
		{
			// Gets the UTF-8 BOM as a string.
			string bomMarkUtf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
			// Checks if the input string starts with the BOM using ordinal comparison for efficiency.
			if (s.StartsWith(bomMarkUtf8, StringComparison.Ordinal))
			{
				// Removes the BOM from the beginning of the string.
				s = s.Remove(0, bomMarkUtf8.Length);
			}
			// Removes any null characters that might appear if the file was read with incorrect encoding.
			return s.Replace("\0", "");
		}

		/// <summary>
		/// Loads the configuration from the default file path, using the <see cref="DefaultFileDirectory"/>
		/// and the provided <paramref name="fileName"/> with the <see cref="EXTENSION"/>.
		/// </summary>
		/// <param name="fileName">The name of the file (e.g., "Configuration.cfg").</param>
		/// <returns>True if the configuration was loaded successfully, false otherwise.</returns>
		public bool Load(string fileName)
		{
			return Load(DefaultFileDirectory, fileName + EXTENSION);
		}

		/// <summary>
		/// Loads the configuration from a specified file path.
		/// The file content is read as UTF-8, stripped of any BOM, and parsed into key-value pairs.
		/// Lines starting with '#' or ';' (after trimming whitespace) are ignored as comments.
		/// Includes robust error handling for file I/O and access exceptions.
		/// </summary>
		/// <param name="fileDirectory">The directory of the file.</param>
		/// <param name="fullFileName">The full file name (e.g., "myconfig.cfg").</param>
		/// <returns>True if the configuration was loaded successfully, false otherwise.</returns>
		public bool Load(string fileDirectory, string fullFileName)
		{
			if (string.IsNullOrWhiteSpace(fileDirectory) || string.IsNullOrWhiteSpace(fullFileName))
			{
				Console.WriteLine("Warning: Cannot load configuration. File directory or file name is invalid.");
				return false;
			}

			FileName = Path.GetFileNameWithoutExtension(fullFileName); // Stores only the base name of the file (without its extension).

			string fullPath = Path.Combine(fileDirectory, fullFileName);
			if (!File.Exists(fullPath))
			{
				return false;
			}

			try
			{
				// Reads the entire content of the file as a single string using UTF-8 encoding.
				string unsplit = File.ReadAllText(fullPath, Encoding.UTF8);

				// Strips any Byte Order Mark (BOM) from the beginning of the string.
				unsplit = RemoveBOM(unsplit);

				// Clears all existing settings before populating with new ones from the file.
				settings.Clear();

				// Splits the entire file content into individual lines, removing any empty lines.
				string[] lines = unsplit.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
				// Processes each line read from the configuration file.
				foreach (string line in lines)
				{
					// Skips lines that are empty or start with '#' or ';' (treated as comments).
					if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.TrimStart().StartsWith(";"))
					{
						continue;
					}

					// Splits the line into a key and a value pair, only at the *first* occurrence of '='.
					// This allows values to safely contain '=' characters (e.g., for URLs or paths).
					string[] pair = line.Split(new char[] { '=' }, 2, StringSplitOptions.None);
					// Checks if the line was successfully split into two parts (key and value) and the key is not empty.
					if (pair.Length == 2 && !string.IsNullOrWhiteSpace(pair[0]))
					{
						// Sets the configuration entry, trimming whitespace from both key and value.
						Set(pair[0].Trim(), pair[1].Trim());
					}
					else
					{
						// Logs a warning for any malformed lines that cannot be parsed.
						Console.WriteLine($"Warning: Malformed configuration line skipped: '{line}' in {fullFileName}");
					}
				}
				return true;
			}
			// Catches specific exceptions related to I/O operations during file loading.
			catch (IOException ex)
			{
				Console.WriteLine($"Error: An I/O error occurred while loading configuration from {fullPath}. {ex.Message}");
				return false;
			}
			// Catches specific exceptions related to file access permissions during loading.
			catch (UnauthorizedAccessException ex)
			{
				Console.WriteLine($"Error: Access denied when loading configuration from {fullPath}. {ex.Message}");
				return false;
			}
			// Catches any other unexpected exceptions during the load process.
			catch (Exception ex)
			{
				Console.WriteLine($"An unexpected error occurred while loading configuration from {fullPath}. {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Sets a string value for a given setting name.
		/// Throws an <see cref="ArgumentNullException"/> if the setting name is null or whitespace.
		/// If the <paramref name="value"/> is null, an empty string is stored.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="value">The string value to set.</param>
		public void Set(string name, string value)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				throw new ArgumentNullException(nameof(name), "Setting name cannot be null or empty.");
			}
			settings[name] = value ?? string.Empty; // Assigns the value; if 'value' is null, it stores an empty string.
		}

		/// <summary>
		/// Sets a generic value for a given setting name by converting it to its string representation.
		/// If the <paramref name="value"/> is null, an empty string is stored.
		/// </summary>
		/// <typeparam name="T">The type of the value.</typeparam>
		/// <param name="name">The name of the setting.</param>
		/// <param name="value">The value to set.</param>
		public void Set<T>(string name, T value)
		{
			if (value != null)
			{
				Set(name, value.ToString());
			}
			else
			{
				Set(name, string.Empty); // Sets the value to an empty string if the provided value is null.
			}
		}

		/// <summary>
		/// Sets a double value for a given setting name, formatted using the <see cref="CultureInfo.InvariantCulture"/>.
		/// The "R" (Round-trip) format specifier is used to ensure precise and consistent serialization of the double value.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="value">The double value to set.</param>
		public void Set(string name, double value)
		{
			Set(name, value.ToString("R", cultureInfo));
		}

		/// <summary>
		/// Checks if a setting with the specified name exists in the configuration.
		/// </summary>
		/// <param name="name">The name of the setting to check.</param>
		/// <returns>True if the setting exists; otherwise, false.</returns>
		public bool Exists(string name)
		{
			return settings.ContainsKey(name);
		}

		/// <summary>
		/// Removes a setting with the specified name from the configuration.
		/// </summary>
		/// <param name="name">The name of the setting to remove.</param>
		/// <returns>True if the setting was successfully removed; otherwise, false if the setting was not found.</returns>
		public bool Remove(string name)
		{
			return settings.Remove(name);
		}

		/// <summary>
		/// Attempts to retrieve a value of a specified type from the configuration.
		/// This is a generic method that uses <see cref="Convert.ChangeType"/>.
		/// Specific `TryParse` methods (e.g., <see cref="TryGetInt(string, out int, int)"/>) are generally preferred for primitive types due to better error handling
		/// and performance for specific types.
		/// Logs warnings to the console for <see cref="InvalidCastException"/>, <see cref="FormatException"/>, or <see cref="OverflowException"/>
		/// that occur during the conversion process.
		/// </summary>
		/// <typeparam name="T">The type to convert the setting value to. Must implement <see cref="IConvertible"/>.</typeparam>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the value from the configuration, if the conversion succeeded,
		/// or the <paramref name="defaultValue"/> for the type if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGet<T>(string name, out T result, T defaultValue = default(T)) where T : IConvertible
		{
			if (settings.TryGetValue(name, out string settingValue))
			{
				try
				{
					result = (T)Convert.ChangeType(settingValue, typeof(T), cultureInfo); // Attempts to convert using invariant culture.
					return true;
				}
				catch (InvalidCastException)
				{
					Console.WriteLine($"Warning: Cannot convert setting '{name}' with value '{settingValue}' to type '{typeof(T).Name}'. Returning default value.");
				}
				catch (FormatException)
				{
					Console.WriteLine($"Warning: Format error when converting setting '{name}' with value '{settingValue}' to type '{typeof(T).Name}'. Returning default value.");
				}
				catch (OverflowException)
				{
					Console.WriteLine($"Warning: Overflow error when converting setting '{name}' with value '{settingValue}' to type '{typeof(T).Name}'. Returning default value.");
				}
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve a string value from the configuration.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the string value from the configuration,
		/// or the <paramref name="defaultValue"/> if the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found.</param>
		/// <returns>True if the setting was found; otherwise, false.</returns>
		public bool TryGetString(string name, out string result, string defaultValue = default(string))
		{
			if (settings.TryGetValue(name, out result))
			{
				return true;
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve a char value from the configuration.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the char value from the configuration,
		/// or the <paramref name="defaultValue"/> if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGetChar(string name, out char result, char defaultValue = default(char))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return char.TryParse(setting, out result);
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve a byte value from the configuration.
		/// Parsing is performed using <see cref="NumberStyles.Any"/> and <see cref="CultureInfo.InvariantCulture"/>.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the byte value from the configuration,
		/// or the <paramref name="defaultValue"/> if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGetByte(string name, out byte result, byte defaultValue = default(byte))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return byte.TryParse(setting, NumberStyles.Any, cultureInfo, out result);
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve an sbyte value from the configuration.
		/// Parsing is performed using <see cref="NumberStyles.Any"/> and <see cref="CultureInfo.InvariantCulture"/>.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the sbyte value from the configuration,
		/// or the <paramref name="defaultValue"/> if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGetSByte(string name, out sbyte result, sbyte defaultValue = default(sbyte))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return sbyte.TryParse(setting, NumberStyles.Any, cultureInfo, out result);
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve a short value from the configuration.
		/// Parsing is performed using <see cref="NumberStyles.Any"/> and <see cref="CultureInfo.InvariantCulture"/>.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the short value from the configuration,
		/// or the <paramref name="defaultValue"/> if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGetShort(string name, out short result, short defaultValue = default(short))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return short.TryParse(setting, NumberStyles.Any, cultureInfo, out result);
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve an ushort value from the configuration.
		/// Parsing is performed using <see cref="NumberStyles.Any"/> and <see cref="CultureInfo.InvariantCulture"/>.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the ushort value from the configuration,
		/// or the <paramref name="defaultValue"/> if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGetUShort(string name, out ushort result, ushort defaultValue = default(ushort))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return ushort.TryParse(setting, NumberStyles.Any, cultureInfo, out result);
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve an int value from the configuration.
		/// Parsing is performed using <see cref="NumberStyles.Any"/> and <see cref="CultureInfo.InvariantCulture"/>.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the int value from the configuration,
		/// or the <paramref name="defaultValue"/> if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGetInt(string name, out int result, int defaultValue = default(int))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return int.TryParse(setting, NumberStyles.Any, cultureInfo, out result);
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve a uint value from the configuration.
		/// Parsing is performed using <see cref="NumberStyles.Any"/> and <see cref="CultureInfo.InvariantCulture"/>.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the uint value from the configuration,
		/// or the <paramref name="defaultValue"/> if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGetUInt(string name, out uint result, uint defaultValue = default(uint))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return uint.TryParse(setting, NumberStyles.Any, cultureInfo, out result);
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve a long value from the configuration.
		/// Parsing is performed using <see cref="NumberStyles.Any"/> and <see cref="CultureInfo.InvariantCulture"/>.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the long value from the configuration,
		/// or the <paramref name="defaultValue"/> if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGetLong(string name, out long result, long defaultValue = default(long))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return long.TryParse(setting, NumberStyles.Any, cultureInfo, out result);
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve an ulong value from the configuration.
		/// Parsing is performed using <see cref="NumberStyles.Any"/> and <see cref="CultureInfo.InvariantCulture"/>.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the ulong value from the configuration,
		/// or the <paramref name="defaultValue"/> if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGetULong(string name, out ulong result, ulong defaultValue = default(ulong))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return ulong.TryParse(setting, NumberStyles.Any, cultureInfo, out result);
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve a boolean value from the configuration.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the boolean value from the configuration,
		/// or the <paramref name="defaultValue"/> if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGetBool(string name, out bool result, bool defaultValue = default(bool))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return bool.TryParse(setting, out result);
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve a float value from the configuration.
		/// Parsing is performed using <see cref="NumberStyles.Any"/> and <see cref="CultureInfo.InvariantCulture"/>.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the float value from the configuration,
		/// or the <paramref name="defaultValue"/> if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGetFloat(string name, out float result, float defaultValue = default(float))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return float.TryParse(setting, NumberStyles.Any, cultureInfo, out result);
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve a double value from the configuration.
		/// Parsing is performed using <see cref="NumberStyles.Any"/> and <see cref="CultureInfo.InvariantCulture.NumberFormat"/>.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the double value from the configuration,
		/// or the <paramref name="defaultValue"/> if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGetDouble(string name, out double result, double defaultValue = default(double))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return double.TryParse(setting, NumberStyles.Any, cultureInfo.NumberFormat, out result);
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve an enum value of a specified type from the configuration.
		/// Parsing is case-insensitive.
		/// </summary>
		/// <typeparam name="TEnum">The type of the enum.</typeparam>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the enum value from the configuration,
		/// or the <paramref name="defaultValue"/> if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGetEnum<TEnum>(string name, out TEnum result, TEnum defaultValue = default(TEnum)) where TEnum : struct, Enum
		{
			if (settings.TryGetValue(name, out string setting))
			{
				// Enum.TryParse is used for robust parsing, including case-insensitivity.
				if (Enum.TryParse(setting, true, out TEnum parsedEnum))
				{
					result = parsedEnum;
					return true;
				}
			}
			result = defaultValue;
			return false;
		}
	}
}