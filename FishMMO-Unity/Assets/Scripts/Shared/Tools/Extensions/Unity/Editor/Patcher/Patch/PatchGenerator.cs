using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Logging;

namespace FishMMO.Shared.Patcher
{
	/// <summary>
	/// Generates binary patch data for a file by comparing an old version and a new version.
	/// It breaks files into chunks and identifies the smallest differing segments within each chunk.
	/// </summary>
	public class PatchGenerator
	{
		private const int ChunkSize = 65536; // Define the fixed size for processing file chunks (64KB).

		/// <summary>
		/// Generates binary patch data for a single file by comparing an old and new version.
		/// The generated patch data describes the changes needed to transform oldFilePath into newFilePath.
		/// </summary>
		/// <param name="oldFilePath">The path to the original (old) version of the file.</param>
		/// <param name="newFilePath">The path to the updated (new) version of the file.</param>
		/// <returns>A byte array containing the binary patch data, or an empty array if generation fails.</returns>
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
						long fileSize = newFile.Length;
						int numChunks = (int)Math.Ceiling((double)fileSize / ChunkSize);

						Log.Debug("PatchGenerator", $"Generating patch for '{oldFilePath}' to '{newFilePath}'. New file size: {fileSize} bytes. Number of chunks: {numChunks}");

						long lastOffset = -1;
						long lastLength = 0;
						List<byte> lastNewBytes = null;

						// Iterate through each chunk of the new file to find differences.
						for (int i = 0; i < numChunks; i++)
						{
							// Read corresponding chunks from both old and new files.
							byte[] oldChunk = ReadChunk(oldFile, i) ?? Array.Empty<byte>();
							byte[] newChunk = ReadChunk(newFile, i) ?? Array.Empty<byte>();

							// Generate patch metadata (differences) for the current chunk pair.
							List<PatchMetadata> patches = GenerateMetadata(oldChunk, newChunk, (long)i * ChunkSize);

							foreach (var patch in patches)
							{
								// Attempt to coalesce the current patch with the previous one if they are contiguous.
								if (lastOffset >= 0 && lastOffset + lastLength == patch.Offset)
								{
									lastLength += patch.Length;
									if (patch.NewBytes?.Length > 0)
										lastNewBytes.AddRange(patch.NewBytes);
								}
								else
								{
									// If not contiguous, write the previously coalesced patch to the stream.
									if (lastOffset >= 0)
									{
										WritePatchMetadata(writer, new PatchMetadata
										{
											Offset = lastOffset,
											Length = (int)lastLength,
											NewBytes = lastNewBytes?.ToArray() ?? Array.Empty<byte>()
										});
									}

									// Start a new coalescing region with the current patch.
									lastOffset = patch.Offset;
									lastLength = patch.Length;
									lastNewBytes = new List<byte>(patch.NewBytes ?? Array.Empty<byte>());
								}
							}
						}

