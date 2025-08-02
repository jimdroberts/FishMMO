using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FishMMO.Shared
{
	/// <summary>
	/// Static class providing methods for compressing and decompressing strings using GZip and Base64 encoding.
	/// Includes file operations for reading and writing compressed strings.
	/// </summary>
	public static class StringCompression
	{
		/// <summary>
		/// Compresses a string using GZip and encodes the result as a Base64 string.
		/// </summary>
		/// <param name="input">Input string to compress.</param>
		/// <returns>Compressed and Base64-encoded string.</returns>
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

		/// <summary>
		/// Decompresses a Base64-encoded, GZip-compressed string back to its original form.
		/// </summary>
		/// <param name="compressedInput">Compressed and Base64-encoded string.</param>
		/// <returns>Decompressed original string.</returns>
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

		/// <summary>
		/// Writes a compressed, Base64-encoded string to a file.
		/// </summary>
		/// <param name="compressedString">Compressed and Base64-encoded string to write.</param>
		/// <param name="filePath">File path to write the data.</param>
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

		/// <summary>
		/// Reads a compressed string from a file and returns it as a Base64-encoded string.
		/// </summary>
		/// <param name="filePath">File path to read the compressed data from.</param>
		/// <returns>Compressed and Base64-encoded string read from the file.</returns>
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
}