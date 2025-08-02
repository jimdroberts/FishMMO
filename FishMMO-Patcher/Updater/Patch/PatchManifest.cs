using System.Text.Json.Serialization;

namespace FishMMO.Patcher
{
	// Represents the entire manifest for a patch file
	public class PatchManifest
	{
		[JsonPropertyName("old_version")]
		public string OldVersion { get; set; }

		[JsonPropertyName("new_version")]
		public string NewVersion { get; set; }

		[JsonPropertyName("deleted_files")]
		public List<DeletedFileEntry> DeletedFiles { get; set; } = new List<DeletedFileEntry>();

		[JsonPropertyName("modified_files")]
		public List<ModifiedFileEntry> ModifiedFiles { get; set; } = new List<ModifiedFileEntry>();

		[JsonPropertyName("new_files")]
		public List<NewFileEntry> NewFiles { get; set; } = new List<NewFileEntry>();
	}
}