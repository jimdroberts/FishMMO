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

		/// <summary>
		/// POSIX permission bits the patched file must carry, or null to keep whatever the
		/// file being replaced already had.
		/// Serialized as 'unix_mode' in JSON, and omitted entirely when null.
		/// </summary>
		/// <remarks>
		/// The updater preserves the replaced file's own bits when this is absent, which is
		/// the correct default; this field exists so a build that deliberately CHANGES a
		/// file's permissions can say so.
		/// </remarks>
		[JsonPropertyName("unix_mode")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? UnixMode { get; set; }

		/// <summary>
		/// The file in the old build the patch data was made against, when it is not
		/// <see cref="RelativePath"/>: a file the new build renamed (an Addressables bundle whose
		/// hashed name changed because a dependency's name inside it did). The old path is also
		/// listed in deleted_files. Null means the same path. Serialized as 'source_path'; format 2.
		/// </summary>
		[JsonPropertyName("source_path")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string SourcePath { get; set; }

		/// <summary>
		/// How the patch data is encoded: <see cref="PatchFormat.EncodingZstd"/> for a zstd frame
		/// that uses the source file as its reference prefix. Serialized as 'encoding'; format 2.
		/// </summary>
		[JsonPropertyName("encoding")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Encoding { get; set; }
	}
}