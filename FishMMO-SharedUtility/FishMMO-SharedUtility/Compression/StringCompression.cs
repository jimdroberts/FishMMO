using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FishMMO.Shared
{
	/// <summary>
	/// Provides high-performance utility methods for compressing strings using GZip and Base64.
	/// Optimized for memory usage and high-concurrency environments.
	/// </summary>
	public static class StringCompression
	{
		/// <summary>
		/// Compresses a string using GZip and encodes the result as a Base64 string.
		/// </summary>
		public static string CompressString(string input)
		{
			if (string.IsNullOrEmpty(input)) return string.Empty;

			try
			{
				byte[] buffer = Encoding.UTF8.GetBytes(input);

				using (var memoryStream = new MemoryStream())
				{
					// Use Optimal compression for maximum space savings in MMO data
					using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, true))
					{
						gzipStream.Write(buffer, 0, buffer.Length);
					}

					return Convert.ToBase64String(memoryStream.GetBuffer(), 0, (int)memoryStream.Length);
				}
			}
			catch (Exception ex)
			{
				// In a production MMO, consider logging via a shared logger rather than throwing generic exceptions
				throw new InvalidOperationException("Failed to compress string.", ex);
			}
		}

		/// <summary>
		/// Decompresses a Base64-encoded, GZip-compressed string back to its original UTF8 form.
		/// </summary>
		public static string DecompressString(string compressedInput)
		{
			if (string.IsNullOrEmpty(compressedInput)) return string.Empty;

			try
			{
				byte[] compressedData = Convert.FromBase64String(compressedInput);

				using (var compressedStream = new MemoryStream(compressedData))
				using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
				using (var resultStream = new MemoryStream())
				{
					gzipStream.CopyTo(resultStream);
					return Encoding.UTF8.GetString(resultStream.GetBuffer(), 0, (int)resultStream.Length);
				}
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException("Failed to decompress string.", ex);
			}
		}

		/// <summary>
		/// Writes a compressed string to a file as raw binary.
		/// </summary>
		public static void WriteCompressedStringToFile(string compressedString, string filePath)
		{
			if (string.IsNullOrEmpty(compressedString)) return;

			try
			{
				byte[] compressedData = Convert.FromBase64String(compressedString);
				File.WriteAllBytes(filePath, compressedData);
			}
			catch (Exception ex)
			{
				throw new IOException($"Failed to write compressed data to {filePath}", ex);
			}
		}

		/// <summary>
		/// Reads a raw GZip file and returns the decompressed UTF8 string.
		/// </summary>
		public static string ReadCompressedStringFromFile(string filePath)
		{
			if (!File.Exists(filePath)) return string.Empty;

			try
			{
				using (var fileStream = File.OpenRead(filePath))
				using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
				using (var resultStream = new MemoryStream())
				{
					gzipStream.CopyTo(resultStream);
					return Encoding.UTF8.GetString(resultStream.GetBuffer(), 0, (int)resultStream.Length);
				}
			}
			catch (Exception ex)
			{
				throw new IOException($"Failed to read and decompress {filePath}", ex);
			}
		}
	}
}