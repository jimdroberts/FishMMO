using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FishMMO.Shared.Patcher
{
	/// <summary>
	/// Represents the manifest for a patch file, including version info and lists of file changes.
	/// Used for serialization and patch processing.
	/// </summary>
	public class PatchManifest
	{
		/// <summary>
		/// The version of the application or data before the patch is applied.
		/// Serialized as 'old_version' in JSON.
		/// </summary>
		[JsonPropertyName("old_version")]
		public string OldVersion { get; set; }

		/// <summary>
		/// The version of the application or data after the patch is applied.
		/// Serialized as 'new_version' in JSON.
		/// </summary>
		[JsonPropertyName("new_version")]
		public string NewVersion { get; set; }

		/// <summary>
		/// List of files to be deleted by the patch.
		/// Serialized as 'deleted_files' in JSON.
		/// </summary>
		[JsonPropertyName("deleted_files")]
		public List<DeletedFileEntry> DeletedFiles { get; set; } = new List<DeletedFileEntry>();

		/// <summary>
		/// List of files to be modified by the patch.
		/// Serialized as 'modified_files' in JSON.
		/// </summary>
		[JsonPropertyName("modified_files")]
		public List<ModifiedFileEntry> ModifiedFiles { get; set; } = new List<ModifiedFileEntry>();

		/// <summary>
		/// List of new files to be added by the patch.
		/// Serialized as 'new_files' in JSON.
		/// </summary>
		[JsonPropertyName("new_files")]
		public List<NewFileEntry> NewFiles { get; set; } = new List<NewFileEntry>();
	}
}