						// After processing all chunks, write any remaining coalesced patch data.
						if (lastOffset >= 0)
						{
							WritePatchMetadata(writer, new PatchMetadata
							{
								Offset = lastOffset,
								Length = (int)lastLength,
								NewBytes = lastNewBytes?.ToArray() ?? Array.Empty<byte>()
							});
						}
					}
					return patchDataStream.ToArray(); // Return the complete binary patch data.
				}
			}
			catch (Exception ex)
			{
				Log.Error("PatchGenerator", $"Failed generating patch data for '{oldFilePath}' vs '{newFilePath}': {ex.Message}\n{ex.StackTrace}");
				return Array.Empty<byte>();
			}
		}

		/// <summary>
		/// Reads a specific chunk of data from a file stream.
		/// </summary>
		/// <param name="fileStream">The FileStream to read from.</param>
		/// <param name="chunkIndex">The index of the chunk to read.</param>
		/// <returns>A byte array containing the chunk data, or null if the chunk is beyond the file's end.</returns>
		private byte[] ReadChunk(FileStream fileStream, int chunkIndex)
		{
			long offset = (long)chunkIndex * ChunkSize;
			if (offset >= fileStream.Length)
				return null; // No more data in this chunk or beyond file end.

			int bytesToRead = (int)Math.Min(ChunkSize, fileStream.Length - offset);
			byte[] buffer = new byte[bytesToRead];

			fileStream.Seek(offset, SeekOrigin.Begin); // Position the stream to the start of the chunk.
			int bytesRead = fileStream.Read(buffer, 0, bytesToRead);

			if (bytesRead <= 0)
				return null; // No bytes read (e.g., end of stream reached unexpectedly).

			// If the last chunk is smaller than ChunkSize, resize the buffer to actual bytes read.
			if (bytesRead != bytesToRead)
				Array.Resize(ref buffer, bytesRead);

			return buffer;
		}

		/// <summary>
		/// Generates a list of PatchMetadata entries by comparing two byte arrays (chunks).
		/// This method finds differing segments and their common prefixes/suffixes.
		/// </summary>
		/// <param name="oldData">The byte array representing the old chunk.</param>
		/// <param name="newData">The byte array representing the new chunk.</param>
		/// <param name="chunkBaseOffset">The absolute starting offset of this chunk within the overall file.</param>
		/// <returns>A list of <see cref="PatchMetadata"/> objects detailing the differences.</returns>
		private List<PatchMetadata> GenerateMetadata(byte[] oldData, byte[] newData, long chunkBaseOffset)
		{
			List<PatchMetadata> patches = new();
			int oldPtr = 0; // Pointer for oldData.
			int newPtr = 0; // Pointer for newData.
			int oldLen = oldData.Length;
			int newLen = newData.Length;

			while (oldPtr < oldLen || newPtr < newLen)
			{
				// Skip common prefix: Advance pointers as long as bytes match.
				while (oldPtr < oldLen && newPtr < newLen && oldData[oldPtr] == newData[newPtr])
				{
					oldPtr++;
					newPtr++;
				}

				if (oldPtr == oldLen && newPtr == newLen)
					break; // Both pointers reached the end, no more differences in this chunk.

				int diffStartOld = oldPtr; // Mark the start of the difference in oldData.
				int diffStartNew = newPtr; // Mark the start of the difference in newData.

				// Find common suffix: Pointers start from the end of their respective chunks and move inwards.
				int suffixOldIdx = oldLen - 1;
				int suffixNewIdx = newLen - 1;
				int commonSuffixLength = 0;

				while (suffixOldIdx >= oldPtr && suffixNewIdx >= newPtr && oldData[suffixOldIdx] == newData[suffixNewIdx])
				{
					commonSuffixLength++;
					suffixOldIdx--;
					suffixNewIdx--;
				}

				// Calculate the length of the actual differing segments.
				// lenOld: Length of bytes to remove/replace from oldData.
				// lenNew: Length of bytes to insert from newData.
				int lenOld = (suffixOldIdx - diffStartOld) + 1;
				int lenNew = (suffixNewIdx - diffStartNew) + 1;

				// Ensure lengths are not negative (can occur if a match spans an entire segment, e.g., pure insertion/deletion).
				lenOld = Math.Max(0, lenOld);
				lenNew = Math.Max(0, lenNew);

				// Extract the new bytes for the patch.
				byte[] newBytes = new byte[lenNew];
				if (lenNew > 0)
					Array.Copy(newData, diffStartNew, newBytes, 0, lenNew);

				// Add the generated patch metadata to the list.
				patches.Add(new PatchMetadata
				{
					Offset = chunkBaseOffset + diffStartOld, // Absolute offset in the original file.
					Length = lenOld, // Number of bytes to effectively remove/replace from the old file.
					NewBytes = newBytes // The actual new bytes to insert.
				});

				// Advance pointers past the detected difference and its common suffix.
				oldPtr = diffStartOld + lenOld + commonSuffixLength;
				newPtr = diffStartNew + lenNew + commonSuffixLength;
			}
			return patches;
		}

		/// <summary>
		/// Writes a <see cref="PatchMetadata"/> object to a <see cref="BinaryWriter"/>.
		/// The format is: Offset (long), Length (int), NewBytes.Length (int), NewBytes (byte[]).
		/// </summary>
		/// <param name="writer">The BinaryWriter to write to.</param>
		/// <param name="metadata">The PatchMetadata object to write.</param>
		/// <exception cref="InvalidOperationException">Thrown if PatchMetadata.NewBytes is null.</exception>
		private void WritePatchMetadata(BinaryWriter writer, PatchMetadata metadata)
		{
			if (metadata.NewBytes == null)
				throw new InvalidOperationException("PatchMetadata.NewBytes cannot be null.");

			writer.Write(metadata.Offset);
			writer.Write(metadata.Length);
			writer.Write(metadata.NewBytes.Length);
			writer.Write(metadata.NewBytes);
		}
	}
}