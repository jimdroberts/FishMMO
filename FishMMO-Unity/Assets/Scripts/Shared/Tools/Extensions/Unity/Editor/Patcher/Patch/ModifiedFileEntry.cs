using System.Text.Json.Serialization;

namespace FishMMO.Shared.Patcher
{
	/// <summary>
	/// Represents an entry for a file that has been modified and requires patching.
	/// Contains metadata for patching, validation, and serialization.
	/// </summary>
	public class ModifiedFileEntry
	{
		/// <summary>
		/// The relative path to the file that has been modified, as specified in the patch manifest.
		/// Serialized as 'path' in JSON.
		/// </summary>
		[JsonPropertyName("path")]
		public string RelativePath { get; set; }

		/// <summary>
		/// The hash of the file before the patch is applied. Used for validation.
		/// Serialized as 'old_hash' in JSON.
		/// </summary>
		[JsonPropertyName("old_hash")]
		public string OldHash { get; set; }

		/// <summary>
		/// The hash of the file after the patch is applied. Used for validation.
		/// Serialized as 'new_hash' in JSON.
		/// </summary>
		[JsonPropertyName("new_hash")]
		public string NewHash { get; set; }

		/// <summary>
		/// Name of the entry within the ZIP archive that contains this file's patch data.
		/// Serialized as 'patch_data_entry_name' in JSON.
		/// </summary>
		[JsonPropertyName("patch_data_entry_name")]
		public string PatchDataEntryName { get; set; }

		/// <summary>
		/// Temporary file path on disk where the patch data is stored before zipping.
		/// Not serialized into the manifest JSON.
		/// </summary>
		[JsonIgnore]
		public string TempPatchFilePath { get; set; }

		/// <summary>
		/// The expected final size of the file after the patch is applied.
		/// Crucial for the patcher to correctly truncate or extend the file.
		/// Serialized as 'final_file_size' in JSON.
		/// </summary>
		[JsonPropertyName("final_file_size")]
		public long FinalFileSize { get; set; }
	}
}