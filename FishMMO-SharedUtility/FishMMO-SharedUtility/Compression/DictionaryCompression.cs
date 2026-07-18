using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for compressing and decompressing dictionaries using GZip.
	/// Uses manual binary serialization for security and performance.
	/// </summary>
	public static class DictionaryExtensions
	{
		/// <summary>
		/// Serializes and compresses a dictionary to a GZip file.
		/// </summary>
		public static void WriteToGZipFile(this Dictionary<long, string> dictionary, string filePath)
		{
			if (dictionary == null) return;

			using (var fileStream = File.Create(filePath))
			using (var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal))
			using (var writer = new BinaryWriter(gzipStream))
			{
				writer.Write(dictionary.Count);
				foreach (var kvp in dictionary)
				{
					writer.Write(kvp.Key);
					writer.Write(kvp.Value ?? string.Empty);
				}
			}
		}

		/// <summary>
		/// Reads and decompresses a dictionary from a GZip file.
		/// </summary>
		public static Dictionary<long, string> ReadFromGZipFile(string filePath)
		{
			if (!File.Exists(filePath)) return new Dictionary<long, string>();

			try
			{
				using (var fileStream = File.OpenRead(filePath))
				{
					if (fileStream.Length == 0) return new Dictionary<long, string>();

					using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
					using (var reader = new BinaryReader(gzipStream))
					{
						int count = reader.ReadInt32();
						var dictionary = new Dictionary<long, string>(count);

						for (int i = 0; i < count; i++)
						{
							long key = reader.ReadInt64();
							string value = reader.ReadString();
							dictionary.Add(key, value);
						}
						return dictionary;
					}
				}
			}
			catch (System.Exception ex)
			{
				System.Console.WriteLine($"Error reading dictionary from GZip file: {ex.Message}");
				return new Dictionary<long, string>();
			}
		}
	}
}