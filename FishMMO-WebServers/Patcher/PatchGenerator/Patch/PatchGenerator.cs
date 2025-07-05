using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace FishMMO.Patcher
{
	public class PatchGenerator
	{
		private const int ChunkSize = 65535; // 65KB chunk size

		/// <summary>
		/// Generates binary patch data for a single file by comparing an old and new version.
		/// The patch data includes metadata for each changed chunk.
		/// </summary>
		/// <param name="oldFilePath">Path to the old version of the file.</param>
		/// <param name="newFilePath">Path to the new version of the file.</param>
		/// <returns>A byte array containing the serialized patch metadata for the file, or an empty array if no changes or an error occurred.</returns>
		public byte[] Generate(string oldFilePath, string newFilePath)
		{
			try
			{
				using (MemoryStream patchDataStream = new MemoryStream())
				using (BinaryWriter writer = new BinaryWriter(patchDataStream))
				{
					using (FileStream oldFile = File.OpenRead(oldFilePath))
					using (FileStream newFile = File.OpenRead(newFilePath))
					{
						long fileSize = Math.Max(oldFile.Length, newFile.Length);
						int numChunks = (int)Math.Ceiling((double)fileSize / ChunkSize);

						for (int i = 0; i < numChunks; i++)
						{
							byte[] oldChunk = ReadChunk(oldFile, i);
							byte[] newChunk = ReadChunk(newFile, i);

							if (oldChunk != null && newChunk != null)
							{
								var patchMetadata = GenerateMetadata(oldChunk, newChunk, i * ChunkSize);
								if (patchMetadata.Length > 0 || patchMetadata.NewBytes.Length > 0) // Only write if there's an actual change
								{
									WritePatchMetadata(writer, patchMetadata);
								}
							}
							else if (newChunk != null)
							{
								// This chunk is new or the old file was shorter
								var patchMetadata = new PatchMetadata()
								{
									Offset = i * ChunkSize,
									Length = 0, // Indicate insertion or new part where old didn't exist
									NewBytes = newChunk,
								};
								WritePatchMetadata(writer, patchMetadata);
							}
							// If oldChunk is not null and newChunk is null, it means deletion beyond new file length.
							// The current GenerateMetadata handles this by comparing up to minLength, and any remaining
							// old data effectively means the new file is shorter. The patch applier needs to handle this by
							// truncating or managing deletions correctly. Our current patch metadata is focused on
							// what bytes to *replace or insert* in the new file.
						}
					}
					return patchDataStream.ToArray();
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error generating patch data for '{oldFilePath}' vs '{newFilePath}': {ex.Message}");
				// Return an empty byte array to indicate failure or no patch data generated
				return new byte[0];
			}
		}

		private byte[] ReadChunk(FileStream fileStream, int chunkIndex)
		{
			byte[] buffer = new byte[ChunkSize];
			long offset = chunkIndex * ChunkSize;
			if (offset >= fileStream.Length) return null; // No more data to read

			fileStream.Seek(offset, SeekOrigin.Begin);
			int bytesRead = fileStream.Read(buffer, 0, ChunkSize);
			if (bytesRead > 0)
			{
				Array.Resize(ref buffer, bytesRead); // Trim buffer to actual size read
				return buffer;
			}
			return null;
		}

		/// <summary>
		/// Generates metadata for a patch by comparing two byte arrays (chunks).
		/// It finds the differing segments and returns the offset within the chunk,
		/// the length of the new data, and the new bytes.
		/// </summary>
		private PatchMetadata GenerateMetadata(byte[] oldData, byte[] newData, long chunkBaseOffset)
		{
			int start = 0;
			int oldLength = oldData.Length;
			int newLength = newData.Length;
			int minLength = Math.Min(oldLength, newLength);

			// Find the first differing byte
			while (start < minLength && oldData[start] == newData[start])
			{
				start++;
			}

			// If no difference found within minLength, check for length differences
			if (start == minLength)
			{
				if (oldLength == newLength)
				{
					// Chunks are identical
					return new PatchMetadata { Offset = chunkBaseOffset + start, Length = 0, NewBytes = new byte[0] };
				}
				else if (newLength > oldLength)
				{
					// New data appended
					byte[] bytes = new byte[newLength - start];
					Buffer.BlockCopy(newData, start, bytes, 0, bytes.Length);
					return new PatchMetadata { Offset = chunkBaseOffset + start, Length = 0, NewBytes = bytes };
				}
				else // oldLength > newLength
				{
					// Data truncated. This patch metadata will represent the portion that *remains* up to newLength.
					// The truncation itself needs to be handled by the patch applier based on final file size.
					// For now, if no difference up to newLength, and newLength is smaller, no patch is generated for this segment.
					// The patch applier will use the new file's total size.
					return new PatchMetadata { Offset = chunkBaseOffset + start, Length = 0, NewBytes = new byte[0] };
				}
			}

			// Find the last differing byte (from the end)
			int endOld = oldLength - 1;
			int endNew = newLength - 1;
			while (endOld >= start && endNew >= start && oldData[endOld] == newData[endNew])
			{
				endOld--;
				endNew--;
			}

			// Calculate the length of the differing segment in the new data
			int segmentLength = endNew - start + 1;
			byte[] newBytes = new byte[Math.Max(0, segmentLength)]; // Ensure non-negative length

			if (segmentLength > 0)
			{
				Buffer.BlockCopy(newData, start, newBytes, 0, segmentLength);
			}

			// Length in PatchMetadata indicates the count of *old* bytes that are replaced/removed.
			// If newBytes.Length is different from this Length, it's an insertion or deletion.
			return new PatchMetadata
			{
				Offset = chunkBaseOffset + start,
				Length = endOld - start + 1, // Length of the segment in the old file that is affected
				NewBytes = newBytes,
			};
		}


		/// <summary>
		/// Writes a PatchMetadata object to a BinaryWriter.
		/// </summary>
		private void WritePatchMetadata(BinaryWriter writer, PatchMetadata patchMetadata)
		{
			writer.Write(patchMetadata.Offset);
			writer.Write(patchMetadata.Length); // Length of old data replaced
			writer.Write(patchMetadata.NewBytes.Length); // Length of new data inserted
			writer.Write(patchMetadata.NewBytes);
		}
	}
}