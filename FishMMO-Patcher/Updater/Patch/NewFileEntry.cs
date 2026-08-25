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

		/// <summary>
		/// POSIX permission bits the file should carry, or null when the generator could not
		/// record them (a Windows build host, or an older archive).
		/// </summary>
		/// <remarks>
		/// A file written by <c>File.Create</c> lands at 0644, so a newly shipped native
		/// binary or script arrives without its execute bit and cannot be run. When this is
		/// absent the updater falls back to inspecting the file's first bytes; see
		/// <c>Program.ApplyModeForNewFile</c>. Only the low nine bits are honoured — setuid,
		/// setgid and sticky are stripped, because an archive is not allowed to ask for them.
		/// </remarks>
		[JsonPropertyName("unix_mode")]
		public int? UnixMode { get; set; }
	}
}