using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

public static class DictionaryExtensions
{
	public static void WriteCompressedToFile(this Dictionary<long, string> dictionary, string filePath)
	{
		try
		{
			using (MemoryStream memoryStream = new MemoryStream())
			{
				using (GZipStream gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, true))
				using (BinaryWriter writer = new BinaryWriter(gzipStream))
				{
					Parallel.ForEach(dictionary, kvp =>
					{
						writer.Write(kvp.Key);
						writer.Write(kvp.Value);
					});
				}

				File.WriteAllBytes(filePath, memoryStream.ToArray());
			}
		}
		catch (Exception ex)
		{
			throw new Exception("Error writing compressed dictionary to file: " + ex.Message, ex);
		}
	}

	public static void ReadCompressedFromFile(Dictionary<long, string> dictionary, string filePath)
	{
		try
		{
			if (File.Exists(filePath))
			{
				using (FileStream fileStream = File.OpenRead(filePath))
				using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
				using (BinaryReader reader = new BinaryReader(gzipStream))
				{
					var locker = new object(); // Used for synchronization

					Parallel.ForEach(
						Partitioner.Create(0, (int)gzipStream.Length),
						range =>
						{
							while (range.Item1 < range.Item2)
							{
								long key = reader.ReadInt64();
								string value = reader.ReadString();

								lock (locker)
								{
									dictionary[key] = value;
								}
							}
						});
				}
			}
			else
			{
				return;
			}
		}
		catch (Exception ex)
		{
			throw new Exception("Error reading compressed dictionary from file: " + ex.Message, ex);
		}
	}
}