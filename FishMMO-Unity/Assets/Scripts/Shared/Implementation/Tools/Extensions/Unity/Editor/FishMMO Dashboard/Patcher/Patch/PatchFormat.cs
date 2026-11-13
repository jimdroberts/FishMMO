namespace FishMMO.Shared.Patcher
{
	/// <summary>
	/// Names and versions of the patch archive format. Kept identical to the Updater's copy
	/// (FishMMO-Patcher/Updater/Patch/PatchFormat.cs).
	/// </summary>
	public static class PatchFormat
	{
		/// <summary>
		/// The format-2 manifest's name inside the archive. A new name rather than a field in
		/// manifest.json, so an Updater that predates format 2 refuses the archive before touching
		/// anything instead of applying only the entries it understands.
		/// </summary>
		public const string ManifestV2EntryName = "manifest-v2.json";

		/// <summary>The format the generator writes.</summary>
		public const int CurrentVersion = 2;

		/// <summary>Patch data is a zstd frame (with the old file as reference prefix, for modified files).</summary>
		public const string EncodingZstd = "zstd";
	}
}
