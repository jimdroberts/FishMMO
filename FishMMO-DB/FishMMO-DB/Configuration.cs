using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FishMMO_DB
{
	public class Configuration
	{
		private CultureInfo cultureInfo = CultureInfo.InvariantCulture;
		private Dictionary<string, string> settings = new Dictionary<string, string>();

		public string defaultFileDirectory;
		public string fileName = "Configuration.cfg";

		public Configuration(string defaultFileDirectory)
		{
			this.defaultFileDirectory = defaultFileDirectory;
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
					if (!settings.ContainsKey(pair.Key))
					{
						settings.Add(pair.Key, pair.Value);
					}
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
				StringBuilder sb = new StringBuilder();
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
			if (settings.ContainsKey(name))
			{
				settings[name] = value;
			}
			else
			{
				settings.Add(name, value);
			}
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

		public bool TryGet<T>(string name, out T result) where T : IConvertible
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
			result = default(T);
			return false;
		}

		public bool TryGetString(string name, out string result)
		{
			string test;
			if (settings.TryGetValue(name, out test))
			{
				result = test;
				return true;
			}
			result = test;
			return false;
		}

		public bool TryGetChar(string name, out char result)
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return char.TryParse(setting, out result);
			}
			result = default(char);
			return false;
		}

		public bool TryGetByte(string name, out byte result)
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return byte.TryParse(setting, out result);
			}
			result = default(byte);
			return false;
		}

		public bool TryGetSByte(string name, out sbyte result)
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return sbyte.TryParse(setting, out result);
			}
			result = default(sbyte);
			return false;
		}

		public bool TryGetShort(string name, out short result)
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return short.TryParse(setting, out result);
			}
			result = default(short);
			return false;
		}

		public bool TryGetUShort(string name, out ushort result)
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return ushort.TryParse(setting, out result);
			}
			result = default(ushort);
			return false;
		}

		public bool TryGetInt(string name, out int result)
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return int.TryParse(setting, out result);
			}
			result = default(int);
			return false;
		}

		public bool TryGetUInt(string name, out uint result)
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return uint.TryParse(setting, out result);
			}
			result = default(uint);
			return false;
		}

		public bool TryGetLong(string name, out long result)
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return long.TryParse(setting, out result);
			}
			result = default(long);
			return false;
		}

		public bool TryGetULong(string name, out ulong result)
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return ulong.TryParse(setting, out result);
			}
			result = default(ulong);
			return false;
		}

		public bool TryGetBool(string name, out bool result)
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return bool.TryParse(setting, out result);
			}
			result = default(bool);
			return false;
		}

		public bool TryGetFloat(string name, out float result)
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return float.TryParse(setting, out result);
			}
			result = default(float);
			return false;
		}

		public bool TryGetDouble(string name, out double result)
		{
			if (settings.TryGetValue(name, out string setting))
			{
				return double.TryParse(setting, NumberStyles.Any, cultureInfo.NumberFormat, out result);
			}
			result = default(double);
			return false;
		}
	}
}