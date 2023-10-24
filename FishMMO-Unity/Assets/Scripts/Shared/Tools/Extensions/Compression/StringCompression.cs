using System;
using System.IO;
using System.IO.Compression;
using System.Text;

public static class StringCompression
{
	public static string CompressString(string input)
	{
		try
		{
			byte[] buffer = Encoding.UTF8.GetBytes(input);

			using (MemoryStream memoryStream = new MemoryStream())
			{
				using (GZipStream gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, true))
				{
					gzipStream.Write(buffer, 0, buffer.Length);
				}

				byte[] compressedData = memoryStream.ToArray();

				return Convert.ToBase64String(compressedData);
			}
		}
		catch (Exception ex)
		{
			throw new Exception("Error compressing the string: " + ex.Message, ex);
		}
	}

	public static string DecompressString(string compressedInput)
	{
		try
		{
			byte[] compressedData = Convert.FromBase64String(compressedInput);

			using (MemoryStream memoryStream = new MemoryStream(compressedData))
			using (MemoryStream decompressedStream = new MemoryStream())
			using (GZipStream gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
			{
				gzipStream.CopyTo(decompressedStream);

				byte[] decompressedBuffer = decompressedStream.ToArray();

				return Encoding.UTF8.GetString(decompressedBuffer);
			}
		}
		catch (Exception ex)
		{
			throw new Exception("Error decompressing the string: " + ex.Message, ex);
		}
	}

	public static void WriteCompressedStringToFile(string compressedString, string filePath)
	{
		try
		{
			byte[] compressedData = Convert.FromBase64String(compressedString);

			using (FileStream fileStream = File.Create(filePath))
			{
				fileStream.Write(compressedData, 0, compressedData.Length);
			}
		}
		catch (Exception ex)
		{
			throw new Exception("Error writing compressed string to file: " + ex.Message, ex);
		}
	}

	public static string ReadCompressedStringFromFile(string filePath)
	{
		try
		{
			using (FileStream fileStream = File.OpenRead(filePath))
			{
				using (MemoryStream memoryStream = new MemoryStream())
				using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
				{
					gzipStream.CopyTo(memoryStream);
					byte[] decompressedData = memoryStream.ToArray();
					return Convert.ToBase64String(decompressedData);
				}
			}
		}
		catch (Exception ex)
		{
			throw new Exception("Error reading compressed string from file: " + ex.Message, ex);
		}
	}
}