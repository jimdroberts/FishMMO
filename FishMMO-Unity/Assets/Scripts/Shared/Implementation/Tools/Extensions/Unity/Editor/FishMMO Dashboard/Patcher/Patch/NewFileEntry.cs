using System.Text.Json.Serialization;

namespace FishMMO.Shared.Patcher
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

		/// <summary>
		/// POSIX permission bits the file must carry once written, or null when the build host
		/// could not report them (Windows, or a probe failure).
		/// Serialized as 'unix_mode' in JSON, and omitted entirely when null.
		/// </summary>
		/// <remarks>
		/// The updater creates new files with <c>File.Create</c>, which produces 0644 — so a
		/// natively-executable file shipped as an addition arrives unrunnable. When this is
		/// absent the updater falls back to sniffing the file's first bytes for an ELF/Mach-O
		/// or shebang signature, which covers the cases that matter but is a heuristic; this
		/// field is the exact answer where one is available.
		/// </remarks>
		[JsonPropertyName("unix_mode")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? UnixMode { get; set; }
	}
}