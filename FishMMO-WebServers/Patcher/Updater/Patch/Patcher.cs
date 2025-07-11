using System;
using System.Collections.Generic;
using System.IO;

namespace FishMMO.Patcher
{
	public class Patcher
	{
		/// <summary>
		/// Applies a patch to an old file. Reads patch metadata and applies changes chunk by chunk to avoid large memory allocations.
		/// This method reads patch data from the provided BinaryReader until the end of its underlying stream.
		/// </summary>
		/// <param name="reader">BinaryReader positioned at the start of the patch data for this file.
		/// It is expected that the reader will be fully consumed by this method.</param>
		/// <param name="oldFilePath">The full path to the old file to be patched.</param>
		/// <param name="onComplete">Action to invoke when patching is complete (true for success, false for failure).</param>
		public void Apply(BinaryReader reader, string oldFilePath, Action<bool> onComplete) // Removed diffCount parameter
		{
			// Ensure the old file exists before proceeding
			if (!File.Exists(oldFilePath))
			{
				Console.WriteLine($"Error: Old file not found at '{oldFilePath}'. Cannot apply patch.");
				onComplete?.Invoke(false);
				return;
			}

			string backupFilePath = oldFilePath + ".bak";

			try
			{
				// Make a backup of the old file for roll-back in case of failure
				File.Copy(oldFilePath, backupFilePath, true);
				Console.WriteLine($"Created backup of '{oldFilePath}' at '{backupFilePath}'.");

				// Open the old file for reading and writing directly
				using (FileStream oldFileStream = new FileStream(oldFilePath, FileMode.Open, FileAccess.ReadWrite))
				{
					// Process each patch metadata entry one by one until the end of the stream
					// This is the core change for memory efficiency: process one patch at a time.
					int patchChunkCount = 0;
					while (reader.BaseStream.Position < reader.BaseStream.Length)
					{
						PatchMetadata patchMetadata = ReadSinglePatchMetadata(reader);

						// Apply the patch data directly to the file stream
						oldFileStream.Seek(patchMetadata.Offset, SeekOrigin.Begin);
						oldFileStream.Write(patchMetadata.NewBytes, 0, patchMetadata.Length);
						patchChunkCount++;
						Console.WriteLine($"Applied patch chunk {patchChunkCount} at offset {patchMetadata.Offset} with length {patchMetadata.Length}.");
					}
				} // FileStream is automatically closed and changes are written to disk

				// Remove the backup if everything was successful
				File.Delete(backupFilePath);
				Console.WriteLine($"Patch applied successfully to '{oldFilePath}'. Backup file '{backupFilePath}' removed.");

				onComplete?.Invoke(true);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error applying patch to '{oldFilePath}': {ex.Message}");
				// Restore from backup in case of error
				if (File.Exists(backupFilePath))
				{
					try
					{
						File.Copy(backupFilePath, oldFilePath, true);
						File.Delete(backupFilePath);
						Console.WriteLine($"Restored '{oldFilePath}' from backup due to error. Backup file removed.");
					}
					catch (Exception restoreEx)
					{
						Console.WriteLine($"Critical Error: Failed to restore '{oldFilePath}' from backup: {restoreEx.Message}");
					}
				}
				onComplete?.Invoke(false);
			}
		}

		/// <summary>
		/// Reads a single PatchMetadata entry from the BinaryReader.
		/// </summary>
		private PatchMetadata ReadSinglePatchMetadata(BinaryReader reader)
		{
			// Read Offset (long)
			long offset = reader.ReadInt64();

			// Read Length (int)
			int length = reader.ReadInt32();

			// Read NewBytes (byte array of 'length' size)
			byte[] newBytes = reader.ReadBytes(length);

			return new PatchMetadata()
			{
				Offset = offset,
				Length = length,
				NewBytes = newBytes
			};
		}
	}
}