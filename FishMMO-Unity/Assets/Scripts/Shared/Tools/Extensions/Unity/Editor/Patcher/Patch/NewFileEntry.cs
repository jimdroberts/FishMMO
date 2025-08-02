using System.Text.Json.Serialization;

namespace FishMMO.Patcher
{
	/// <summary>
	/// Represents an entry for a new file that needs to be added during a patch operation.
	/// Contains metadata for file addition and serialization.
	/// </summary>
	public class NewFileEntry
	{
		/// <summary>
		/// The relative path to the new file to be added, as specified in the patch manifest.
		/// Serialized as 'path' in JSON.
		/// </summary>
		[JsonPropertyName("path")]
		public string RelativePath { get; set; }

		/// <summary>
		/// The hash of the new file after addition. Used for validation.
		/// Serialized as 'new_hash' in JSON.
		/// </summary>
		[JsonPropertyName("new_hash")]
		public string NewHash { get; set; }

		/// <summary>
		/// Name of the entry within the ZIP archive that contains this file's full data.
		/// Serialized as 'file_data_entry_name' in JSON.
		/// </summary>
		[JsonPropertyName("file_data_entry_name")]
		public string FileDataEntryName { get; set; }
	}
}