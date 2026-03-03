using System.Text.Json.Serialization;

namespace FishMMO.Shared.Patcher
{
	/// <summary>
	/// Represents an entry for a file that should be deleted during a patch operation.
	/// </summary>
	public class DeletedFileEntry
	{
		/// <summary>
		/// The relative path to the file to be deleted, as specified in the patch manifest.
		/// Serialized as 'path' in JSON.
		/// </summary>
		[JsonPropertyName("path")]
		public string RelativePath { get; set; }
	}
}