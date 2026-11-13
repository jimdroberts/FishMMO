namespace FishMMO.Patcher
{
	/// <summary>
	/// Names and versions of the patch archive format. Kept identical to the generator's copy
	/// (FishMMO-Unity, Patcher/Patch/PatchFormat.cs).
	/// </summary>
	public static class PatchFormat
	{
		/// <summary>
		/// The format-2 manifest's name inside the archive.
		/// </summary>
		/// <remarks>
		/// A different name, not a version field in <c>manifest.json</c>, on purpose: an Updater
		/// that predates format 2 looks only for <c>manifest.json</c>, so it refuses the archive
		/// before touching anything ("manifest.json not found") instead of applying the entries it
		/// understands, skipping the renames it does not, and reporting a version it is not running.
		/// </remarks>
		public const string ManifestV2EntryName = "manifest-v2.json";

		/// <summary>The original format's manifest name.</summary>
		public const string ManifestV1EntryName = "manifest.json";

		/// <summary>The format this Updater writes and reads.</summary>
		public const int CurrentVersion = 2;

		/// <summary>Patch data is a zstd frame (with the source file as reference prefix, for modified files).</summary>
		public const string EncodingZstd = "zstd";
	}
}
