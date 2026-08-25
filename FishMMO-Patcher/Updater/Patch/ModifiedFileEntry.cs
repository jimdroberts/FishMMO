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

		// The expected final size of the file after the patch is applied.
		// This is crucial for the Patcher to correctly truncate or extend the file.
		[JsonPropertyName("final_file_size")]
		public long FinalFileSize { get; set; }

		/// <summary>
		/// POSIX permission bits the patched file should carry, or null to keep whatever the
		/// file being replaced already had.
		/// </summary>
		/// <remarks>
		/// Keeping the existing bits is the right default and the one the updater uses when
		/// this is absent: the patched result is written to a fresh temp file at 0644 and then
		/// moved over the target, so without carrying the mode across, every patch that touches
		/// the Linux client executable leaves the game unable to start.
		/// </remarks>
		[JsonPropertyName("unix_mode")]
		public int? UnixMode { get; set; }
	}
}