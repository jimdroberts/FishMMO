using System;
using System.Collections.Generic;
using System.IO;

namespace FishMMO.Patcher
{
	public class PatchGenerator
	{
		private const int ChunkSize = 65535; // 65KB chunk size

		public void Generate(BinaryWriter writer, string oldFilePath, string newFilePath, Action<bool> onComplete)
		{
			try
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
							WritePatchMetadata(writer, patchMetadata);
						}
						else if (newChunk != null)
						{
							var patchMetadata = new PatchMetadata()
							{
								Offset = i * ChunkSize,
								Length = newChunk.Length,
								NewBytes = newChunk,
							};
							WritePatchMetadata(writer, patchMetadata);
						}
						else
						{
							throw new Exception("Failed to read chunk data.");
						}
					}
				}

				onComplete?.Invoke(true);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error generating meta data: {ex.Message}");
				onComplete?.Invoke(false);
			}
		}

		private byte[] ReadChunk(FileStream fileStream, int chunkIndex)
		{
			byte[] buffer = new byte[ChunkSize];
			long offset = chunkIndex * ChunkSize;
			fileStream.Seek(offset, SeekOrigin.Begin);
			int bytesRead = fileStream.Read(buffer, 0, ChunkSize);
			if (bytesRead > 0)
			{
				Array.Resize(ref buffer, bytesRead); // Trim buffer to actual size read
				return buffer;
			}
			return null;
		}

		private PatchMetadata GenerateMetadata(byte[] oldData, byte[] newData, long offset)
		{
			int start = 0;
			int oldLength = oldData.Length;
			int newLength = newData.Length;
			int minLength = Math.Min(oldLength, newLength);

			while (start < minLength && oldData[start] == newData[start])
			{
				start++;
			}

			int endOld = oldLength - 1;
			int endNew = newLength - 1;
			while (endOld >= start && endNew >= start && oldData[endOld] == newData[endNew])
			{
				endOld--;
				endNew--;
			}

			int length = Math.Max(endOld - start + 1, endNew - start + 1);
			byte[] newBytes = new byte[length];

			Buffer.BlockCopy(newData, start, newBytes, 0, length);

			return new PatchMetadata
			{
				Offset = offset + start,
				Length = newBytes.Length,
				NewBytes = newBytes,
			};
		}

		private void WritePatchMetadata(BinaryWriter writer, PatchMetadata patchMetadata)
		{
			writer.Write(patchMetadata.Offset);
			writer.Write(patchMetadata.Length);
			writer.Write(patchMetadata.NewBytes.Length);
			writer.Write(patchMetadata.NewBytes);
		}
	}
}