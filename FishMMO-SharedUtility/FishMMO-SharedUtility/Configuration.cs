using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Cysharp.Text;

namespace FishMMO.Shared
{
	/// <summary>
	/// Thread-safe key-value configuration store with file I/O, environment-variable
	/// override support, and typed accessors. Each instance manages its own lock
	/// so that multiple <see cref="Configuration"/> objects can be used independently
	/// without cross-instance contention.
	/// </summary>
	public class Configuration
	{
		public const string DEFAULT_FILENAME = "Configuration";
		public const string EXTENSION = ".cfg";
		public const string FULL_NAME = DEFAULT_FILENAME + EXTENSION;

		/// <summary>
		/// Represents the globally accessible configuration instance. This should typically be set once at application startup.
		/// </summary>
		public static Configuration? GlobalSettings { get; private set; }

		private static int nextInstanceId = 0;

		private readonly int instanceId;

		private readonly CultureInfo cultureInfo = CultureInfo.InvariantCulture;

		/// <summary>
		/// Synchronizes access to the <see cref="settings"/> dictionary. ReaderWriterLockSlim is used
		/// because reads vastly outnumber writes in typical usage.
		/// Instance-level so multiple <see cref="Configuration"/> objects do not contend.
		/// </summary>
		private readonly ReaderWriterLockSlim settingsLock = new ReaderWriterLockSlim();

