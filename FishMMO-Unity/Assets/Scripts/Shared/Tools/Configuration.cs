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
		public const string FULL_NAME = DEFAULT_FILENAME + "." + EXTENSION;

		public static Configuration GlobalSettings;

		private CultureInfo cultureInfo = CultureInfo.InvariantCulture;
		private Dictionary<string, string> settings = new Dictionary<string, string>();

		public string defaultFileDirectory;
		public string fileName = DEFAULT_FILENAME;

		public Configuration(string defaultFileDirectory)
		{
			this.defaultFileDirectory = defaultFileDirectory;
		}

		public override string ToString()
		{
			using (var sb = ZString.CreateStringBuilder())
			{
				sb.Append("Configuration: ");
				sb.Append(defaultFileDirectory);
				sb.Append("\\");
				sb.Append(fileName);
				if (settings != null)
				{
					foreach (KeyValuePair<string, string> setting in settings)
					{
						sb.AppendLine();
						sb.Append(setting.Key);
						sb.Append(": ");
						sb.Append(setting.Value);
					}
				}
				return sb.ToString();
			}
		}

		/// <summary>
		/// Attempts to combine the other configuration with this configuration. If the entry already exists it will not be updated to match the other configuration.
		/// </summary>
		public void Combine(Configuration other)
		{
			if (other != null)
			{
				foreach (KeyValuePair<string, string> pair in other.settings)
				{
					settings[pair.Key] = pair.Value;
				}
			}
		}

		public void Save()
		{
			Save(defaultFileDirectory, fileName);
		}
		public void Save(string fileDirectory, string fileName)
		{
			if (fileDirectory == null || fileDirectory.Length < 1 || fileName == null || fileName.Length < 1)
			{
				return;
			}
			string fullPath = Path.Combine(fileDirectory, fileName);
			if (!Directory.Exists(fileDirectory))
			{
				Directory.CreateDirectory(fileDirectory);
			}
			else if (File.Exists(fullPath))
			{
				File.Delete(fullPath);
			}
			using (BinaryWriter bw = new BinaryWriter(File.Open(fullPath, FileMode.Create)))
			{
				using (var sb = ZString.CreateStringBuilder())
				{
					foreach (KeyValuePair<string, string> pair in settings)
					{
						sb.Append(pair.Key);
						sb.Append("=");
						sb.Append(pair.Value);
						sb.AppendLine();
					}
					byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
					bw.Write(bytes);
				}
			}
		}

		public string RemoveBOM(string s)
		{
			string BOMMarkUtf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
			if (s.StartsWith(BOMMarkUtf8, StringComparison.OrdinalIgnoreCase))
				s = s.Remove(0, BOMMarkUtf8.Length);
			return s.Replace("\0", "");
		}

		public bool Load(string fileName)
		{
			return Load(defaultFileDirectory, fileName);
		}
		public bool Load(string fileDirectory, string fileName)
		{
			if (fileDirectory == null || fileDirectory.Length < 1 || fileName == null || fileName.Length < 1)
			{
				return false;
			}

			this.fileName = fileName;

			string fullPath = Path.Combine(fileDirectory, this.fileName);
			if (!File.Exists(fullPath))
			{
				return false;
			}

			byte[] bytes = File.ReadAllBytes(fullPath);
			if (bytes != null && bytes.Length > 0)
			{
				settings.Clear();
				string unsplit = Encoding.UTF8.GetString(bytes);
				unsplit = RemoveBOM(unsplit);
				string[] lines = unsplit.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
				foreach (string line in lines)
				{
					if (line != null && line.Length > 0)
					{
						string[] pair = line.Split(new string[] { "=", }, StringSplitOptions.None);
						if (pair != null && pair.Length == 2 && pair[0] != null && pair[0].Length > 0)
						{
							Set(pair[0].Trim(), pair[1].Trim());
						}
					}
				}
			}
			return true;
		}

		public void Set(string name, string value)
		{
			settings[name] = value;
		}

		public void Set<T>(string name, T value)
		{
			if (value != null)
				Set(name, value.ToString());
		}

		public void Set(string name, double value)
		{
			Set(name, value.ToString("R", cultureInfo));
		}

		public bool TryGet<T>(string name, out T result, T defaultValue = default(T)) where T : IConvertible
		{
			if (settings.TryGetValue(name, out string setting))
			{
				object o = Convert.ChangeType(setting, typeof(T));
				if (o != null)
				{
					result = (T)o;
					return true;
				}
			}
			result = defaultValue;
			return false;
		}

		public bool TryGetString(string name, out string result, string defaultValue = default(string))
		{
			if (settings.TryGetValue(name, out result))
			{
				return true;
			}
			result = defaultValue;
			return false;
		}

		public bool TryGetChar(string name, out char result, char defaultValue = default(char))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return char.TryParse(setting, out result);
			}
			result = defaultValue;
			return false;
		}

		public bool TryGetByte(string name, out byte result, byte defaultValue = default(byte))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return byte.TryParse(setting, out result);
			}
			result = defaultValue;
			return false;
		}

		public bool TryGetSByte(string name, out sbyte result, sbyte defaultValue = default(sbyte))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return sbyte.TryParse(setting, out result);
			}
			result = defaultValue;
			return false;
		}

		public bool TryGetShort(string name, out short result, short defaultValue = default(short))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return short.TryParse(setting, out result);
			}
			result = defaultValue;
			return false;
		}

		public bool TryGetUShort(string name, out ushort result, ushort defaultValue = default(ushort))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return ushort.TryParse(setting, out result);
			}
			result = defaultValue;
			return false;
		}

		public bool TryGetInt(string name, out int result, int defaultValue = default(int))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return int.TryParse(setting, out result);
			}
			result = defaultValue;
			return false;
		}

		public bool TryGetUInt(string name, out uint result, uint defaultValue = default(uint))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return uint.TryParse(setting, out result);
			}
			result = defaultValue;
			return false;
		}

		public bool TryGetLong(string name, out long result, long defaultValue = default(long))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return long.TryParse(setting, out result);
			}
			result = defaultValue;
			return false;
		}

		public bool TryGetULong(string name, out ulong result, ulong defaultValue = default(ulong))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return ulong.TryParse(setting, out result);
			}
			result = defaultValue;
			return false;
		}

		public bool TryGetBool(string name, out bool result, bool defaultValue = default(bool))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return bool.TryParse(setting, out result);
			}
			result = defaultValue;
			return false;
		}

		public bool TryGetFloat(string name, out float result, float defaultValue = default(float))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return float.TryParse(setting, out result);
			}
			result = defaultValue;
			return false;
		}

		public bool TryGetDouble(string name, out double result, double defaultValue = default(double))
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return double.TryParse(setting, NumberStyles.Any, cultureInfo.NumberFormat, out result);
			}
			result = defaultValue;
			return false;
		}
	}
}