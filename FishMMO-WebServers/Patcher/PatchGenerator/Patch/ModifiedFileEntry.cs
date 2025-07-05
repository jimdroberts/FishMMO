using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FishMMO.Patcher
{
	// Represents a file that has been modified
	public class ModifiedFileEntry
	{
		[JsonPropertyName("path")]
		public string RelativePath { get; set; }

		[JsonPropertyName("old_hash")]
		public string OldHash { get; set; }

		[JsonPropertyName("new_hash")]
		public string NewHash { get; set; }

		// Name of the entry within the ZIP archive that contains this file's patch data
		[JsonPropertyName("patch_data_entry_name")]
		public string PatchDataEntryName { get; set; }

		// Temporary file path on disk where the patch data is stored before zipping
		[JsonIgnore] // This property should not be serialized into the manifest JSON
		public string TempPatchFilePath { get; set; }
	}
}