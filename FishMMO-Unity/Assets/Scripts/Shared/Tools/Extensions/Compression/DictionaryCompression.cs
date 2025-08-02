using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization.Formatters.Binary;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for compressing and decompressing dictionaries using GZip and BinaryFormatter.
	/// </summary>
	public static class DictionaryExtensions
	{
		/// <summary>
		/// Serializes and compresses a dictionary to a GZip file using BinaryFormatter.
		/// </summary>
		/// <param name="dictionary">Dictionary to serialize and compress.</param>
		/// <param name="filePath">File path to write the compressed data.</param>
		public static void WriteToGZipFile(this Dictionary<long, string> dictionary, string filePath)
		{
			using (var fileStream = File.Create(filePath))
			using (var gzipStream = new GZipStream(fileStream, CompressionMode.Compress))
			{
				var formatter = new BinaryFormatter();
				formatter.Serialize(gzipStream, dictionary);
			}
		}

		/// <summary>
		/// Reads and decompresses a dictionary from a GZip file using BinaryFormatter.
		/// Returns an empty dictionary if the file does not exist or is empty.
		/// </summary>
		/// <param name="filePath">File path to read the compressed data from.</param>
		/// <returns>Decompressed dictionary, or empty dictionary if file is missing or empty.</returns>
		public static Dictionary<long, string> ReadFromGZipFile(string filePath)
		{
			if (File.Exists(filePath))
			{
				using (var fileStream = File.OpenRead(filePath))
				{
					if (fileStream.Length > 0)
					{
						using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
						{
							var formatter = new BinaryFormatter();
							return (Dictionary<long, string>)formatter.Deserialize(gzipStream);
						}
					}
				}
			}
			return new Dictionary<long, string>();
		}
	}
}