		/// <summary>
		/// Stores the configuration settings as key-value pairs. Keys are treated case-insensitively using <see cref="StringComparer.OrdinalIgnoreCase"/>.
		/// All access must be synchronized via <see cref="settingsLock"/>.
		/// </summary>
		private Dictionary<string, string> settings = new Dictionary<string, string>(100, StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Gets the default directory where configuration files are saved and loaded. This value is set during construction.
		/// </summary>
		public string DefaultFileDirectory { get; }

		/// <summary>
		/// Gets or sets the base name of the configuration file (without the extension).
		/// </summary>
		public string FileName { get; set; } = DEFAULT_FILENAME;


		/// <summary>
		/// Resolves a setting value, preferring the environment variable
		/// <c>FISHMMO_CONFIG_{NAME}</c> (uppercased, '.'/':' -&gt; '_') over the
		/// in-memory config dictionary. This allows operators to override
		/// sensitive values (DB passwords, signing keys, etc.) without
		/// committing them to disk.
		///
		/// LIMITATION: Dots, colons, and dashes in setting names are all converted
		/// to underscores for environment variable lookup. This means keys like
		/// "Database.Password", "Database:Password", and "Database-Password" all
		/// map to the same env var FISHMMO_CONFIG_DATABASE_PASSWORD, creating an
		/// ambiguity. Workarounds include: (a) choosing a single delimiter and
		/// enforcing it in naming conventions, or (b) encoding the original key
		/// in a side-channel value.
		/// </summary>
		private bool TryResolveRawValue(string name, out string value)
		{
			if (!string.IsNullOrEmpty(name))
			{
				string envKey = "FISHMMO_CONFIG_" + name.ToUpperInvariant().Replace('.', '_').Replace(':', '_').Replace('-', '_');
				string envVal = Environment.GetEnvironmentVariable(envKey);
				if (envVal != null)
				{
					value = envVal;
					return true;
				}
			}
			settingsLock.EnterReadLock();
			try
			{
				return this.settings.TryGetValue(name, out value);
			}
			finally
			{
				settingsLock.ExitReadLock();
			}
		}

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
			instanceId = Interlocked.Increment(ref nextInstanceId);
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

				settingsLock.EnterReadLock();
				try
				{
					if (this.settings.Count > 0)
					{
						sb.AppendLine("Settings:");
						foreach (KeyValuePair<string, string> setting in this.settings)
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
				}
				finally
				{
					settingsLock.ExitReadLock();
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

			if (other == this)
			{
				// No-op when combining with self.
				return;
			}

			// Acquire locks in a consistent order (by unique instance ID) to prevent
			// deadlock when two threads call a.Combine(b) and b.Combine(a) concurrently.
			// Always copies FROM other TO this, regardless of lock acquisition order.
			bool lockSelfFirst = this.instanceId < other.instanceId;

			if (lockSelfFirst)
			{
				this.settingsLock.EnterWriteLock();
				try
				{
					other.settingsLock.EnterReadLock();
					try
					{
						foreach (KeyValuePair<string, string> pair in other.settings)
						{
							this.settings[pair.Key] = pair.Value;
						}
					}
					finally
					{
						other.settingsLock.ExitReadLock();
					}
				}
				finally
				{
					this.settingsLock.ExitWriteLock();
				}
			}
			else
			{
				other.settingsLock.EnterReadLock();
				try
				{
					this.settingsLock.EnterWriteLock();
					try
					{
						foreach (KeyValuePair<string, string> pair in other.settings)
						{
							this.settings[pair.Key] = pair.Value;
						}
					}
					finally
					{
						this.settingsLock.ExitWriteLock();
					}
				}
				finally
				{
					other.settingsLock.ExitReadLock();
				}
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

				// Acquire the read lock BEFORE opening the file to prevent a TOCTOU race
				// where another thread mutates the dictionary between the file-open and
				// the lock acquisition, causing the written snapshot to be inconsistent.
				settingsLock.EnterReadLock();
				try
				{
					// Opens/creates the file for writing, truncating if it already exists, and ensures exclusive access.
					// Uses StreamWriter with UTF8 encoding without a Byte Order Mark (BOM) for cleaner text files.
					using (FileStream fs = File.Open(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
					using (StreamWriter sw = new StreamWriter(fs, new UTF8Encoding(false)))
					{
						// Writes each key-value pair to the file in "key=value" format, followed by a new line.
						foreach (KeyValuePair<string, string> pair in this.settings)
						{
							sw.WriteLine($"{pair.Key}={pair.Value}");
						}
					}
				}
				finally
				{
					settingsLock.ExitReadLock();
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
		/// Removes the UTF-8 Byte Order Mark (BOM) from the beginning of a string if present.
		/// </summary>
		/// <param name="s">The input string.</param>
		/// <returns>The string with BOM removed if present.</returns>
		private string RemoveBOM(string s)
		{
			// BOM is the single Unicode character U+FEFF (UTF-8 preamble encoded as a char).
			// Checking s[0] avoids allocating a BOM string on every call.
			if (s.Length > 0 && s[0] == '﻿')
			{
				return s.Substring(1);
			}
			return s;
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

				// Synchronize the entire dictionary replacement under a single write lock
				// to avoid nested locking with Set() and to keep the replacement atomic.
				settingsLock.EnterWriteLock();
				try
				{
					// Clears all existing settings before populating with new ones from the file.
					this.settings.Clear();

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
							// Sets the configuration entry by writing directly to the dictionary under the write lock
							// instead of calling Set(), to avoid nested lock acquisition.
							this.settings[pair[0].Trim()] = pair[1].Trim();
						}
						else
						{
							// Logs a warning for any malformed lines that cannot be parsed.
							Console.WriteLine($"Warning: Malformed configuration line skipped: '{line}' in {fullFileName}");
						}
					}
				}
				finally
				{
					settingsLock.ExitWriteLock();
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
			settingsLock.EnterWriteLock();
			try
			{
				this.settings[name] = value ?? string.Empty; // Assigns the value; if 'value' is null, it stores an empty string.
			}
			finally
			{
				settingsLock.ExitWriteLock();
			}
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
			Set(name, value.ToString("R", this.cultureInfo));
		}

		/// <summary>
		/// Checks if a setting with the specified name exists in the configuration.
		/// </summary>
		/// <param name="name">The name of the setting to check.</param>
		/// <returns>True if the setting exists; otherwise, false.</returns>
		public bool Exists(string name)
		{
			settingsLock.EnterReadLock();
			try
			{
				return this.settings.ContainsKey(name);
			}
			finally
			{
				settingsLock.ExitReadLock();
			}
		}

		/// <summary>
		/// Removes a setting with the specified name from the configuration.
		/// </summary>
		/// <param name="name">The name of the setting to remove.</param>
		/// <returns>True if the setting was successfully removed; otherwise, false if the setting was not found.</returns>
		public bool Remove(string name)
		{
			settingsLock.EnterWriteLock();
			try
			{
				return this.settings.Remove(name);
			}
			finally
			{
				settingsLock.ExitWriteLock();
			}
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
		public bool TryGet<T>(string name, out T result, T defaultValue = default!) where T : IConvertible
		{
			if (TryResolveRawValue(name, out string settingValue))
			{
				try
				{
					result = (T)Convert.ChangeType(settingValue, typeof(T), this.cultureInfo); // Attempts to convert using invariant culture.
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
		public bool TryGetString(string name, out string? result, string? defaultValue = null)
		{
			if (TryResolveRawValue(name, out result))
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
			if (TryResolveRawValue(name, out string setting))
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
			if (TryResolveRawValue(name, out string setting))
			{
				return byte.TryParse(setting, NumberStyles.Any, this.cultureInfo, out result);
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
			if (TryResolveRawValue(name, out string setting))
			{
				return sbyte.TryParse(setting, NumberStyles.Any, this.cultureInfo, out result);
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
			if (TryResolveRawValue(name, out string setting))
			{
				return short.TryParse(setting, NumberStyles.Any, this.cultureInfo, out result);
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
			if (TryResolveRawValue(name, out string setting))
			{
				return ushort.TryParse(setting, NumberStyles.Any, this.cultureInfo, out result);
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
			if (TryResolveRawValue(name, out string setting))
			{
				return int.TryParse(setting, NumberStyles.Any, this.cultureInfo, out result);
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
			if (TryResolveRawValue(name, out string setting))
			{
				return uint.TryParse(setting, NumberStyles.Any, this.cultureInfo, out result);
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
			if (TryResolveRawValue(name, out string setting))
			{
				return long.TryParse(setting, NumberStyles.Any, this.cultureInfo, out result);
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
			if (TryResolveRawValue(name, out string setting))
			{
				return ulong.TryParse(setting, NumberStyles.Any, this.cultureInfo, out result);
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
			if (TryResolveRawValue(name, out string setting))
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
			if (TryResolveRawValue(name, out string setting))
			{
				return float.TryParse(setting, NumberStyles.Any, this.cultureInfo, out result);
			}
			result = defaultValue;
			return false;
		}

		/// <summary>
		/// Attempts to retrieve a double value from the configuration.
		/// Parsing is performed using <see cref="System.Globalization.NumberStyles.Any"/> and <see cref="System.Globalization.CultureInfo.InvariantCulture"/>.
		/// </summary>
		/// <param name="name">The name of the setting.</param>
		/// <param name="result">When this method returns, contains the double value from the configuration,
		/// or the <paramref name="defaultValue"/> if the conversion failed or the setting was not found.</param>
		/// <param name="defaultValue">The value to return if the setting is not found or cannot be converted.</param>
		/// <returns>True if the setting was found and successfully converted; otherwise, false.</returns>
		public bool TryGetDouble(string name, out double result, double defaultValue = default(double))
		{
			if (TryResolveRawValue(name, out string setting))
			{
				return double.TryParse(setting, NumberStyles.Any, this.cultureInfo.NumberFormat, out result);
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
			if (TryResolveRawValue(name, out string setting))
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