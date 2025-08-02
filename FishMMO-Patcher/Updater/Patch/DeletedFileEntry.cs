using System.Text.Json.Serialization;

namespace FishMMO.Patcher
{
	// Represents a file that needs to be deleted
	public class DeletedFileEntry
	{
		[JsonPropertyName("path")]
		public string RelativePath { get; set; }
	}
}