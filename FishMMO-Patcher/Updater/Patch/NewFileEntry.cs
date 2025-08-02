using System.Text.Json.Serialization;

namespace FishMMO.Patcher
{
	// Represents a new file that needs to be added
	public class NewFileEntry
	{
		[JsonPropertyName("path")]
		public string RelativePath { get; set; }

		[JsonPropertyName("new_hash")]
		public string NewHash { get; set; }

		// Name of the entry within the ZIP archive that contains this file's full data
		[JsonPropertyName("file_data_entry_name")]
		public string FileDataEntryName { get; set; }
	}
}