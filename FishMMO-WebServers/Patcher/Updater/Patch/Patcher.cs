using System;
using System.Collections.Generic;
using System.IO;

namespace FishMMO.Patcher
{
	public class Patcher
	{
		public void Apply(BinaryReader reader, int diffCount, string oldFilePath, Action<bool> onComplete)
		{
			try
			{
				// Make a backup of the old file
				string backupFilePath = oldFilePath + ".bak";
				File.Copy(oldFilePath, backupFilePath, true);

				using (FileStream oldFile = new FileStream(oldFilePath, FileMode.Open, FileAccess.ReadWrite))
				{
					var patchMetadataList = ReadAllPatchMetadata(reader, diffCount);

					foreach (var patchMetadata in patchMetadataList)
					{
						oldFile.Seek(patchMetadata.Offset, SeekOrigin.Begin);
						oldFile.Write(patchMetadata.NewBytes, 0, patchMetadata.Length);
					}
				}

				// Remove the backup if everything was successful
				File.Delete(backupFilePath);

				onComplete?.Invoke(true);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error applying patch: {ex.Message}");
				// Restore from backup in case of error
				if (File.Exists(oldFilePath + ".bak"))
				{
					File.Copy(oldFilePath + ".bak", oldFilePath, true);
					File.Delete(oldFilePath + ".bak");
				}
				onComplete?.Invoke(false);
			}
		}

		private List<PatchMetadata> ReadAllPatchMetadata(BinaryReader reader, int diffCount)
		{
			var patchMetadataList = new List<PatchMetadata>();
			for (int i = 0; i < diffCount; i++)
			{
				var patchMetadata = new PatchMetadata
				{
					Offset = reader.ReadInt64(),
					Length = reader.ReadInt32(),
					NewBytes = reader.ReadBytes(reader.ReadInt32())
				};
				patchMetadataList.Add(patchMetadata);
			}
			return patchMetadataList;
		}
	}
}