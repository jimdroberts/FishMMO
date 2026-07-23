using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for compressing and decompressing dictionaries using GZip.
	/// Uses manual binary serialization for security and performance.
	/// </summary>
	public static class DictionaryCompression
	{
		/// <summary>
		/// Maximum number of dictionary entries allowed when reading from a GZip file,
		/// to guard against malicious or malformed files that advertise an unrealistically
		/// large count and cause an OutOfMemoryException.
		/// </summary>
		private const int MaxDictionaryCount = 10_000_000;

		/// <summary>
		/// Serializes and compresses a dictionary to a GZip file.
		/// </summary>
		public static void WriteToGZipFile(this Dictionary<long, string> dictionary, string filePath)
		{
			if (dictionary == null) throw new ArgumentNullException(nameof(dictionary));

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
						if (count < 0 || count > MaxDictionaryCount)
						{
							throw new InvalidDataException($"Dictionary entry count {count} exceeds the maximum allowed ({MaxDictionaryCount}).");
						}
						var dictionary = new Dictionary<long, string>(count);

						for (int i = 0; i < count; i++)
						{
							long key = reader.ReadInt64();
							string value = reader.ReadString();
							dictionary[key] = value;
						}
						return dictionary;
					}
				}
			}
			catch (IOException ex)
			{
				System.Console.WriteLine($"Error reading dictionary from GZip file: {ex.Message}");
				return new Dictionary<long, string>();
			}
			catch (InvalidDataException ex)
			{
				System.Console.WriteLine($"Error reading dictionary from GZip file (invalid data): {ex.Message}");
				return new Dictionary<long, string>();
			}
		}
	}
}
