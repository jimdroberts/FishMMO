using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization.Formatters.Binary;

public static class DictionaryExtensions
{
	public static void WriteToGZipFile(this Dictionary<long, string> dictionary, string filePath)
	{
		using (var fileStream = File.Create(filePath))
		using (var gzipStream = new GZipStream(fileStream, CompressionMode.Compress))
		{
			var formatter = new BinaryFormatter();
			formatter.Serialize(gzipStream, dictionary);
		}
	}

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